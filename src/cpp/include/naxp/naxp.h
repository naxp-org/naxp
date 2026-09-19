/* Copyright (c) Tim Gordon.
   This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file. */

/*
   The C face of naxp, over the C++ library.

   Everything here is a plain function over two opaque types, so that any language with a C
   foreign function interface can bind it. No exception crosses this boundary: a failure is a
   null pointer or a false return, and a fault is handed back as an object to be read and
   then freed.

   Text is always a pointer and a length, never a NUL-terminated string, because a naxp may
   accept text holding any ASCII character. Where a caller does hold a C string, strlen gives
   the length.
*/

#ifndef NAXP_NAXP_H
#define NAXP_NAXP_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* A parsed naxp. Immutable once parsed, so one may be used from several threads at once. */
typedef struct naxp naxp;

/* A fault found in a pattern: what is wrong, where, and a stable code naming it. */
typedef struct naxp_fault naxp_fault;

/*
   Parses a naxp.

   pattern, pattern_length: the pattern of the naxp.
   fault: where to put the fault where the pattern is invalid, or NULL where the caller only
       wants to know whether it parsed. Set to NULL where it did. The caller frees it with
       naxp_fault_free.

   Returns the naxp, which the caller frees with naxp_free, or NULL where the pattern is not a
   well-formed naxp. NULL with *fault also NULL means memory ran out.
*/
naxp *naxp_parse(const char *pattern, size_t pattern_length, naxp_fault **fault);

/* Frees a naxp. NULL is allowed and does nothing. */
void naxp_free(naxp *expression);

/* The stable identifier for a fault, such as "NAXP1002". Valid until the fault is freed. */
const char *naxp_fault_code(const naxp_fault *fault);

/* What is wrong, and where practical what to write instead. Valid until the fault is freed. */
const char *naxp_fault_message(const naxp_fault *fault);

/* Where the fault starts, in characters from the start of the pattern. */
size_t naxp_fault_offset(const naxp_fault *fault);

/*
   How much of the pattern is at fault, which is the whole of it where the fault belongs to
   the naxp rather than to any one place in it.
*/
size_t naxp_fault_length(const naxp_fault *fault);

/* Frees a fault. NULL is allowed and does nothing. */
void naxp_fault_free(naxp_fault *fault);

/*
   The pattern a naxp was parsed from. The pointer is valid until the naxp is freed, and the
   pattern is NUL-terminated as well as measured, so length may be NULL.
*/
const char *naxp_pattern(const naxp *expression, size_t *length);

/*
   The largest encoded value a naxp produces. Encoded values run from 1 with no gaps, so this
   is also how many there are.
*/
uint64_t naxp_max_encoded_value(const naxp *expression);

/*
   The length of the longest text a naxp decodes a value to. A buffer this long takes anything
   naxp_decode or naxp_canonical_form writes.
*/
size_t naxp_max_length(const naxp *expression);

/* Whether a naxp accepts text. A byte outside ASCII is never accepted. */
bool naxp_accepts(const naxp *expression, const char *text, size_t text_length);

/*
   The encoded value of text: from 1 to naxp_max_encoded_value, or zero where the text is
   invalid for the naxp.
*/
uint64_t naxp_encode(const naxp *expression, const char *text, size_t text_length);

/*
   Writes the text an encoded value stands for, which is in canonical form.

   destination, capacity: where the text goes and how many bytes there are room for. No NUL
       is written; naxp_max_length bounds what is needed.
   length: how many bytes were written, or zero where none were. May be NULL.

   Returns false where the value is not one the naxp produces, or the destination is too
   short, in which case nothing is written.
*/
bool naxp_decode(const naxp *expression, uint64_t encoded_value, char *destination, size_t capacity, size_t *length);

/*
   Writes the canonical form of text, which is the text naxp_decode gives back for its encoded
   value. The parameters are those of naxp_decode.

   Returns false where the text is invalid for the naxp, or the destination is too short, in
   which case nothing is written.
*/
bool naxp_canonical_form(const naxp *expression, const char *text, size_t text_length, char *destination, size_t capacity, size_t *length);

/* How one set stands to another, for two sets called a and b. */
typedef enum naxp_set_relationship
{
	/* Neither set contains the other. The default, which claims nothing. */
	NAXP_INCOMPARABLE = 0,

	/* The two sets have exactly the same members. */
	NAXP_EQUAL = 1,

	/* Every member of a is a member of b, and b has at least one more. */
	NAXP_SUBSET_OF = 2,

	/* Every member of b is a member of a, and a has at least one more. */
	NAXP_SUPERSET_OF = 3
} naxp_set_relationship;

/*
   How one naxp stands to another, as three set relationships, each from the first naxp's point
   of view: the strings each accepts, the (text, value) pairs each defines, and the strings each
   prints. A change is safe for stored values exactly when encoding is NAXP_EQUAL or
   NAXP_SUBSET_OF.
*/
typedef struct naxp_comparison
{
	naxp_set_relationship accepted_text;
	naxp_set_relationship encoding;
	naxp_set_relationship printed_text;
} naxp_comparison;

/*
   Finds how b stands to a: what happens to text and values held under a if b replaces it.

   Returns whether the comparison was decided, in which case *comparison holds it. Only the
   encoding relationship can fail to be decided, when the walk that decides it outgrows its
   budget; *comparison is then NAXP_INCOMPARABLE on every axis.
*/
bool naxp_compare(const naxp *a, const naxp *b, naxp_comparison *comparison);

/*
   The lowest value both naxps hold that they decode to different strings, or zero where every
   value both hold decodes alike. Values only one naxp holds do not count.
*/
uint64_t naxp_first_divergent_value(const naxp *a, const naxp *b);

/* The language a naxp is emitted as source in. */
typedef enum naxp_output_language
{
	/* C#, as a fragment of static members. */
	NAXP_CSHARP = 0,

	/* JavaScript, as a fragment of const and function declarations. */
	NAXP_JAVASCRIPT = 1,

	/* C, C99, as a fragment of static functions. */
	NAXP_C = 2,

	/* C++, C++17, as a fragment of inline functions that can sit in a header. */
	NAXP_CPP = 3
} naxp_output_language;

/*
   The integer type generated code uses for encoded values. Each holds every encoded value up
   to its own largest, which naxp_emit checks the naxp against.
*/
typedef enum naxp_value_type
{
	NAXP_INT8 = 0,
	NAXP_UINT8 = 1,
	NAXP_INT16 = 2,
	NAXP_UINT16 = 3,
	NAXP_INT32 = 4,
	NAXP_UINT32 = 5,
	NAXP_INT64 = 6,

	/* Holds every encoded value any naxp produces. */
	NAXP_UINT64 = 7
} naxp_value_type;

/*
   Emits a naxp as source in a language: a fragment of the declarations answering the same
   questions this library does, for that one naxp, calling back into nothing. The header
   comment, includes, namespace or wrapping class around it are the caller's.

   prefix: what every generated name starts with, so several naxps can share one scope. NULL
       or empty gives the bare names. Each language applies its own casing convention to it.
   value_type: the integer type the generated code uses for encoded values, which
       naxp_max_encoded_value must fit.
   initial_indent, new_line, indent: what every line starts with ahead of its own depth, what
       ends every line, and what one level of indentation is. NULL means none, "\n" and "\t".
   length: the fragment's length in bytes, or zero where there is none. May be NULL.

   Returns the fragment, NUL-terminated, which the caller frees with naxp_string_free, or NULL
   where the language is not an output language, the prefix is neither empty nor an ASCII
   identifier, the largest encoded value does not fit the value type, or memory ran out.
*/
char *naxp_emit(
	const naxp *expression,
	naxp_output_language language,
	const char *prefix,
	naxp_value_type value_type,
	const char *initial_indent,
	const char *new_line,
	const char *indent,
	size_t *length);

/* Frees a string naxp_emit returned. NULL is allowed and does nothing. */
void naxp_string_free(char *text);

/* The version of the library, such as "0.10.0". */
const char *naxp_version(void);

#ifdef __cplusplus
}
#endif

#endif
