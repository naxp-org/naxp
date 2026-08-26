// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import {
	ALL_DIGITS,
	ALL_DIGITS_AND_UPPER_CASE_LETTERS,
	ALL_LOWER_CASE_LETTERS,
	ALL_UPPER_CASE_LETTERS,
	AsciiCharSet,
} from '../lib/ascii-char-set.js';

const CHARACTER_COUNT = AsciiCharSet.characterCount;

// #region Supporting stuff

/**
 * The set holding exactly the given codes, built one character at a time so that the union is
 * exercised rather than trusted.
 *
 * @param {number[]} codes The character codes.
 * @returns {AsciiCharSet} The set.
 */
function setOf(codes) {
	let charSet = AsciiCharSet.empty;

	for (const code of codes) { charSet = charSet.union(AsciiCharSet.fromSingleChar(code)); }

	return charSet;
}

/**
 * The set holding the characters of a string.
 *
 * @param {string} characters The characters.
 * @returns {AsciiCharSet} The set.
 */
function setOfString(characters) {
	return setOf([...characters].map(c => c.charCodeAt(0)));
}

/**
 * The codes from `min` to `max` inclusive.
 *
 * @param {number} min The first code.
 * @param {number} max The last code.
 * @returns {number[]} The codes.
 */
function rangeCodes(min, max) {
	const codes = [];

	for (let code = min; code <= max; ++code) { codes.push(code); }

	return codes;
}

/** A deterministic generator, so a failure is reproducible. */
function makeRandom(seed) {
	let state = seed | 0;

	return () => {
		state = (Math.imul(state, 1103515245) + 12345) | 0;

		return (state >>> 16) & 0x7fff;
	};
}

/**
 * Sets to test against, each paired with its characters in ascending order. The same list the C#
 * tests use, with the same seed and the same count of random sets.
 *
 * @returns {Array<{charSet: AsciiCharSet, reference: number[]}>} The samples.
 */
function sampleSets() {
	const samples = [];
	const add = codes => {
		const reference = [...codes].sort((a, b) => a - b);
		samples.push({ charSet: setOf(reference), reference });
	};

	add([]);
	add([0]);
	add([63]);
	add([64]);
	add([127]);
	add([63, 64]);
	add([0, 127]);
	add(rangeCodes(0, 127));
	add(rangeCodes(0x30, 0x39));
	add(rangeCodes(0x41, 0x5a));
	add(rangeCodes(0x61, 0x7a));
	add([0x61]);
	add([0x61, 0x62]);
	add([0x61, 0x62, 0x63]);
	add([0x61, 0x63]);
	add([0x62]);

	// The word boundaries of this implementation are at 32, 64 and 96, and only the middle one is
	// a boundary of the C# implementation too. These pin the two this port introduces.
	add([31, 32]);
	add([95, 96]);
	add([31, 32, 63, 64, 95, 96]);

	const random = makeRandom(20260810);

	for (let i = 0; i < 200; ++i) {
		const codes = [];

		for (let code = 0; code < CHARACTER_COUNT; ++code) {
			if (random() % 4 === 0) { codes.push(code); }
		}

		add(codes);
	}

	return samples;
}

/**
 * Asserts that a set holds exactly the expected codes.
 *
 * @param {number[]} expected The codes, in any order.
 * @param {AsciiCharSet} actual The set.
 * @param {string} what What is being checked, for the message.
 */
function assertSameCharacters(expected, actual, what) {
	assert.equal(actual.count, expected.length, `${what}: count`);

	for (let code = 0; code < CHARACTER_COUNT; ++code) {
		assert.equal(actual.contains(code), expected.includes(code), `${what}: contains(${code})`);
	}
}

/** The characters of a set as a string, which is the basis of the documented order. */
function asString(codes) {
	return String.fromCharCode(...codes);
}

/** An ordinal string comparison, reduced to its sign. */
function compareOrdinal(left, right) {
	if (left === right) { return 0; }

	return left < right ? -1 : 1;
}

// #endregion
// #region Construction

test('fromCharRange matches the reference for every range', () => {
	// Every range, checked character by character. This is what catches a shift count that
	// JavaScript has masked, because it covers 0, 31, 32, 63, 64, 95, 96 and 127 as both bounds.
	for (let min = 0; min < CHARACTER_COUNT; ++min) {
		for (let max = min; max < CHARACTER_COUNT; ++max) {
			const charSet = AsciiCharSet.fromCharRange(min, max);

			assert.equal(charSet.count, (max - min) + 1, `[${min},${max}] count`);

			for (let code = 0; code < CHARACTER_COUNT; ++code) {
				assert.equal(
					charSet.contains(code),
					code >= min && code <= max,
					`[${min},${max}] contains(${code})`);
			}
		}
	}
});

test('fromSingleChar matches the reference for every character', () => {
	for (let i = 0; i < CHARACTER_COUNT; ++i) {
		const charSet = AsciiCharSet.fromSingleChar(i);

		assert.equal(charSet.count, 1, `${i} count`);
		assert.equal(charSet.singleCharacter, i, `${i} singleCharacter`);

		for (let code = 0; code < CHARACTER_COUNT; ++code) {
			assert.equal(charSet.contains(code), code === i, `${i} contains(${code})`);
		}
	}
});

test('construction rejects a character outside ASCII', () => {
	assert.throws(() => AsciiCharSet.fromSingleChar(128), RangeError);
	assert.throws(() => AsciiCharSet.fromSingleChar(-1), RangeError);
	assert.throws(() => AsciiCharSet.fromSingleChar(1.5), RangeError);
	assert.throws(() => AsciiCharSet.fromCharRange(128, 129), RangeError);
	assert.throws(() => AsciiCharSet.fromCharRange(0x41, 200), RangeError);
});

test('fromCharRange rejects a highest first range', () => {
	// The bug fixed in NXOld: the parser must check this before calling in, and the contract here
	// is that it throws, so that a missing check cannot pass unnoticed.
	assert.throws(() => AsciiCharSet.fromCharRange(0x45, 0x41), RangeError);
});

test('the empty set has no characters', () => {
	const empty = AsciiCharSet.empty;

	assert.equal(empty.isEmpty, true);
	assert.equal(empty.count, 0);
	assert.equal(empty.singleCharacter, null);

	for (let code = 0; code < CHARACTER_COUNT; ++code) {
		assert.equal(empty.contains(code), false, `contains(${code})`);
		assert.equal(empty.indexOf(code), -1, `indexOf(${code})`);
	}
});

test('a character outside ASCII is never contained', () => {
	const all = AsciiCharSet.fromCharRange(0, 127);

	assert.equal(all.contains(128), false);
	assert.equal(all.contains('£'.charCodeAt(0)), false);
	assert.equal(all.indexOf(128), -1);
});

// #endregion
// #region Behaviour against a reference implementation

test('membership matches the reference', () => {
	for (const { charSet, reference } of sampleSets()) {
		assert.equal(charSet.count, reference.length, 'count');
		assert.equal(charSet.isEmpty, reference.length === 0, 'isEmpty');
		assert.equal(
			charSet.singleCharacter,
			reference.length === 1 ? reference[0] : null,
			'singleCharacter');

		for (let code = 0; code < CHARACTER_COUNT; ++code) {
			assert.equal(charSet.contains(code), reference.includes(code), `contains(${code})`);
			assert.equal(charSet.indexOf(code), reference.indexOf(code), `indexOf(${code})`);
		}
	}
});

test('characterAt inverts indexOf', () => {
	// Decoding needs the inverse of indexOf, so the two must agree both ways round.
	for (const { charSet, reference } of sampleSets()) {
		for (let i = 0; i < reference.length; ++i) {
			assert.equal(charSet.characterAt(i), reference[i], `characterAt(${i})`);
			assert.equal(charSet.indexOf(charSet.characterAt(i)), i, `indexOf of characterAt(${i})`);
		}

		assert.throws(() => charSet.characterAt(reference.length), RangeError);
		assert.throws(() => charSet.characterAt(-1), RangeError);
	}
});

test('iteration yields characters in ascending order', () => {
	for (const { charSet, reference } of sampleSets()) {
		assert.deepEqual([...charSet], reference);
	}
});

test('the set operations match the reference', () => {
	const samples = sampleSets();

	for (const { charSet: left, reference: leftReference } of samples) {
		for (const { charSet: right, reference: rightReference } of samples) {
			const union = [...new Set([...leftReference, ...rightReference])];
			const intersection = leftReference.filter(c => rightReference.includes(c));
			const difference = leftReference.filter(c => !rightReference.includes(c));

			assertSameCharacters(union, left.union(right), 'union');
			assertSameCharacters(intersection, left.intersect(right), 'intersect');
			assertSameCharacters(difference, left.subtract(right), 'subtract');

			assert.equal(left.intersectsWith(right), intersection.length !== 0, 'intersectsWith');

			const combinations = left.getDisjointCombinations(right);

			assert.equal(combinations.intersection.equals(left.intersect(right)), true);
			assert.equal(combinations.thisLessOther.equals(left.subtract(right)), true);
			assert.equal(combinations.otherLessThis.equals(right.subtract(left)), true);
		}
	}
});

test('compareTo matches ordinal string order', () => {
	// The documented order is that of the sets written out as strings and compared ordinally.
	const samples = sampleSets();

	for (const { charSet: left, reference: leftReference } of samples) {
		for (const { charSet: right, reference: rightReference } of samples) {
			const expected = compareOrdinal(asString(leftReference), asString(rightReference));
			const actual = Math.sign(left.compareTo(right));

			assert.equal(actual, expected, `${asString(leftReference)} vs ${asString(rightReference)}`);
		}
	}
});

test('compareTo orders the documented examples', () => {
	// [a] < [ab] < [abc] < [ac] < [b] < [c] < [cd]
	const ordered = ['a', 'ab', 'abc', 'ac', 'b', 'c', 'cd'].map(setOfString);

	for (let i = 0; i < ordered.length - 1; ++i) {
		assert.ok(ordered[i].compareTo(ordered[i + 1]) < 0, `item ${i} should sort before ${i + 1}`);
		assert.ok(ordered[i + 1].compareTo(ordered[i]) > 0, `item ${i + 1} should sort after ${i}`);
		assert.equal(ordered[i].compareTo(ordered[i]), 0, `item ${i} equals itself`);
	}
});

test('the empty set sorts before every other set', () => {
	// A prefix sorts first, and the empty string is a prefix of everything.
	for (const { charSet } of sampleSets()) {
		if (charSet.isEmpty) { continue; }

		assert.ok(AsciiCharSet.empty.compareTo(charSet) < 0, `empty vs ${charSet.key()}`);
		assert.ok(charSet.compareTo(AsciiCharSet.empty) > 0, `${charSet.key()} vs empty`);
	}
});

test('equality survives a different route to the same set', () => {
	const byRange = AsciiCharSet.fromCharRange(0x30, 0x39);
	const byUnion = setOf(rangeCodes(0x30, 0x39));

	assert.equal(byRange.equals(byUnion), true);
	assert.equal(byRange.key(), byUnion.key());
	assert.equal(byRange.compareTo(byUnion), 0);
	assert.equal(byRange.equals('not a char set'), false);
});

test('the key tells different sets apart', () => {
	const samples = sampleSets();
	const keys = new Map();

	for (const { charSet } of samples) {
		const key = charSet.key();
		const seen = keys.get(key);

		if (seen === undefined) { keys.set(key, charSet); continue; }

		assert.equal(seen.equals(charSet), true, `two different sets share the key ${key}`);
	}
});

// #endregion
// #region Named sets

test('the named sets hold the right characters', () => {
	assertSameCharacters(rangeCodes(0x30, 0x39), ALL_DIGITS, 'digits');
	assertSameCharacters(rangeCodes(0x41, 0x5a), ALL_UPPER_CASE_LETTERS, 'upper case');
	assertSameCharacters(rangeCodes(0x61, 0x7a), ALL_LOWER_CASE_LETTERS, 'lower case');
	assertSameCharacters(
		[...rangeCodes(0x30, 0x39), ...rangeCodes(0x41, 0x5a)],
		ALL_DIGITS_AND_UPPER_CASE_LETTERS,
		'digits and upper case');

	assert.equal(ALL_DIGITS.count, 10);
	assert.equal(ALL_UPPER_CASE_LETTERS.count, 26);
	assert.equal(ALL_LOWER_CASE_LETTERS.count, 26);
	assert.equal(ALL_DIGITS_AND_UPPER_CASE_LETTERS.count, 36);
});

// #endregion
