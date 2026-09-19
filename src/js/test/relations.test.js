// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCompile } from '../lib/compiler.js';
import { compareLanguages } from '../lib/relations.js';
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
// #region The four relationships

test('the same language is equal', () => {
	const cases = [
		['AB', 'AB'],
		['[AB]', 'A|B'],
		['A(B|C)', 'AB|AC'],
		['\\9{2}', '#[00-99]'],
	];

	for (const [a, b] of cases) {
		assert.equal(accepted(a, b), SetRelationship.Equal, `${a} against ${b}`);
	}
});

test('fewer strings is a subset', () => {
	const cases = [
		['[AB]', '[ABC]'],
		['A', 'A?'],
		['AB', 'AB|CD'],
		['\\A\\9', '\\A\\X'],
	];

	for (const [a, b] of cases) {
		assert.equal(accepted(a, b), SetRelationship.SubsetOf, `${a} against ${b}`);
	}
});

test('more strings is a superset', () => {
	const cases = [
		['[ABC]', '[AB]'],
		['A?', 'A'],
		['AB|CD', 'AB'],
	];

	for (const [a, b] of cases) {
		assert.equal(accepted(a, b), SetRelationship.SupersetOf, `${a} against ${b}`);
	}
});

test('neither containing the other is incomparable', () => {
	const cases = [
		['[AB]', '[BC]'],
		['AB', 'CD'],
		['A|BB', 'A|CC'],
	];

	for (const [a, b] of cases) {
		assert.equal(accepted(a, b), SetRelationship.Incomparable, `${a} against ${b}`);
	}
});

// #endregion
// #region The relationship is directional

test('swapping the arguments swaps subset and superset', () => {
	assert.equal(accepted('[AB]', '[ABC]'), SetRelationship.SubsetOf);
	assert.equal(accepted('[ABC]', '[AB]'), SetRelationship.SupersetOf);
});

// #endregion
// #region Strings of differing length

// A string one machine takes and the other has no transition for, at a point where the other has
// already finished, is still a difference.
test('one language runs on past the other', () => {
	assert.equal(accepted('AB', 'AB|ABC'), SetRelationship.SubsetOf);
	assert.equal(accepted('AB|ABC', 'AB'), SetRelationship.SupersetOf);
	assert.equal(accepted('AB|ABC', 'AB|ABD'), SetRelationship.Incomparable);
});

// #endregion
// #region The accepted language and the canonical language differ

// A '!' widens what is accepted and leaves the canonical language alone, which is the property the
// whole comparison rests on. The two axes must therefore be able to disagree.
test('unified widens the accepted language only', () => {
	assert.equal(accepted('A\\sB', 'A\\s!!B'), SetRelationship.SubsetOf);
	assert.equal(canonical('A\\sB', 'A\\s!!B'), SetRelationship.Equal);
});

// A case fold does the same, with the upper case form canonical either way.
test('a fold widens the accepted language only', () => {
	assert.equal(accepted('\\A\\A', '\\C(\\A\\A)'), SetRelationship.SubsetOf);
	assert.equal(canonical('\\A\\A', '\\C(\\A\\A)'), SetRelationship.Equal);
});

// #endregion
// #region The postcode cases the site quotes

// Adding GIR 0AA widens both languages by exactly one string.
test('adding GIR widens both languages', () => {
	const without = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const withGir = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA';

	assert.equal(accepted(without, withGir), SetRelationship.SubsetOf);
	assert.equal(canonical(without, withGir), SetRelationship.SubsetOf);
	assert.equal(compiled(withGir).maxEncodedValue - compiled(without).maxEncodedValue, 1n);
});

// Restricting the letters to those the Royal Mail guide lists refuses text the looser naxp
// accepts, which is the first column saying so.
test('tightening the letters narrows both languages', () => {
	const loose = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const tight = '[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]';

	assert.equal(accepted(loose, tight), SetRelationship.SupersetOf);
	assert.equal(canonical(loose, tight), SetRelationship.SupersetOf);
});

// #endregion
