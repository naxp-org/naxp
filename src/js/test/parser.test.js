// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import {
	AstAlternation,
	AstChars,
	AstDigitsRange,
	AstEmpty,
	AstInterval,
	AstOptional,
	AstReplaceable,
	AstSequence,
	ReplaceableForm,
} from '../lib/ast.js';
import { tryParse } from '../lib/parser.js';
import { ruleOf } from './naxp-message-rules.js';

// #region Helpers

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
 * Parses, and fails the test if the source is accepted.
 *
 * @param {string} text The source.
 * @returns {import('../lib/naxp-error.js').NaxpError} The refusal.
 */
function refuse(text) {
	const { ast, error } = tryParse(text);

	assert.equal(ast, null, `${text} was accepted.`);

	return error;
}

/**
 * The shape of a tree, with source offsets dropped.
 *
 * The C# test compares two trees by generating strings from each and checking they agree, which
 * needs the matcher. Comparing the shapes directly is available now and is the stronger check:
 * two trees that generate the same strings can still differ.
 *
 * @param {import('../lib/ast.js').Ast} node The node.
 * @returns {object} The shape.
 */
function shape(node) {
	if (node instanceof AstEmpty) { return { kind: 'empty' }; }

	if (node instanceof AstChars) { return { kind: 'chars', chars: node.charSet.key() }; }

	if (node instanceof AstDigitsRange) {
		return {
			kind: 'digitsRange',
			low: node.low,
			lowDigitCount: node.lowDigitCount,
			high: node.high,
			highDigitCount: node.highDigitCount,
		};
	}

	if (node instanceof AstSequence) {
		return { kind: 'sequence', children: node.children.map(shape) };
	}

	if (node instanceof AstAlternation) {
		return { kind: 'alternation', children: node.children.map(shape) };
	}

	if (node instanceof AstOptional) { return { kind: 'optional', child: shape(node.child) }; }

	if (node instanceof AstInterval) {
		return {
			kind: 'interval',
			min: node.minCount,
			max: node.maxCount,
			child: shape(node.child),
		};
	}

	if (node instanceof AstReplaceable) {
		return {
			kind: 'replaceable',
			form: node.form,
			subject: shape(node.subject),
			rendering: shape(node.rendering),
		};
	}

	throw new Error(`Unknown node type ${node.constructor.name}.`);
}

// #endregion
// #region Tree shape

test('a group does not survive parsing', () => {
	const bare = parse('A');
	const grouped = parse('(A)');

	assert.ok(bare instanceof AstChars);
	assert.ok(grouped instanceof AstChars);
	assert.equal(bare.charSet.equals(grouped.charSet), true);
});

test('an empty group is the empty string', () => {
	assert.ok(parse('()') instanceof AstEmpty);
});

test('x!! expands to an optional subject rendered as itself', () => {
	// x!! is x?!(x), and the expansion is structural rather than textual.
	const replaceable = parse('\\s!!');

	assert.ok(replaceable instanceof AstReplaceable);
	assert.equal(replaceable.form, ReplaceableForm.Reproduced);
	assert.ok(replaceable.subject instanceof AstOptional);
	assert.equal(replaceable.subject.child, replaceable.rendering, 'the subtree is shared');
});

test('x!? expands to an optional subject rendered as nothing', () => {
	const replaceable = parse('\\A!?');

	assert.ok(replaceable instanceof AstReplaceable);
	assert.equal(replaceable.form, ReplaceableForm.Dropped);
	assert.ok(replaceable.subject instanceof AstOptional);
	assert.ok(replaceable.rendering instanceof AstEmpty);
});

test('a quantifier binds to the base before it', () => {
	// It does not reach back over the sequence.
	const sequence = parse('AB?');

	assert.ok(sequence instanceof AstSequence);
	assert.equal(sequence.children.length, 2);
	assert.ok(sequence.children[0] instanceof AstChars);
	assert.ok(sequence.children[1] instanceof AstOptional);
});

test('an interval keeps both counts', () => {
	const interval = parse('A{2,4}');

	assert.ok(interval instanceof AstInterval);
	assert.equal(interval.minCount, 2);
	assert.equal(interval.maxCount, 4);
});

test('an interval with one count uses it for both', () => {
	const interval = parse('A{3}');

	assert.ok(interval instanceof AstInterval);
	assert.equal(interval.minCount, 3);
	assert.equal(interval.maxCount, 3);
});

test('a digits range keeps the widths as written', () => {
	const padded = parse('#[00-105]');

	assert.ok(padded instanceof AstDigitsRange);
	assert.equal(padded.low, 0);
	assert.equal(padded.lowDigitCount, 2);
	assert.equal(padded.high, 105);
	assert.equal(padded.highDigitCount, 3);
});

test('an interval is not expanded at parse time', () => {
	// The cap on an interval count exists so that an implementation can reject a naxp before
	// expanding it, which parsing must not throw away.
	const interval = parse('(A{99}){99}');

	assert.ok(interval instanceof AstInterval);
	assert.equal(interval.maxCount, 99);
	assert.ok(interval.child instanceof AstInterval);
	assert.equal(interval.child.maxCount, 99);
});

// #endregion
// #region Whitespace

test('whitespace between tokens is ignored', () => {
	// In each of these the separator is a token in its own right, so whitespace around it is
	// whitespace between tokens.
	const pairs = [
		['[A - E]', '[A-E]'],
		['A{2 , 5}', 'A{2,5}'],
		['#[0 - 10]', '#[0-10]'],
		[' A | B ', 'A|B'],
		['\\s !!', '\\s!!'],
		['( A B )', '(AB)'],
		['#[ 0 - 10 ]', '#[0-10]'],
	];

	for (const [spaced, tight] of pairs) {
		assert.deepEqual(shape(parse(spaced)), shape(parse(tight)), `${spaced} versus ${tight}`);
	}
});

// #endregion
// #region Error productions for near misses

test('an interval with a hyphen names the separator', () => {
	// The counts take a comma, as in every regular expression dialect. A hyphen is what somebody
	// carrying a habit over from a character range would reach for, so it earns its own message.
	const error = refuse('A{2-5}');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.equal(error.offset, 3);
	assert.ok(error.text.includes("',', not by a hyphen"), error.text);
});

test('an unbounded interval says there is none', () => {
	const error = refuse('A{2,}');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.ok(error.text.includes('no unbounded interval'), error.text);
});

test('a bare bang names the three forms', () => {
	const error = refuse('A!');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.equal(error.offset, 1);
	assert.ok(error.text.includes("'x!y', 'x!!' or 'x!?'"), error.text);
});

test('the hex escape says it was removed', () => {
	// Version 0.3 removed it, so anyone who knows regex or an earlier draft will write this and
	// deserves to be told why it has gone.
	const error = refuse('\\x41');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.equal(error.offset, 0);
	assert.ok(error.text.includes('removed in version 0.3'), error.text);
});

test('an undefined escape lists the escape letters', () => {
	const error = refuse('\\d');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.ok(error.text.includes("'s', '9', 'A', 'a' and 'X'"), error.text);
});

test('a range written backwards says lowest first', () => {
	const error = refuse('[E-A]');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.ok(error.text.includes('lowest first'), error.text);
	assert.ok(error.text.includes("'A'-'E'"), error.text);
});

test("'!!' after a '?' says to write it out", () => {
	const error = refuse('\\s?!!');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.ok(error.text.includes("x!(x)"), error.text);
});

test('two quantifiers on one base says to group it', () => {
	const error = refuse('A?{2}');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.ok(error.text.includes("'(A?){2}'"), error.text);
});

test('whitespace splitting a token points at the whitespace', () => {
	const cases = [
		['\\ s', 1, 'cannot be followed by whitespace'],
		['A! !', 2, 'is one token'],
		['A{2 5}', 3, 'cannot be separated by whitespace'],
		['# [0-10]', 1, "no whitespace between '#' and '['"],
		['#[1 0-20]', 3, 'cannot be separated by whitespace'],
	];

	for (const [text, offset, fragment] of cases) {
		const error = refuse(text);

		assert.equal(ruleOf(error.message), 'syntax', text);
		assert.equal(error.offset, offset, text);
		assert.ok(error.text.includes(fragment), `${text}: ${error.text}`);
	}
});

test('further refusals the test data does not cover', () => {
	// Kept here so the parser cannot quietly grow lax.
	const cases = [
		['A)', 'syntax'],
		['A-B', 'syntax'],
		['[\\9-A]', 'syntax'],
		['[A-]', 'syntax'],
		['A{}', 'syntax'],
		['[]', 'syntax'],
		['(A', 'syntax'],
		['[A', 'syntax'],
		['A|', 'syntax'],
		['|A', 'syntax'],
		['A{2,1}', 'W4'],
		['#[5-4]', 'W4'],
		['A{123}', 'W4'],
		['#[0-1234567890123456]', 'W4'],
	];

	for (const [text, rule] of cases) {
		assert.equal(ruleOf(refuse(text).message), rule, text);
	}
});

// #endregion
// #region Source repertoire

test('source outside the repertoire is refused', () => {
	// The source may hold whitespace and the printable ASCII characters U+0021 to U+007E.
	for (const c of ['\u00e9', '\u0001', '\u007f']) {
		const error = refuse(`A${c}`);

		assert.equal(ruleOf(error.message), 'syntax');
		assert.equal(error.offset, 1);
		assert.ok(error.text.includes('cannot appear in the source'), error.text);
	}
});

test('the repertoire message names the code point', () => {
	assert.ok(refuse('A\u00e9').text.includes('U+00E9'));
});

test('an empty source is not a naxp', () => {
	assert.equal(ruleOf(refuse('').message), 'syntax');
});

test('a source of nothing but whitespace is not a naxp', () => {
	assert.equal(ruleOf(refuse('   ').message), 'syntax');
});

// #endregion
