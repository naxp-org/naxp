// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;

namespace LogMu;

/// <summary>
/// The expansion of a decimal range whose lower bound carries marks, as in <c>#[0!0!0-105]</c>.
/// </summary>
/// <remarks>
/// <para>
/// A mark is shorthand and nothing downstream of the parser sees one. <c>0!</c> becomes
/// <c>0!!</c> and <c>0?</c> becomes <c>0!?</c>, so the language, the well-formedness rules and the
/// encoding all apply to the expansion and none of them needs a case for padding. W2 falls out of
/// this in the same way it does for a case fold: a marked range inside the operand of a <c>!</c>
/// puts a <c>!</c> there, which the existing nesting check refuses.
/// </para>
/// <para>
/// The shape is one alternative per width the value itself takes, with the padding positions that
/// apply to that width written out in front. A padding position is mandatory where it carries no
/// mark, which is what makes an unmarked leading zero go on setting a minimum width.
/// </para>
/// <para>
/// A unified element contributes its rendering whether or not its subject matched anything, so a
/// run of padding contributes a fixed string. That is what keeps <c>07</c> to one canonical form
/// under <c>0!! 0!!</c>, where either of the two could have been the one that matched.
/// </para>
/// <para>
/// Every node made here takes the pattern offset of the range it replaces, so a fault found in an
/// expansion still points at what the author wrote.
/// </para>
/// </remarks>
static class Padding
{
	#region Private data
	static readonly ulong[] PowersOfTen = BuildPowersOfTen();
	#endregion
	#region Public entry point
	/// <summary>
	/// Expands a marked decimal range into an alternation of ordinary elements.
	/// </summary>
	/// <param name="low">The value of the lower bound.</param>
	/// <param name="lowDigitCount">The digits the lower bound was written with.</param>
	/// <param name="high">The value of the upper bound.</param>
	/// <param name="highDigitCount">The digits the upper bound was written with.</param>
	/// <param name="marks">
	/// One entry per digit of the lower bound, each <c>'!'</c>, <c>'?'</c> or nul where that digit
	/// carries no mark. The array may be longer than the bound.
	/// </param>
	/// <param name="offset">The offset of the range in the pattern, for diagnostics.</param>
	/// <returns>The expansion.</returns>
	public static Ast Expand(
		ulong low,
		int lowDigitCount,
		ulong high,
		int highDigitCount,
		char[] marks,
		int offset)
	{
		if (marks is null) { throw new ArgumentNullException(nameof(marks)); }

		var alternatives = new List<Ast>(highDigitCount);

		for (int width = 1; width <= highDigitCount; ++width)
		{
			ulong lowest = width == 1 ? 0UL : PowersOfTen[width - 1];
			ulong highest = PowersOfTen[width] - 1UL;

			ulong first = Math.Max(low, lowest);
			ulong last = Math.Min(high, highest);

			if (first > last) { continue; }

			alternatives.Add(Alternative(first, last, width, lowDigitCount, marks, offset));
		}

		return alternatives.Count == 1
			? alternatives[0]
			: new AstAlternation(alternatives) { PatternOffset = offset }
			;
	}
	#endregion
	#region Private methods
	/// <summary>
	/// The alternative for values of one width: its padding, then the values themselves.
	/// </summary>
	static Ast Alternative(ulong first, ulong last, int width, int lowDigitCount, char[] marks, int offset)
	{
		int padCount = Math.Max(0, lowDigitCount - width);
		Ast values = new AstDecimalRange(first, width, last, width) { PatternOffset = offset };

		if (padCount == 0) { return values; }

		var parts = new List<Ast>(padCount + 1);

		for (int position = 0; position < padCount; ++position)
		{
			parts.Add(PaddingPosition(marks[position], offset));
		}

		parts.Add(values);

		return new AstSequence(parts) { PatternOffset = offset };
	}

	/// <summary>
	/// One padding position: a mandatory zero, or the unified element its mark stands for.
	/// </summary>
	static Ast PaddingPosition(char mark, int offset)
	{
		Ast zero = Zero(offset);

		if (mark == '\0') { return zero; }

		// The expansions are the ones the specification gives: 0! is 0!!, which is 0?!(0), and
		// 0? is 0!?, which is 0?!().
		Ast subject = new AstOptional(zero) { PatternOffset = offset };
		bool reproduced = mark == '!';

		Ast rendering = reproduced
			? Zero(offset)
			: new AstEmpty { PatternOffset = offset }
			;

		UnifiedForm form = reproduced ? UnifiedForm.Reproduced : UnifiedForm.Dropped;

		return new AstUnified(subject, rendering, form) { PatternOffset = offset };
	}

	static Ast Zero(int offset)
		=> new AstChars(AsciiCharSet.FromSingleChar('0')) { PatternOffset = offset };

	static ulong[] BuildPowersOfTen()
	{
		var powers = new ulong[16];
		powers[0] = 1UL;
		for (int i = 1; i < powers.Length; ++i) { powers[i] = powers[i - 1] * 10UL; }

		return powers;
	}
	#endregion
}
