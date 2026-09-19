// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The whole test framework. A test is a function registered under a name; a check is a
// condition that throws on failure with the file and line; the runner in check.cpp runs every
// test, prints each failure, and exits non-zero if there were any. That is all a library of
// this size needs, and it keeps the build free of downloads.

#ifndef NAXP_TESTS_CHECK_HPP
#define NAXP_TESTS_CHECK_HPP

#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>

namespace logmu::testing
{
	/// A failed check, carrying where it failed.
	class check_failure : public std::runtime_error
	{
	public:
		using std::runtime_error::runtime_error;
	};

	/// Registers a test at start-up. Declared through the NAXP_TEST macro rather than directly.
	struct registration
	{
		registration(std::string_view name, void (*body)());
	};

	[[noreturn]] void fail(const char* file, int line, std::string_view message);

	/// The one rendering of a value a failure message has, so that anything streamable can be
	/// checked for equality.
	template <typename Value>
	std::string show(const Value& value)
	{
		std::ostringstream out;
		out << value;

		return out.str();
	}

	template <typename Left, typename Right>
	void check_equal(const char* file, int line, const Left& expected, const Right& actual, const char* expression)
	{
		if (!(expected == actual))
		{
			fail(file, line, std::string(expression) + ": expected " + show(expected) + " but was " + show(actual));
		}
	}
}

#define NAXP_TEST(name) \
	static void name(); \
	static const ::logmu::testing::registration name##_registration(#name, &name); \
	static void name()

#define NAXP_CHECK(condition) \
	do { if (!(condition)) { ::logmu::testing::fail(__FILE__, __LINE__, #condition); } } while (false)

#define NAXP_CHECK_EQUAL(expected, actual) \
	::logmu::testing::check_equal(__FILE__, __LINE__, (expected), (actual), #actual)

#define NAXP_CHECK_THROWS(exception, expression) \
	do \
	{ \
		bool naxp_thrown = false; \
		try { (void)(expression); } \
		catch (const exception&) { naxp_thrown = true; } \
		if (!naxp_thrown) { ::logmu::testing::fail(__FILE__, __LINE__, #expression " did not throw " #exception); } \
	} while (false)

#endif
