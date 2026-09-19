// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "check.hpp"
#include "naxp_message_rules.hpp"

#include "ascii_char_set.hpp"
#include "ast.hpp"
#include "compiler.hpp"
#include "fault.hpp"
#include "parser.hpp"
#include "rx.hpp"
#include "rx_converter.hpp"
#include "state_map.hpp"

#include <algorithm>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

using namespace logmu::detail;
using logmu::testing::rule_of;

namespace
{
	std::unique_ptr<compilation> compile(std::string_view naxp)
	{
		std::unique_ptr<compilation> compiled;
		std::optional<fault> error;

		if (!compiler::try_compile(naxp, compiled, error))
		{
			logmu::testing::fail(__FILE__, __LINE__, std::string(naxp) + " was invalid: " + error->to_string());
		}

		return compiled;
	}

	ast_ptr parse_only(std::string_view naxp)
	{
		ast_ptr tree;
		std::optional<fault> error;

		if (!try_parse(naxp, tree, error))
		{
			logmu::testing::fail(__FILE__, __LINE__, std::string(naxp) + " was invalid: " + error->to_string());
		}

		return tree;
	}

	ascii_char_set set_of(std::string_view characters)
	{
		ascii_char_set set;

		for (const char c : characters)
		{
			set = set | ascii_char_set::single_character(c);
		}

		return set;
	}

	void number(const state* current, std::unordered_map<const state*, int>& numbers, std::vector<const state*>& order)
	{
		if (numbers.find(current) != numbers.end())
		{
			return;
		}

		numbers.emplace(current, static_cast<int>(order.size()));
		order.push_back(current);

		for (const transition& arc : current->transitions)
		{
			number(arc.next, numbers, order);
		}
	}

	/// A machine as text, numbered from the start so that two machines with the same shape
	/// describe the same way whatever ids they were built with.
	std::string describe(const state_map& map)
	{
		std::unordered_map<const state*, int> numbers;
		std::vector<const state*> order;

		number(map.start, numbers, order);

		std::string text;

		for (const state* current : order)
		{
			text += std::to_string(numbers.at(current)) + " count=" + std::to_string(current->string_count);

			for (const transition& arc : current->transitions)
			{
				text += " <";

				for (const char c : arc.set)
				{
					text.push_back(c);
				}

				text += ">->" + std::to_string(numbers.at(arc.next));
			}

			text += "\n";
		}

		return text;
	}

	std::string fault_rule(std::string_view naxp)
	{
		std::unique_ptr<compilation> compiled;
		std::optional<fault> error;

		if (compiler::try_compile(naxp, compiled, error))
		{
			logmu::testing::fail(__FILE__, __LINE__, std::string(naxp) + " was accepted.");
		}

		return std::string(rule_of(error->message));
	}
}

NAXP_TEST(worked_example_matches_the_specification)
{
	const std::unique_ptr<compilation> compiled = compile("#[0-10]");
	const state_map& map = compiled->canonical();

	NAXP_CHECK_EQUAL(std::uint64_t{11}, map.string_count());

	const state* start = map.start;

	NAXP_CHECK_EQUAL(std::size_t{2}, start->transitions.size());
	NAXP_CHECK(!start->accepts_end_of_text());

	NAXP_CHECK(set_of("023456789") == start->transitions[0].set);
	NAXP_CHECK(start->transitions[0].next->is_terminal());

	NAXP_CHECK(set_of("1") == start->transitions[1].set);

	const state* after_one = start->transitions[1].next;

	NAXP_CHECK_EQUAL(std::uint64_t{2}, after_one->string_count);
	NAXP_CHECK_EQUAL(std::size_t{2}, after_one->transitions.size());
	NAXP_CHECK(after_one->transitions[0].set.is_empty());
	NAXP_CHECK(after_one->transitions[0].next->is_terminal());
	NAXP_CHECK(set_of("0") == after_one->transitions[1].set);
	NAXP_CHECK(after_one->transitions[1].next->is_terminal());
}

NAXP_TEST(padded_decimal_range_has_one_width)
{
	const std::unique_ptr<compilation> compiled = compile("#[00-10]");
	const state_map& map = compiled->canonical();

	NAXP_CHECK_EQUAL(std::uint64_t{11}, map.string_count());
	NAXP_CHECK(!map.start->accepts_end_of_text());
	NAXP_CHECK(set_of("0") == map.start->transitions[0].set);
	NAXP_CHECK(set_of("1") == map.start->transitions[1].set);
}

NAXP_TEST(equivalent_naxps_give_the_same_machine)
{
	const std::pair<const char*, const char*> pairs[] = {
		{"AB|AC", "A(B|C)"},
		{"A?A?", "(AA)?|A"},
		{"[AB]C|[BC]C", "[ABC]C"},
		{"A{2,4}", "AAA?A?"},
		{"A|A", "A"},
		{"#[0-9]", "[0-9]"},
		{"()?", "()"},
	};

	for (const auto& [left, right] : pairs)
	{
		NAXP_CHECK_EQUAL(describe(compile(left)->canonical()), describe(compile(right)->canonical()));
	}
}

NAXP_TEST(different_renderings_give_different_machines)
{
	NAXP_CHECK(describe(compile("(A|b)!bX|BY")->canonical()) != describe(compile("(A|b)!AX|BY")->canonical()));

	// Both accept the same three strings, so only the canonical machines differ.
	NAXP_CHECK_EQUAL(describe(compile("(A|b)!bX|BY")->accepted()), describe(compile("(A|b)!AX|BY")->accepted()));
}

NAXP_TEST(same_values_different_text)
{
	NAXP_CHECK_EQUAL(std::uint64_t{1}, compile("[\\s\\-]!?")->max_encoded_value());
	NAXP_CHECK_EQUAL(std::uint64_t{1}, compile("[\\s\\-]?!\\-")->max_encoded_value());
	NAXP_CHECK(describe(compile("[\\s\\-]!?")->canonical()) != describe(compile("[\\s\\-]?!\\-")->canonical()));
}

NAXP_TEST(string_counts)
{
	NAXP_CHECK_EQUAL(std::uint64_t{1000000000000000000}, compile("\\9{18}")->max_encoded_value());
	NAXP_CHECK_EQUAL(std::uint64_t{1000}, compile("\\9{3}")->max_encoded_value());
	NAXP_CHECK_EQUAL(std::uint64_t{60}, compile("[0-5]\\9")->max_encoded_value());
	NAXP_CHECK_EQUAL(std::uint64_t{106}, compile("#[0-105]")->max_encoded_value());
}

NAXP_TEST(w5_invalidates_twenty_digits)
{
	compile("\\9{19}");
	NAXP_CHECK_EQUAL(std::string("W5"), fault_rule("\\9{20}"));
}

NAXP_TEST(w5_invalidates_a_product_of_two_legal_halves)
{
	compile("\\9{19}");
	NAXP_CHECK_EQUAL(std::string("W5"), fault_rule("\\9{19}\\9{19}"));
}

NAXP_TEST(w5_invalidates_a_sum_of_two_legal_alternatives)
{
	compile("\\9{19}");
	compile("[A-J]\\9{17}[A-J]");
	NAXP_CHECK_EQUAL(std::string("W5"), fault_rule("\\9{19}|[A-J]\\9{17}[A-J]"));
}

NAXP_TEST(state_budget_invalidates_a_machine_too_large_to_build)
{
	rx_factory factory;

	// (A{50}){10} is 500 copies, the machine A{500} would give if a count could have three
	// digits.
	const ast_ptr tree = parse_only("(A{50}){10}");
	const rx* expression = rx_converter::convert(*tree, factory, true);

	std::unique_ptr<state_map> map;
	std::optional<fault> error;

	NAXP_CHECK(!state_map_builder::try_build(expression, factory, map, error, 100));
	NAXP_CHECK_EQUAL(std::string("W6"), std::string(rule_of(error->message)));

	NAXP_CHECK(state_map_builder::try_build(expression, factory, map, error, 1000));
	NAXP_CHECK_EQUAL(std::uint64_t{1}, map->string_count());
}

NAXP_TEST(minterms_split_overlapping_sets)
{
	const ascii_char_set sets[] = {set_of("AB"), set_of("BC")};
	const std::vector<ascii_char_set> blocks = state_map_builder::minterms(sets);

	NAXP_CHECK_EQUAL(std::size_t{3}, blocks.size());

	for (const char* block : {"A", "B", "C"})
	{
		NAXP_CHECK(std::find(blocks.begin(), blocks.end(), set_of(block)) != blocks.end());
	}
}

NAXP_TEST(minterms_of_one_set_are_that_set)
{
	const ascii_char_set sets[] = {set_of("ABC")};

	NAXP_CHECK(std::vector<ascii_char_set>{set_of("ABC")} == state_map_builder::minterms(sets));
}
