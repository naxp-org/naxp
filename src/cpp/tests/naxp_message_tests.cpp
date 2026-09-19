// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "check.hpp"
#include "naxp_message_rules.hpp"

#include "fault.hpp"
#include "naxp_limits.hpp"
#include "naxp_message.hpp"

#include <cstddef>
#include <set>
#include <string>
#include <string_view>
#include <vector>

using logmu::detail::fault;
using logmu::detail::format_message;
using logmu::detail::message_code;
using logmu::detail::naxp_message;

namespace
{
	constexpr std::size_t message_count = static_cast<std::size_t>(naxp_message::count);

	naxp_message message_at(std::size_t index) noexcept
	{
		return static_cast<naxp_message>(index);
	}

	bool takes_an_argument(naxp_message message)
	{
		return format_message(message, "").find("{0}") != std::string::npos;
	}
}

NAXP_TEST(codes_are_numbered_by_position)
{
	for (std::size_t i = 0; i < message_count; ++i)
	{
		NAXP_CHECK_EQUAL("NAXP" + std::to_string(1001 + i), std::string(message_code(message_at(i))));
	}
}

NAXP_TEST(codes_are_distinct)
{
	std::set<std::string_view> codes;

	for (std::size_t i = 0; i < message_count; ++i)
	{
		codes.insert(message_code(message_at(i)));
	}

	NAXP_CHECK_EQUAL(message_count, codes.size());
}

NAXP_TEST(every_message_has_text)
{
	for (std::size_t i = 0; i < message_count; ++i)
	{
		const std::string text = format_message(message_at(i), "");

		NAXP_CHECK(!text.empty());
		NAXP_CHECK(text.back() == '.');
	}
}

NAXP_TEST(messages_taking_an_argument_are_exactly_those_that_use_it)
{
	std::vector<naxp_message> with_argument;

	for (std::size_t i = 0; i < message_count; ++i)
	{
		if (takes_an_argument(message_at(i)))
		{
			with_argument.push_back(message_at(i));
		}
	}

	const std::vector<naxp_message> expected = {
		naxp_message::range_reversed,
		naxp_message::escape_undefined,
		naxp_message::character_not_allowed,
		naxp_message::reserved_character_here,
		naxp_message::character_here,
		naxp_message::rendering_not_generated,
		naxp_message::unification_not_single_valued_witness,
		naxp_message::fold_in_character_set,
		naxp_message::repetition_unbounded,
		naxp_message::anchor,
	};

	NAXP_CHECK(expected == with_argument);
}

NAXP_TEST(every_message_taking_an_argument_formats_it_in)
{
	for (std::size_t i = 0; i < message_count; ++i)
	{
		if (!takes_an_argument(message_at(i)))
		{
			continue;
		}

		const std::string text = format_message(message_at(i), "WITNESS");

		NAXP_CHECK(text.find("WITNESS") != std::string::npos);
		NAXP_CHECK(text.find("{0}") == std::string::npos);
	}
}

NAXP_TEST(messages_naming_a_budget_name_the_real_one)
{
	using namespace logmu::detail::limits;

	const std::string states = std::to_string(max_states);

	NAXP_CHECK(format_message(naxp_message::too_many_states, "").find(states) != std::string::npos);
	NAXP_CHECK(format_message(naxp_message::too_many_pair_states, "").find(states) != std::string::npos);
	NAXP_CHECK(format_message(naxp_message::too_many_canonical_states, "").find(std::to_string(max_canonical_states)) != std::string::npos);

	const std::string generated = std::to_string(max_string_length);

	NAXP_CHECK(format_message(naxp_message::element_too_long, "").find(generated) != std::string::npos);
	NAXP_CHECK(format_message(naxp_message::pair_output_abandoned, "").find(generated) != std::string::npos);

	// No message keeps the placeholder the limit is spliced into.
	for (std::size_t i = 0; i < message_count; ++i)
	{
		NAXP_CHECK(format_message(message_at(i), "").find("{limit}") == std::string::npos);
	}
}

NAXP_TEST(every_message_has_a_rule)
{
	for (std::size_t i = 0; i < message_count; ++i)
	{
		NAXP_CHECK(!logmu::testing::rule_of(message_at(i)).empty());
	}
}

NAXP_TEST(a_fault_with_a_position_is_not_mistaken_for_the_whole_naxp)
{
	NAXP_CHECK(fault(naxp_message::too_many_values).is_whole_naxp());
	NAXP_CHECK(!fault(naxp_message::interval_hyphen, 0, 1).is_whole_naxp());
	NAXP_CHECK(!fault(naxp_message::interval_hyphen, 3, 1).is_whole_naxp());
}

NAXP_TEST(a_fault_prints_its_code_and_span_and_text)
{
	NAXP_CHECK_EQUAL(std::string("NAXP1046: ") + format_message(naxp_message::too_many_values, ""), fault(naxp_message::too_many_values).to_string());
	NAXP_CHECK_EQUAL(std::string("NAXP1002 at 3..4: ") + format_message(naxp_message::interval_hyphen, ""), fault(naxp_message::interval_hyphen, 3, 1).to_string());
}
