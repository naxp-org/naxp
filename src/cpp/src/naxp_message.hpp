// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_NAXP_MESSAGE_HPP
#define NAXP_NAXP_MESSAGE_HPP

#include <string>
#include <string_view>

namespace logmu::detail
{
	/// Every fault this implementation can produce, one member per message.
	///
	/// The member's words say which fault a line is about; its code, `NAXP1001` and so on, is
	/// what a caller sees, and `message_code` gives it. The C# spells both in one identifier,
	/// which C++ cannot read back, so here the code lives in the table beside the prose.
	///
	/// Nothing inside the library handles message text. A fault names a member of this enum
	/// and, at most, supplies one string for it to interpolate; `format_message` is the only
	/// place that knows what any of them say.
	///
	/// The comment above each group names the rule of the specification it belongs to. That is
	/// the only place the rules survive: they organise the language's requirements, they are not
	/// something a user of this library needs, and no code branches on them.
	enum class naxp_message
	{
		// Syntax: quantifiers and intervals
		quantifier_repeated,
		interval_hyphen,
		interval_unbounded,
		interval_not_closed,
		interval_count_not_digits,
		interval_count_split,

		// W4: interval counts
		interval_counts_out_of_order,
		interval_count_too_long,

		// Syntax: groups
		group_not_closed,

		// Syntax: unified elements
		reproduced_after_optional,
		dropped_after_optional,
		reproduced_split,
		dropped_split,
		rendering_missing,

		// Syntax: decimal ranges
		hash_split_from_bracket,
		hash_without_bracket,
		decimal_range_bounds_separator,
		decimal_range_not_closed,
		decimal_range_bound_not_digits,
		decimal_range_bound_split,

		// W4: decimal range bounds
		lower_bound_wider_than_upper,
		upper_bound_leading_zeros,
		lower_bound_exceeds_upper,
		decimal_range_bound_too_long,

		// Syntax: character sets
		character_set_not_closed,
		range_upper_bound_is_block_escape,
		range_reversed,
		character_set_empty,

		// Syntax: escapes
		backslash_before_whitespace,
		backslash_without_escape,
		escape_undefined,

		// Syntax: the pattern itself
		character_not_allowed,

		// Syntax: structure
		element_required,
		alternative_empty,
		unified_without_element,
		naxp_incomplete,
		reserved_character_here,
		character_here,

		// W2: nesting
		unified_nested,

		// W1: renderings
		reproduced_subject_not_single,
		rendering_not_single,
		element_not_deletable,
		rendering_not_generated,

		// W3: single valued unification
		unification_not_single_valued,
		unification_not_single_valued_witness,

		// W5: the size of the encoding
		too_many_values,

		// Not rules of the language: budgets this implementation imposes
		element_too_long,
		too_many_states,
		too_many_canonical_states,
		too_many_pair_states,
		pair_output_abandoned,

		// Syntax: case folds
		fold_in_character_set,

		// Syntax: decimal range padding marks
		decimal_range_mark_split,

		// W4: decimal range padding marks
		decimal_range_mark_on_non_zero,
		decimal_range_mark_not_padding,
		decimal_range_mark_on_upper_bound,

		// Syntax: a closing parenthesis with no group to close
		group_not_opened,

		// Syntax: a case fold where a rendering should begin
		fold_begins_rendering,

		// Syntax: the regex metacharacters naxp reserves so that a regex habit gets a message
		repetition_unbounded,
		any_character,
		anchor,

		// W4: a fixed count of zero
		interval_count_zero,

		/// One past the last member, so the table can be checked against the enum.
		count,
	};

	/// The stable identifier for a message, such as `NAXP1002`.
	std::string_view message_code(naxp_message message) noexcept;

	/// What a message says, with its argument interpolated where it has one.
	///
	/// @param message The message.
	/// @param argument Its argument, or empty where it takes none. No message takes an empty
	///     argument, so nothing is lost in that reading.
	std::string format_message(naxp_message message, std::string_view argument);
}

#endif
