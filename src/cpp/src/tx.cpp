// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "array_view.hpp"
#include "tx.hpp"

#include "ascii_char_set.hpp"
#include "ast.hpp"
#include "rx.hpp"
#include "rx_converter.hpp"
#include "tree_walker.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace logmu::detail
{
	// eot

	eot eot::single(std::string text)
	{
		return text.size() > static_cast<std::size_t>(tree_walker::max_generated_length)
			? too_long()
			: eot{eot_kind::single, std::move(text)};
	}

	eot eot::concat(const eot& left, const eot& right)
	{
		if (left.kind == eot_kind::none || right.kind == eot_kind::none)
		{
			return none();
		}

		if (left.kind == eot_kind::too_long || right.kind == eot_kind::too_long)
		{
			return too_long();
		}

		if (left.kind == eot_kind::multiple || right.kind == eot_kind::multiple)
		{
			return multiple();
		}

		return single(left.text + right.text);
	}

	eot eot::union_(const eot& left, const eot& right)
	{
		if (left.kind == eot_kind::none)
		{
			return right;
		}

		if (right.kind == eot_kind::none)
		{
			return left;
		}

		if (left.kind == eot_kind::too_long || right.kind == eot_kind::too_long)
		{
			return too_long();
		}

		if (left.kind == eot_kind::multiple || right.kind == eot_kind::multiple)
		{
			return multiple();
		}

		return left.text == right.text ? left : multiple();
	}

	// tx

	const eot& tx::get_eot() const
	{
		if (!this->end_of_text.has_value())
		{
			this->end_of_text = this->compute_eot();
		}

		return *this->end_of_text;
	}

	const std::vector<ascii_char_set>& tx::get_first_sets() const
	{
		if (!this->first_sets_known)
		{
			this->collect_first_sets(this->first_sets);
			this->first_sets_known = true;
		}

		return this->first_sets;
	}

	eot tx::compute_eot() const
	{
		switch (this->kind)
		{
			case tx_kind::empty_set:
			case tx_kind::chars:
				return eot::none();

			case tx_kind::epsilon:
				return eot::empty();

			case tx_kind::repl:
				// Completing a unified element emits its rendering even though nothing was
				// consumed.
				return this->subject->is_nullable ? eot::single(this->rendering) : eot::none();

			case tx_kind::concat:
			{
				eot result = eot::empty();

				for (const tx* child : this->children)
				{
					result = eot::concat(result, child->get_eot());
				}

				return result;
			}

			case tx_kind::union_:
			{
				eot result = eot::none();

				for (const tx* child : this->children)
				{
					result = eot::union_(result, child->get_eot());
				}

				return result;
			}

			case tx_kind::interval:
			{
				const tx* child = this->children[0];

				// A count of zero denotes the empty string whatever the child would emit.
				if (!child->is_nullable)
				{
					return this->min_count == 0 ? eot::empty() : eot::none();
				}

				const eot& inner = child->get_eot();

				if (inner.kind != eot_kind::single)
				{
					return inner;
				}

				// Repeating something that emits nothing emits nothing however often it happens.
				if (inner.text.empty())
				{
					return eot::empty();
				}

				// Otherwise every count between the two bounds consumes nothing and emits a
				// different length, so a free count is more than one output. A fixed count is
				// one output, which is why '(A!!){2}' is well formed and '(A!!){0,2}' is not.
				if (this->min_count != this->max_count)
				{
					return eot::multiple();
				}

				if (static_cast<std::int64_t>(inner.text.size()) * this->min_count > tree_walker::max_generated_length)
				{
					return eot::too_long();
				}

				std::string repeated;
				repeated.reserve(inner.text.size() * static_cast<std::size_t>(this->min_count));

				for (int i = 0; i < this->min_count; ++i)
				{
					repeated += inner.text;
				}

				return eot::single(std::move(repeated));
			}
		}

		throw std::logic_error("Unhandled tx kind.");
	}

	void tx::collect_first_sets(std::vector<ascii_char_set>& sets) const
	{
		switch (this->kind)
		{
			case tx_kind::empty_set:
			case tx_kind::epsilon:
				return;

			case tx_kind::chars:
				sets.push_back(this->char_set);
				return;

			case tx_kind::repl:
			{
				const std::vector<ascii_char_set>& subject_sets = this->subject->get_first_sets();
				sets.insert(sets.end(), subject_sets.begin(), subject_sets.end());
				return;
			}

			case tx_kind::concat:
				for (const tx* child : this->children)
				{
					child->collect_first_sets(sets);

					if (!child->is_nullable)
					{
						return;
					}
				}

				return;

			case tx_kind::union_:
				for (const tx* child : this->children)
				{
					child->collect_first_sets(sets);
				}

				return;

			case tx_kind::interval:
				this->children[0]->collect_first_sets(sets);
				return;
		}

		throw std::logic_error("Unhandled tx kind.");
	}

	// tx_factory

	tx_factory::tx_factory(rx_factory& rx_factory)
		: rxs(rx_factory)
	{
		this->empty_set_node = this->intern(tx_kind::empty_set, ascii_char_set(), nullptr, {}, {}, 0, 0, false);
		this->epsilon_node = this->intern(tx_kind::epsilon, ascii_char_set(), nullptr, {}, {}, 0, 0, true);
	}

	const tx* tx_factory::chars(ascii_char_set set)
	{
		return set.is_empty()
			? this->empty_set_node
			: this->intern(tx_kind::chars, set, nullptr, {}, {}, 0, 0, false);
	}

	const tx* tx_factory::repl(const rx* subject, std::string_view rendering)
	{
		if (subject->kind == rx_kind::empty_set)
		{
			return this->empty_set_node;
		}

		for (const char c : rendering)
		{
			if (std::find(this->rendering_chars.begin(), this->rendering_chars.end(), c) == this->rendering_chars.end())
			{
				this->rendering_chars.push_back(c);
			}
		}

		return this->intern(tx_kind::repl, ascii_char_set(), subject, std::string(rendering), {}, 0, 0, subject->is_nullable);
	}

	const tx* tx_factory::concat(array_view<const tx*> parts)
	{
		std::vector<const tx*> flattened;

		for (const tx* part : parts)
		{
			if (part->kind == tx_kind::empty_set)
			{
				return this->empty_set_node;
			}

			// An epsilon emits nothing, so dropping it changes neither input nor output.
			if (part->kind == tx_kind::epsilon)
			{
				continue;
			}

			if (part->kind == tx_kind::concat)
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

		for (const tx* part : flattened)
		{
			is_nullable = is_nullable && part->is_nullable;
		}

		return this->intern(tx_kind::concat, ascii_char_set(), nullptr, {}, std::move(flattened), 0, 0, is_nullable);
	}

	const tx* tx_factory::concat(const tx* first, const tx* second)
	{
		const tx* const parts[] = {first, second};

		return this->concat(parts);
	}

	const tx* tx_factory::union_(array_view<const tx*> alternatives)
	{
		std::vector<const tx*> flattened;

		for (const tx* alternative : alternatives)
		{
			if (alternative->kind == tx_kind::empty_set)
			{
				continue;
			}

			if (alternative->kind == tx_kind::union_)
			{
				flattened.insert(flattened.end(), alternative->children.begin(), alternative->children.end());
			}
			else
			{
				flattened.push_back(alternative);
			}
		}

		std::sort(flattened.begin(), flattened.end(), [](const tx* left, const tx* right) { return left->id < right->id; });

		std::vector<const tx*> distinct;
		distinct.reserve(flattened.size());

		for (const tx* alternative : flattened)
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

		for (const tx* alternative : distinct)
		{
			is_nullable = is_nullable || alternative->is_nullable;
		}

		return this->intern(tx_kind::union_, ascii_char_set(), nullptr, {}, std::move(distinct), 0, 0, is_nullable);
	}

	const tx* tx_factory::union_(const tx* first, const tx* second)
	{
		const tx* const alternatives[] = {first, second};

		return this->union_(alternatives);
	}

	const tx* tx_factory::interval(const tx* child, int min_count, int max_count)
	{
		if (max_count == 0)
		{
			return this->epsilon_node;
		}

		if (child->kind == tx_kind::empty_set)
		{
			return min_count == 0 ? this->epsilon_node : this->empty_set_node;
		}

		// Unlike rx, an epsilon child is not dropped here unless it emits nothing: a unified
		// with a nullable subject consumes nothing and still emits, and how often that happens
		// is what makes '(A!!){0,3}' ambiguous.
		if (child->kind == tx_kind::epsilon)
		{
			return this->epsilon_node;
		}

		// rx drives the minimum to zero where the child is nullable, because for input alone
		// x{2} and x{0,2} then accept the same language. That is not available here: the count
		// decides how many renderings are emitted, and '(A!!){2}' emits 'AA' where '(A!!){0,2}'
		// emits one of three strings.
		if (min_count == 1 && max_count == 1)
		{
			return child;
		}

		return this->intern(tx_kind::interval, ascii_char_set(), nullptr, {}, {child}, min_count, max_count, min_count == 0 || child->is_nullable);
	}

	const tx_derivative& tx_factory::derivative(const tx* expression, ascii_char_set block)
	{
		const derivative_key key{expression->id, block};

		if (const auto found = this->derivatives.find(key); found != this->derivatives.end())
		{
			return *found->second;
		}

		// Computed before it is stored, because computing it re-enters this map.
		auto result = std::make_unique<tx_derivative>(this->compute_derivative(expression, block));
		const tx_derivative& stored = *result;
		this->derivatives.emplace(key, std::move(result));

		return stored;
	}

	tx_derivative tx_factory::compute_derivative(const tx* expression, ascii_char_set block)
	{
		switch (expression->kind)
		{
			case tx_kind::empty_set:
			case tx_kind::epsilon:
				return tx_derivative{};

			case tx_kind::chars:
			{
				if (!block.intersects_with(expression->char_set))
				{
					return tx_derivative{};
				}

				// A block of one character is already concrete; a wider one is not, and what it
				// emits stays undecided until the checker narrows it.
				const std::optional<char> single = block.single_character();
				const std::string emitted(1, single.value_or(copy_marker));

				return tx_derivative{{tx_move{emitted, this->epsilon_node}}, false, false};
			}

			case tx_kind::repl:
			{
				const rx* residual = this->rxs.derivative(expression->subject, block);

				if (residual->kind == rx_kind::empty_set)
				{
					return tx_derivative{};
				}

				// Nothing is emitted while the subject is being consumed.
				return tx_derivative{{tx_move{std::string(), this->repl(residual, expression->rendering)}}, false, false};
			}

			case tx_kind::union_:
			{
				tx_derivative result;

				for (const tx* child : expression->children)
				{
					const tx_derivative& sub = this->derivative(child, block);
					result.moves.insert(result.moves.end(), sub.moves.begin(), sub.moves.end());
					result.skips_ambiguously = result.skips_ambiguously || sub.skips_ambiguously;
					result.too_long = result.too_long || sub.too_long;
				}

				return result;
			}

			case tx_kind::concat:
			{
				tx_derivative result;

				// What the elements skipped over so far emit. Skipping a nullable element means
				// choosing one of its end of text parses, and that choice can emit.
				eot skipped = eot::empty();

				for (std::size_t i = 0; i < expression->children.size(); ++i)
				{
					const tx_derivative& sub = this->derivative(expression->children[i], block);
					result.skips_ambiguously = result.skips_ambiguously || sub.skips_ambiguously;
					result.too_long = result.too_long || sub.too_long;

					if (!sub.moves.empty())
					{
						switch (skipped.kind)
						{
							case eot_kind::multiple:
								// Two ways of skipping emit differently and both continue, so
								// one input has two outputs. There is nothing left to decide.
								result.skips_ambiguously = true;
								break;

							case eot_kind::too_long:
								result.too_long = true;
								break;

							default:
								for (const tx_move& move : sub.moves)
								{
									std::vector<const tx*> rest;
									rest.reserve(expression->children.size() - i);
									rest.push_back(move.residual);
									rest.insert(rest.end(), expression->children.begin() + static_cast<std::ptrdiff_t>(i) + 1, expression->children.end());

									result.moves.push_back(tx_move{skipped.text + move.emitted, this->concat(rest)});
								}

								break;
						}
					}

					// Only an element that can consume nothing lets a later one take the
					// character.
					if (!expression->children[i]->is_nullable)
					{
						break;
					}

					skipped = eot::concat(skipped, expression->children[i]->get_eot());
				}

				return result;
			}

			case tx_kind::interval:
			{
				const tx* child = expression->children[0];
				const tx_derivative& sub = this->derivative(child, block);

				if (sub.moves.empty())
				{
					return tx_derivative{};
				}

				tx_derivative result;
				result.skips_ambiguously = sub.skips_ambiguously;
				result.too_long = sub.too_long;

				// Copies before the one that consumes may be skipped, and a skipped copy emits
				// what its child emits at end of text.
				const eot inner = child->is_nullable ? child->get_eot() : eot::none();
				int skips = 0;

				if (child->is_nullable && expression->max_count >= 2)
				{
					if (inner.kind == eot_kind::multiple)
					{
						// Two ways of skipping one copy emit differently and leave the same work
						// behind them, so the totals differ whatever follows.
						result.skips_ambiguously = true;
					}
					else if (inner.kind == eot_kind::too_long)
					{
						result.too_long = true;
					}
					else if (!inner.text.empty())
					{
						// Skipping emits, so each count is a separate parse and has to be
						// followed. What it leaves behind shrinks as more are skipped, and that
						// can pay the difference back: '(A!!){2}' emits 'AA' by either route.
						skips = expression->max_count - 1;
					}
				}

				// Where a skipped copy emits nothing the parses differ only in a residual that
				// the unskipped one already covers, so one move stands for all of them.
				if (skips > max_skipped_copies)
				{
					result.moves.clear();
					result.too_long = true;

					return result;
				}

				result.moves.reserve(sub.moves.size() * static_cast<std::size_t>(skips + 1));
				std::string emitted_by_skips;

				for (int skipped = 0; skipped <= skips; ++skipped)
				{
					const int used = skipped + 1;
					const tx* rest = this->interval(
						child,
						expression->min_count <= used ? 0 : expression->min_count - used,
						expression->max_count - used);

					for (const tx_move& move : sub.moves)
					{
						result.moves.push_back(tx_move{emitted_by_skips + move.emitted, this->concat(move.residual, rest)});
					}

					if (inner.kind == eot_kind::single)
					{
						emitted_by_skips += inner.text;
					}
				}

				return result;
			}
		}

		throw std::logic_error("Unhandled tx kind.");
	}

	const tx* tx_factory::intern(tx_kind kind, ascii_char_set char_set, const rx* subject, std::string rendering, std::vector<const tx*> children, int min_count, int max_count, bool is_nullable)
	{
		tx_key key{kind, char_set, subject, std::move(rendering), std::move(children), min_count, max_count};

		if (const auto found = this->interned.find(key); found != this->interned.end())
		{
			return found->second;
		}

		auto created = std::make_unique<tx>(this->next_id++, kind, char_set, subject, key.rendering, key.children, min_count, max_count, is_nullable);
		const tx* result = created.get();

		this->nodes.push_back(std::move(created));
		this->interned.emplace(std::move(key), result);

		return result;
	}

	std::size_t tx_factory::tx_key_hash::operator()(const tx_key& key) const noexcept
	{
		std::size_t accumulated = static_cast<std::size_t>(key.kind);
		accumulated = (accumulated * 31) + key.char_set.hash();
		accumulated = (accumulated * 31) + (key.subject == nullptr ? 0 : static_cast<std::size_t>(key.subject->id));
		accumulated = (accumulated * 31) + std::hash<std::string>()(key.rendering);
		accumulated = (accumulated * 31) + static_cast<std::size_t>(key.min_count);
		accumulated = (accumulated * 31) + static_cast<std::size_t>(key.max_count);

		for (const tx* child : key.children)
		{
			accumulated = (accumulated * 31) + static_cast<std::size_t>(child->id);
		}

		return accumulated;
	}

	std::size_t tx_factory::derivative_key_hash::operator()(const derivative_key& key) const noexcept
	{
		return (static_cast<std::size_t>(key.expression_id) * 31) ^ key.block.hash();
	}

	// tx_converter

	namespace tx_converter
	{
		namespace
		{
			/// Reads an expression with no unified elements as a transduction, which copies.
			const tx* lift(const rx* expression, tx_factory& factory)
			{
				switch (expression->kind)
				{
					case rx_kind::empty_set:
						return factory.empty_set();

					case rx_kind::epsilon:
						return factory.epsilon();

					case rx_kind::chars:
						return factory.chars(expression->char_set);

					case rx_kind::concat:
					{
						std::vector<const tx*> parts;
						parts.reserve(expression->children.size());

						for (const rx* child : expression->children)
						{
							parts.push_back(lift(child, factory));
						}

						return factory.concat(parts);
					}

					case rx_kind::union_:
					{
						std::vector<const tx*> alternatives;
						alternatives.reserve(expression->children.size());

						for (const rx* child : expression->children)
						{
							alternatives.push_back(lift(child, factory));
						}

						return factory.union_(alternatives);
					}

					case rx_kind::interval:
						return factory.interval(lift(expression->children[0], factory), expression->min_count, expression->max_count);
				}

				throw std::logic_error("Unhandled rx kind.");
			}
		}

		const tx* convert(const ast& node, tx_factory& factory, rx_factory& rxs)
		{
			switch (node.kind())
			{
				case ast_kind::empty:
					return factory.epsilon();

				case ast_kind::chars:
					return factory.chars(as<ast_chars>(node).char_set);

				case ast_kind::decimal_range:
					// A decimal range emits what it consumed, so its expansion needs no output
					// of its own and the one the rx converter already knows how to build can be
					// lifted.
					return lift(rx_converter::convert(node, rxs, false), factory);

				case ast_kind::sequence:
				{
					const ast_sequence& sequence = as<ast_sequence>(node);
					std::vector<const tx*> parts;
					parts.reserve(sequence.children.size());

					for (const auto& child : sequence.children)
					{
						parts.push_back(convert(*child, factory, rxs));
					}

					return factory.concat(parts);
				}

				case ast_kind::alternation:
				{
					const ast_alternation& alternation = as<ast_alternation>(node);
					std::vector<const tx*> alternatives;
					alternatives.reserve(alternation.children.size());

					for (const auto& child : alternation.children)
					{
						alternatives.push_back(convert(*child, factory, rxs));
					}

					return factory.union_(alternatives);
				}

				case ast_kind::optional:
					return factory.union_(factory.epsilon(), convert(*as<ast_optional>(node).child, factory, rxs));

				case ast_kind::interval:
				{
					const ast_interval& interval = as<ast_interval>(node);

					return factory.interval(convert(*interval.child, factory, rxs), interval.min_count, interval.max_count);
				}

				case ast_kind::unified:
				{
					const ast_unified& unified = as<ast_unified>(node);
					std::string rendering;

					// W1 has already established that the rendering generates exactly one string.
					if (tree_walker::try_get_single_string(*unified.rendering, rendering) != tree_walker::single_string_outcome::single)
					{
						throw std::logic_error("A unified element passed W1 but has no single rendering.");
					}

					return factory.repl(rx_converter::convert(*unified.subject, rxs, false), rendering);
				}
			}

			throw std::logic_error("Unhandled node kind.");
		}
	}
}
