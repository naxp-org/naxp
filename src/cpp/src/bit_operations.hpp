// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_BIT_OPERATIONS_HPP
#define NAXP_BIT_OPERATIONS_HPP

#include <cstdint>

namespace logmu::detail
{
	/// The two bit operations `ascii_char_set` rests on, which are `std::popcount` and
	/// `std::countr_zero` in C++20.
	///
	/// The library stops at C++17 so that the toolchains R has shipped since 4.1 can build it,
	/// and these two are what `<bit>` would have given it. Both must be usable in constant
	/// expressions, because the named sets at the foot of `ascii_char_set.hpp` are built at
	/// compile time. GCC and Clang allow their builtins there and compile them to one
	/// instruction each; anything else gets the portable arithmetic, which is what the standard
	/// library itself falls back on.

	/// The number of set bits in a word, from 0 to 64.
	constexpr int popcount(std::uint64_t word) noexcept
	{
#if defined(__GNUC__) || defined(__clang__)
		return __builtin_popcountll(word);
#else
		word = word - ((word >> 1) & std::uint64_t{0x5555555555555555});
		word = (word & std::uint64_t{0x3333333333333333}) + ((word >> 2) & std::uint64_t{0x3333333333333333});
		word = (word + (word >> 4)) & std::uint64_t{0x0F0F0F0F0F0F0F0F};

		return static_cast<int>((word * std::uint64_t{0x0101010101010101}) >> 56);
#endif
	}

	/// The number of consecutive zero bits from the least significant end of a word, which is
	/// the position of its lowest set bit, or 64 for a word of zero.
	constexpr int countr_zero(std::uint64_t word) noexcept
	{
		if (word == 0)
		{
			return 64;
		}

#if defined(__GNUC__) || defined(__clang__)
		return __builtin_ctzll(word);
#else
		// The lowest set bit on its own, less one, is a mask of exactly the zeros below it.
		return popcount((word & (~word + 1)) - 1);
#endif
	}
}

#endif
