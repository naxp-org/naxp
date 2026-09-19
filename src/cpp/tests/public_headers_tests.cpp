// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The two public headers compile on their own, and the one function that exists already answers.

#include "check.hpp"

#include "naxp/naxp.h"
#include "naxp/naxp.hpp"

#include <cstring>
#include <string>

// Defined in c_header_tests.c, which is compiled as C.
extern "C" const char *naxp_tests_version_via_c(void);

NAXP_TEST(version_is_stated)
{
	const std::string version = naxp_version();

	NAXP_CHECK(!version.empty());
	NAXP_CHECK(version.find('.') != std::string::npos);
}

NAXP_TEST(c_header_was_seen_by_c)
{
	NAXP_CHECK(std::strcmp(naxp_version(), naxp_tests_version_via_c()) == 0);
}
