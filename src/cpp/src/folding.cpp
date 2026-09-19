// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "folding.hpp"

#include "ascii_char_set.hpp"
#include "ast.hpp"

#include <memory>
#include <utility>
#include <vector>

namespace logmu::detail::folding
{
	namespace
	{
		bool is_cased(char c) noexcept
		{
			return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
		}

		char to_upper(char c) noexcept
		{
			return c >= 'a' && c <= 'z' ? static_cast<char>(c - 32) : c;
		}

		char to_lower(char c) noexcept
		{
			return c >= 'A' && c <= 'Z' ? static_cast<char>(c + 32) : c;
		}

		char canonical(char c, bool to_upper_case) noexcept
		{
			return to_upper_case ? to_upper(c) : to_lower(c);
		}

		char other_case(char c) noexcept
		{
			if (c >= 'A' && c <= 'Z')
			{
				return static_cast<char>(c + 32);
			}

			if (c >= 'a' && c <= 'z')
			{
				return static_cast<char>(c - 32);
			}

			return c;
		}

		/// Adds the other case of every cased character, leaving the rest alone.
		ascii_char_set widen_set(ascii_char_set set)
		{
			ascii_char_set widened = set;

			for (const char c : set)
			{
				if (is_cased(c))
				{
					widened = widened | ascii_char_set::single_character(other_case(c));
				}
			}

			return widened;
		}

		/// Replaces every cased character by its canonical case, which may shrink the set.
		ascii_char_set canonicalise_set(ascii_char_set set, bool to_upper_case)
		{
			ascii_char_set result;

			for (const char c : set)
			{
				result = result | ascii_char_set::single_character(canonical(c, to_upper_case));
			}

			return result;
		}

		/// Expands a fold over one character set, which is where a fold does its work.
		///
		/// The characters with no case stay in a set of their own and are matched as they were.
		/// Each letter present in either case becomes one unified element accepting the pair and
		/// rendering the canonical one, so `\C[\9A-F]` is `[\9]` beside six of them.
		ast_ptr apply_to_chars(const ast_ptr& node, bool to_upper_case)
		{
			const ast_chars& chars = as<ast_chars>(*node);
			ascii_char_set uncased;
			ascii_char_set canonical_letters;

			for (const char c : chars.char_set)
			{
				if (is_cased(c))
				{
					canonical_letters = canonical_letters | ascii_char_set::single_character(canonical(c, to_upper_case));
				}
				else
				{
					uncased = uncased | ascii_char_set::single_character(c);
				}
			}

			// A fold over characters with no case is not an error, it just has nothing to do.
			if (canonical_letters.is_empty())
			{
				return node;
			}

			const std::size_t offset = chars.pattern_offset;
			std::vector<ast_ptr> branches;

			if (!uncased.is_empty())
			{
				branches.push_back(std::make_shared<ast_chars>(uncased, offset));
			}

			for (const char c : canonical_letters)
			{
				const ascii_char_set pair = ascii_char_set::single_character(c) | ascii_char_set::single_character(other_case(c));

				branches.push_back(std::make_shared<ast_unified>(
					std::make_shared<ast_chars>(pair, offset),
					std::make_shared<ast_chars>(ascii_char_set::single_character(c), offset),
					unified_form::fold,
					offset));
			}

			return branches.size() == 1
				? branches[0]
				: std::make_shared<ast_alternation>(std::move(branches), offset);
		}

		template <typename Map>
		std::vector<ast_ptr> map_each(const std::vector<ast_ptr>& children, const Map& map);

		/// Rebuilds a subtree with every character set passed through `map`.
		///
		/// The `x!!` form shares one subtree between its subject and its rendering. Rebuilding
		/// rather than mutating is what allows the two to be mapped differently, which is exactly
		/// what folding one of them does.
		template <typename Map>
		ast_ptr map_char_sets(const ast_ptr& node, const Map& map)
		{
			switch (node->kind())
			{
				case ast_kind::chars:
				{
					const ast_chars& chars = as<ast_chars>(*node);

					return std::make_shared<ast_chars>(map(chars.char_set), chars.pattern_offset);
				}

				case ast_kind::sequence:
				{
					const ast_sequence& sequence = as<ast_sequence>(*node);

					return std::make_shared<ast_sequence>(map_each(sequence.children, map), sequence.pattern_offset);
				}

				case ast_kind::alternation:
				{
					const ast_alternation& alternation = as<ast_alternation>(*node);

					return std::make_shared<ast_alternation>(map_each(alternation.children, map), alternation.pattern_offset);
				}

				case ast_kind::optional:
				{
					const ast_optional& optional = as<ast_optional>(*node);

					return std::make_shared<ast_optional>(map_char_sets(optional.child, map), optional.pattern_offset);
				}

				case ast_kind::interval:
				{
					const ast_interval& interval = as<ast_interval>(*node);

					return std::make_shared<ast_interval>(
						map_char_sets(interval.child, map),
						interval.min_count,
						interval.max_count,
						interval.pattern_offset);
				}

				case ast_kind::unified:
				{
					// Nesting is W2's to refuse, and it reads a tree this has already been through.
					const ast_unified& unified = as<ast_unified>(*node);

					return std::make_shared<ast_unified>(
						map_char_sets(unified.subject, map),
						map_char_sets(unified.rendering, map),
						unified.form,
						unified.pattern_offset);
				}

				default:
					return node;
			}
		}

		template <typename Map>
		std::vector<ast_ptr> map_each(const std::vector<ast_ptr>& children, const Map& map)
		{
			std::vector<ast_ptr> mapped;
			mapped.reserve(children.size());

			for (const auto& child : children)
			{
				mapped.push_back(map_char_sets(child, map));
			}

			return mapped;
		}

		/// Folds a unified element, which widens its subject and canonicalises its rendering.
		///
		/// No `!` is added. Which of the strings the subject accepts was matched is unencoded
		/// already, so there is nothing there for a fold to unify; all that is wanted is that the
		/// subject accept both cases and that the rendering come out in the canonical one. W1
		/// then holds without being checked, the rendering being a case variant of a string the
		/// subject generated and the widened subject generating every case variant of what it
		/// generated before.
		ast_ptr apply_to_unified(const ast_ptr& node, bool to_upper_case)
		{
			const ast_unified& unified = as<ast_unified>(*node);

			// A fold already expanded within the extent is treated like any other unified
			// element: its subject is widened again, which changes nothing, and its rendering
			// takes the outer fold's case. The outer fold governs.
			return std::make_shared<ast_unified>(
				map_char_sets(unified.subject, widen_set),
				map_char_sets(unified.rendering, [to_upper_case](ascii_char_set set) { return canonicalise_set(set, to_upper_case); }),
				unified.form,
				unified.pattern_offset);
		}

		std::vector<ast_ptr> apply_to_each(const std::vector<ast_ptr>& children, bool to_upper_case)
		{
			std::vector<ast_ptr> folded;
			folded.reserve(children.size());

			for (const auto& child : children)
			{
				folded.push_back(apply(child, to_upper_case));
			}

			return folded;
		}
	}

	ast_ptr apply(const ast_ptr& node, bool to_upper)
	{
		switch (node->kind())
		{
			case ast_kind::chars:
				return apply_to_chars(node, to_upper);

			case ast_kind::sequence:
			{
				const ast_sequence& sequence = as<ast_sequence>(*node);

				return std::make_shared<ast_sequence>(apply_to_each(sequence.children, to_upper), sequence.pattern_offset);
			}

			case ast_kind::alternation:
			{
				const ast_alternation& alternation = as<ast_alternation>(*node);

				return std::make_shared<ast_alternation>(apply_to_each(alternation.children, to_upper), alternation.pattern_offset);
			}

			case ast_kind::optional:
			{
				const ast_optional& optional = as<ast_optional>(*node);

				return std::make_shared<ast_optional>(apply(optional.child, to_upper), optional.pattern_offset);
			}

			case ast_kind::interval:
			{
				const ast_interval& interval = as<ast_interval>(*node);

				return std::make_shared<ast_interval>(
					apply(interval.child, to_upper),
					interval.min_count,
					interval.max_count,
					interval.pattern_offset);
			}

			case ast_kind::unified:
				return apply_to_unified(node, to_upper);

			default:
				// The empty string and a decimal range have no character to fold.
				return node;
		}
	}
}
