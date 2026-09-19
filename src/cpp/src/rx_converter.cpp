// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "rx_converter.hpp"

#include "ascii_char_set.hpp"
#include "ast.hpp"
#include "rx.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <stdexcept>
#include <vector>

namespace logmu::detail::rx_converter
{
	namespace
	{
		/// Powers of ten up to the fifteen digit cap on a decimal range bound.
		constexpr std::array<std::uint64_t, 16> powers_of_ten = []
		{
			std::array<std::uint64_t, 16> powers{};
			powers[0] = 1;

			for (std::size_t i = 1; i < powers.size(); ++i)
			{
				powers[i] = powers[i - 1] * 10;
			}

			return powers;
		}();

		std::uint64_t power_of_ten(int exponent) noexcept
		{
			return powers_of_ten[static_cast<std::size_t>(exponent)];
		}

		const rx* digit_chars(int low_digit, int high_digit, rx_factory& factory)
		{
			return factory.chars(ascii_char_set::character_range(static_cast<char>('0' + low_digit), static_cast<char>('0' + high_digit)));
		}

		/// The strings of exactly `width` digits whose value lies between `low` and `high`
		/// inclusive, leading zeros included.
		const rx* fixed_width_range(std::uint64_t low, std::uint64_t high, int width, rx_factory& factory)
		{
			if (width == 0)
			{
				return factory.epsilon();
			}

			// Every string of this width qualifies, so there is nothing to split on.
			if (low == 0 && high == power_of_ten(width) - 1)
			{
				return factory.interval(factory.chars(all_digits), width, width);
			}

			const std::uint64_t place = power_of_ten(width - 1);
			const int low_lead = static_cast<int>(low / place);
			const int high_lead = static_cast<int>(high / place);
			const std::uint64_t low_rest = low % place;
			const std::uint64_t high_rest = high % place;

			if (low_lead == high_lead)
			{
				return factory.concat(
					digit_chars(low_lead, low_lead, factory),
					fixed_width_range(low_rest, high_rest, width - 1, factory));
			}

			std::vector<const rx*> alternatives;
			alternatives.reserve(3);

			alternatives.push_back(factory.concat(
				digit_chars(low_lead, low_lead, factory),
				fixed_width_range(low_rest, place - 1, width - 1, factory)));

			if (high_lead - low_lead >= 2)
			{
				alternatives.push_back(factory.concat(
					digit_chars(low_lead + 1, high_lead - 1, factory),
					factory.interval(factory.chars(all_digits), width - 1, width - 1)));
			}

			alternatives.push_back(factory.concat(
				digit_chars(high_lead, high_lead, factory),
				fixed_width_range(0, high_rest, width - 1, factory)));

			return factory.union_(alternatives);
		}

		/// Expands a decimal range into an ordinary expression.
		///
		/// One alternative per width. The lower width admits the leading zeros the lower bound
		/// was written with; every width above it does not, which is what makes `#[0-105]`
		/// stand for `[0-9] | [1-9][0-9] | 10[0-5]` rather than admitting `07`.
		const rx* convert_decimal_range(const ast_decimal_range& range, rx_factory& factory)
		{
			std::vector<const rx*> widths;
			widths.reserve(static_cast<std::size_t>(range.high_digit_count - range.low_digit_count + 1));

			for (int width = range.low_digit_count; width <= range.high_digit_count; ++width)
			{
				const std::uint64_t low = width == range.low_digit_count ? range.low : power_of_ten(width - 1);
				const std::uint64_t high = width == range.high_digit_count ? range.high : power_of_ten(width) - 1;

				if (low > high)
				{
					continue;
				}

				widths.push_back(fixed_width_range(low, high, width, factory));
			}

			return factory.union_(widths);
		}
	}

	const rx* convert(const ast& node, rx_factory& factory, bool is_canonical)
	{
		switch (node.kind())
		{
			case ast_kind::empty:
				return factory.epsilon();

			case ast_kind::chars:
				return factory.chars(as<ast_chars>(node).char_set);

			case ast_kind::decimal_range:
				return convert_decimal_range(as<ast_decimal_range>(node), factory);

			case ast_kind::sequence:
			{
				const ast_sequence& sequence = as<ast_sequence>(node);
				std::vector<const rx*> parts;
				parts.reserve(sequence.children.size());

				for (const auto& child : sequence.children)
				{
					parts.push_back(convert(*child, factory, is_canonical));
				}

				return factory.concat(parts);
			}

			case ast_kind::alternation:
			{
				const ast_alternation& alternation = as<ast_alternation>(node);
				std::vector<const rx*> alternatives;
				alternatives.reserve(alternation.children.size());

				for (const auto& child : alternation.children)
				{
					alternatives.push_back(convert(*child, factory, is_canonical));
				}

				return factory.union_(alternatives);
			}

			case ast_kind::optional:
				return factory.union_(factory.epsilon(), convert(*as<ast_optional>(node).child, factory, is_canonical));

			case ast_kind::interval:
			{
				const ast_interval& interval = as<ast_interval>(node);

				return factory.interval(convert(*interval.child, factory, is_canonical), interval.min_count, interval.max_count);
			}

			case ast_kind::unified:
			{
				const ast_unified& unified = as<ast_unified>(node);

				return convert(is_canonical ? *unified.rendering : *unified.subject, factory, is_canonical);
			}
		}

		throw std::logic_error("Unhandled node kind.");
	}
}
