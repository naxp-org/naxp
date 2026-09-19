// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_FOLDING_HPP
#define NAXP_FOLDING_HPP

#include "ast.hpp"

namespace logmu::detail::folding
{
	/// Expands a case fold over the element it binds to, written `\C` for upper case canonical
	/// and `\c` for lower.
	///
	/// A fold is shorthand and nothing downstream of the parser sees one. `\CA` becomes `[Aa]!A`
	/// and `\C[A-F]` becomes the six way alternation the specification writes out, so the
	/// language, the well-formedness rules and the encoding all apply to the expansion and none
	/// of them needs a case for folding. W2 falls out of this: a fold inside the operand of a
	/// `!` puts a `!` there, which the existing nesting check refuses.
	///
	/// The expansion grows with the alphabet rather than with the pattern, `\C\a` being twenty
	/// six alternatives. That is affordable because the branches are disjoint on their first
	/// character, so the machines built from them merge back into one state with a wider
	/// transition table.
	///
	/// Every node made here takes the pattern offset of the node it replaces, so a fault found
	/// in an expansion still points at what the author wrote.
	///
	/// @param node The element, already parsed.
	/// @param to_upper Whether upper case is canonical, which is `\C`.
	/// @returns The element with the fold expanded, or the element itself where nothing has case.
	ast_ptr apply(const ast_ptr& node, bool to_upper);
}

#endif
