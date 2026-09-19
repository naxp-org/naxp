// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * The figures W6 fixes, and W5's ceiling on an encoded value.
 *
 * W6 exists because W5 does not bound the work. W5 caps the largest encoded value, and a naxp can
 * be enormous while producing very few of them: `(A{99}){99}` is eleven characters of pattern
 * denoting a single string of 9 801 characters, whose minimal machine has 9 802 states and exactly
 * one encoded value.
 *
 * There is one number, with the others derived from it, so they cannot drift apart. A string of
 * *n* characters forces a machine of at least *n* + 1 states, so the longest string a naxp
 * satisfying W6 can generate is one shorter than the budget.
 *
 * These are figures of the language rather than of this implementation, which is why W6 states
 * them. Two implementations disagreeing here would disagree about which naxps are valid, and the
 * value of a naxp is that it means the same thing everywhere.
 */
const MAX_STATES = 2000;

export const NaxpLimits = Object.freeze({
	/**
	 * The most states a naxp's machine may have, which is W6.
	 *
	 * Nothing a naxp is for comes near this. The five examples on naxp.org are between seven and
	 * forty six states. What sets the floor is the grammar rather than any use: an interval count
	 * may have two digits, so the largest a single interval can expand to is `A{99}` at a hundred
	 * and one states. What W6 rules out is nesting, which multiplies.
	 */
	maxStates: MAX_STATES,

	/**
	 * The longest string a naxp satisfying W6 can generate.
	 *
	 * Derived rather than chosen, and a consequence of W6 rather than a rule beside it. A machine
	 * of `maxStates` states has a longest path of one fewer, so a naxp generating a longer string
	 * than this breaks the state budget in any case.
	 */
	maxStringLength: MAX_STATES - 1,

	/**
	 * The most states the canonicalisation machine may have, which W6 also fixes.
	 *
	 * Chosen, not derived, and the same figure as `maxStates` rather than a separate one. This
	 * machine grows on a different axis: its size can be exponential in the length of the naxp
	 * while both language machines stay tiny. See `encoding/transducer-determinisation.md`.
	 */
	maxCanonicalStates: MAX_STATES,

	/**
	 * The largest value the encoding can produce, which is W5's limit of 2^64 - 1.
	 *
	 * A BigInt, because no JavaScript number holds it. This is the one place the width of the
	 * encoding shows through, and it is why `encode` returns a BigInt whatever the naxp.
	 */
	maxEncodedValue: 18446744073709551615n,
});
