// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The comparison of two naxps: the three axes through the public surfaces, the encoding
// relation and the first divergent value in the internals, and each walk against the slow way,
// which enumerates every value and compares the answers one by one.

#include "check.hpp"

#include "naxp/naxp.h"
#include "naxp/naxp.hpp"

#include "compiler.hpp"
#include "fault.hpp"
#include "rank_agreement.hpp"
#include "relations.hpp"
#include "value_agreement.hpp"

#include <algorithm>
#include <cstdint>
#include <memory>
#include <optional>
#include <set>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

using logmu::set_relationship;
using namespace logmu::detail;

namespace logmu
{
	/// Reaches the private budgeted overload, which exists so that a test can hit the undecided
	/// path without a naxp large enough to exhaust the real budget.
	class naxp_testing
	{
	public:
		static bool try_compare(const naxp& a, const naxp& b, logmu::naxp_comparison& comparison, int budget)
		{
			return naxp::try_compare(a, b, comparison, budget);
		}
	};
}

namespace
{
	constexpr std::string_view postcode = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
	constexpr std::string_view postcode_space_required = "\\A\\A?\\9\\X? \\s \\9\\A\\A";
	constexpr std::string_view postcode_folded = "\\C(\\A\\A?\\9\\X? \\s!! \\9\\A\\A)";
	constexpr std::string_view postcode_with_gir = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA";
	constexpr std::string_view postcode_tight = "[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]";

	logmu::naxp_comparison compare(std::string_view a, std::string_view b)
	{
		return logmu::naxp::compare(logmu::naxp::parse(a), logmu::naxp::parse(b));
	}

	logmu::naxp_comparison make(set_relationship accepted_text, set_relationship encoding, set_relationship printed_text)
	{
		return logmu::naxp_comparison{accepted_text, encoding, printed_text};
	}

	std::unique_ptr<compilation> compiled(std::string_view pattern)
	{
		std::unique_ptr<compilation> result;
		std::optional<fault> error;

		if (!compiler::try_compile(pattern, result, error))
		{
			logmu::testing::fail(__FILE__, __LINE__, std::string(pattern) + " was invalid: " + error->to_string());
		}

		return result;
	}

	set_relationship encoding(std::string_view a, std::string_view b)
	{
		set_relationship relationship = set_relationship::incomparable;

		NAXP_CHECK(relations::try_compare_encodings(*compiled(a), *compiled(b), relationship));

		return relationship;
	}

	set_relationship accepted(std::string_view a, std::string_view b)
	{
		return relations::compare_languages(compiled(a)->accepted(), compiled(b)->accepted());
	}

	set_relationship canonical(std::string_view a, std::string_view b)
	{
		return relations::compare_languages(compiled(a)->canonical(), compiled(b)->canonical());
	}

	std::uint64_t first_divergent(std::string_view a, std::string_view b)
	{
		return relations::first_divergent_value(compiled(a)->canonical(), compiled(b)->canonical());
	}

	/// Every string a machine's language holds, by walking it.
	std::vector<std::string> strings(const state_map& map)
	{
		std::vector<std::string> result;
		std::vector<std::pair<const state*, std::string>> pending{{map.start, std::string()}};

		while (!pending.empty())
		{
			const auto [current, prefix] = pending.back();
			pending.pop_back();

			if (current->is_terminal())
			{
				result.push_back(prefix);
				continue;
			}

			for (const transition& arc : current->transitions)
			{
				if (arc.set.is_empty())
				{
					result.push_back(prefix);
					continue;
				}

				for (const char c : arc.set)
				{
					pending.emplace_back(arc.next, prefix + c);
				}
			}
		}

		return result;
	}

	/// The first divergent value the slow way: decode every value both hold and compare.
	std::uint64_t first_divergent_by_enumeration(std::string_view a, std::string_view b)
	{
		const std::unique_ptr<compilation> left = compiled(a);
		const std::unique_ptr<compilation> right = compiled(b);
		const std::uint64_t shared = std::min(left->max_encoded_value(), right->max_encoded_value());

		for (std::uint64_t value = 1; value <= shared; ++value)
		{
			std::string x;
			std::string y;
			NAXP_CHECK(left->try_decode(value, x));
			NAXP_CHECK(right->try_decode(value, y));

			if (x != y)
			{
				return value;
			}
		}

		return 0;
	}

	/// Value agreement the slow way: every string either accepts, valued under both.
	agreement values_by_enumeration(std::string_view a, std::string_view b)
	{
		const std::unique_ptr<compilation> left = compiled(a);
		const std::unique_ptr<compilation> right = compiled(b);

		NAXP_CHECK(left->accepted_count() <= 200000 && right->accepted_count() <= 200000);

		for (const compilation* side : {left.get(), right.get()})
		{
			for (const std::string& text : strings(side->accepted()))
			{
				if (!left->accepts(text) || !right->accepts(text))
				{
					continue;
				}

				if (left->encode(text) != right->encode(text))
				{
					return agreement::differs;
				}
			}
		}

		return agreement::agrees;
	}
}

// The three axes

NAXP_TEST(comparison_site_rows)
{
	NAXP_CHECK(make(set_relationship::subset_of, set_relationship::subset_of, set_relationship::equal) == compare(postcode_space_required, postcode));
	NAXP_CHECK(make(set_relationship::subset_of, set_relationship::subset_of, set_relationship::equal) == compare(postcode, postcode_folded));
	NAXP_CHECK(make(set_relationship::subset_of, set_relationship::incomparable, set_relationship::subset_of) == compare(postcode, postcode_with_gir));
	NAXP_CHECK(make(set_relationship::superset_of, set_relationship::incomparable, set_relationship::superset_of) == compare(postcode, postcode_tight));
}

NAXP_TEST(comparison_of_a_naxp_against_itself_is_equal_on_every_axis)
{
	NAXP_CHECK(make(set_relationship::equal, set_relationship::equal, set_relationship::equal) == compare(postcode, postcode));
}

NAXP_TEST(comparison_axes_can_disagree)
{
	NAXP_CHECK(make(set_relationship::equal, set_relationship::equal, set_relationship::incomparable) == compare("(A|B)!A", "(A|B)!B"));
	NAXP_CHECK(make(set_relationship::equal, set_relationship::equal, set_relationship::incomparable) == compare("[\\s\\-]?!\\-", "[\\s\\-]!?"));
	NAXP_CHECK(make(set_relationship::equal, set_relationship::incomparable, set_relationship::superset_of) == compare("[ab]{2}", "([ab]{2})!(aa)"));
	NAXP_CHECK(make(set_relationship::equal, set_relationship::incomparable, set_relationship::equal) == compare("(a|b)!a|c", "a|(b|c)!c"));
}

NAXP_TEST(comparison_swapping_the_arguments_swaps_subset_and_superset)
{
	const logmu::naxp_comparison forward = compare(postcode, postcode_with_gir);
	const logmu::naxp_comparison backward = compare(postcode_with_gir, postcode);

	NAXP_CHECK(forward.accepted_text == set_relationship::subset_of);
	NAXP_CHECK(backward.accepted_text == set_relationship::superset_of);
	NAXP_CHECK(forward.encoding == set_relationship::incomparable);
	NAXP_CHECK(backward.encoding == set_relationship::incomparable);
	NAXP_CHECK(forward.printed_text == set_relationship::subset_of);
	NAXP_CHECK(backward.printed_text == set_relationship::superset_of);
}

NAXP_TEST(comparison_default_claims_nothing)
{
	const logmu::naxp_comparison comparison;

	NAXP_CHECK(comparison.accepted_text == set_relationship::incomparable);
	NAXP_CHECK(comparison.encoding == set_relationship::incomparable);
	NAXP_CHECK(comparison.printed_text == set_relationship::incomparable);
}

NAXP_TEST(comparison_try_compare_decides_what_compare_decides)
{
	logmu::naxp_comparison comparison;

	NAXP_CHECK(logmu::naxp::try_compare(logmu::naxp::parse(postcode), logmu::naxp::parse(postcode_with_gir), comparison));
	NAXP_CHECK(compare(postcode, postcode_with_gir) == comparison);
}

NAXP_TEST(comparison_undecided_channel_is_false_and_throws)
{
	set_relationship relationship = set_relationship::equal;

	NAXP_CHECK(!relations::try_compare_encodings(*compiled(postcode), *compiled(postcode_with_gir), relationship, 1));
	NAXP_CHECK(relationship == set_relationship::incomparable);

	logmu::naxp_comparison comparison = make(set_relationship::equal, set_relationship::equal, set_relationship::equal);

	NAXP_CHECK(!logmu::naxp_testing::try_compare(logmu::naxp::parse(postcode), logmu::naxp::parse(postcode_with_gir), comparison, 1));
	NAXP_CHECK(comparison == logmu::naxp_comparison());
}

// The encoding relation

NAXP_TEST(encoding_values_kept_follows_the_language)
{
	NAXP_CHECK(encoding("AB", "AB") == set_relationship::equal);
	NAXP_CHECK(encoding("[AB]", "A|B") == set_relationship::equal);
	NAXP_CHECK(encoding("[AB]", "[ABC]") == set_relationship::subset_of);
	NAXP_CHECK(encoding("[ABC]", "[AB]") == set_relationship::superset_of);
	NAXP_CHECK(encoding("AB", "AB|AC") == set_relationship::subset_of);
}

NAXP_TEST(encoding_values_moved_is_incomparable)
{
	for (const auto& [a, b] : {std::pair("[AC]", "[ABC]"), std::pair("[ABC]", "[AC]"), std::pair("AC", "AB|AC")})
	{
		NAXP_CHECK(accepted(a, b) != set_relationship::incomparable);
		NAXP_CHECK(encoding(a, b) == set_relationship::incomparable);
	}
}

NAXP_TEST(encoding_incomparable_languages_are_incomparable_encodings)
{
	NAXP_CHECK(encoding("AB", "CD") == set_relationship::incomparable);
	NAXP_CHECK(encoding("[AB]", "[BC]") == set_relationship::incomparable);
}

NAXP_TEST(encoding_same_values_different_text_is_equal)
{
	for (const auto& [a, b] : {
		std::pair("[\\s\\-]?!\\-", "[\\s\\-]!?"),
		std::pair("(A|B)!A", "(A|B)!B"),
		std::pair("(A|B)!A|C", "(A|B)!B|C"),
		std::pair("\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)")})
	{
		NAXP_CHECK(encoding(a, b) == set_relationship::equal);
		NAXP_CHECK(canonical(a, b) == set_relationship::incomparable);
	}
}

NAXP_TEST(encoding_same_canonical_ranks_different_values_is_incomparable)
{
	NAXP_CHECK(accepted("[ab]{2}", "([ab]{2})!(aa)") == set_relationship::equal);
	NAXP_CHECK(rank_agreement::compare(compiled("[ab]{2}")->canonical(), compiled("([ab]{2})!(aa)")->canonical()) == agreement::agrees);
	NAXP_CHECK(encoding("[ab]{2}", "([ab]{2})!(aa)") == set_relationship::incomparable);
}

NAXP_TEST(encoding_safe_edits_are_subset_of)
{
	for (const auto& [a, b] : {
		std::pair<std::string_view, std::string_view>("A", "(A|a)!A"),
		std::pair<std::string_view, std::string_view>("A\\sB", "A\\s!!B"),
		std::pair<std::string_view, std::string_view>(postcode_space_required, postcode),
		std::pair<std::string_view, std::string_view>(postcode, postcode_folded)})
	{
		NAXP_CHECK(encoding(a, b) == set_relationship::subset_of);
	}
}

NAXP_TEST(encoding_breaking_edits_are_incomparable)
{
	NAXP_CHECK(encoding(postcode, postcode_with_gir) == set_relationship::incomparable);
	NAXP_CHECK(encoding(postcode, postcode_tight) == set_relationship::incomparable);
}

NAXP_TEST(encoding_dropping_the_space_is_incomparable)
{
	constexpr std::string_view reproduced = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
	constexpr std::string_view dropped = "\\A\\A?\\9\\X? \\s!? \\9\\A\\A";

	NAXP_CHECK(accepted(reproduced, dropped) == set_relationship::equal);
	NAXP_CHECK(rank_agreement::compare(compiled(reproduced)->canonical(), compiled(dropped)->canonical()) == agreement::agrees);
	NAXP_CHECK(encoding(reproduced, dropped) == set_relationship::incomparable);
}

NAXP_TEST(encoding_implies_accepted_text)
{
	for (const auto& [a, b] : {
		std::pair<std::string_view, std::string_view>("AB", "AB"),
		std::pair<std::string_view, std::string_view>("[AB]", "[ABC]"),
		std::pair<std::string_view, std::string_view>("[AC]", "[ABC]"),
		std::pair<std::string_view, std::string_view>("(A|B)!A", "(A|B)!B"),
		std::pair<std::string_view, std::string_view>("[ab]{2}", "([ab]{2})!(aa)"),
		std::pair<std::string_view, std::string_view>("A\\sB", "A\\s!!B"),
		std::pair<std::string_view, std::string_view>(postcode, "\\A\\A?\\9\\X? \\s!? \\9\\A\\A")})
	{
		const set_relationship relationship = encoding(a, b);

		if (relationship != set_relationship::incomparable)
		{
			NAXP_CHECK(relationship == accepted(a, b));
		}
	}
}

NAXP_TEST(encoding_a_budget_too_small_is_undecided)
{
	for (const auto& [a, b] : {
		std::pair<std::string_view, std::string_view>("[AB]", "[ABC]"),
		std::pair<std::string_view, std::string_view>(postcode, "\\A\\A?\\9\\X? \\s!? \\9\\A\\A")})
	{
		set_relationship relationship = set_relationship::equal;

		NAXP_CHECK(!relations::try_compare_encodings(*compiled(a), *compiled(b), relationship, 1));
		NAXP_CHECK(relationship == set_relationship::incomparable);
	}

	set_relationship relationship = set_relationship::equal;

	NAXP_CHECK(relations::try_compare_encodings(*compiled("AB"), *compiled("CD"), relationship, 1));
	NAXP_CHECK(relationship == set_relationship::incomparable);
}

NAXP_TEST(value_agreement_finds_a_witness)
{
	std::optional<std::string> witness;

	NAXP_CHECK(value_agreement::compare(*compiled(postcode), *compiled(postcode_with_gir), witness) == agreement::differs);
	NAXP_CHECK(witness.has_value());
	NAXP_CHECK_EQUAL(std::uint64_t{405194401}, logmu::naxp::parse(postcode).encode(*witness));
	NAXP_CHECK_EQUAL(std::uint64_t{1688310001}, logmu::naxp::parse(postcode_with_gir).encode(*witness));

	NAXP_CHECK(value_agreement::compare(*compiled("[ab]{2}"), *compiled("([ab]{2})!(aa)"), witness) == agreement::differs);
	NAXP_CHECK(witness.has_value());
	NAXP_CHECK(compiled("[ab]{2}")->encode(*witness) != compiled("([ab]{2})!(aa)")->encode(*witness));
}

NAXP_TEST(value_agreement_agrees_with_enumeration)
{
	const std::pair<const char*, const char*> cases[] = {
		{"[AB]", "[ABC]"},
		{"[AC]", "[ABC]"},
		{"AB|AC", "AB|AC|AD"},
		{"AB|AD", "AB|AC|AD"},
		{"\\A\\9", "\\A\\X"},
		{"A|BB", "A|AB|BB"},
		{"\\A{2}", "\\A{2,3}"},
		{"#[00-99]", "#[00-99]|AA"},
		{"(A|B)!A", "(A|B)!B"},
		{"(A|B)!A|C", "(A|B)!B|C"},
		{"[\\s\\-]?!\\-", "[\\s\\-]!?"},
		{"[ab]{2}", "([ab]{2})!(aa)"},
		{"[ab]{3}", "([ab]{3})!(aab)"},
		{"\\A\\9\\s\\A", "\\A\\9\\s!!\\A"},
		{"\\A\\9\\A", "\\C(\\A\\9\\A)"},
		{"\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)"},
		{"A!?|B", "\\C(A!?|B)"},
		{"A B!! C", "A B? C"},
		{"A B!? C", "A B? C"},
		{"#[0-105]", "#[0?0!0-105]"},
		{"#[0!0-105]", "#[0?0-105]"},
		{"#[00!0-999]", "#[0!00-999]"},
	};

	for (const auto& [a, b] : cases)
	{
		std::optional<std::string> witness;

		NAXP_CHECK(values_by_enumeration(a, b) == value_agreement::compare(*compiled(a), *compiled(b), witness));
	}
}

// The first divergent value

NAXP_TEST(first_divergent_same_canonical_language_is_zero)
{
	for (const auto& [a, b] : {
		std::pair<std::string_view, std::string_view>("AB", "AB"),
		std::pair<std::string_view, std::string_view>("[AB]", "A|B"),
		std::pair<std::string_view, std::string_view>("\\A\\A", "\\C(\\A\\A)"),
		std::pair<std::string_view, std::string_view>("A\\sB", "A\\s!!B"),
		std::pair<std::string_view, std::string_view>(postcode, postcode)})
	{
		NAXP_CHECK_EQUAL(std::uint64_t{0}, first_divergent(a, b));
	}
}

NAXP_TEST(first_divergent_values_added_after_everything_else_is_zero)
{
	for (const auto& [a, b] : {std::pair("[AB]", "[ABC]"), std::pair("AB", "AB|AC"), std::pair("AB", "AB|ABC"), std::pair("A", "A|B{2,9}")})
	{
		NAXP_CHECK_EQUAL(std::uint64_t{0}, first_divergent(a, b));
	}
}

NAXP_TEST(first_divergent_values_moved_is_the_first_moved)
{
	NAXP_CHECK_EQUAL(std::uint64_t{2}, first_divergent("[AC]", "[ABC]"));
	NAXP_CHECK_EQUAL(std::uint64_t{1}, first_divergent("AC", "AB|AC"));
	NAXP_CHECK_EQUAL(std::uint64_t{2}, first_divergent("A|AB", "A|AC"));
}

NAXP_TEST(first_divergent_one_accepts_the_empty_string_is_one)
{
	NAXP_CHECK_EQUAL(std::uint64_t{1}, first_divergent("A?", "A"));
	NAXP_CHECK_EQUAL(std::uint64_t{1}, first_divergent("A", "A?"));
}

NAXP_TEST(first_divergent_one_continuation_runs_out_first_is_the_value_after_it)
{
	NAXP_CHECK_EQUAL(std::uint64_t{2}, first_divergent("A[BC]|D", "AB|D"));
	NAXP_CHECK_EQUAL(std::uint64_t{2}, first_divergent("AB|D", "A[BC]|D"));
}

NAXP_TEST(first_divergent_same_values_different_text_is_one)
{
	for (const auto& [a, b] : {
		std::pair("(A|B)!A", "(A|B)!B"),
		std::pair("[\\s\\-]?!\\-", "[\\s\\-]!?"),
		std::pair("\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)")})
	{
		NAXP_CHECK(encoding(a, b) == set_relationship::equal);
		NAXP_CHECK_EQUAL(std::uint64_t{1}, first_divergent(a, b));
	}
}

NAXP_TEST(first_divergent_adding_gir_diverges_where_the_site_says)
{
	const logmu::naxp without = logmu::naxp::parse(postcode);
	const logmu::naxp with = logmu::naxp::parse(postcode_with_gir);
	const std::uint64_t value = logmu::naxp::first_divergent_value(without, with);

	NAXP_CHECK_EQUAL(std::uint64_t{405194401}, value);
	NAXP_CHECK_EQUAL(std::string("G0 0AA"), without.decode(value));
	NAXP_CHECK_EQUAL(std::string("H0 0AA"), with.decode(value));
	NAXP_CHECK_EQUAL(std::string("FZ9Z 9ZZ"), without.decode(value - 1));
	NAXP_CHECK_EQUAL(std::string("FZ9Z 9ZZ"), with.decode(value - 1));
}

NAXP_TEST(first_divergent_tightening_the_postcode_letters_diverges_at_three)
{
	const logmu::naxp loose = logmu::naxp::parse(postcode);
	const logmu::naxp tight = logmu::naxp::parse(postcode_tight);
	const std::uint64_t value = logmu::naxp::first_divergent_value(loose, tight);

	NAXP_CHECK_EQUAL(std::uint64_t{3}, value);
	NAXP_CHECK(loose.decode(value) != tight.decode(value));
	NAXP_CHECK_EQUAL(loose.decode(value - 1), tight.decode(value - 1));
}

NAXP_TEST(first_divergent_agrees_with_enumeration)
{
	for (const auto& [a, b] : {
		std::pair("[AB]", "[ABC]"),
		std::pair("[AC]", "[ABC]"),
		std::pair("[BC]", "[ABC]"),
		std::pair("AB|AC", "AB|AC|AD"),
		std::pair("AB|AD", "AB|AC|AD"),
		std::pair("\\A\\9", "\\A\\X"),
		std::pair("\\9\\A", "\\X\\A"),
		std::pair("A|BB", "A|BB|C"),
		std::pair("A|BB", "A|AB|BB"),
		std::pair("\\A{2}", "\\A{2,3}"),
		std::pair("\\A{2,3}", "\\A{2}"),
		std::pair("#[00-99]", "#[00-99]|AA"),
		std::pair("#[0-105]", "#[0?0!0-105]"),
		std::pair("A[BC]|D", "AB|D"),
		std::pair("A?", "A"),
		std::pair("(A|B)!A", "(A|B)!B"),
		std::pair("\\A\\9\\s!!\\A", "\\A\\9\\s!?\\A"),
		std::pair("\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)")})
	{
		NAXP_CHECK_EQUAL(first_divergent_by_enumeration(a, b), first_divergent(a, b));
	}
}

NAXP_TEST(first_divergent_swapping_the_arguments_keeps_the_value)
{
	for (const auto& [a, b] : {
		std::pair<std::string_view, std::string_view>("[AC]", "[ABC]"),
		std::pair<std::string_view, std::string_view>("A[BC]|D", "AB|D"),
		std::pair<std::string_view, std::string_view>(postcode, postcode_with_gir)})
	{
		NAXP_CHECK_EQUAL(first_divergent(a, b), first_divergent(b, a));
	}
}

// The C surface

NAXP_TEST(c_compare_and_first_divergent_value)
{
	::naxp* without = naxp_parse(postcode.data(), postcode.size(), nullptr);
	::naxp* with = naxp_parse(postcode_with_gir.data(), postcode_with_gir.size(), nullptr);

	NAXP_CHECK(without != nullptr && with != nullptr);

	::naxp_comparison comparison;

	NAXP_CHECK(naxp_compare(without, with, &comparison));
	NAXP_CHECK(comparison.accepted_text == NAXP_SUBSET_OF);
	NAXP_CHECK(comparison.encoding == NAXP_INCOMPARABLE);
	NAXP_CHECK(comparison.printed_text == NAXP_SUBSET_OF);

	NAXP_CHECK_EQUAL(uint64_t{405194401}, naxp_first_divergent_value(without, with));

	// The C enum is the C++ enum's values, which the façade relies on.
	static_assert(static_cast<int>(NAXP_INCOMPARABLE) == static_cast<int>(set_relationship::incomparable));
	static_assert(static_cast<int>(NAXP_EQUAL) == static_cast<int>(set_relationship::equal));
	static_assert(static_cast<int>(NAXP_SUBSET_OF) == static_cast<int>(set_relationship::subset_of));
	static_assert(static_cast<int>(NAXP_SUPERSET_OF) == static_cast<int>(set_relationship::superset_of));

	naxp_free(without);
	naxp_free(with);
}
