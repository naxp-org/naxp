// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { tryCompile } from './compiler.js';
import { NaxpLimits } from './naxp-limits.js';

/**
 * Why a naxp was refused.
 *
 * The C# throws a `FormatException`, which JavaScript has no equivalent of: `SyntaxError` would be
 * a lie for a naxp that parses and then breaks W1, and nothing else in the language fits. So this
 * exists, and like the C# exception it carries the words and nothing else. Anything that wants the
 * code or the span asks {@link Naxp.tryParse} for them.
 */
export class NaxpFormatError extends Error {
	/**
	 * @param {string} message The code, the span and the reason.
	 */
	constructor(message) {
		super(message);

		this.name = 'NaxpFormatError';
	}
}

/**
 * The longest string widened from bytes, which is the longest any naxp within the state budget can
 * generate.
 */
const MAX_LENGTH = NaxpLimits.maxStringLength;

/**
 * Copies ASCII bytes into a string.
 *
 * A byte of 0x80 or above becomes a character no naxp can name, so it is refused further down
 * rather than needing a check here.
 *
 * @param {Uint8Array} bytes The bytes.
 * @returns {string} The string.
 */
function widen(bytes) {
	let text = '';

	// Chunked, because spreading a large array into String.fromCharCode overflows the argument
	// list. Nothing a naxp is for comes near the chunk size, so this is one pass in practice.
	for (let at = 0; at < bytes.length; at += 4096) {
		text += String.fromCharCode(...bytes.subarray(at, at + 4096));
	}

	return text;
}

/**
 * Accepts a string or ASCII bytes, and gives back a string or null where the bytes are too long
 * for any naxp to accept.
 *
 * @param {string | Uint8Array} text The string or ASCII text.
 * @returns {string | null} The string.
 */
function asText(text) {
	if (typeof text === 'string') { return text; }

	if (text instanceof Uint8Array) {
		return text.length > MAX_LENGTH ? null : widen(text);
	}

	throw new TypeError('text must be a string or a Uint8Array.');
}

/**
 * Accepts a value as a bigint or as a safe integer.
 *
 * Encoding always gives a bigint, so that a caller never has to know which naxp it holds. Decoding
 * takes either, because rejecting `naxp.decode(5)` would buy nothing.
 *
 * @param {bigint | number} value The value.
 * @returns {bigint} The value.
 */
function asValue(value) {
	if (typeof value === 'bigint') { return value; }

	if (typeof value === 'number') {
		if (!Number.isSafeInteger(value)) {
			throw new TypeError(
				'A value given as a number must be a safe integer. '
				+ 'Pass a bigint for values above 2^53 - 1.');
		}

		return BigInt(value);
	}

	throw new TypeError('value must be a bigint or a number.');
}

/**
 * @param {unknown} text The argument.
 * @returns {string} The argument.
 */
function requireString(text) {
	if (typeof text !== 'string') { throw new TypeError('text must be a string.'); }

	return text;
}

/**
 * A naxp: an expression over ASCII strings that numbers the strings it accepts.
 *
 * A naxp accepts a set of strings and gives each one a number from 1 upwards, with zero reserved
 * for a string it does not accept. The numbering is a property of the language rather than of how
 * it was written, so two naxps accepting the same strings number them alike.
 *
 * Every rule of the language is decided when the naxp is parsed. An instance of this type is
 * therefore a well-formed naxp, and no operation on it can fail for a reason of the naxp's own:
 * {@link encode} returns zero only because the string is not one this naxp accepts.
 *
 * Instances are immutable.
 */
export class Naxp {
	/** @type {import('./compiler.js').Compilation} */
	#compilation;

	/**
	 * Private. Use {@link Naxp.parse} or {@link Naxp.tryParse}.
	 *
	 * @param {import('./compiler.js').Compilation} compilation The compilation.
	 */
	constructor(compilation) {
		if (compilation === undefined) {
			throw new TypeError('Use Naxp.parse or Naxp.tryParse rather than the constructor.');
		}

		this.#compilation = compilation;

		Object.freeze(this);
	}

	/**
	 * Parses a naxp.
	 *
	 * @param {string} text The source of the naxp.
	 * @returns {Naxp} The naxp.
	 * @throws {NaxpFormatError} The source is not a well-formed naxp, or is one this
	 * implementation will not compile because of its size.
	 */
	static parse(text) {
		const result = Naxp.tryParse(text);

		if (result.naxp !== null) { return result.naxp; }

		// The code and the span are in the message because a thrown error is all anybody gets:
		// there is nothing to read them from.
		const to = result.errorTextOffset + result.errorTextLength;

		throw new NaxpFormatError(
			`${result.errorCode} at ${result.errorTextOffset}..${to}: ${result.errorMessage}`);
	}

	/**
	 * Tries to parse a naxp, or says what is wrong, where, and which refusal it is.
	 *
	 * The C# splits this across two overloads, because out parameters make the short one worth
	 * having. Here there is one, since nothing is saved by leaving a field out of an object.
	 *
	 * @param {string} text The source of the naxp.
	 * @returns {{naxp: Naxp | null, errorMessage: string | null, errorTextOffset: number,
	 * errorTextLength: number, errorCode: string | null}} The naxp, or what is wrong and where.
	 * `errorMessage` is the reason alone; `errorTextLength` is the whole of `text` where the fault
	 * belongs to the naxp rather than to any one place in it; and `errorCode` is a stable
	 * identifier such as `NAXP1002`, for a log or a bug report rather than for
	 * branching on.
	 */
	static tryParse(text) {
		const { compilation, error } = tryCompile(requireString(text));

		if (compilation !== null) {
			return {
				naxp: new Naxp(compilation),
				errorMessage: null,
				errorTextOffset: 0,
				errorTextLength: 0,
				errorCode: null,
			};
		}

		return {
			naxp: null,
			errorMessage: error.text,
			errorTextOffset: error.offset,

			// Only here is the length of the source known, so this is where a refusal that named
			// no place in the naxp is given the whole of it.
			errorTextLength: error.isWholeNaxp ? text.length : error.length,
			errorCode: error.code,
		};
	}

	/** The source this naxp was parsed from. */
	get source() {
		return this.#compilation.source;
	}

	/**
	 * The count of values this naxp encodes, which is the largest value it can produce.
	 *
	 * W5 caps this at 2^64 - 1, so a naxp with more values than that is refused rather than
	 * reported here.
	 */
	get valueCount() {
		return this.#compilation.valueCount;
	}

	/**
	 * Whether this naxp accepts a string.
	 *
	 * A byte outside ASCII is not accepted, since no naxp can name a character above U+007E.
	 *
	 * @param {string | Uint8Array} text The string or ASCII text to test.
	 * @returns {boolean} Whether the naxp accepts it.
	 */
	accepts(text) {
		const widened = asText(text);

		return widened === null ? false : this.#compilation.accepts(widened);
	}

	/**
	 * The value of a string.
	 *
	 * Encoding cannot fail. Every rule was decided when the naxp was parsed, so the string either
	 * has exactly one value or is not one this naxp accepts.
	 *
	 * @param {string | Uint8Array} text The string or ASCII text to encode.
	 * @returns {bigint} The value, from 1 to {@link valueCount}, or zero if the naxp does not
	 * accept the string.
	 */
	encode(text) {
		const widened = asText(text);

		return widened === null ? 0n : this.#compilation.encode(widened);
	}

	/**
	 * The string a value stands for, which is in canonical form.
	 *
	 * @param {bigint | number} value The value, from 1 to {@link valueCount}.
	 * @returns {string} The string.
	 * @throws {RangeError} The value is not one this naxp produces.
	 */
	decode(value) {
		const text = this.tryDecode(value);

		if (text === null) {
			throw new RangeError(`This naxp encodes the values 1 to ${this.valueCount}.`);
		}

		return text;
	}

	/**
	 * Tries to find the string a value stands for.
	 *
	 * @param {bigint | number} value The value, from 1 to {@link valueCount}.
	 * @returns {string | null} The string, or null if the value is not one this naxp produces.
	 */
	tryDecode(value) {
		return this.#compilation.tryDecode(asValue(value));
	}

	/**
	 * The canonical form of a string, which is the string with the match of each replaceable
	 * element replaced by that element's rendering.
	 *
	 * A string and its canonical form encode to the same value, and decoding produces the
	 * canonical form.
	 *
	 * @param {string | Uint8Array} text The string or ASCII text.
	 * @returns {string | null} The canonical form, or null if the naxp does not accept the string.
	 */
	getCanonicalForm(text) {
		const widened = asText(text);

		return widened === null ? null : this.#compilation.tryGetCanonicalForm(widened);
	}

	/** @returns {string} The source this naxp was parsed from. */
	toString() {
		return this.source;
	}
}
