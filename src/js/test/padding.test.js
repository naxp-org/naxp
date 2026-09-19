// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { AsciiCharSet } from '../lib/ascii-char-set.js';
import {
	AstAlternation,
	AstChars,
	AstDecimalRange,
	AstEmpty,
	AstOptional,
	AstSequence,
	AstUnified,
	UnifiedForm,
} from '../lib/ast.js';
import { tryCompile } from '../lib/compiler.js';
import { NaxpMessage } from '../lib/naxp-message.js';
import { Naxp } from '../lib/naxp.js';
import { tryParse } from '../lib/parser.js';
import { check } from '../lib/well-formedness.js';
import { ruleOf } from './naxp-message-rules.js';

// #region Helpers

/**
 * Parses and checks, failing the test if the pattern is invalid.
 *
 * @param {string} text The pattern.
 * @returns {import('../lib/ast.js').Ast} The tree.
 */
function parse(text) {
	const { ast, error } = tryParse(text);

	assert.ok(ast !== null, `${text} did not parse: ${error}`);
	assert.equal(check(ast), null, `${text} was invalid.`);

	return ast;
}

/**
 * Parses and checks, failing the test if the pattern is accepted.
 *
 * @param {string} text The pattern.
 * @returns {import('../lib/naxp-error.js').NaxpError} The fault.
 */
function faultOf(text) {
	const { ast, error } = tryParse(text);

	if (ast === null) { return error; }

	const wellFormedness = check(ast);

	assert.ok(wellFormedness !== null, `${text} was accepted.`);

	return wellFormedness;
}

/**
 * The padding element of the narrowest branch, which for a range with one padding position is the
 * branch the alternation leads with.
 *
 * @param {string} pattern The naxp.
 * @returns {AstUnified} The unified element standing for that padding position.
 */
function firstPadOf(pattern) {
	const alternation = parse(pattern);

	assert.ok(alternation instanceof AstAlternation);

	const shorter = alternation.children[0];

	assert.ok(shorter instanceof AstSequence);
	assert.ok(shorter.children[0] instanceof AstUnified);

	return shorter.children[0];
}

const ZERO = AsciiCharSet.fromSingleChar(0x30);

// #endregion
// #region What a mark expands to

test('a \'!\' mark expands to an optional zero rendered as a zero', () => {
	// 0! is 0!!, which is 0?!(0).
	const pad = firstPadOf('#[0!0-99]');

	assert.equal(pad.form, UnifiedForm.Reproduced);
	assert.ok(pad.subject instanceof AstOptional);
	assert.ok(pad.subject.child instanceof AstChars);
	assert.ok(pad.subject.child.charSet.equals(ZERO));
	assert.ok(pad.rendering instanceof AstChars);
	assert.ok(pad.rendering.charSet.equals(ZERO));
});

test('a \'?\' mark expands to an optional zero rendered as nothing', () => {
	// 0? is 0!?, which is 0?!().
	const pad = firstPadOf('#[0?0-99]');

	assert.equal(pad.form, UnifiedForm.Dropped);
	assert.ok(pad.subject instanceof AstOptional);
	assert.ok(pad.rendering instanceof AstEmpty);
});

test('an unmarked range is not expanded', () => {
	// Nothing pays for a feature it does not use.
	assert.ok(parse('#[00-105]') instanceof AstDecimalRange);
});

test('the expansion is one branch per width of the value', () => {
	// Widest last, and the padding in front of each branch is the positions that width leaves over.
	const alternation = parse('#[0?0!0-105]');

	assert.ok(alternation instanceof AstAlternation);
	assert.equal(alternation.children.length, 3);
	assert.equal(alternation.children[0].children.length, 3);
	assert.equal(alternation.children[1].children.length, 2);
	assert.ok(alternation.children[2] instanceof AstDecimalRange);
});

// #endregion
// #region Which spellings a marked range admits

test('a marked range admits the widths its marks allow', () => {
	const cases = [
		['#[0!0!0-105]', '7', true],
		['#[0!0!0-105]', '07', true],
		['#[0!0!0-105]', '007', true],
		['#[0!0!0-105]', '0007', false],
		['#[0!0!0-105]', '106', false],
		['#[0!0!0-105]', '0105', false],
		// An unmarked padding position stays mandatory, so this one has a minimum width of two.
		['#[00!0-999]', '7', false],
		['#[00!0-999]', '07', true],
		['#[00!0-999]', '007', true],
		['#[00!0-999]', '42', false],
		['#[00!0-999]', '042', true],
	];

	for (const [pattern, text, expected] of cases) {
		assert.equal(Naxp.parse(pattern).accepts(text), expected, `${pattern} on ${text}`);
	}
});

// #endregion
// #region What a mark does to the canonical form

test('the marks fix the canonical width and are read outside in', () => {
	const cases = [
		['#[0!0!0-105]', '007', '042', '105'],
		['#[0?0?0-105]', '7', '42', '105'],
		['#[0?0!0-105]', '07', '42', '105'],
		['#[0!0?0-105]', '07', '042', '105'],
	];

	for (const [pattern, seven, fortyTwo, oneOhFive] of cases) {
		const naxp = Naxp.parse(pattern);

		for (const spelling of ['7', '07', '007']) {
			assert.equal(naxp.getCanonicalForm(spelling), seven, `${pattern} on ${spelling}`);
		}

		assert.equal(naxp.getCanonicalForm('42'), fortyTwo, pattern);
		assert.equal(naxp.getCanonicalForm('042'), fortyTwo, pattern);
		assert.equal(naxp.getCanonicalForm('105'), oneOhFive, pattern);
	}
});

// #endregion
// #region What a mark leaves the encoding as

test('a marked range assigns the values of the range it canonicalises to', () => {
	// A mark changes which spellings are accepted and nothing else.
	const cases = [
		['#[0!0!0-105]', '#[000-105]'],
		['#[0?0!0-105]', '#[00-105]'],
		['#[0?0?0-105]', '#[0-105]'],
	];

	for (const [marked, plain] of cases) {
		const withMarks = Naxp.parse(marked);
		const without = Naxp.parse(plain);

		assert.equal(withMarks.maxEncodedValue, without.maxEncodedValue, marked);

		for (let value = 1n; value <= without.maxEncodedValue; ++value) {
			assert.equal(withMarks.decode(value), without.decode(value), `${marked} at ${value}`);
		}
	}
});

test('padding that keeps every zero orders numerically', () => {
	// Marking every padding position '!' makes the canonical language fixed width, which is the
	// condition the ordering guarantee is stated on.
	const naxp = Naxp.parse('#[0!0!0-105]');
	let previous = 0n;

	for (let number = 0; number <= 105; ++number) {
		const value = naxp.encode(String(number));

		assert.ok(value > previous, `${number} did not encode above the number below it.`);
		previous = value;
	}
});

test('the flight designator takes either spelling and orders by number', () => {
	// Two characters of airline, then a number that may be written with or without its leading
	// zeros and is stored with them.
	const naxp = Naxp.parse(String.raw`(\A\A|\A\9|\9\A) #[0!0!0!1-9999]`);

	assert.equal(naxp.maxEncodedValue, 11958804n);
	assert.equal(naxp.encode('AC861'), naxp.encode('AC0861'));
	assert.equal(naxp.decode(naxp.encode('AC861')), 'AC0861');
	assert.ok(naxp.encode('AC861') > naxp.encode('AC860'));

	// The airline still leads, so a later designator outranks an earlier one whatever the number,
	// and a designator of a letter and a digit is admitted.
	assert.ok(naxp.encode('BA1') > naxp.encode('AC9999'));
	assert.equal(naxp.decode(naxp.encode('U28')), 'U20008');
});

test('a fully marked range reaches eleven digits, and W3 is what stops it', () => {
	// A '!' mark cannot emit until it knows how long the input is, so its machine holds what it
	// has read. Holding it as a reference to how far back it was read rather than as its value
	// costs a state per shape instead of a state per string, so the machine is no longer what
	// binds. What binds is the W3 square, whose pair count is quartic in the width.
	Naxp.parse('#[' + '0!'.repeat(10) + '1-' + '9'.repeat(11) + ']');
	Naxp.parse('#[' + '0?'.repeat(10) + '1-' + '9'.repeat(11) + ']');

	const { error } = tryCompile('#[' + '0!'.repeat(11) + '1-' + '9'.repeat(12) + ']');

	assert.equal(error?.message, NaxpMessage.NAXP1050_TooManyPairStates);
});

test('a lightly marked range goes wider, because the square grows with the marks', () => {
	// The square pairs residuals, and a marked range reaches one chain per width it accepts, so
	// fewer marks means fewer widths and a smaller square. One mark reaches the cap on a bound.
	Naxp.parse('#[0!' + '0'.repeat(13) + '-' + '9'.repeat(15) + ']');

	const { error } = tryCompile('#[0!0!' + '0'.repeat(13) + '-' + '9'.repeat(15) + ']');

	assert.equal(error?.message, NaxpMessage.NAXP1050_TooManyPairStates);
});

// #endregion
// #region Faults

test('a misplaced mark is refused', () => {
	const cases = [
		// The last digit is the number, not padding in front of it.
		['#[0!-9]', 'W4', 'zeros at the front of a number'],
		['#[10?5-999]', 'W4', 'zeros at the front of a number'],
		// Only a zero stands for padding.
		['#[1!05-999]', 'W4', 'Only a zero may be marked'],
		// The upper bound does not fix the width, so it carries no marks.
		['#[0-9!]', 'W4', 'Only the lower bound'],
		// A mark belongs to the digit before it.
		['#[0 !0-9]', 'syntax', 'separating whitespace'],
		['#[0! 0-9]', 'syntax', 'cannot be separated by whitespace'],
	];

	for (const [pattern, rule, fragment] of cases) {
		const error = faultOf(pattern);

		assert.equal(ruleOf(error.message), rule, pattern);
		assert.ok(error.text.includes(fragment), `${pattern} said: ${error.text}`);
	}
});

test('a marked range inside a unified element breaks W2', () => {
	// A marked range holds unified elements once expanded, so W2 reaches it where the unmarked form
	// was legal.
	assert.equal(ruleOf(faultOf('(#[0!0-105])!(07)').message), 'W2');

	Naxp.parse('(#[00-105])!(07)');
});

test('a marked range beside a variable width neighbour breaks W3', () => {
	// A marked range accepts more than one width, so what follows it has to fix where it stops. A
	// single digit does; an optional one does not.
	Naxp.parse(String.raw`#[0!0!0-105]\9`);

	const { error } = tryCompile(String.raw`#[0!0!0-105]\9?`);

	assert.equal(ruleOf(error.message), 'W3');
});

// #endregion
