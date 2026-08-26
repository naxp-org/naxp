// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * Bit primitives over a 32 bit word.
 *
 * The C# implementation works in 64 bit words because .NET has an integer that wide. JavaScript
 * does not: its bitwise operators convert to a signed 32 bit integer first, so the natural word
 * here is half the size and there are twice as many of them. Using BigInt to keep the widths the
 * same would be slower for no gain, since nothing in a character set needs more than a bit per
 * character.
 *
 * Every function takes whatever a bitwise operator produced, which is a signed 32 bit integer, and
 * treats it as 32 unsigned bits.
 */

/**
 * The number of set bits in a word.
 *
 * @param {number} value The word, read as 32 unsigned bits.
 * @returns {number} The number of set bits, from 0 to 32.
 */
export function popCount(value) {
	// The usual SWAR sum: pairs, then nibbles, then a multiply that sums the bytes into the top
	// byte. `Math.imul` is used for the multiply because an ordinary `*` would produce a value
	// above 2^32 and the shift below would then have to truncate it.
	let bits = value >>> 0;

	bits = bits - ((bits >>> 1) & 0x55555555);
	bits = (bits & 0x33333333) + ((bits >>> 2) & 0x33333333);
	bits = (bits + (bits >>> 4)) & 0x0f0f0f0f;

	return Math.imul(bits, 0x01010101) >>> 24;
}

/**
 * The number of zero bits below the least significant set bit of a word, or 32 if the word is
 * zero.
 *
 * @param {number} value The word, read as 32 unsigned bits.
 * @returns {number} The trailing zero count, from 0 to 32.
 */
export function trailingZeroCount(value) {
	const bits = value | 0;

	if (bits === 0) { return 32; }

	// Isolating the lowest set bit leaves a single bit, and one less than that is a run of
	// exactly as many ones as there were trailing zeros. Where that single bit is bit 31 the
	// subtraction leaves the range of a signed 32 bit integer, which `popCount` folds back.
	return popCount((bits & -bits) - 1);
}
