// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The character set against a reference: a sorted vector of the characters it should hold.

#include "check.hpp"

#include "ascii_char_set.hpp"

#include <algorithm>
#include <functional>
#include <optional>
#include <random>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

using logmu::detail::ascii_char_set;

namespace
{
	constexpr int character_count = ascii_char_set::character_count;

	struct sample
	{
		ascii_char_set set;
		std::vector<char> reference;
	};

	sample make_sample(std::vector<char> characters)
	{
		std::sort(characters.begin(), characters.end());

		ascii_char_set set;

		for (const char c : characters)
		{
			set = set | ascii_char_set::single_character(c);
		}

		return sample{set, std::move(characters)};
	}

	std::vector<char> range_characters(int minimum, int maximum)
	{
		std::vector<char> characters;

		for (int c = minimum; c <= maximum; ++c)
		{
			characters.push_back(static_cast<char>(c));
		}

		return characters;
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

	std::vector<char> characters_of(ascii_char_set set)
	{
		return std::vector<char>(set.begin(), set.end());
	}

	std::vector<sample> sample_sets()
	{
		std::vector<sample> samples;

		samples.push_back(make_sample({}));
		samples.push_back(make_sample({static_cast<char>(0)}));
		samples.push_back(make_sample({static_cast<char>(63)}));
		samples.push_back(make_sample({static_cast<char>(64)}));
		samples.push_back(make_sample({static_cast<char>(127)}));
		samples.push_back(make_sample({static_cast<char>(63), static_cast<char>(64)}));
		samples.push_back(make_sample({static_cast<char>(0), static_cast<char>(127)}));
		samples.push_back(make_sample(range_characters(0, 127)));
		samples.push_back(make_sample(range_characters('0', '9')));
		samples.push_back(make_sample(range_characters('A', 'Z')));
		samples.push_back(make_sample(range_characters('a', 'z')));
		samples.push_back(make_sample({'a'}));
		samples.push_back(make_sample({'a', 'b'}));
		samples.push_back(make_sample({'a', 'b', 'c'}));
		samples.push_back(make_sample({'a', 'c'}));
		samples.push_back(make_sample({'b'}));

		// A fixed seed, so a failure can be reproduced.
		std::mt19937 random(20260810);
		std::uniform_int_distribution<int> quarter(0, 3);

		for (int i = 0; i < 200; ++i)
		{
			std::vector<char> characters;

			for (int c = 0; c < character_count; ++c)
			{
				if (quarter(random) == 0)
				{
					characters.push_back(static_cast<char>(c));
				}
			}

			samples.push_back(make_sample(std::move(characters)));
		}

		return samples;
	}

	std::vector<char> merged(const std::vector<char>& left, const std::vector<char>& right, bool (*keep)(bool in_left, bool in_right))
	{
		std::vector<char> result;

		for (int c = 0; c < character_count; ++c)
		{
			const char character = static_cast<char>(c);
			const bool in_left = std::find(left.begin(), left.end(), character) != left.end();
			const bool in_right = std::find(right.begin(), right.end(), character) != right.end();

			if (keep(in_left, in_right))
			{
				result.push_back(character);
			}
		}

		return result;
	}

	std::vector<char> set_union(const std::vector<char>& left, const std::vector<char>& right)
	{
		return merged(left, right, [](bool in_left, bool in_right) { return in_left || in_right; });
	}

	std::vector<char> set_intersection(const std::vector<char>& left, const std::vector<char>& right)
	{
		return merged(left, right, [](bool in_left, bool in_right) { return in_left && in_right; });
	}

	std::vector<char> set_difference(const std::vector<char>& left, const std::vector<char>& right)
	{
		return merged(left, right, [](bool in_left, bool in_right) { return in_left && !in_right; });
	}

	int sign(int value)
	{
		return value < 0 ? -1 : value > 0 ? 1 : 0;
	}
}

NAXP_TEST(character_range_matches_reference_for_every_range)
{
	for (int minimum = 0; minimum < character_count; ++minimum)
	{
		for (int maximum = minimum; maximum < character_count; ++maximum)
		{
			const ascii_char_set set = ascii_char_set::character_range(static_cast<char>(minimum), static_cast<char>(maximum));

			NAXP_CHECK_EQUAL((maximum - minimum) + 1, set.count());

			for (int c = 0; c < character_count; ++c)
			{
				NAXP_CHECK_EQUAL(c >= minimum && c <= maximum, set.contains(static_cast<char>(c)));
			}
		}
	}
}

NAXP_TEST(single_character_matches_reference_for_every_character)
{
	for (int i = 0; i < character_count; ++i)
	{
		const ascii_char_set set = ascii_char_set::single_character(static_cast<char>(i));

		NAXP_CHECK_EQUAL(1, set.count());
		NAXP_CHECK(set.single_character() == static_cast<char>(i));

		for (int c = 0; c < character_count; ++c)
		{
			NAXP_CHECK_EQUAL(c == i, set.contains(static_cast<char>(c)));
		}
	}
}

NAXP_TEST(construction_rejects_non_ascii)
{
	NAXP_CHECK_THROWS(std::out_of_range, ascii_char_set::single_character(static_cast<char>(128)));
	NAXP_CHECK_THROWS(std::out_of_range, ascii_char_set::character_range(static_cast<char>(128), static_cast<char>(129)));
	NAXP_CHECK_THROWS(std::out_of_range, ascii_char_set::character_range('A', static_cast<char>(200)));
}

NAXP_TEST(character_range_rejects_a_highest_first_range)
{
	// The bug fixed in NXOld: the parser must check this before calling in, and the contract
	// here is that it throws, so that a missing check cannot pass unnoticed.
	NAXP_CHECK_THROWS(std::out_of_range, ascii_char_set::character_range('E', 'A'));
}

NAXP_TEST(empty_has_no_characters)
{
	constexpr ascii_char_set empty;

	static_assert(empty.is_empty());
	static_assert(empty.count() == 0);
	static_assert(!empty.single_character().has_value());

	for (int c = 0; c < character_count; ++c)
	{
		NAXP_CHECK(!empty.contains(static_cast<char>(c)));
		NAXP_CHECK_EQUAL(-1, empty.index_of(static_cast<char>(c)));
	}
}

NAXP_TEST(non_ascii_character_is_never_contained)
{
	const ascii_char_set all = ascii_char_set::character_range(static_cast<char>(0), static_cast<char>(127));

	NAXP_CHECK(!all.contains(static_cast<char>(128)));
	NAXP_CHECK(!all.contains(static_cast<char>(255)));
	NAXP_CHECK_EQUAL(-1, all.index_of(static_cast<char>(128)));
}

NAXP_TEST(membership_matches_reference)
{
	for (const auto& [set, reference] : sample_sets())
	{
		NAXP_CHECK_EQUAL(static_cast<int>(reference.size()), set.count());
		NAXP_CHECK_EQUAL(reference.empty(), set.is_empty());
		NAXP_CHECK(set.single_character() == (reference.size() == 1 ? std::optional<char>(reference[0]) : std::nullopt));

		for (int c = 0; c < character_count; ++c)
		{
			const char character = static_cast<char>(c);
			const auto found = std::find(reference.begin(), reference.end(), character);
			const int reference_index = found == reference.end() ? -1 : static_cast<int>(found - reference.begin());

			NAXP_CHECK_EQUAL(found != reference.end(), set.contains(character));
			NAXP_CHECK_EQUAL(reference_index, set.index_of(character));
		}
	}
}

NAXP_TEST(character_at_inverts_index_of)
{
	for (const auto& [set, reference] : sample_sets())
	{
		for (int i = 0; i < static_cast<int>(reference.size()); ++i)
		{
			NAXP_CHECK_EQUAL(static_cast<int>(reference[static_cast<std::size_t>(i)]), static_cast<int>(set.character_at(i)));
			NAXP_CHECK_EQUAL(i, set.index_of(set.character_at(i)));
		}

		NAXP_CHECK_THROWS(std::out_of_range, set.character_at(static_cast<int>(reference.size())));
		NAXP_CHECK_THROWS(std::out_of_range, set.character_at(-1));
	}
}

NAXP_TEST(iterator_yields_characters_in_ascending_order)
{
	for (const auto& [set, reference] : sample_sets())
	{
		NAXP_CHECK(reference == characters_of(set));
	}
}

NAXP_TEST(iterator_is_usable_at_compile_time)
{
	// Static, so that the lambda can range over it without capturing it.
	static constexpr ascii_char_set digits = ascii_char_set::character_range('0', '9');
	constexpr int total = []
	{
		int sum = 0;

		for (const char c : digits)
		{
			sum += c - '0';
		}

		return sum;
	}();

	static_assert(total == 45);
}

NAXP_TEST(operators_match_reference)
{
	const std::vector<sample> samples = sample_sets();

	for (const auto& [left, left_reference] : samples)
	{
		for (const auto& [right, right_reference] : samples)
		{
			NAXP_CHECK(set_union(left_reference, right_reference) == characters_of(left | right));
			NAXP_CHECK(set_intersection(left_reference, right_reference) == characters_of(left & right));
			NAXP_CHECK(set_difference(left_reference, right_reference) == characters_of(left - right));

			NAXP_CHECK_EQUAL(!set_intersection(left_reference, right_reference).empty(), left.intersects_with(right));
		}
	}
}

NAXP_TEST(ordering_matches_ordinal_string_order)
{
	const std::vector<sample> samples = sample_sets();

	for (const auto& [left, left_reference] : samples)
	{
		for (const auto& [right, right_reference] : samples)
		{
			const std::string left_text(left_reference.begin(), left_reference.end());
			const std::string right_text(right_reference.begin(), right_reference.end());

			NAXP_CHECK_EQUAL(sign(left_text.compare(right_text)), sign(left.compare(right)));
		}
	}
}

NAXP_TEST(ordering_orders_the_documented_examples)
{
	// [a] < [ab] < [abc] < [ac] < [b] < [c] < [cd]
	const ascii_char_set ordered[] = {set_of("a"), set_of("ab"), set_of("abc"), set_of("ac"), set_of("b"), set_of("c"), set_of("cd")};

	for (std::size_t i = 0; i + 1 < std::size(ordered); ++i)
	{
		NAXP_CHECK(ordered[i] < ordered[i + 1]);
		NAXP_CHECK(ordered[i + 1] > ordered[i]);
		NAXP_CHECK(ordered[i] <= ordered[i]);
		NAXP_CHECK(ordered[i] >= ordered[i]);
	}
}

NAXP_TEST(equality_survives_a_different_route_to_the_same_set)
{
	const ascii_char_set by_range = ascii_char_set::character_range('0', '9');
	ascii_char_set by_union;

	for (char c = '0'; c <= '9'; ++c)
	{
		by_union = by_union | ascii_char_set::single_character(c);
	}

	NAXP_CHECK(by_range == by_union);
	NAXP_CHECK(!(by_range != by_union));
	NAXP_CHECK_EQUAL(std::hash<ascii_char_set>()(by_range), std::hash<ascii_char_set>()(by_union));
	NAXP_CHECK(by_range.compare(by_union) == 0);
}

NAXP_TEST(named_sets_hold_the_right_characters)
{
	using namespace logmu::detail;

	NAXP_CHECK(range_characters('0', '9') == characters_of(all_digits));
	NAXP_CHECK(range_characters('A', 'Z') == characters_of(all_upper_case_letters));
	NAXP_CHECK(range_characters('a', 'z') == characters_of(all_lower_case_letters));
	NAXP_CHECK(set_union(range_characters('0', '9'), range_characters('A', 'Z')) == characters_of(all_digits_and_upper_case_letters));

	static_assert(all_digits.count() == 10);
	static_assert(all_upper_case_letters.count() == 26);
	static_assert(all_lower_case_letters.count() == 26);
	static_assert(all_digits_and_upper_case_letters.count() == 36);
}
