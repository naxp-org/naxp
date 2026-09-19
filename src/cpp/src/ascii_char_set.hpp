// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_ASCII_CHAR_SET_HPP
#define NAXP_ASCII_CHAR_SET_HPP

#include "bit_operations.hpp"

#include <cstddef>
#include <cstdint>
#include <functional>
#include <iterator>
#include <optional>
#include <stdexcept>

namespace logmu::detail
{
	/// An immutable set of ASCII characters, that is of characters in the range U+0000 to U+007F.
	///
	/// The set is held as 128 bits in two 64-bit words. Every shift below masks its count
	/// explicitly, because a shift by the width of the word is undefined behaviour in C++ and
	/// the obvious two word shift meets that at counts of 0 and 64.
	///
	/// Everything is constexpr, so the named sets at the foot of this file are built at compile
	/// time and a set written into a table costs nothing at start-up.
	class ascii_char_set
	{
	public:
		/// The number of characters that can be held, that is 128.
		static constexpr int character_count = 128;

		/// The empty set.
		constexpr ascii_char_set() noexcept = default;

		/// The set containing the single character `c`.
		///
		/// @param c The single character in the set. Must be ASCII.
		/// @throws std::out_of_range `c` is not ASCII.
		static constexpr ascii_char_set single_character(char c)
		{
			const int index = check_ascii(c);

			return index < 64
				? ascii_char_set(std::uint64_t{1} << index, 0)
				: ascii_char_set(0, std::uint64_t{1} << (index - 64));
		}

		/// The set containing the inclusive character range [`first`, `last`].
		///
		/// @param first The first character in the range. Must be ASCII.
		/// @param last The last character in the range. Must be ASCII and not less than `first`.
		/// @throws std::out_of_range A bound is not ASCII, or `first` is greater than `last`.
		static constexpr ascii_char_set character_range(char first, char last)
		{
			const int minimum = check_ascii(first);
			const int maximum = check_ascii(last);

			if (minimum > maximum)
			{
				throw std::out_of_range("The first character of a range cannot be greater than the last.");
			}

			const std::uint64_t bits_low = minimum < 64 ? mask_range(minimum, maximum < 64 ? maximum : 63) : 0;
			const std::uint64_t bits_high = maximum >= 64 ? mask_range(minimum > 64 ? minimum - 64 : 0, maximum - 64) : 0;

			return ascii_char_set(bits_low, bits_high);
		}

		/// Whether the set is empty.
		constexpr bool is_empty() const noexcept
		{
			return (this->bits_low | this->bits_high) == 0;
		}

		/// The number of characters in the set, in the range 0 to 128.
		constexpr int count() const noexcept
		{
			return popcount(this->bits_low) + popcount(this->bits_high);
		}

		/// If the set holds exactly one character then that character, otherwise nothing.
		constexpr std::optional<char> single_character() const noexcept
		{
			return count() == 1 ? std::optional<char>(static_cast<char>(first_character_code())) : std::nullopt;
		}

		/// Whether the set contains the specified character. A character outside ASCII is never
		/// contained.
		constexpr bool contains(char c) const noexcept
		{
			const int index = static_cast<unsigned char>(c);

			if (index >= character_count)
			{
				return false;
			}

			const std::uint64_t word = index < 64 ? this->bits_low : this->bits_high;

			return ((word >> (index & 63)) & 1) != 0;
		}

		/// The zero based position of `c` among the characters of the set taken in ascending
		/// order, or -1 if the set does not contain it.
		constexpr int index_of(char c) const noexcept
		{
			if (!contains(c))
			{
				return -1;
			}

			const int index = static_cast<unsigned char>(c);

			return index < 64
				? popcount(this->bits_low & mask_below(index))
				: popcount(this->bits_low) + popcount(this->bits_high & mask_below(index - 64));
		}

		/// The character at `index` among the characters of the set taken in ascending order.
		/// The inverse of `index_of`.
		///
		/// @param index The position wanted, from zero to one less than `count()`.
		/// @throws std::out_of_range The set holds no character at that position.
		constexpr char character_at(int index) const
		{
			if (index < 0)
			{
				throw std::out_of_range("The index cannot be negative.");
			}

			const int in_low_word = popcount(this->bits_low);

			if (index < in_low_word)
			{
				return static_cast<char>(set_bit_at(this->bits_low, index));
			}

			const int remaining = index - in_low_word;

			if (remaining >= popcount(this->bits_high))
			{
				throw std::out_of_range("The set holds no character at that position.");
			}

			return static_cast<char>(64 + set_bit_at(this->bits_high, remaining));
		}

		/// Whether this set has any character in common with `other`.
		constexpr bool intersects_with(ascii_char_set other) const noexcept
		{
			return ((this->bits_low & other.bits_low) | (this->bits_high & other.bits_high)) != 0;
		}

		/// Union.
		friend constexpr ascii_char_set operator|(ascii_char_set left, ascii_char_set right) noexcept
		{
			return ascii_char_set(left.bits_low | right.bits_low, left.bits_high | right.bits_high);
		}

		/// Intersection.
		friend constexpr ascii_char_set operator&(ascii_char_set left, ascii_char_set right) noexcept
		{
			return ascii_char_set(left.bits_low & right.bits_low, left.bits_high & right.bits_high);
		}

		/// Set difference: the characters in `left` but not in `right`.
		friend constexpr ascii_char_set operator-(ascii_char_set left, ascii_char_set right) noexcept
		{
			return ascii_char_set(left.bits_low & ~right.bits_low, left.bits_high & ~right.bits_high);
		}

		friend constexpr bool operator==(ascii_char_set left, ascii_char_set right) noexcept
		{
			return left.bits_low == right.bits_low && left.bits_high == right.bits_high;
		}

		friend constexpr bool operator!=(ascii_char_set left, ascii_char_set right) noexcept
		{
			return !(left == right);
		}

		/// Orders this set against another as the strings of their characters in ascending order
		/// would be ordered ordinally. So `[a]` < `[ab]` < `[abc]` < `[ac]` < `[b]`.
		///
		/// @returns Negative where this set sorts first, zero where the two are equal, positive
		///     where the other sorts first, as `std::string::compare` does.
		constexpr int compare(ascii_char_set other) const noexcept
		{
			if (*this == other)
			{
				return 0;
			}

			// The lowest character at which the two sets differ. It exists, because they are
			// not equal.
			const int first_difference = first_set_bit(
				this->bits_low ^ other.bits_low,
				this->bits_high ^ other.bits_high);

			// Both sets agree below that character, so the comparison is settled by the next
			// character each of them holds at or above it. One of the two holds the differing
			// character itself.
			const int next_in_this = this->first_character_code_at_or_above(first_difference);
			const int next_in_other = other.first_character_code_at_or_above(first_difference);

			// A set with nothing left is a prefix of the other, and a prefix sorts first.
			if (next_in_this == character_count)
			{
				return -1;
			}

			if (next_in_other == character_count)
			{
				return 1;
			}

			return next_in_this < next_in_other ? -1 : 1;
		}

		friend constexpr bool operator<(ascii_char_set left, ascii_char_set right) noexcept
		{
			return left.compare(right) < 0;
		}

		friend constexpr bool operator>(ascii_char_set left, ascii_char_set right) noexcept
		{
			return left.compare(right) > 0;
		}

		friend constexpr bool operator<=(ascii_char_set left, ascii_char_set right) noexcept
		{
			return left.compare(right) <= 0;
		}

		friend constexpr bool operator>=(ascii_char_set left, ascii_char_set right) noexcept
		{
			return left.compare(right) >= 0;
		}

		constexpr std::size_t hash() const noexcept
		{
			const std::uint64_t mixed = this->bits_low ^ (this->bits_high * std::uint64_t{0x9E3779B97F4A7C15});

			return static_cast<std::size_t>(mixed ^ (mixed >> 32));
		}

		/// Walks the characters of a set in ascending order. Defined after the class, because
		/// it holds one and the class is incomplete until its closing brace.
		class iterator;

		constexpr iterator begin() const noexcept;

		constexpr iterator end() const noexcept;

	private:
		constexpr ascii_char_set(std::uint64_t low, std::uint64_t high) noexcept
			: bits_low(low)
			, bits_high(high)
		{
		}

		/// The code of an ASCII character, or a throw where it is not one.
		static constexpr int check_ascii(char c)
		{
			const int index = static_cast<unsigned char>(c);

			if (index >= character_count)
			{
				throw std::out_of_range("The character must be ASCII, that is below U+0080.");
			}

			return index;
		}

		/// The bits from `from` to `to` inclusive, within one word.
		///
		/// @param from The lowest bit to set, in the range 0 to 63.
		/// @param to The highest bit to set, in the range `from` to 63.
		static constexpr std::uint64_t mask_range(int from, int to) noexcept
		{
			// A shift count of 64 is undefined, so the top of the range is special cased rather
			// than written as ((1 << (to + 1)) - 1).
			const std::uint64_t mask_from = ~std::uint64_t{0} << from;
			const std::uint64_t mask_to = to == 63 ? ~std::uint64_t{0} : ((std::uint64_t{1} << (to + 1)) - 1);

			return mask_from & mask_to;
		}

		/// The bits below `index`, within one word.
		///
		/// @param index The bit below which to set, in the range 0 to 63.
		static constexpr std::uint64_t mask_below(int index) noexcept
		{
			// A shift count of 64 is undefined, so zero is special cased.
			return index == 0 ? 0 : (~std::uint64_t{0} >> (64 - index));
		}

		/// The position of the `index`th set bit of a word, counting from zero.
		///
		/// @param word The word, which must hold more than `index` set bits.
		/// @param index How many set bits to skip.
		static constexpr int set_bit_at(std::uint64_t word, int index) noexcept
		{
			// Clearing the lowest set bit is one instruction, and the index is at most 63.
			for (int i = 0; i < index; ++i)
			{
				word &= word - 1;
			}

			return countr_zero(word);
		}

		/// The position of the lowest set bit across the two words, or 128 if both are zero.
		static constexpr int first_set_bit(std::uint64_t bits_low, std::uint64_t bits_high) noexcept
		{
			if (bits_low != 0)
			{
				return countr_zero(bits_low);
			}

			if (bits_high != 0)
			{
				return 64 + countr_zero(bits_high);
			}

			return character_count;
		}

		/// The lowest character in the set, or 128 if it is empty.
		constexpr int first_character_code() const noexcept
		{
			return first_set_bit(this->bits_low, this->bits_high);
		}

		/// The lowest character in the set that is not below `index`, or 128 if there is none.
		///
		/// @param index The character code at or above which to look, in the range 0 to 127.
		constexpr int first_character_code_at_or_above(int index) const noexcept
		{
			const std::uint64_t low = index < 64 ? (this->bits_low & ~mask_below(index)) : 0;
			const std::uint64_t high = index < 64 ? this->bits_high : (this->bits_high & ~mask_below(index - 64));

			return first_set_bit(low, high);
		}

		/// Characters U+0000 to U+003F, one per bit, least significant bit first.
		std::uint64_t bits_low = 0;

		/// Characters U+0040 to U+007F, one per bit, least significant bit first.
		std::uint64_t bits_high = 0;
	};

	class ascii_char_set::iterator
	{
	public:
		using iterator_category = std::forward_iterator_tag;
		using value_type = char;
		using difference_type = std::ptrdiff_t;
		using pointer = const char*;
		using reference = char;

		constexpr iterator() noexcept = default;

		constexpr explicit iterator(ascii_char_set set) noexcept
			: remaining(set)
		{
		}

		constexpr char operator*() const noexcept
		{
			return static_cast<char>(this->remaining.first_character_code());
		}

		constexpr iterator& operator++() noexcept
		{
			this->remaining = this->remaining - single_character(**this);

			return *this;
		}

		constexpr iterator operator++(int) noexcept
		{
			const iterator before = *this;
			++*this;

			return before;
		}

		friend constexpr bool operator==(const iterator& left, const iterator& right) noexcept
		{
			return left.remaining == right.remaining;
		}

		friend constexpr bool operator!=(const iterator& left, const iterator& right) noexcept
		{
			return !(left == right);
		}

	private:
		ascii_char_set remaining;
	};

	constexpr ascii_char_set::iterator ascii_char_set::begin() const noexcept
	{
		return iterator(*this);
	}

	constexpr ascii_char_set::iterator ascii_char_set::end() const noexcept
	{
		return iterator();
	}

	/// The digits `0` to `9`, written `\9` in a naxp.
	inline constexpr ascii_char_set all_digits = ascii_char_set::character_range('0', '9');

	/// The letters `A` to `Z`, written `\A` in a naxp.
	inline constexpr ascii_char_set all_upper_case_letters = ascii_char_set::character_range('A', 'Z');

	/// The letters `a` to `z`, written `\a` in a naxp.
	inline constexpr ascii_char_set all_lower_case_letters = ascii_char_set::character_range('a', 'z');

	/// The digits and the upper case letters, written `\X` in a naxp.
	inline constexpr ascii_char_set all_digits_and_upper_case_letters = all_digits | all_upper_case_letters;

	/// The digits and the lower case letters, written `\x` in a naxp.
	inline constexpr ascii_char_set all_digits_and_lower_case_letters = all_digits | all_lower_case_letters;
}

template <>
struct std::hash<logmu::detail::ascii_char_set>
{
	std::size_t operator()(logmu::detail::ascii_char_set set) const noexcept
	{
		return set.hash();
	}
};

#endif
