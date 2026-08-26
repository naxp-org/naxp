// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { NaxpLimits } from './naxp-limits.js';

/**
 * Every refusal this implementation can produce, one member per message.
 *
 * The name is the code a caller sees, so `NAXP1001_QuantifierRepeated` reaches a log or a bug
 * report without anybody quoting the prose. The number makes it greppable and the words make it
 * readable, which is why both are in the name rather than in a lookup somewhere else.
 *
 * Nothing inside the library handles message text. A refusal names a member of this object and,
 * at most, supplies one string for it to interpolate; {@link NAXP_MESSAGE_TEXT} is the only place
 * that knows what any of them say.
 *
 * The comment above each group names the rule of the specification it belongs to. That is the
 * only place the rules survive: they organise the language's requirements, they are not
 * something a user of this package needs, and no code branches on them.
 *
 * This file and its C# twin are generated from one source, so the two implementations refuse
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
	// Syntax: replaceable elements
	NAXP1010_ReproducedAfterOptional: 'NAXP1010_ReproducedAfterOptional',
	NAXP1011_DroppedAfterOptional: 'NAXP1011_DroppedAfterOptional',
	NAXP1012_ReproducedSplit: 'NAXP1012_ReproducedSplit',
	NAXP1013_DroppedSplit: 'NAXP1013_DroppedSplit',
	NAXP1014_ReplacementMissing: 'NAXP1014_ReplacementMissing',
	// Syntax: digits ranges
	NAXP1015_HashSplitFromBracket: 'NAXP1015_HashSplitFromBracket',
	NAXP1016_HashWithoutBracket: 'NAXP1016_HashWithoutBracket',
	NAXP1017_DigitsRangeBoundsSeparator: 'NAXP1017_DigitsRangeBoundsSeparator',
	NAXP1018_DigitsRangeNotClosed: 'NAXP1018_DigitsRangeNotClosed',
	NAXP1019_DigitsRangeBoundNotDigits: 'NAXP1019_DigitsRangeBoundNotDigits',
	NAXP1020_DigitsRangeBoundSplit: 'NAXP1020_DigitsRangeBoundSplit',
	// W4: digits range bounds
	NAXP1021_LowerBoundWiderThanUpper: 'NAXP1021_LowerBoundWiderThanUpper',
	NAXP1022_UpperBoundLeadingZeros: 'NAXP1022_UpperBoundLeadingZeros',
	NAXP1023_LowerBoundExceedsUpper: 'NAXP1023_LowerBoundExceedsUpper',
	NAXP1024_DigitsRangeBoundTooLong: 'NAXP1024_DigitsRangeBoundTooLong',
	// Syntax: character sets
	NAXP1025_CharacterSetNotClosed: 'NAXP1025_CharacterSetNotClosed',
	NAXP1026_RangeUpperBoundIsBlockEscape: 'NAXP1026_RangeUpperBoundIsBlockEscape',
	NAXP1027_RangeReversed: 'NAXP1027_RangeReversed',
	NAXP1028_CharacterSetEmpty: 'NAXP1028_CharacterSetEmpty',
	// Syntax: escapes
	NAXP1029_BackslashBeforeWhitespace: 'NAXP1029_BackslashBeforeWhitespace',
	NAXP1030_BackslashWithoutEscape: 'NAXP1030_BackslashWithoutEscape',
	NAXP1031_HexEscapeRemoved: 'NAXP1031_HexEscapeRemoved',
	NAXP1032_EscapeUndefined: 'NAXP1032_EscapeUndefined',
	// Syntax: the source itself
	NAXP1033_CharacterNotAllowed: 'NAXP1033_CharacterNotAllowed',
	// Syntax: structure
	NAXP1034_ElementRequired: 'NAXP1034_ElementRequired',
	NAXP1035_AlternativeEmpty: 'NAXP1035_AlternativeEmpty',
	NAXP1036_ReplaceableWithoutElement: 'NAXP1036_ReplaceableWithoutElement',
	NAXP1037_NaxpIncomplete: 'NAXP1037_NaxpIncomplete',
	NAXP1038_ReservedCharacterHere: 'NAXP1038_ReservedCharacterHere',
	NAXP1039_CharacterHere: 'NAXP1039_CharacterHere',
	// W2: nesting
	NAXP1040_ReplaceableNested: 'NAXP1040_ReplaceableNested',
	// W1: renderings
	NAXP1041_ReproducedSubjectNotSingle: 'NAXP1041_ReproducedSubjectNotSingle',
	NAXP1042_RenderingNotSingle: 'NAXP1042_RenderingNotSingle',
	NAXP1043_ElementNotDeletable: 'NAXP1043_ElementNotDeletable',
	NAXP1044_RenderingNotGenerated: 'NAXP1044_RenderingNotGenerated',
	// W3: single valued replacement
	NAXP1045_ReplacementNotSingleValued: 'NAXP1045_ReplacementNotSingleValued',
	NAXP1046_ReplacementNotSingleValuedWitness: 'NAXP1046_ReplacementNotSingleValuedWitness',
	// W5: the size of the encoding
	NAXP1047_TooManyValues: 'NAXP1047_TooManyValues',
	// Not rules of the language: budgets this implementation imposes
	NAXP1048_ElementTooLong: 'NAXP1048_ElementTooLong',
	NAXP1049_TooManyStates: 'NAXP1049_TooManyStates',
	NAXP1050_TooManyCanonicalStates: 'NAXP1050_TooManyCanonicalStates',
	NAXP1051_TooManyPairStates: 'NAXP1051_TooManyPairStates',
	NAXP1052_PairOutputAbandoned: 'NAXP1052_PairOutputAbandoned',
});

/**
 * What each {@link NaxpMessage} says.
 *
 * A member with no `{0}` is returned as it stands, so the braces in messages such as
 * `'A{2-5}'` need no escaping. A message that grows an argument must have its braces doubled at
 * the same time, which `naxp-message.test.js` checks.
 *
 * The budgets are read from {@link NaxpLimits} rather than passed in by whoever refused. Only
 * the tests ever build with a smaller one, and a message is not the place to describe a test.
 */
const NAXP_MESSAGE_TEXT = Object.freeze({
	NAXP1001_QuantifierRepeated:
		'A base may take only one quantifier. To repeat something already quantified, group it first, as in \'(A?){2}\'.',
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
	NAXP1014_ReplacementMissing:
		'A \'!\' must be followed by its replacement. Write \'x!y\', \'x!!\' or \'x!?\'; there is no bare \'x!\'.',
	NAXP1015_HashSplitFromBracket:
		'There should be no whitespace between \'#\' and \'[\' in a digits range.',
	NAXP1016_HashWithoutBracket:
		'A \'#\' introduces a digits range and must be followed by \'[\'. To match a hash write \'\\#\'.',
	NAXP1017_DigitsRangeBoundsSeparator:
		'The bounds of a digits range are separated by \'-\'. Write \'#[0-105]\'.',
	NAXP1018_DigitsRangeNotClosed:
		'This digits range is not closed. Add a \']\'.',
	NAXP1019_DigitsRangeBoundNotDigits:
		'A digits range bound must be a run of one to fifteen digits.',
	NAXP1020_DigitsRangeBoundSplit:
		'The digits of a digits range bound cannot be separated by whitespace.',
	NAXP1021_LowerBoundWiderThanUpper:
		'The lower bound of a digits range may not have more digits than the upper bound.',
	NAXP1022_UpperBoundLeadingZeros:
		'Where the upper bound of a digits range has more digits than the lower, it may not have leading zeros.',
	NAXP1023_LowerBoundExceedsUpper:
		'The lower bound of a digits range may not exceed the upper bound.',
	NAXP1024_DigitsRangeBoundTooLong:
		'A digits range bound may have at most fifteen digits, which is what a 53 bit mantissa holds exactly.',
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
	NAXP1031_HexEscapeRemoved:
		'\'\\x\' is not an escape. The hex escape was removed in version 0.3, so \'\\x41\' is no longer a naxp; write the character itself.',
	NAXP1032_EscapeUndefined:
		'\'\\{0}\' is not an escape. A backslash may be followed by one of the letters \'s\', \'9\', \'A\', \'a\' and \'X\', or by a reserved character.',
	NAXP1033_CharacterNotAllowed:
		'{0} cannot appear in the source of a naxp, which may hold whitespace and the printable ASCII characters U+0021 to U+007E.',
	NAXP1034_ElementRequired:
		'An element is required here, but the naxp ends.',
	NAXP1035_AlternativeEmpty:
		'An alternative must contain at least one element. To admit the empty string write \'()\'.',
	NAXP1036_ReplaceableWithoutElement:
		'A \'!\' must follow the element it makes replaceable. To match an exclamation mark write \'\\!\'.',
	NAXP1037_NaxpIncomplete:
		'The naxp ends before it is complete.',
	NAXP1038_ReservedCharacterHere:
		'\'{0}\' is reserved and cannot appear here. To match it write \'\\{0}\'.',
	NAXP1039_CharacterHere:
		'{0} cannot appear here.',
	NAXP1040_ReplaceableNested:
		'A \'!\' may not nest, so neither the subject nor the rendering may contain another \'!\'.',
	NAXP1041_ReproducedSubjectNotSingle:
		'The subject of a \'!!\' must generate exactly one string, since \'!!\' reproduces it.',
	NAXP1042_RenderingNotSingle:
		'The rendering of a \'!\' must generate exactly one string, or there would be no basis on which to choose between them.',
	NAXP1043_ElementNotDeletable:
		'This element cannot be deleted, because its subject does not generate the empty string. Make the subject optional.',
	NAXP1044_RenderingNotGenerated:
		'The rendering \'{0}\' is not one of the strings its subject generates, so reconstituted text would not encode again.',
	NAXP1045_ReplacementNotSingleValued:
		'Replacement must be single valued, but this naxp gives one string more than one canonical form, so it would have more than one value.',
	NAXP1046_ReplacementNotSingleValuedWitness:
		'Replacement must be single valued, but \'{0}\' has more than one canonical form under this naxp, so it would have more than one value.',
	NAXP1047_TooManyValues:
		'This naxp has more than 18 446 744 073 709 551 615 encodable values, which is the largest value the encoding can produce.',
	NAXP1048_ElementTooLong:
		`This element generates a string longer than ${NaxpLimits.maxStringLength} characters, which this implementation declines to build. The naxp is legal.`,
	NAXP1049_TooManyStates:
		`This naxp needs more than ${NaxpLimits.maxStates} states, which this implementation declines to build. The naxp may well be legal.`,
	NAXP1050_TooManyCanonicalStates:
		`This naxp needs more than ${NaxpLimits.maxCanonicalStates} states to canonicalise, which is more than this implementation will build.`,
	NAXP1051_TooManyPairStates:
		`Deciding whether replacement is single valued for this naxp needs more than ${NaxpLimits.maxStates} pair states, which this implementation declines to explore. The naxp may well be legal.`,
	NAXP1052_PairOutputAbandoned:
		'Deciding whether replacement is single valued for this naxp needs more intermediate output than this implementation will build. The naxp may well be legal.',
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
	// than once - NAXP1038 names the character and then the escape that matches it - and
	// `replace` with a string would substitute only the first. And `replace` reads `$&` and its
	// like in the replacement, which a naxp is perfectly entitled to contain.
	return format.split('{0}').join(argument);
}

/** Every message, so that a test can walk them. */
export const ALL_NAXP_MESSAGES = Object.freeze(Object.keys(NAXP_MESSAGE_TEXT));
