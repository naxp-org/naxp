// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LogMu;

/// <summary>
/// A recursive descent parser for naxp version 0.4.
/// </summary>
/// <remarks>
/// <para>
/// The parser reports W4 as well as syntax, because the constraints on interval counts and
/// digits range bounds are decided at the point the tokens are read and nowhere else. W1 and W2
/// need the finished tree and live in <see cref="WellFormedness"/>. W3 and W5 need the state
/// map and are not implemented yet.
/// </para>
/// <para>
/// It carries error productions for syntax that is plausibly wrong rather than merely invalid,
/// so that the message names the mistake: a comma in an interval, an unbounded interval, a bare
/// <c>x!</c>, the hex escape that version 0.3 removed, and whitespace splitting a token.
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
	/// <summary>Returned by <see cref="Peek"/> past the end of the source.</summary>
	/// <remarks>
	/// Safe as a sentinel because <see cref="TryCheckSourceCharacters"/> has already refused
	/// any source containing a character outside whitespace and U+0021 to U+007E.
	/// </remarks>
	const char EndOfText = '\0';

	/// <summary>The most digits an interval count may have.</summary>
	const int MaxIntervalCountDigits = 2;

	/// <summary>The most digits a digits range bound may have.</summary>
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
	/// <param name="text">The source of the naxp.</param>
	/// <param name="ast">The tree, or <see langword="null"/> if the source was refused.</param>
	/// <param name="error">The refusal, or <see langword="null"/> if the source parsed.</param>
	/// <returns>Whether the source parsed.</returns>
	public static bool TryParse(ReadOnlySpan<char> text, out Ast? ast, out NaxpError? error)
		=> new Parser(text).TryParseNaxp(out ast, out error);
	#endregion
	#region Productions
	bool TryParseNaxp(out Ast? ast, out NaxpError? error)
	{
		ast = null;

		if (!this.TryCheckSourceCharacters(out error)) { return false; }

		this.SkipWhitespace();

		if (!this.TryParseExpr(out Ast? expr, out error)) { return false; }

		this.SkipWhitespace();

		if (this.pos != this.text.Length)
		{
			error = this.UnexpectedCharacter();
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
			: new AstAlternation(alternatives) { SourceOffset = start }
			;
		error = null;
		return true;
	}

	/// <summary><c>seq ::= element+</c></summary>
	bool TryParseSeq(out Ast? ast, out NaxpError? error)
	{
		ast = null;
		int start = this.pos;

		Ast? first = null;
		List<Ast>? elements = null;

		while (true)
		{
			this.SkipWhitespace();
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
			: new AstSequence(elements) { SourceOffset = start }
			;
		error = null;
		return true;
	}

	/// <summary><c>element ::= base quantifier? replaceable?</c></summary>
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
			node = new AstOptional(node!) { SourceOffset = start };
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
			if (!this.TryParseReplaceable(node!, start, hasOptional, out node, out error)) { return false; }
		}

		ast = node;
		error = null;
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
				ast = new AstEmpty { SourceOffset = start };
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
			inner!.SourceOffset = start;
			ast = inner;
			error = null;
			return true;
		}

		if (c == '#')
		{
			return this.TryParseDigitsRange(out ast, out error);
		}

		if (c == '[')
		{
			if (!this.TryParseBracketSet(out AsciiCharSet bracketSet, out error)) { return false; }

			ast = new AstChars(bracketSet) { SourceOffset = start };
			error = null;
			return true;
		}

		if (!this.TryParseCharAtom(out AsciiCharSet atomSet, out _, out _, out error)) { return false; }

		ast = new AstChars(atomSet) { SourceOffset = start };
		error = null;
		return true;
	}

	/// <summary><c>replaceable ::= "!" element | "!!" | "!?"</c></summary>
	/// <param name="subject">The element the <c>!</c> binds to.</param>
	/// <param name="start">The offset at which that element starts.</param>
	/// <param name="subjectIsOptional">Whether the subject already carries a <c>?</c>.</param>
	/// <param name="ast">The replaceable element.</param>
	/// <param name="error">The refusal, if any.</param>
	/// <returns>Whether the replacement parsed.</returns>
	bool TryParseReplaceable(Ast subject, int start, bool subjectIsOptional, out Ast? ast, out NaxpError? error)
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
			Ast optionalSubject = new AstOptional(subject) { SourceOffset = start };
			Ast rendering = next == '!'
				? subject
				: new AstEmpty { SourceOffset = this.pos }
				;
			ReplaceableForm form = next == '!' ? ReplaceableForm.Reproduced : ReplaceableForm.Dropped;

			ast = new AstReplaceable(optionalSubject, rendering, form) { SourceOffset = start };
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

		if (!IsStartOfElement(this.Peek()))
		{
			error = new NaxpError(NaxpMessage.NAXP1014_ReplacementMissing, offset: bangOffset, length: 1);
			return false;
		}

		if (!this.TryParseElement(out Ast? explicitRendering, out error)) { return false; }

		ast = new AstReplaceable(subject, explicitRendering!, ReplaceableForm.Explicit) { SourceOffset = start };
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

		ast = new AstInterval(child, minCount, maxCount) { SourceOffset = start };
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

	/// <summary><c>digits_range ::= "#[" digits "-" digits "]"</c></summary>
	bool TryParseDigitsRange(out Ast? ast, out NaxpError? error)
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

		// Leading zeros in the lower bound are the point of it: they set a minimum width.
		if (!this.TryParseBound(out ulong low, out int lowDigitCount, out _, out error)) { return false; }

		this.SkipWhitespace();

		if (this.Peek() != '-')
		{
			error = new NaxpError(NaxpMessage.NAXP1017_DigitsRangeBoundsSeparator, offset: this.pos, length: 1);
			return false;
		}

		this.Advance();
		this.SkipWhitespace();

		if (!this.TryParseBound(out ulong high, out int highDigitCount, out bool highHasLeadingZero, out error)) { return false; }

		this.SkipWhitespace();

		if (this.Peek() != ']')
		{
			error = new NaxpError(NaxpMessage.NAXP1018_DigitsRangeNotClosed, offset: start, length: 1);
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

		ast = new AstDigitsRange(low, lowDigitCount, high, highDigitCount) { SourceOffset = start };
		error = null;
		return true;
	}

	bool TryParseBound(out ulong value, out int digitCount, out bool hasLeadingZero, out NaxpError? error)
	{
		value = 0UL;
		digitCount = 0;
		hasLeadingZero = false;

		int start = this.pos;

		if (!IsDigit(this.Peek()))
		{
			error = new NaxpError(NaxpMessage.NAXP1019_DigitsRangeBoundNotDigits, offset: this.pos, length: 1);
			return false;
		}

		char firstDigit = this.Peek();

		while (IsDigit(this.Peek()))
		{
			if (digitCount < MaxBoundDigits)
			{
				value = (value * 10UL) + (ulong)(this.Peek() - '0');
			}

			++digitCount;
			this.Advance();
		}

		if (!this.TryCheckDigitRunNotSplit(NaxpMessage.NAXP1020_DigitsRangeBoundSplit, out error)) { return false; }

		if (digitCount > MaxBoundDigits)
		{
			error = new NaxpError(NaxpMessage.NAXP1024_DigitsRangeBoundTooLong, offset: start, length: this.pos - start);
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
				error = new NaxpError(NaxpMessage.NAXP1027_RangeReversed, DescribeChar(upperChar) + "-" + DescribeChar(itemChar), hyphenOffset, 1);
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
	/// <param name="error">The refusal, if any.</param>
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
	#region Source scanning
	bool TryCheckSourceCharacters(out NaxpError? error)
	{
		for (int i = 0; i < this.text.Length; ++i)
		{
			char c = this.text[i];

			if (IsWhitespace(c) || (c >= '\x21' && c <= '\x7E')) { continue; }

			error = new NaxpError(NaxpMessage.NAXP1033_CharacterNotAllowed, CodePointAsText(c), i, 1);
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
	/// Refuses whitespace that splits a run of digits, which whitespace between tokens does not.
	/// Called immediately after the run has been read.
	/// </summary>
	/// <param name="message">Which refusal to give, since the two callers word it differently.</param>
	/// <param name="error">The refusal, if any.</param>
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
	/// The refusal for a position at which an element was required and none begins.
	/// </summary>
	NaxpError NoElementHere()
	{
		char c = this.Peek();

		if (c == EndOfText)
		{
			return new NaxpError(NaxpMessage.NAXP1034_ElementRequired, offset: this.pos, length: 0);
		}

		if (c == '|' || c == ')')
		{
			return new NaxpError(NaxpMessage.NAXP1035_AlternativeEmpty, offset: this.pos, length: 1);
		}

		if (c == '!')
		{
			return new NaxpError(NaxpMessage.NAXP1036_ReplaceableWithoutElement, offset: this.pos, length: 1);
		}

		return this.UnexpectedCharacter();
	}

	/// <summary>
	/// The refusal for a character that cannot appear where it stands.
	/// </summary>
	NaxpError UnexpectedCharacter()
	{
		char c = this.Peek();

		if (c == EndOfText)
		{
			return new NaxpError(NaxpMessage.NAXP1037_NaxpIncomplete, offset: this.pos, length: 0);
		}

		return new NaxpError(IsReservedChar(c) ? NaxpMessage.NAXP1038_ReservedCharacterHere : NaxpMessage.NAXP1039_CharacterHere, IsReservedChar(c) ? c.ToString() : DescribeChar(c), this.pos, 1);
	}

	/// <summary>
	/// The refusal for a backslash followed by something that is not an escape.
	/// </summary>
	/// <remarks>
	/// The span covers the backslash and what follows it, which is two characters whichever of
	/// the two messages this gives.
	/// </remarks>
	static NaxpError UndefinedEscapeError(char escaped, int backslashOffset)
		=> escaped == 'x'
			? new NaxpError(NaxpMessage.NAXP1031_HexEscapeRemoved, offset: backslashOffset, length: 2)
			: new NaxpError(NaxpMessage.NAXP1032_EscapeUndefined, escaped.ToString(), backslashOffset, 2)
			;

	/// <summary>
	/// Names a character the source may not hold, which is by definition one that cannot be shown.
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

	static string DescribeChar(char c)
		=> c switch
		{
			' ' => "a space",
			'\t' => "a tab",
			'\r' => "a carriage return",
			'\n' => "a line feed",
			_ => string.Format(CultureInfo.InvariantCulture, "'{0}'", c),
		};
	#endregion
	#region Character classes
	static bool IsWhitespace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

	static bool IsDigit(char c) => c >= '0' && c <= '9';

	static bool IsReservedChar(char c)
		=> c is '!' or '#' or '(' or ')' or ',' or '-' or '?' or '[' or '\\' or ']' or '{' or '|' or '}';

	static bool IsBareChar(char c) => c >= '\x21' && c <= '\x7E' && !IsReservedChar(c);

	static bool IsStartOfElement(char c)
		=> c == '\\' || c == '[' || c == '#' || c == '(' || IsBareChar(c);
	#endregion
}
