// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { NaxpLimits } from './naxp-limits.js';

/**
 * Every fault this implementation can produce, one member per message.
 *
 * The name is the code a caller sees, so `NAXP1001_QuantifierRepeated` reaches a log or a bug
 * report without anybody quoting the prose. The number makes it greppable and the words make it
 * readable, which is why both are in the name rather than in a lookup somewhere else.
 *
 * Nothing inside the library handles message text. A fault names a member of this object and,
 * at most, supplies one string for it to interpolate; {@link NAXP_MESSAGE_TEXT} is the only place
 * that knows what any of them say.
 *
 * The comment above each group names the rule of the specification it belongs to. That is the
 * only place the rules survive: they organise the language's requirements, they are not
 * something a user of this package needs, and no code branches on them.
 *
 * This file and its C# twin are generated from one pattern, so the two implementations report a fault
 * with the same code and the same words.
 *
 * @enum {string}
 */
export const NaxpMessage = Object.freeze({
	// Syntax: quantifiers and intervals
	NAXP1001_QuantifierRepeated: 'NAXP1001_QuantifierRepeated',
	NAXP1002_IntervalHyphen: 'NAXP1002_IntervalHyphen',
	NAXP1003_IntervalUnbounded: 'NAXP1003_IntervalUnbounded',
	NAXP1004_IntervalNotClosed: 'NAXP1004_IntervalNotClosed',
	NAXP1005_IntervalCountNotDigits: 'NAXP1005_IntervalCountNotDigits',
	NAXP1006_IntervalCountSplit: 'NAXP1006_IntervalCountSplit',
	// W4: interval counts
	NAXP1007_IntervalCountsOutOfOrder: 'NAXP1007_IntervalCountsOutOfOrder',
	NAXP1008_IntervalCountTooLong: 'NAXP1008_IntervalCountTooLong',
	// Syntax: groups
	NAXP1009_GroupNotClosed: 'NAXP1009_GroupNotClosed',
	// Syntax: unified elements
	NAXP1010_ReproducedAfterOptional: 'NAXP1010_ReproducedAfterOptional',
	NAXP1011_DroppedAfterOptional: 'NAXP1011_DroppedAfterOptional',
	NAXP1012_ReproducedSplit: 'NAXP1012_ReproducedSplit',
	NAXP1013_DroppedSplit: 'NAXP1013_DroppedSplit',
	NAXP1014_RenderingMissing: 'NAXP1014_RenderingMissing',
	// Syntax: decimal ranges
	NAXP1015_HashSplitFromBracket: 'NAXP1015_HashSplitFromBracket',
	NAXP1016_HashWithoutBracket: 'NAXP1016_HashWithoutBracket',
	NAXP1017_DecimalRangeBoundsSeparator: 'NAXP1017_DecimalRangeBoundsSeparator',
	NAXP1018_DecimalRangeNotClosed: 'NAXP1018_DecimalRangeNotClosed',
	NAXP1019_DecimalRangeBoundNotDigits: 'NAXP1019_DecimalRangeBoundNotDigits',
	NAXP1020_DecimalRangeBoundSplit: 'NAXP1020_DecimalRangeBoundSplit',
	// W4: decimal range bounds
	NAXP1021_LowerBoundWiderThanUpper: 'NAXP1021_LowerBoundWiderThanUpper',
	NAXP1022_UpperBoundLeadingZeros: 'NAXP1022_UpperBoundLeadingZeros',
	NAXP1023_LowerBoundExceedsUpper: 'NAXP1023_LowerBoundExceedsUpper',
	NAXP1024_DecimalRangeBoundTooLong: 'NAXP1024_DecimalRangeBoundTooLong',
	// Syntax: character sets
	NAXP1025_CharacterSetNotClosed: 'NAXP1025_CharacterSetNotClosed',
	NAXP1026_RangeUpperBoundIsBlockEscape: 'NAXP1026_RangeUpperBoundIsBlockEscape',
	NAXP1027_RangeReversed: 'NAXP1027_RangeReversed',
	NAXP1028_CharacterSetEmpty: 'NAXP1028_CharacterSetEmpty',
	// Syntax: escapes
	NAXP1029_BackslashBeforeWhitespace: 'NAXP1029_BackslashBeforeWhitespace',
	NAXP1030_BackslashWithoutEscape: 'NAXP1030_BackslashWithoutEscape',
	NAXP1031_EscapeUndefined: 'NAXP1031_EscapeUndefined',
	// Syntax: the pattern itself
	NAXP1032_CharacterNotAllowed: 'NAXP1032_CharacterNotAllowed',
	// Syntax: structure
	NAXP1033_ElementRequired: 'NAXP1033_ElementRequired',
	NAXP1034_AlternativeEmpty: 'NAXP1034_AlternativeEmpty',
	NAXP1035_UnifiedWithoutElement: 'NAXP1035_UnifiedWithoutElement',
	NAXP1036_NaxpIncomplete: 'NAXP1036_NaxpIncomplete',
	NAXP1037_ReservedCharacterHere: 'NAXP1037_ReservedCharacterHere',
	NAXP1038_CharacterHere: 'NAXP1038_CharacterHere',
	// W2: nesting
	NAXP1039_UnifiedNested: 'NAXP1039_UnifiedNested',
	// W1: renderings
	NAXP1040_ReproducedSubjectNotSingle: 'NAXP1040_ReproducedSubjectNotSingle',
	NAXP1041_RenderingNotSingle: 'NAXP1041_RenderingNotSingle',
	NAXP1042_ElementNotDeletable: 'NAXP1042_ElementNotDeletable',
	NAXP1043_RenderingNotGenerated: 'NAXP1043_RenderingNotGenerated',
	// W3: single valued unification
	NAXP1044_UnificationNotSingleValued: 'NAXP1044_UnificationNotSingleValued',
	NAXP1045_UnificationNotSingleValuedWitness: 'NAXP1045_UnificationNotSingleValuedWitness',
	// W5: the size of the encoding
	NAXP1046_TooManyValues: 'NAXP1046_TooManyValues',
	// Not rules of the language: budgets this implementation imposes
	NAXP1047_ElementTooLong: 'NAXP1047_ElementTooLong',
	NAXP1048_TooManyStates: 'NAXP1048_TooManyStates',
	NAXP1049_TooManyCanonicalStates: 'NAXP1049_TooManyCanonicalStates',
	NAXP1050_TooManyPairStates: 'NAXP1050_TooManyPairStates',
	NAXP1051_PairOutputAbandoned: 'NAXP1051_PairOutputAbandoned',

	// Syntax: case folds
	NAXP1052_FoldInCharacterSet: 'NAXP1052_FoldInCharacterSet',

	// Syntax: decimal range padding marks
	NAXP1053_DecimalRangeMarkSplit: 'NAXP1053_DecimalRangeMarkSplit',

	// W4: decimal range padding marks
	NAXP1054_DecimalRangeMarkOnNonZero: 'NAXP1054_DecimalRangeMarkOnNonZero',
	NAXP1055_DecimalRangeMarkNotPadding: 'NAXP1055_DecimalRangeMarkNotPadding',
	NAXP1056_DecimalRangeMarkOnUpperBound: 'NAXP1056_DecimalRangeMarkOnUpperBound',

	// Syntax: a closing parenthesis with no group to close
	NAXP1057_GroupNotOpened: 'NAXP1057_GroupNotOpened',

	// Syntax: a case fold where a rendering should begin
	NAXP1058_FoldBeginsRendering: 'NAXP1058_FoldBeginsRendering',

	// Syntax: the regex metacharacters naxp reserves so that a regex habit gets a message
	NAXP1059_RepetitionUnbounded: 'NAXP1059_RepetitionUnbounded',
	NAXP1060_AnyCharacter: 'NAXP1060_AnyCharacter',
	NAXP1061_Anchor: 'NAXP1061_Anchor',

	// W4: a fixed count of zero
	NAXP1062_IntervalCountZero: 'NAXP1062_IntervalCountZero',
});

/**
 * What each {@link NaxpMessage} says.
 *
 * A member with no `{0}` is returned as it stands, so the braces in messages such as
 * `'A{2-5}'` need no escaping. A message that grows an argument must have its braces doubled at
 * the same time, which `naxp-message.test.js` checks.
 *
 * The budgets are read from {@link NaxpLimits} rather than passed in by whoever invalid. Only
 * the tests ever build with a smaller one, and a message is not the place to describe a test.
 */
const NAXP_MESSAGE_TEXT = Object.freeze({
	NAXP1001_QuantifierRepeated:
		'An atom may take only one quantifier. To repeat something already quantified, group it first, as in \'(A?){2}\'.',
	NAXP1002_IntervalHyphen:
		'The counts of an interval are separated by \',\', not by a hyphen. Write \'A{2,5}\'.',
	NAXP1003_IntervalUnbounded:
		'There is no unbounded interval, because a naxp must have a finite count of values. Write both counts, as in \'A{2,5}\'.',
	NAXP1004_IntervalNotClosed:
		'This interval is not closed. Add a \'}\'.',
	NAXP1005_IntervalCountNotDigits:
		'An interval count must be a run of one to two digits.',
	NAXP1006_IntervalCountSplit:
		'The digits of an interval count cannot be separated by whitespace.',
	NAXP1007_IntervalCountsOutOfOrder:
		'The first count of an interval cannot exceed the second.',
	NAXP1008_IntervalCountTooLong:
		'An interval count may have at most two digits. The cap bounds the expansion an implementation must carry out before it can judge a naxp on any other ground.',
	NAXP1009_GroupNotClosed:
		'This group is not closed. Add a \')\'.',
	NAXP1010_ReproducedAfterOptional:
		'\'!!\' carries its own \'?\', so it cannot follow one. Write \'x!(x)\' instead.',
	NAXP1011_DroppedAfterOptional:
		'\'!?\' carries its own \'?\', so it cannot follow one. Write \'x!()\' instead.',
	NAXP1012_ReproducedSplit:
		'\'!!\' is one token, so whitespace may not split it.',
	NAXP1013_DroppedSplit:
		'\'!?\' is one token, so whitespace may not split it.',
	NAXP1014_RenderingMissing:
		'A \'!\' must be followed by its rendering. Write \'x!y\', \'x!!\' or \'x!?\'; there is no bare \'x!\'.',
	NAXP1015_HashSplitFromBracket:
		'There should be no whitespace between \'#\' and \'[\' in a decimal range.',
	NAXP1016_HashWithoutBracket:
		'A \'#\' introduces a decimal range and must be followed by \'[\'. To match a hash write \'\\#\'.',
	NAXP1017_DecimalRangeBoundsSeparator:
		'The bounds of a decimal range are separated by \'-\'. Write \'#[0-105]\'.',
	NAXP1018_DecimalRangeNotClosed:
		'This decimal range is not closed. Add a \']\'.',
	NAXP1019_DecimalRangeBoundNotDigits:
		'A decimal range bound must be a run of one to fifteen digits.',
	NAXP1020_DecimalRangeBoundSplit:
		'The digits of a decimal range bound cannot be separated by whitespace.',
	NAXP1021_LowerBoundWiderThanUpper:
		'The lower bound of a decimal range may not have more digits than the upper bound.',
	NAXP1022_UpperBoundLeadingZeros:
		'Where the upper bound of a decimal range has more digits than the lower, it may not have leading zeros.',
	NAXP1023_LowerBoundExceedsUpper:
		'The lower bound of a decimal range may not exceed the upper bound.',
	NAXP1024_DecimalRangeBoundTooLong:
		'A decimal range bound may have at most fifteen digits, which is what a 53 bit mantissa holds exactly.',
	NAXP1025_CharacterSetNotClosed:
		'This character set is not closed. Add a \']\'.',
	NAXP1026_RangeUpperBoundIsBlockEscape:
		'A range in a character set runs between two single characters, so its upper bound cannot be a block escape.',
	NAXP1027_RangeReversed:
		'A range in a character set must be written lowest first. Write \'{0}\'.',
	NAXP1028_CharacterSetEmpty:
		'A character set must contain at least one character, so \'[]\' is not legal.',
	NAXP1029_BackslashBeforeWhitespace:
		'A \'\\\' cannot be followed by whitespace. To match a space write \'\\s\'.',
	NAXP1030_BackslashWithoutEscape:
		'A \'\\\' must be followed by an escape letter or a reserved character.',
	NAXP1031_EscapeUndefined:
		'\'\\{0}\' is not an escape. A backslash may be followed by one of the letters \'s\', \'9\', \'A\', \'a\', \'X\', \'x\', \'C\' and \'c\', or by a reserved character.',
	NAXP1032_CharacterNotAllowed:
		'{0} cannot appear in the pattern of a naxp, which may hold whitespace and the printable ASCII characters U+0021 to U+007E.',
	NAXP1033_ElementRequired:
		'An element is required here, but the naxp ends.',
	NAXP1034_AlternativeEmpty:
		'An alternative must contain at least one element. To admit the empty string write \'()\'.',
	NAXP1035_UnifiedWithoutElement:
		'A \'!\' must follow its left operand, the subject it unifies. To match an exclamation mark write \'\\!\'.',
	NAXP1036_NaxpIncomplete:
		'The naxp ends before it is complete.',
	NAXP1037_ReservedCharacterHere:
		'\'{0}\' is reserved and cannot appear here. To match it write \'\\{0}\'.',
	NAXP1038_CharacterHere:
		'{0} cannot appear here.',
	NAXP1039_UnifiedNested:
		'A \'!\' may not nest, so neither the subject nor the rendering may contain another \'!\'.',
	NAXP1040_ReproducedSubjectNotSingle:
		'The subject of a \'!!\' must generate exactly one string, since \'!!\' reproduces it.',
	NAXP1041_RenderingNotSingle:
		'The rendering of a \'!\' must generate exactly one string, or there would be no basis on which to choose between them.',
	NAXP1042_ElementNotDeletable:
		'This element cannot be deleted, because its subject does not generate the empty string. Make the subject optional.',
	NAXP1043_RenderingNotGenerated:
		'The rendering \'{0}\' is not one of the strings its subject generates, so reconstituted text would not encode again.',
	NAXP1044_UnificationNotSingleValued:
		'Text unification must be single valued, but this naxp gives one string more than one canonical form, so it would have more than one value.',
	NAXP1045_UnificationNotSingleValuedWitness:
		'Text unification must be single valued, but \'{0}\' has more than one canonical form under this naxp, so it would have more than one value.',
	NAXP1046_TooManyValues:
		'This naxp has more than 18 446 744 073 709 551 615 encoded values, which is more than W5 allows.',
	NAXP1047_ElementTooLong:
		`This element generates a string longer than ${NaxpLimits.maxStringLength} characters, which W6 does not allow.`,
	NAXP1048_TooManyStates:
		`This naxp needs more than ${NaxpLimits.maxStates} states, which W6 does not allow.`,
	NAXP1049_TooManyCanonicalStates:
		`This naxp needs more than ${NaxpLimits.maxCanonicalStates} states to canonicalise, which W6 does not allow.`,
	NAXP1050_TooManyPairStates:
		`Deciding whether unification is single valued for this naxp needs more than ${NaxpLimits.maxStates} pair states, which W6 does not allow.`,
	NAXP1051_PairOutputAbandoned:
		`Deciding whether unification is single valued for this naxp needs an intermediate string longer than ${NaxpLimits.maxStringLength} characters, which W6 does not allow.`,
	NAXP1052_FoldInCharacterSet:
		'\'\\{0}\' is a case fold and applies to a whole element, so it cannot appear inside a character set. Write it before the set instead.',
	NAXP1053_DecimalRangeMarkSplit:
		'The \'!\' or \'?\' must immediately follow the preceding zero without any separating whitespace.',
	NAXP1054_DecimalRangeMarkOnNonZero:
		'Only a zero may be marked with \'!\' or \'?\'.',
	NAXP1055_DecimalRangeMarkNotPadding:
		'Only zeros at the front of a number may be marked with \'!\' or \'?\'. The lower bound must always end with a digit without a \'!\' or a \'?\'.',
	NAXP1056_DecimalRangeMarkOnUpperBound:
		'Only the lower bound of a decimal range may be marked with \'!\' or \'?\'.',
	NAXP1057_GroupNotOpened:
		'There is no group for this \')\' to close. To match a parenthesis write \'\\)\'.',
	NAXP1058_FoldBeginsRendering:
		'A case fold cannot begin the rendering of a \'!\'. Write the rendering in the case you want, or case fold the whole text unification.',
	NAXP1059_RepetitionUnbounded:
		'\'{0}\' is reserved. naxp has no unbounded repetition, so write an interval such as \'{1,9}\'. To match the character write \'\\{0}\'.',
	NAXP1060_AnyCharacter:
		'\'.\' is reserved. Write the character set you mean, such as \'\\X\'. To match a full stop write \'\\.\'.',
	NAXP1061_Anchor:
		'\'{0}\' is reserved. A naxp matches the whole text, so there are no anchors. To match the character write \'\\{0}\'.',
	NAXP1062_IntervalCountZero:
		'A fixed count of zero matches only the empty string. Write \'()\' instead.',
});

/**
 * What a message says, with its argument interpolated where it has one.
 *
 * @param {string} message The message, a member of {@link NaxpMessage}.
 * @param {string | null} argument Its argument, or null where it takes none.
 * @returns {string} The text.
 */
export function formatNaxpMessage(message, argument) {
	const format = NAXP_MESSAGE_TEXT[message];

	if (format === undefined) { throw new Error(`There is no text for ${message}.`); }

	if (argument === null || argument === undefined) { return format; }

	// Split and join rather than replace, for two reasons. A message may use its argument more
	// than once - NAXP1037 names the character and then the escape that matches it - and
	// `replace` with a string would substitute only the first. And `replace` reads `$&` and its
	// like in the replacement, which a naxp is perfectly entitled to contain.
	return format.split('{0}').join(argument);
}

/** Every message, so that a test can walk them. */
export const ALL_NAXP_MESSAGES = Object.freeze(Object.keys(NAXP_MESSAGE_TEXT));
