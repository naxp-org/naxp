// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_WELL_FORMEDNESS_HPP
#define NAXP_WELL_FORMEDNESS_HPP

#include "ast.hpp"
#include "fault.hpp"

#include <optional>

/// The well-formedness rules that need the finished tree.
///
/// W4 is decided by the parser, where the tokens are read. W3 needs the single-valuedness of a
/// transduction and W5 needs the size of the canonical language, so both wait on the state map.
///
/// W1 asks whether a rendering is one of the strings its subject generates, which is the tree
/// walker's business rather than this one's.
namespace logmu::detail::well_formedness
{
	/// Checks the rules that can be decided from the tree, which is W2 then W1.
	///
	/// @param tree The tree, as returned by `try_parse`.
	/// @param error The fault, or nothing if the tree passes.
	/// @returns Whether the tree passes.
	bool try_check(const ast& tree, std::optional<fault>& error);
}

#endif
