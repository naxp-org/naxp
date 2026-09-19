// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_RELATIONS_HPP
#define NAXP_RELATIONS_HPP

#include "naxp/naxp.hpp"

#include "compiler.hpp"
#include "state_map.hpp"
#include "value_agreement.hpp"

#include <cstdint>

/// How the languages of two machines, and the encodings of two naxps, stand to one another.
///
/// States are interned within one build, so two languages built through the same factory are
/// equal exactly when their start states are the same object. Two naxps parsed separately do
/// not share a factory, so that shortcut is unavailable here and the walks below are what
/// decide it.
namespace logmu::detail::relations
{
	/// How the language of `a` stands to the language of `b`.
	///
	/// A walk of the product, carrying two facts: whether `a` holds a string `b` does not, and
	/// whether the reverse holds. Both machines are acyclic and trimmed, so a state reached with
	/// characters the other side cannot take is a witness on its own and needs no completion.
	///
	/// @returns The relationship, from `a`'s point of view.
	set_relationship compare_languages(const state_map& a, const state_map& b);

	/// How the encoding of `a` stands to the encoding of `b`, each taken as the set of (text,
	/// value) pairs it defines.
	///
	/// One graph contains another exactly when its domain does and the two functions agree on
	/// the smaller domain. So the relationship is the one between the accepted languages,
	/// provided every string both accept takes the same value under each, and `incomparable`
	/// otherwise, since a string valued differently puts a pair in each graph that the other
	/// lacks.
	///
	/// Value agreement is asked of the values and never of the canonical forms. `(A|B)!A` and
	/// `(A|B)!B` print `B` differently and value it alike, and their encodings are equal. Where
	/// neither naxp holds a unified element the canonical machines are the accepted ones and
	/// the rank agreement decides exactly. Otherwise its disagreement is still exact, because a
	/// shared canonical string is fixed by both canonicalisations, and it is tried first for
	/// that reason; its agreement says nothing about the strings a unified element rewrites,
	/// which the value agreement then decides over parses.
	///
	/// This is the one axis of the comparison that can fail to be decided, when a walk outgrows
	/// its budget. It then returns false and the relationship is left as `incomparable`, which
	/// is the value that claims nothing.
	///
	/// @param relationship The relationship, from `a`'s point of view.
	/// @param budget How many product states either walk may build.
	/// @returns Whether the relationship was decided.
	bool try_compare_encodings(const compilation& a, const compilation& b, set_relationship& relationship, int budget = value_agreement::max_tuples);

	/// The lowest value both machines hold that they decode to different strings, or zero
	/// where every value both hold decodes alike.
	///
	/// The walk enumerates both canonical languages in the order the specification defines,
	/// the empty string first and then each first class in set order, each character of it in
	/// ASCII order and each continuation in turn, and compares the two enumerations position
	/// by position. Where two continuations are reached by the same character the comparison
	/// recurses and a whole chunk is stepped over by its count, which is what keeps the walk
	/// linear in the product of the machines rather than in the number of values. The result
	/// at a pair of states does not depend on how the pair was reached, so it is memoised.
	///
	/// Counts are trusted, which W5 guarantees for a canonical machine: the count of values is
	/// capped at 2^64 - 1, so no count here is saturated.
	///
	/// @param a The first canonical machine.
	/// @param b The second canonical machine.
	/// @returns The value, or zero for none.
	std::uint64_t first_divergent_value(const state_map& a, const state_map& b);
}

#endif
