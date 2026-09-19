// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// Which rule of the specification each message belongs to.
//
// The library does not carry this. A rule is a way of organising the language's requirements
// rather than something a caller acts on, so in the message table it survives only as a comment
// above each group, and no code there branches on it.
//
// The tests do need it, because the conformance data tags every invalid naxp with a rule and the
// point of those cases is that the right naxp is invalid for the right reason. So the mapping
// lives here, in the thing doing the verifying. If it drifts from the comments the conformance
// cases fail, which is what should happen.
//
// The strings are the data's spelling, so `syntax` is lower case where the W rules are not.

#ifndef NAXP_TESTS_NAXP_MESSAGE_RULES_HPP
#define NAXP_TESTS_NAXP_MESSAGE_RULES_HPP

#include "naxp_message.hpp"

#include <string_view>

namespace logmu::testing
{
	inline std::string_view rule_of(detail::naxp_message message) noexcept
	{
		using detail::naxp_message;

		switch (message)
		{
			case naxp_message::interval_counts_out_of_order:
			case naxp_message::interval_count_too_long:
			case naxp_message::lower_bound_wider_than_upper:
			case naxp_message::upper_bound_leading_zeros:
			case naxp_message::lower_bound_exceeds_upper:
			case naxp_message::decimal_range_bound_too_long:
			case naxp_message::decimal_range_mark_on_non_zero:
			case naxp_message::decimal_range_mark_not_padding:
			case naxp_message::decimal_range_mark_on_upper_bound:
			case naxp_message::range_reversed:
			case naxp_message::interval_count_zero:
				return "W4";

			case naxp_message::unified_nested:
				return "W2";

			case naxp_message::reproduced_subject_not_single:
			case naxp_message::rendering_not_single:
			case naxp_message::element_not_deletable:
			case naxp_message::rendering_not_generated:
				return "W1";

			case naxp_message::unification_not_single_valued:
			case naxp_message::unification_not_single_valued_witness:
				return "W3";

			case naxp_message::too_many_values:
				return "W5";

			case naxp_message::element_too_long:
			case naxp_message::too_many_states:
			case naxp_message::too_many_canonical_states:
			case naxp_message::too_many_pair_states:
			case naxp_message::pair_output_abandoned:
				return "W6";

			case naxp_message::count:
				return "";

			default:
				return "syntax";
		}
	}
}

#endif
