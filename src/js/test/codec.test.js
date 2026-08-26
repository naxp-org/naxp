// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { encode, tryDecode } from '../lib/codec.js';
import { tryParse } from '../lib/parser.js';
import { NaxpLanguage, convert } from '../lib/rx-converter.js';
import { RxFactory } from '../lib/rx.js';
import { tryBuild } from '../lib/state-map.js';
import { checkW3 } from '../lib/w3-checker.js';
import { check } from '../lib/well-formedness.js';

const POSTCODE = '\\A \\A? \\9 \\X? \\s!! \\9 \\A \\A';

/**
 * The machine for a naxp's canonical language, which is the one the encoding ranks.
 *
 * @param {string} naxp The source.
 * @returns {import('../lib/state-map.js').StateMap} The machine.
 */
function canonicalMap(naxp) {
	const { ast, error } = tryParse(naxp);

	assert.ok(ast !== null, `${naxp} did not parse: ${error}`);
	assert.equal(check(ast), null, `${naxp} failed W1 or W2`);

	const factory = new RxFactory();

	assert.equal(checkW3(ast, factory), null, `${naxp} failed W3`);

	const built = tryBuild(convert(ast, factory, NaxpLanguage.Canonical), factory);

	assert.ok(built.map !== null, `${naxp} has no machine: ${built.error}`);

	return built.map;
}

/**
 * Every string of a language, in the order the machine puts them.
 *
 * @param {import('../lib/state-map.js').StateMap} map The machine.
 * @returns {string[]} The language.
 */
function enumerate(map) {
	const found = [];
	const walk = (state, prefix) => {
		for (const transition of state.transitions) {
			if (transition.set.isEmpty) { found.push(prefix); continue; }

			for (const code of transition.set) {
				walk(transition.next, prefix + String.fromCharCode(code));
			}
		}

		if (state.isTerminal) { found.push(prefix); }
	};

	walk(map.start, '');

	return found;
}

// #region The specification's worked values

test('an unpadded digits range puts the wider matches last', () => {
	// The case the Ordering section warns about: '1' sorts above '9' because it is the only
	// leading digit that can carry a second.
	const map = canonicalMap('#[0-10]');

	assert.equal(encode(map, '0'), 1n);
	assert.equal(encode(map, '9'), 9n);
	assert.equal(encode(map, '1'), 10n);
	assert.equal(encode(map, '10'), 11n);
});

test('padding the lower bound keeps numeric order', () => {
	// Every match then has the same width, so nothing can carry a second digit.
	const map = canonicalMap('#[00-10]');

	assert.equal(encode(map, '00'), 1n);
	assert.equal(encode(map, '09'), 10n);
	assert.equal(encode(map, '10'), 11n);
});

test('the order is neither lexicographic nor shortlex', () => {
	// In AB|B the first classes are [A] and [B], so AB precedes B although it is longer.
	const map = canonicalMap('AB|B');

	assert.equal(encode(map, 'AB'), 1n);
	assert.equal(encode(map, 'B'), 2n);
});

test('a string the machine does not accept encodes to zero', () => {
	assert.equal(encode(canonicalMap('#[0-10]'), '11'), 0n);
	assert.equal(encode(canonicalMap('#[0-10]'), ''), 0n);
	assert.equal(encode(canonicalMap('A'), 'B'), 0n);
});

// #endregion
// #region The worked example

test('the postcode values run from one to the count', () => {
	const map = canonicalMap(POSTCODE);

	assert.equal(map.valueCount, 1755842400n);
	assert.equal(encode(map, 'A0 0AA'), 1n);
	assert.equal(encode(map, 'ZZ9Z 9ZZ'), 1755842400n);
});

test('the worked postcodes take the values the specification states', () => {
	// These are the canonical forms, which is what the codec ranks. The tight spellings reach the
	// same value only after canonicalisation, which is the compiler's job rather than this one's.
	const map = canonicalMap(POSTCODE);
	const cases = [
		['M1 1AA', 810639597n],
		['CR2 6XH', 180591302n],
		['DN55 1PT', 238906246n],
		['W1A 1AA', 1486037957n],
		['EC1A 1BB', 277958384n],
	];

	for (const [text, expected] of cases) {
		assert.equal(encode(map, text), expected, text);
		assert.equal(tryDecode(map, expected), text, `${expected}`);
	}
});

test('a value outside the range decodes to nothing', () => {
	const map = canonicalMap(POSTCODE);

	// Zero is reserved for a string the naxp does not accept.
	assert.equal(tryDecode(map, 0n), null);
	assert.equal(tryDecode(map, -1n), null);
	assert.equal(tryDecode(map, 1755842401n), null);
});

// #endregion
// #region Rank and unrank are inverse

test('decoding and encoding are inverse across a whole language', () => {
	const naxps = [
		'#[0-10]',
		'#[00-10]',
		'AB|B',
		'\\A\\9?',
		'[ab]{0,3}',
		'(A|a)!A(B|b)!B',
		'\\X{2}',
		'(()|A)!(A)\\9',
		'A?B?C?',
		'#[0-105]',
	];

	let checked = 0;

	for (const naxp of naxps) {
		const map = canonicalMap(naxp);
		const count = map.valueCount;

		assert.ok(count <= 2000n, `${naxp} has ${count} values, too many to walk`);

		for (let value = 1n; value <= count; ++value) {
			++checked;

			const text = tryDecode(map, value);

			assert.notEqual(text, null, `${naxp} could not decode ${value}`);
			assert.equal(encode(map, text), value, `${naxp} at ${value} gave '${text}'`);
		}
	}

	assert.ok(checked > 500, `only ${checked} values were checked`);
});

test('the values are the position of the string in the machine order', () => {
	// The encoding is a rank, so walking the machine and numbering what comes out has to agree
	// with it. This is the whole of what the codec claims, checked without using the codec.
	for (const naxp of ['#[0-10]', 'AB|B', '\\A\\9?', '[ab]{0,3}', 'A?B?C?', '#[0-105]']) {
		const map = canonicalMap(naxp);
		const strings = enumerate(map);

		assert.equal(BigInt(strings.length), map.valueCount, `${naxp} enumerated the wrong count`);

		for (let i = 0; i < strings.length; ++i) {
			assert.equal(encode(map, strings[i]), BigInt(i + 1), `${naxp}: '${strings[i]}'`);
		}
	}
});

// #endregion
// #region Replacement

test('every string a replaceable element accepts takes the same value', () => {
	// They share a canonical form, so the canonical language holds one of them.
	const map = canonicalMap('(A|a)!A');

	assert.equal(map.valueCount, 1n);
	assert.equal(encode(map, 'A'), 1n);
});

test('the rendering decides the values', () => {
	// A rendering determines the canonical language, so changing it changes which value a string
	// takes even though the accepted language is the same.
	assert.equal(encode(canonicalMap('(A|b)!bX|BY'), 'BY'), 1n);
	assert.equal(encode(canonicalMap('(A|b)!AX|BY'), 'BY'), 2n);
});

// #endregion
