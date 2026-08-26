// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { containsReplaceable } from '../lib/ast.js';
import { tryCanonicalise as treeWalk } from '../lib/canonicaliser.js';
import { tryParse } from '../lib/parser.js';
import { RxFactory } from '../lib/rx.js';
import { tryBuildTxMachine } from '../lib/tx-machine.js';
import { TxFactory, convert as convertTx } from '../lib/tx.js';
import { checkW3 } from '../lib/w3-checker.js';
import { check } from '../lib/well-formedness.js';
import { loadConformanceData } from './conformance.js';

const data = loadConformanceData();

/**
 * The tree and, where the naxp holds a replaceable element, the canonicalisation machine.
 *
 * Without a `!` there is no machine, because ρ is the identity and a machine would only copy.
 *
 * @param {string} naxp The source.
 * @returns {{ast: import('../lib/ast.js').Ast,
 *   machine: import('../lib/tx-machine.js').TxMachine | null}} What was built.
 */
function build(naxp) {
	const { ast, error } = tryParse(naxp);

	assert.ok(ast !== null, `${naxp} did not parse: ${error}`);
	assert.equal(check(ast), null, `${naxp} failed W1 or W2`);

	const rxFactory = new RxFactory();

	assert.equal(checkW3(ast, rxFactory), null, `${naxp} failed W3`);

	if (!containsReplaceable(ast)) { return { ast, machine: null }; }

	const txFactory = new TxFactory(rxFactory);
	const built = tryBuildTxMachine(convertTx(ast, txFactory, rxFactory), txFactory);

	assert.ok(built.machine !== null, `${naxp} has no machine: ${built.error}`);

	return { ast, machine: built.machine };
}

test('the tree walk gives the canonical form the test data states', () => {
	const failures = [];
	let checked = 0;

	for (const item of data.cases) {
		const { ast } = build(item.naxp);

		for (const value of item.values) {
			if (value.out === '0' || value.canon === undefined) { continue; }

			++checked;

			const canonical = treeWalk(ast, value.in);

			if (canonical !== value.canon) {
				failures.push(
					`${item.naxp}: '${value.in}' walks to '${canonical}', `
					+ `and the test data says '${value.canon}'.`);
			}
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.ok(checked > 400, `only ${checked} strings were checked`);
});

test('the machine gives the canonical form the test data states', () => {
	// The same question of the other implementation of ρ. Only the naxps with a '!' have a
	// machine, and those are the only ones where the answer can differ from the input.
	const failures = [];
	let checked = 0;

	for (const item of data.cases) {
		const { machine } = build(item.naxp);

		if (machine === null) { continue; }

		for (const value of item.values) {
			if (value.out === '0' || value.canon === undefined) { continue; }

			++checked;

			const canonical = machine.tryCanonicalise(value.in);

			if (canonical !== value.canon) {
				failures.push(
					`${item.naxp}: '${value.in}' canonicalises to '${canonical}', `
					+ `and the test data says '${value.canon}'.`);
			}
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.ok(checked > 0, 'no naxp in the data had a machine');
});

test('a naxp with no replaceable element leaves every string alone', () => {
	// ρ is the identity there, so the canonical form is the input and the data agrees.
	let checked = 0;

	for (const item of data.cases) {
		const { ast } = build(item.naxp);

		if (containsReplaceable(ast)) { continue; }

		for (const value of item.values) {
			if (value.out === '0' || value.canon === undefined) { continue; }

			++checked;

			assert.equal(value.canon, value.in, `${item.naxp}: '${value.in}'`);
			assert.equal(treeWalk(ast, value.in), value.in, `${item.naxp}: '${value.in}'`);
		}
	}

	assert.ok(checked > 0, 'no naxp in the data was free of replaceable elements');
});

test('a string the test data marks as not accepted has no canonical form', () => {
	for (const item of data.cases) {
		const { ast, machine } = build(item.naxp);

		for (const refused of item.notAccepted) {
			assert.equal(treeWalk(ast, refused), null, `${item.naxp}: '${refused}'`);

			if (machine !== null) {
				assert.equal(machine.tryCanonicalise(refused), null, `${item.naxp}: '${refused}'`);
			}
		}
	}
});
