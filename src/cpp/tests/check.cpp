// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "check.hpp"

#include <cstdlib>
#include <exception>
#include <iostream>
#include <string>
#include <utility>
#include <vector>

namespace logmu::testing
{
	namespace
	{
		struct test
		{
			std::string name;
			void (*body)();
		};

		// A function-local static, so that registrations from other translation units find it
		// built whatever order the static initialisers run in.
		std::vector<test>& tests()
		{
			static std::vector<test> all;

			return all;
		}
	}

	registration::registration(std::string_view name, void (*body)())
	{
		tests().push_back(test{std::string(name), body});
	}

	void fail(const char* file, int line, std::string_view message)
	{
		throw check_failure(std::string(file) + "(" + std::to_string(line) + "): " + std::string(message));
	}
}

int main(int argc, char** argv)
{
	// An argument names a substring; only tests whose names contain it run.
	const std::string filter = argc > 1 ? argv[1] : "";
	int passed = 0;
	int failed = 0;

	for (const auto& test : logmu::testing::tests())
	{
		if (test.name.find(filter) == std::string::npos)
		{
			continue;
		}

		try
		{
			test.body();
			++passed;
		}
		catch (const std::exception& exception)
		{
			++failed;
			std::cout << "FAILED " << test.name << "\n    " << exception.what() << "\n";
		}
	}

	std::cout << passed << " passed, " << failed << " failed\n";

	return failed == 0 ? EXIT_SUCCESS : EXIT_FAILURE;
}
