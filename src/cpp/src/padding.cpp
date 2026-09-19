// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "array_view.hpp"
#include "padding.hpp"

#include "ascii_char_set.hpp"
#include "ast.hpp"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace logmu::detail::padding
{
	namespace
	{
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

		ast_ptr zero(std::size_t offset)
		{
			return std::make_shared<ast_chars>(ascii_char_set::single_character('0'), offset);
		}

		/// One padding position: a mandatory zero, or the unified element its mark stands for.
		ast_ptr padding_position(char mark, std::size_t offset)
		{
			if (mark == '\0')
			{
				return zero(offset);
			}

			// The expansions are the ones the specification gives: 0! is 0!!, which is 0?!(0),
			// and 0? is 0!?, which is 0?!().
			ast_ptr subject = std::make_shared<ast_optional>(zero(offset), offset);
			const bool reproduced = mark == '!';

			ast_ptr rendering = reproduced
				? zero(offset)
				: ast_ptr(std::make_shared<ast_empty>(offset));

			const unified_form form = reproduced ? unified_form::reproduced : unified_form::dropped;

			return std::make_shared<ast_unified>(std::move(subject), std::move(rendering), form, offset);
		}

		/// The alternative for values of one width: its padding, then the values themselves.
		ast_ptr alternative(std::uint64_t first, std::uint64_t last, int width, int low_digit_count, array_view<char> marks, std::size_t offset)
		{
			const int pad_count = std::max(0, low_digit_count - width);
			ast_ptr values = std::make_shared<ast_decimal_range>(first, width, last, width, offset);

			if (pad_count == 0)
			{
				return values;
			}

			std::vector<ast_ptr> parts;
			parts.reserve(static_cast<std::size_t>(pad_count) + 1);

			for (int position = 0; position < pad_count; ++position)
			{
				parts.push_back(padding_position(marks[static_cast<std::size_t>(position)], offset));
			}

			parts.push_back(std::move(values));

			return std::make_shared<ast_sequence>(std::move(parts), offset);
		}
	}

	ast_ptr expand(
		std::uint64_t low,
		int low_digit_count,
		std::uint64_t high,
		int high_digit_count,
		array_view<char> marks,
		std::size_t offset)
	{
		std::vector<ast_ptr> alternatives;
		alternatives.reserve(static_cast<std::size_t>(high_digit_count));

		for (int width = 1; width <= high_digit_count; ++width)
		{
			const std::uint64_t lowest = width == 1 ? 0 : powers_of_ten[static_cast<std::size_t>(width - 1)];
			const std::uint64_t highest = powers_of_ten[static_cast<std::size_t>(width)] - 1;

			const std::uint64_t first = std::max(low, lowest);
			const std::uint64_t last = std::min(high, highest);

			if (first > last)
			{
				continue;
			}

			alternatives.push_back(alternative(first, last, width, low_digit_count, marks, offset));
		}

		return alternatives.size() == 1
			? alternatives[0]
			: std::make_shared<ast_alternation>(std::move(alternatives), offset);
	}
}
