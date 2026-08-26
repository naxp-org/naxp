// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;

namespace LogMu;

/// <summary>
/// Which of a naxp's two languages an expression is being built for.
/// </summary>
enum NaxpLanguage
{
	/// <summary>The accepted language <i>L</i>, the strings the naxp matches.</summary>
	Accepted,

	/// <summary>
	/// The canonical language <i>C</i>, which is <i>L</i> with each replaceable element replaced
	/// by its rendering. The encoding is a rank over this one.
	/// </summary>
	Canonical,
}

/// <summary>
/// Turns a parsed naxp into the algebra the state map is built over.
/// </summary>
/// <remarks>
/// <para>
/// This is step 1 of the specification's procedure. The canonicalisation table there has three
/// rows, <c>x!y</c> to <c>y</c>, <c>x!!</c> to <c>x</c> and <c>x!?</c> to <c>()</c>, but the
/// parser already expanded the two abbreviations into the general form, so all three collapse
/// to taking the rendering.
/// </para>
/// <para>
/// Digits ranges are expanded here, because a bound of fifteen digits expands to about fifteen
/// alternatives and costs nothing. Intervals are not, because their counts multiply when nested.
/// </para>
/// </remarks>
static class RxConverter
{
	/// <summary>Powers of ten up to the fifteen digit cap on a digits range bound.</summary>
	static readonly ulong[] PowersOfTen = BuildPowersOfTen();

	public static Rx Convert(Ast node, RxFactory factory, NaxpLanguage language)
	{
		switch (node)
		{
			case AstEmpty:
				return factory.Epsilon;

			case AstChars chars:
				return factory.Chars(chars.CharSet);

			case AstDigitsRange range:
				return ConvertDigitsRange(range, factory);

			case AstSequence sequence:
			{
				var parts = new List<Rx>(sequence.Children.Count);
				foreach (Ast child in sequence.Children) { parts.Add(Convert(child, factory, language)); }

				return factory.Concat(parts);
			}

			case AstAlternation alternation:
			{
				var alternatives = new List<Rx>(alternation.Children.Count);
				foreach (Ast child in alternation.Children) { alternatives.Add(Convert(child, factory, language)); }

				return factory.Union(alternatives);
			}

			case AstOptional optional:
				return factory.Union(factory.Epsilon, Convert(optional.Child, factory, language));

			case AstInterval interval:
				return factory.Interval(Convert(interval.Child, factory, language), interval.MinCount, interval.MaxCount);

			case AstReplaceable replaceable:
				return Convert(
					language == NaxpLanguage.Canonical ? replaceable.Rendering : replaceable.Subject,
					factory,
					language);

			default:
				throw new InvalidOperationException($"Unhandled node type {node.GetType().Name}.");
		}
	}

	/// <summary>
	/// Expands a digits range into an ordinary expression.
	/// </summary>
	/// <remarks>
	/// One alternative per width. The lower width admits the leading zeros the lower bound was
	/// written with; every width above it does not, which is what makes <c>#[0-105]</c> stand
	/// for <c>[0-9] | [1-9][0-9] | 10[0-5]</c> rather than admitting <c>07</c>.
	/// </remarks>
	static Rx ConvertDigitsRange(AstDigitsRange range, RxFactory factory)
	{
		var widths = new List<Rx>(range.HighDigitCount - range.LowDigitCount + 1);

		for (int width = range.LowDigitCount; width <= range.HighDigitCount; ++width)
		{
			ulong low = width == range.LowDigitCount ? range.Low : PowersOfTen[width - 1];
			ulong high = width == range.HighDigitCount ? range.High : PowersOfTen[width] - 1UL;

			if (low > high) { continue; }

			widths.Add(FixedWidthRange(low, high, width, factory));
		}

		return factory.Union(widths);
	}

	/// <summary>
	/// The strings of exactly <paramref name="width"/> digits whose value lies between
	/// <paramref name="low"/> and <paramref name="high"/> inclusive, leading zeros included.
	/// </summary>
	static Rx FixedWidthRange(ulong low, ulong high, int width, RxFactory factory)
	{
		if (width == 0) { return factory.Epsilon; }

		// Every string of this width qualifies, so there is nothing to split on.
		if (low == 0UL && high == PowersOfTen[width] - 1UL)
		{
			return factory.Interval(factory.Chars(AsciiCharSet.AllDigits), width, width);
		}

		ulong place = PowersOfTen[width - 1];
		int lowLead = (int)(low / place);
		int highLead = (int)(high / place);
		ulong lowRest = low % place;
		ulong highRest = high % place;

		if (lowLead == highLead)
		{
			return factory.Concat(
				DigitChars(lowLead, lowLead, factory),
				FixedWidthRange(lowRest, highRest, width - 1, factory));
		}

		var alternatives = new List<Rx>(3)
		{
			factory.Concat(
				DigitChars(lowLead, lowLead, factory),
				FixedWidthRange(lowRest, place - 1UL, width - 1, factory)),
		};

		if (highLead - lowLead >= 2)
		{
			alternatives.Add(
				factory.Concat(
					DigitChars(lowLead + 1, highLead - 1, factory),
					factory.Interval(factory.Chars(AsciiCharSet.AllDigits), width - 1, width - 1)));
		}

		alternatives.Add(
			factory.Concat(
				DigitChars(highLead, highLead, factory),
				FixedWidthRange(0UL, highRest, width - 1, factory)));

		return factory.Union(alternatives);
	}

	static Rx DigitChars(int lowDigit, int highDigit, RxFactory factory)
		=> factory.Chars(AsciiCharSet.FromCharRange((char)('0' + lowDigit), (char)('0' + highDigit)));

	static ulong[] BuildPowersOfTen()
	{
		var powers = new ulong[16];
		powers[0] = 1UL;
		for (int i = 1; i < powers.Length; ++i) { powers[i] = powers[i - 1] * 10UL; }

		return powers;
	}
}
