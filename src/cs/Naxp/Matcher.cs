// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LogMu;

/// <summary>
/// Whether an expression generates exactly one string.
/// </summary>
enum SingleStringOutcome
{
	/// <summary>The expression generates exactly one string.</summary>
	Single,
	/// <summary>It generates none, or more than one.</summary>
	Multiple,
	/// <summary>It generates one string, but longer than <see cref="Matcher.MaxGeneratedLength"/>.</summary>
	TooLong,
}

/// <summary>
/// Matches a string against a parsed naxp, without building a machine.
/// </summary>
/// <remarks>
/// W1 and the canonical form both need an answer before any machine exists, W1 because it runs
/// before the build and canonicalisation because it works over the tree, where the replaceable
/// elements are still visible. Working with sets of positions rather than by backtracking keeps
/// the cost polynomial.
/// </remarks>
static class Matcher
{
	#region Does an expression generate exactly one string?
	/// <summary>
	/// The most characters this implementation will materialise for a single generated string.
	/// </summary>
	/// <remarks>
	/// Not a rule of the language. A naxp generating a longer string than this would be refused
	/// by <see cref="NaxpLimits.MaxStates"/> a moment later in any case; see
	/// <see cref="NaxpLimits.MaxStringLength"/> for why the two are tied together.
	/// </remarks>
	internal const int MaxGeneratedLength = NaxpLimits.MaxStringLength;

	internal static SingleStringOutcome TryGetSingleString(Ast node, out string? result)
	{
		var builder = new StringBuilder();
		SingleStringOutcome outcome = AppendSingleString(node, builder);

		result = outcome == SingleStringOutcome.Single ? builder.ToString() : null;
		return outcome;
	}

	static SingleStringOutcome AppendSingleString(Ast node, StringBuilder builder)
	{
		switch (node)
		{
			case AstEmpty:
				return SingleStringOutcome.Single;

			case AstChars chars:
			{
				char? single = chars.CharSet.SingleCharacter;
				if (single is null) { return SingleStringOutcome.Multiple; }

				builder.Append(single.Value);
				return Within(builder);
			}

			case AstDigitsRange range:
			{
				// One string only where the two bounds are the same number written to the same width.
				if (range.Low != range.High || range.LowDigitCount != range.HighDigitCount)
				{
					return SingleStringOutcome.Multiple;
				}

				builder.Append(range.Low.ToString(CultureInfo.InvariantCulture).PadLeft(range.LowDigitCount, '0'));
				return Within(builder);
			}

			case AstSequence sequence:
			{
				foreach (Ast child in sequence.Children)
				{
					SingleStringOutcome outcome = AppendSingleString(child, builder);
					if (outcome != SingleStringOutcome.Single) { return outcome; }
				}

				return SingleStringOutcome.Single;
			}

			case AstAlternation alternation:
			{
				// Every alternative must give the same one string, so 'A|A' generates one.
				var firstBuilder = new StringBuilder();
				SingleStringOutcome firstOutcome = AppendSingleString(alternation.Children[0], firstBuilder);
				if (firstOutcome != SingleStringOutcome.Single) { return firstOutcome; }

				string first = firstBuilder.ToString();

				for (int i = 1; i < alternation.Children.Count; ++i)
				{
					var otherBuilder = new StringBuilder();
					SingleStringOutcome otherOutcome = AppendSingleString(alternation.Children[i], otherBuilder);
					if (otherOutcome != SingleStringOutcome.Single) { return otherOutcome; }

					if (!string.Equals(first, otherBuilder.ToString(), StringComparison.Ordinal))
					{
						return SingleStringOutcome.Multiple;
					}
				}

				builder.Append(first);
				return Within(builder);
			}

			case AstOptional optional:
			{
				// x? always generates the empty string, so it is single valued only where x does too.
				var inner = new StringBuilder();
				SingleStringOutcome outcome = AppendSingleString(optional.Child, inner);
				if (outcome == SingleStringOutcome.TooLong) { return outcome; }

				return outcome == SingleStringOutcome.Single && inner.Length == 0
					? SingleStringOutcome.Single
					: SingleStringOutcome.Multiple
					;
			}

			case AstInterval interval:
			{
				// A zero count denotes the empty string whatever the child generates.
				if (interval.MaxCount == 0) { return SingleStringOutcome.Single; }

				var inner = new StringBuilder();
				SingleStringOutcome outcome = AppendSingleString(interval.Child, inner);
				if (outcome != SingleStringOutcome.Single) { return outcome; }

				if (inner.Length == 0) { return SingleStringOutcome.Single; }
				if (interval.MinCount != interval.MaxCount) { return SingleStringOutcome.Multiple; }

				if ((long)inner.Length * interval.MinCount > MaxGeneratedLength) { return SingleStringOutcome.TooLong; }

				string once = inner.ToString();
				for (int i = 0; i < interval.MinCount; ++i) { builder.Append(once); }

				return Within(builder);
			}

			case AstReplaceable replaceable:
				// The strings x!y generates are the strings x accepts. W2 has already refused any
				// tree that reaches this case from within another '!'.
				return AppendSingleString(replaceable.Subject, builder);

			default:
				throw new InvalidOperationException($"Unhandled node type {node.GetType().Name}.");
		}
	}

	static SingleStringOutcome Within(StringBuilder builder)
		=> builder.Length <= MaxGeneratedLength ? SingleStringOutcome.Single : SingleStringOutcome.TooLong;
	#endregion
	#region Does an expression generate a given string?
	/// <summary>
	/// Whether <paramref name="node"/> generates <paramref name="text"/> exactly.
	/// </summary>
	/// <param name="node">The expression.</param>
	/// <param name="text">The string it must generate in full.</param>
	/// <param name="tooLong">Whether the answer was abandoned as too large to compute.</param>
	/// <returns>Whether the expression generates the string.</returns>
	internal static bool Generates(Ast node, ReadOnlySpan<char> text, out bool tooLong)
	{
		if (node is null) { throw new ArgumentNullException(nameof(node)); }

		tooLong = false;

		if (text.Length > MaxGeneratedLength)
		{
			tooLong = true;
			return false;
		}

		HashSet<int> ends = Advance(node, text, new HashSet<int> { 0 });
		return ends.Contains(text.Length);
	}

	/// <summary>
	/// The set of positions reachable by matching <paramref name="node"/> from each of
	/// <paramref name="starts"/>.
	/// </summary>
	/// <remarks>
	/// Working with sets of positions rather than backtracking keeps the cost polynomial. There
	/// are at most one more positions than there are characters, so an alternation cannot
	/// multiply the work.
	/// </remarks>
	internal static HashSet<int> Advance(Ast node, ReadOnlySpan<char> text, HashSet<int> starts)
	{
		if (starts.Count == 0) { return starts; }

		switch (node)
		{
			case AstEmpty:
				return starts;

			case AstChars chars:
			{
				var result = new HashSet<int>();
				foreach (int p in starts)
				{
					if (p < text.Length && chars.CharSet.Contains(text[p])) { result.Add(p + 1); }
				}

				return result;
			}

			case AstDigitsRange range:
				return AdvanceDigitsRange(range, text, starts);

			case AstSequence sequence:
			{
				HashSet<int> current = starts;
				foreach (Ast child in sequence.Children)
				{
					current = Advance(child, text, current);
					if (current.Count == 0) { break; }
				}

				return current;
			}

			case AstAlternation alternation:
			{
				var result = new HashSet<int>();
				foreach (Ast child in alternation.Children)
				{
					result.UnionWith(Advance(child, text, starts));
				}

				return result;
			}

			case AstOptional optional:
			{
				var result = new HashSet<int>(starts);
				result.UnionWith(Advance(optional.Child, text, starts));
				return result;
			}

			case AstInterval interval:
			{
				HashSet<int> result = interval.MinCount == 0 ? new HashSet<int>(starts) : new HashSet<int>();
				HashSet<int> current = starts;

				for (int i = 1; i <= interval.MaxCount; ++i)
				{
					HashSet<int> next = Advance(interval.Child, text, current);
					if (next.Count == 0) { break; }

					if (i >= interval.MinCount) { result.UnionWith(next); }

					// A child that matches the empty string reaches a fixed point at once, and
					// the remaining repetitions add nothing. Without this the largest count
					// costs that many passes over the string for no gain.
					if (next.SetEquals(current) && i >= interval.MinCount) { break; }

					current = next;
				}

				return result;
			}

			case AstReplaceable replaceable:
				// x!y accepts whatever x accepts.
				return Advance(replaceable.Subject, text, starts);

			default:
				throw new InvalidOperationException($"Unhandled node type {node.GetType().Name}.");
		}
	}

	/// <summary>
	/// Matches a digits range without expanding it.
	/// </summary>
	/// <remarks>
	/// A string of <c>w</c> digits is generated when <c>w</c> lies between the two written
	/// widths, its value is at least the lower bound if <c>w</c> is the lower width, its value
	/// is at most the upper bound if <c>w</c> is the upper width, and it has no leading zero
	/// unless <c>w</c> is the lower width. That last clause is what makes <c>#[0-105]</c>
	/// expand to <c>[0-9] | [1-9][0-9] | 10[0-5]</c> rather than admitting <c>07</c>.
	/// </remarks>
	static HashSet<int> AdvanceDigitsRange(AstDigitsRange range, ReadOnlySpan<char> text, HashSet<int> starts)
	{
		var result = new HashSet<int>();

		foreach (int p in starts)
		{
			ulong value = 0UL;

			for (int width = 1; width <= range.HighDigitCount; ++width)
			{
				int index = p + width - 1;
				if (index >= text.Length) { break; }

				char c = text[index];
				if (c < '0' || c > '9') { break; }

				value = (value * 10UL) + (ulong)(c - '0');

				if (width < range.LowDigitCount) { continue; }
				if (width > range.LowDigitCount && text[p] == '0') { continue; }
				if (width == range.LowDigitCount && value < range.Low) { continue; }
				if (width == range.HighDigitCount && value > range.High) { continue; }

				result.Add(p + width);
			}
		}

		return result;
	}
	#endregion
}
