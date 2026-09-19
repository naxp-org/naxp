// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCompile } from '../lib/compiler.js';
import { Naxp } from '../lib/naxp.js';
import { Agreement, compare } from '../lib/rank-agreement.js';

// #region Helpers

/**
 * Compiles a naxp, failing the test where it is invalid.
 *
 * @param {string} pattern The pattern.
 * @returns {import('../lib/compiler.js').Compilation} The compilation.
 */
function compiled(pattern) {
	const { compilation, error } = tryCompile(pattern);

	assert.ok(compilation !== null, `${pattern}: ${error}`);

	return compilation;
}

/**
 * Whether two naxps give the same encoded value to the strings they share.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {string} One of {@link Agreement}.
 */
function ranks(a, b) {
	return compare(compiled(a).canonical, compiled(b).canonical);
}

/**
 * Every string the two share, checked one at a time through the public surface. The slow and
 * obvious way, so that the walk has something independent to answer to.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {string} One of {@link Agreement}.
 */
function ranksByEnumeration(a, b) {
	const left = Naxp.parse(a);
	const right = Naxp.parse(b);

	for (let value = 1n; value <= left.maxEncodedValue; ++value) {
		const text = left.decode(value);

		if (right.accepts(text) && right.encode(text) !== value) { return Agreement.Differs; }
	}

	return Agreement.Agrees;
}

// #endregion
// #region Agreement

test('the same canonical language agrees', () => {
	const cases = [
		['AB', 'AB'],
		['[AB]', 'A|B'],
		['A\\sB', 'A\\s!!B'],
		['\\A\\A', '\\C(\\A\\A)'],
	];

	for (const [a, b] of cases) {
		assert.equal(ranks(a, b), Agreement.Agrees, `${a} against ${b}`);
	}
});

// A shorter language whose strings keep their places agrees, because agreement is only ever asked
// of the strings the two share.
test('adding after everything else agrees', () => {
	const cases = [
		['[AB]', '[ABC]'],
		['AB', 'AB|AC'],
	];

	for (const [a, b] of cases) {
		assert.equal(ranks(a, b), Agreement.Agrees, `${a} against ${b}`);
	}
});

// #endregion
// #region Disagreement

// A string inserted among the others pushes everything after it along by one.
test('adding in the middle differs', () => {
	const cases = [
		['[AC]', '[ABC]'],
		['AC', 'AB|AC'],
	];

	for (const [a, b] of cases) {
		assert.equal(ranks(a, b), Agreement.Differs, `${a} against ${b}`);
	}
});

// The site's own case. GIR 0AA takes the highest value of all and still moves the codes beneath
// it, because it splits G out of its first class.
test('adding GIR to the postcode differs', () => {
	const without = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const withGir = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA';

	assert.equal(ranks(without, withGir), Agreement.Differs);
});

// Restricting the letters to those the Royal Mail guide lists disagrees almost at once.
test('tightening the postcode letters differs', () => {
	const loose = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const tight = '[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]';

	assert.equal(ranks(loose, tight), Agreement.Differs);
});

// #endregion
// #region Against the slow way

// The walk and an enumeration of every shared string must reach the same verdict. The naxps here
// are small enough to enumerate, which is the only reason this can be asked.
test('the walk agrees with enumeration', () => {
	const cases = [
		['[AB]', '[ABC]'],
		['[AC]', '[ABC]'],
		['[BC]', '[ABC]'],
		['AB|AC', 'AB|AC|AD'],
		['AB|AD', 'AB|AC|AD'],
		['\\A\\9', '\\A\\X'],
		['\\9\\A', '\\X\\A'],
		['A|BB', 'A|BB|C'],
		['A|BB', 'A|AB|BB'],
		['\\A{2}', '\\A{2,3}'],
		['\\A{2,3}', '\\A{2}'],
		['#[00-99]', '#[00-99]|AA'],
	];

	for (const [a, b] of cases) {
		assert.equal(ranks(a, b), ranksByEnumeration(a, b), `${a} against ${b}`);
	}
});

// #endregion
// #region Direction

// The question is symmetric: it asks about the strings both hold, and neither side is privileged.
test('swapping the arguments keeps the verdict', () => {
	const cases = [
		['[AC]', '[ABC]'],
		['[AB]', '[ABC]'],
	];

	for (const [a, b] of cases) {
		assert.equal(ranks(a, b), ranks(b, a), `${a} against ${b}`);
	}
});

// #endregion
// #region The budget

// A walk given no room says so rather than guessing.
test('a budget too small is undecided', () => {
	const a = compiled('\\A\\A?\\9\\X? \\s!! \\9\\A\\A').canonical;
	const b = compiled('\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA').canonical;

	assert.equal(compare(a, b, 2), Agreement.Undecided);
});

// The guard against a saturated count cannot be reached through a compiled naxp, because W5
// refuses any naxp holding more than 2^64 - 1 values in the first place. The largest one there is
// comes nowhere near saturating, and this says so, so the guard is read as defensive rather than
// as something a caller can trip.
test('a valid naxp never has a saturated count', () => {
	const biggest = compiled('\\9{19}');

	assert.equal(biggest.canonical.countSaturated, false);
	assert.equal(biggest.maxEncodedValue, 10000000000000000000n);
	assert.equal(compare(biggest.canonical, biggest.canonical), Agreement.Agrees);
});

// #endregion
