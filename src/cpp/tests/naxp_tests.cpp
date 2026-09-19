// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The two public surfaces: the C++ class and the C face over it.

#include "check.hpp"

#include "naxp/naxp.h"
#include "naxp/naxp.hpp"

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>

// The C++ class and the C handle are both called naxp, one in logmu and one at global scope,
// so neither is brought in with a using declaration here.

namespace
{
	constexpr std::string_view postcode = "\\A\\A?\\9\\X?\\s!!\\9\\A\\A | GIR\\s!!0AA";
}

// The C++ surface

NAXP_TEST(parse_gives_a_working_naxp)
{
	const logmu::naxp parsed = logmu::naxp::parse(postcode);

	NAXP_CHECK_EQUAL(std::string(postcode), std::string(parsed.pattern()));
	NAXP_CHECK(parsed.accepts("SW1A 1AA"));
	NAXP_CHECK(parsed.accepts("SW1A1AA"));
	NAXP_CHECK(!parsed.accepts("SW1A  1AA"));
	NAXP_CHECK(parsed.max_encoded_value() > 0);
	NAXP_CHECK_EQUAL(std::size_t{8}, parsed.max_length());

	const std::uint64_t value = parsed.encode("SW1A1AA");

	NAXP_CHECK(value != 0);
	NAXP_CHECK_EQUAL(value, parsed.encode("SW1A 1AA"));
	NAXP_CHECK_EQUAL(std::uint64_t{0}, parsed.encode("SW1A  1AA"));
	NAXP_CHECK_EQUAL(std::string("SW1A 1AA"), parsed.decode(value));
	NAXP_CHECK(parsed.canonical_form("SW1A1AA") == std::optional<std::string>("SW1A 1AA"));
	NAXP_CHECK(!parsed.canonical_form("nonsense").has_value());
}

NAXP_TEST(lower_case_x_is_a_digit_or_a_lower_case_letter)
{
	// The regex hex escape reads as the block escape followed by two literal digits. Anyone
	// expecting 'A' out of it gets a naxp that accepts 'a41' and refuses 'A'.
	const logmu::naxp parsed = logmu::naxp::parse("\\x41");

	NAXP_CHECK_EQUAL(std::uint64_t{36}, parsed.max_encoded_value());
	NAXP_CHECK(parsed.accepts("a41"));
	NAXP_CHECK(parsed.accepts("741"));
	NAXP_CHECK(!parsed.accepts("A41"));
	NAXP_CHECK(!parsed.accepts("A"));
}

NAXP_TEST(a_case_fold_runs_to_the_end_of_the_enclosing_group)
{
	// Across the rest of the sequence and across any '|'; grouping the fold stops it short,
	// and a later fold sits inside the earlier one, whose case governs.
	const logmu::naxp whole = logmu::naxp::parse("\\CAB");

	NAXP_CHECK(whole.accepts("aB"));
	NAXP_CHECK(whole.accepts("Ab"));
	NAXP_CHECK_EQUAL(std::string("AB"), whole.decode(whole.encode("ab")));

	const logmu::naxp grouped = logmu::naxp::parse("(\\CA)B");

	NAXP_CHECK(grouped.accepts("aB"));
	NAXP_CHECK(!grouped.accepts("Ab"));

	const logmu::naxp across = logmu::naxp::parse("\\CA|b");

	NAXP_CHECK_EQUAL(std::uint64_t{2}, across.max_encoded_value());
	NAXP_CHECK_EQUAL(std::string("B"), across.decode(across.encode("b")));

	const logmu::naxp later = logmu::naxp::parse("\\Ca\\cb");

	NAXP_CHECK_EQUAL(std::string("AB"), later.decode(later.encode("ab")));
}

NAXP_TEST(parse_throws_with_the_fault_in_parts)
{
	try
	{
		logmu::naxp::parse("A{2-5}");
		NAXP_CHECK(false);
	}
	catch (const logmu::naxp_error& error)
	{
		NAXP_CHECK_EQUAL(std::string("NAXP1002"), error.fault().code);
		NAXP_CHECK_EQUAL(std::size_t{3}, error.fault().offset);
		NAXP_CHECK_EQUAL(std::size_t{1}, error.fault().length);
		NAXP_CHECK(std::string(error.what()).find("NAXP1002 at 3..4: ") == 0);
	}
}

NAXP_TEST(try_parse_reports_the_fault_or_leaves_it_alone)
{
	logmu::naxp_fault fault;

	NAXP_CHECK(logmu::naxp::try_parse("A", &fault).has_value());
	NAXP_CHECK(fault.code.empty());

	NAXP_CHECK(!logmu::naxp::try_parse("A!", &fault).has_value());
	NAXP_CHECK_EQUAL(std::string("NAXP1014"), fault.code);
	NAXP_CHECK(!fault.message.empty());

	// A fault of the naxp as a whole is given the whole pattern as its span.
	NAXP_CHECK(!logmu::naxp::try_parse("A!(B|C)", &fault).has_value());
	NAXP_CHECK_EQUAL(std::string("NAXP1041"), fault.code);
	NAXP_CHECK_EQUAL(std::size_t{0}, fault.offset);
	NAXP_CHECK_EQUAL(std::size_t{7}, fault.length);

	NAXP_CHECK(!logmu::naxp::try_parse("").has_value());
}

NAXP_TEST(decode_refuses_zero_and_values_beyond_the_count)
{
	const logmu::naxp digits = logmu::naxp::parse("\\9");

	NAXP_CHECK_EQUAL(std::uint64_t{10}, digits.max_encoded_value());
	NAXP_CHECK_THROWS(std::out_of_range, digits.decode(0));
	NAXP_CHECK_THROWS(std::out_of_range, digits.decode(11));

	std::string text = "untouched";

	NAXP_CHECK(!digits.try_decode(11, text));
	NAXP_CHECK_EQUAL(std::string("untouched"), text);
	NAXP_CHECK(digits.try_decode(10, text));
	NAXP_CHECK_EQUAL(std::string("9"), text);
}

NAXP_TEST(copies_are_the_same_naxp)
{
	const logmu::naxp original = logmu::naxp::parse("\\A\\9");
	const logmu::naxp copy = original;

	NAXP_CHECK_EQUAL(original.encode("B7"), copy.encode("B7"));
	NAXP_CHECK(original.pattern().data() == copy.pattern().data());
}

NAXP_TEST(a_byte_outside_ascii_is_never_accepted)
{
	const logmu::naxp any = logmu::naxp::parse("[\\s-~]{1,3}");

	NAXP_CHECK(any.accepts("~"));
	NAXP_CHECK(!any.accepts("\xC2\xA3"));
	NAXP_CHECK_EQUAL(std::uint64_t{0}, any.encode("\xFF"));
}

// The C surface

NAXP_TEST(c_parse_and_free_round_trip)
{
	::naxp_fault* fault = nullptr;
	::naxp* expression = naxp_parse(postcode.data(), postcode.size(), &fault);

	NAXP_CHECK(expression != nullptr);
	NAXP_CHECK(fault == nullptr);

	std::size_t length = 0;
	const char* pattern = naxp_pattern(expression, &length);

	NAXP_CHECK_EQUAL(postcode.size(), length);
	NAXP_CHECK_EQUAL(std::string(postcode), std::string(pattern));
	NAXP_CHECK(std::strlen(pattern) == length);
	NAXP_CHECK_EQUAL(std::size_t{8}, naxp_max_length(expression));

	NAXP_CHECK(naxp_accepts(expression, "SW1A1AA", 7));
	NAXP_CHECK(!naxp_accepts(expression, "SW1A1A", 6));

	const uint64_t value = naxp_encode(expression, "SW1A1AA", 7);

	NAXP_CHECK(value != 0);
	NAXP_CHECK_EQUAL(value, naxp_encode(expression, "SW1A 1AA", 8));

	char buffer[8];
	std::size_t written = 0;

	NAXP_CHECK(naxp_decode(expression, value, buffer, sizeof buffer, &written));
	NAXP_CHECK_EQUAL(std::size_t{8}, written);
	NAXP_CHECK_EQUAL(std::string("SW1A 1AA"), std::string(buffer, written));

	NAXP_CHECK(naxp_canonical_form(expression, "SW1A1AA", 7, buffer, sizeof buffer, &written));
	NAXP_CHECK_EQUAL(std::string("SW1A 1AA"), std::string(buffer, written));

	naxp_free(expression);
	naxp_free(nullptr);
}

NAXP_TEST(c_decode_refuses_a_short_buffer_and_a_bad_value)
{
	::naxp* expression = naxp_parse("\\A{3}", 5, nullptr);
	char buffer[2];
	std::size_t written = 99;

	NAXP_CHECK(!naxp_decode(expression, 1, buffer, sizeof buffer, &written));
	NAXP_CHECK_EQUAL(std::size_t{0}, written);

	NAXP_CHECK(!naxp_decode(expression, 0, buffer, sizeof buffer, &written));
	NAXP_CHECK(!naxp_decode(expression, naxp_max_encoded_value(expression) + 1, buffer, sizeof buffer, nullptr));

	naxp_free(expression);
}

NAXP_TEST(c_parse_hands_back_a_fault_to_free)
{
	::naxp_fault* fault = nullptr;
	::naxp* expression = naxp_parse("A{2-5}", 6, &fault);

	NAXP_CHECK(expression == nullptr);
	NAXP_CHECK(fault != nullptr);
	NAXP_CHECK_EQUAL(std::string("NAXP1002"), std::string(naxp_fault_code(fault)));
	NAXP_CHECK(std::strlen(naxp_fault_message(fault)) > 0);
	NAXP_CHECK_EQUAL(std::size_t{3}, naxp_fault_offset(fault));
	NAXP_CHECK_EQUAL(std::size_t{1}, naxp_fault_length(fault));

	naxp_fault_free(fault);
	naxp_fault_free(nullptr);

	// Without a place to put the fault the parse still says no.
	NAXP_CHECK(naxp_parse("A{2-5}", 6, nullptr) == nullptr);
}
