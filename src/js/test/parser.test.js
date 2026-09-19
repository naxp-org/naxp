// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import {
	AstAlternation,
	AstChars,
	AstDecimalRange,
	AstEmpty,
	AstInterval,
	AstOptional,
	AstUnified,
	AstSequence,
	UnifiedForm,
} from '../lib/ast.js';
import { NaxpMessage } from '../lib/naxp-message.js';
import { Naxp } from '../lib/naxp.js';
import { tryParse } from '../lib/parser.js';
import { ruleOf } from './naxp-message-rules.js';

// #region Helpers

/**
 * Parses, and fails the test if the pattern is invalid.
 *
 * @param {string} text The pattern.
 * @returns {import('../lib/ast.js').Ast} The tree.
 */
function parse(text) {
	const { ast, error } = tryParse(text);

	assert.ok(ast !== null, `${text} was invalid: ${error}`);

	return ast;
}

/**
 * Parses, and fails the test if the pattern is accepted.
 *
 * @param {string} text The pattern.
 * @returns {import('../lib/naxp-error.js').NaxpError} The fault.
 */
function faultOf(text) {
	const { ast, error } = tryParse(text);

	assert.equal(ast, null, `${text} was accepted.`);

	return error;
}

/**
 * The shape of a tree, with pattern offsets dropped.
 *
 * The C# test compares two trees by generating strings from each and checking they agree, which
 * needs the tree walker. Comparing the shapes directly is available now and is the stronger check:
 * two trees that generate the same strings can still differ.
 *
 * @param {import('../lib/ast.js').Ast} node The node.
 * @returns {object} The shape.
 */
function shape(node) {
	if (node instanceof AstEmpty) { return { kind: 'empty' }; }

	if (node instanceof AstChars) { return { kind: 'chars', chars: node.charSet.key() }; }

	if (node instanceof AstDecimalRange) {
		return {
			kind: 'decimalRange',
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

	if (node instanceof AstUnified) {
		return {
			kind: 'unified',
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
	const unified = parse('\\s!!');

	assert.ok(unified instanceof AstUnified);
	assert.equal(unified.form, UnifiedForm.Reproduced);
	assert.ok(unified.subject instanceof AstOptional);
	assert.equal(unified.subject.child, unified.rendering, 'the subtree is shared');
});

test('x!? expands to an optional subject rendered as nothing', () => {
	const unified = parse('\\A!?');

	assert.ok(unified instanceof AstUnified);
	assert.equal(unified.form, UnifiedForm.Dropped);
	assert.ok(unified.subject instanceof AstOptional);
	assert.ok(unified.rendering instanceof AstEmpty);
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

test('a decimal range keeps the widths as written', () => {
	const padded = parse('#[00-105]');

	assert.ok(padded instanceof AstDecimalRange);
	assert.equal(padded.low, 0);
	assert.equal(padded.lowDigitCount, 2);
	assert.equal(padded.high, 105);
	assert.equal(padded.highDigitCount, 3);
});

test('an interval is not expanded at parse time', () => {
	// The cap on an interval count exists so that an implementation can find a naxp invalid before
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
	const error = faultOf('A{2-5}');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.equal(error.offset, 3);
	assert.ok(error.text.includes("',', not by a hyphen"), error.text);
});

test('an unbounded interval says there is none', () => {
	const error = faultOf('A{2,}');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.ok(error.text.includes('no unbounded interval'), error.text);
});

test('a bare bang names the three forms', () => {
	const error = faultOf('A!');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.equal(error.offset, 1);
	assert.ok(error.text.includes("'x!y', 'x!!' or 'x!?'"), error.text);
});

test('\\x is a digit or a lower case letter, so \\x41 is three positions', () => {
	// The regex hex escape reads as the block escape followed by two literal digits. Anyone
	// expecting 'A' out of it gets a naxp that accepts 'a41' and refuses 'A'.
	const naxp = Naxp.parse('\\x41');

	assert.equal(naxp.maxEncodedValue, 36n);
	assert.ok(naxp.accepts('a41'));
	assert.ok(naxp.accepts('741'));
	assert.ok(!naxp.accepts('A41'));
	assert.ok(!naxp.accepts('A'));
});

test('an undefined escape lists the escape letters', () => {
	const error = faultOf('\\d');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.ok(error.text.includes("'s', '9', 'A', 'a', 'X', 'x', 'C' and 'c'"), error.text);
});

test('a range written backwards says lowest first', () => {
	const error = faultOf('[E-A]');

	assert.equal(ruleOf(error.message), 'W4');
	assert.ok(error.text.includes('lowest first'), error.text);
	assert.ok(error.text.includes("'A-E'"), error.text);
});

test('a range written backwards suggests something that can be typed', () => {
	// The message quotes its argument, so the argument must not quote itself. It also has to be
	// the pattern of the range rather than a description of its bounds: a space is written '\s'
	// and cannot be typed as itself, and a reserved bound has to keep its backslash.
	const cases = [
		['[E-A]', 'A-E'],
		['[~-\\s]', '\\s-~'],
		['[a-\\]]', '\\]-a'],
	];

	for (const [text, suggestion] of cases) {
		const error = faultOf(text);

		assert.ok(error.text.includes(`Write '${suggestion}'.`), `${text}: ${error.text}`);

		// A suggestion that is not itself a naxp is not a suggestion.
		parse(`[${suggestion}]`);
	}
});

test("'!!' after a '?' says to write it out", () => {
	const error = faultOf('\\s?!!');

	assert.equal(ruleOf(error.message), 'syntax');
	assert.ok(error.text.includes("x!(x)"), error.text);
});

test('two quantifiers on one base says to group it', () => {
	const error = faultOf('A?{2}');

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
		const error = faultOf(text);

		assert.equal(ruleOf(error.message), 'syntax', text);
		assert.equal(error.offset, offset, text);
		assert.ok(error.text.includes(fragment), `${text}: ${error.text}`);
	}
});

test('further faults the test data does not cover', () => {
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
		assert.equal(ruleOf(faultOf(text).message), rule, text);
	}
});

// #endregion
// #region Pattern repertoire

test('a closing parenthesis with no group to close says so', () => {
	const cases = [
		['AB)', 2],
		['A)B', 1],
		['(A)B)', 4],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A) | GIR \\s!! 0AA', 22],
	];

	for (const [text, offset] of cases) {
		const error = faultOf(text);

		assert.equal(error.message, NaxpMessage.NAXP1057_GroupNotOpened, text);
		assert.equal(error.offset, offset, text);
		assert.ok(error.text.includes('no group'), error.text);
	}
});

test('the faults either side of it are unchanged', () => {
	// An unclosed group still reports the opener, and an escaped parenthesis is still an
	// ordinary character rather than a fault.
	assert.equal(faultOf('(AB').message, NaxpMessage.NAXP1009_GroupNotClosed);
	assert.equal(faultOf('((A)').message, NaxpMessage.NAXP1009_GroupNotClosed);
	assert.notEqual(tryParse('A\\)B').ast, null);
});

test('pattern outside the repertoire is invalid', () => {
	// The pattern may hold whitespace and the printable ASCII characters U+0021 to U+007E.
	for (const c of ['\u00e9', '\u0001', '\u007f']) {
		const error = faultOf(`A${c}`);

		assert.equal(ruleOf(error.message), 'syntax');
		assert.equal(error.offset, 1);
		assert.ok(error.text.includes('cannot appear in the pattern'), error.text);
	}
});

test('the repertoire message names the code point', () => {
	assert.ok(faultOf('A\u00e9').text.includes('U+00E9'));
});

test('an empty pattern is not a naxp', () => {
	assert.equal(ruleOf(faultOf('').message), 'syntax');
});

test('a pattern of nothing but whitespace is not a naxp', () => {
	assert.equal(ruleOf(faultOf('   ').message), 'syntax');
});

// #endregion
