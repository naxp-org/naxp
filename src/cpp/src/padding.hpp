// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_PADDING_HPP
#define NAXP_PADDING_HPP

#include "array_view.hpp"
#include "ast.hpp"

#include <cstddef>
#include <cstdint>

namespace logmu::detail::padding
{
	/// Expands a decimal range whose lower bound carries marks, as in `#[0!0!0-105]`, into an
	/// alternation of ordinary elements.
	///
	/// A mark is shorthand and nothing downstream of the parser sees one. `0!` becomes `0!!`
	/// and `0?` becomes `0!?`, so the language, the well-formedness rules and the encoding all
	/// apply to the expansion and none of them needs a case for padding. W2 falls out of this
	/// in the same way it does for a case fold: a marked range inside the operand of a `!` puts
	/// a `!` there, which the existing nesting check refuses.
	///
	/// The shape is one alternative per width the value itself takes, with the padding positions
	/// that apply to that width written out in front. A padding position is mandatory where it
	/// carries no mark, which is what makes an unmarked leading zero go on setting a minimum
	/// width.
	///
	/// A unified element contributes its rendering whether or not its subject matched anything,
	/// so a run of padding contributes a fixed string. That is what keeps `07` to one canonical
	/// form under `0!! 0!!`, where either of the two could have been the one that matched.
	///
	/// Every node made here takes the pattern offset of the range it replaces, so a fault found
	/// in an expansion still points at what the author wrote.
	///
	/// @param low The value of the lower bound.
	/// @param low_digit_count The digits the lower bound was written with.
	/// @param high The value of the upper bound.
	/// @param high_digit_count The digits the upper bound was written with.
	/// @param marks One entry per digit of the lower bound, each `'!'`, `'?'` or nul where that
	///     digit carries no mark. May be longer than the bound.
	/// @param offset The offset of the range in the pattern, for diagnostics.
	/// @returns The expansion.
	ast_ptr expand(
		std::uint64_t low,
		int low_digit_count,
		std::uint64_t high,
		int high_digit_count,
		array_view<char> marks,
		std::size_t offset);
}

#endif
