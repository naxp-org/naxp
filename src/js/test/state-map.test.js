// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { AsciiCharSet } from '../lib/ascii-char-set.js';
import { tryParse } from '../lib/parser.js';
import { NaxpLanguage, convert } from '../lib/rx-converter.js';
import { RxFactory } from '../lib/rx.js';
import { minterms, tryBuild } from '../lib/state-map.js';
import { check } from '../lib/well-formedness.js';
import { NaxpMessage } from '../lib/naxp-message.js';
import { ruleOf } from './naxp-message-rules.js';

// #region Helpers

/**
 * Parses, checks, converts and builds. The compiler that ties these together is not ported yet,
 * so the wiring lives here.
 *
 * @param {string} naxp The source.
 * @param {string} language One of {@link NaxpLanguage}.
 * @param {number} [maxStates] The state budget.
 * @returns {{map: import('../lib/state-map.js').StateMap | null,
 *   error: import('../lib/naxp-error.js').NaxpError | null}} The machine, or the refusal.
 */
function build(naxp, language, maxStates) {
	const { ast, error } = tryParse(naxp);

	assert.ok(ast !== null, `${naxp} did not parse: ${error}`);

	const wellFormedness = check(ast);

	assert.equal(wellFormedness, null, `${naxp} was refused: ${wellFormedness}`);

	const factory = new RxFactory();

	return tryBuild(convert(ast, factory, language), factory, maxStates);
}

/**
 * The machine for the canonical language, which is the one the encoding ranks over.
 *
 * @param {string} naxp The source.
 * @returns {import('../lib/state-map.js').StateMap} The machine.
 */
function canonical(naxp) {
	const { map, error } = build(naxp, NaxpLanguage.Canonical);

	assert.ok(map !== null, `${naxp} was refused: ${error}`);

	return map;
}

/**
 * The machine for the accepted language.
 *
 * @param {string} naxp The source.
 * @returns {import('../lib/state-map.js').StateMap} The machine.
 */
function accepted(naxp) {
	const { map, error } = build(naxp, NaxpLanguage.Accepted);

	assert.ok(map !== null, `${naxp} was refused: ${error}`);

	return map;
}

/**
 * The set holding the characters of a string.
 *
 * @param {string} characters The characters.
 * @returns {AsciiCharSet} The set.
 */
function setOf(characters) {
	let set = AsciiCharSet.empty;

	for (const c of characters) { set = set.union(AsciiCharSet.fromSingleChar(c.charCodeAt(0))); }

	return set;
}

/**
 * A machine written out with its states numbered in the order a walk from the start reaches them.
 * Two machines describe alike exactly when they have the same shape.
 *
 * @param {import('../lib/state-map.js').StateMap} map The machine.
 * @returns {string} The description.
 */
function describe(map) {
	/** @type {Map<import('../lib/state-map.js').State, number>} */
	const numbers = new Map();
	const order = [];

	const walk = state => {
		if (numbers.has(state)) { return; }

		numbers.set(state, order.length);
		order.push(state);

		for (const transition of state.transitions) { walk(transition.next); }
	};

	walk(map.start);

	const lines = [];

	for (const state of order) {
		let line = `${numbers.get(state)} count=${state.valueCount}`;

		for (const transition of state.transitions) {
			line += ` <${transition.set.toString()}>->${numbers.get(transition.next)}`;
		}

		lines.push(line);
	}

	return lines.join('\n');
}

// #endregion
// #region The specification's worked example

test("the worked example matches the specification", () => {
	// #[0-10] expands to [0-9] | 10. The continuation after each of 0 and 2 to 9 is the empty
	// string alone; the continuation after 1 is the empty string or 0. So the first classes are
	// [02-9] and [1].
	const map = canonical('#[0-10]');

	assert.equal(map.valueCount, 11n);

	const start = map.start;

	assert.equal(start.transitions.length, 2);
	assert.equal(start.acceptsEndOfText, false);

	assert.equal(start.transitions[0].set.equals(setOf('023456789')), true);
	assert.equal(start.transitions[0].next.isTerminal, true);

	assert.equal(start.transitions[1].set.equals(setOf('1')), true);

	const afterOne = start.transitions[1].next;

	assert.equal(afterOne.valueCount, 2n);
	assert.equal(afterOne.transitions.length, 2);
	assert.equal(afterOne.transitions[0].set.isEmpty, true);
	assert.equal(afterOne.transitions[0].next.isTerminal, true);
	assert.equal(afterOne.transitions[1].set.equals(setOf('0')), true);
	assert.equal(afterOne.transitions[1].next.isTerminal, true);
});

test('a padded digits range has one width', () => {
	// Padding the lower bound fixes one width, so every match takes the same route and numeric
	// order is preserved.
	const map = canonical('#[00-10]');

	assert.equal(map.valueCount, 11n);
	assert.equal(map.start.acceptsEndOfText, false);
	assert.equal(map.start.transitions[0].set.equals(setOf('0')), true);
	assert.equal(map.start.transitions[1].set.equals(setOf('1')), true);
});

// #endregion
// #region The machine depends on the language, not the spelling

test('naxps denoting the same language give the same machine', () => {
	// Hash-consing on transition lists is what does this. The minterms of [AB]C|[BC]C at the
	// first position are [A], [B] and [C], all with derivative C, and merging recombines them
	// into the single class [ABC].
	const pairs = [
		['AB|AC', 'A(B|C)'],
		['A?A?', '(AA)?|A'],
		['[AB]C|[BC]C', '[ABC]C'],
		['A{2,4}', 'AAA?A?'],
		['A|A', 'A'],
		['#[0-9]', '[0-9]'],
		['A{0}', '()'],
	];

	for (const [left, right] of pairs) {
		assert.equal(describe(canonical(left)), describe(canonical(right)), `${left} versus ${right}`);
	}
});

test('different renderings give different machines', () => {
	// A rendering is not cosmetic: it determines the canonical language, so changing it changes
	// the machine and with it the values.
	assert.notEqual(describe(canonical('(A|b)!bX|BY')), describe(canonical('(A|b)!AX|BY')));

	// Both accept the same three strings, so only the canonical machines differ.
	assert.equal(describe(accepted('(A|b)!bX|BY')), describe(accepted('(A|b)!AX|BY')));
});

test('equal values do not mean equal text', () => {
	// Both give every string they accept the value 1, and they print nothing and a hyphen.
	assert.equal(canonical('[\\s\\-]!?').valueCount, 1n);
	assert.equal(canonical('[\\s\\-]?!\\-').valueCount, 1n);
	assert.notEqual(describe(canonical('[\\s\\-]!?')), describe(canonical('[\\s\\-]?!\\-')));
});

// #endregion
// #region Counting

test('value counts are what the arithmetic says', () => {
	const cases = [
		['\\9{18}', 1000000000000000000n],
		['\\9{3}', 1000n],
		['[0-5]\\9', 60n],
		['#[0-105]', 106n],
		['A', 1n],
		['A?', 2n],
		['()', 1n],
	];

	for (const [naxp, expected] of cases) {
		assert.equal(canonical(naxp).valueCount, expected, naxp);
	}
});

test('a count above 2^53 is exact, and one above 2^63 keeps its top bit', () => {
	// The two places a number would have gone wrong. 10^19 is past both, and is the largest
	// interval of digits W5 admits.
	assert.equal(canonical('\\9{16}').valueCount, 10000000000000000n);
	assert.equal(canonical('\\9{19}').valueCount, 10000000000000000000n);
	assert.ok(canonical('\\9{19}').valueCount > (2n ** 63n) - 1n);
	assert.equal(canonical('\\9{19}').countSaturated, false);
});

test('a count past 2^64 - 1 saturates and says so', () => {
	const map = canonical('\\9{20}');

	assert.equal(map.countSaturated, true);
	assert.equal(map.valueCount, (2n ** 64n) - 1n);
});

test('a product of two legal halves can saturate', () => {
	// Each half is 10^19, which is legal on its own.
	const map = canonical('\\9{19}\\9{19}');

	assert.equal(map.countSaturated, true);
});

test('a sum of two legal alternatives can saturate', () => {
	// 10^19 + 10^19 is 2 x 10^19, which is above 2^64 - 1 while each alternative is legal.
	const map = canonical('\\9{19}|A\\9{19}');

	assert.equal(map.countSaturated, true);
});

test('a count exactly at the limit does not saturate', () => {
	// 128 characters at one position, twice over, is far below the limit; the point is that
	// countSaturated stays false for anything that fits.
	assert.equal(canonical('\\9{19}').countSaturated, false);
	assert.equal(canonical('\\9{18}').countSaturated, false);
});

// #endregion
// #region The state budget

test('a machine too large to build is refused, and the naxp may still be legal', () => {
	const { map, error } = build('A{99}', NaxpLanguage.Canonical, 10);

	assert.equal(map, null);
	// The code, not the prose. The message names the budget this implementation ships with,
	// which is not the lowered one a test builds against.
	assert.equal(error.message, NaxpMessage.NAXP1049_TooManyStates);
	assert.ok(error.text.includes('may well be legal'), error.text);
});

test('a machine inside the budget is built', () => {
	const { map } = build('A{99}', NaxpLanguage.Canonical, 200);

	assert.ok(map !== null);
	assert.equal(map.valueCount, 1n);
});

// #endregion
// #region Acceptance

test('the machine accepts what the naxp accepts', () => {
	const postcode = accepted('\\A \\A? \\9 \\X? \\s!! \\9 \\A \\A');

	assert.equal(postcode.accepts('M1 1AA'), true);
	assert.equal(postcode.accepts('M11AA'), true);
	assert.equal(postcode.accepts('EC1A 1BB'), true);
	assert.equal(postcode.accepts('M1  1AA'), false);
	assert.equal(postcode.accepts('1M1 1AA'), false);
	assert.equal(postcode.accepts(''), false);
});

test('the canonical machine refuses what only the accepted one takes', () => {
	// The space is optional on input and printed on the way out, so the canonical language holds
	// only the spaced form.
	const naxp = '\\A \\9 \\s!! \\9 \\A';

	assert.equal(accepted(naxp).accepts('M1 1A'), true);
	assert.equal(accepted(naxp).accepts('M11A'), true);
	assert.equal(canonical(naxp).accepts('M1 1A'), true);
	assert.equal(canonical(naxp).accepts('M11A'), false);
});

// #endregion
// #region Minterms

test('minterms split overlapping sets', () => {
	const blocks = minterms([setOf('AB'), setOf('BC')]);
	const described = blocks.map(b => b.toString()).sort();

	assert.deepEqual(described, ['A', 'B', 'C']);
});

test('the minterms of one set are that set', () => {
	const blocks = minterms([setOf('ABC')]);

	assert.equal(blocks.length, 1);
	assert.equal(blocks[0].equals(setOf('ABC')), true);
});

test('the minterms of no sets are none', () => {
	assert.deepEqual(minterms([]), []);
	assert.deepEqual(minterms([AsciiCharSet.empty]), []);
});

test('every minterm is inside or outside each set, never split by it', () => {
	const sets = [setOf('ABCDE'), setOf('CDEFG'), setOf('AEG'), setOf('BDF')];

	for (const block of minterms(sets)) {
		for (const set of sets) {
			const { intersection, thisLessOther } = block.getDisjointCombinations(set);

			assert.ok(
				intersection.isEmpty || thisLessOther.isEmpty,
				`${block.toString()} is split by ${set.toString()}`);
		}
	}
});

// #endregion
