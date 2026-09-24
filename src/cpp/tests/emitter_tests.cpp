// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The four emitters. The tests over the text are few and shallow, because the test that
// matters is the last one: every fragment the JavaScript implementation emits for every
// conformance naxp, byte for byte. The C# and JavaScript emitters are already held identical to
// each other, so matching one is matching both.

#include "check.hpp"

#include "naxp/naxp.h"
#include "naxp/naxp.hpp"

#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>

// Defined in c_header_tests.c, which is compiled as C.
extern "C" size_t naxp_tests_emit_via_c(const char *pattern, char *destination, size_t capacity);

namespace
{
	std::string emit(std::string_view pattern, logmu::output_language language, std::string_view prefix = "", logmu::naxp_value_type value_type = logmu::naxp_value_type::uint64)
	{
		return logmu::naxp::parse(pattern).emit(language, prefix, value_type);
	}

	bool contains(const std::string& text, std::string_view needle)
	{
		return text.find(needle) != std::string::npos;
	}
}

NAXP_TEST(csharp_constants_carry_the_count_and_the_longest_string)
{
	const std::string source = emit("#[1-12]", logmu::output_language::csharp);

	NAXP_CHECK(contains(source, "public const ulong MaxEncodedValue = 12UL;"));
	NAXP_CHECK(contains(source, "public const int MaxLength = 2;"));
	NAXP_CHECK(contains(source, "public const string Pattern = @\"#[1-12]\";"));
}

NAXP_TEST(javascript_names_are_camel_cased_under_the_prefix)
{
	const std::string source = emit("#[1-12]", logmu::output_language::javascript, "Postcode");

	NAXP_CHECK(contains(source, "const postcodeMaxEncodedValue = 12;"));
	NAXP_CHECK(contains(source, "function postcodeAccepts(text) {"));
}

NAXP_TEST(c_names_are_snake_cased_at_case_boundaries)
{
	const std::string source = emit("#[1-12]", logmu::output_language::c, "UKPostcode");

	NAXP_CHECK(contains(source, "static const uint64_t uk_postcode_max_encoded_value = 12ULL;"));
	NAXP_CHECK(contains(source, "static inline bool uk_postcode_accepts_cstr(const char *text)"));
	NAXP_CHECK(contains(source, "static inline const char *uk_postcode_pattern(void)"));
	NAXP_CHECK(contains(source, "static inline bool uk_postcode_canonical_form_cstr(const char *text, char *destination, size_t capacity)"));
}

NAXP_TEST(cpp_fragment_is_inline_with_a_digit_separator)
{
	const std::string source = emit("\\9{19}", logmu::output_language::cpp);

	NAXP_CHECK(contains(source, "inline constexpr std::uint64_t max_encoded_value = 10'000'000'000'000'000'000ULL;"));
	NAXP_CHECK(contains(source, "inline bool accepts(std::string_view text)"));
	NAXP_CHECK(contains(source, "inline bool try_decode(std::uint64_t value, std::string& text)"));
	NAXP_CHECK(contains(source, "inline constexpr std::string_view pattern = R\"(\\9{19})\";"));
	NAXP_CHECK(contains(source, "inline bool try_canonical_form(std::string_view text, char* destination, std::size_t capacity, std::size_t& length)"));
}

NAXP_TEST(a_blank_prefix_gives_bare_names)
{
	NAXP_CHECK(contains(emit("A", logmu::output_language::csharp), "public static bool Accepts("));
	NAXP_CHECK(contains(emit("A", logmu::output_language::javascript), "function accepts(text) {"));
	NAXP_CHECK(contains(emit("A", logmu::output_language::c), "static inline bool accepts(const char *text, size_t text_length)"));
}

NAXP_TEST(a_machine_above_the_chunk_size_is_split_behind_a_dispatcher)
{
	const std::string source = emit("A{99}B{99}C{99}", logmu::output_language::csharp);

	NAXP_CHECK(contains(source, "static int AcceptStep0(int state, char c)"));
	NAXP_CHECK(contains(source, "static int AcceptStep1(int state, char c)"));
	NAXP_CHECK(contains(source, "if (state < 250) { return AcceptStep0(state, c); }"));
}

NAXP_TEST(emitting_the_same_naxp_twice_gives_the_same_text)
{
	NAXP_CHECK_EQUAL(emit("K9\\9 9K9", logmu::output_language::c), emit("K9\\9 9K9", logmu::output_language::c));
}

NAXP_TEST(the_formatting_arguments_shape_every_line)
{
	const std::string source = logmu::naxp::parse("A").emit(logmu::output_language::javascript, "", logmu::naxp_value_type::uint64, "  ", "\r\n", "    ");

	NAXP_CHECK(contains(source, "  function accepts(text) {\r\n      const bytes = typeof text !== 'string';\r\n"));
}

NAXP_TEST(a_prefix_that_is_not_an_identifier_is_refused)
{
	NAXP_CHECK_THROWS(std::invalid_argument, emit("A", logmu::output_language::csharp, "9lives"));
	NAXP_CHECK_THROWS(std::invalid_argument, emit("A", logmu::output_language::csharp, "no-hyphens"));
}

NAXP_TEST(a_value_type_the_naxp_outgrows_is_refused)
{
	NAXP_CHECK_THROWS(std::invalid_argument, emit("[A-Z]{2}", logmu::output_language::cpp, "", logmu::naxp_value_type::int8));
	NAXP_CHECK(contains(emit("[A-Z]{2}", logmu::output_language::cpp, "", logmu::naxp_value_type::uint16), "inline constexpr std::uint16_t max_encoded_value = 676;"));
}

NAXP_TEST(the_c_face_emits_the_same_fragment)
{
	const std::string expected = emit("[A-Z]{2}", logmu::output_language::c, "Pair");

	naxp *parsed = naxp_parse("[A-Z]{2}", 8, nullptr);
	size_t length = 0;
	char *fragment = naxp_emit(parsed, NAXP_C, "Pair", NAXP_UINT64, nullptr, nullptr, nullptr, &length);

	NAXP_CHECK(fragment != nullptr);
	NAXP_CHECK_EQUAL(expected.size(), length);
	NAXP_CHECK_EQUAL(expected, std::string(fragment, length));
	NAXP_CHECK_EQUAL(std::size_t{ 0 }, std::strlen(fragment) - length);

	naxp_string_free(fragment);

	char *refused = naxp_emit(parsed, NAXP_C, "9lives", NAXP_UINT64, nullptr, nullptr, nullptr, &length);

	NAXP_CHECK(refused == nullptr);
	NAXP_CHECK_EQUAL(std::size_t{ 0 }, length);

	naxp_free(parsed);
}

NAXP_TEST(the_c_face_is_callable_from_c)
{
	const std::string expected = emit("A", logmu::output_language::c);
	std::string buffer(expected.size() + 1, '\0');

	const size_t length = naxp_tests_emit_via_c("A", &buffer[0], buffer.size());

	NAXP_CHECK_EQUAL(expected.size(), length);
	NAXP_CHECK_EQUAL(expected, buffer.substr(0, length));
}

// The differential test. The file is written by conformance/emit/generate.js through the
// JavaScript implementation, and ctest runs this test with NAXP_EMIT_EXPECTED naming it; with
// nothing named there is nothing to compare, and the test says so and passes.

namespace
{
	std::string expected_file()
	{
#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable : 4996)
#endif
		const char* named = std::getenv("NAXP_EMIT_EXPECTED");
#ifdef _MSC_VER
#pragma warning(pop)
#endif

		return named == nullptr ? std::string() : std::string(named);
	}

	logmu::output_language language_named(const std::string& name)
	{
		if (name == "CSharp") { return logmu::output_language::csharp; }
		if (name == "JavaScript") { return logmu::output_language::javascript; }
		if (name == "C") { return logmu::output_language::c; }
		if (name == "Cpp") { return logmu::output_language::cpp; }

		throw std::runtime_error("Unknown output language " + name);
	}

	logmu::naxp_value_type value_type_named(const std::string& name)
	{
		if (name == "Int8") { return logmu::naxp_value_type::int8; }
		if (name == "UInt8") { return logmu::naxp_value_type::uint8; }
		if (name == "Int16") { return logmu::naxp_value_type::int16; }
		if (name == "UInt16") { return logmu::naxp_value_type::uint16; }
		if (name == "Int32") { return logmu::naxp_value_type::int32; }
		if (name == "UInt32") { return logmu::naxp_value_type::uint32; }
		if (name == "Int64") { return logmu::naxp_value_type::int64; }
		if (name == "UInt64") { return logmu::naxp_value_type::uint64; }

		throw std::runtime_error("Unknown value type " + name);
	}

	/// Where two texts first differ, as a line number and both lines, for a message.
	std::string first_difference(const std::string& expected, const std::string& actual)
	{
		std::size_t line = 1;
		std::size_t start = 0;

		for (std::size_t i = 0; i < expected.size() && i < actual.size(); ++i)
		{
			if (expected[i] != actual[i])
			{
				break;
			}

			if (expected[i] == '\n')
			{
				++line;
				start = i + 1;
			}
		}

		const auto line_of = [start](const std::string& text)
		{
			const std::size_t end = text.find('\n', start);

			return start >= text.size() ? std::string("<end>") : text.substr(start, end == std::string::npos ? std::string::npos : end - start);
		};

		return "line " + std::to_string(line) + ": expected '" + line_of(expected) + "' but was '" + line_of(actual) + "'";
	}
}

NAXP_TEST(emitted_fragments_match_the_javascript_implementation)
{
	const std::string name = expected_file();

	if (name.empty())
	{
		return;
	}

	std::ifstream file(name, std::ios::binary);

	if (!file)
	{
		throw std::runtime_error("The expected fragments could not be read from " + name + ".");
	}

	std::ostringstream buffer;
	buffer << file.rdbuf();
	const std::string text = buffer.str();

	std::size_t pos = 0;
	int compared = 0;

	while (pos < text.size())
	{
		// === <language> <valueType> <prefix> <bytes>
		const std::size_t header_end = text.find('\n', pos);
		std::istringstream header(text.substr(pos, header_end - pos));
		std::string marker;
		std::string language;
		std::string value_type;
		std::string prefix;
		std::size_t bytes = 0;

		header >> marker >> language >> value_type;

		// The prefix may be empty, in which case the byte count is the third field read.
		std::string third;
		header >> third;

		if (header >> bytes)
		{
			prefix = third;
		}
		else
		{
			bytes = std::stoul(third);
		}

		if (marker != "===")
		{
			throw std::runtime_error("The expected fragments are not in the shape generate.js writes.");
		}

		const std::size_t naxp_start = header_end + 1;
		const std::size_t naxp_end = text.find('\n', naxp_start);
		const std::string pattern = text.substr(naxp_start, naxp_end - naxp_start);
		const std::string expected = text.substr(naxp_end + 1, bytes);

		pos = naxp_end + 1 + bytes + 1;

		// The one record with its own formatting is the Narrow one; see generate.js.
		const std::string actual = prefix == "Narrow"
			? logmu::naxp::parse(pattern).emit(language_named(language), prefix, value_type_named(value_type), "    ", "\r\n", "  ")
			: logmu::naxp::parse(pattern).emit(language_named(language), prefix, value_type_named(value_type));

		if (actual != expected)
		{
			logmu::testing::fail(__FILE__, __LINE__, pattern + " in " + language + " with prefix '" + prefix + "': " + first_difference(expected, actual));
		}

		++compared;
	}

	NAXP_CHECK(compared > 0);
}
