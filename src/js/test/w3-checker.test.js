// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryParse } from '../lib/parser.js';
import { RxFactory } from '../lib/rx.js';
import { checkW3 } from '../lib/w3-checker.js';
import { check } from '../lib/well-formedness.js';
import { ruleOf } from './naxp-message-rules.js';

/**
 * Parses, checks W1 and W2, then checks W3.
 *
 * @param {string} naxp The source.
 * @param {number} [maxStates] The budget.
 * @returns {import('../lib/naxp-error.js').NaxpError | null} The refusal, or null.
 */
function w3(naxp, maxStates) {
	const { ast, error } = tryParse(naxp);

	assert.ok(ast !== null, `${naxp} did not parse: ${error}`);
	assert.equal(check(ast), null, `${naxp} failed W1 or W2`);

	return checkW3(ast, new RxFactory(), maxStates === undefined ? {} : { maxStates });
}

/**
 * The witness a violation message names.
 *
 * @param {string} message The message.
 * @returns {string} The witness.
 */
function witnessOf(message) {
	const match = /but '([^']*)' has more than one canonical form/.exec(message);

	assert.ok(match !== null, `no witness in: ${message}`);

	return match[1];
}

// #region Violations

test('replacement that is not single valued is refused', () => {
	// The cases come from encoding/w3-functionality.md, which reviewed the procedure before it
	// was written and supplied the naxps that break the obvious wrong versions of it.
	const cases = [
		// The case the conformance data already carried.
		'AB!!B?C',
		// Five characters, and smaller than the above. The first two are caught only by comparing
		// what a branch emits at end of text, not by comparing what it has emitted so far.
		'A!!A?',
		'A?A!!',
		'A!?A?',
		// Witnessed by the empty string alone, so no character is ever read.
		'A!!|()',
		// The same point with both canonical forms produced at end of text from one residual.
		'A!?|A!!',
		// Emissions are not uniform over a first-set minterm: 'a' agrees and 'b' does not.
		'[ab]|[ab]!a',
		// Skipping a nullable copy of an interval emits, so how many are skipped is a choice.
		'(A!!){0,3}',
	];

	for (const naxp of cases) {
		const error = w3(naxp);

		assert.ok(error !== null, `${naxp} was accepted.`);
		assert.equal(ruleOf(error.message), 'W3', `${naxp}: ${error.text}`);
	}
});

test('a violation on the empty string is found before any character is read', () => {
	const error = w3('A!!|()');

	assert.equal(ruleOf(error.message), 'W3');
	assert.equal(witnessOf(error.text), '');
});

test('a violation names the witness the specification names', () => {
	// W3 in the specification works AB!!B?C through by hand and settles on ABC: read the B as the
	// replaceable element with the optional one absent and the canonical form is ABC; read it the
	// other way round and it is ABBC. The checker has to find that same string.
	assert.equal(witnessOf(w3('AB!!B?C').text), 'ABC');

	// 'a' agrees between the two branches and 'b' does not, so 'b' is the only witness.
	assert.equal(witnessOf(w3('[ab]|[ab]!a').text), 'b');
});

// #endregion
// #region Well formed

test('near misses that a checker comparing the wrong thing would refuse are accepted', () => {
	const cases = [
		// Both alternatives map B and BA to BA, so two branches with pendings that differ still
		// agree once what they emit at end of text is counted. A checker comparing pendings
		// refuses this.
		'(B|BA)!(BA)|BA!!',
		// The same shape with a tail, so the disagreement survives past a consumed character.
		'(B|BA)!(BA)X|BA!!X',
		// Four-character near misses bracketing the five-character violations above.
		'A!!B',
		'BA!!',
		'A!?A',
		'A!!A',
		// A '?' three tokens away is the whole difference between this and AB!!B?C.
		'AB!!BC',
		// No '!' at all, so the transduction is the identity.
		'\\A\\A?\\9\\X?\\s\\9\\A\\A',
		// The postcode: the space appears in neither \X nor \9, so nothing can be confused.
		'\\A\\A?\\9\\X?\\s!!\\9\\A\\A',
	];

	for (const naxp of cases) {
		assert.equal(w3(naxp), null, `${naxp} was refused`);
	}
});

test('A!!A? breaks W3 and A!!A does not', () => {
	// They look interchangeable and are not. Kept on its own because it is the pair most likely
	// to be broken by a careless change.
	assert.equal(ruleOf(w3('A!!A?').message), 'W3');
	assert.equal(w3('A!!A'), null);
});

test('a naxp with no replaceable element is passed without building anything', () => {
	// Without a '!' the transduction is the identity, which is single valued for nothing.
	const { ast } = tryParse('\\A\\9{3}');

	assert.equal(checkW3(ast, new RxFactory(), { hasReplaceable: false }), null);
});

// #endregion
// #region Cost

test('the ill formed blow-up family is diagnosed within a small budget', () => {
	// A subset construction over sets of branches passes 100 000 configurations here before it
	// can even reach its first acceptance check; the square settles it in a few dozen pair
	// states.
	const error = w3('([ab]|[ab]!a){17}', 2000);

	assert.ok(error !== null, 'accepted');
	assert.equal(ruleOf(error.message), 'W3');
});

test('the well formed blow-up family is accepted within a small budget', () => {
	// This is the case that killed the subset construction: both machines have fewer than forty
	// states, yet a determinisation needs 2^17, so a legal naxp would have been rejected.
	assert.equal(w3('[ab]{17}c|([ab]!a){17}d', 2000), null);
});

test('beyond the budget is an implementation limit, not a verdict', () => {
	const error = w3('[ab]{17}c|([ab]!a){17}d', 8);

	assert.ok(error !== null, 'accepted');
	assert.equal(ruleOf(error.message), 'ImplementationLimit');
	assert.ok(error.text.includes('pair states'), error.text);
	assert.ok(error.text.includes('may well be legal'), error.text);
});

test('the two implementation limits say different things', () => {
	// Running out of pair states and abandoning an intermediate result are different failures,
	// and neither message may claim to be the other.
	const budget = w3('[ab]{17}c|([ab]!a){17}d', 8);

	assert.ok(budget.text.includes('pair states'), budget.text);
	assert.ok(!budget.text.includes('intermediate output'), budget.text);
});

// #endregion
