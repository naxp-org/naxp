// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "tree_walker.hpp"

#include "ast.hpp"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>

namespace logmu::detail::tree_walker
{
	namespace
	{
		single_string_outcome within(const std::string& text) noexcept
		{
			return text.size() <= static_cast<std::size_t>(max_generated_length)
				? single_string_outcome::single
				: single_string_outcome::too_long;
		}

		/// A value written to a width, with leading zeros.
		std::string padded(std::uint64_t value, int width)
		{
			std::string digits = std::to_string(value);

			return digits.size() < static_cast<std::size_t>(width)
				? std::string(static_cast<std::size_t>(width) - digits.size(), '0') + digits
				: digits;
		}

		single_string_outcome append_single_string(const ast& node, std::string& builder)
		{
			switch (node.kind())
			{
				case ast_kind::empty:
					return single_string_outcome::single;

				case ast_kind::chars:
				{
					const std::optional<char> single = as<ast_chars>(node).char_set.single_character();

					if (!single.has_value())
					{
						return single_string_outcome::multiple;
					}

					builder.push_back(*single);

					return within(builder);
				}

				case ast_kind::decimal_range:
				{
					// One string only where the two bounds are the same number written to the
					// same width.
					const ast_decimal_range& range = as<ast_decimal_range>(node);

					if (range.low != range.high || range.low_digit_count != range.high_digit_count)
					{
						return single_string_outcome::multiple;
					}

					builder += padded(range.low, range.low_digit_count);

					return within(builder);
				}

				case ast_kind::sequence:
				{
					for (const auto& child : as<ast_sequence>(node).children)
					{
						const single_string_outcome outcome = append_single_string(*child, builder);

						if (outcome != single_string_outcome::single)
						{
							return outcome;
						}
					}

					return single_string_outcome::single;
				}

				case ast_kind::alternation:
				{
					// Every alternative must give the same one string, so 'A|A' generates one.
					const ast_alternation& alternation = as<ast_alternation>(node);
					std::string first;
					const single_string_outcome first_outcome = append_single_string(*alternation.children[0], first);

					if (first_outcome != single_string_outcome::single)
					{
						return first_outcome;
					}

					for (std::size_t i = 1; i < alternation.children.size(); ++i)
					{
						std::string other;
						const single_string_outcome other_outcome = append_single_string(*alternation.children[i], other);

						if (other_outcome != single_string_outcome::single)
						{
							return other_outcome;
						}

						if (first != other)
						{
							return single_string_outcome::multiple;
						}
					}

					builder += first;

					return within(builder);
				}

				case ast_kind::optional:
				{
					// x? always generates the empty string, so it is single valued only where x
					// does too.
					std::string inner;
					const single_string_outcome outcome = append_single_string(*as<ast_optional>(node).child, inner);

					if (outcome == single_string_outcome::too_long)
					{
						return outcome;
					}

					return outcome == single_string_outcome::single && inner.empty()
						? single_string_outcome::single
						: single_string_outcome::multiple;
				}

				case ast_kind::interval:
				{
					const ast_interval& interval = as<ast_interval>(node);

					// A zero count denotes the empty string whatever the child generates.
					if (interval.max_count == 0)
					{
						return single_string_outcome::single;
					}

					std::string inner;
					const single_string_outcome outcome = append_single_string(*interval.child, inner);

					if (outcome != single_string_outcome::single)
					{
						return outcome;
					}

					if (inner.empty())
					{
						return single_string_outcome::single;
					}

					if (interval.min_count != interval.max_count)
					{
						return single_string_outcome::multiple;
					}

					if (static_cast<std::uint64_t>(inner.size()) * static_cast<std::uint64_t>(interval.min_count) > static_cast<std::uint64_t>(max_generated_length))
					{
						return single_string_outcome::too_long;
					}

					for (int i = 0; i < interval.min_count; ++i)
					{
						builder += inner;
					}

					return within(builder);
				}

				case ast_kind::unified:
					// The strings x!y generates are the strings x accepts. W2 has already ruled
					// out any tree that reaches this case from within another '!'.
					return append_single_string(*as<ast_unified>(node).subject, builder);
			}

			throw std::logic_error("Unhandled node kind.");
		}

		/// Matches a decimal range without expanding it.
		///
		/// A string of `w` digits is generated when `w` lies between the two written widths,
		/// its value is at least the lower bound if `w` is the lower width, its value is at
		/// most the upper bound if `w` is the upper width, and it has no leading zero unless
		/// `w` is the lower width. That last clause is what makes `#[0-105]` expand to
		/// `[0-9] | [1-9][0-9] | 10[0-5]` rather than admitting `07`.
		position_set advance_decimal_range(const ast_decimal_range& range, std::string_view text, const position_set& starts)
		{
			position_set result(text.size());

			for (std::size_t p = 0; p < starts.extent(); ++p)
			{
				if (!starts.contains(p))
				{
					continue;
				}

				std::uint64_t value = 0;

				for (int width = 1; width <= range.high_digit_count; ++width)
				{
					const std::size_t index = p + static_cast<std::size_t>(width) - 1;

					if (index >= text.size())
					{
						break;
					}

					const char c = text[index];

					if (c < '0' || c > '9')
					{
						break;
					}

					value = (value * 10) + static_cast<std::uint64_t>(c - '0');

					if (width < range.low_digit_count)
					{
						continue;
					}

					if (width > range.low_digit_count && text[p] == '0')
					{
						continue;
					}

					if (width == range.low_digit_count && value < range.low)
					{
						continue;
					}

					if (width == range.high_digit_count && value > range.high)
					{
						continue;
					}

					result.add(p + static_cast<std::size_t>(width));
				}
			}

			return result;
		}
	}

	single_string_outcome try_get_single_string(const ast& node, std::string& result)
	{
		std::string builder;
		const single_string_outcome outcome = append_single_string(node, builder);

		result = outcome == single_string_outcome::single ? builder : std::string();

		return outcome;
	}

	bool generates(const ast& node, std::string_view text, bool& too_long)
	{
		too_long = false;

		if (text.size() > static_cast<std::size_t>(max_generated_length))
		{
			too_long = true;

			return false;
		}

		position_set starts(text.size());
		starts.add(0);

		return advance(node, text, starts).contains(text.size());
	}

	position_set advance(const ast& node, std::string_view text, const position_set& starts)
	{
		if (starts.is_empty())
		{
			return starts;
		}

		switch (node.kind())
		{
			case ast_kind::empty:
				return starts;

			case ast_kind::chars:
			{
				const ascii_char_set& set = as<ast_chars>(node).char_set;
				position_set result(text.size());

				for (std::size_t p = 0; p < text.size(); ++p)
				{
					if (starts.contains(p) && set.contains(text[p]))
					{
						result.add(p + 1);
					}
				}

				return result;
			}

			case ast_kind::decimal_range:
				return advance_decimal_range(as<ast_decimal_range>(node), text, starts);

			case ast_kind::sequence:
			{
				position_set current = starts;

				for (const auto& child : as<ast_sequence>(node).children)
				{
					current = advance(*child, text, current);

					if (current.is_empty())
					{
						break;
					}
				}

				return current;
			}

			case ast_kind::alternation:
			{
				position_set result(text.size());

				for (const auto& child : as<ast_alternation>(node).children)
				{
					result.add_all(advance(*child, text, starts));
				}

				return result;
			}

			case ast_kind::optional:
			{
				position_set result = starts;
				result.add_all(advance(*as<ast_optional>(node).child, text, starts));

				return result;
			}

			case ast_kind::interval:
			{
				const ast_interval& interval = as<ast_interval>(node);
				position_set result = interval.min_count == 0 ? starts : position_set(text.size());
				position_set current = starts;

				for (int i = 1; i <= interval.max_count; ++i)
				{
					position_set next = advance(*interval.child, text, current);

					if (next.is_empty())
					{
						break;
					}

					if (i >= interval.min_count)
					{
						result.add_all(next);
					}

					// A child that matches the empty string reaches a fixed point at once, and
					// the remaining repetitions add nothing. Without this the largest count
					// costs that many passes over the string for no gain.
					if (next == current && i >= interval.min_count)
					{
						break;
					}

					current = next;
				}

				return result;
			}

			case ast_kind::unified:
				// x!y accepts whatever x accepts.
				return advance(*as<ast_unified>(node).subject, text, starts);
		}

		throw std::logic_error("Unhandled node kind.");
	}
}
