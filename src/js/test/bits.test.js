// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { popCount, trailingZeroCount } from '../lib/bits.js';

/**
 * A reference popcount that shares no working with the one under test.
 *
 * @param {number} value The word.
 * @returns {number} The number of set bits.
 */
function referencePopCount(value) {
	return (value >>> 0).toString(2).split('').filter(c => c === '1').length;
}

/**
 * A reference trailing zero count that shares no working with the one under test.
 *
 * @param {number} value The word.
 * @returns {number} The trailing zero count.
 */
function referenceTrailingZeroCount(value) {
	const bits = (value >>> 0).toString(2).padStart(32, '0');
	const lastOne = bits.lastIndexOf('1');

	return lastOne === -1 ? 32 : 31 - lastOne;
}

/** A deterministic generator, so a failure is reproducible. */
function* pseudoRandomWords(count) {
	let state = 20260821;

	for (let i = 0; i < count; ++i) {
		state = (Math.imul(state, 1103515245) + 12345) | 0;
		yield state;
	}
}

/** Every value that has ever broken a shift: the ends, the word boundary, and each single bit. */
function* interestingWords() {
	yield 0;
	yield 1;
	yield -1;
	yield 0x7fffffff;
	yield 0x80000000 | 0;
	yield 0xffffffff | 0;

	for (let bit = 0; bit < 32; ++bit) { yield (1 << bit) | 0; }
	for (let bit = 0; bit < 32; ++bit) { yield ~(1 << bit) | 0; }
}

test('popCount matches the reference for every interesting word', () => {
	for (const word of interestingWords()) {
		assert.equal(popCount(word), referencePopCount(word), `popCount(${word >>> 0})`);
	}
});

test('popCount matches the reference for pseudo random words', () => {
	for (const word of pseudoRandomWords(2000)) {
		assert.equal(popCount(word), referencePopCount(word), `popCount(${word >>> 0})`);
	}
});

test('popCount counts a single bit at every position', () => {
	for (let bit = 0; bit < 32; ++bit) {
		assert.equal(popCount((1 << bit) | 0), 1, `bit ${bit}`);
	}
});

test('trailingZeroCount matches the reference for every interesting word', () => {
	for (const word of interestingWords()) {
		assert.equal(
			trailingZeroCount(word),
			referenceTrailingZeroCount(word),
			`trailingZeroCount(${word >>> 0})`);
	}
});

test('trailingZeroCount matches the reference for pseudo random words', () => {
	for (const word of pseudoRandomWords(2000)) {
		assert.equal(
			trailingZeroCount(word),
			referenceTrailingZeroCount(word),
			`trailingZeroCount(${word >>> 0})`);
	}
});

test('trailingZeroCount finds a single bit at every position, including bit 31', () => {
	// Bit 31 is the case where isolating the lowest set bit and subtracting one leaves the range
	// of a signed 32 bit integer.
	for (let bit = 0; bit < 32; ++bit) {
		assert.equal(trailingZeroCount((1 << bit) | 0), bit, `bit ${bit}`);
	}
});

test('trailingZeroCount is 32 for zero', () => {
	assert.equal(trailingZeroCount(0), 32);
});
