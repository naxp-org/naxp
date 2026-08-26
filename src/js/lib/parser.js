// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import {
	ALL_DIGITS,
	ALL_DIGITS_AND_UPPER_CASE_LETTERS,
	ALL_LOWER_CASE_LETTERS,
	ALL_UPPER_CASE_LETTERS,
	AsciiCharSet,
} from './ascii-char-set.js';
import {
	AstAlternation,
	AstChars,
	AstDigitsRange,
	AstEmpty,
	AstInterval,
	AstOptional,
	AstReplaceable,
	AstSequence,
	ReplaceableForm,
} from './ast.js';
import { NaxpError } from './naxp-error.js';
import { NaxpMessage } from './naxp-message.js';

/**
 * Returned by `peek` past the end of the source.
 *
 * Safe as a sentinel because `checkSourceCharacters` has already refused any source containing a
 * character outside whitespace and U+0021 to U+007E.
 */
const END_OF_TEXT = '\0';

/** The most digits an interval count may have. */
const MAX_INTERVAL_COUNT_DIGITS = 2;

/** The most digits a digits range bound may have. */
const MAX_BOUND_DIGITS = 15;

/**
 * Thrown by a production that has refused the source, and caught by {@link tryParse}.
 *
 * The C# implementation threads a `bool` and two `out` parameters through every production,
 * because it parses in a `ref struct` and will not allocate. Nothing here is measurable, parsing
 * happens once per naxp, and the two behave identically: the first refusal wins and no production
 * backtracks.
 */
class ParseFailure extends Error {
	/**
	 * @param {NaxpError} naxpError The refusal.
	 */
	constructor(naxpError) {
		super(naxpError.toString());
		this.name = 'ParseFailure';
		this.naxpError = naxpError;
	}
}

/**
 * A recursive descent parser for naxp version 0.5.
 *
 * The parser reports W4 as well as syntax, because the constraints on interval counts and digits
 * range bounds are decided at the point the tokens are read and nowhere else. W1 and W2 need the
 * finished tree. W3 and W5 need the state map.
 *
 * It carries error productions for syntax that is plausibly wrong rather than merely invalid, so
 * that the message names the mistake: a comma in an interval, an unbounded interval, a bare `x!`,
 * the hex escape that version 0.3 removed, and whitespace splitting a token.
 */
class Parser {
	/**
	 * @param {string} text The source of the naxp.
	 */
	constructor(text) {
		/** @type {string} */
		this.text = text;
		/** @type {number} */
		this.pos = 0;
	}

	// #region Productions

	/**
	 * Parses a whole naxp.
	 *
	 * @returns {import('./ast.js').Ast} The tree.
	 * @throws {ParseFailure} The source was refused.
	 */
	parseNaxp() {
		this.checkSourceCharacters();
		this.skipWhitespace();

		const expr = this.parseExpr();

		this.skipWhitespace();

		if (this.pos !== this.text.length) { throw this.unexpectedCharacter(); }

		return expr;
	}

	/** `expr ::= seq ( "|" seq )*` */
	parseExpr() {
		const start = this.pos;
		const first = this.parseSeq();

		/** @type {import('./ast.js').Ast[] | null} */
		let alternatives = null;

		this.skipWhitespace();

		while (this.peek() === '|') {
			this.advance();
			this.skipWhitespace();

			const next = this.parseSeq();

			if (alternatives === null) { alternatives = [first]; }

			alternatives.push(next);

			this.skipWhitespace();
		}

		if (alternatives === null) { return first; }

		return at(new AstAlternation(alternatives), start);
	}

	/** `seq ::= element+` */
	parseSeq() {
		const start = this.pos;

		/** @type {import('./ast.js').Ast | null} */
		let first = null;
		/** @type {import('./ast.js').Ast[] | null} */
		let elements = null;

		for (;;) {
			this.skipWhitespace();

			if (!isStartOfElement(this.peek())) { break; }

			const element = this.parseElement();

			if (first === null) {
				first = element;
			} else {
				if (elements === null) { elements = [first]; }

				elements.push(element);
			}
		}

		if (first === null) { throw this.noElementHere(); }

		if (elements === null) { return first; }

		return at(new AstSequence(elements), start);
	}

	/** `element ::= base quantifier? replaceable?` */
	parseElement() {
		const start = this.pos;

		let node = this.parseBase();
		let hasQuantifier = false;
		let hasOptional = false;

		this.skipWhitespace();

		if (this.peek() === '?') {
			this.advance();
			node = at(new AstOptional(node), start);
			hasQuantifier = true;
			hasOptional = true;
		} else if (this.peek() === '{') {
			node = this.parseInterval(node, start);
			hasQuantifier = true;
		}

		this.skipWhitespace();

		if (hasQuantifier && (this.peek() === '?' || this.peek() === '{')) {
			throw fail(NaxpMessage.NAXP1001_QuantifierRepeated, null, this.pos, 1);
		}

		if (this.peek() === '!') { node = this.parseReplaceable(node, start, hasOptional); }

		return node;
	}

	/** `base ::= char_set | digits_range | "(" expr? ")"` */
	parseBase() {
		const start = this.pos;
		const c = this.peek();

		if (c === '(') {
			this.advance();
			this.skipWhitespace();

			if (this.peek() === ')') {
				this.advance();

				return at(new AstEmpty(), start);
			}

			const inner = this.parseExpr();

			this.skipWhitespace();

			if (this.peek() !== ')') {
				throw fail(NaxpMessage.NAXP1009_GroupNotClosed, null, start, 1);
			}

			this.advance();

			return at(inner, start);
		}

		if (c === '#') { return this.parseDigitsRange(); }

		if (c === '[') { return at(new AstChars(this.parseBracketSet()), start); }

		return at(new AstChars(this.parseCharAtom().set), start);
	}

	/**
	 * `replaceable ::= "!" element | "!!" | "!?"`
	 *
	 * @param {import('./ast.js').Ast} subject The element the `!` binds to.
	 * @param {number} start The offset at which that element starts.
	 * @param {boolean} subjectIsOptional Whether the subject already carries a `?`.
	 * @returns {import('./ast.js').Ast} The replaceable element.
	 */
	parseReplaceable(subject, start, subjectIsOptional) {
		const bangOffset = this.pos;

		this.advance();

		// No whitespace is skipped here: '!!' and '!?' are single tokens.
		const next = this.peek();

		if (next === '!' || next === '?') {
			this.advance();

			if (subjectIsOptional) {
				throw fail(next === '!' ? NaxpMessage.NAXP1010_ReproducedAfterOptional : NaxpMessage.NAXP1011_DroppedAfterOptional, null, bangOffset, 2);
			}

			// The expansions are structural: x!! is x?!(x), and x!? is x?!().
			const optionalSubject = at(new AstOptional(subject), start);
			const rendering = next === '!' ? subject : at(new AstEmpty(), this.pos);
			const form = next === '!' ? ReplaceableForm.Reproduced : ReplaceableForm.Dropped;

			return at(new AstReplaceable(optionalSubject, rendering, form), start);
		}

		if (isWhitespace(next)) {
			const whitespaceOffset = this.pos;
			let lookahead = this.pos;

			while (lookahead < this.text.length && isWhitespace(this.text[lookahead])) { ++lookahead; }

			const afterWhitespace = lookahead < this.text.length
				? this.text[lookahead]
				: END_OF_TEXT;

			if (afterWhitespace === '!' || afterWhitespace === '?') {
				throw fail(afterWhitespace === '!' ? NaxpMessage.NAXP1012_ReproducedSplit : NaxpMessage.NAXP1013_DroppedSplit, null, whitespaceOffset, lookahead - whitespaceOffset);
			}
		}

		this.skipWhitespace();

		if (!isStartOfElement(this.peek())) {
			throw fail(NaxpMessage.NAXP1014_ReplacementMissing, null, bangOffset, 1);
		}

		const explicitRendering = this.parseElement();

		return at(
			new AstReplaceable(subject, explicitRendering, ReplaceableForm.Explicit),
			start);
	}

	/**
	 * `interval ::= "{" digits ( "-" digits )? "}"`
	 *
	 * @param {import('./ast.js').Ast} child The element repeated.
	 * @param {number} start The offset at which that element starts.
	 * @returns {import('./ast.js').Ast} The interval.
	 */
	parseInterval(child, start) {
		const braceOffset = this.pos;

		this.advance();
		this.skipWhitespace();

		const minCount = this.parseIntervalCount();

		this.skipWhitespace();

		let maxCount = minCount;

		if (this.peek() === '-') {
			throw fail(NaxpMessage.NAXP1002_IntervalHyphen, null, this.pos, 1);
		}

		if (this.peek() === ',') {
			this.advance();
			this.skipWhitespace();

			if (!isDigit(this.peek())) {
				throw fail(NaxpMessage.NAXP1003_IntervalUnbounded, null, this.pos, 1);
			}

			maxCount = this.parseIntervalCount();

			this.skipWhitespace();
		}

		if (this.peek() !== '}') {
			throw fail(NaxpMessage.NAXP1004_IntervalNotClosed, null, braceOffset, 1);
		}

		this.advance();

		if (minCount > maxCount) {
			throw fail(NaxpMessage.NAXP1007_IntervalCountsOutOfOrder, null, braceOffset, this.pos - braceOffset);
		}

		return at(new AstInterval(child, minCount, maxCount), start);
	}

	/** @returns {number} The count. */
	parseIntervalCount() {
		const start = this.pos;

		if (!isDigit(this.peek())) {
			throw fail(NaxpMessage.NAXP1005_IntervalCountNotDigits, null, this.pos, 1);
		}

		let count = 0;
		let digitCount = 0;

		while (isDigit(this.peek())) {
			if (digitCount < MAX_INTERVAL_COUNT_DIGITS) {
				count = (count * 10) + (this.peek().charCodeAt(0) - 0x30);
			}

			++digitCount;
			this.advance();
		}

		this.checkDigitRunNotSplit(NaxpMessage.NAXP1006_IntervalCountSplit);

		if (digitCount > MAX_INTERVAL_COUNT_DIGITS) {
			throw fail(NaxpMessage.NAXP1008_IntervalCountTooLong, null, start, this.pos - start);
		}

		return count;
	}

	/** `digits_range ::= "#[" digits "-" digits "]"` */
	parseDigitsRange() {
		const start = this.pos;

		this.advance();

		// '#[' is one token, so no whitespace is skipped between the two characters.
		if (this.peek() !== '[') {
			throw isWhitespace(this.peek())
				? fail(NaxpMessage.NAXP1015_HashSplitFromBracket, null, this.pos, 1)
				: fail(NaxpMessage.NAXP1016_HashWithoutBracket, null, start, 1);
		}

		this.advance();
		this.skipWhitespace();

		// Leading zeros in the lower bound are the point of it: they set a minimum width.
		const low = this.parseBound();

		this.skipWhitespace();

		if (this.peek() !== '-') {
			throw fail(NaxpMessage.NAXP1017_DigitsRangeBoundsSeparator, null, this.pos, 1);
		}

		this.advance();
		this.skipWhitespace();

		const high = this.parseBound();

		this.skipWhitespace();

		if (this.peek() !== ']') {
			throw fail(NaxpMessage.NAXP1018_DigitsRangeNotClosed, null, start, 1);
		}

		this.advance();

		if (low.digitCount > high.digitCount) {
			throw fail(NaxpMessage.NAXP1021_LowerBoundWiderThanUpper, null, start, this.pos - start);
		}

		if (high.digitCount > low.digitCount && high.hasLeadingZero) {
			throw fail(NaxpMessage.NAXP1022_UpperBoundLeadingZeros, null, start, this.pos - start);
		}

		if (low.value > high.value) {
			throw fail(NaxpMessage.NAXP1023_LowerBoundExceedsUpper, null, start, this.pos - start);
		}

		return at(
			new AstDigitsRange(low.value, low.digitCount, high.value, high.digitCount),
			start);
	}

	/**
	 * Reads one bound of a digits range.
	 *
	 * @returns {{value: number, digitCount: number, hasLeadingZero: boolean}} The bound.
	 */
	parseBound() {
		const start = this.pos;

		if (!isDigit(this.peek())) {
			throw fail(NaxpMessage.NAXP1019_DigitsRangeBoundNotDigits, null, this.pos, 1);
		}

		const firstDigit = this.peek();

		let value = 0;
		let digitCount = 0;

		while (isDigit(this.peek())) {
			// Accumulation stops at the cap, so a run long enough to lose precision is refused
			// below rather than silently mis-read on the way in.
			if (digitCount < MAX_BOUND_DIGITS) {
				value = (value * 10) + (this.peek().charCodeAt(0) - 0x30);
			}

			++digitCount;
			this.advance();
		}

		this.checkDigitRunNotSplit(NaxpMessage.NAXP1020_DigitsRangeBoundSplit);

		if (digitCount > MAX_BOUND_DIGITS) {
			throw fail(NaxpMessage.NAXP1024_DigitsRangeBoundTooLong, null, start, this.pos - start);
		}

		return { value, digitCount, hasLeadingZero: digitCount > 1 && firstDigit === '0' };
	}

	/**
	 * `char_set ::= ... | "[" set_item+ "]"`
	 *
	 * @returns {AsciiCharSet} The characters.
	 */
	parseBracketSet() {
		const start = this.pos;

		this.advance();

		let result = AsciiCharSet.empty;
		let itemCount = 0;

		for (;;) {
			this.skipWhitespace();

			if (this.peek() === ']') {
				this.advance();
				break;
			}

			if (this.peek() === END_OF_TEXT) {
				throw fail(NaxpMessage.NAXP1025_CharacterSetNotClosed, null, start, 1);
			}

			const item = this.parseCharAtom();

			++itemCount;

			if (item.isBlockEscape) {
				result = result.union(item.set);
				continue;
			}

			this.skipWhitespace();

			if (this.peek() !== '-') {
				result = result.union(item.set);
				continue;
			}

			const hyphenOffset = this.pos;

			this.advance();
			this.skipWhitespace();

			const upper = this.parseCharAtom();

			if (upper.isBlockEscape) {
				throw fail(NaxpMessage.NAXP1026_RangeUpperBoundIsBlockEscape, null, hyphenOffset, 1);
			}

			if (upper.literalChar < item.literalChar) {
				throw fail(NaxpMessage.NAXP1027_RangeReversed, `${describeChar(upper.literalChar)}-${describeChar(item.literalChar)}`, hyphenOffset, 1);
			}

			result = result.union(AsciiCharSet.fromCharRange(
				item.literalChar.charCodeAt(0),
				upper.literalChar.charCodeAt(0)));
		}

		if (itemCount === 0) {
			throw fail(NaxpMessage.NAXP1028_CharacterSetEmpty, null, start, this.pos - start);
		}

		return result;
	}

	/**
	 * Reads one bare character, escape or block escape.
	 *
	 * `literalChar` is the single character the atom denotes, meaningful only when
	 * `isBlockEscape` is false. Only a literal character may bound a range.
	 *
	 * @returns {{set: AsciiCharSet, literalChar: string, isBlockEscape: boolean}} The atom.
	 */
	parseCharAtom() {
		const c = this.peek();

		if (c === '\\') {
			const backslashOffset = this.pos;

			this.advance();

			const escaped = this.peek();

			if (isWhitespace(escaped)) {
				throw fail(NaxpMessage.NAXP1029_BackslashBeforeWhitespace, null, this.pos, 1);
			}

			if (escaped === END_OF_TEXT) {
				throw fail(NaxpMessage.NAXP1030_BackslashWithoutEscape, null, backslashOffset, 1);
			}

			this.advance();

			switch (escaped) {
				case 's':
					return { set: AsciiCharSet.fromSingleChar(0x20), literalChar: ' ', isBlockEscape: false };
				case '9':
					return { set: ALL_DIGITS, literalChar: END_OF_TEXT, isBlockEscape: true };
				case 'A':
					return { set: ALL_UPPER_CASE_LETTERS, literalChar: END_OF_TEXT, isBlockEscape: true };
				case 'a':
					return { set: ALL_LOWER_CASE_LETTERS, literalChar: END_OF_TEXT, isBlockEscape: true };
				case 'X':
					return {
						set: ALL_DIGITS_AND_UPPER_CASE_LETTERS,
						literalChar: END_OF_TEXT,
						isBlockEscape: true,
					};
				default:
					break;
			}

			if (isReservedChar(escaped)) {
				return {
					set: AsciiCharSet.fromSingleChar(escaped.charCodeAt(0)),
					literalChar: escaped,
					isBlockEscape: false,
				};
			}

			throw undefinedEscape(escaped, backslashOffset);
		}

		if (isBareChar(c)) {
			this.advance();

			return {
				set: AsciiCharSet.fromSingleChar(c.charCodeAt(0)),
				literalChar: c,
				isBlockEscape: false,
			};
		}

		throw this.unexpectedCharacter();
	}

	// #endregion
	// #region Source scanning

	checkSourceCharacters() {
		for (let i = 0; i < this.text.length; ++i) {
			const c = this.text[i];

			if (isWhitespace(c) || (c >= '\x21' && c <= '\x7e')) { continue; }

			throw fail(NaxpMessage.NAXP1033_CharacterNotAllowed, codePointAsText(this.text.charCodeAt(i)), i, 1);
		}
	}

	/** @returns {string} The character at the position, or the end of text sentinel. */
	peek() {
		return this.pos < this.text.length ? this.text[this.pos] : END_OF_TEXT;
	}

	advance() {
		++this.pos;
	}

	skipWhitespace() {
		while (this.pos < this.text.length && isWhitespace(this.text[this.pos])) { ++this.pos; }
	}

	/**
	 * Refuses whitespace that splits a run of digits, which whitespace between tokens does not.
	 * Called immediately after the run has been read.
	 *
	 * @param {string} message Which refusal to give, since the two callers word it differently.
	 */
	checkDigitRunNotSplit(message) {
		if (!isWhitespace(this.peek())) { return; }

		const whitespaceOffset = this.pos;
		let lookahead = this.pos;

		while (lookahead < this.text.length && isWhitespace(this.text[lookahead])) { ++lookahead; }

		if (lookahead < this.text.length && isDigit(this.text[lookahead])) {
			throw fail(message, null, whitespaceOffset, lookahead - whitespaceOffset);
		}
	}

	// #endregion
	// #region Diagnostics

	/**
	 * The refusal for a position at which an element was required and none begins.
	 *
	 * @returns {ParseFailure} The refusal.
	 */
	noElementHere() {
		const c = this.peek();

		if (c === END_OF_TEXT) {
			return fail(NaxpMessage.NAXP1034_ElementRequired, null, this.pos, 0);
		}

		if (c === '|' || c === ')') {
			return fail(NaxpMessage.NAXP1035_AlternativeEmpty, null, this.pos, 1);
		}

		if (c === '!') {
			return fail(NaxpMessage.NAXP1036_ReplaceableWithoutElement, null, this.pos, 1);
		}

		return this.unexpectedCharacter();
	}

	/**
	 * The refusal for a character that cannot appear where it stands.
	 *
	 * @returns {ParseFailure} The refusal.
	 */
	unexpectedCharacter() {
		const c = this.peek();

		if (c === END_OF_TEXT) {
			return fail(NaxpMessage.NAXP1037_NaxpIncomplete, null, this.pos, 0);
		}

		return fail(isReservedChar(c) ? NaxpMessage.NAXP1038_ReservedCharacterHere : NaxpMessage.NAXP1039_CharacterHere, isReservedChar(c) ? c : describeChar(c), this.pos, 1);
	}

	// #endregion
}

// #region Free functions

/**
 * Stamps a node with the offset in the source at which it starts.
 *
 * @template {import('./ast.js').Ast} T
 * @param {T} node The node.
 * @param {number} offset The offset.
 * @returns {T} The same node.
 */
function at(node, offset) {
	node.sourceOffset = offset;

	return node;
}

/**
 * A refusal, ready to throw.
 *
 * @param {string} message Which refusal this is, a member of {@link NaxpMessage}.
 * @param {string | null} argument What the message interpolates, or null.
 * @param {number} offset The offset in the source at which the fault was found.
 * @param {string} message What is wrong.
 * @returns {ParseFailure} The refusal.
 */
function fail(message, argument, offset, length) {
	return new ParseFailure(new NaxpError(message, argument, offset, length));
}

/**
 * The refusal for a backslash followed by something that is not an escape.
 *
 * The span covers the backslash and what follows it, which is two characters whichever of the two
 * messages this gives.
 *
 * @param {string} escaped The character after the backslash.
 * @param {number} backslashOffset Where the backslash is.
 * @returns {ParseFailure} The refusal.
 */
function undefinedEscape(escaped, backslashOffset) {
	return escaped === 'x'
		? fail(NaxpMessage.NAXP1031_HexEscapeRemoved, null, backslashOffset, 2)
		: fail(NaxpMessage.NAXP1032_EscapeUndefined, escaped, backslashOffset, 2);
}

/**
 * Names a character the source may not hold, which is by definition one that cannot be shown.
 *
 * A surrogate is called out because the offset alone misleads there: the user typed one character
 * above the basic plane and this names half of it. Only the first half is ever reported, since
 * well-formed UTF-16 puts it before the second and the scan stops there.
 *
 * @param {number} code The character's code unit.
 * @returns {string} How to name it.
 */
function codePointAsText(code) {
	const hex = `U+${code.toString(16).toUpperCase().padStart(4, '0')}`;

	return code >= 0xD800 && code <= 0xDFFF ? `${hex} (part of a UTF-16 surrogate pair)` : hex;
}

/**
 * A character as a message names it.
 *
 * @param {string} c The character.
 * @returns {string} The description.
 */
function describeChar(c) {
	switch (c) {
		case ' ': return 'a space';
		case '\t': return 'a tab';
		case '\r': return 'a carriage return';
		case '\n': return 'a line feed';
		default: return `'${c}'`;
	}
}

// #endregion
// #region Character classes

function isWhitespace(c) {
	return c === ' ' || c === '\t' || c === '\r' || c === '\n';
}

function isDigit(c) {
	return c >= '0' && c <= '9';
}

function isReservedChar(c) {
	return c === '!' || c === '#' || c === '(' || c === ')' || c === ',' || c === '-'
		|| c === '?' || c === '[' || c === '\\' || c === ']' || c === '{' || c === '|'
		|| c === '}';
}

function isBareChar(c) {
	return c >= '\x21' && c <= '\x7e' && !isReservedChar(c);
}

function isStartOfElement(c) {
	return c === '\\' || c === '[' || c === '#' || c === '(' || isBareChar(c);
}

// #endregion

/**
 * Parses a naxp, checking syntax and W4.
 *
 * W1 and W2 need the finished tree and are checked elsewhere; W3 and W5 need the state map.
 *
 * @param {string} text The source of the naxp.
 * @returns {{ast: import('./ast.js').Ast | null, error: NaxpError | null}} The tree, or the
 * refusal. Exactly one of the two is null.
 */
export function tryParse(text) {
	try {
		return { ast: new Parser(text).parseNaxp(), error: null };
	} catch (thrown) {
		if (thrown instanceof ParseFailure) { return { ast: null, error: thrown.naxpError }; }

		throw thrown;
	}
}
