// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Globalization;

namespace LogMu;

/// <summary>
/// Every refusal this implementation can produce, one member per message.
/// </summary>
/// <remarks>
/// <para>
/// The name is the code a caller sees, so <c>NAXP1001_QuantifierRepeated</c> reaches a log or a
/// bug report without anybody quoting the prose. The number makes it greppable and the words make
/// it readable, which is why both are in the name rather than in a lookup somewhere else.
/// </para>
/// <para>
/// Nothing inside the library handles message text. A refusal names a member of this enum and, at
/// most, supplies one string for it to interpolate; <see cref="NaxpMessages"/> is the only place
/// that knows what any of them say. Two messages that used to be chosen by a ternary at the point
/// of refusal are therefore two members here.
/// </para>
/// <para>
/// The comment above each group names the rule of the specification it belongs to. That is the
/// only place the rules survive: they organise the language's requirements, they are not
/// something a user of this package needs, and no code branches on them.
/// </para>
/// </remarks>
enum NaxpMessage
{
	// Syntax: quantifiers and intervals
	NAXP1001_QuantifierRepeated,
	NAXP1002_IntervalHyphen,
	NAXP1003_IntervalUnbounded,
	NAXP1004_IntervalNotClosed,
	NAXP1005_IntervalCountNotDigits,
	NAXP1006_IntervalCountSplit,

	// W4: interval counts
	NAXP1007_IntervalCountsOutOfOrder,
	NAXP1008_IntervalCountTooLong,

	// Syntax: groups
	NAXP1009_GroupNotClosed,

	// Syntax: replaceable elements
	NAXP1010_ReproducedAfterOptional,
	NAXP1011_DroppedAfterOptional,
	NAXP1012_ReproducedSplit,
	NAXP1013_DroppedSplit,
	NAXP1014_ReplacementMissing,

	// Syntax: digits ranges
	NAXP1015_HashSplitFromBracket,
	NAXP1016_HashWithoutBracket,
	NAXP1017_DigitsRangeBoundsSeparator,
	NAXP1018_DigitsRangeNotClosed,
	NAXP1019_DigitsRangeBoundNotDigits,
	NAXP1020_DigitsRangeBoundSplit,

	// W4: digits range bounds
	NAXP1021_LowerBoundWiderThanUpper,
	NAXP1022_UpperBoundLeadingZeros,
	NAXP1023_LowerBoundExceedsUpper,
	NAXP1024_DigitsRangeBoundTooLong,

	// Syntax: character sets
	NAXP1025_CharacterSetNotClosed,
	NAXP1026_RangeUpperBoundIsBlockEscape,
	NAXP1027_RangeReversed,
	NAXP1028_CharacterSetEmpty,

	// Syntax: escapes
	NAXP1029_BackslashBeforeWhitespace,
	NAXP1030_BackslashWithoutEscape,
	NAXP1031_HexEscapeRemoved,
	NAXP1032_EscapeUndefined,

	// Syntax: the source itself
	NAXP1033_CharacterNotAllowed,

	// Syntax: structure
	NAXP1034_ElementRequired,
	NAXP1035_AlternativeEmpty,
	NAXP1036_ReplaceableWithoutElement,
	NAXP1037_NaxpIncomplete,
	NAXP1038_ReservedCharacterHere,
	NAXP1039_CharacterHere,

	// W2: nesting
	NAXP1040_ReplaceableNested,

	// W1: renderings
	NAXP1041_ReproducedSubjectNotSingle,
	NAXP1042_RenderingNotSingle,
	NAXP1043_ElementNotDeletable,
	NAXP1044_RenderingNotGenerated,

	// W3: single valued replacement
	NAXP1045_ReplacementNotSingleValued,
	NAXP1046_ReplacementNotSingleValuedWitness,

	// W5: the size of the encoding
	NAXP1047_TooManyValues,

	// Not rules of the language: budgets this implementation imposes
	NAXP1048_ElementTooLong,
	NAXP1049_TooManyStates,
	NAXP1050_TooManyCanonicalStates,
	NAXP1051_TooManyPairStates,
	NAXP1052_PairOutputAbandoned,
}

/// <summary>
/// What each <see cref="NaxpMessage"/> says.
/// </summary>
/// <remarks>
/// <para>
/// One entry per member, in the same order, which <c>NaxpMessageTests</c> checks. A member with
/// no <c>{0}</c> is returned as it stands rather than passed through <see cref="string.Format(
/// IFormatProvider, string, object?)"/>, so the braces in messages such as <c>'A{2,5}'</c> need
/// no escaping. A message that grows an argument must have its braces doubled at the same time.
/// </para>
/// <para>
/// The budgets below are read from <see cref="NaxpLimits"/> rather than passed in by whoever
/// refused. Only the tests ever build with a smaller one, and a message is not the place to
/// describe a test.
/// </para>
/// </remarks>
static class NaxpMessages
{
	static readonly string[] Formats = BuildFormats();

	/// <summary>
	/// What a message says, with its argument interpolated where it has one.
	/// </summary>
	/// <param name="message">The message.</param>
	/// <param name="argument">Its argument, or <see langword="null"/> where it takes none.</param>
	/// <returns>The text.</returns>
	public static string Format(NaxpMessage message, string? argument)
	{
		string format = Formats[(int)message];

		return argument is null
			? format
			: string.Format(CultureInfo.InvariantCulture, format, argument)
			;
	}

	/// <summary>How many messages the table holds, so that a test can compare it with the enum.</summary>
	internal static int Count => Formats.Length;

	static string[] BuildFormats()
	{
		string maxStates = NaxpLimits.MaxStates.ToString(CultureInfo.InvariantCulture);
		string maxCanonicalStates = NaxpLimits.MaxCanonicalStates.ToString(CultureInfo.InvariantCulture);
		string maxGeneratedLength = Matcher.MaxGeneratedLength.ToString(CultureInfo.InvariantCulture);

		return
		[
			// NAXP1001_QuantifierRepeated
			"A base may take only one quantifier. To repeat something already quantified, group it first, as in '(A?){2}'.",
			// NAXP1002_IntervalHyphen
			"The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'.",
			// NAXP1003_IntervalUnbounded
			"There is no unbounded interval, because a naxp must have a finite count of values. Write both counts, as in 'A{2,5}'.",
			// NAXP1004_IntervalNotClosed
			"This interval is not closed. Add a '}'.",
			// NAXP1005_IntervalCountNotDigits
			"An interval count must be a run of one to two digits.",
			// NAXP1006_IntervalCountSplit
			"The digits of an interval count cannot be separated by whitespace.",
			// NAXP1007_IntervalCountsOutOfOrder
			"The first count of an interval cannot exceed the second.",
			// NAXP1008_IntervalCountTooLong
			"An interval count may have at most two digits. The cap bounds the expansion an implementation must carry out before it can judge a naxp on any other ground.",
			// NAXP1009_GroupNotClosed
			"This group is not closed. Add a ')'.",
			// NAXP1010_ReproducedAfterOptional
			"'!!' carries its own '?', so it cannot follow one. Write 'x!(x)' instead.",
			// NAXP1011_DroppedAfterOptional
			"'!?' carries its own '?', so it cannot follow one. Write 'x!()' instead.",
			// NAXP1012_ReproducedSplit
			"'!!' is one token, so whitespace may not split it.",
			// NAXP1013_DroppedSplit
			"'!?' is one token, so whitespace may not split it.",
			// NAXP1014_ReplacementMissing
			"A '!' must be followed by its replacement. Write 'x!y', 'x!!' or 'x!?'; there is no bare 'x!'.",
			// NAXP1015_HashSplitFromBracket
			"There should be no whitespace between '#' and '[' in a digits range.",
			// NAXP1016_HashWithoutBracket
			"A '#' introduces a digits range and must be followed by '['. To match a hash write '\\#'.",
			// NAXP1017_DigitsRangeBoundsSeparator
			"The bounds of a digits range are separated by '-'. Write '#[0-105]'.",
			// NAXP1018_DigitsRangeNotClosed
			"This digits range is not closed. Add a ']'.",
			// NAXP1019_DigitsRangeBoundNotDigits
			"A digits range bound must be a run of one to fifteen digits.",
			// NAXP1020_DigitsRangeBoundSplit
			"The digits of a digits range bound cannot be separated by whitespace.",
			// NAXP1021_LowerBoundWiderThanUpper
			"The lower bound of a digits range may not have more digits than the upper bound.",
			// NAXP1022_UpperBoundLeadingZeros
			"Where the upper bound of a digits range has more digits than the lower, it may not have leading zeros.",
			// NAXP1023_LowerBoundExceedsUpper
			"The lower bound of a digits range may not exceed the upper bound.",
			// NAXP1024_DigitsRangeBoundTooLong
			"A digits range bound may have at most fifteen digits, which is what a 53 bit mantissa holds exactly.",
			// NAXP1025_CharacterSetNotClosed
			"This character set is not closed. Add a ']'.",
			// NAXP1026_RangeUpperBoundIsBlockEscape
			"A range in a character set runs between two single characters, so its upper bound cannot be a block escape.",
			// NAXP1027_RangeReversed
			"A range in a character set must be written lowest first. Write '{0}'.",
			// NAXP1028_CharacterSetEmpty
			"A character set must contain at least one character, so '[]' is not legal.",
			// NAXP1029_BackslashBeforeWhitespace
			"A '\\' cannot be followed by whitespace. To match a space write '\\s'.",
			// NAXP1030_BackslashWithoutEscape
			"A '\\' must be followed by an escape letter or a reserved character.",
			// NAXP1031_HexEscapeRemoved
			"'\\x' is not an escape. The hex escape was removed in version 0.3, so '\\x41' is no longer a naxp; write the character itself.",
			// NAXP1032_EscapeUndefined
			"'\\{0}' is not an escape. A backslash may be followed by one of the letters 's', '9', 'A', 'a' and 'X', or by a reserved character.",
			// NAXP1033_CharacterNotAllowed
			"{0} cannot appear in the source of a naxp, which may hold whitespace and the printable ASCII characters U+0021 to U+007E.",
			// NAXP1034_ElementRequired
			"An element is required here, but the naxp ends.",
			// NAXP1035_AlternativeEmpty
			"An alternative must contain at least one element. To admit the empty string write '()'.",
			// NAXP1036_ReplaceableWithoutElement
			"A '!' must follow the element it makes replaceable. To match an exclamation mark write '\\!'.",
			// NAXP1037_NaxpIncomplete
			"The naxp ends before it is complete.",
			// NAXP1038_ReservedCharacterHere
			"'{0}' is reserved and cannot appear here. To match it write '\\{0}'.",
			// NAXP1039_CharacterHere
			"{0} cannot appear here.",
			// NAXP1040_ReplaceableNested
			"A '!' may not nest, so neither the subject nor the rendering may contain another '!'.",
			// NAXP1041_ReproducedSubjectNotSingle
			"The subject of a '!!' must generate exactly one string, since '!!' reproduces it.",
			// NAXP1042_RenderingNotSingle
			"The rendering of a '!' must generate exactly one string, or there would be no basis on which to choose between them.",
			// NAXP1043_ElementNotDeletable
			"This element cannot be deleted, because its subject does not generate the empty string. Make the subject optional.",
			// NAXP1044_RenderingNotGenerated
			"The rendering '{0}' is not one of the strings its subject generates, so reconstituted text would not encode again.",
			// NAXP1045_ReplacementNotSingleValued
			"Replacement must be single valued, but this naxp gives one string more than one canonical form, so it would have more than one value.",
			// NAXP1046_ReplacementNotSingleValuedWitness
			"Replacement must be single valued, but '{0}' has more than one canonical form under this naxp, so it would have more than one value.",
			// NAXP1047_TooManyValues
			"This naxp has more than 18 446 744 073 709 551 615 encodable values, which is the largest value the encoding can produce.",
			// NAXP1048_ElementTooLong
			$"This element generates a string longer than {maxGeneratedLength} characters, which this implementation declines to build. The naxp is legal.",
			// NAXP1049_TooManyStates
			$"This naxp needs more than {maxStates} states, which this implementation declines to build. The naxp may well be legal.",
			// NAXP1050_TooManyCanonicalStates
			$"This naxp needs more than {maxCanonicalStates} states to canonicalise, which is more than this implementation will build.",
			// NAXP1051_TooManyPairStates
			$"Deciding whether replacement is single valued for this naxp needs more than {maxStates} pair states, which this implementation declines to explore. The naxp may well be legal.",
			// NAXP1052_PairOutputAbandoned
			"Deciding whether replacement is single valued for this naxp needs more intermediate output than this implementation will build. The naxp may well be legal.",
		];
	}
}
