// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace NXOld.NXComponents;

static class Parser
{
	/// <summary>
	/// Tries parsing the <paramref name="text"/>.
	/// If <see langword="true"/> is returned then the parse succeeded and the result is <paramref name="ast"/>.
	/// Otherwise, the parse failed and an error message is provided in <paramref name="errorMessage"/> 
	/// and the char offset at which the error occurred is given in <paramref name="errorOffset"/>.
	/// <para>Note that <paramref name="ast"/> matches the text parsed -- it is not simplified.</para>
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <param name="ast">The <see cref="Ast"/> (if the methods returns <see langword="true"/>). Note that this matches the text parsed -- it is not simplified.</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <param name="errorOffset">
	/// The (zero-based) offset to the position of the error in <paramref name="text"/>
	/// (if the method returns <see langword="false"/>).
	/// </param>
	/// <returns>Whether the parse succeeeded.</returns>
	public static bool TryParse(ReadOnlySpan<char> text
		, [NotNullWhen(true)] out Ast? ast
		, [NotNullWhen(false)] out string? errorMessage
		, out int errorOffset
		)
	{
		// Internally this is the position of the next non-WS char.
		errorOffset = -1;

		// Ensure we're on a non-space.
		AdvanceSkippingWS(text, ref errorOffset);

		if ((uint)errorOffset >= (uint)text.Length)
		{
			ast = Empty.Instance;
			errorMessage = null;
			return true;
		}

		if (!EXPECT_expr(text, ref errorOffset, out ast, out errorMessage))
		{
			return false;
		}

		if (errorOffset == text.Length) { return true; }

		// We get here if there are unread chars.
		ast = default;
		char c = PeekChar(text, errorOffset);
		errorMessage = $"Unexpected {SafeFormatChar(c)}.";
		return false;
	}

	/// <summary>
	/// Parses the <paramref name="text"/> and returns an <see cref="Ast"/>.
	/// <para>
	/// Notes
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// The AST matches the text parsed -- it is not simplified.
	/// </item>
	/// <item>
	/// If the text is invalid, then an exception is thrown.
	/// </item>
	/// </list>
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <returns>The <see cref="Ast"/> derived from the text.</returns>
	public static Ast Parse(ReadOnlySpan<char> text)
	=> TryParse(text, out var ast, out string? errorMessage, out int errorOffset)
		? ast
		: throw new ArgumentOutOfRangeException(nameof(text), $"Error at offset {errorOffset}: {errorMessage}")
		;

	/// <summary>
	/// Parses the text as an ASCII char set, e.g. <c>A</c>,  <c>[AB]</c>,  <c>\X</c> etc.
	/// <para>This is included in <see cref="Parser"/> to enable code sharing. It should always be called from <see cref="AsciiCharSet"/>.</para>
	/// </summary>
	/// <param name="text">The text to parse..</param>
	/// <param name="charSet">The <see cref="AsciiCharSet"/> (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <param name="errorOffset">
	/// The (zero-based) offset to the position of the error in <paramref name="text"/>
	/// (if the methods returns <see langword="false"/>).
	/// </param>
	/// <returns>Whether the parse succeeeded.</returns>
	public static bool TryParseChars(ReadOnlySpan<char> text
		, out AsciiCharSet charSet
		, [NotNullWhen(false)] out string? errorMessage
		, out int errorOffset
		)
	{
		// Internally this is the position of the next non-WS char.
		errorOffset = -1;

		// Ensure we're on a non-space.
		AdvanceSkippingWS(text, ref errorOffset);

		if ((uint)errorOffset >= (uint)text.Length)
		{
			charSet = default;
			errorMessage = null;
			return true;
		}

		if (!EXPECT_charSet(text, ref errorOffset, out charSet, out errorMessage))
		{
			return false;
		}

		if (errorOffset == text.Length) { return true; }

		// We get here if there are unread chars.
		charSet = default;
		char c = PeekChar(text, errorOffset);
		errorMessage = $"Unexpected {SafeFormatChar(c)}.";
		return false;
	}
	/// <summary>
	/// 15, which is the maximum number of digits that can be used in a digits literal, e.g. <c>[001-314]</c>.
	/// <para>
	/// This number of digits can be stored without loss in a 64 bit integer or a 
	/// 64 bit floating point (which is the default in MS Excel), 
	/// but <i>not</i> in a 32 bit integer.
	/// </para>
	/// </summary>
	public const int MaxLiteralDigitCount = 15;
	#region Parsing
	/// <summary>
	/// <i>encoding</i> ::= <i>seq</i> ( "|" <i>seq</i>)*
	/// </summary>
	/// <param name="text">The text to parse (from position <paramref name="pos"/>).</param>
	/// <param name="pos">The position of the next character to be read in <paramref name="text"/>. (Guaranteed not to be a space.)</param>
	/// <param name="ast">The <see cref="Ast"/> (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <returns>Whether the parse succeeeded.</returns>
	static bool EXPECT_expr(ReadOnlySpan<char> text, ref int pos, [NotNullWhen(true)] out Ast? ast, [NotNullWhen(false)] out string? errorMessage)
	{
		if (!EXPECT_seq(text, ref pos, out ast, out errorMessage))
		{
			return false;
		}

		if (PeekChar(text, pos) == '|')
		{
			List<Ast> childList;
			if (ast is Or or)
			{
				var existingChildren = or.Children;
				childList = new List<Ast>(existingChildren.Length + 20);
				childList.AddRange(existingChildren);
			}
			else
			{
				childList = new List<Ast>(20) { ast };
			}

			do
			{
				AdvanceSkippingWS(text, ref pos);
				if (!EXPECT_seq(text, ref pos, out Ast? child, out errorMessage))
				{
					return false;
				}

				if (child is Or childOr)
				{
					childList.AddRange(childOr.Children);
				}
				else
				{
					childList.Add(child);
				}
			} while (PeekChar(text, pos) == '|');

			var children = childList.ToArray();
			ast = new Or(children);
		}

		return true;
	}
	/// <summary>
	/// <i>seq</i> ::= <i>element</i>+
	/// </summary>
	/// <param name="text">The text to parse (from position <paramref name="pos"/>).</param>
	/// <param name="pos">The position of the next character to be read in <paramref name="text"/>. (Guaranteed not to be a space.)</param>
	/// <param name="ast">The <see cref="Ast"/> (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <returns>Whether the parse succeeeded.</returns>
	static bool EXPECT_seq(ReadOnlySpan<char> text, ref int pos, [NotNullWhen(true)] out Ast? ast, [NotNullWhen(false)] out string? errorMessage)
	{
		if (!EXPECT_element(text, ref pos, out ast, out errorMessage))
		{
			return false;
		}

		if (IsStartOfElement(PeekChar(text, pos)))
		{
			List<Ast> childList;
			if (ast is Seq seq)
			{
				var existingChildren = seq.Children;
				childList = new List<Ast>(existingChildren.Length + 20);
				childList.AddRange(existingChildren);
			}
			else
			{
				childList = new List<Ast>(20) { ast };
			}

			do
			{
				if (!EXPECT_element(text, ref pos, out Ast? child, out errorMessage))
				{
					return false;
				}

				if (child is Seq childSeq)
				{
					childList.AddRange(childSeq.Children);
				}
				else
				{
					childList.Add(child);
				}
			} while (IsStartOfElement(PeekChar(text, pos)));

			var children = childList.ToArray();
			ast = new Seq(children);
		}

		return true;
	}
	/// <summary>
	/// </summary>
	/// <param name="text">The text to parse (from position <paramref name="pos"/>).</param>
	/// <param name="pos">The position of the next character to be read in <paramref name="text"/>. (Guaranteed not to be a space.)</param>
	/// <param name="ast">The <see cref="Ast"/> (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <returns>Whether the parse succeeeded.</returns>
	static bool EXPECT_element(ReadOnlySpan<char> text, ref int pos, [NotNullWhen(true)] out Ast? ast, [NotNullWhen(false)] out string? errorMessage)
	{
		var c = PeekChar(text, pos);

		if (IsNonEscapedLiteralChar(c) || c == '\\' || c == '[')
		{
			if (!EXPECT_charSet(text, ref pos, out var charSet, out errorMessage))
			{
				ast = null;
				return false;
			}
			else
			{
				Debug.Assert(!charSet.IsEmpty);
				ast = new Chars(charSet);
			}
		}
		else if (c == '(')
		{
			AdvanceSkippingWS(text, ref pos);
			if (!EXPECT_expr(text, ref pos, out ast, out errorMessage)) { return false; }
			c = PeekChar(text, pos);
			if (c != ')')
			{
				ast = null;
				errorMessage = "Expected ')'.";
				return false;
			}
			AdvanceSkippingWS(text, ref pos);
		}
		else if (c == '#')
		{
			if (!EXPECT_digits_range_literal(text, ref pos, out ast, out errorMessage))
			{
				ast = null;
				return false;
			}
		}
		else
		{
			ast = null;
			errorMessage = $"Unexpected {SafeFormatChar(c)}.";
			return false;
		}

		c = PeekChar(text, pos);

		if (c == '?')
		{
			ast = new Opt(ast);
			AdvanceSkippingWS(text, ref pos);

			// Consecutive '?' are not legal according to the grammar.
			c = PeekChar(text, pos);
			if (c == '?')
			{
				ast = null;
				errorMessage = "Consecutive \'?\' characters are not legal.";
				return false;
			}
		}
		else if (c == '!')
		{
			ast = null;
			errorMessage = "Optional format characters have not been implemented";
			return false;
		}

		errorMessage = null;
		return true;
	}
	/// <summary>
	/// <i>char_set</i> ::= <i>literal_char</i> | "[" (<i>literal_char</i> | (<i>literal_char</i> "-" <i>literal_char</i>)+ "]" 
	/// </summary>
	/// <param name="text">The text to parse (from position <paramref name="pos"/>).</param>
	/// <param name="pos">The position of the next character to be read in <paramref name="text"/>. (Guaranteed not to be a space.)</param>
	/// <param name="charSet">The char set read (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <returns>Whether the parse succeeeded.</returns>
	static bool EXPECT_charSet(ReadOnlySpan<char> text, ref int pos, out AsciiCharSet charSet, [NotNullWhen(false)] out string? errorMessage)
	{
		var c = PeekChar(text, pos);

		if (IsNonEscapedLiteralChar(c))
		{
			charSet = AsciiCharSet.FromSingleChar(c);
			errorMessage = null;
			AdvanceSkippingWS(text, ref pos);
			return true;
		}
		else if (c == '\\')
		{
			if (!TryAdvanceAfterEscape(text, ref pos, out errorMessage))
			{
				charSet = default;
				return false;
			}
			c = PeekChar(text, pos);
			if (IsBlockEscapeChar(c, out charSet))
			{
				errorMessage = null;
				AdvanceSkippingWS(text, ref pos);
				return true;
			}
			else if (c == 'x')
			{
				AdvanceSkippingWS(text, ref pos);
				if (EXPECT_hex_literal_char(text, ref pos, out c, out errorMessage))
				{
					charSet = AsciiCharSet.FromSingleChar(c);
					return true;
				}
				else
				{
					charSet = default;
					return false;
				}
			}
			else if (IsEscapedLiteralChar(c))
			{
				if (c == 's') { c = ' '; }
				charSet = AsciiCharSet.FromSingleChar(c);
				errorMessage = null;
				AdvanceSkippingWS(text, ref pos);
				return true;
			}
			else
			{
				charSet = default;
				errorMessage = $"Unexpected escaped character {SafeFormatChar(c)}.";
				return false;
			}
		}
		else if (c == '[')
		{
			// '[' (C | C '-' C)+ ']'
			// C ::= is allowed literal char | '\' escaped literal char (with '\s' meaning space)

			AdvanceSkippingWS(text, ref pos);

			c = PeekChar(text, pos);

			if (c == ']')
			{
				charSet = default;
				errorMessage = "Range ('[ ... ]') must contain at least one character.";
				return false;
			}

			charSet = default;
			do
			{
				// We do allow blocks escapes inside ranges,
				// e.g. ""[\9A-F]" is legal
				bool isBlockEscape = false;
				if (c == '\\')
				{
					var pos2 = pos;
					if (!TryAdvanceAfterEscape(text, ref pos2, out errorMessage))
					{
						charSet = default;
						return false;
					}
					if (IsBlockEscapeChar(PeekChar(text, pos2), out var charSet2))
					{
						pos = pos2;
						charSet |= charSet2;
						// Read the e.g. 'X' in "\X"
						AdvanceSkippingWS(text, ref pos);
						c = PeekChar(text, pos);
						isBlockEscape = true;
					}
				}
				if (!isBlockEscape)
				{
					var posStartChar = pos;
					if (!EXPECT_literal_char(text, ref pos, out var cMin, out errorMessage))
					{
						charSet = default;
						return false;
					}
					c = PeekChar(text, pos);
					if (c != '-')
					{
						charSet |= AsciiCharSet.FromSingleChar(cMin);
					}
					else
					{
						// Read the '-'
						AdvanceSkippingWS(text, ref pos);
						if (!EXPECT_literal_char(text, ref pos, out var cMax, out errorMessage))
						{
							charSet = default;
							return false;
						}
						if (cMin > cMax)
						{
							charSet = default;
							pos = posStartChar;
							errorMessage = "The start character must be before the end character in a character range.";
							return false;
						}
						charSet |= AsciiCharSet.FromCharRange(cMin, cMax);
						c = PeekChar(text, pos);
					}
				}
			} while (c != ']');

			// Read the ']'
			AdvanceSkippingWS(text, ref pos);
			errorMessage = null;
			return true;
		}
		else
		{
			charSet = default;
			errorMessage = $"Unexpected character {SafeFormatChar(c)}.";
			return false;
		}
	}
	/// <summary>
	/// <i>literal_char</i> ::= ["0","9"] | ["A"-"Z"] | ["a"-"z"] 
	///     | "$" | "%" | "&amp;" | "'" | "*" | "+" | "," | "." | "/" | ":" | ";" | "&lt;" | "=" | ">" | "@" | "\" | "^" | "_" | "`" | "{" | "}" | "~"
	///     | <i>escaped_literal_char</i>
	/// <br/>
	/// <i>escaped_literal_char</i> ::= "\"  ("s" | "!" | """ | "#" | "(" | ")"  | "-"  | "?"  | "["  | "]"  | "|" )
	/// </summary>
	/// <param name="text">The text to parse (from position <paramref name="pos"/>).</param>
	/// <param name="pos">The position of the next character to be read in <paramref name="text"/>. (Guaranteed not to be a space.)</param>
	/// <param name="c">The character read (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <returns>Whether the parse succeeeded.</returns>
	static bool EXPECT_literal_char(ReadOnlySpan<char> text, ref int pos, out char c, [NotNullWhen(false)] out string? errorMessage)
	{
		c = PeekChar(text, pos);

		if (IsNonEscapedLiteralChar(c))
		{
			AdvanceSkippingWS(text, ref pos);
			errorMessage = null;
			return true;
		}

		if (c == '\\')
		{
			if (!TryAdvanceAfterEscape(text, ref pos, out errorMessage))
			{
				return false;
			}
			c = PeekChar(text, pos);

			if (c == 'x')
			{
				AdvanceSkippingWS(text, ref pos);
				return EXPECT_hex_literal_char(text, ref pos, out c, out errorMessage);
			}
			else if (IsEscapedLiteralChar(c))
			{
				if (c == 's') { c = ' '; }
				AdvanceSkippingWS(text, ref pos);
				errorMessage = null;
				return true;
			}
			errorMessage = $"Unexpected escaped character {SafeFormatChar(c)}.";
			return false;
		}

		errorMessage = "Expected a literal character, e.g. 'A' or '\\('.";
		return false;
	}
	/// <summary>
	/// Reads a hexadecimal char literal after the "\x" has been consumed.
	/// </summary>
	/// <param name="text">The text to parse (from position <paramref name="pos"/>).</param>
	/// <param name="pos">The position of the next character to be read in <paramref name="text"/>. (Guaranteed not to be a space.)</param>
	/// <param name="c">The character read (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <returns>Whether the parse succeeeded.</returns>
	static bool EXPECT_hex_literal_char(ReadOnlySpan<char> text, ref int pos, out char c, [NotNullWhen(false)] out string? errorMessage)
	{
		static bool IsHexDigit(char c) => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');
		static int GetHexValue(char c) => c <= '9' ? (c - '0') : ((c <= 'Z' ? (c - 'A') : (c - 'a')) + 0xA);

		char c_0 = PeekChar(text, pos);
		if (!IsHexDigit(c_0))
		{
			c = (char)0;
			errorMessage = "Missing hexadecimal digit after hex escape '\\x'.";
			return false;
		}

		int h_0 = GetHexValue(c_0);
		if (h_0 >= 0x8)
		{
			c = (char)0;
			errorMessage = "The first hexadecimal digit after hex escape '\\x' must be less than '8' in order to be an ASCII character.";
			return false;
		}

		AdvanceSkippingWS(text, ref pos);

		char c_1 = PeekChar(text, pos);
		if (!IsHexDigit(c_1))
		{
			c = (char)0;
			errorMessage = "Missing second hexadecimal digit after hex escape '\\x'.";
			return false;
		}

		int h_1 = GetHexValue(c_1);
		c = (char)(0x10 * h_0 + h_1);

		// I think this check is here because I was assuming that code 0 would be
		// used to indicate the end of text. But that's not actually how I've
		// implemented things and so we no longer need the check.
		//if (c == 0)
		//{
		//    errorMessage = "ASCII code 0 is illegal.";
		//    return false;
		//}

		AdvanceSkippingWS(text, ref pos);
		errorMessage = null;
		return true;
	}
	static bool EXPECT_digits_range_literal(ReadOnlySpan<char> text, ref int pos, [NotNullWhen(true)] out Ast? ast, [NotNullWhen(false)] out string? errorMessage)
	{
		Debug.Assert(PeekChar(text, pos) == '#');

		Span<byte> buffer = stackalloc byte[MaxLiteralDigitCount * 2];
		Span<byte> digitsLo = buffer[..MaxLiteralDigitCount];
		Span<byte> digitsHi = buffer[MaxLiteralDigitCount..];

		var startPos = pos;

		// Digits range literal, e.g. #[001-345]
		AdvanceSimple(text, ref pos);
		char c = PeekChar(text, pos);
		if (c != '[')
		{
			ast = null;
			errorMessage = "Expected '[' after '#' for digits range.";
			if (IsWS(c))
			{
				var pos2 = pos;
				AdvanceSkippingWS(text, ref pos2);
				if (PeekChar(text, pos2) == '[')
				{
					errorMessage = "There should be no whitespace between '#' and '[' in a digits range.";
				}
			}
			return false;
		}
		AdvanceSkippingWS(text, ref pos);

		if (!EXPECT_digits_number_literal(text, ref pos, digitsLo, out var digitCount, out var leadingZeroCountLo, out errorMessage))
		{
			ast = null;
			return false;
		}
		digitsLo = digitsLo[..digitCount];

		c = PeekChar(text, pos);
		if (c != '-')
		{
			ast = null;
			errorMessage = "Expected '-' between min and max for digits range.";
			if (c == ',') { errorMessage += " (',' is not the correct separator)."; }
			return false;
		}
		AdvanceSkippingWS(text, ref pos);

		if (!EXPECT_digits_number_literal(text, ref pos, digitsHi, out digitCount, out var leadingZeroCountHi, out errorMessage))
		{
			ast = null;
			return false;
		}
		digitsHi = digitsHi[..digitCount];

		c = PeekChar(text, pos);
		if (c != ']')
		{
			ast = null;
			errorMessage = "Expected ']'.";
			return false;
		}
		AdvanceSkippingWS(text, ref pos);

		if (!TryGenerateDigitsLiteralRange(
				digitsLo, leadingZeroCountLo
				, digitsHi, leadingZeroCountHi
				, out ast
				, out errorMessage
				))
		{
			pos = startPos;
			return false;
		}

		return true;
	}

	/// <summary>
	/// Reads a digits literal (inside <c>[a-b]</c>).
	/// </summary>
	/// <param name="text">The text to parse (from position <paramref name="pos"/>).</param>
	/// <param name="pos">The position of the next character to be read in <paramref name="text"/>. (Guaranteed not to be a space.)</param>
	/// <param name="digits">The span of bytes to populate with digits values (not chars) (if the methods returns <see langword="true"/>).</param>
	/// <param name="digitCount">The number of digits including leading zeros (if the methods returns <see langword="true"/>).</param>
	/// <param name="leadingZeroCount">The number of leading zeros (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <returns>Whether the parse succeeeded.</returns>
	static bool EXPECT_digits_number_literal(ReadOnlySpan<char> text, ref int pos
		, Span<byte> digits
		, out int digitCount
		, out int leadingZeroCount
		, [NotNullWhen(false)] out string? errorMessage
		)
	{
		char c = PeekChar(text, pos);
		if (!IsDigit(c))
		{
			digitCount = 0;
			leadingZeroCount = 0;
			errorMessage = "Digit expected.";
			return false;
		}

		digits[0] = (byte)(c - '0');
		digitCount = 1;
		leadingZeroCount = c == '0' ? 1 : 0;
		bool seenNonZero = c != '0';
		if (!TryAdvanceAfterDigitInARange(text, ref pos, out errorMessage))
		{
			return false;
		}

		while (IsDigit(c = PeekChar(text, pos)))
		{
			if (digitCount >= MaxLiteralDigitCount)
			{
				digitCount = 0;
				leadingZeroCount = 0;
				errorMessage = "More than 15 digits in a digits range.";
				return false;
			}

			digits[digitCount] = (byte)(c - '0');
			++digitCount;

			if (!TryAdvanceAfterDigitInARange(text, ref pos, out errorMessage))
			{
				return false;
			}

			if (!seenNonZero)
			{
				if (c == '0') { ++leadingZeroCount; }
				else { seenNonZero = true; }
			}
		}

		errorMessage = null;
		return true;
	}
	#endregion
	#region Handling digits ranges
	/// <summary>
	/// 
	/// </summary>
	/// <param name="digitsLo"></param>
	/// <param name="leadingZeroCountLo"></param>
	/// <param name="digitsHi"></param>
	/// <param name="leadingZeroCountHi"></param>
	/// <param name="ast"></param>
	/// <param name="errorMessage"></param>
	/// <returns></returns>
	static bool TryGenerateDigitsLiteralRange(
		Span<byte> digitsLo, int leadingZeroCountLo
		, Span<byte> digitsHi, int leadingZeroCountHi
		, [NotNullWhen(true)] out Ast? ast
		, [NotNullWhen(false)] out string? errorMessage
		)
	{
#if DEBUG
		foreach (var digit in digitsLo)
		{
			if (digit > 9) { throw new ArgumentOutOfRangeException(nameof(digitsLo), "Contains a non-digit."); }
		}
		foreach (var digit in digitsHi)
		{
			if (digit > 9) { throw new ArgumentOutOfRangeException(nameof(digitsLo), "Contains a non-digit."); }
		}
		Debug.Assert(leadingZeroCountLo >= 0);
		Debug.Assert(leadingZeroCountHi >= 0);
#endif

		#region Check args
		if (digitsLo.Length > digitsHi.Length)
		{
			ast = null;
			errorMessage = "The first value cannot have more digits than the second value in a digits range.";
			return false;
		}
		if (digitsLo.Length < digitsHi.Length && leadingZeroCountHi != 0)
		{
			ast = null;
			errorMessage = "If the second value has more digits than the first in a digits range then it cannot have leading zeros.";
			return false;
		}
		var valueLo = DigitsToValue(digitsLo);
		var valueHi = DigitsToValue(digitsHi);
		if (valueLo > valueHi)
		{
			ast = null;
			errorMessage = "The first value cannot be greater than the second value in a digits range.";
			return false;
		}
		#endregion

		// Given the above conditions, we are guaranteed to succeed, so the remaining calls just return an `Ast`.

		errorMessage = null;
		ast = GetAstFromDigitsRange(digitsLo, digitsHi);
#if DEBUG
		var old_ast = ast;
#endif
		Ast.Simplify(ref ast);
		return true;
	}
	static Ast GetAstFromDigitsRange(Span<byte> digitsLo, Span<byte> digitsHi)
	{
		Span<byte> buffer = stackalloc byte[MaxLiteralDigitCount * 2];
		Span<byte> buffer_A = buffer[..MaxLiteralDigitCount];
		Span<byte> buffer_B = buffer[MaxLiteralDigitCount..];

		// 1. Recursively split into groups with the same number of digits
		// Δ=2 : [39-1523] → Δ=0 : [39-99] | Δ=1 : [100-1523]
		//     Δ=1 : [100-1523] → Δ=0 : [100-999] | Δ=0 : [1000-123]
		// 2. Recursively beakdown by leading digit into at most 3 parts, noting that the 'middle' item is fully solved
		// [023-471] → 0[23-99] | [1-3][0-9][0-9] | 4[00-71]
		//     [23-99] → 2[3-9] | [3-9][0-9]
		//     [00-71] → 0[0-9] | [7][0-1]
		// Note that
		// - all 0s xor all 9s leads to two items, and
		// - all 0s and all 9s leads to one solved item.

		int n = digitsLo.Length;
		var Δ = digitsHi.Length - n;

		if (Δ != 0)
		{
			// Δ=2 : [39-1523] → Δ=0 : [39-99] | Δ=1 : [100-1523]

			Debug.Assert(Δ > 0);

			// 99
			var digitsLeftHi = buffer_A[..n];
			digitsLeftHi.Fill(9);
			var ast_A = GetAstFromDigitsRange(digitsLo, digitsLeftHi);

			// 100
			var digitsRightLo = buffer_A[..(n + 1)];
			digitsRightLo[0] = 1;
			digitsRightLo[1..].Clear();
			var ast_B = GetAstFromDigitsRange(digitsRightLo, digitsHi);

			return new Or([ast_A, ast_B]);
		}
		else
		{
			Debug.Assert(digitsLo.Length > 0);

			// [023-471] → 0[23-99] | [1-3][00-99] | 4[00-71]
			//     [23-99] → 2[3-9] | [3-9][0-9]
			//     [00-99] → [0-9][0-9]
			//     [00-71] → [0-6][0-9] | 7[0-1]

			var significantLo = digitsLo[0];
			var significantHi = digitsHi[0];

			Debug.Assert(significantLo <= significantHi);

			if (n == 1)
			{
				return new Chars(AsciiCharSet.FromCharRange((char)('0' + significantLo), (char)('0' + significantHi)));
			}
			else if (significantLo == significantHi)
			{
				var ast_head = new Chars(AsciiCharSet.FromSingleChar((char)('0' + significantLo)));
				var ast_tail = GetAstFromDigitsRange(digitsLo[1..], digitsHi[1..]);
				return new Seq([ast_head, ast_tail]);
			}
			else
			{
				var astLeft_head = new Chars(AsciiCharSet.FromSingleChar((char)('0' + significantLo)));
				var digitsLeftHi = buffer_A[..(n - 1)];
				digitsLeftHi.Fill(9);
				var astLeft_tail = GetAstFromDigitsRange(digitsLo[1..], digitsLeftHi);
				var seqLeft = new Seq([astLeft_head, astLeft_tail]);

				var astRight_head = new Chars(AsciiCharSet.FromSingleChar((char)('0' + significantHi)));
				var digitsRightLo = buffer_A[..(n - 1)];
				digitsRightLo.Clear();
				var astRight_tail = GetAstFromDigitsRange(digitsRightLo, digitsHi[1..]);
				var seqRight = new Seq([astRight_head, astRight_tail]);

				if (significantHi - significantLo == 1)
				{
					return new Or([seqLeft, seqRight]);
				}
				else
				{
					Debug.Assert(significantHi - significantLo >= 2);
					var astMiddle_head = new Chars(AsciiCharSet.FromCharRange((char)('0' + significantLo + 1), (char)('0' + significantHi - 1)));
					var digitsMiddleLo = buffer_A[..(n - 1)];
					digitsMiddleLo.Clear();
					var digitsMiddleHi = buffer_B[..(n - 1)];
					digitsMiddleHi.Fill(9);
					var astMiddle_tail = GetAstFromDigitsRange(digitsMiddleLo, digitsMiddleHi);
					var seqMiddle = new Seq([astMiddle_head, astMiddle_tail]);

					return new Or([seqLeft, seqMiddle, seqRight]);
				}
			}
		}
	}
	static ulong DigitsToValue(Span<byte> digits)
	{
		ulong value = 0;
		foreach (var digit in digits) { value = 10 * value + digit; }
		return value;
	}
	#endregion
	#region Private helper methods
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static char PeekChar(ReadOnlySpan<char> text, int pos) => (uint)pos < (uint)text.Length ? text[pos] : (char)0;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable IDE0060 // Remove unused parameter
	static void AdvanceSimple(ReadOnlySpan<char> text, ref int pos) { ++pos; }
#pragma warning restore IDE0060 // Remove unused parameter
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static void AdvanceSkippingWS(ReadOnlySpan<char> text, ref int pos)
	{
		for (; ; )
		{
			++pos;
			if ((uint)pos >= (uint)text.Length || !IsWS(text[pos]))
			{
				return;
			}
		}
	}
	static bool TryAdvanceAfterDigitInARange(ReadOnlySpan<char> text, ref int pos, [NotNullWhen(false)] out string? errorMessage)
	{
		Debug.Assert((uint)pos < (uint)text.Length && IsDigit(text[pos]));

		++pos;
		if ((uint)pos >= (uint)text.Length || !IsWS(text[pos]))
		{
			errorMessage = null;
			return true;
		}

		// We end up here only if we've seen whitespace.

		var posWS = pos;
		for (; ; )
		{
			++pos;
			if ((uint)pos >= (uint)text.Length)
			{
				errorMessage = null;
				return true;
			}
			char c = text[pos];
			if (!IsWS(c))
			{
				if (IsDigit(c))
				{
					pos = posWS;
					errorMessage = "The digits in a digits range cannot be separated by whitespace.";
					return false;
				}
				else
				{
					errorMessage = null;
					return true;
				}
			}
		}
	}
	static bool TryAdvanceAfterEscape(ReadOnlySpan<char> text, ref int pos, [NotNullWhen(false)] out string? errorMessage)
	{
		Debug.Assert((uint)pos < (uint)text.Length && text[pos] == '\\');

		++pos;
		if ((uint)pos >= (uint)text.Length || !IsWS(text[pos]))
		{
			errorMessage = null;
			return true;
		}
		else
		{
			errorMessage = "A '\\' cannot be followed by whitespace. To match a space write '\\s'.";
			return false;
		}
	}
	static bool IsDigit(char c) => c is >= '0' and <= '9';
	static bool IsWS(char c) => c is ' ' or '\t' or '\r' or '\n';
	static bool IsStartOfElement(char c) => IsNonEscapedLiteralChar(c)
		|| c == '('
		|| c == '\\'
		|| c == '['
		|| c == '#'
		;
	static string SafeFormatChar(char c)
	{
		if (c == 0)
		{
			return "end of text";
		}

		switch (char.GetUnicodeCategory(c))
		{
			case UnicodeCategory.UppercaseLetter:
			case UnicodeCategory.LowercaseLetter:
			case UnicodeCategory.TitlecaseLetter:
			case UnicodeCategory.ModifierLetter:
			case UnicodeCategory.OtherLetter:
			case UnicodeCategory.DecimalDigitNumber:
			case UnicodeCategory.LetterNumber:
			case UnicodeCategory.OtherNumber:
			case UnicodeCategory.ConnectorPunctuation:
			case UnicodeCategory.DashPunctuation:
			case UnicodeCategory.OpenPunctuation:
			case UnicodeCategory.ClosePunctuation:
			case UnicodeCategory.InitialQuotePunctuation:
			case UnicodeCategory.FinalQuotePunctuation:
			case UnicodeCategory.OtherPunctuation:
			case UnicodeCategory.MathSymbol:
			case UnicodeCategory.CurrencySymbol:
			case UnicodeCategory.ModifierSymbol:
			case UnicodeCategory.OtherSymbol:
				return $"character \'{c}\' (U+{(uint)c:X4})";

			default:
				return $"character U+{(uint)c:X4}";
		}
	}
	static bool IsNonEscapedLiteralChar(char c) => c switch
	{
		'$' => true, // 0x24
		'%' => true, // 0x25
		'&' => true, // 0x26
		'\'' => true, // 0x27
		'*' => true, // 0x2A
		'+' => true, // 0x2B
		',' => true, // 0x2C
		'.' => true, // 0x2E
		'/' => true, // 0x2F
		>= '0' and <= '9' => true, // [ 0x30,0x39]
		':' => true, // 0x3A
		';' => true, // 0x3B
		'<' => true, // 0x3C
		'=' => true, // 0x3D
		'>' => true, // 0x3E
		'@' => true, // 0x40
		>= 'A' and <= 'Z' => true, // [ 0x41,0x5A]
		'^' => true, // 0x5E
		'_' => true, // 0x5F
		'`' => true, // 0x60
		>= 'a' and <= 'z' => true, // [ 0x61,0x7A]
		'{' => true, // 0x7B
		'}' => true, // 0x7D
		'~' => true, // 0x7E
		_ => false,
	};
	static bool IsEscapedLiteralChar(char c) => c switch
	{
		's' => true, // Short for space !!

		'!' => true, // 0x21
		'"' => true, // 0x22
		'#' => true, // 0x23
		'(' => true, // 0x28
		')' => true, // 0x29
		'-' => true, // 0x2D
		'[' => true, // 0x5B
		'\\' => true, // 0x5C
		']' => true, // 0x5D
		'|' => true, // 0x7C

		_ => false,
	};
	/// <summary>
	/// Whether the specified character represents a block escape when preceded by `\`, i.e.
	/// '9', 'A', 'a' and 'X'.
	/// </summary>
	/// <param name="c"></param>
	/// <param name="charSet"></param>
	/// <returns></returns>
	static bool IsBlockEscapeChar(char c, out AsciiCharSet charSet)
	{
		switch (c)
		{
			case '9': charSet = AsciiCharSet.AllDigits; return true;
			case 'A': charSet = AsciiCharSet.AllUpperCaseLetters; return true;
			case 'a': charSet = AsciiCharSet.AllLowerCaseLetters; return true;
			case 'X': charSet = AsciiCharSet.AllDigitsAndUpperCaseLetters; return true;
		}
		charSet = default;
		return false;
	}
	#endregion
}