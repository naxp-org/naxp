// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_NAXP_LIMITS_HPP
#define NAXP_NAXP_LIMITS_HPP

#include <cstdint>
#include <limits>

/// The figures W6 fixes, and W5's ceiling on an encoded value.
///
/// W6 exists because W5 does not bound the work. W5 caps the largest encoded value, and a naxp
/// can be enormous while producing very few of them: `(A{99}){99}` is eleven characters of
/// pattern denoting a single string of 9 801 characters, whose minimal machine has 9 802 states
/// and exactly one encoded value.
///
/// There is one number, with the others derived from it, so they cannot drift apart. A string of
/// n characters forces a machine of at least n + 1 states, so the longest string a naxp
/// satisfying W6 can generate is one shorter than the budget.
///
/// These are figures of the language rather than of this implementation, which is why W6 states
/// them. Two implementations disagreeing here would disagree about which naxps are valid, and
/// the value of a naxp is that it means the same thing everywhere. The reasoning behind each
/// figure is with the C# implementation, in `NaxpLimits.cs`.
namespace logmu::detail::limits
{
	/// The most states a naxp's machine may have, which is W6.
	inline constexpr int max_states = 2000;

	/// The longest string a naxp satisfying W6 can generate. Derived rather than chosen: a
	/// machine of `max_states` states has a longest path of one fewer.
	inline constexpr int max_string_length = max_states - 1;

	/// The most states the canonicalisation machine may have, which W6 also fixes. The same
	/// figure as `max_states` rather than a separate one, so there is one thing to reason about.
	inline constexpr int max_canonical_states = max_states;

	/// The largest encoded value the encoding can produce, which is W5's limit of 2^64 - 1.
	inline constexpr std::uint64_t max_encoded_value = std::numeric_limits<std::uint64_t>::max();
}

#endif
