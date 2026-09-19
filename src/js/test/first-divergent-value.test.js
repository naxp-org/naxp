// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCompile } from '../lib/compiler.js';
import { Naxp } from '../lib/naxp.js';
import { firstDivergentValue, tryCompareEncodings } from '../lib/relations.js';
import { SetRelationship } from '../lib/set-relationship.js';

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
 * The lowest value two naxps both hold and decode differently.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {bigint} The value, or zero for none.
 */
function firstDivergent(a, b) {
	return firstDivergentValue(compiled(a).canonical, compiled(b).canonical);
}

/**
 * Every value both hold, decoded one at a time through the public surface. The slow and obvious
 * way, so that the walk has something independent to answer to.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {bigint} The value, or zero for none.
 */
function firstDivergentByEnumeration(a, b) {
	const left = Naxp.parse(a);
	const right = Naxp.parse(b);
	const shared = left.maxEncodedValue < right.maxEncodedValue
		? left.maxEncodedValue
		: right.maxEncodedValue;

	assert.ok(shared <= 200000n, 'Too many values to enumerate.');

	for (let value = 1n; value <= shared; ++value) {
		if (left.decode(value) !== right.decode(value)) { return value; }
	}

	return 0n;
}

// #endregion
// #region No divergence

test('the same canonical language is zero', () => {
	const cases = [
		['AB', 'AB'],
		['[AB]', 'A|B'],
		['\\A\\A', '\\C(\\A\\A)'],
		['A\\sB', 'A\\s!!B'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '\\A\\A?\\9\\X? \\s!! \\9\\A\\A'],
	];

	for (const [a, b] of cases) {
		assert.equal(firstDivergent(a, b), 0n, `${a} against ${b}`);
	}
});

// Values added after every existing one leave the existing ones decoding as before, so there is
// no divergence to report, however many were added.
test('values added after everything else is zero', () => {
	const cases = [
		['[AB]', '[ABC]'],
		['AB', 'AB|AC'],
		['AB', 'AB|ABC'],
		['A', 'A|B{2,9}'],
	];

	for (const [a, b] of cases) {
		assert.equal(firstDivergent(a, b), 0n, `${a} against ${b}`);
		assert.equal(firstDivergent(b, a), 0n, `${b} against ${a}`);
	}
});

// #endregion
// #region Divergence

// A string inserted among the others pushes everything after it along by one, so the first value
// at or after the insertion decodes differently.
test('values moved is the first moved', () => {
	const cases = [
		['[AC]', '[ABC]', 2n],
		['AC', 'AB|AC', 1n],
		['A|AB', 'A|AC', 2n],
	];

	for (const [a, b, expected] of cases) {
		assert.equal(firstDivergent(a, b), expected, `${a} against ${b}`);
		assert.equal(firstDivergent(b, a), expected, `${b} against ${a}`);
	}
});

// The empty string comes first in the order, so a naxp that accepts it and one that does not
// diverge at value 1.
test('one accepting the empty string is one', () => {
	assert.equal(firstDivergent('A?', 'A'), 1n);
	assert.equal(firstDivergent('A', 'A?'), 1n);
});

// Where one side's continuation runs out inside a chunk the other side's continues, the next value
// on the longer side is compared against the next chunk on the shorter one.
test('one continuation running out first is the value after it', () => {
	assert.equal(firstDivergent('A[BC]|D', 'AB|D'), 2n);
	assert.equal(firstDivergent('AB|D', 'A[BC]|D'), 2n);
});

// Two naxps that value every string alike can still decode every value differently, which is the
// reason this is a separate question from the encoding relation.
test('the same values with different text is one', () => {
	const cases = [
		['(A|B)!A', '(A|B)!B'],
		['[\\s\\-]?!\\-', '[\\s\\-]!?'],
		['\\C(\\A\\9\\s!!\\A)', '\\c(\\A\\9\\s!!\\A)'],
	];

	for (const [a, b] of cases) {
		const { decided, relationship } = tryCompareEncodings(compiled(a), compiled(b));

		assert.ok(decided, `${a} against ${b} was not decided.`);
		assert.equal(relationship, SetRelationship.Equal, `${a} against ${b}`);

		assert.equal(firstDivergent(a, b), 1n, `${a} against ${b}`);
	}
});

// #endregion
// #region The postcode cases the site quotes

// The site's figure: FZ9Z 9ZZ agrees at 405 194 400, and the next value is G0 0AA under one and
// H0 0AA under the other.
test('adding GIR to the postcode diverges where the site says', () => {
	const without = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const withGir = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA';

	const value = firstDivergent(without, withGir);

	assert.equal(value, 405194401n);
	assert.equal(Naxp.parse(without).decode(value), 'G0 0AA');
	assert.equal(Naxp.parse(withGir).decode(value), 'H0 0AA');
	assert.equal(Naxp.parse(without).decode(value - 1n), 'FZ9Z 9ZZ');
	assert.equal(Naxp.parse(withGir).decode(value - 1n), 'FZ9Z 9ZZ');
});

// Restricting the letters to those the Royal Mail guide lists diverges almost at once.
test('tightening the postcode letters diverges at three', () => {
	const loose = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const tight = '[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]';

	const value = firstDivergent(loose, tight);

	assert.equal(value, 3n);
	assert.notEqual(Naxp.parse(loose).decode(value), Naxp.parse(tight).decode(value));
	assert.equal(Naxp.parse(loose).decode(value - 1n), Naxp.parse(tight).decode(value - 1n));
});

// #endregion
// #region Against the slow way

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
		['#[0-105]', '#[0?0!0-105]'],
		['A[BC]|D', 'AB|D'],
		['A?', 'A'],
		['(A|B)!A', '(A|B)!B'],
		['\\A\\9\\s!!\\A', '\\A\\9\\s!?\\A'],
		['\\C(\\A\\9\\s!!\\A)', '\\c(\\A\\9\\s!!\\A)'],
	];

	for (const [a, b] of cases) {
		assert.equal(firstDivergent(a, b), firstDivergentByEnumeration(a, b), `${a} against ${b}`);
	}
});

// #endregion
// #region Direction

// The question is symmetric. It asks about the values both hold, and neither side is privileged.
test('swapping the arguments keeps the value', () => {
	const cases = [
		['[AC]', '[ABC]'],
		['A[BC]|D', 'AB|D'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA'],
	];

	for (const [a, b] of cases) {
		assert.equal(firstDivergent(a, b), firstDivergent(b, a), `${a} against ${b}`);
	}
});

// #endregion
