// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "check.hpp"
#include "naxp_message_rules.hpp"

#include "ast.hpp"
#include "fault.hpp"
#include "naxp_message.hpp"
#include "parser.hpp"
#include "tree_walker.hpp"
#include "well_formedness.hpp"

#include <cstddef>
#include <optional>
#include <string>
#include <string_view>

using namespace logmu::detail;
using logmu::testing::rule_of;

namespace
{
	ast_ptr parse(std::string_view text)
	{
		ast_ptr tree;
		std::optional<fault> error;

		if (!try_parse(text, tree, error) || !well_formedness::try_check(*tree, error))
		{
			logmu::testing::fail(__FILE__, __LINE__, std::string(text) + " was invalid: " + error->to_string());
		}

		return tree;
	}

	fault fault_of(std::string_view text)
	{
		ast_ptr tree;
		std::optional<fault> error;

		if (!try_parse(text, tree, error))
		{
			return *error;
		}

		if (well_formedness::try_check(*tree, error))
		{
			logmu::testing::fail(__FILE__, __LINE__, std::string(text) + " was accepted.");
		}

		return *error;
	}

	bool generates(const ast& node, std::string_view text)
	{
		bool too_long = false;

		return tree_walker::generates(node, text, too_long);
	}

	bool contains(const std::string& text, std::string_view fragment)
	{
		return text.find(fragment) != std::string::npos;
	}
}

// Tree shape

NAXP_TEST(group_does_not_survive_parsing)
{
	const ast_ptr bare = parse("A");
	const ast_ptr grouped = parse("(A)");

	NAXP_CHECK(bare->kind() == ast_kind::chars);
	NAXP_CHECK(grouped->kind() == ast_kind::chars);
	NAXP_CHECK(as<ast_chars>(*bare).char_set == as<ast_chars>(*grouped).char_set);
}

NAXP_TEST(empty_group_is_the_empty_string)
{
	NAXP_CHECK(parse("()")->kind() == ast_kind::empty);
}

NAXP_TEST(double_bang_expands_to_an_optional_subject_rendered_as_itself)
{
	const ast_ptr tree = parse("\\s!!");

	NAXP_CHECK(tree->kind() == ast_kind::unified);

	const ast_unified& unified = as<ast_unified>(*tree);

	NAXP_CHECK(unified.form == unified_form::reproduced);
	NAXP_CHECK(unified.subject->kind() == ast_kind::optional);
	NAXP_CHECK(as<ast_optional>(*unified.subject).child == unified.rendering);
}

NAXP_TEST(bang_query_expands_to_an_optional_subject_rendered_as_nothing)
{
	const ast_ptr tree = parse("\\A!?");

	NAXP_CHECK(tree->kind() == ast_kind::unified);

	const ast_unified& unified = as<ast_unified>(*tree);

	NAXP_CHECK(unified.form == unified_form::dropped);
	NAXP_CHECK(unified.subject->kind() == ast_kind::optional);
	NAXP_CHECK(unified.rendering->kind() == ast_kind::empty);
}

NAXP_TEST(quantifier_binds_to_the_base_before_it)
{
	const ast_ptr tree = parse("AB?");

	NAXP_CHECK(tree->kind() == ast_kind::sequence);

	const ast_sequence& sequence = as<ast_sequence>(*tree);

	NAXP_CHECK_EQUAL(std::size_t{2}, sequence.children.size());
	NAXP_CHECK(sequence.children[0]->kind() == ast_kind::chars);
	NAXP_CHECK(sequence.children[1]->kind() == ast_kind::optional);
}

NAXP_TEST(interval_keeps_both_counts)
{
	const ast_ptr tree = parse("A{2,4}");

	NAXP_CHECK(tree->kind() == ast_kind::interval);
	NAXP_CHECK_EQUAL(2, as<ast_interval>(*tree).min_count);
	NAXP_CHECK_EQUAL(4, as<ast_interval>(*tree).max_count);
}

NAXP_TEST(interval_with_one_count_uses_it_for_both)
{
	const ast_ptr tree = parse("A{3}");

	NAXP_CHECK(tree->kind() == ast_kind::interval);
	NAXP_CHECK_EQUAL(3, as<ast_interval>(*tree).min_count);
	NAXP_CHECK_EQUAL(3, as<ast_interval>(*tree).max_count);
}

NAXP_TEST(decimal_range_keeps_the_widths_as_written)
{
	const ast_ptr tree = parse("#[00-105]");

	NAXP_CHECK(tree->kind() == ast_kind::decimal_range);

	const ast_decimal_range& padded = as<ast_decimal_range>(*tree);

	NAXP_CHECK_EQUAL(std::uint64_t{0}, padded.low);
	NAXP_CHECK_EQUAL(2, padded.low_digit_count);
	NAXP_CHECK_EQUAL(std::uint64_t{105}, padded.high);
	NAXP_CHECK_EQUAL(3, padded.high_digit_count);
}

NAXP_TEST(whitespace_between_tokens_is_ignored)
{
	const std::pair<const char*, const char*> pairs[] = {
		{"[A - E]", "[A-E]"},
		{"A{2 , 5}", "A{2,5}"},
		{"#[0 - 10]", "#[0-10]"},
		{" A | B ", "A|B"},
		{"\\s !!", "\\s!!"},
	};

	for (const auto& [spaced, tight] : pairs)
	{
		const ast_ptr with_spaces = parse(spaced);
		const ast_ptr without_spaces = parse(tight);

		NAXP_CHECK(with_spaces->kind() == without_spaces->kind());

		for (const char* text : {"A", "C", "E", "2", "10", "AAAAA", " ", ""})
		{
			NAXP_CHECK_EQUAL(generates(*without_spaces, text), generates(*with_spaces, text));
		}
	}
}

// Error productions for near misses

NAXP_TEST(interval_with_a_hyphen_names_the_separator)
{
	const fault error = fault_of("A{2-5}");

	NAXP_CHECK_EQUAL("syntax", rule_of(error.message));
	NAXP_CHECK_EQUAL(std::size_t{3}, error.offset);
	NAXP_CHECK(contains(error.text(), "',', not by a hyphen"));
}

NAXP_TEST(interval_unbounded_says_there_is_none)
{
	const fault error = fault_of("A{2,}");

	NAXP_CHECK_EQUAL("syntax", rule_of(error.message));
	NAXP_CHECK(contains(error.text(), "no unbounded interval"));
}

NAXP_TEST(bare_bang_names_the_three_forms)
{
	const fault error = fault_of("A!");

	NAXP_CHECK_EQUAL("syntax", rule_of(error.message));
	NAXP_CHECK_EQUAL(std::size_t{1}, error.offset);
	NAXP_CHECK(contains(error.text(), "'x!y', 'x!!' or 'x!?'"));
}

NAXP_TEST(undefined_escape_lists_the_escape_letters)
{
	const fault error = fault_of("\\d");

	NAXP_CHECK_EQUAL("syntax", rule_of(error.message));
	NAXP_CHECK(contains(error.text(), "'s', '9', 'A', 'a', 'X', 'x', 'C' and 'c'"));
}

NAXP_TEST(range_written_backwards_says_lowest_first)
{
	const fault error = fault_of("[E-A]");

	NAXP_CHECK_EQUAL("W4", rule_of(error.message));
	NAXP_CHECK(contains(error.text(), "lowest first"));
	NAXP_CHECK(contains(error.text(), "'A-E'"));
}

NAXP_TEST(range_written_backwards_suggests_something_that_can_be_typed)
{
	const std::pair<const char*, const char*> cases[] = {
		{"[E-A]", "A-E"},
		{"[~-\\s]", "\\s-~"},
		{"[a-\\]]", "\\]-a"},
	};

	for (const auto& [text, suggestion] : cases)
	{
		const fault error = fault_of(text);

		NAXP_CHECK(contains(error.text(), std::string("Write '") + suggestion + "'."));

		// A suggestion that is not itself a naxp is not a suggestion.
		parse(std::string("[") + suggestion + "]");
	}
}

NAXP_TEST(whitespace_splitting_a_token_points_at_the_whitespace)
{
	struct case_
	{
		const char* text;
		std::size_t offset;
		const char* fragment;
	};

	const case_ cases[] = {
		{"\\ s", 1, "cannot be followed by whitespace"},
		{"A! !", 2, "is one token"},
		{"A{2 5}", 3, "cannot be separated by whitespace"},
		{"# [0-10]", 1, "no whitespace between '#' and '['"},
		{"#[1 0-20]", 3, "cannot be separated by whitespace"},
	};

	for (const auto& [text, offset, fragment] : cases)
	{
		const fault error = fault_of(text);

		NAXP_CHECK_EQUAL("syntax", rule_of(error.message));
		NAXP_CHECK_EQUAL(offset, error.offset);
		NAXP_CHECK(contains(error.text(), fragment));
	}
}

NAXP_TEST(further_faults)
{
	const std::pair<const char*, const char*> cases[] = {
		{"A)", "syntax"},
		{"A-B", "syntax"},
		{"[\\9-A]", "syntax"},
		{"[A-]", "syntax"},
		{"A{}", "syntax"},
		{"A{2,1}", "W4"},
		{"#[5-4]", "W4"},
		{"(A|B)!(A|B)", "W1"},
	};

	for (const auto& [text, rule] : cases)
	{
		NAXP_CHECK_EQUAL(rule, rule_of(fault_of(text).message));
	}
}

NAXP_TEST(closing_parenthesis_with_no_group_is_not_reported_as_reserved)
{
	const std::pair<const char*, std::size_t> cases[] = {
		{"AB)", 2},
		{"A)B", 1},
		{"(A)B)", 4},
		{"\\A\\A?\\9\\X? \\s!! \\9\\A\\A) | GIR \\s!! 0AA", 22},
	};

	for (const auto& [text, offset] : cases)
	{
		const fault error = fault_of(text);

		NAXP_CHECK(error.message == naxp_message::group_not_opened);
		NAXP_CHECK_EQUAL(offset, error.offset);
		NAXP_CHECK(contains(error.text(), "no group"));
	}
}

NAXP_TEST(parenthesis_faults_either_side_are_unchanged)
{
	NAXP_CHECK(fault_of("(AB").message == naxp_message::group_not_closed);
	NAXP_CHECK(fault_of("((A)").message == naxp_message::group_not_closed);
	parse("A\\)B");
}

// Pattern repertoire

NAXP_TEST(pattern_outside_the_repertoire_is_invalid)
{
	for (const char c : {'\x01', '\x7F'})
	{
		const fault error = fault_of(std::string("A") + c);

		NAXP_CHECK_EQUAL("syntax", rule_of(error.message));
		NAXP_CHECK_EQUAL(std::size_t{1}, error.offset);
		NAXP_CHECK_EQUAL(std::size_t{1}, error.length);
		NAXP_CHECK(contains(error.text(), "cannot appear in the pattern"));
	}
}

NAXP_TEST(pattern_outside_the_repertoire_names_the_code_point_where_it_can)
{
	// The pound sign, U+00A3, is two bytes of UTF-8 and is named as one character.
	const fault pound = fault_of("A\xC2\xA3");

	NAXP_CHECK_EQUAL(std::size_t{1}, pound.offset);
	NAXP_CHECK_EQUAL(std::size_t{2}, pound.length);
	NAXP_CHECK(contains(pound.text(), "U+00A3 cannot appear"));

	// A stray continuation byte is not UTF-8, and is named as the byte it is.
	const fault stray = fault_of("A\xA3");

	NAXP_CHECK_EQUAL(std::size_t{1}, stray.offset);
	NAXP_CHECK_EQUAL(std::size_t{1}, stray.length);
	NAXP_CHECK(contains(stray.text(), "byte 0xA3 cannot appear"));
}

NAXP_TEST(empty_pattern_is_not_a_naxp)
{
	NAXP_CHECK_EQUAL("syntax", rule_of(fault_of("").message));
}

// Expansions the parser performs

NAXP_TEST(fold_expands_a_letter_to_a_unified_pair)
{
	const ast_ptr tree = parse("\\CA");

	NAXP_CHECK(tree->kind() == ast_kind::unified);

	const ast_unified& unified = as<ast_unified>(*tree);

	NAXP_CHECK(unified.form == unified_form::fold);
	NAXP_CHECK(as<ast_chars>(*unified.subject).char_set == (ascii_char_set::single_character('A') | ascii_char_set::single_character('a')));
	NAXP_CHECK(as<ast_chars>(*unified.rendering).char_set == ascii_char_set::single_character('A'));
	NAXP_CHECK_EQUAL(std::size_t{0}, unified.pattern_offset);
}

NAXP_TEST(fold_over_characters_with_no_case_has_nothing_to_do)
{
	NAXP_CHECK(parse("\\c\\9")->kind() == ast_kind::chars);
}

NAXP_TEST(marked_decimal_range_expands_to_one_alternative_per_width)
{
	const ast_ptr tree = parse("#[0!0!0-105]");

	NAXP_CHECK(tree->kind() == ast_kind::alternation);

	const ast_alternation& alternation = as<ast_alternation>(*tree);

	NAXP_CHECK_EQUAL(std::size_t{3}, alternation.children.size());

	// Width one: two marked zeros then the range 0 to 9.
	NAXP_CHECK(alternation.children[0]->kind() == ast_kind::sequence);
	NAXP_CHECK_EQUAL(std::size_t{3}, as<ast_sequence>(*alternation.children[0]).children.size());

	// Width three: the range 100 to 105, with no padding in front.
	NAXP_CHECK(alternation.children[2]->kind() == ast_kind::decimal_range);
	NAXP_CHECK_EQUAL(std::uint64_t{100}, as<ast_decimal_range>(*alternation.children[2]).low);
	NAXP_CHECK_EQUAL(std::uint64_t{105}, as<ast_decimal_range>(*alternation.children[2]).high);
}

NAXP_TEST(mark_inside_the_value_names_the_zero_and_its_mark)
{
	const fault error = fault_of("#[10!0-999]");

	NAXP_CHECK(error.message == naxp_message::decimal_range_mark_not_padding);
	NAXP_CHECK_EQUAL(std::size_t{3}, error.offset);
	NAXP_CHECK_EQUAL(std::size_t{2}, error.length);
}

// Matching

NAXP_TEST(decimal_range_matches_the_widths_its_bounds_fix)
{
	struct case_
	{
		const char* naxp;
		const char* text;
		bool expected;
	};

	const case_ cases[] = {
		{"#[0-10]", "0", true},
		{"#[0-10]", "9", true},
		{"#[0-10]", "10", true},
		{"#[0-10]", "00", false},
		{"#[0-10]", "11", false},
		{"#[00-10]", "00", true},
		{"#[00-10]", "7", false},
		{"#[00-105]", "07", true},
		{"#[00-105]", "007", false},
		{"#[0-105]", "07", false},
		{"#[0-105]", "105", true},
		{"#[0-105]", "106", false},
	};

	for (const auto& [naxp, text, expected] : cases)
	{
		NAXP_CHECK_EQUAL(expected, generates(*parse(naxp), text));
	}
}

NAXP_TEST(interval_matches_its_counts)
{
	struct case_
	{
		const char* naxp;
		const char* text;
		bool expected;
	};

	const case_ cases[] = {
		{"A{0,3}", "", true},
		{"A{0,3}", "AAA", true},
		{"A{0,3}", "AAAA", false},
		{"()", "", true},
		{"()", "A", false},
		{"(A?){9}", "AAA", true},
	};

	for (const auto& [naxp, text, expected] : cases)
	{
		NAXP_CHECK_EQUAL(expected, generates(*parse(naxp), text));
	}
}

// Well-formedness

NAXP_TEST(nested_unification_is_w2)
{
	NAXP_CHECK(fault_of("(A!B)!C").message == naxp_message::unified_nested);
	NAXP_CHECK(fault_of("A!(B!C)").message == naxp_message::unified_nested);

	// A fold binds to the whole element, '!' included, so this is one '!' and not two.
	parse("\\CA!A");
}

NAXP_TEST(rendering_faults_are_w1)
{
	NAXP_CHECK(fault_of("[AB]!!").message == naxp_message::reproduced_subject_not_single);
	NAXP_CHECK(fault_of("A!(B|C)").message == naxp_message::rendering_not_single);
	NAXP_CHECK(fault_of("A!()").message == naxp_message::element_not_deletable);

	const fault not_generated = fault_of("A!B");

	NAXP_CHECK(not_generated.message == naxp_message::rendering_not_generated);
	NAXP_CHECK(contains(not_generated.text(), "'B'"));
}

NAXP_TEST(a_case_fold_cannot_begin_a_rendering)
{
	const fault error = fault_of("A!\\CA");

	NAXP_CHECK(error.message == naxp_message::fold_begins_rendering);
	NAXP_CHECK_EQUAL("syntax", rule_of(error.message));
	NAXP_CHECK_EQUAL(std::size_t{2}, error.offset);
}

NAXP_TEST(a_fold_inside_a_unified_element_widens_without_nesting)
{
	// The fold is applied to the whole element, so the inner subject accepts both cases and
	// the rendering comes out upper case, with no second '!' for W2 to refuse.
	const ast_ptr tree = parse("\\C(a|b)!a");

	NAXP_CHECK(tree->kind() == ast_kind::unified);
	NAXP_CHECK(generates(*tree, "A"));
	NAXP_CHECK(generates(*tree, "b"));

	std::string rendering;

	NAXP_CHECK(tree_walker::try_get_single_string(*as<ast_unified>(*tree).rendering, rendering) == tree_walker::single_string_outcome::single);
	NAXP_CHECK_EQUAL(std::string("A"), rendering);
}

NAXP_TEST(single_string_is_found_through_every_node)
{
	const std::pair<const char*, const char*> cases[] = {
		{"()", ""},
		{"AB", "AB"},
		{"A|A", "A"},
		{"A{3}", "AAA"},
		{"#[007-007]", "007"},
		{"(A|A)!A", "A"},
		{"()?", ""},
	};

	for (const auto& [naxp, expected] : cases)
	{
		std::string result;

		NAXP_CHECK(tree_walker::try_get_single_string(*parse(naxp), result) == tree_walker::single_string_outcome::single);
		NAXP_CHECK_EQUAL(std::string(expected), result);
	}

	// A unified element generates what its subject accepts, so a choice in the subject is
	// more than one string whatever the rendering.
	for (const char* naxp : {"[AB]", "A|B", "A?", "A{1,2}", "#[0-9]", "(A|B)!A"})
	{
		std::string result;

		NAXP_CHECK(tree_walker::try_get_single_string(*parse(naxp), result) == tree_walker::single_string_outcome::multiple);
	}
}
