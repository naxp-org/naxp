// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_W3_CHECKER_HPP
#define NAXP_W3_CHECKER_HPP

#include "ast.hpp"
#include "fault.hpp"
#include "naxp_limits.hpp"
#include "rx.hpp"
#include "tx.hpp"

#include <optional>

/// Decides W3: whether rho is single valued, so that every accepted string has exactly one
/// canonical form and therefore exactly one value.
///
/// W3 is a property of pairs of parses, so this tracks pairs and never sets. The obvious
/// alternative, a subset construction over sets of live branches, is a determinisation: it
/// computes rho online, and for `[ab]{17}c|([ab]!a){17}d` that function provably needs 2^17
/// states even though both of the naxp's machines have fewer than forty. Tracking pairs decides
/// the same naxp in a few dozen. The argument is in `encoding/w3-functionality.md`.
///
/// A state is two residuals and a delay. The delay is what one branch has emitted beyond the
/// other, so at most one side of it is non-empty; the moment both are, the two outputs differ at
/// a position neither can revisit and the delay collapses to a mismatch, after which no output
/// need be tracked at all.
///
/// The check is skipped outright for a naxp with no `!`, where rho is the identity. That is the
/// only short-circuit: the by-eye rule in the specification is sufficient rather than necessary,
/// and putting an unproved condition in front of the decision is the mistake that
/// `encoding/canonicity.md` records.
namespace logmu::detail::w3_checker
{
	/// Checks that unification is single valued.
	///
	/// @param tree The tree, which must already have passed W1 and W2.
	/// @param rxs The factory the machines will be built with, reused for interning.
	/// @param error The fault, or nothing if the naxp passes.
	/// @param max_states The budget, lowered by tests so the cap can be reached cheaply.
	/// @returns Whether the naxp passes.
	bool try_check(const ast& tree, rx_factory& rxs, std::optional<fault>& error, int max_states = limits::max_states);

	/// Checks a transduction that has already been built.
	///
	/// The compiler needs the same transduction afterwards, to build the machine that
	/// canonicalises, so it converts once and passes it to both rather than paying for the
	/// derivatives twice.
	///
	/// @param root The transduction.
	/// @param factory The factory that made it, whose derivative cache is reused.
	/// @param error The violation, or nothing where there is none.
	/// @param max_states The budget, lowered by tests so the cap can be reached cheaply.
	/// @returns Whether unification is single valued.
	bool try_check(const tx* root, tx_factory& factory, std::optional<fault>& error, int max_states = limits::max_states);
}

#endif
