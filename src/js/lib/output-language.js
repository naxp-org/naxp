// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * The language a naxp is emitted as source in.
 *
 * Named for what it is rather than called a language on its own, because in a naxp's own
 * vocabulary a language is a set of strings: the accepted language *L* and the canonical
 * language *C*. This is the other sense.
 *
 * @enum {string}
 */
export const OutputLanguage = Object.freeze({
	/** C#, as a fragment of static members. */
	CSharp: 'CSharp',

	/** JavaScript, as a fragment of const and function declarations. */
	JavaScript: 'JavaScript',

	/** C, C99, as a fragment of static functions. */
	C: 'C',

	/** C++, C++17, as a fragment of inline functions that can sit in a header. */
	Cpp: 'Cpp',
});
