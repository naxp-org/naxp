// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCompile } from '../lib/compiler.js';
import { Agreement, compare as compareRanks } from '../lib/rank-agreement.js';
import { compareLanguages, tryCompareEncodings } from '../lib/relations.js';
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
 * How the encoding of one naxp stands to another's, failing the test where it was not decided.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {string} The relationship, as one of {@link SetRelationship}.
 */
function encoding(a, b) {
	const { decided, relationship } = tryCompareEncodings(compiled(a), compiled(b));

	assert.ok(decided, `${a} against ${b} was not decided.`);

	return relationship;
}

/**
 * How the accepted language of one naxp stands to another's.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {string} The relationship, as one of {@link SetRelationship}.
 */
function accepted(a, b) {
	return compareLanguages(compiled(a).accepted, compiled(b).accepted);
}

/**
 * How the canonical language of one naxp stands to another's.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {string} The relationship, as one of {@link SetRelationship}.
 */
function canonical(a, b) {
	return compareLanguages(compiled(a).canonical, compiled(b).canonical);
}

// #endregion
// #region Without unified elements it is the language relation, refined

// Where every shared string keeps its value, the encoding stands as the accepted language does,
// because the graph of a function is contained in another's exactly when the domain is and the
// two agree on it.
test('values kept follows the language', () => {
	const cases = [
		['AB', 'AB', SetRelationship.Equal],
		['[AB]', 'A|B', SetRelationship.Equal],
		['[AB]', '[ABC]', SetRelationship.SubsetOf],
		['[ABC]', '[AB]', SetRelationship.SupersetOf],
		['AB', 'AB|AC', SetRelationship.SubsetOf],
	];

	for (const [a, b, expected] of cases) {
		assert.equal(encoding(a, b), expected, `${a} against ${b}`);
	}
});

// Where a shared string changes value the graphs are incomparable whatever the languages do,
// since each holds a pair the other lacks.
test('values moved is incomparable', () => {
	const cases = [
		['[AC]', '[ABC]'],
		['[ABC]', '[AC]'],
		['AC', 'AB|AC'],
	];

	for (const [a, b] of cases) {
		assert.notEqual(accepted(a, b), SetRelationship.Incomparable, `${a} against ${b}`);
		assert.equal(encoding(a, b), SetRelationship.Incomparable, `${a} against ${b}`);
	}
});

// Two naxps sharing no string at all, or each holding strings the other lacks, are incomparable
// before any value is looked at.
test('incomparable languages are incomparable encodings', () => {
	const cases = [
		['AB', 'CD'],
		['[AB]', '[BC]'],
	];

	for (const [a, b] of cases) {
		assert.equal(encoding(a, b), SetRelationship.Incomparable, `${a} against ${b}`);
	}
});

// #endregion
// #region Canonical forms are not values

// The encoding is about values, and two naxps can print a string differently while giving it the
// same value. The first pair is the specification's own example under "Values", the last is a naxp
// folded the wrong way. On all of them the encoding is Equal and the printed text is Incomparable,
// and a comparison that confused the two axes would tell somebody their stored values were broken
// when they were not.
test('the same values with different text is equal', () => {
	const cases = [
		['[\\s\\-]?!\\-', '[\\s\\-]!?'],
		['(A|B)!A', '(A|B)!B'],
		['(A|B)!A|C', '(A|B)!B|C'],
		['\\C(\\A\\9\\s!!\\A)', '\\c(\\A\\9\\s!!\\A)'],
	];

	for (const [a, b] of cases) {
		assert.equal(encoding(a, b), SetRelationship.Equal, `${a} against ${b}`);
		assert.equal(canonical(a, b), SetRelationship.Incomparable, `${a} against ${b}`);
	}
});

// And the reverse: the same canonical strings at the same ranks, with a unified element sending
// the other strings elsewhere.
test('the same canonical ranks with different values is incomparable', () => {
	assert.equal(accepted('[ab]{2}', '([ab]{2})!(aa)'), SetRelationship.Equal);
	assert.equal(encoding('[ab]{2}', '([ab]{2})!(aa)'), SetRelationship.Incomparable);
});

// #endregion
// #region The edits the site quotes

// A safe edit accepts more and keeps every value: a space made optional, a case fold added, a
// unified element introduced.
test('safe edits are a subset', () => {
	const cases = [
		['A', '(A|a)!A'],
		['A\\sB', 'A\\s!!B'],
		['\\A\\A?\\9\\X? \\s \\9\\A\\A', '\\A\\A?\\9\\X? \\s!! \\9\\A\\A'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '\\C(\\A\\A?\\9\\X? \\s!! \\9\\A\\A)'],
	];

	for (const [a, b] of cases) {
		assert.equal(encoding(a, b), SetRelationship.SubsetOf, `${a} against ${b}`);
	}
});

// The two breaking rows: GIR 0AA added, and the letters tightened to the Royal Mail guide. Both
// are decided from the canonical machines alone, as the site's figures were.
test('breaking edits are incomparable', () => {
	const postcode = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const withGir = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA';
	const tight = '[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]';

	assert.equal(encoding(postcode, withGir), SetRelationship.Incomparable);
	assert.equal(encoding(postcode, tight), SetRelationship.Incomparable);
});

// Dropping the space rather than reproducing it keeps the accepted language and changes the value
// of every string with a space in it, which only the walk over parses can see.
test('dropping the space is incomparable', () => {
	const reproduced = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const dropped = '\\A\\A?\\9\\X? \\s!? \\9\\A\\A';

	assert.equal(accepted(reproduced, dropped), SetRelationship.Equal);
	assert.equal(
		compareRanks(compiled(reproduced).canonical, compiled(dropped).canonical),
		Agreement.Agrees);
	assert.equal(encoding(reproduced, dropped), SetRelationship.Incomparable);
});

// #endregion
// #region The encoding is the strongest axis

// Equal or SubsetOf on the encoding forces the same on the accepted text, because the graph's
// domain is the accepted language. The converse does not hold, which the cases above show.
test('the encoding implies the accepted text', () => {
	const cases = [
		['AB', 'AB'],
		['[AB]', '[ABC]'],
		['[AC]', '[ABC]'],
		['(A|B)!A', '(A|B)!B'],
		['[ab]{2}', '([ab]{2})!(aa)'],
		['A\\sB', 'A\\s!!B'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '\\A\\A?\\9\\X? \\s!? \\9\\A\\A'],
	];

	for (const [a, b] of cases) {
		const relationship = encoding(a, b);

		if (relationship !== SetRelationship.Incomparable) {
			assert.equal(accepted(a, b), relationship, `${a} against ${b}`);
		}
	}
});

// #endregion
// #region The budget

// A comparison that outgrows its budget says so rather than guessing, and says nothing about the
// relationship.
test('a budget too small is undecided', () => {
	const cases = [
		['[AB]', '[ABC]'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '\\A\\A?\\9\\X? \\s!? \\9\\A\\A'],
	];

	for (const [a, b] of cases) {
		const { decided, relationship } = tryCompareEncodings(compiled(a), compiled(b), 1);

		assert.equal(decided, false, `${a} against ${b}`);
		assert.equal(relationship, SetRelationship.Incomparable, `${a} against ${b}`);
	}
});

// Where the languages are incomparable no walk is needed, so no budget can be too small.
test('incomparable languages need no budget', () => {
	const { decided, relationship } = tryCompareEncodings(compiled('AB'), compiled('CD'), 1);

	assert.equal(decided, true);
	assert.equal(relationship, SetRelationship.Incomparable);
});

// #endregion
