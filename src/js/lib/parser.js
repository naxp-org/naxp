// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import {
	ALL_DIGITS,
	ALL_DIGITS_AND_LOWER_CASE_LETTERS,
	ALL_DIGITS_AND_UPPER_CASE_LETTERS,
	ALL_LOWER_CASE_LETTERS,
	ALL_UPPER_CASE_LETTERS,
	AsciiCharSet,
} from './ascii-char-set.js';
import {
	AstAlternation,
	AstChars,
	AstDecimalRange,
	AstEmpty,
	AstInterval,
	AstOptional,
	AstUnified,
	AstSequence,
	UnifiedForm,
} from './ast.js';
import { applyFold } from './folding.js';
import { expandPadding } from './padding.js';
import { NaxpError } from './naxp-error.js';
import { NaxpMessage } from './naxp-message.js';

/**
 * Returned by `peek` past the end of the pattern.
 *
 * Safe as a sentinel because `checkPatternCharacters` has already ruled out any pattern containing a
 * character outside whitespace and U+0021 to U+007E.
 */
const END_OF_TEXT = '\0';

/** The most digits an interval count may have. */
const MAX_INTERVAL_COUNT_DIGITS = 2;

/** The most digits a decimal range bound may have. */
const MAX_BOUND_DIGITS = 15;

/**
 * Thrown by a production that found the pattern invalid, and caught by {@link tryParse}.
 *
 * The C# implementation threads a `bool` and two `out` parameters through every production,
 * because it parses in a `ref struct` and will not allocate. Nothing here is measurable, parsing
 * happens once per naxp, and the two behave identically: the first fault wins and no production
 * backtracks.
 */
class ParseFailure extends Error {
	/**
	 * @param {NaxpError} naxpError The fault.
	 */
	constructor(naxpError) {
		super(naxpError.toString());
		this.name = 'ParseFailure';
		this.naxpError = naxpError;
	}
}

/**
 * A recursive descent parser for naxp version 0.10.
 *
 * The parser reports W4 as well as syntax, because the constraints on interval counts and digits
 * range bounds are decided at the point the tokens are read and nowhere else. W1 and W2 need the
 * finished tree. W3 and W5 need the state map.
 *
 * It carries error productions for syntax that is plausibly wrong rather than merely invalid, so
 * that the message names the mistake: a comma in an interval, an unbounded interval, a bare `x!`,
 * the hex escape naxp does not have, and whitespace splitting a token.
 */
class Parser {
	/**
	 * @param {string} text The pattern of the naxp.
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
	 * @throws {ParseFailure} The pattern was invalid.
	 */
	parseNaxp() {
		this.checkPatternCharacters();
		this.skipWhitespace();

		const expr = this.parseExpr();

		this.skipWhitespace();

		if (this.pos !== this.text.length) {
			// A ')' that closes a group is consumed by parseBase, so one still standing here
			// closes nothing. Calling it a reserved character and offering the escape is true
			// and is almost never what was meant.
			if (this.peek() === ')') {
				throw fail(NaxpMessage.NAXP1057_GroupNotOpened, null, this.pos, 1);
			}

			throw this.unexpectedCharacter();
		}

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

	/**
	 * `seq ::= element+ | element* case_fold expr`
	 *
	 * A case fold is the loosest operator: it runs from where it is written to the end of the
	 * enclosing group, across any `|`, the way a regex flag does. So a fold ends the sequence it
	 * is written in, taking the rest of the expression as its operand. Where one fold is written
	 * inside another the outer governs, so a run of folds is the first of them and the rest are
	 * consumed and dropped.
	 */
	parseSeq() {
		const start = this.pos;

		/** @type {import('./ast.js').Ast | null} */
		let first = null;
		/** @type {import('./ast.js').Ast[] | null} */
		let elements = null;

		for (;;) {
			this.skipWhitespace();

			const foldStart = this.pos;
			const fold = this.parseFold();

			if (fold !== null) {
				const rest = at(applyFold(this.parseExpr(), fold), foldStart);

				if (first === null) { return rest; }

				if (elements === null) { elements = [first]; }

				elements.push(rest);
				break;
			}

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

	/** `element ::= operand quantifier? text_unification?` */
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

		if (this.peek() === '!') { node = this.parseUnified(node, start, hasOptional); }

		return node;
	}

	/**
	 * `case_fold ::= "\C" | "\c"`, as many as are written.
	 *
	 * The fold is consumed here and expanded by {@link applyFold} once the expression it binds
	 * to has been parsed, so no later stage sees one. A run of folds is the first of them; see
	 * {@link parseSeq}.
	 *
	 * @returns {boolean | null} Whether upper case is canonical, or null where no fold is
	 *   written.
	 */
	parseFold() {
		let fold = null;

		for (let letter = this.peekFold(); letter !== null; letter = this.peekFold()) {
			this.advance();
			this.advance();
			this.skipWhitespace();

			if (fold === null) { fold = letter === 'C'; }
		}

		return fold;
	}

	/**
	 * Whether a fold starts here, without consuming it.
	 *
	 * @returns {string | null} The fold letter, `C` or `c`, or null where there is none.
	 */
	peekFold() {
		if (this.peek() !== '\\') { return null; }

		const next = this.pos + 1 < this.text.length ? this.text[this.pos + 1] : END_OF_TEXT;

		return next === 'C' || next === 'c' ? next : null;
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

		if (c === '#') { return this.parseDecimalRange(); }

		if (c === '[') { return at(new AstChars(this.parseBracketSet()), start); }

		return at(new AstChars(this.parseCharAtom().set), start);
	}

	/**
	 * `unified ::= "!" element | "!!" | "!?"`
	 *
	 * @param {import('./ast.js').Ast} subject The element the `!` binds to.
	 * @param {number} start The offset at which that element starts.
	 * @param {boolean} subjectIsOptional Whether the subject already carries a `?`.
	 * @returns {import('./ast.js').Ast} The unified element.
	 */
	parseUnified(subject, start, subjectIsOptional) {
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
			const form = next === '!' ? UnifiedForm.Reproduced : UnifiedForm.Dropped;

			return at(new AstUnified(optionalSubject, rendering, form), start);
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

		// A fold would run to the end of the group, and a fold with anything to do expands to a
		// `!`, which W2 refuses inside a rendering; so there is nothing a fold could usefully mean
		// here, and it is refused as syntax with a message that says what to write instead.
		if (this.peekFold() !== null) {
			throw fail(NaxpMessage.NAXP1058_FoldBeginsRendering, null, this.pos, 2);
		}

		if (!isStartOfElement(this.peek())) {
			throw fail(NaxpMessage.NAXP1014_RenderingMissing, null, bangOffset, 1);
		}

		const explicitRendering = this.parseElement();

		return at(
			new AstUnified(subject, explicitRendering, UnifiedForm.Explicit),
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

		if (maxCount === 0) {
			throw fail(NaxpMessage.NAXP1062_IntervalCountZero, null, braceOffset, this.pos - braceOffset);
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

	/** `digits_range ::= "#[" low_bound "-" digits "]"` */
	parseDecimalRange() {
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

		// Leading zeros in the lower bound are the point of it: they set a minimum width, and a
		// mark on one says that the width it sets is optional on input.
		const low = this.parseBound(true);

		this.skipWhitespace();

		if (this.peek() !== '-') {
			throw fail(NaxpMessage.NAXP1017_DecimalRangeBoundsSeparator, null, this.pos, 1);
		}

		this.advance();
		this.skipWhitespace();

		const high = this.parseBound(false);

		this.skipWhitespace();

		if (this.peek() !== ']') {
			throw fail(NaxpMessage.NAXP1018_DecimalRangeNotClosed, null, start, 1);
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

		// A mark stands for a padding zero, so it may sit only in front of the digits of the
		// value. Which positions those are is known once the bound has been read.
		if (low.marks !== null) {
			const at = markedInsideValue(low.value, low.digitCount, low.marks);

			// The zero and the mark after it, which is the thing that is wrong. Naming the whole
			// range would leave 'this zero' pointing at nothing.
			if (at >= 0) {
				throw fail(
					NaxpMessage.NAXP1055_DecimalRangeMarkNotPadding,
					null,
					low.digitOffsets[at],
					2);
			}
		}

		if (low.marks === null) {
			return at(
				new AstDecimalRange(low.value, low.digitCount, high.value, high.digitCount),
				start);
		}

		return expandPadding(
			low.value, low.digitCount, high.value, high.digitCount, low.marks, start);
	}

	/**
	 * Reads one bound of a decimal range.
	 *
	 * @param {boolean} allowMarks Whether a padding mark may follow a digit, which is so for the
	 *   lower bound alone.
	 * @returns {{value: number, digitCount: number, hasLeadingZero: boolean, marks: string[] | null}}
	 *   The bound. `marks` holds one entry per digit, the empty string where that digit carries no
	 *   mark, or is null where the bound carries no mark at all.
	 */
	parseBound(allowMarks) {
		const start = this.pos;

		if (!isDigit(this.peek())) {
			throw fail(NaxpMessage.NAXP1019_DecimalRangeBoundNotDigits, null, this.pos, 1);
		}

		const firstDigit = this.peek();

		let value = 0;
		let digitCount = 0;
		let marks = null;

		// Where each digit sits in the pattern, so that a fault on one can name it rather than
		// the whole range. A digit and its mark are one token, so these are not evenly spaced.
		const digitOffsets = [];

		while (isDigit(this.peek())) {
			const digit = this.peek();

			if (digitCount < MAX_BOUND_DIGITS) { digitOffsets.push(this.pos); }

			// Accumulation stops at the cap, so a run long enough to lose precision is invalid
			// below rather than silently mis-read on the way in.
			if (digitCount < MAX_BOUND_DIGITS) {
				value = (value * 10) + (digit.charCodeAt(0) - 0x30);
			}

			++digitCount;
			this.advance();

			// A mark is part of the digit's token, so nothing is skipped between the two.
			const mark = this.peek();

			if (mark !== '!' && mark !== '?') { continue; }

			if (!allowMarks) {
				throw fail(NaxpMessage.NAXP1056_DecimalRangeMarkOnUpperBound, null, this.pos, 1);
			}

			if (digit !== '0') {
				throw fail(NaxpMessage.NAXP1054_DecimalRangeMarkOnNonZero, null, this.pos, 1);
			}

			if (digitCount <= MAX_BOUND_DIGITS) {
				marks ??= new Array(MAX_BOUND_DIGITS).fill('');
				marks[digitCount - 1] = mark;
			}

			this.advance();
		}

		if (allowMarks) { this.checkMarkNotSplit(); }

		this.checkDigitRunNotSplit(NaxpMessage.NAXP1020_DecimalRangeBoundSplit);

		if (digitCount > MAX_BOUND_DIGITS) {
			throw fail(NaxpMessage.NAXP1024_DecimalRangeBoundTooLong, null, start, this.pos - start);
		}

		return {
			value,
			digitCount,
			hasLeadingZero: digitCount > 1 && firstDigit === '0',
			marks,
			digitOffsets,
		};
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
				throw fail(NaxpMessage.NAXP1027_RangeReversed, `${patternForChar(upper.literalChar)}-${patternForChar(item.literalChar)}`, hyphenOffset, 1);
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
				case 'x':
					return {
						set: ALL_DIGITS_AND_LOWER_CASE_LETTERS,
						literalChar: END_OF_TEXT,
						isBlockEscape: true,
					};
				case 'C':
				case 'c':
					// A fold before an element is taken in parseElement, so one reaching here is
					// inside a character set, where it means nothing.
					throw fail(NaxpMessage.NAXP1052_FoldInCharacterSet, escaped, backslashOffset, 2);
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
	// #region Pattern scanning

	checkPatternCharacters() {
		for (let i = 0; i < this.text.length; ++i) {
			const c = this.text[i];

			if (isWhitespace(c) || (c >= '\x21' && c <= '\x7e')) { continue; }

			throw fail(NaxpMessage.NAXP1032_CharacterNotAllowed, codePointAsText(this.text.charCodeAt(i)), i, 1);
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
	 * Refuses whitespace between a decimal range bound's digit and a padding mark on it.
	 *
	 * Called where the digit run has ended, which is where a mark separated from its digit leaves
	 * the parser: whitespace is not a digit, so the run stops in front of it.
	 */
	checkMarkNotSplit() {
		if (!isWhitespace(this.peek())) { return; }

		const whitespaceOffset = this.pos;
		let lookahead = this.pos;

		while (lookahead < this.text.length && isWhitespace(this.text[lookahead])) { ++lookahead; }

		const afterWhitespace = lookahead < this.text.length ? this.text[lookahead] : END_OF_TEXT;

		if (afterWhitespace === '!' || afterWhitespace === '?') {
			throw fail(
				NaxpMessage.NAXP1053_DecimalRangeMarkSplit,
				null,
				whitespaceOffset,
				lookahead - whitespaceOffset);
		}
	}

	/**
	 * Rules out whitespace that splits a run of digits, which whitespace between tokens does not.
	 * Called immediately after the run has been read.
	 *
	 * @param {string} message Which fault to give, since the two callers word it differently.
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
	 * The fault for a position at which an element was required and none begins.
	 *
	 * @returns {ParseFailure} The fault.
	 */
	noElementHere() {
		const c = this.peek();

		if (c === END_OF_TEXT) {
			return fail(NaxpMessage.NAXP1033_ElementRequired, null, this.pos, 0);
		}

		if (c === '|' || c === ')') {
			return fail(NaxpMessage.NAXP1034_AlternativeEmpty, null, this.pos, 1);
		}

		if (c === '!') {
			return fail(NaxpMessage.NAXP1035_UnifiedWithoutElement, null, this.pos, 1);
		}

		return this.unexpectedCharacter();
	}

	/**
	 * The fault for a character that cannot appear where it stands.
	 *
	 * @returns {ParseFailure} The fault.
	 */
	unexpectedCharacter() {
		const c = this.peek();

		if (c === END_OF_TEXT) {
			return fail(NaxpMessage.NAXP1036_NaxpIncomplete, null, this.pos, 0);
		}

		if (c === '*' || c === '+') { return fail(NaxpMessage.NAXP1059_RepetitionUnbounded, c, this.pos, 1); }
		if (c === '.') { return fail(NaxpMessage.NAXP1060_AnyCharacter, null, this.pos, 1); }
		if (c === '^' || c === '$') { return fail(NaxpMessage.NAXP1061_Anchor, c, this.pos, 1); }

		return fail(isReservedChar(c) ? NaxpMessage.NAXP1037_ReservedCharacterHere : NaxpMessage.NAXP1038_CharacterHere, isReservedChar(c) ? c : describeChar(c), this.pos, 1);
	}

	// #endregion
}

// #region Free functions

/**
 * Stamps a node with the offset in the pattern at which it starts.
 *
 * @template {import('./ast.js').Ast} T
 * @param {T} node The node.
 * @param {number} offset The offset.
 * @returns {T} The same node.
 */
/**
 * Whether any mark falls on a digit of the value rather than on the padding in front of it.
 *
 * @param {number} value The value of the bound.
 * @param {number} digitCount The digits the bound was written with.
 * @param {string[]} marks One entry per digit, the empty string where that digit carries no mark.
 * @returns {number} The position of the first mark that is out of place, or -1 where none is.
 */
function markedInsideValue(value, digitCount, marks) {
	let significant = 1;

	for (let rest = value; rest >= 10; rest = Math.floor(rest / 10)) { ++significant; }

	for (let position = digitCount - significant; position < digitCount; ++position) {
		if (position >= 0 && marks[position]) { return position; }
	}

	return -1;
}

function at(node, offset) {
	node.patternOffset = offset;

	return node;
}

/**
 * A fault, ready to throw.
 *
 * @param {string} message Which fault this is, a member of {@link NaxpMessage}.
 * @param {string | null} argument What the message interpolates, or null.
 * @param {number} offset The offset in the pattern at which the fault was found.
 * @param {string} message What is wrong.
 * @returns {ParseFailure} The fault.
 */
function fail(message, argument, offset, length) {
	return new ParseFailure(new NaxpError(message, argument, offset, length));
}

/**
 * The fault for a backslash followed by something that is not an escape.
 *
 * The span covers the backslash and what follows it, which is two characters.
 *
 * @param {string} escaped The character after the backslash.
 * @param {number} backslashOffset Where the backslash is.
 * @returns {ParseFailure} The fault.
 */
function undefinedEscape(escaped, backslashOffset) {
	return fail(NaxpMessage.NAXP1031_EscapeUndefined, escaped, backslashOffset, 2);
}

/**
 * Names a character the pattern may not hold, which is by definition one that cannot be shown.
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
 * The quotes belong here rather than in the message, so that 'a space' is not quoted as though it
 * were a character. A message using this must therefore not quote its argument.
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

/**
 * A character as it is written inside a character set, for a message telling somebody what to
 * write.
 *
 * Naming a character and writing one are different jobs, which is why this is not
 * {@link describeChar}: that says 'a space', and a space is the one thing nobody can type.
 *
 * Only three kinds of character reach here. A space arrives as `\s`, since bare whitespace inside
 * a set is skipped and a backslash before whitespace is invalid; a reserved character arrives
 * escaped and has to go back escaped; and everything else is bare and stands for itself.
 *
 * @param {string} c The character.
 * @returns {string} The pattern that denotes it.
 */
function patternForChar(c) {
	if (c === ' ') { return '\\s'; }

	return isReservedChar(c) ? `\\${c}` : c;
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
		|| c === '}' || isRegexMetachar(c);
}

/**
 * The five regex metacharacters naxp reserves without giving them a meaning, so that a regex
 * habit gets a message naming what naxp offers instead rather than a pattern that silently
 * means something else.
 *
 * @param {string} c The character.
 * @returns {boolean} Whether it is one of the five.
 */
function isRegexMetachar(c) {
	return c === '*' || c === '+' || c === '.' || c === '^' || c === '$';
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
 * @param {string} text The pattern of the naxp.
 * @returns {{ast: import('./ast.js').Ast | null, error: NaxpError | null}} The tree, or the
 * fault. Exactly one of the two is null.
 */
export function tryParse(text) {
	try {
		return { ast: new Parser(text).parseNaxp(), error: null };
	} catch (thrown) {
		if (thrown instanceof ParseFailure) { return { ast: null, error: thrown.naxpError }; }

		throw thrown;
	}
}
