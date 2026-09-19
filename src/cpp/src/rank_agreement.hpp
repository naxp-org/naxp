// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_RANK_AGREEMENT_HPP
#define NAXP_RANK_AGREEMENT_HPP

#include "state_map.hpp"

#include <cstdint>

namespace logmu::detail
{
	/// Whether two machines give the same encoded value to every string they both hold.
	enum class agreement
	{
		/// Every string both machines hold takes the same value under each.
		agrees,

		/// At least one string both hold takes different values.
		differs,

		/// Not decided. Either a count saturated, so the arithmetic is not the true rank, or
		/// the walk outgrew its budget.
		undecided,
	};

	/// Comparing the encoded values two canonical machines give the strings they share.
	///
	/// A value is a rank: the count of strings that precede this one. Walking a machine
	/// accumulates that count a character at a time, so walking two machines together and
	/// carrying the difference of the two running totals says whether they will end up
	/// agreeing.
	///
	/// The difference at a state pair is a property of the pair, not of the path that reached
	/// it. Two strings that arrive at the same pair with different differences have the same
	/// completions available to both machines from there, so whatever suffix takes one to an
	/// accepting pair takes the other as well, and the two cannot both come out at zero. A
	/// second, unequal arrival is therefore a disagreement rather than something to explore,
	/// which is what keeps this linear in the size of the product rather than exponential in
	/// the alphabet.
	namespace rank_agreement
	{
		/// How many state pairs the walk may visit before giving up.
		///
		/// W6 caps each machine at 2000 states, so a product can in principle reach four
		/// million pairs. This is far below that, because a comparison worth making is between
		/// two naxps that resemble one another, and one that explodes is telling you they do
		/// not.
		inline constexpr int max_pairs = 200'000;

		/// Whether `a` and `b` give the same value to every string they both hold.
		///
		/// @param a The first canonical machine.
		/// @param b The second canonical machine.
		/// @param budget How many state pairs to allow.
		agreement compare(const state_map& a, const state_map& b, int budget = rank_agreement::max_pairs);

		/// What taking one character adds to the running total, which is the count of strings
		/// the machine passes over to reach it.
		///
		/// The same arithmetic as `codec::encode`, and it has to stay the same: the transitions
		/// before the one taken are skipped whole, and within the one taken the character's own
		/// position counts once for each string its successor holds.
		///
		/// @param from The state the character is read in.
		/// @param taken The transition the character belongs to.
		/// @param c The character.
		std::uint64_t contribution(const state& from, const transition& taken, char c) noexcept;
	}
}

#endif
