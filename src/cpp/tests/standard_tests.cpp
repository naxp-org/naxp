// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The standard naxps built into the library, against standards/naxp-standards.json. The table in
// src/standard_table.hpp is generated from that file; these tests prove the library answers as
// the file says, for every standard and variant.

#include "check.hpp"
#include "json.hpp"

#include "naxp/naxp.h"
#include "naxp/naxp.hpp"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

#ifndef NAXP_STANDARDS_FILE
#error "NAXP_STANDARDS_FILE must be defined by the build."
#endif

namespace
{
	const logmu::testing::json& standards_file()
	{
		static const logmu::testing::json data = logmu::testing::read_json_file(NAXP_STANDARDS_FILE);

		return data;
	}

	std::string upper_case(std::string_view text)
	{
		std::string result(text);

		for (auto& character : result)
		{
			if (character >= 'a' && character <= 'z')
			{
				character = static_cast<char>(character - 'a' + 'A');
			}
		}

		return result;
	}

	std::string message_of_standard_naxp(std::string_view id, std::string_view variant)
	{
		try
		{
			(void)logmu::naxp::standard_naxp(id, variant);
		}
		catch (const std::invalid_argument& error)
		{
			return error.what();
		}

		return std::string();
	}
}

NAXP_TEST(standard_naxps_list_the_file_row_for_row)
{
	const auto& rows = logmu::naxp::all_standard_naxps();
	std::size_t index = 0;

	NAXP_CHECK_EQUAL(std::string("naxp-standards/0.1"), standards_file()["format"].text());

	for (const auto& standard : standards_file()["standards"].items())
	{
		for (const auto& variant : standard["variants"].items())
		{
			NAXP_CHECK(index < rows.size());

			const logmu::standard_naxp_variant& row = rows[index++];

			NAXP_CHECK_EQUAL(standard["id"].text(), std::string(row.id));
			NAXP_CHECK_EQUAL(standard["name"].text(), std::string(row.name));
			NAXP_CHECK_EQUAL(variant["id"].text(), std::string(row.variant));
			NAXP_CHECK_EQUAL(variant["naxp"].text(), std::string(row.pattern));
			NAXP_CHECK_EQUAL(variant["notes"].text(), std::string(row.notes));
		}
	}

	NAXP_CHECK_EQUAL(rows.size(), index);
}

NAXP_TEST(every_standard_naxp_encodes_its_examples)
{
	for (const auto& standard : standards_file()["standards"].items())
	{
		for (const auto& variant : standard["variants"].items())
		{
			const logmu::naxp parsed = logmu::naxp::standard_naxp(standard["id"].text(), variant["id"].text());

			NAXP_CHECK_EQUAL(variant["naxp"].text(), std::string(parsed.pattern()));
			NAXP_CHECK_EQUAL(std::stoull(variant["maxEncodedValue"].text()), parsed.max_encoded_value());

			for (const auto& example : variant["valid"].items())
			{
				const std::uint64_t value = std::stoull(example["value"].text());

				NAXP_CHECK_EQUAL(value, parsed.encode(example["text"].text()));
				NAXP_CHECK(parsed.canonical_form(example["text"].text()) == std::optional<std::string>(example["canonical"].text()));
				NAXP_CHECK_EQUAL(example["canonical"].text(), parsed.decode(value));
			}

			for (const auto& example : variant["invalid"].items())
			{
				NAXP_CHECK_EQUAL(std::uint64_t{0}, parsed.encode(example.text()));
			}
		}
	}
}

NAXP_TEST(standard_naxp_matches_ids_without_regard_to_ascii_case)
{
	for (const auto& row : logmu::naxp::all_standard_naxps())
	{
		const std::optional<logmu::naxp> found = logmu::naxp::try_standard_naxp(upper_case(row.id), upper_case(row.variant));

		NAXP_CHECK(found.has_value());
		NAXP_CHECK_EQUAL(std::string(row.pattern), std::string(found->pattern()));
	}

	// A long s and a Kelvin sign fold to s and k outside ASCII, which must not make them letters
	// of an id.
	NAXP_CHECK(!logmu::naxp::try_standard_naxp("uk-po\xC5\xBFtcode").has_value());
	NAXP_CHECK(!logmu::naxp::try_standard_naxp("u\xE2\x84\xAA-postcode").has_value());
}

NAXP_TEST(standard_naxp_takes_an_empty_variant_as_the_default)
{
	const std::string expected(logmu::naxp::standard_naxp("uk-postcode", "default").pattern());

	NAXP_CHECK_EQUAL(expected, std::string(logmu::naxp::standard_naxp("uk-postcode").pattern()));
	NAXP_CHECK_EQUAL(expected, std::string(logmu::naxp::standard_naxp("uk-postcode", "").pattern()));
	NAXP_CHECK_EQUAL(expected, std::string(logmu::naxp::standard_naxp("uk-postcode", "DEFAULT").pattern()));
}

NAXP_TEST(standard_naxp_refuses_an_unknown_id_or_variant)
{
	NAXP_CHECK_THROWS(std::invalid_argument, logmu::naxp::standard_naxp("uk-postcod"));
	NAXP_CHECK_THROWS(std::invalid_argument, logmu::naxp::standard_naxp(""));
	NAXP_CHECK_THROWS(std::invalid_argument, logmu::naxp::standard_naxp("uk-postcode", "strict"));
	NAXP_CHECK(!logmu::naxp::try_standard_naxp("uk-postcod").has_value());
	NAXP_CHECK(!logmu::naxp::try_standard_naxp("uk-postcode", "strict").has_value());

	NAXP_CHECK_EQUAL(
		std::string("There is no standard naxp 'uk-postcod'. The standard naxps are uk-postcode, eircode, nl-postcode and flight-designator."),
		message_of_standard_naxp("uk-postcod", ""));

	NAXP_CHECK_EQUAL(
		std::string("The standard naxp 'uk-postcode' has no variant 'strict'. Its variants are default, padding-tolerant and padding-canonical."),
		message_of_standard_naxp("UK-Postcode", "strict"));
}

NAXP_TEST(c_standard_naxp_finds_and_refuses_as_the_class_does)
{
	::naxp* found = naxp_standard_naxp("NL-postcode", nullptr);

	NAXP_CHECK(found != nullptr);
	NAXP_CHECK_EQUAL(std::string(logmu::naxp::standard_naxp("nl-postcode").pattern()), std::string(naxp_pattern(found)));
	naxp_free(found);

	found = naxp_standard_naxp("nl-postcode", "");
	NAXP_CHECK(found != nullptr);
	naxp_free(found);

	found = naxp_standard_naxp("nl-postcode", "hyphen-tolerant");
	NAXP_CHECK(found != nullptr);
	NAXP_CHECK(naxp_accepts_cstr(found, "1012-AB"));
	naxp_free(found);

	NAXP_CHECK(naxp_standard_naxp("nl-postcode", "strict") == nullptr);
	NAXP_CHECK(naxp_standard_naxp("nowhere", nullptr) == nullptr);
	NAXP_CHECK(naxp_standard_naxp(nullptr, nullptr) == nullptr);
}
