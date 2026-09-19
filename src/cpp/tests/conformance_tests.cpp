// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The library against the conformance data, which was generated from the specification rather
// than from any implementation.

#include "check.hpp"
#include "conformance_data.hpp"
#include "naxp_message_rules.hpp"

#include "naxp/naxp.hpp"

#include "compiler.hpp"
#include "fault.hpp"
#include "tree_walker.hpp"

#include <cstdint>
#include <iostream>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <vector>

using namespace logmu::detail;
using logmu::testing::conformance_case;
using logmu::testing::conformance_data;
using logmu::testing::conformance_invalid_naxp;
using logmu::testing::conformance_pair;
using logmu::testing::conformance_value;
using logmu::testing::rule_of;

namespace
{
	const std::set<std::string> implemented_rules = {"syntax", "W1", "W2", "W3", "W4", "W5", "W6"};

	std::string quoted(const std::string& text)
	{
		return "'" + text + "'";
	}

	void assert_no_failures(const std::vector<std::string>& failures)
	{
		if (failures.empty())
		{
			return;
		}

		std::string report = std::to_string(failures.size()) + " failures:";

		for (const std::string& failure : failures)
		{
			report += "\n    " + failure;
		}

		logmu::testing::fail(__FILE__, __LINE__, report);
	}

	std::unique_ptr<compilation> compile_or_note(const conformance_case& item, std::vector<std::string>& failures)
	{
		std::unique_ptr<compilation> compiled;
		std::optional<fault> error;

		if (!compiler::try_compile(item.naxp, compiled, error))
		{
			failures.push_back(item.naxp + " was invalid: " + error->to_string());
		}

		return compiled;
	}
}

NAXP_TEST(conformance_data_is_for_version_0_10)
{
	const conformance_data& data = conformance_data::load();

	std::cout << "    reading " << data.file << ": " << data.cases.size() << " cases, " << data.invalid_naxps.size() << " invalid naxps, " << data.pairs.size() << " pairs\n";

	NAXP_CHECK_EQUAL(std::string("0.10"), data.naxp_version);
	NAXP_CHECK(!data.cases.empty());
	NAXP_CHECK(!data.invalid_naxps.empty());
}

NAXP_TEST(conformance_cases_are_accepted)
{
	std::vector<std::string> failures;

	for (const conformance_case& item : conformance_data::load().cases)
	{
		compile_or_note(item, failures);
	}

	assert_no_failures(failures);
}

NAXP_TEST(conformance_cases_have_the_stated_counts)
{
	std::vector<std::string> failures;

	for (const conformance_case& item : conformance_data::load().cases)
	{
		const std::unique_ptr<compilation> compiled = compile_or_note(item, failures);

		if (compiled == nullptr)
		{
			continue;
		}

		if (compiled->max_encoded_value() != item.max_encoded_value)
		{
			failures.push_back(item.naxp + " has " + std::to_string(compiled->max_encoded_value()) + " values, and the test data says " + std::to_string(item.max_encoded_value) + ".");
		}

		if (compiled->accepted_count() != item.accepted_count)
		{
			failures.push_back(item.naxp + " accepts " + std::to_string(compiled->accepted_count()) + " strings, and the test data says " + std::to_string(item.accepted_count) + ".");
		}
	}

	assert_no_failures(failures);
}

NAXP_TEST(conformance_values_are_accepted_exactly_when_the_test_data_says_so)
{
	std::vector<std::string> failures;
	int check_count = 0;

	const auto check = [&failures](const conformance_case& item, const compilation& compiled, const std::string& text, bool expected)
	{
		bool too_long = false;
		const bool by_tree = tree_walker::generates(compiled.tree(), text, too_long);
		const bool by_machine = compiled.accepted().accepts(text);

		if (too_long)
		{
			failures.push_back(item.naxp + " abandoned " + quoted(text) + " as too long.");

			return;
		}

		if (by_tree != expected)
		{
			failures.push_back(item.naxp + " finds " + quoted(text) + (by_tree ? " valid" : " invalid") + " by the tree, and the test data says otherwise.");
		}

		if (by_machine != expected)
		{
			failures.push_back(item.naxp + " finds " + quoted(text) + (by_machine ? " valid" : " invalid") + " by the machine, and the test data says otherwise.");
		}
	};

	for (const conformance_case& item : conformance_data::load().cases)
	{
		const std::unique_ptr<compilation> compiled = compile_or_note(item, failures);

		if (compiled == nullptr)
		{
			continue;
		}

		for (const conformance_value& value : item.values)
		{
			++check_count;
			check(item, *compiled, value.in, value.out != 0);
		}

		for (const std::string& invalid_text : item.invalid)
		{
			++check_count;
			check(item, *compiled, invalid_text, false);
		}
	}

	assert_no_failures(failures);
	NAXP_CHECK(check_count > 1400);
}

NAXP_TEST(conformance_values_encode_and_decode_as_the_test_data_says)
{
	std::vector<std::string> failures;
	int check_count = 0;

	for (const conformance_case& item : conformance_data::load().cases)
	{
		const std::unique_ptr<compilation> compiled = compile_or_note(item, failures);

		if (compiled == nullptr)
		{
			continue;
		}

		for (const conformance_value& value : item.values)
		{
			++check_count;

			const std::uint64_t encoded = compiled->encode(value.in);

			if (encoded != value.out)
			{
				failures.push_back(item.naxp + " encodes " + quoted(value.in) + " to " + std::to_string(encoded) + ", and the test data says " + std::to_string(value.out) + ".");
				continue;
			}

			if (value.out == 0)
			{
				continue;
			}

			std::string canonical;

			if (!compiled->try_get_canonical_form(value.in, canonical) || canonical != value.canon)
			{
				failures.push_back(item.naxp + " canonicalises " + quoted(value.in) + " to " + quoted(canonical) + ", and the test data says " + quoted(value.canon) + ".");
			}

			std::string decoded;

			if (!compiled->try_decode(encoded, decoded) || decoded != value.canon)
			{
				failures.push_back(item.naxp + " decodes " + std::to_string(encoded) + " to " + quoted(decoded) + ", and the test data says " + quoted(value.canon) + ".");
			}

			const std::uint64_t re_encoded = compiled->encode(value.canon);

			if (re_encoded != encoded)
			{
				failures.push_back(item.naxp + " encodes the canonical form " + quoted(value.canon) + " to " + std::to_string(re_encoded) + " rather than " + std::to_string(encoded) + ".");
			}
		}

		for (const std::string& invalid_text : item.invalid)
		{
			++check_count;

			const std::uint64_t encoded = compiled->encode(invalid_text);

			if (encoded != 0)
			{
				failures.push_back(item.naxp + " encodes " + quoted(invalid_text) + " to " + std::to_string(encoded) + ", and the test data lists it as invalid.");
			}
		}
	}

	assert_no_failures(failures);
	NAXP_CHECK(check_count > 1400);
}

NAXP_TEST(conformance_complete_cases_decode_to_a_bijection)
{
	std::vector<std::string> failures;

	for (const conformance_case& item : conformance_data::load().cases)
	{
		if (!item.complete || item.max_encoded_value > 2000)
		{
			continue;
		}

		const std::unique_ptr<compilation> compiled = compile_or_note(item, failures);

		if (compiled == nullptr)
		{
			continue;
		}

		std::set<std::string> seen;

		for (std::uint64_t value = 1; value <= item.max_encoded_value; ++value)
		{
			std::string decoded;

			if (!compiled->try_decode(value, decoded))
			{
				failures.push_back(item.naxp + " could not decode " + std::to_string(value) + ", and it claims " + std::to_string(item.max_encoded_value) + " values.");
				continue;
			}

			if (!seen.insert(decoded).second)
			{
				failures.push_back(item.naxp + " decodes two values to " + quoted(decoded) + ".");
			}

			const std::uint64_t again = compiled->encode(decoded);

			if (again != value)
			{
				failures.push_back(item.naxp + " decodes " + std::to_string(value) + " to " + quoted(decoded) + ", which encodes back to " + std::to_string(again) + ".");
			}
		}

		std::string beyond;

		if (compiled->try_decode(item.max_encoded_value + 1, beyond))
		{
			failures.push_back(item.naxp + " decoded a value above its count of " + std::to_string(item.max_encoded_value) + ".");
		}
	}

	assert_no_failures(failures);
}

NAXP_TEST(conformance_complete_cases_list_every_accepted_string)
{
	std::vector<std::string> failures;

	for (const conformance_case& item : conformance_data::load().cases)
	{
		if (!item.complete)
		{
			continue;
		}

		std::uint64_t accepted = 0;

		for (const conformance_value& value : item.values)
		{
			if (value.out != 0)
			{
				++accepted;
			}
		}

		if (accepted != item.accepted_count)
		{
			failures.push_back(item.naxp + " lists " + std::to_string(accepted) + " accepted strings but claims " + std::to_string(item.accepted_count) + ".");
		}
	}

	assert_no_failures(failures);
}

NAXP_TEST(conformance_invalid_naxps_are_invalid_for_the_stated_rule)
{
	std::vector<std::string> failures;

	for (const conformance_invalid_naxp& item : conformance_data::load().invalid_naxps)
	{
		if (implemented_rules.count(item.rule) == 0)
		{
			continue;
		}

		std::unique_ptr<compilation> compiled;
		std::optional<fault> error;

		if (compiler::try_compile(item.naxp, compiled, error))
		{
			failures.push_back(item.naxp + " was accepted; it breaks " + item.rule + " (" + item.note + ").");
			continue;
		}

		const std::string actual(rule_of(error->message));

		if (actual != item.rule)
		{
			failures.push_back(item.naxp + " was invalid for " + actual + " rather than " + item.rule + ": " + error->to_string());
			continue;
		}

		// The fuzz data records the reference implementation's fault in full. A fault that
		// belongs to the naxp as a whole is given the whole pattern as its span, which is what
		// both public surfaces do. Offsets and lengths are compared except for a character
		// outside the repertoire, where the JavaScript counts UTF-16 units and this counts
		// bytes.
		if (item.code.empty())
		{
			continue;
		}

		if (error->code() != item.code)
		{
			failures.push_back(item.naxp + " was invalid with " + std::string(error->code()) + " rather than " + item.code + ": " + error->to_string());
			continue;
		}

		if (item.code == "NAXP1032")
		{
			continue;
		}

		const std::size_t offset = error->offset;
		const std::size_t length = error->is_whole_naxp() ? item.naxp.size() : error->length;

		if (offset != item.offset || length != item.length)
		{
			failures.push_back(item.naxp + " was invalid at " + std::to_string(offset) + ".." + std::to_string(offset + length) + " rather than " + std::to_string(item.offset) + ".." + std::to_string(item.offset + item.length) + ": " + error->to_string());
		}
	}

	assert_no_failures(failures);
}

NAXP_TEST(conformance_pairs_compare_as_the_test_data_says)
{
	std::vector<std::string> failures;

	const auto name = [](logmu::set_relationship relationship)
	{
		switch (relationship)
		{
			case logmu::set_relationship::equal: return "Equal";
			case logmu::set_relationship::subset_of: return "SubsetOf";
			case logmu::set_relationship::superset_of: return "SupersetOf";
			default: return "Incomparable";
		}
	};

	for (const conformance_pair& item : conformance_data::load().pairs)
	{
		const std::optional<logmu::naxp> a = logmu::naxp::try_parse(item.a);
		const std::optional<logmu::naxp> b = logmu::naxp::try_parse(item.b);

		if (!a.has_value() || !b.has_value())
		{
			failures.push_back("A naxp of a pair was invalid: " + item.a + " / " + item.b);
			continue;
		}

		logmu::naxp_comparison comparison;
		const bool decided = logmu::naxp::try_compare(*a, *b, comparison);

		if (decided != item.decided)
		{
			failures.push_back(item.a + " against " + item.b + (decided ? " was decided" : " was undecided") + ", and the test data says otherwise.");
			continue;
		}

		const std::string actual = std::string(name(comparison.accepted_text)) + "/" + name(comparison.encoding) + "/" + name(comparison.printed_text);
		const std::string expected = item.accepted_text + "/" + item.encoding + "/" + item.printed_text;

		if (actual != expected)
		{
			failures.push_back(item.a + " against " + item.b + " compares as " + actual + ", and the test data says " + expected + ".");
		}

		const std::uint64_t divergent = logmu::naxp::first_divergent_value(*a, *b);

		if (divergent != item.first_divergent_value)
		{
			failures.push_back(item.a + " against " + item.b + " diverges at " + std::to_string(divergent) + ", and the test data says " + std::to_string(item.first_divergent_value) + ".");
		}
	}

	assert_no_failures(failures);
}

NAXP_TEST(conformance_invalid_naxps_leave_no_rule_unimplemented)
{
	for (const conformance_invalid_naxp& item : conformance_data::load().invalid_naxps)
	{
		NAXP_CHECK(implemented_rules.count(item.rule) != 0);
	}
}
