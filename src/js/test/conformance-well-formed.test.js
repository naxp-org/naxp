// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { Naxp } from '../lib/naxp.js';
import { tryParse } from '../lib/parser.js';
import { RxFactory } from '../lib/rx.js';
import { checkW3 } from '../lib/w3-checker.js';
import { check } from '../lib/well-formedness.js';
import { ruleOf, ruleOfCode } from './naxp-message-rules.js';
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
 * The rules nothing here decides yet. W5 needs the size of the canonical language and W6 the size
 * of the machines, both of which are the compiler's job, so a naxp breaking either is accepted by
 * the layers below it.
 */
const DEFERRED_RULES = new Set(['W5', 'W6']);

/**
 * Runs everything that is written, in the order a compilation would.
 *
 * @param {string} naxp The pattern.
 * @returns {import('../lib/naxp-error.js').NaxpError | null} The fault, or null.
 */
function fault(naxp) {
	const { ast, error } = tryParse(naxp);

	if (ast === null) { return error; }

	return check(ast) ?? checkW3(ast, new RxFactory());
}

test('the test data is the version this port targets', () => {
	assert.equal(data.naxpVersion, '0.10');
	assert.equal(data.cases.length, 70);
	assert.equal(data.invalidNaxps.length, 63);
});

test('every invalid naxp in the test data is tagged with a rule this port knows', () => {
	// If a rule appears that none of the three sets names, the counts below stop meaning
	// anything and the gaps go unnoticed.
	for (const item of data.invalidNaxps) {
		assert.ok(
			PARSER_RULES.has(item.rule) || TREE_RULES.has(item.rule)
				|| DEFERRED_RULES.has(item.rule),
			`${item.naxp} is tagged ${item.rule}, which this test does not account for`);
	}
});

test('every well-formed naxp in the test data parses and passes W1, W2 and W3', () => {
	const failures = [];

	for (const item of data.cases) {
		const error = fault(item.naxp);

		if (error !== null) { failures.push(`${item.naxp} was invalid: ${error}`); }
	}

	assert.deepEqual(failures, [], failures.join('\n'));
});

test('every naxp the test data marks invalid for syntax or W4 is invalid to the parser, for that rule', () => {
	const failures = [];
	let checked = 0;

	for (const item of data.invalidNaxps) {
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
				`${item.naxp} was invalid as ${ruleOf(error.message)}, and the test data says ${item.rule}.`);
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.equal(checked, 46);
});

test('every naxp the test data marks invalid for W1, W2 or W3 parses, then fails that rule', () => {
	// Parsing has to succeed first. A parser that ruled one of these out would be doing so for the
	// right naxp for the wrong reason, and the rule in the message would be a lie.
	const failures = [];
	let checked = 0;

	for (const item of data.invalidNaxps) {
		const expected = TREE_RULES.get(item.rule);

		if (expected === undefined) { continue; }

		++checked;

		const { ast, error: parseError } = tryParse(item.naxp);

		if (ast === null) {
			failures.push(`${item.naxp} was found invalid by the parser as ${ruleOf(parseError.message)}, `
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
				`${item.naxp} was invalid as ${ruleOf(error.message)}, and the test data says ${item.rule}.`);
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.equal(checked, 14);
});

test('every naxp invalid under W5 or W6 passes every rule these layers own, and the compiler finds it', () => {
	// W5 counts the canonical language and W6 the machines, so neither can be decided before the
	// machines are built. These layers accepting them is right rather than a gap: the naxps break
	// no rule they own. Both halves are asserted, because either alone would pass with the rule
	// missing entirely.
	const deferred = data.invalidNaxps.filter(item => DEFERRED_RULES.has(item.rule));

	assert.equal(deferred.length, 3);

	for (const item of deferred) {
		assert.equal(fault(item.naxp), null, `${item.naxp} was invalid before the compiler`);

		const { naxp, errorCode } = Naxp.tryParse(item.naxp);

		assert.equal(naxp, null, `${item.naxp} compiled`);
		assert.equal(ruleOfCode(errorCode), item.rule, `${item.naxp} gave ${errorCode}`);
	}
});

test('every fault points somewhere inside the pattern, or just past its end', () => {
	for (const item of data.invalidNaxps) {
		const error = fault(item.naxp);

		if (error === null) { continue; }

		assert.ok(error.offset >= 0, `${item.naxp}: ${error.offset}`);
		assert.ok(error.offset <= item.naxp.length, `${item.naxp}: ${error.offset}`);
		assert.ok(error.text.length > 0, item.naxp);
	}
});
