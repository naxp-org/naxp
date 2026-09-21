// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { CEmitter } from './c-emitter.js';
import { Compilation, tryCompile } from './compiler.js';
import { CppEmitter } from './cpp-emitter.js';
import { CSharpEmitter } from './csharp-emitter.js';
import { NaxpValueType } from './emitter.js';
import { JavaScriptEmitter } from './javascript-emitter.js';
import { NaxpComparison } from './naxp-comparison.js';
import { NaxpLimits } from './naxp-limits.js';
import { OutputLanguage } from './output-language.js';
import {
	compareLanguages,
	firstDivergentValue as divergentValue,
	tryCompareEncodings,
} from './relations.js';
import { MAX_TUPLES } from './value-agreement.js';

/**
 * Why a naxp was invalid.
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
 * A byte of 0x80 or above becomes a character no naxp can name, so it is invalid further down
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
 * @param {unknown} pattern The argument.
 * @returns {string} The argument.
 */
function requirePatternString(pattern) {
	if (typeof pattern !== 'string') { throw new TypeError('pattern must be a string.'); }

	return pattern;
}

/**
 * @param {unknown} naxp The argument.
 * @param {string} name What the argument is called.
 * @returns {Naxp} The argument.
 */
function requireNaxp(naxp, name) {
	if (!(naxp instanceof Naxp)) { throw new TypeError(`${name} must be a Naxp.`); }

	return naxp;
}

/**
 * A naxp: an expression over ASCII strings that numbers the strings it accepts.
 *
 * A naxp accepts a set of strings and gives each one an encoded value from 1 upwards, with zero
 * reserved for invalid text. The numbering is a property of the language rather than of how it was
 * written, so two naxps accepting the same strings number them alike.
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
		if (!(compilation instanceof Compilation)) {
			throw new TypeError('Use Naxp.parse or Naxp.tryParse rather than the constructor.');
		}

		this.#compilation = compilation;

		Object.freeze(this);
	}

	/**
	 * Parses a naxp.
	 *
	 * @param {string} pattern The pattern of the naxp.
	 * @returns {Naxp} The naxp.
	 * @throws {NaxpFormatError} The pattern is not a well-formed naxp.
	 */
	static parse(pattern) {
		const result = Naxp.tryParse(pattern);

		if (result.naxp !== null) { return result.naxp; }

		// The code and the span are in the message because a thrown error is all anybody gets:
		// there is nothing to read them from.
		const to = result.errorOffset + result.errorLength;

		throw new NaxpFormatError(
			`${result.errorCode} at ${result.errorOffset}..${to}: ${result.errorMessage}`);
	}

	/**
	 * Tries to parse a naxp, or says what is wrong, where, and why it is invalid.
	 *
	 * The C# splits this across two overloads, because out parameters make the short one worth
	 * having. Here there is one, since nothing is saved by leaving a field out of an object.
	 *
	 * @param {string} pattern The pattern of the naxp.
	 * @returns {{naxp: Naxp | null, errorMessage: string | null, errorOffset: number,
	 * errorLength: number, errorCode: string | null}} The naxp, or what is wrong and where.
	 * `errorMessage` is the reason alone; `errorLength` is the whole of `pattern` where the fault
	 * belongs to the naxp rather than to any one place in it; and `errorCode` is a stable
	 * identifier such as `NAXP1002`, for a log or a bug report rather than for
	 * branching on.
	 */
	static tryParse(pattern) {
		const { compilation, error } = tryCompile(requirePatternString(pattern));

		if (compilation !== null) {
			return {
				naxp: new Naxp(compilation),
				errorMessage: null,
				errorOffset: 0,
				errorLength: 0,
				errorCode: null,
			};
		}

		return {
			naxp: null,
			errorMessage: error.text,
			errorOffset: error.offset,

			// Only here is the length of the pattern known, so this is where a fault that named
			// no place in the naxp is given the whole of it.
			errorLength: error.isWholeNaxp ? pattern.length : error.length,
			errorCode: error.code,
		};
	}

	/** The pattern this naxp was parsed from. */
	get pattern() {
		return this.#compilation.pattern;
	}

	/**
	 * The largest encoded value this naxp produces, which is also how many it has.
	 *
	 * W5 caps this at 2^64 - 1, so a naxp with more encoded values than that is invalid rather
	 * than reported here.
	 */
	get maxEncodedValue() {
		return this.#compilation.maxEncodedValue;
	}

	/**
	 * Whether this naxp accepts a string.
	 *
	 * A byte outside ASCII makes the text invalid, since no naxp can name a character above U+007E.
	 *
	 * @param {string | Uint8Array} text The string or ASCII text to test.
	 * @returns {boolean} Whether the naxp accepts it.
	 */
	accepts(text) {
		const widened = asText(text);

		return widened === null ? false : this.#compilation.accepts(widened);
	}

	/**
	 * The encoded value of the text.
	 *
	 * Encoding cannot fail. Every rule was decided when the naxp was parsed, so the text either
	 * has exactly one encoded value or is invalid.
	 *
	 * @param {string | Uint8Array} text The string or ASCII text to encode.
	 * @returns {bigint} The encoded value, from 1 to {@link maxEncodedValue}, or zero if the text
	 * is invalid.
	 */
	encode(text) {
		const widened = asText(text);

		return widened === null ? 0n : this.#compilation.encode(widened);
	}

	/**
	 * The string an encoded value stands for, which is in canonical form.
	 *
	 * @param {bigint | number} value The encoded value, from 1 to {@link maxEncodedValue}.
	 * @returns {string} The string.
	 * @throws {RangeError} This naxp does not produce that encoded value.
	 */
	decode(value) {
		const text = this.tryDecode(value);

		if (text === null) {
			throw new RangeError(`This naxp encodes the values 1 to ${this.maxEncodedValue}.`);
		}

		return text;
	}

	/**
	 * Tries to find the string an encoded value stands for.
	 *
	 * @param {bigint | number} value The encoded value, from 1 to {@link maxEncodedValue}.
	 * @returns {string | null} The string, or null if this naxp does not produce that encoded value.
	 */
	tryDecode(value) {
		return this.#compilation.tryDecode(asValue(value));
	}

	/**
	 * The canonical form of a string, which is the string with the match of each unified
	 * element replaced by that element's rendering.
	 *
	 * A string and its canonical form encode to the same value, and decoding produces the
	 * canonical form.
	 *
	 * @param {string | Uint8Array} text The string or ASCII text.
	 * @returns {string | null} The canonical form, or null if the string is invalid.
	 */
	getCanonicalForm(text) {
		const widened = asText(text);

		return widened === null ? null : this.#compilation.tryGetCanonicalForm(widened);
	}

	/**
	 * How `b` stands to `a`: what happens to text and values held under `a` if `b` replaces it.
	 *
	 * Three set relationships, each from `a`'s point of view: the strings each accepts, the
	 * (text, value) pairs each defines, and the strings each prints. A change is safe for stored
	 * values exactly when `encoding` is `Equal` or `SubsetOf`. See {@link NaxpComparison} for how
	 * the three relate.
	 *
	 * The encoding relationship is decided by walking the two naxps together, which has a budget.
	 * No naxp anybody has reason to write comes near it, but a pair that does cannot be decided,
	 * and this throws rather than guess; {@link Naxp.tryCompare} returns null instead. The other
	 * two relationships are always decidable.
	 *
	 * @param {Naxp} a The naxp the data was encoded with.
	 * @param {Naxp} b The naxp proposed to replace it.
	 * @param {number} [budget] How many product states either walk may build, lowered by tests so
	 * the undecided path can be reached cheaply.
	 * @returns {NaxpComparison} The comparison.
	 * @throws {TypeError} Either argument is not a naxp.
	 * @throws {Error} The encoding relationship could not be decided within the budget.
	 */
	static compare(a, b, budget = MAX_TUPLES) {
		const comparison = Naxp.tryCompare(a, b, budget);

		if (comparison === null) {
			throw new Error(
				'The relationship between the encodings of these two naxps could not be decided '
				+ 'within the budget. Their accepted text and printed text can still be compared.');
		}

		return comparison;
	}

	/**
	 * Tries to find how `b` stands to `a`.
	 *
	 * The C# takes the comparison as an out parameter and returns whether it was decided. Here an
	 * undecided comparison is null, as an undecoded value is in {@link Naxp.tryDecode}, since
	 * nothing is saved by handing back a comparison that claims nothing.
	 *
	 * @param {Naxp} a The naxp the data was encoded with.
	 * @param {Naxp} b The naxp proposed to replace it.
	 * @param {number} [budget] How many product states either walk may build.
	 * @returns {NaxpComparison | null} The comparison, or null where it could not be decided. Only
	 * the encoding relationship can fail to be, when the walk that decides it outgrows its budget.
	 * @throws {TypeError} Either argument is not a naxp.
	 */
	static tryCompare(a, b, budget = MAX_TUPLES) {
		requireNaxp(a, 'a');
		requireNaxp(b, 'b');

		const left = a.#compilation;
		const right = b.#compilation;

		// First, because it is the one that can fail, and nothing is worth computing if it does.
		const { decided, relationship } = tryCompareEncodings(left, right, budget);

		if (!decided) { return null; }

		return new NaxpComparison(
			compareLanguages(left.accepted, right.accepted),
			relationship,
			compareLanguages(left.canonical, right.canonical));
	}

	/**
	 * The lowest value both naxps hold that they decode to different strings, or zero where every
	 * value both hold decodes alike.
	 *
	 * This is a different question from {@link Naxp.compare}, which is about the text going in;
	 * this is about the text coming out. `(A|B)!A` and `(A|B)!B` have equal encodings and diverge
	 * at 1, decoding it to `A` and to `B`. Decode the value under each naxp to see the two forms.
	 *
	 * Values only one naxp holds do not count, so a naxp extended by values that sort after all of
	 * its own gives zero however many were added; {@link maxEncodedValue} says the rest.
	 *
	 * @param {Naxp} a The first naxp.
	 * @param {Naxp} b The second naxp.
	 * @returns {bigint} The value, or zero for none.
	 * @throws {TypeError} Either argument is not a naxp.
	 */
	static firstDivergentValue(a, b) {
		requireNaxp(a, 'a');
		requireNaxp(b, 'b');

		return divergentValue(a.#compilation.canonical, b.#compilation.canonical);
	}

	/**
	 * Emits this naxp as source in the given language.
	 *
	 * What comes back is a fragment: the declarations answering the same questions this class
	 * does, for this one naxp, calling back into nothing. The paraphernalia around it, a header
	 * comment, imports, a module wrapper or a class, is the caller's, which is what lets one
	 * fragment land in a build step and on a web page alike.
	 *
	 * Everything a naxp decides is decided when it is compiled, so generated code carries no
	 * dependency on this library at all.
	 *
	 * @param {string} language The {@link OutputLanguage} to emit.
	 * @param {string} [prefix] The prefix every generated name starts with, so several naxps can
	 * share one scope. Empty gives the bare names. Each language applies its own casing
	 * convention to it.
	 * @param {string} [valueType] The {@link NaxpValueType} the generated code uses for encoded
	 * values. {@link Naxp#maxEncodedValue} must fit it.
	 * @param {string} [initialIndent] What every line is indented with ahead of its own depth, so
	 * the fragment can sit inside an already-indented wrapper.
	 * @param {string} [newLine] What ends every line. The caller's choice rather than the host's,
	 * so that one naxp gives the same fragment on every machine.
	 * @param {string} [indent] What one level of indentation is written as.
	 * @returns {string} The fragment.
	 * @throws {TypeError} The language is not an output language, the prefix is neither empty nor
	 * an ASCII identifier, or one of the formatting arguments is not a string.
	 * @throws {RangeError} The largest encoded value does not fit `valueType`.
	 */
	emit(
		language,
		prefix = '',
		valueType = NaxpValueType.UInt64,
		initialIndent = '',
		newLine = '\n',
		indent = '\t'
	) {
		const emitter = EMITTERS[language];

		if (emitter === undefined) {
			throw new TypeError(`'${language}' is not an output language.`);
		}

		return emitter().emit(this.#compilation, prefix, valueType, initialIndent, newLine, indent);
	}

	/** @returns {string} The pattern this naxp was parsed from. */
	toString() {
		return this.pattern;
	}
}

/**
 * The emitter for each output language, each one built on first use and then shared.
 *
 * The instances are reached through functions rather than held here, because an emitter's own
 * module holds the instance and this map is built before either has been asked for one.
 */
const EMITTERS = Object.freeze({
	[OutputLanguage.CSharp]: () => CSharpEmitter.instance,
	[OutputLanguage.JavaScript]: () => JavaScriptEmitter.instance,
	[OutputLanguage.C]: () => CEmitter.instance,
	[OutputLanguage.Cpp]: () => CppEmitter.instance,
});
