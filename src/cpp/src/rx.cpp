// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "array_view.hpp"
#include "rx.hpp"

#include "ascii_char_set.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <memory>
#include <stdexcept>
#include <utility>
#include <vector>

namespace logmu::detail
{
	namespace
	{
		std::int64_t saturating_add(std::int64_t left, std::int64_t right) noexcept
		{
			return left > std::numeric_limits<std::int64_t>::max() - right
				? std::numeric_limits<std::int64_t>::max()
				: left + right;
		}

		std::int64_t saturating_multiply(std::int64_t left, std::int64_t right) noexcept
		{
			if (left == 0 || right == 0)
			{
				return 0;
			}

			return left > std::numeric_limits<std::int64_t>::max() / right
				? std::numeric_limits<std::int64_t>::max()
				: left * right;
		}
	}

	// rx

	const std::vector<ascii_char_set>& rx::get_first_sets() const
	{
		if (!this->first_sets_known)
		{
			this->collect_first_sets(this->first_sets);
			this->first_sets_known = true;
		}

		return this->first_sets;
	}

	void rx::collect_first_sets(std::vector<ascii_char_set>& sets) const
	{
		switch (this->kind)
		{
			case rx_kind::empty_set:
			case rx_kind::epsilon:
				return;

			case rx_kind::chars:
				sets.push_back(this->char_set);
				return;

			case rx_kind::concat:
				for (const rx* child : this->children)
				{
					child->collect_first_sets(sets);

					if (!child->is_nullable)
					{
						return;
					}
				}

				return;

			case rx_kind::union_:
				for (const rx* child : this->children)
				{
					child->collect_first_sets(sets);
				}

				return;

			case rx_kind::interval:
				this->children[0]->collect_first_sets(sets);
				return;
		}

		throw std::logic_error("Unhandled rx kind.");
	}

	// rx_factory

	rx_factory::rx_factory()
	{
		this->empty_set_node = this->intern(rx_kind::empty_set, ascii_char_set(), {}, 0, 0, false, 0);
		this->epsilon_node = this->intern(rx_kind::epsilon, ascii_char_set(), {}, 0, 0, true, 0);
	}

	const rx* rx_factory::chars(ascii_char_set set)
	{
		return set.is_empty()
			? this->empty_set_node
			: this->intern(rx_kind::chars, set, {}, 0, 0, false, 1);
	}

	const rx* rx_factory::concat(array_view<const rx*> parts)
	{
		std::vector<const rx*> flattened;

		for (const rx* part : parts)
		{
			if (part->kind == rx_kind::empty_set)
			{
				return this->empty_set_node;
			}

			if (part->kind == rx_kind::epsilon)
			{
				continue;
			}

			if (part->kind == rx_kind::concat)
			{
				flattened.insert(flattened.end(), part->children.begin(), part->children.end());
			}
			else
			{
				flattened.push_back(part);
			}
		}

		if (flattened.empty())
		{
			return this->epsilon_node;
		}

		if (flattened.size() == 1)
		{
			return flattened[0];
		}

		bool is_nullable = true;
		std::int64_t max_length = 0;

		for (const rx* part : flattened)
		{
			is_nullable = is_nullable && part->is_nullable;
			max_length = saturating_add(max_length, part->max_length);
		}

		return this->intern(rx_kind::concat, ascii_char_set(), std::move(flattened), 0, 0, is_nullable, max_length);
	}

	const rx* rx_factory::concat(const rx* first, const rx* second)
	{
		const rx* const parts[] = {first, second};

		return this->concat(parts);
	}

	const rx* rx_factory::union_(array_view<const rx*> alternatives)
	{
		std::vector<const rx*> flattened;

		for (const rx* alternative : alternatives)
		{
			if (alternative->kind == rx_kind::empty_set)
			{
				continue;
			}

			if (alternative->kind == rx_kind::union_)
			{
				flattened.insert(flattened.end(), alternative->children.begin(), alternative->children.end());
			}
			else
			{
				flattened.push_back(alternative);
			}
		}

		std::sort(flattened.begin(), flattened.end(), [](const rx* left, const rx* right) { return left->id < right->id; });

		std::vector<const rx*> distinct;
		distinct.reserve(flattened.size());

		for (const rx* alternative : flattened)
		{
			if (distinct.empty() || distinct.back() != alternative)
			{
				distinct.push_back(alternative);
			}
		}

		if (distinct.empty())
		{
			return this->empty_set_node;
		}

		if (distinct.size() == 1)
		{
			return distinct[0];
		}

		bool is_nullable = false;
		std::int64_t max_length = 0;

		for (const rx* alternative : distinct)
		{
			is_nullable = is_nullable || alternative->is_nullable;
			max_length = std::max(max_length, alternative->max_length);
		}

		return this->intern(rx_kind::union_, ascii_char_set(), std::move(distinct), 0, 0, is_nullable, max_length);
	}

	const rx* rx_factory::union_(const rx* first, const rx* second)
	{
		const rx* const alternatives[] = {first, second};

		return this->union_(alternatives);
	}

	const rx* rx_factory::interval(const rx* child, int min_count, int max_count)
	{
		if (max_count == 0)
		{
			return this->epsilon_node;
		}

		if (child->kind == rx_kind::epsilon)
		{
			return this->epsilon_node;
		}

		if (child->kind == rx_kind::empty_set)
		{
			return min_count == 0 ? this->epsilon_node : this->empty_set_node;
		}

		// Where the child accepts the empty string, so does every count above the minimum, and
		// x{m,n} and x{0,n} are the same language. Normalising here is what lets is_nullable be
		// read off the minimum alone.
		if (child->is_nullable)
		{
			min_count = 0;
		}

		if (min_count == 1 && max_count == 1)
		{
			return child;
		}

		const std::int64_t max_length = saturating_multiply(child->max_length, max_count);

		return this->intern(rx_kind::interval, ascii_char_set(), {child}, min_count, max_count, min_count == 0, max_length);
	}

	const rx* rx_factory::derivative(const rx* expression, ascii_char_set minterm)
	{
		const derivative_key key{expression->id, minterm};

		if (const auto found = this->derivatives.find(key); found != this->derivatives.end())
		{
			return found->second;
		}

		const rx* result = this->compute_derivative(expression, minterm);
		this->derivatives.emplace(key, result);

		return result;
	}

	const rx* rx_factory::compute_derivative(const rx* expression, ascii_char_set minterm)
	{
		switch (expression->kind)
		{
			case rx_kind::empty_set:
			case rx_kind::epsilon:
				return this->empty_set_node;

			case rx_kind::chars:
				// The minterm is wholly inside the set or wholly outside it.
				return minterm.intersects_with(expression->char_set) ? this->epsilon_node : this->empty_set_node;

			case rx_kind::concat:
			{
				std::vector<const rx*> alternatives;

				for (std::size_t i = 0; i < expression->children.size(); ++i)
				{
					const rx* head = this->derivative(expression->children[i], minterm);

					if (head->kind != rx_kind::empty_set)
					{
						std::vector<const rx*> parts;
						parts.reserve(expression->children.size() - i);
						parts.push_back(head);
						parts.insert(parts.end(), expression->children.begin() + static_cast<std::ptrdiff_t>(i) + 1, expression->children.end());

						alternatives.push_back(this->concat(parts));
					}

					// Only a part that can match nothing lets the character be consumed later on.
					if (!expression->children[i]->is_nullable)
					{
						break;
					}
				}

				return this->union_(alternatives);
			}

			case rx_kind::union_:
			{
				std::vector<const rx*> alternatives;
				alternatives.reserve(expression->children.size());

				for (const rx* child : expression->children)
				{
					alternatives.push_back(this->derivative(child, minterm));
				}

				return this->union_(alternatives);
			}

			case rx_kind::interval:
			{
				const rx* child = expression->children[0];
				const rx* head = this->derivative(child, minterm);

				if (head->kind == rx_kind::empty_set)
				{
					return this->empty_set_node;
				}

				const int min_count = expression->min_count == 0 ? 0 : expression->min_count - 1;

				return this->concat(head, this->interval(child, min_count, expression->max_count - 1));
			}
		}

		throw std::logic_error("Unhandled rx kind.");
	}

	const rx* rx_factory::intern(rx_kind kind, ascii_char_set char_set, std::vector<const rx*> children, int min_count, int max_count, bool is_nullable, std::int64_t max_length)
	{
		rx_key key{kind, char_set, std::move(children), min_count, max_count};

		if (const auto found = this->interned.find(key); found != this->interned.end())
		{
			return found->second;
		}

		auto created = std::make_unique<rx>(this->next_id++, kind, char_set, key.children, min_count, max_count, is_nullable, max_length);
		const rx* result = created.get();

		this->nodes.push_back(std::move(created));
		this->interned.emplace(std::move(key), result);

		return result;
	}

	std::size_t rx_factory::rx_key_hash::operator()(const rx_key& key) const noexcept
	{
		std::size_t accumulated = static_cast<std::size_t>(key.kind);
		accumulated = (accumulated * 31) + key.char_set.hash();
		accumulated = (accumulated * 31) + static_cast<std::size_t>(key.min_count);
		accumulated = (accumulated * 31) + static_cast<std::size_t>(key.max_count);

		for (const rx* child : key.children)
		{
			accumulated = (accumulated * 31) + static_cast<std::size_t>(child->id);
		}

		return accumulated;
	}

	std::size_t rx_factory::derivative_key_hash::operator()(const derivative_key& key) const noexcept
	{
		return (static_cast<std::size_t>(key.expression_id) * 31) ^ key.minterm.hash();
	}
}
