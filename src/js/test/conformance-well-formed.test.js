// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { Naxp } from '../lib/naxp.js';
import { tryParse } from '../lib/parser.js';
import { RxFactory } from '../lib/rx.js';
import { checkW3 } from '../lib/w3-checker.js';
import { check } from '../lib/well-formedness.js';
import { ruleOf } from './naxp-message-rules.js';
import { loadConformanceData } from './conformance.js';

const data = loadConformanceData();

/**
 * The rules the parser decides, at the point the tokens are read.
 */
const PARSER_RULES = new Map([
	['syntax', 'syntax'],
	['W4', 'W4'],
]);

/**
 * The rules that need the finished tree. W3 needs the transduction as well, but it is reached the
 * same way and reports the same way, so it belongs here.
 */
const TREE_RULES = new Map([
	['W1', 'W1'],
	['W2', 'W2'],
	['W3', 'W3'],
]);

/**
 * The rules nothing here decides yet. W5 needs the size of the canonical language compared against
 * the cap, which is the compiler's job, so a naxp breaking it is currently accepted.
 */
const DEFERRED_RULES = new Set(['W5']);

/**
 * Runs everything that is written, in the order a compilation would.
 *
 * @param {string} naxp The source.
 * @returns {import('../lib/naxp-error.js').NaxpError | null} The refusal, or null.
 */
function refusal(naxp) {
	const { ast, error } = tryParse(naxp);

	if (ast === null) { return error; }

	return check(ast) ?? checkW3(ast, new RxFactory());
}

test('the test data is the version this port targets', () => {
	assert.equal(data.naxpVersion, '0.5');
	assert.equal(data.cases.length, 39);
	assert.equal(data.rejected.length, 41);
});

test('every rejection in the test data is tagged with a rule this port knows', () => {
	// If a rule appears that none of the three sets names, the counts below stop meaning
	// anything and the gaps go unnoticed.
	for (const item of data.rejected) {
		assert.ok(
			PARSER_RULES.has(item.rule) || TREE_RULES.has(item.rule)
				|| DEFERRED_RULES.has(item.rule),
			`${item.naxp} is tagged ${item.rule}, which this test does not account for`);
	}
});

test('every well-formed naxp in the test data parses and passes W1, W2 and W3', () => {
	const failures = [];

	for (const item of data.cases) {
		const error = refusal(item.naxp);

		if (error !== null) { failures.push(`${item.naxp} was refused: ${error}`); }
	}

	assert.deepEqual(failures, [], failures.join('\n'));
});

test('every naxp the test data refuses for syntax or W4 is refused by the parser, with that rule', () => {
	const failures = [];
	let checked = 0;

	for (const item of data.rejected) {
		const expected = PARSER_RULES.get(item.rule);

		if (expected === undefined) { continue; }

		++checked;

		const { ast, error } = tryParse(item.naxp);

		if (ast !== null) {
			failures.push(`${item.naxp} parsed, and the test data says ${item.rule}.`);
			continue;
		}

		if (ruleOf(error.message) !== expected) {
			failures.push(
				`${item.naxp} was refused as ${ruleOf(error.message)}, and the test data says ${item.rule}.`);
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.equal(checked, 31);
});

test('every naxp the test data refuses for W1, W2 or W3 parses, then fails that rule', () => {
	// Parsing has to succeed first. A parser that refused one of these would be refusing the
	// right naxp for the wrong reason, and the rule in the message would be a lie.
	const failures = [];
	let checked = 0;

	for (const item of data.rejected) {
		const expected = TREE_RULES.get(item.rule);

		if (expected === undefined) { continue; }

		++checked;

		const { ast, error: parseError } = tryParse(item.naxp);

		if (ast === null) {
			failures.push(`${item.naxp} was refused by the parser as ${ruleOf(parseError.message)}, `
				+ `and the test data says ${item.rule}.`);
			continue;
		}

		const error = check(ast) ?? checkW3(ast, new RxFactory());

		if (error === null) {
			failures.push(`${item.naxp} passed, and the test data says ${item.rule}.`);
			continue;
		}

		if (ruleOf(error.message) !== expected) {
			failures.push(
				`${item.naxp} was refused as ${ruleOf(error.message)}, and the test data says ${item.rule}.`);
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.equal(checked, 9);
});

test('the one naxp refused for W5 passes every rule these layers own, and the compiler refuses it', () => {
	// W5 counts the canonical language, so it cannot be decided before the machine is built.
	// These layers accepting it is right rather than a gap: the naxp breaks no rule they own.
	// Both halves are asserted, because either alone would pass with the rule missing entirely.
	const deferred = data.rejected.filter(item => DEFERRED_RULES.has(item.rule));

	assert.equal(deferred.length, 1);

	for (const item of deferred) {
		assert.equal(refusal(item.naxp), null, `${item.naxp} was refused before the compiler`);

		const { naxp, errorCode } = Naxp.tryParse(item.naxp);

		assert.equal(naxp, null, `${item.naxp} compiled`);
		assert.equal(errorCode, 'NAXP1047', item.naxp);
	}
});

test('every refusal points somewhere inside the source, or just past its end', () => {
	for (const item of data.rejected) {
		const error = refusal(item.naxp);

		if (error === null) { continue; }

		assert.ok(error.offset >= 0, `${item.naxp}: ${error.offset}`);
		assert.ok(error.offset <= item.naxp.length, `${item.naxp}: ${error.offset}`);
		assert.ok(error.text.length > 0, item.naxp);
	}
});
