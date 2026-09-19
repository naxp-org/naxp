// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LogMu;

/// <summary>
/// A recursive descent parser for naxp version 0.10.
/// </summary>
/// <remarks>
/// <para>
/// The parser reports W4 as well as syntax, because the constraints on interval counts and
/// decimal range bounds are decided at the point the tokens are read and nowhere else. W1 and W2
/// need the finished tree and live in <see cref="WellFormedness"/>. W3 and W5 need the state
/// map and are not implemented yet.
/// </para>
/// <para>
/// It carries error productions for syntax that is plausibly wrong rather than merely invalid,
/// so that the message names the mistake: a comma in an interval, an unbounded interval, a bare
/// <c>x!</c>, the hex escape naxp does not have, and whitespace splitting a token.
/// </para>
/// <para>
/// The input is a span, so the parser is a <see langword="ref"/> <see langword="struct"/> and
/// holds one. Parsing happens once per naxp and nothing here is measurable, but the public
/// surface takes spans throughout and this saves the boundary a conversion.
/// </para>
/// </remarks>
ref struct Parser
{
	#region Private data
	/// <summary>Returned by <see cref="Peek"/> past the end of the pattern.</summary>
	/// <remarks>
	/// Safe as a sentinel because <see cref="TryCheckPatternCharacters"/> has already ruled out
	/// any pattern containing a character outside whitespace and U+0021 to U+007E.
	/// </remarks>
	const char EndOfText = '\0';

	/// <summary>The most digits an interval count may have.</summary>
	const int MaxIntervalCountDigits = 2;

	/// <summary>The most digits a decimal range bound may have.</summary>
	const int MaxBoundDigits = 15;

	readonly ReadOnlySpan<char> text;
	int pos;
	#endregion
	#region Private ctors
	Parser(ReadOnlySpan<char> text)
	{
		this.text = text;
		this.pos = 0;
	}
	#endregion
	#region Public entry point
	/// <summary>
	/// Parses a naxp, checking syntax and W4.
	/// </summary>
	/// <param name="text">The pattern of the naxp.</param>
	/// <param name="ast">The tree, or <see langword="null"/> if the pattern was invalid.</param>
	/// <param name="error">The fault, or <see langword="null"/> if the pattern parsed.</param>
	/// <returns>Whether the pattern parsed.</returns>
	public static bool TryParse(ReadOnlySpan<char> text, out Ast? ast, out NaxpError? error)
		=> new Parser(text).TryParseNaxp(out ast, out error);
	#endregion
	#region Productions
	bool TryParseNaxp(out Ast? ast, out NaxpError? error)
	{
		ast = null;

		if (!this.TryCheckPatternCharacters(out error)) { return false; }

		this.SkipWhitespace();

		if (!this.TryParseExpr(out Ast? expr, out error)) { return false; }

		this.SkipWhitespace();

		if (this.pos != this.text.Length)
		{
			// A ')' that closes a group is consumed by ParseBase, so one still standing here
			// closes nothing. Calling it a reserved character and offering the escape is true
			// and is almost never what was meant.
			error = this.Peek() == ')'
				? new NaxpError(NaxpMessage.NAXP1057_GroupNotOpened, offset: this.pos, length: 1)
				: this.UnexpectedCharacter();

			return false;
		}

		ast = expr;
		error = null;
		return true;
	}

	/// <summary><c>expr ::= seq ( "|" seq )*</c></summary>
	bool TryParseExpr(out Ast? ast, out NaxpError? error)
	{
		ast = null;
		int start = this.pos;

		if (!this.TryParseSeq(out Ast? first, out error)) { return false; }

		List<Ast>? alternatives = null;

		this.SkipWhitespace();
		while (this.Peek() == '|')
		{
			this.Advance();
			this.SkipWhitespace();

			if (!this.TryParseSeq(out Ast? next, out error)) { return false; }

			alternatives ??= new List<Ast> { first! };
			alternatives.Add(next!);

			this.SkipWhitespace();
		}

		ast = alternatives is null
			? first
			: new AstAlternation(alternatives) { PatternOffset = start }
			;
		error = null;
		return true;
	}

	/// <summary><c>seq ::= element+ | element* case_fold expr</c></summary>
	/// <remarks>
	/// A case fold is the loosest operator: it runs from where it is written to the end of the
	/// enclosing group, across any <c>|</c>, the way a regex flag does. So a fold ends the
	/// sequence it is written in, taking the rest of the expression as its operand. Where one
	/// fold is written inside another the outer governs, so a run of folds is the first of them
	/// and the rest are consumed and dropped.
	/// </remarks>
	bool TryParseSeq(out Ast? ast, out NaxpError? error)
	{
		ast = null;
		int start = this.pos;

		Ast? first = null;
		List<Ast>? elements = null;

		while (true)
		{
			this.SkipWhitespace();

			int foldStart = this.pos;

			if (!this.TryParseFold(out bool hasFold, out bool foldToUpper, out error)) { return false; }

			if (hasFold)
			{
				if (!this.TryParseExpr(out Ast? tail, out error)) { return false; }

				Ast rest = Folding.Apply(tail!, foldToUpper);
				rest.PatternOffset = foldStart;

				if (first is null)
				{
					ast = rest;
					error = null;
					return true;
				}

				elements ??= new List<Ast> { first };
				elements.Add(rest);
				break;
			}

			if (!IsStartOfElement(this.Peek())) { break; }

			if (!this.TryParseElement(out Ast? element, out error)) { return false; }

			if (first is null)
			{
				first = element;
			}
			else
			{
				elements ??= new List<Ast> { first };
				elements.Add(element!);
			}
		}

		if (first is null)
		{
			error = this.NoElementHere();
			return false;
		}

		ast = elements is null
			? first
			: new AstSequence(elements) { PatternOffset = start }
			;
		error = null;
		return true;
	}

	/// <summary><c>element ::= operand quantifier? text_unification?</c></summary>
	bool TryParseElement(out Ast? ast, out NaxpError? error)
	{
		ast = null;
		int start = this.pos;

		if (!this.TryParseBase(out Ast? node, out error)) { return false; }

		bool hasQuantifier = false;
		bool hasOptional = false;

		this.SkipWhitespace();

		if (this.Peek() == '?')
		{
			this.Advance();
			node = new AstOptional(node!) { PatternOffset = start };
			hasQuantifier = true;
			hasOptional = true;
		}
		else if (this.Peek() == '{')
		{
			if (!this.TryParseInterval(node!, start, out node, out error)) { return false; }
			hasQuantifier = true;
		}

		this.SkipWhitespace();

		if (hasQuantifier && (this.Peek() == '?' || this.Peek() == '{'))
		{
			error = new NaxpError(NaxpMessage.NAXP1001_QuantifierRepeated, offset: this.pos, length: 1);
			return false;
		}

		if (this.Peek() == '!')
		{
			if (!this.TryParseUnified(node!, start, hasOptional, out node, out error)) { return false; }
		}

		ast = node;
		error = null;
		return true;
	}

	/// <summary><c>case_fold ::= "\C" | "\c"</c>, as many as are written.</summary>
	/// <remarks>
	/// The fold is consumed here and expanded by <see cref="Folding"/> once the expression it
	/// binds to has been parsed, so no later stage sees one. A run of folds is the first of
	/// them; see <see cref="TryParseSeq"/>.
	/// </remarks>
	/// <param name="hasFold">Whether a fold was written.</param>
	/// <param name="toUpper">Whether upper case is canonical, which is <c>\C</c>.</param>
	/// <param name="error">The fault, if any.</param>
	/// <returns>Whether the fold, if there was one, is well placed.</returns>
	bool TryParseFold(out bool hasFold, out bool toUpper, out NaxpError? error)
	{
		hasFold = false;
		toUpper = false;
		error = null;

		// Where one fold is written directly on another the outer governs, as it does over a fold
		// anywhere within its extent, so a run of folds is the first of them and the rest are
		// consumed and dropped.
		while (this.TryPeekFold(out char letter))
		{
			this.Advance();
			this.Advance();

			if (!hasFold)
			{
				hasFold = true;
				toUpper = letter == 'C';
			}

			this.SkipWhitespace();
		}

		return true;
	}

	/// <summary>Whether a fold starts here, without consuming it.</summary>
	/// <param name="letter">The fold letter, <c>C</c> or <c>c</c>.</param>
	/// <returns>Whether one is there.</returns>
	bool TryPeekFold(out char letter)
	{
		letter = EndOfText;

		if (this.Peek() != '\\') { return false; }

		char next = this.pos + 1 < this.text.Length ? this.text[this.pos + 1] : EndOfText;

		if (next != 'C' && next != 'c') { return false; }

		letter = next;
		return true;
	}

	/// <summary><c>base ::= char_set | digits_range | "(" expr? ")"</c></summary>
	bool TryParseBase(out Ast? ast, out NaxpError? error)
	{
		ast = null;
		int start = this.pos;
		char c = this.Peek();

		if (c == '(')
		{
			this.Advance();
			this.SkipWhitespace();

			if (this.Peek() == ')')
			{
				this.Advance();
				ast = new AstEmpty { PatternOffset = start };
				error = null;
				return true;
			}

			if (!this.TryParseExpr(out Ast? inner, out error)) { return false; }

			this.SkipWhitespace();

			if (this.Peek() != ')')
			{
				error = new NaxpError(NaxpMessage.NAXP1009_GroupNotClosed, offset: start, length: 1);
				return false;
			}

			this.Advance();
			inner!.PatternOffset = start;
			ast = inner;
			error = null;
			return true;
		}

		if (c == '#')
		{
			return this.TryParseDecimalRange(out ast, out error);
		}

		if (c == '[')
		{
			if (!this.TryParseBracketSet(out AsciiCharSet bracketSet, out error)) { return false; }

			ast = new AstChars(bracketSet) { PatternOffset = start };
			error = null;
			return true;
		}

		if (!this.TryParseCharAtom(out AsciiCharSet atomSet, out _, out _, out error)) { return false; }

		ast = new AstChars(atomSet) { PatternOffset = start };
		error = null;
		return true;
	}

	/// <summary><c>unified ::= "!" element | "!!" | "!?"</c></summary>
	/// <param name="subject">The element the <c>!</c> binds to.</param>
	/// <param name="start">The offset at which that element starts.</param>
	/// <param name="subjectIsOptional">Whether the subject already carries a <c>?</c>.</param>
	/// <param name="ast">The unified element.</param>
	/// <param name="error">The fault, if any.</param>
	/// <returns>Whether the unified element parsed.</returns>
	bool TryParseUnified(Ast subject, int start, bool subjectIsOptional, out Ast? ast, out NaxpError? error)
	{
		ast = null;
		int bangOffset = this.pos;
		this.Advance();

		// No whitespace is skipped here: '!!' and '!?' are single tokens.
		char next = this.Peek();

		if (next == '!' || next == '?')
		{
			this.Advance();

			if (subjectIsOptional)
			{
				error = new NaxpError(next == '!' ? NaxpMessage.NAXP1010_ReproducedAfterOptional : NaxpMessage.NAXP1011_DroppedAfterOptional, offset: bangOffset, length: 2);
				return false;
			}

			// The expansions are structural: x!! is x?!(x), and x!? is x?!().
			Ast optionalSubject = new AstOptional(subject) { PatternOffset = start };
			Ast rendering = next == '!'
				? subject
				: new AstEmpty { PatternOffset = this.pos }
				;
			UnifiedForm form = next == '!' ? UnifiedForm.Reproduced : UnifiedForm.Dropped;

			ast = new AstUnified(optionalSubject, rendering, form) { PatternOffset = start };
			error = null;
			return true;
		}

		if (IsWhitespace(next))
		{
			int whitespaceOffset = this.pos;
			int lookahead = this.pos;
			while (lookahead < this.text.Length && IsWhitespace(this.text[lookahead])) { ++lookahead; }

			char afterWhitespace = lookahead < this.text.Length ? this.text[lookahead] : EndOfText;

			if (afterWhitespace == '!' || afterWhitespace == '?')
			{
				error = new NaxpError(afterWhitespace == '!' ? NaxpMessage.NAXP1012_ReproducedSplit : NaxpMessage.NAXP1013_DroppedSplit, offset: whitespaceOffset, length: lookahead - whitespaceOffset);
				return false;
			}
		}

		this.SkipWhitespace();

		// A fold would run to the end of the group, and a fold with anything to do expands to a
		// '!', which W2 refuses inside a rendering; so there is nothing a fold could usefully mean
		// here, and it is refused as syntax with a message that says what to write instead.
		if (this.TryPeekFold(out _))
		{
			error = new NaxpError(NaxpMessage.NAXP1058_FoldBeginsRendering, offset: this.pos, length: 2);
			return false;
		}

		if (!IsStartOfElement(this.Peek()))
		{
			error = new NaxpError(NaxpMessage.NAXP1014_RenderingMissing, offset: bangOffset, length: 1);
			return false;
		}

		if (!this.TryParseElement(out Ast? explicitRendering, out error)) { return false; }

		ast = new AstUnified(subject, explicitRendering!, UnifiedForm.Explicit) { PatternOffset = start };
		error = null;
		return true;
	}

	/// <summary><c>interval ::= "{" digits ( "-" digits )? "}"</c></summary>
	bool TryParseInterval(Ast child, int start, out Ast? ast, out NaxpError? error)
	{
		ast = null;
		int braceOffset = this.pos;
		this.Advance();
		this.SkipWhitespace();

		if (!this.TryParseIntervalCount(out int minCount, out error)) { return false; }

		this.SkipWhitespace();

		int maxCount = minCount;

		if (this.Peek() == '-')
		{
			error = new NaxpError(NaxpMessage.NAXP1002_IntervalHyphen, offset: this.pos, length: 1);
			return false;
		}

		if (this.Peek() == ',')
		{
			this.Advance();
			this.SkipWhitespace();

			if (!IsDigit(this.Peek()))
			{
				error = new NaxpError(NaxpMessage.NAXP1003_IntervalUnbounded, offset: this.pos, length: 1);
				return false;
			}

			if (!this.TryParseIntervalCount(out maxCount, out error)) { return false; }

			this.SkipWhitespace();
		}

		if (this.Peek() != '}')
		{
			error = new NaxpError(NaxpMessage.NAXP1004_IntervalNotClosed, offset: braceOffset, length: 1);
			return false;
		}

		this.Advance();

		if (minCount > maxCount)
		{
			error = new NaxpError(NaxpMessage.NAXP1007_IntervalCountsOutOfOrder, offset: braceOffset, length: this.pos - braceOffset);
			return false;
		}

		if (maxCount == 0)
		{
			error = new NaxpError(NaxpMessage.NAXP1062_IntervalCountZero, offset: braceOffset, length: this.pos - braceOffset);
			return false;
		}

		ast = new AstInterval(child, minCount, maxCount) { PatternOffset = start };
		error = null;
		return true;
	}

	bool TryParseIntervalCount(out int count, out NaxpError? error)
	{
		count = 0;
		int start = this.pos;

		if (!IsDigit(this.Peek()))
		{
			error = new NaxpError(NaxpMessage.NAXP1005_IntervalCountNotDigits, offset: this.pos, length: 1);
			return false;
		}

		int digitCount = 0;
		while (IsDigit(this.Peek()))
		{
			if (digitCount < MaxIntervalCountDigits)
			{
				count = (count * 10) + (this.Peek() - '0');
			}

			++digitCount;
			this.Advance();
		}

		if (!this.TryCheckDigitRunNotSplit(NaxpMessage.NAXP1006_IntervalCountSplit, out error)) { return false; }

		if (digitCount > MaxIntervalCountDigits)
		{
			error = new NaxpError(NaxpMessage.NAXP1008_IntervalCountTooLong, offset: start, length: this.pos - start);
			return false;
		}

		error = null;
		return true;
	}

	/// <summary><c>digits_range ::= "#[" low_bound "-" digits "]"</c></summary>
	bool TryParseDecimalRange(out Ast? ast, out NaxpError? error)
	{
		ast = null;
		int start = this.pos;
		this.Advance();

		// '#[' is one token, so no whitespace is skipped between the two characters.
		if (this.Peek() != '[')
		{
			error = IsWhitespace(this.Peek())
				? new NaxpError(NaxpMessage.NAXP1015_HashSplitFromBracket, offset: this.pos, length: 1)
				: new NaxpError(NaxpMessage.NAXP1016_HashWithoutBracket, offset: start, length: 1)
				;
			return false;
		}

		this.Advance();
		this.SkipWhitespace();

		// Leading zeros in the lower bound are the point of it: they set a minimum width, and a
		// mark on one says that the width it sets is optional on input.
		if (!this.TryParseBound(allowMarks: true, out ulong low, out int lowDigitCount, out _, out char[]? lowMarks, out int[] lowDigitOffsets, out error)) { return false; }

		this.SkipWhitespace();

		if (this.Peek() != '-')
		{
			error = new NaxpError(NaxpMessage.NAXP1017_DecimalRangeBoundsSeparator, offset: this.pos, length: 1);
			return false;
		}

		this.Advance();
		this.SkipWhitespace();

		if (!this.TryParseBound(allowMarks: false, out ulong high, out int highDigitCount, out bool highHasLeadingZero, out _, out _, out error)) { return false; }

		this.SkipWhitespace();

		if (this.Peek() != ']')
		{
			error = new NaxpError(NaxpMessage.NAXP1018_DecimalRangeNotClosed, offset: start, length: 1);
			return false;
		}

		this.Advance();

		if (lowDigitCount > highDigitCount)
		{
			error = new NaxpError(NaxpMessage.NAXP1021_LowerBoundWiderThanUpper, offset: start, length: this.pos - start);
			return false;
		}

		if (highDigitCount > lowDigitCount && highHasLeadingZero)
		{
			error = new NaxpError(NaxpMessage.NAXP1022_UpperBoundLeadingZeros, offset: start, length: this.pos - start);
			return false;
		}

		if (low > high)
		{
			error = new NaxpError(NaxpMessage.NAXP1023_LowerBoundExceedsUpper, offset: start, length: this.pos - start);
			return false;
		}

		// A mark stands for a padding zero, so it may sit only in front of the digits of the
		// value. Which positions those are is known once the bound has been read.
		if (lowMarks is not null)
		{
			int at = MarkedInsideValue(low, lowDigitCount, lowMarks);

			// The zero and the mark after it, which is the thing that is wrong. Naming the whole
			// range would leave 'this zero' pointing at nothing.
			if (at >= 0)
			{
				error = new NaxpError(
					NaxpMessage.NAXP1055_DecimalRangeMarkNotPadding,
					offset: lowDigitOffsets[at],
					length: 2);
				return false;
			}
		}

		ast = lowMarks is null
			? new AstDecimalRange(low, lowDigitCount, high, highDigitCount) { PatternOffset = start }
			: Padding.Expand(low, lowDigitCount, high, highDigitCount, lowMarks, start)
			;

		error = null;
		return true;
	}

	/// <summary>
	/// Whether any mark falls on a digit of the value rather than on the padding in front of it.
	/// </summary>
	/// <param name="value">The value of the bound.</param>
	/// <param name="digitCount">The digits the bound was written with.</param>
	/// <param name="marks">One entry per digit, nul where that digit carries no mark.</param>
	/// <returns>The position of the first mark that is out of place, or -1 where none is.</returns>
	static int MarkedInsideValue(ulong value, int digitCount, char[] marks)
	{
		int significant = 1;
		for (ulong rest = value; rest >= 10UL; rest /= 10UL) { ++significant; }

		for (int position = digitCount - significant; position < digitCount; ++position)
		{
			if (position >= 0 && marks[position] != '\0') { return position; }
		}

		return -1;
	}

	/// <param name="allowMarks">
	/// Whether a padding mark may follow a digit, which is so for the lower bound alone.
	/// </param>
	/// <param name="value">The value of the bound.</param>
	/// <param name="digitCount">The digits it was written with.</param>
	/// <param name="hasLeadingZero">Whether it was written with a leading zero.</param>
	/// <param name="marks">
	/// One entry per digit, nul where that digit carries no mark, or <see langword="null"/> where
	/// the bound carries no mark at all.
	/// </param>
	/// <param name="digitOffsets">
	/// Where each digit sits in the pattern, so a fault on one can name it rather than the
	/// whole range. A digit and its mark are one token, so these are not evenly spaced.
	/// </param>
	/// <param name="error">The fault, if any.</param>
	/// <returns>Whether the bound parsed.</returns>
	bool TryParseBound(bool allowMarks, out ulong value, out int digitCount, out bool hasLeadingZero, out char[]? marks, out int[] digitOffsets, out NaxpError? error)
	{
		value = 0UL;
		digitCount = 0;
		hasLeadingZero = false;
		marks = null;
		digitOffsets = new int[MaxBoundDigits];

		int start = this.pos;

		if (!IsDigit(this.Peek()))
		{
			error = new NaxpError(NaxpMessage.NAXP1019_DecimalRangeBoundNotDigits, offset: this.pos, length: 1);
			return false;
		}

		char firstDigit = this.Peek();

		while (IsDigit(this.Peek()))
		{
			char digit = this.Peek();

			// Where the digit sits, so that a fault on one can name it rather than the whole
			// range. A digit and its mark are one token, so these are not evenly spaced.
			if (digitCount < MaxBoundDigits) { digitOffsets[digitCount] = this.pos; }

			if (digitCount < MaxBoundDigits)
			{
				value = (value * 10UL) + (ulong)(digit - '0');
			}

			++digitCount;
			this.Advance();

			// A mark is part of the digit's token, so nothing is skipped between the two.
			if (this.Peek() != '!' && this.Peek() != '?') { continue; }

			if (!allowMarks)
			{
				error = new NaxpError(NaxpMessage.NAXP1056_DecimalRangeMarkOnUpperBound, offset: this.pos, length: 1);
				return false;
			}

			if (digit != '0')
			{
				error = new NaxpError(NaxpMessage.NAXP1054_DecimalRangeMarkOnNonZero, offset: this.pos, length: 1);
				return false;
			}

			if (digitCount <= MaxBoundDigits)
			{
				marks ??= new char[MaxBoundDigits];
				marks[digitCount - 1] = this.Peek();
			}

			this.Advance();
		}

		if (allowMarks && !this.TryCheckMarkNotSplit(out error)) { return false; }

		if (!this.TryCheckDigitRunNotSplit(NaxpMessage.NAXP1020_DecimalRangeBoundSplit, out error)) { return false; }

		if (digitCount > MaxBoundDigits)
		{
			error = new NaxpError(NaxpMessage.NAXP1024_DecimalRangeBoundTooLong, offset: start, length: this.pos - start);
			return false;
		}

		hasLeadingZero = digitCount > 1 && firstDigit == '0';
		error = null;
		return true;
	}

	/// <summary><c>char_set ::= ... | "[" set_item+ "]"</c></summary>
	bool TryParseBracketSet(out AsciiCharSet set, out NaxpError? error)
	{
		set = AsciiCharSet.Empty;

		int start = this.pos;
		this.Advance();

		AsciiCharSet result = AsciiCharSet.Empty;
		int itemCount = 0;

		while (true)
		{
			this.SkipWhitespace();

			if (this.Peek() == ']')
			{
				this.Advance();
				break;
			}

			if (this.Peek() == EndOfText)
			{
				error = new NaxpError(NaxpMessage.NAXP1025_CharacterSetNotClosed, offset: start, length: 1);
				return false;
			}

			if (!this.TryParseCharAtom(out AsciiCharSet itemSet, out char itemChar, out bool itemIsBlockEscape, out error))
			{
				return false;
			}

			++itemCount;

			if (itemIsBlockEscape)
			{
				result |= itemSet;
				continue;
			}

			this.SkipWhitespace();

			if (this.Peek() != '-')
			{
				result |= itemSet;
				continue;
			}

			int hyphenOffset = this.pos;
			this.Advance();
			this.SkipWhitespace();

			if (!this.TryParseCharAtom(out AsciiCharSet upperSet, out char upperChar, out bool upperIsBlockEscape, out error))
			{
				return false;
			}

			if (upperIsBlockEscape)
			{
				error = new NaxpError(NaxpMessage.NAXP1026_RangeUpperBoundIsBlockEscape, offset: hyphenOffset, length: 1);
				return false;
			}

			if (upperChar < itemChar)
			{
				error = new NaxpError(NaxpMessage.NAXP1027_RangeReversed, PatternForChar(upperChar) + "-" + PatternForChar(itemChar), hyphenOffset, 1);
				return false;
			}

			result |= AsciiCharSet.FromCharRange(itemChar, upperChar);
		}

		if (itemCount == 0)
		{
			error = new NaxpError(NaxpMessage.NAXP1028_CharacterSetEmpty, offset: start, length: this.pos - start);
			return false;
		}

		set = result;
		error = null;
		return true;
	}

	/// <summary>
	/// Reads one bare character, escape or block escape.
	/// </summary>
	/// <param name="set">The characters it denotes.</param>
	/// <param name="literalChar">
	/// The single character it denotes, meaningful only when <paramref name="isBlockEscape"/>
	/// is <see langword="false"/>. Only a literal character may bound a range.
	/// </param>
	/// <param name="isBlockEscape">Whether it was one of <c>\9</c>, <c>\A</c>, <c>\a</c> or <c>\X</c>.</param>
	/// <param name="error">The fault, if any.</param>
	/// <returns>Whether an atom was read.</returns>
	bool TryParseCharAtom(out AsciiCharSet set, out char literalChar, out bool isBlockEscape, out NaxpError? error)
	{
		set = AsciiCharSet.Empty;
		literalChar = EndOfText;
		isBlockEscape = false;

		char c = this.Peek();

		if (c == '\\')
		{
			int backslashOffset = this.pos;
			this.Advance();

			char escaped = this.Peek();

			if (IsWhitespace(escaped))
			{
				error = new NaxpError(NaxpMessage.NAXP1029_BackslashBeforeWhitespace, offset: this.pos, length: 1);
				return false;
			}

			if (escaped == EndOfText)
			{
				error = new NaxpError(NaxpMessage.NAXP1030_BackslashWithoutEscape, offset: backslashOffset, length: 1);
				return false;
			}

			this.Advance();

			switch (escaped)
			{
				case 's':
					literalChar = ' ';
					set = AsciiCharSet.FromSingleChar(' ');
					error = null;
					return true;

				case '9':
					set = AsciiCharSet.AllDigits;
					isBlockEscape = true;
					error = null;
					return true;

				case 'A':
					set = AsciiCharSet.AllUpperCaseLetters;
					isBlockEscape = true;
					error = null;
					return true;

				case 'a':
					set = AsciiCharSet.AllLowerCaseLetters;
					isBlockEscape = true;
					error = null;
					return true;

				case 'X':
					set = AsciiCharSet.AllDigitsAndUpperCaseLetters;
					isBlockEscape = true;
					error = null;
					return true;

				case 'x':
					set = AsciiCharSet.AllDigitsAndLowerCaseLetters;
					isBlockEscape = true;
					error = null;
					return true;

				case 'C':
				case 'c':
					// A fold before an element is taken in TryParseElement, so one reaching here
					// is inside a character set, where it means nothing.
					error = new NaxpError(NaxpMessage.NAXP1052_FoldInCharacterSet, escaped.ToString(), backslashOffset, 2);
					return false;
			}

			if (IsReservedChar(escaped))
			{
				literalChar = escaped;
				set = AsciiCharSet.FromSingleChar(escaped);
				error = null;
				return true;
			}

			error = UndefinedEscapeError(escaped, backslashOffset);
			return false;
		}

		if (IsBareChar(c))
		{
			this.Advance();
			literalChar = c;
			set = AsciiCharSet.FromSingleChar(c);
			error = null;
			return true;
		}

		error = this.UnexpectedCharacter();
		return false;
	}
	#endregion
	#region Pattern scanning
	bool TryCheckPatternCharacters(out NaxpError? error)
	{
		for (int i = 0; i < this.text.Length; ++i)
		{
			char c = this.text[i];

			if (IsWhitespace(c) || (c >= '\x21' && c <= '\x7E')) { continue; }

			error = new NaxpError(NaxpMessage.NAXP1032_CharacterNotAllowed, CodePointAsText(c), i, 1);
			return false;
		}

		error = null;
		return true;
	}

	char Peek() => this.pos < this.text.Length ? this.text[this.pos] : EndOfText;

	void Advance() => ++this.pos;

	void SkipWhitespace()
	{
		while (this.pos < this.text.Length && IsWhitespace(this.text[this.pos])) { ++this.pos; }
	}

	/// <summary>
	/// Refuses whitespace between a decimal range bound's digit and a padding mark on it.
	/// </summary>
	/// <remarks>
	/// Called where the digit run has ended, which is where a mark separated from its digit
	/// leaves the parser: whitespace is not a digit, so the run stops in front of it.
	/// </remarks>
	/// <param name="error">The fault, if any.</param>
	/// <returns>Whether the mark, if there is one, stands against its digit.</returns>
	bool TryCheckMarkNotSplit(out NaxpError? error)
	{
		if (IsWhitespace(this.Peek()))
		{
			int whitespaceOffset = this.pos;
			int lookahead = this.pos;
			while (lookahead < this.text.Length && IsWhitespace(this.text[lookahead])) { ++lookahead; }

			char afterWhitespace = lookahead < this.text.Length ? this.text[lookahead] : EndOfText;

			if (afterWhitespace == '!' || afterWhitespace == '?')
			{
				error = new NaxpError(NaxpMessage.NAXP1053_DecimalRangeMarkSplit, offset: whitespaceOffset, length: lookahead - whitespaceOffset);
				return false;
			}
		}

		error = null;
		return true;
	}

	/// <summary>
	/// Rules out whitespace that splits a run of digits, which whitespace between tokens does not.
	/// Called immediately after the run has been read.
	/// </summary>
	/// <param name="message">Which fault to give, since the two callers word it differently.</param>
	/// <param name="error">The fault, if any.</param>
	/// <returns>Whether the run stands whole.</returns>
	bool TryCheckDigitRunNotSplit(NaxpMessage message, out NaxpError? error)
	{
		if (IsWhitespace(this.Peek()))
		{
			int whitespaceOffset = this.pos;
			int lookahead = this.pos;
			while (lookahead < this.text.Length && IsWhitespace(this.text[lookahead])) { ++lookahead; }

			if (lookahead < this.text.Length && IsDigit(this.text[lookahead]))
			{
				error = new NaxpError(message, offset: whitespaceOffset, length: lookahead - whitespaceOffset);
				return false;
			}
		}

		error = null;
		return true;
	}
	#endregion
	#region Diagnostics
	/// <summary>
	/// The fault for a position at which an element was required and none begins.
	/// </summary>
	NaxpError NoElementHere()
	{
		char c = this.Peek();

		if (c == EndOfText)
		{
			return new NaxpError(NaxpMessage.NAXP1033_ElementRequired, offset: this.pos, length: 0);
		}

		if (c == '|' || c == ')')
		{
			return new NaxpError(NaxpMessage.NAXP1034_AlternativeEmpty, offset: this.pos, length: 1);
		}

		if (c == '!')
		{
			return new NaxpError(NaxpMessage.NAXP1035_UnifiedWithoutElement, offset: this.pos, length: 1);
		}

		return this.UnexpectedCharacter();
	}

	/// <summary>
	/// The fault for a character that cannot appear where it stands.
	/// </summary>
	NaxpError UnexpectedCharacter()
	{
		char c = this.Peek();

		if (c == EndOfText)
		{
			return new NaxpError(NaxpMessage.NAXP1036_NaxpIncomplete, offset: this.pos, length: 0);
		}

		if (c is '*' or '+') { return new NaxpError(NaxpMessage.NAXP1059_RepetitionUnbounded, c.ToString(), this.pos, 1); }
		if (c == '.') { return new NaxpError(NaxpMessage.NAXP1060_AnyCharacter, offset: this.pos, length: 1); }
		if (c is '^' or '$') { return new NaxpError(NaxpMessage.NAXP1061_Anchor, c.ToString(), this.pos, 1); }

		return new NaxpError(IsReservedChar(c) ? NaxpMessage.NAXP1037_ReservedCharacterHere : NaxpMessage.NAXP1038_CharacterHere, IsReservedChar(c) ? c.ToString() : DescribeChar(c), this.pos, 1);
	}

	/// <summary>
	/// The fault for a backslash followed by something that is not an escape.
	/// </summary>
	/// <remarks>
	/// The span covers the backslash and what follows it, which is two characters.
	/// </remarks>
	static NaxpError UndefinedEscapeError(char escaped, int backslashOffset)
		=> new(NaxpMessage.NAXP1031_EscapeUndefined, escaped.ToString(), backslashOffset, 2);

	/// <summary>
	/// Names a character the pattern may not hold, which is by definition one that cannot be shown.
	/// </summary>
	/// <remarks>
	/// A surrogate is called out because the offset alone misleads there: the user typed one
	/// character above the basic plane and this names half of it. Only the first half is ever
	/// reported, since well-formed UTF-16 puts it before the second and the scan stops there.
	/// </remarks>
	static string CodePointAsText(char c)
	{
		string hex = string.Format(CultureInfo.InvariantCulture, "U+{0:X4}", (int)c);

		if (char.IsSurrogate(c)) { hex += " (part of a UTF-16 surrogate pair)"; }

		return hex;
	}

	/// <summary>
	/// A character as a message names it.
	/// </summary>
	/// <remarks>
	/// The quotes belong here rather than in the message, so that 'a space' is not quoted as
	/// though it were a character. A message using this must therefore not quote its argument.
	/// </remarks>
	static string DescribeChar(char c)
		=> c switch
		{
			' ' => "a space",
			'\t' => "a tab",
			'\r' => "a carriage return",
			'\n' => "a line feed",
			_ => string.Format(CultureInfo.InvariantCulture, "'{0}'", c),
		};

	/// <summary>
	/// A character as it is written inside a character set, for a message telling somebody what
	/// to write.
	/// </summary>
	/// <remarks>
	/// Naming a character and writing one are different jobs, which is why this is not
	/// <see cref="DescribeChar"/>: that says 'a space', and a space is the one thing nobody can
	/// type.
	/// <para>
	/// Only three kinds of character reach here. A space arrives as <c>\s</c>, since bare
	/// whitespace inside a set is skipped and a backslash before whitespace is invalid; a
	/// reserved character arrives escaped and has to go back escaped; and everything else is
	/// bare and stands for itself.
	/// </para>
	/// </remarks>
	static string PatternForChar(char c)
		=> c switch
		{
			' ' => "\\s",
			_ when IsReservedChar(c) => string.Format(CultureInfo.InvariantCulture, "\\{0}", c),
			_ => c.ToString(CultureInfo.InvariantCulture),
		};
	#endregion
	#region Character classes
	static bool IsWhitespace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

	static bool IsDigit(char c) => c >= '0' && c <= '9';

	static bool IsReservedChar(char c)
		=> c is '!' or '#' or '(' or ')' or ',' or '-' or '?' or '[' or '\\' or ']' or '{' or '|' or '}' || IsRegexMetachar(c);

	/// <summary>
	/// The five regex metacharacters naxp reserves without giving them a meaning, so that a regex
	/// habit gets a message naming what naxp offers instead rather than a pattern that silently
	/// means something else.
	/// </summary>
	static bool IsRegexMetachar(char c) => c is '*' or '+' or '.' or '^' or '$';

	static bool IsBareChar(char c) => c >= '\x21' && c <= '\x7E' && !IsReservedChar(c);

	static bool IsStartOfElement(char c)
		=> c == '\\' || c == '[' || c == '#' || c == '(' || IsBareChar(c);
	#endregion
}
