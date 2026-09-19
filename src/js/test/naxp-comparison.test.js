// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCompile } from '../lib/compiler.js';
import { Naxp } from '../lib/naxp.js';
import { NaxpComparison } from '../lib/naxp-comparison.js';
import { tryCompareEncodings } from '../lib/relations.js';
import { SetRelationship } from '../lib/set-relationship.js';

const POSTCODE = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
const POSTCODE_SPACE_OPTIONAL = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
const POSTCODE_SPACE_REQUIRED = '\\A\\A?\\9\\X? \\s \\9\\A\\A';
const POSTCODE_FOLDED = '\\C(\\A\\A?\\9\\X? \\s!! \\9\\A\\A)';
const POSTCODE_WITH_GIR = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA';
const POSTCODE_TIGHT = '[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]';

// #region Helpers

/**
 * How one naxp stands to another, through the public surface.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {NaxpComparison} The comparison.
 */
function compare(a, b) {
	return Naxp.compare(Naxp.parse(a), Naxp.parse(b));
}

// #endregion
// #region The three axes

// The four rows the site quotes, in the order it quotes them, with the figures the site gives for
// each.
test('the site rows', () => {
	assert.deepEqual(
		compare(POSTCODE_SPACE_REQUIRED, POSTCODE_SPACE_OPTIONAL),
		new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.SubsetOf, SetRelationship.Equal));

	assert.deepEqual(
		compare(POSTCODE, POSTCODE_FOLDED),
		new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.SubsetOf, SetRelationship.Equal));

	assert.deepEqual(
		compare(POSTCODE, POSTCODE_WITH_GIR),
		new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.Incomparable, SetRelationship.SubsetOf));

	assert.deepEqual(
		compare(POSTCODE, POSTCODE_TIGHT),
		new NaxpComparison(SetRelationship.SupersetOf, SetRelationship.Incomparable, SetRelationship.SupersetOf));
});

test('a naxp against itself is equal on every axis', () => {
	assert.deepEqual(
		compare(POSTCODE, POSTCODE),
		new NaxpComparison(SetRelationship.Equal, SetRelationship.Equal, SetRelationship.Equal));
});

// The axes are independent. The same values with different printed text, and the same printed text
// with different values, are both possible, and each is a different kind of change to warn about.
// The last pair prints 'a' and 'c' either way and sends 'b' to a different one of them.
test('the axes can disagree', () => {
	const cases = [
		['(A|B)!A', '(A|B)!B', SetRelationship.Equal, SetRelationship.Equal, SetRelationship.Incomparable],
		['[\\s\\-]?!\\-', '[\\s\\-]!?', SetRelationship.Equal, SetRelationship.Equal, SetRelationship.Incomparable],
		['[ab]{2}', '([ab]{2})!(aa)', SetRelationship.Equal, SetRelationship.Incomparable, SetRelationship.SupersetOf],
		['(a|b)!a|c', 'a|(b|c)!c', SetRelationship.Equal, SetRelationship.Incomparable, SetRelationship.Equal],
	];

	for (const [a, b, acceptedText, encoding, printedText] of cases) {
		assert.deepEqual(
			compare(a, b),
			new NaxpComparison(acceptedText, encoding, printedText),
			`${a} against ${b}`);
	}
});

// The relationships are from a's point of view, so swapping the arguments swaps subset and
// superset on every axis and leaves the other two values alone.
test('swapping the arguments swaps subset and superset', () => {
	const forward = compare(POSTCODE, POSTCODE_WITH_GIR);
	const backward = compare(POSTCODE_WITH_GIR, POSTCODE);

	assert.equal(forward.acceptedText, SetRelationship.SubsetOf);
	assert.equal(backward.acceptedText, SetRelationship.SupersetOf);
	assert.equal(forward.encoding, SetRelationship.Incomparable);
	assert.equal(backward.encoding, SetRelationship.Incomparable);
	assert.equal(forward.printedText, SetRelationship.SubsetOf);
	assert.equal(backward.printedText, SetRelationship.SupersetOf);
});

// #endregion
// #region The type

test('a comparison built with no arguments claims nothing', () => {
	const comparison = new NaxpComparison();

	assert.equal(comparison.acceptedText, SetRelationship.Incomparable);
	assert.equal(comparison.encoding, SetRelationship.Incomparable);
	assert.equal(comparison.printedText, SetRelationship.Incomparable);
});

// The C# is a record struct and gets value equality from the language. Here the axes are strings
// on a frozen object, so two comparisons of the same axes are alike under a structural comparison
// and a caller reads an axis directly.
test('comparisons with the same axes are alike', () => {
	const left = new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.SubsetOf, SetRelationship.Equal);
	const right = new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.SubsetOf, SetRelationship.Equal);
	const other = new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.Incomparable, SetRelationship.Equal);

	assert.deepEqual(left, right);
	assert.notDeepEqual(left, other);
	assert.equal(left.encoding, right.encoding);
	assert.notEqual(left.encoding, other.encoding);
});

test('a comparison cannot be changed after it is built', () => {
	const comparison = compare(POSTCODE, POSTCODE_WITH_GIR);

	assert.throws(() => { comparison.encoding = SetRelationship.Equal; }, TypeError);
});

// #endregion
// #region Deciding, and not

test('tryCompare decides what compare decides', () => {
	const comparison = Naxp.tryCompare(Naxp.parse(POSTCODE), Naxp.parse(POSTCODE_WITH_GIR));

	assert.notEqual(comparison, null);
	assert.deepEqual(comparison, compare(POSTCODE, POSTCODE_WITH_GIR));
});

// Only the encoding axis can go undecided, when its walk outgrows the budget, and no naxp anybody
// has reason to write gets near it. The budget is there to be lowered by a test, which is how the
// undecided path is reached at all.
test('the undecided channel is null and throws', () => {
	const { compilation: a } = tryCompile(POSTCODE);
	const { compilation: b } = tryCompile(POSTCODE_WITH_GIR);

	assert.equal(tryCompareEncodings(a, b, 1).decided, false);

	assert.throws(
		() => Naxp.compare(Naxp.parse(POSTCODE), Naxp.parse(POSTCODE_WITH_GIR), 1),
		/budget/);

	assert.equal(Naxp.tryCompare(Naxp.parse(POSTCODE), Naxp.parse(POSTCODE_WITH_GIR), 1), null);
});

test('arguments that are not naxps are refused', () => {
	const naxp = Naxp.parse('A');

	assert.throws(() => Naxp.compare(null, naxp), TypeError);
	assert.throws(() => Naxp.compare(naxp, 'A'), TypeError);
	assert.throws(() => Naxp.tryCompare(undefined, naxp), TypeError);
	assert.throws(() => Naxp.firstDivergentValue(naxp, null), TypeError);
});

// #endregion
// #region The first divergent value

test('adding GIR diverges where the site says', () => {
	const without = Naxp.parse(POSTCODE);
	const withGir = Naxp.parse(POSTCODE_WITH_GIR);

	const value = Naxp.firstDivergentValue(without, withGir);

	assert.equal(value, 405194401n);
	assert.equal(without.decode(value), 'G0 0AA');
	assert.equal(withGir.decode(value), 'H0 0AA');
});

// Zero says every value both hold decodes alike. It says nothing about values only one holds,
// which the count of values covers.
test('safe edits have no divergent value', () => {
	const cases = [
		['[AB]', '[ABC]'],
		[POSTCODE_SPACE_REQUIRED, POSTCODE_SPACE_OPTIONAL],
		[POSTCODE, POSTCODE_FOLDED],
	];

	for (const [a, b] of cases) {
		assert.equal(
			Naxp.firstDivergentValue(Naxp.parse(a), Naxp.parse(b)),
			0n,
			`${a} against ${b}`);
	}
});

// Equal encodings and a divergent value are not a contradiction: the two questions are about text
// going in and text coming out.
test('equal encodings can still diverge on decoding', () => {
	const a = Naxp.parse('(A|B)!A');
	const b = Naxp.parse('(A|B)!B');

	assert.equal(Naxp.compare(a, b).encoding, SetRelationship.Equal);
	assert.equal(Naxp.firstDivergentValue(a, b), 1n);
	assert.equal(a.decode(1n), 'A');
	assert.equal(b.decode(1n), 'B');
});

// #endregion
