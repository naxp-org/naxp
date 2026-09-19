// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The amalgamation's headers are found flat, as a consumer who dropped the three files into a
// directory would include them, and the version naxp.cpp bakes in is the project's.

#include "check.hpp"

#include "naxp.h"
#include "naxp.hpp"

#include <cstring>

NAXP_TEST(amalgamation_states_the_project_version)
{
	NAXP_CHECK(std::strcmp(naxp_version(), NAXP_PROJECT_VERSION) == 0);
}

NAXP_TEST(amalgamation_answers_through_the_flat_header)
{
	const logmu::naxp postcode = logmu::naxp::parse("[A-Z]{2}[0-9]{2}");

	NAXP_CHECK(postcode.accepts("AB12"));
	NAXP_CHECK(!postcode.accepts("ab12"));
}
