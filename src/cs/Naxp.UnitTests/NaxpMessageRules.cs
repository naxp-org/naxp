// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;

namespace LogMu.UnitTests;

/// <summary>
/// Which rule of the specification each <see cref="NaxpMessage"/> belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The library does not carry this. A rule is a way of organising the language's requirements
/// rather than something a caller acts on, so in <c>NaxpMessages</c> it survives only as a comment
/// above each group, and no code there branches on it.
/// </para>
/// <para>
/// The tests do need it, because the conformance data tags every invalid naxp with a rule and the
/// point of those cases is that the right naxp is invalid <em>for the right reason</em>. So the
/// mapping lives here, in the thing doing the verifying. If it drifts from the comments the
/// conformance cases fail, which is what should happen.
/// </para>
/// <para>
/// The strings are the data's spelling, so <c>syntax</c> is lower case where the W rules are not.
/// </para>
/// </remarks>
static class NaxpMessageRules
{
	const string Syntax = "syntax";
	const string StateBudget = "W6";

	static readonly Dictionary<NaxpMessage, string> Rules = new()
	{
		[NaxpMessage.NAXP1001_QuantifierRepeated] = Syntax,
		[NaxpMessage.NAXP1002_IntervalHyphen] = Syntax,
		[NaxpMessage.NAXP1003_IntervalUnbounded] = Syntax,
		[NaxpMessage.NAXP1004_IntervalNotClosed] = Syntax,
		[NaxpMessage.NAXP1005_IntervalCountNotDigits] = Syntax,
		[NaxpMessage.NAXP1006_IntervalCountSplit] = Syntax,
		[NaxpMessage.NAXP1007_IntervalCountsOutOfOrder] = "W4",
		[NaxpMessage.NAXP1008_IntervalCountTooLong] = "W4",
		[NaxpMessage.NAXP1009_GroupNotClosed] = Syntax,
		[NaxpMessage.NAXP1010_ReproducedAfterOptional] = Syntax,
		[NaxpMessage.NAXP1011_DroppedAfterOptional] = Syntax,
		[NaxpMessage.NAXP1012_ReproducedSplit] = Syntax,
		[NaxpMessage.NAXP1013_DroppedSplit] = Syntax,
		[NaxpMessage.NAXP1014_RenderingMissing] = Syntax,
		[NaxpMessage.NAXP1015_HashSplitFromBracket] = Syntax,
		[NaxpMessage.NAXP1016_HashWithoutBracket] = Syntax,
		[NaxpMessage.NAXP1017_DecimalRangeBoundsSeparator] = Syntax,
		[NaxpMessage.NAXP1018_DecimalRangeNotClosed] = Syntax,
		[NaxpMessage.NAXP1019_DecimalRangeBoundNotDigits] = Syntax,
		[NaxpMessage.NAXP1020_DecimalRangeBoundSplit] = Syntax,
		[NaxpMessage.NAXP1021_LowerBoundWiderThanUpper] = "W4",
		[NaxpMessage.NAXP1022_UpperBoundLeadingZeros] = "W4",
		[NaxpMessage.NAXP1023_LowerBoundExceedsUpper] = "W4",
		[NaxpMessage.NAXP1024_DecimalRangeBoundTooLong] = "W4",
		[NaxpMessage.NAXP1025_CharacterSetNotClosed] = Syntax,
		[NaxpMessage.NAXP1026_RangeUpperBoundIsBlockEscape] = Syntax,
		[NaxpMessage.NAXP1027_RangeReversed] = "W4",
		[NaxpMessage.NAXP1028_CharacterSetEmpty] = Syntax,
		[NaxpMessage.NAXP1029_BackslashBeforeWhitespace] = Syntax,
		[NaxpMessage.NAXP1030_BackslashWithoutEscape] = Syntax,
		[NaxpMessage.NAXP1031_EscapeUndefined] = Syntax,
		[NaxpMessage.NAXP1032_CharacterNotAllowed] = Syntax,
		[NaxpMessage.NAXP1033_ElementRequired] = Syntax,
		[NaxpMessage.NAXP1034_AlternativeEmpty] = Syntax,
		[NaxpMessage.NAXP1035_UnifiedWithoutElement] = Syntax,
		[NaxpMessage.NAXP1036_NaxpIncomplete] = Syntax,
		[NaxpMessage.NAXP1037_ReservedCharacterHere] = Syntax,
		[NaxpMessage.NAXP1038_CharacterHere] = Syntax,
		[NaxpMessage.NAXP1039_UnifiedNested] = "W2",
		[NaxpMessage.NAXP1040_ReproducedSubjectNotSingle] = "W1",
		[NaxpMessage.NAXP1041_RenderingNotSingle] = "W1",
		[NaxpMessage.NAXP1042_ElementNotDeletable] = "W1",
		[NaxpMessage.NAXP1043_RenderingNotGenerated] = "W1",
		[NaxpMessage.NAXP1044_UnificationNotSingleValued] = "W3",
		[NaxpMessage.NAXP1045_UnificationNotSingleValuedWitness] = "W3",
		[NaxpMessage.NAXP1046_TooManyValues] = "W5",
		[NaxpMessage.NAXP1047_ElementTooLong] = StateBudget,
		[NaxpMessage.NAXP1048_TooManyStates] = StateBudget,
		[NaxpMessage.NAXP1049_TooManyCanonicalStates] = StateBudget,
		[NaxpMessage.NAXP1050_TooManyPairStates] = StateBudget,
		[NaxpMessage.NAXP1051_PairOutputAbandoned] = StateBudget,
		[NaxpMessage.NAXP1052_FoldInCharacterSet] = Syntax,
		[NaxpMessage.NAXP1053_DecimalRangeMarkSplit] = Syntax,
		[NaxpMessage.NAXP1054_DecimalRangeMarkOnNonZero] = "W4",
		[NaxpMessage.NAXP1055_DecimalRangeMarkNotPadding] = "W4",
		[NaxpMessage.NAXP1056_DecimalRangeMarkOnUpperBound] = "W4",
		[NaxpMessage.NAXP1057_GroupNotOpened] = Syntax,
		[NaxpMessage.NAXP1058_FoldBeginsRendering] = Syntax,
		[NaxpMessage.NAXP1059_RepetitionUnbounded] = Syntax,
		[NaxpMessage.NAXP1060_AnyCharacter] = Syntax,
		[NaxpMessage.NAXP1061_Anchor] = Syntax,
		[NaxpMessage.NAXP1062_IntervalCountZero] = "W4",
	};

	/// <summary>The rule a message belongs to, spelled as the conformance data spells it.</summary>
	public static string RuleOf(NaxpMessage message)
		=> Rules.TryGetValue(message, out string? rule)
			? rule
			: throw new InvalidOperationException($"{message} has no rule in this table.")
			;

	/// <summary>
	/// Whether a message is the state budget, W6, rather than any other rule.
	/// </summary>
	/// <remarks>
	/// Worth singling out because a naxp that breaks W6 is one this implementation stopped
	/// working on, so a test comparing two ways of deciding something has nothing to compare.
	/// </remarks>
	public static bool IsStateBudget(NaxpMessage message)
		=> string.Equals(RuleOf(message), StateBudget, StringComparison.Ordinal);

	/// <summary>
	/// The rule a bare code belongs to, which is what a caller of the public surface has.
	/// </summary>
	/// <remarks>
	/// The code is the member's name with the hint cut off, so this puts it back on. Nothing in
	/// the library does this: a caller is given a code to log, not a thing to look up.
	/// </remarks>
	/// <param name="code">The code, such as <c>NAXP1002</c>.</param>
	/// <returns>The rule.</returns>
	public static string RuleOf(string code)
	{
		foreach (KeyValuePair<NaxpMessage, string> pair in Rules)
		{
			if (pair.Key.ToString().StartsWith(code + "_", StringComparison.Ordinal))
			{
				return pair.Value;
			}
		}

		throw new InvalidOperationException($"{code} names no message in this table.");
	}

	/// <summary>Every message, so that a test can check none is missing from the table.</summary>
	public static IReadOnlyCollection<NaxpMessage> Mapped => Rules.Keys;
}
