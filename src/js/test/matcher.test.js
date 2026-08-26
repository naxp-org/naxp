// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { SingleStringOutcome, generates, tryGetSingleString } from '../lib/matcher.js';
import { tryParse } from '../lib/parser.js';

/**
 * Parses, and fails the test if the source is refused.
 *
 * @param {string} text The source.
 * @returns {import('../lib/ast.js').Ast} The tree.
 */
function parse(text) {
	const { ast, error } = tryParse(text);

	assert.ok(ast !== null, `${text} was refused: ${error}`);

	return ast;
}

/**
 * Whether a naxp generates a string.
 *
 * @param {string} naxp The source.
 * @param {string} text The string.
 * @returns {boolean} Whether it generates it.
 */
function makes(naxp, text) {
	const { matched, tooLong } = generates(parse(naxp), text);

	assert.equal(tooLong, false, `${naxp} against '${text}' was abandoned as too long`);

	return matched;
}

// #region Matching

test('a digits range matches the widths its bounds fix', () => {
	const cases = [
		['#[0-10]', '0', true],
		['#[0-10]', '9', true],
		['#[0-10]', '10', true],
		['#[0-10]', '00', false],
		['#[0-10]', '11', false],
		['#[00-10]', '00', true],
		['#[00-10]', '7', false],
		['#[00-105]', '07', true],
		['#[00-105]', '007', false],
		['#[0-105]', '07', false],
		['#[0-105]', '105', true],
		['#[0-105]', '106', false],
	];

	for (const [naxp, text, expected] of cases) {
		assert.equal(makes(naxp, text), expected, `${naxp} against '${text}'`);
	}
});

test('an interval matches its counts', () => {
	const cases = [
		['A{0,3}', '', true],
		['A{0,3}', 'AAA', true],
		['A{0,3}', 'AAAA', false],
		['A{0}', '', true],
		['A{0}', 'A', false],
		['(A?){9}', 'AAA', true],
	];

	for (const [naxp, text, expected] of cases) {
		assert.equal(makes(naxp, text), expected, `${naxp} against '${text}'`);
	}
});

test('an alternation matches any of its branches and nothing else', () => {
	assert.equal(makes('AB|B', 'AB'), true);
	assert.equal(makes('AB|B', 'B'), true);
	assert.equal(makes('AB|B', 'A'), false);
	assert.equal(makes('AB|B', ''), false);
});

test('the empty naxp matches only the empty string', () => {
	assert.equal(makes('()', ''), true);
	assert.equal(makes('()', 'A'), false);
});

test('a replaceable matches whatever its subject accepts', () => {
	// x!y accepts the strings x accepts; the rendering does not widen or narrow that.
	assert.equal(makes('(A|b)!b', 'A'), true);
	assert.equal(makes('(A|b)!b', 'b'), true);
	assert.equal(makes('(A|b)!b', 'c'), false);
	assert.equal(makes('\\s!!', ' '), true);
	assert.equal(makes('\\s!!', ''), true);
});

test('a string longer than the budget is abandoned rather than matched', () => {
	const { matched, tooLong } = generates(parse('A'), 'A'.repeat(2000));

	assert.equal(matched, false);
	assert.equal(tooLong, true);
});

test('the postcode naxp matches with and without the space', () => {
	const postcode = '\\A \\A? \\9 \\X? \\s!! \\9 \\A \\A';

	assert.equal(makes(postcode, 'M1 1AA'), true);
	assert.equal(makes(postcode, 'M11AA'), true);
	assert.equal(makes(postcode, 'EC1A 1BB'), true);
	assert.equal(makes(postcode, 'M1  1AA'), false);
	assert.equal(makes(postcode, '1M1 1AA'), false);
});

// #endregion
// #region The one string an expression generates

test('an expression generating exactly one string gives it', () => {
	const cases = [
		['A', 'A'],
		['()', ''],
		['\\s', ' '],
		['ABC', 'ABC'],
		['A|A', 'A'],
		['(A)', 'A'],
		['A{3}', 'AAA'],
		['A{0}', ''],
		['#[7-7]', '7'],
		['#[07-07]', '07'],
	];

	for (const [naxp, expected] of cases) {
		const { outcome, result } = tryGetSingleString(parse(naxp));

		assert.equal(outcome, SingleStringOutcome.Single, naxp);
		assert.equal(result, expected, naxp);
	}
});

test('an expression generating more than one string says so', () => {
	for (const naxp of ['\\A', '[AB]', 'A|B', 'A?', 'A{1,2}', '#[0-9]', '#[7-7]?']) {
		const { outcome, result } = tryGetSingleString(parse(naxp));

		assert.equal(outcome, SingleStringOutcome.Multiple, naxp);
		assert.equal(result, null, naxp);
	}
});

test('a digits range gives one string only at one number and one width', () => {
	// #[7-07] would be refused by W4, so the widths can differ only by way of the values.
	assert.equal(tryGetSingleString(parse('#[7-7]')).outcome, SingleStringOutcome.Single);
	assert.equal(tryGetSingleString(parse('#[7-8]')).outcome, SingleStringOutcome.Multiple);
	assert.equal(tryGetSingleString(parse('#[07-99]')).outcome, SingleStringOutcome.Multiple);
});

test('an optional gives one string only when its child gives the empty one', () => {
	assert.equal(tryGetSingleString(parse('()?')).outcome, SingleStringOutcome.Single);
	assert.equal(tryGetSingleString(parse('A?')).outcome, SingleStringOutcome.Multiple);
});

test('a string too long to build is reported rather than built', () => {
	// (A{99}){99} is 9 801 characters, which is past the budget and is the shape a hostile naxp
	// takes. The point of the two digit interval cap is that this is refused without expanding.
	const { outcome, result } = tryGetSingleString(parse('(A{99}){99}'));

	assert.equal(outcome, SingleStringOutcome.TooLong);
	assert.equal(result, null);
});

test('a string just inside the budget is still built', () => {
	const { outcome, result } = tryGetSingleString(parse('(A{97}){20}'));

	assert.equal(outcome, SingleStringOutcome.Single);
	assert.equal(result.length, 1940);
});

// #endregion
