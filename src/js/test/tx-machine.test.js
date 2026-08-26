// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCanonicalise as treeWalk } from '../lib/canonicaliser.js';
import { tryParse } from '../lib/parser.js';
import { NaxpLanguage, convert as convertRx } from '../lib/rx-converter.js';
import { RxFactory } from '../lib/rx.js';
import { tryBuild } from '../lib/state-map.js';
import { tryBuildTxMachine } from '../lib/tx-machine.js';
import { TxFactory, convert as convertTx } from '../lib/tx.js';
import { checkW3 } from '../lib/w3-checker.js';
import { check } from '../lib/well-formedness.js';
import { NaxpMessage } from '../lib/naxp-message.js';
import { ruleOf } from './naxp-message-rules.js';

// #region Helpers

/**
 * Parses, checks every rule that is written, and builds the canonicalisation machine.
 *
 * @param {string} naxp The source.
 * @param {number} [maxStates] The budget.
 * @returns {{ast: import('../lib/ast.js').Ast,
 *   machine: import('../lib/tx-machine.js').TxMachine | null,
 *   error: import('../lib/naxp-error.js').NaxpError | null}} What was built.
 */
function build(naxp, maxStates) {
	const { ast, error } = tryParse(naxp);

	assert.ok(ast !== null, `${naxp} did not parse: ${error}`);
	assert.equal(check(ast), null, `${naxp} failed W1 or W2`);

	const rxFactory = new RxFactory();

	assert.equal(checkW3(ast, rxFactory), null, `${naxp} failed W3`);

	const txFactory = new TxFactory(rxFactory);
	const root = convertTx(ast, txFactory, rxFactory);
	const built = tryBuildTxMachine(root, txFactory, maxStates);

	return { ast, machine: built.machine, error: built.error };
}

/**
 * The machine, failing the test where it could not be built.
 *
 * @param {string} naxp The source.
 * @returns {import('../lib/tx-machine.js').TxMachine} The machine.
 */
function machineFor(naxp) {
	const { machine, error } = build(naxp);

	assert.ok(machine !== null, `${naxp} has no machine: ${error}`);

	return machine;
}

/**
 * Every string the naxp accepts, which is finite because a naxp has no unbounded repetition.
 *
 * @param {import('../lib/ast.js').Ast} ast The tree.
 * @returns {string[]} The language.
 */
function enumerateLanguage(ast) {
	const factory = new RxFactory();
	const { map } = tryBuild(convertRx(ast, factory, NaxpLanguage.Accepted), factory);

	assert.ok(map !== null, 'the accepted machine could not be built');

	const found = [];
	const walk = (state, prefix) => {
		if (state.acceptsEndOfText) { found.push(prefix); }

		for (const transition of state.transitions) {
			if (transition.set.isEmpty) { continue; }

			for (const code of transition.set) {
				walk(transition.next, prefix + String.fromCharCode(code));
			}
		}
	};

	walk(map.start, '');

	return found;
}

// #endregion
// #region The machine against the tree walk

test('the machine agrees with the tree walk on every accepted string', () => {
	// The tree walk is the reference; the machine is the form the emitters need. They share the
	// matcher and nothing else, so agreeing on a whole language is a real check.
	const naxps = [
		'(A|a)!A',
		'\\A!?',
		'\\s!!X',
		'[\\s\\-]?!\\-',
		'[\\s\\-]!?',
		// A rendering decides the canonical language, so these two differ in what they emit.
		'(A|b)!bX|BY',
		'(A|b)!AX|BY',
		'(a|A)!AX|AY',
		// Replaceables under an interval, in sequence, and under an optional.
		'((A|a)!A){3}',
		'((A|a)!A|B){2}',
		'(AB|ab)!(AB)(C|c)!C',
		'X(\\s|\\-)!\\-Y',
		'(A|a)!A(B|b)!B(C|c)!C',
		'((A|a)!A)?B',
		'(A|a)!A|(B|b)!B',
		'(ABC|abc|AbC)!(ABC)',
		'\\9{2}(A|a)!A\\9{2}',
		'((AA|aa)!(AA)){2}',
		'(A|a)!A(B|b)!B|(A|a)!A(C|c)!C',
		'(\\A|\\s)!Q',
		// Subjects whose strings differ in length, so the machine owes output across steps.
		'(A|BB|CCC)!(BB)',
		'(A|BB|CCC)!(CCC)',
		'(A|BB|CCC)!A',
		// Subjects that accept the empty string, so a rendering is emitted for nothing read.
		'(()|A)!(A)',
		'(()|A)!()',
		'(()|AAAA)!(AAAA)',
		'((()|A)!(A)){3}',
		'(()|A)!(A)(()|B)!(B)',
		'X(()|AAA)!(AAA)X',
	];

	let checked = 0;

	for (const naxp of naxps) {
		const { ast } = tryParse(naxp);
		const machine = machineFor(naxp);

		for (const text of enumerateLanguage(ast)) {
			++checked;

			assert.equal(
				machine.tryCanonicalise(text),
				treeWalk(ast, text),
				`${naxp} on '${text}'`);
		}
	}

	assert.ok(checked > 200, `only ${checked} strings were compared`);
});

test('a string the naxp does not accept is refused', () => {
	const cases = [
		['(A|a)!A', 'B'],
		['(A|a)!A', ''],
		['(A|a)!A', 'AA'],
		['(A|BB|CCC)!(BB)', 'CC'],
		['(()|AAAA)!(AAAA)', 'AA'],
		['X(\\s|\\-)!\\-Y', 'XY'],
	];

	for (const [naxp, text] of cases) {
		assert.equal(machineFor(naxp).tryCanonicalise(text), null, `${naxp} on '${text}'`);
		assert.equal(treeWalk(tryParse(naxp).ast, text), null, `tree walk: ${naxp} on '${text}'`);
	}
});

// #endregion
// #region Shape

test('the transitions of a state are disjoint', () => {
	// Which is what lets a walk stop at the first set that holds the character.
	for (const naxp of ['(A|a)!A', '\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '(A|BB|CCC)!(BB)']) {
		for (const state of machineFor(naxp).states) {
			for (let i = 0; i < state.transitions.length; ++i) {
				for (let j = i + 1; j < state.transitions.length; ++j) {
					assert.equal(
						state.transitions[i].set.intersectsWith(state.transitions[j].set),
						false,
						`${naxp}: state ${state.id} has two transitions sharing a character`);
				}
			}
		}
	}
});

test('building the same naxp twice gives the same shape', () => {
	// Nothing in the construction depends on anything but the expression.
	const naxp = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const first = machineFor(naxp);
	const second = machineFor(naxp);

	assert.equal(first.states.length, second.states.length);

	for (let i = 0; i < first.states.length; ++i) {
		const left = first.states[i];
		const right = second.states[i];

		assert.equal(left.endOutput, right.endOutput, `state ${i} end output`);
		assert.equal(left.transitions.length, right.transitions.length, `state ${i} transitions`);

		for (let t = 0; t < left.transitions.length; ++t) {
			assert.equal(left.transitions[t].set.equals(right.transitions[t].set), true);
			assert.equal(left.transitions[t].output, right.transitions[t].output);
			assert.equal(left.transitions[t].next.id, right.transitions[t].next.id);
		}
	}
});

test('states that behave alike are merged', () => {
	// The builder shares a state only where two branch sets are equal, which is a property of the
	// construction rather than of behaviour. This is the smallest witness found: eight states
	// built, five after the merging pass.
	const machine = machineFor('A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)');

	assert.equal(machine.states.length, 5);
});

// #endregion
// #region The postcode

test('the postcode machine inserts the separator', () => {
	const machine = machineFor('\\A\\A?\\9\\X? \\s!! \\9\\A\\A');
	const cases = [
		['SW1A 1AA', 'SW1A 1AA'],
		['SW1A1AA', 'SW1A 1AA'],
		['M1 1AE', 'M1 1AE'],
		['M11AE', 'M1 1AE'],
		['CR2 6XH', 'CR2 6XH'],
		['DN55 1PT', 'DN55 1PT'],
	];

	for (const [input, expected] of cases) {
		assert.equal(machine.tryCanonicalise(input), expected, input);
	}
});

// #endregion
// #region Size, and the naxps that have no machine

test('the state count is exponential in the length of the naxp', () => {
	// Intrinsic rather than a weakness of this construction. Nothing before the final character
	// says which branch was taken, so the machine has to remember every character it has read in
	// order to emit them later. Both language machines stay small.
	for (const [k, expected] of [[2, 8], [3, 16], [4, 32]]) {
		const machine = machineFor(`[ab]{${k}}c|([ab]!a){${k}}d`);

		assert.equal(machine.states.length, expected, `k = ${k}`);
	}
});

test('a legal naxp can have no machine, and the refusal says which limit was hit', () => {
	// [ab]{16}c|([ab]!a){16}d passes every rule and compiles; the machine is the thing it cannot
	// have. That is not a duplicate of any rule, so the message must not name one.
	const { machine, error } = build('[ab]{6}c|([ab]!a){6}d', 8);

	assert.equal(machine, null);
	// The code, not the prose, since the message names the shipped budget rather than the
	// lowered one this test builds against.
	assert.equal(error.message, NaxpMessage.NAXP1050_TooManyCanonicalStates);
	assert.ok(error.text.includes('to canonicalise'), error.text);
});

// #endregion
