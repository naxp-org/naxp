// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { popCount, trailingZeroCount } from './bits.js';

/** How many characters a set can hold, that is 128. */
const CHARACTER_COUNT = 128;

/** How many words the bits are held in. */
const WORD_COUNT = 4;

/**
 * The bits from `from` to `to` inclusive, within one word.
 *
 * @param {number} from The lowest bit to set, from 0 to 31.
 * @param {number} to The highest bit to set, from `from` to 31.
 * @returns {number} The mask.
 */
function maskRange(from, to) {
	// A shift count of 32 would be masked to 0 and set every bit, so the top of the range is
	// special cased rather than written as ((1 << (to + 1)) - 1).
	const maskFrom = 0xffffffff << from;
	const maskTo = to === 31 ? 0xffffffff | 0 : (1 << (to + 1)) - 1;

	return maskFrom & maskTo;
}

/**
 * The bits below `index`, within one word.
 *
 * @param {number} index The bit below which to set, from 0 to 31.
 * @returns {number} The mask.
 */
function maskBelow(index) {
	// A shift count of 32 would be masked to 0 and set every bit, so zero is special cased.
	return index === 0 ? 0 : (0xffffffff >>> (32 - index)) | 0;
}

/**
 * The position of the `index`th set bit of a word, counting from zero.
 *
 * @param {number} word The word, which must hold more than `index` set bits.
 * @param {number} index How many set bits to skip.
 * @returns {number} The position, from 0 to 31.
 */
function setBitAt(word, index) {
	let remaining = word;

	// Clearing the lowest set bit is one operation, and the index is at most 31.
	for (let i = 0; i < index; ++i) { remaining &= remaining - 1; }

	return trailingZeroCount(remaining);
}

/**
 * An immutable set of ASCII characters, that is of characters in the range U+0000 to U+007F.
 *
 * Characters are named by their code rather than as one character strings, because every caller
 * has a code already: the parser reads the source with `charCodeAt`, and the encoder walks a
 * string the same way. Taking a string would mean allocating one per character tested.
 *
 * Internal rather than part of the published surface. Nothing on `Naxp` exposes a character set,
 * so exporting this would commit the package to thirty odd members no caller can reach. Widening
 * later is not a breaking change; narrowing would be.
 */
export class AsciiCharSet {
	/**
	 * Constructs a set from its four words, each holding 32 characters, least significant bit
	 * first.
	 *
	 * @param {number} word0 Characters U+0000 to U+001F.
	 * @param {number} word1 Characters U+0020 to U+003F.
	 * @param {number} word2 Characters U+0040 to U+005F.
	 * @param {number} word3 Characters U+0060 to U+007F.
	 */
	constructor(word0, word1, word2, word3) {
		/** @type {number} */
		this.word0 = word0 | 0;
		/** @type {number} */
		this.word1 = word1 | 0;
		/** @type {number} */
		this.word2 = word2 | 0;
		/** @type {number} */
		this.word3 = word3 | 0;
	}

	/** How many characters a set can hold, that is 128. */
	static get characterCount() { return CHARACTER_COUNT; }

	/** The empty set. */
	static get empty() { return EMPTY; }

	/**
	 * The set containing a single character.
	 *
	 * @param {number} code The character code. Must be ASCII.
	 * @returns {AsciiCharSet} The set containing just that character.
	 * @throws {RangeError} The code is not ASCII.
	 */
	static fromSingleChar(code) {
		requireAscii(code, 'code');

		const words = [0, 0, 0, 0];
		words[code >>> 5] = (1 << (code & 31)) | 0;

		return new AsciiCharSet(words[0], words[1], words[2], words[3]);
	}

	/**
	 * The set containing the inclusive character range [`minCode`, `maxCode`].
	 *
	 * @param {number} minCode The first character code in the range. Must be ASCII.
	 * @param {number} maxCode The last character code, not less than `minCode`. Must be ASCII.
	 * @returns {AsciiCharSet} The set containing the range.
	 * @throws {RangeError} A bound is not ASCII, or `minCode` is greater than `maxCode`.
	 */
	static fromCharRange(minCode, maxCode) {
		requireAscii(minCode, 'minCode');
		requireAscii(maxCode, 'maxCode');

		if (minCode > maxCode) {
			throw new RangeError('minCode cannot be greater than maxCode.');
		}

		const words = [0, 0, 0, 0];

		// Each word is masked against its own overlap with the range, which keeps every shift
		// count inside a word and so out of reach of the masking JavaScript applies at 32.
		for (let word = 0; word < WORD_COUNT; ++word) {
			const base = word * 32;
			const low = Math.max(minCode, base);
			const high = Math.min(maxCode, base + 31);

			if (low <= high) { words[word] = maskRange(low - base, high - base); }
		}

		return new AsciiCharSet(words[0], words[1], words[2], words[3]);
	}

	/**
	 * One of the four words, low to high.
	 *
	 * A method taking an index rather than a getter returning an array, because `contains` is the
	 * hottest thing in the library and an array would be allocated on every call.
	 *
	 * @param {number} index Which word, from 0 to 3.
	 * @returns {number} The word.
	 */
	wordAt(index) {
		switch (index) {
			case 0: return this.word0;
			case 1: return this.word1;
			case 2: return this.word2;
			default: return this.word3;
		}
	}

	/** Whether the set is empty. */
	get isEmpty() {
		return (this.word0 | this.word1 | this.word2 | this.word3) === 0;
	}

	/** The number of characters in the set, from 0 to 128. */
	get count() {
		return popCount(this.word0) + popCount(this.word1)
			+ popCount(this.word2) + popCount(this.word3);
	}

	/**
	 * If the set holds exactly one character then that character's code, otherwise `null`.
	 *
	 * @returns {number | null} The code, or `null`.
	 */
	get singleCharacter() {
		return this.count === 1 ? this.firstCharacterCode() : null;
	}

	/**
	 * Whether the set contains a character.
	 *
	 * @param {number} code The character code to test for membership.
	 * @returns {boolean} Whether the set contains it.
	 */
	contains(code) {
		if (code < 0 || code >= CHARACTER_COUNT) { return false; }

		return ((this.wordAt(code >>> 5) >>> (code & 31)) & 1) !== 0;
	}

	/**
	 * The zero based position of a character among the characters of the set taken in ascending
	 * order, or -1 if the set does not contain it.
	 *
	 * @param {number} code The character code whose position is wanted.
	 * @returns {number} The position, or -1.
	 */
	indexOf(code) {
		if (!this.contains(code)) { return -1; }

		const word = code >>> 5;
		let position = 0;

		for (let i = 0; i < word; ++i) { position += popCount(this.wordAt(i)); }

		return position + popCount(this.wordAt(word) & maskBelow(code & 31));
	}

	/**
	 * The character at a position among the characters of the set taken in ascending order. The
	 * inverse of {@link AsciiCharSet#indexOf}.
	 *
	 * @param {number} index The position wanted, from zero to one less than `count`.
	 * @returns {number} The code of the character at that position.
	 * @throws {RangeError} The set holds no character at that position.
	 */
	characterAt(index) {
		if (index < 0) { throw new RangeError('index cannot be negative.'); }

		let remaining = index;

		for (let word = 0; word < WORD_COUNT; ++word) {
			const bits = this.wordAt(word);
			const held = popCount(bits);

			if (remaining < held) { return (word * 32) + setBitAt(bits, remaining); }

			remaining -= held;
		}

		throw new RangeError(`The set holds no character at position ${index}.`);
	}

	/**
	 * Whether this set has any character in common with another.
	 *
	 * @param {AsciiCharSet} other The other set.
	 * @returns {boolean} Whether the two sets intersect.
	 */
	intersectsWith(other) {
		return ((this.word0 & other.word0) | (this.word1 & other.word1)
			| (this.word2 & other.word2) | (this.word3 & other.word3)) !== 0;
	}

	/**
	 * The characters in either set.
	 *
	 * @param {AsciiCharSet} other The other set.
	 * @returns {AsciiCharSet} The union.
	 */
	union(other) {
		return new AsciiCharSet(
			this.word0 | other.word0,
			this.word1 | other.word1,
			this.word2 | other.word2,
			this.word3 | other.word3);
	}

	/**
	 * The characters in both sets.
	 *
	 * @param {AsciiCharSet} other The other set.
	 * @returns {AsciiCharSet} The intersection.
	 */
	intersect(other) {
		return new AsciiCharSet(
			this.word0 & other.word0,
			this.word1 & other.word1,
			this.word2 & other.word2,
			this.word3 & other.word3);
	}

	/**
	 * The characters in this set but not in another.
	 *
	 * @param {AsciiCharSet} other The set to remove.
	 * @returns {AsciiCharSet} The difference.
	 */
	subtract(other) {
		return new AsciiCharSet(
			this.word0 & ~other.word0,
			this.word1 & ~other.word1,
			this.word2 & ~other.word2,
			this.word3 & ~other.word3);
	}

	/**
	 * The three disjoint combinations of this set and another, in the order intersection, this
	 * less other, other less this. Any of them may be empty.
	 *
	 * @param {AsciiCharSet} other The set to combine with this one.
	 * @returns {{intersection: AsciiCharSet, thisLessOther: AsciiCharSet, otherLessThis: AsciiCharSet}}
	 * The three disjoint combinations.
	 */
	getDisjointCombinations(other) {
		return {
			intersection: this.intersect(other),
			thisLessOther: this.subtract(other),
			otherLessThis: other.subtract(this),
		};
	}

	/**
	 * Whether two sets hold the same characters.
	 *
	 * @param {AsciiCharSet} other The set to compare with this one.
	 * @returns {boolean} Whether they are equal.
	 */
	equals(other) {
		return other instanceof AsciiCharSet
			&& this.word0 === other.word0 && this.word1 === other.word1
			&& this.word2 === other.word2 && this.word3 === other.word3;
	}

	/**
	 * Compares two sets in the order they would take if each were written out as the string of its
	 * characters in ascending order and the strings compared ordinally. So
	 * `[a]` < `[ab]` < `[abc]` < `[ac]` < `[b]`.
	 *
	 * @param {AsciiCharSet} other The set to compare with this one.
	 * @returns {number} A negative number, zero, or a positive number.
	 */
	compareTo(other) {
		if (this.equals(other)) { return 0; }

		// The lowest character at which the two sets differ. It exists, because they are not
		// equal.
		const firstDifference = firstSetBit(
			this.word0 ^ other.word0,
			this.word1 ^ other.word1,
			this.word2 ^ other.word2,
			this.word3 ^ other.word3);

		// Both sets agree below that character, so the comparison is settled by the next character
		// each of them holds at or above it. One of the two holds the differing character itself.
		const nextInThis = this.firstCharacterCodeAtOrAbove(firstDifference);
		const nextInOther = other.firstCharacterCodeAtOrAbove(firstDifference);

		// A set with nothing left is a prefix of the other, and a prefix sorts first.
		if (nextInThis === CHARACTER_COUNT) { return -1; }
		if (nextInOther === CHARACTER_COUNT) { return 1; }

		return nextInThis - nextInOther;
	}

	/**
	 * A string that is equal for equal sets and different for different ones, for use as a `Map`
	 * or `Set` key. JavaScript has no value equality for objects, so the places where the C#
	 * implementation uses a set as a dictionary key use this instead.
	 *
	 * @returns {string} The key.
	 */
	key() {
		return `${(this.word0 >>> 0).toString(16)}.${(this.word1 >>> 0).toString(16)}`
			+ `.${(this.word2 >>> 0).toString(16)}.${(this.word3 >>> 0).toString(16)}`;
	}

	/**
	 * The characters of the set in ascending order.
	 *
	 * @returns {Generator<number>} The character codes.
	 */
	*[Symbol.iterator]() {
		for (let code = this.firstCharacterCode(); code < CHARACTER_COUNT; ++code) {
			if (this.contains(code)) { yield code; }
		}
	}

	/**
	 * The characters of the set in ascending order, as a string.
	 *
	 * @returns {string} The characters.
	 */
	toString() {
		return String.fromCharCode(...this);
	}

	/**
	 * The lowest character in the set, or 128 if it is empty.
	 *
	 * @returns {number} The character code, from 0 to 128.
	 */
	firstCharacterCode() {
		return firstSetBit(this.word0, this.word1, this.word2, this.word3);
	}

	/**
	 * The lowest character in the set that is not below `index`, or 128 if there is none.
	 *
	 * @param {number} index The character code at or above which to look, from 0 to 127.
	 * @returns {number} The character code, from 0 to 128.
	 */
	firstCharacterCodeAtOrAbove(index) {
		const from = index >>> 5;

		for (let word = from; word < WORD_COUNT; ++word) {
			const bits = word === from
				? this.wordAt(word) & ~maskBelow(index & 31)
				: this.wordAt(word);

			if (bits !== 0) { return (word * 32) + trailingZeroCount(bits); }
		}

		return CHARACTER_COUNT;
	}
}

/**
 * The position of the lowest set bit across the four words, or 128 if all are zero.
 *
 * @param {number} word0 Characters U+0000 to U+001F.
 * @param {number} word1 Characters U+0020 to U+003F.
 * @param {number} word2 Characters U+0040 to U+005F.
 * @param {number} word3 Characters U+0060 to U+007F.
 * @returns {number} The position, from 0 to 128.
 */
function firstSetBit(word0, word1, word2, word3) {
	if (word0 !== 0) { return trailingZeroCount(word0); }
	if (word1 !== 0) { return 32 + trailingZeroCount(word1); }
	if (word2 !== 0) { return 64 + trailingZeroCount(word2); }
	if (word3 !== 0) { return 96 + trailingZeroCount(word3); }

	return CHARACTER_COUNT;
}

/**
 * Throws unless a character code is ASCII.
 *
 * @param {number} code The code to check.
 * @param {string} parameterName The name to put in the message.
 * @throws {RangeError} The code is not ASCII.
 */
function requireAscii(code, parameterName) {
	if (!Number.isInteger(code) || code < 0 || code >= CHARACTER_COUNT) {
		throw new RangeError(
			`${parameterName} must be an ASCII character code, that is an integer below 128.`);
	}
}

const EMPTY = new AsciiCharSet(0, 0, 0, 0);

/** The digits `0` to `9`, written `\9` in a naxp. */
export const ALL_DIGITS = AsciiCharSet.fromCharRange(0x30, 0x39);

/** The letters `A` to `Z`, written `\A` in a naxp. */
export const ALL_UPPER_CASE_LETTERS = AsciiCharSet.fromCharRange(0x41, 0x5a);

/** The letters `a` to `z`, written `\a` in a naxp. */
export const ALL_LOWER_CASE_LETTERS = AsciiCharSet.fromCharRange(0x61, 0x7a);

/** The digits and the upper case letters, written `\X` in a naxp. */
export const ALL_DIGITS_AND_UPPER_CASE_LETTERS = ALL_DIGITS.union(ALL_UPPER_CASE_LETTERS);
