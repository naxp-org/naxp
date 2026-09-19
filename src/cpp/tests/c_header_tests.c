/* Copyright (c) Tim Gordon.
   This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file. */

/* Compiled as C, so that the C header is known to be C and not merely C++ in disguise. */

#include "naxp/naxp.h"

#include <stddef.h>
#include <string.h>

const char *naxp_tests_version_via_c(void)
{
	return naxp_version();
}

/*
   Emits a naxp as C through the C face, copying the fragment into the caller's buffer, and
   returns its length; zero where anything failed or the buffer is too small.
*/
size_t naxp_tests_emit_via_c(const char *pattern, char *destination, size_t capacity)
{
	naxp *parsed = naxp_parse(pattern, strlen(pattern), NULL);
	size_t length = 0;
	char *fragment;

	if (parsed == NULL)
	{
		return 0;
	}

	fragment = naxp_emit(parsed, NAXP_C, "", NAXP_UINT64, "", "\n", "\t", &length);

	if (fragment != NULL && length < capacity)
	{
		memcpy(destination, fragment, length + 1);
	}
	else
	{
		length = 0;
	}

	naxp_string_free(fragment);
	naxp_free(parsed);

	return length;
}
