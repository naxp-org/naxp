// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryParse } from '../lib/parser.js';
import { check } from '../lib/well-formedness.js';
import { ruleOf } from './naxp-message-rules.js';

/**
 * Parses and checks, failing the test if the source does not parse.
 *
 * @param {string} text The source.
 * @returns {import('../lib/naxp-error.js').NaxpError | null} The refusal, or null.
 */
function checkNaxp(text) {
	const { ast, error } = tryParse(text);

	assert.ok(ast !== null, `${text} did not parse: ${error}`);

	return check(ast);
}

/**
 * Asserts that a naxp passes W1 and W2.
 *
 * @param {string} text The source.
 */
function passes(text) {
	const error = checkNaxp(text);

	assert.equal(error, null, `${text} was refused: ${error}`);
}

/**
 * Asserts that a naxp is refused, and returns the refusal.
 *
 * @param {string} text The source.
 * @param {string} rule The rule it should break.
 * @returns {import('../lib/naxp-error.js').NaxpError} The refusal.
 */
function breaks(text, rule) {
	const error = checkNaxp(text);

	assert.ok(error !== null, `${text} was accepted.`);
	assert.equal(ruleOf(error.message), rule, `${text}: ${error.text}`);

	return error;
}

// #region W1

test('a rendering that is one of the strings its subject generates is fine', () => {
	// The specification's own examples.
	passes('[\\s\\-]?!\\-');
	passes('\\s?!()');
	passes('\\s!!');
	passes('(ABC)!!');
	passes('\\A!?');
	passes('(A|b)!b');
});

test('a rendering generating more than one string is refused', () => {
	const error = breaks('\\s!(B|C)', 'W1');

	assert.ok(error.text.includes('exactly one string'), error.text);
});

test('a rendering the subject never generates is refused', () => {
	const error = breaks('\\s!\\-', 'W1');

	assert.ok(error.text.includes('not one of the strings'), error.text);
	assert.ok(error.text.includes("'-'"), error.text);
});

test('deleting an element whose subject is not optional is refused', () => {
	const error = breaks('\\s!()', 'W1');

	assert.ok(error.text.includes('cannot be deleted'), error.text);
	assert.ok(error.text.includes('Make the subject optional'), error.text);
});

test("a '!!' whose subject is not a single string is refused", () => {
	// x!! expands to x?!(x), so the rendering is the subject and W1 needs it single valued.
	for (const naxp of ['\\A!!', '[AB]!!', '(B|C)!!']) {
		const error = breaks(naxp, 'W1');

		assert.ok(error.text.includes("'!!'"), `${naxp}: ${error.text}`);
	}
});

test('the message for a bad !! names !! rather than the form it expands to', () => {
	// The whole reason the tree records how a replaceable was written.
	assert.ok(breaks('\\A!!', 'W1').text.includes("subject of a '!!'"));
	assert.ok(breaks('\\s!(B|C)', 'W1').text.includes("rendering of a '!'"));
});

test('an explicit rendering with two branches is refused', () => {
	breaks('(A|B)!(A|B)', 'W1');
});

// #endregion
// #region W2

test("a '!' may not nest", () => {
	for (const naxp of ['(\\s!?)!?', '(A|B)!(B!B)']) {
		const error = breaks(naxp, 'W2');

		assert.ok(error.text.includes('may not nest'), `${naxp}: ${error.text}`);
	}
});

test('W2 is decided before W1', () => {
	// (A|B)!(B!B) breaks both: the rendering nests a '!' and also generates more than one string.
	// W1 reads inside both operands of a '!', so its answer is only meaningful once W2 has
	// established that nothing is hidden in there.
	assert.equal(ruleOf(checkNaxp('(A|B)!(B!B)').message), 'W2');
});

test('two replaceables side by side do not nest', () => {
	passes('\\s!! \\s!!');
	passes('(A|b)!b(C|d)!d');
});

// #endregion
// #region The implementation limit

test('a rendering too long to build is an implementation limit, not a rule of the language', () => {
	// (A{99}){99} is one string of 9 801 characters, past what this implementation will
	// materialise. The naxp is legal, and the message has to say so.
	const error = breaks('((A{99}){99})!!', 'ImplementationLimit');

	assert.ok(error.text.includes('The naxp is legal'), error.text);
	assert.ok(error.text.includes('1999'), error.text);
});

// #endregion
// #region Offsets

test('a refusal with no position reports the whole naxp', () => {
	// W1 and W2 are decided over the finished tree, which records where a node starts and not
	// where it ends, so there is no span to give. Both numbers stay at zero, which the public
	// surface reads as the whole naxp rather than as its first character.
	const error = breaks('AB\\A!!', 'W1');

	assert.equal(error.isWholeNaxp, true);
	assert.equal(error.offset, 0);
	assert.equal(error.length, 0);
});

// #endregion
