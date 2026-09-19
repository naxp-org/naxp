// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "naxp/naxp.h"

// The version comes from the CMake project, so that it is stated once.
#ifndef NAXP_VERSION
#error "NAXP_VERSION must be defined by the build."
#endif

extern "C" const char *naxp_version(void)
{
	return NAXP_VERSION;
}
