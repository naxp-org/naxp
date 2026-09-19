// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_VALUE_AGREEMENT_HPP
#define NAXP_VALUE_AGREEMENT_HPP

#include "compiler.hpp"
#include "rank_agreement.hpp"

#include <optional>
#include <string>

namespace logmu::detail
{
	/// Whether two naxps give the same encoded value to every string they both accept.
	///
	/// This is the question the encoding relation turns on, and it is not the question of
	/// whether the two canonicalise alike. `(A|B)!A` and `(A|B)!B` print different things for
	/// `B` and give it the same value, because each rank is taken in its own canonical language.
	/// So canonical forms are never compared here. Each side's output is fed into its own
	/// canonical machine as it is emitted, and what is carried is the difference of the two
	/// running rank totals, as the rank agreement carries it over two machines.
	///
	/// A state is a pair of transduction residuals, one from each naxp, with the state each
	/// canonical machine has reached on its side's output so far. Nothing is held back: a copied
	/// character is fixed by narrowing the block to single characters before it is walked, a
	/// rendering is a fixed string, and a skipped element's end of text output is a fixed
	/// string, so every emission is concrete at the step that makes it. That is why there is no
	/// delay here where the W3 checker needs one. The square compares two strings that arrive
	/// at different times; this compares two numbers, and a number can be accumulated as its
	/// string arrives.
	///
	/// The difference at a state is a property of the state, not of the path: from a state
	/// every completion both sides accept has one output per side, by W3, so the rank each side
	/// adds over it is fixed, and two arrivals with different differences cannot both finish at
	/// zero. A second and unequal arrival is therefore a disagreement rather than a branch, with
	/// one proviso that the rank agreement also makes: it counts only where the two residuals
	/// still share a completion.
	///
	/// The argument, the pairs it was tried on and the construction it replaces are in
	/// `encoding/comparison-square-review.md`.
	namespace value_agreement
	{
		/// How many product states the walk may build before giving up.
		///
		/// The same figure as `rank_agreement::max_pairs`, for the same reason.
		inline constexpr int max_tuples = 200'000;

		/// Whether `a` and `b` give the same value to every string both accept.
		///
		/// @param a The first naxp.
		/// @param b The second naxp.
		/// @param witness Where they differ, a string both accept and value differently, the
		///     shortest the walk found. Otherwise nothing.
		/// @param budget How many product states to allow.
		/// @returns Whether they agree, differ, or could not be decided within the budget.
		agreement compare(const compilation& a, const compilation& b, std::optional<std::string>& witness, int budget = value_agreement::max_tuples);
	}
}

#endif
