// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LogMu.Generator;

/// <summary>
/// Mapping a position in a string back to the character in the source that wrote it.
/// </summary>
/// <remarks>
/// This is what lets a refusal underline the offending character of the naxp rather than the
/// whole attribute. Only a string literal on one line is mapped, which covers ordinary and
/// verbatim literals and the single-line raw form; anything else - a multi-line literal, a
/// constant referred to by name, a concatenation - falls back to the whole expression.
/// </remarks>
static class Literals
{
	/// <summary>
	/// Where each character of <paramref name="value"/> was written, as offsets from the start of
	/// <paramref name="expression"/>, with one more entry for the position past the last
	/// character. Empty where the expression cannot be mapped.
	/// </summary>
	public static ImmutableArray<int> MapOffsets(ExpressionSyntax expression, string value)
	{
		if (expression is not LiteralExpressionSyntax literal
			|| !literal.Token.IsKind(SyntaxKind.StringLiteralToken))
		{
			return ImmutableArray<int>.Empty;
		}

		SyntaxToken token = literal.Token;

		if (!IsOnOneLine(token)) { return ImmutableArray<int>.Empty; }

		string raw = token.Text;
		ImmutableArray<int>.Builder offsets = ImmutableArray.CreateBuilder<int>(value.Length + 1);

		if (raw.StartsWith("@\"", System.StringComparison.Ordinal))
		{
			if (!MapVerbatim(raw, value, offsets)) { return ImmutableArray<int>.Empty; }
		}
		else if (raw.StartsWith("\"\"\"", System.StringComparison.Ordinal))
		{
			if (!MapRaw(raw, value, offsets)) { return ImmutableArray<int>.Empty; }
		}
		else if (raw.StartsWith("\"", System.StringComparison.Ordinal))
		{
			if (!MapOrdinary(raw, value, offsets)) { return ImmutableArray<int>.Empty; }
		}
		else
		{
			return ImmutableArray<int>.Empty;
		}

		return offsets.Count == value.Length + 1 ? offsets.ToImmutable() : ImmutableArray<int>.Empty;
	}

	static bool IsOnOneLine(SyntaxToken token)
	{
		if (token.SyntaxTree is null) { return false; }

		FileLinePositionSpan span = token.SyntaxTree.GetLineSpan(token.Span);

		return span.StartLinePosition.Line == span.EndLinePosition.Line;
	}

	/// <summary>Maps <c>@"..."</c>, where the only escape is a doubled quote.</summary>
	static bool MapVerbatim(string raw, string value, ImmutableArray<int>.Builder offsets)
	{
		int i = 2;
		int end = raw.Length - 1;

		while (i < end && offsets.Count < value.Length)
		{
			offsets.Add(i);
			i += raw[i] == '"' && i + 1 < end && raw[i + 1] == '"' ? 2 : 1;
		}

		offsets.Add(i);

		return true;
	}

	/// <summary>Maps <c>"""..."""</c> on one line, where every character stands for itself.</summary>
	static bool MapRaw(string raw, string value, ImmutableArray<int>.Builder offsets)
	{
		int quotes = 0;

		while (quotes < raw.Length && raw[quotes] == '"') { quotes++; }

		int i = quotes;
		int end = raw.Length - quotes;

		while (i < end && offsets.Count < value.Length)
		{
			offsets.Add(i);
			i++;
		}

		offsets.Add(i);

		return true;
	}

	/// <summary>Maps <c>"..."</c>, walking the escape sequences.</summary>
	static bool MapOrdinary(string raw, string value, ImmutableArray<int>.Builder offsets)
	{
		int i = 1;
		int end = raw.Length - 1;

		while (i < end && offsets.Count < value.Length)
		{
			offsets.Add(i);

			if (raw[i] != '\\')
			{
				i++;
				continue;
			}

			if (i + 1 >= end) { return false; }

			char kind = raw[i + 1];

			switch (kind)
			{
				case 'x':
					{
						int digits = 0;

						while (digits < 4 && i + 2 + digits < end && IsHex(raw[i + 2 + digits])) { digits++; }

						if (digits == 0) { return false; }

						i += 2 + digits;
						break;
					}

				case 'u':
					if (i + 6 > end) { return false; }

					i += 6;
					break;

				case 'U':
					if (i + 10 > end) { return false; }

					// A code point above the basic plane is two characters of the decoded string,
					// both written by the one escape.
					if (offsets.Count < value.Length) { offsets.Add(i); }

					i += 10;
					break;

				default:
					i += 2;
					break;
			}
		}

		offsets.Add(i);

		return true;
	}

	static bool IsHex(char c)
		=> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
