// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_RANK_DIFFERENCE_HPP
#define NAXP_RANK_DIFFERENCE_HPP

#include <cstddef>
#include <cstdint>
#include <functional>

namespace logmu::detail
{
	/// The difference between two running rank totals, one per machine.
	///
	/// The C# carries this as a BigInteger. It need not be one: every total is a partial sum of
	/// non-negative contributions towards a value below 2^64, so at every step the difference is
	/// one number below 2^64 less another, and a sign with a 64-bit magnitude holds it exactly.
	/// That holds only if the operations are applied in the order the totals accumulate, adding
	/// one side's contribution and then subtracting the other's, which is how both walks use it.
	class rank_difference
	{
	public:
		constexpr rank_difference() noexcept = default;

		constexpr bool is_zero() const noexcept
		{
			return this->magnitude == 0;
		}

		/// This plus a contribution to the first total.
		constexpr rank_difference plus(std::uint64_t amount) const noexcept
		{
			if (!this->negative)
			{
				return rank_difference(false, this->magnitude + amount);
			}

			return amount >= this->magnitude
				? rank_difference(false, amount - this->magnitude)
				: rank_difference(true, this->magnitude - amount);
		}

		/// This less a contribution to the second total.
		constexpr rank_difference minus(std::uint64_t amount) const noexcept
		{
			if (this->negative)
			{
				return rank_difference(true, this->magnitude + amount);
			}

			return amount > this->magnitude
				? rank_difference(true, amount - this->magnitude)
				: rank_difference(false, this->magnitude - amount);
		}

		friend constexpr bool operator==(rank_difference left, rank_difference right) noexcept
		{
			return left.negative == right.negative && left.magnitude == right.magnitude;
		}

		friend constexpr bool operator!=(rank_difference left, rank_difference right) noexcept
		{
			return !(left == right);
		}

		std::size_t hash() const noexcept
		{
			return std::hash<std::uint64_t>()(this->magnitude) ^ (this->negative ? 0x9E3779B97F4A7C15ULL : 0);
		}

	private:
		constexpr rank_difference(bool negative, std::uint64_t magnitude) noexcept
			: negative(negative && magnitude != 0)
			, magnitude(magnitude)
		{
		}

		bool negative = false;
		std::uint64_t magnitude = 0;
	};
}

#endif
