// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { NaxpMessage } from '../lib/naxp-message.js';

const SYNTAX = 'syntax';
const STATE_BUDGET = 'W6';

/**
 * Which rule of the specification each `NaxpMessage` belongs to.
 *
 * The library does not carry this. A rule is a way of organising the language's requirements
 * rather than something a caller acts on, so in `naxp-message.js` it survives only as a comment
 * above each group, and no code there branches on it.
 *
 * The tests do need it, because the conformance data tags every invalid naxp with a rule and the point
 * of those cases is that the right naxp is invalid *for the right reason*. So the mapping lives
 * here, in the thing doing the verifying. If it drifts from the comments the conformance cases
 * fail, which is what should happen.
 *
 * The strings are the data's spelling, so `syntax` is lower case where the W rules are not. This is
 * the twin of `NaxpMessageRules.cs` and the two must say the same thing.
 */
const RULES = Object.freeze({
	[NaxpMessage.NAXP1001_QuantifierRepeated]: SYNTAX,
	[NaxpMessage.NAXP1002_IntervalHyphen]: SYNTAX,
	[NaxpMessage.NAXP1003_IntervalUnbounded]: SYNTAX,
	[NaxpMessage.NAXP1004_IntervalNotClosed]: SYNTAX,
	[NaxpMessage.NAXP1005_IntervalCountNotDigits]: SYNTAX,
	[NaxpMessage.NAXP1006_IntervalCountSplit]: SYNTAX,
	[NaxpMessage.NAXP1007_IntervalCountsOutOfOrder]: 'W4',
	[NaxpMessage.NAXP1008_IntervalCountTooLong]: 'W4',
	[NaxpMessage.NAXP1009_GroupNotClosed]: SYNTAX,
	[NaxpMessage.NAXP1010_ReproducedAfterOptional]: SYNTAX,
	[NaxpMessage.NAXP1011_DroppedAfterOptional]: SYNTAX,
	[NaxpMessage.NAXP1012_ReproducedSplit]: SYNTAX,
	[NaxpMessage.NAXP1013_DroppedSplit]: SYNTAX,
	[NaxpMessage.NAXP1014_RenderingMissing]: SYNTAX,
	[NaxpMessage.NAXP1015_HashSplitFromBracket]: SYNTAX,
	[NaxpMessage.NAXP1016_HashWithoutBracket]: SYNTAX,
	[NaxpMessage.NAXP1017_DecimalRangeBoundsSeparator]: SYNTAX,
	[NaxpMessage.NAXP1018_DecimalRangeNotClosed]: SYNTAX,
	[NaxpMessage.NAXP1019_DecimalRangeBoundNotDigits]: SYNTAX,
	[NaxpMessage.NAXP1020_DecimalRangeBoundSplit]: SYNTAX,
	[NaxpMessage.NAXP1021_LowerBoundWiderThanUpper]: 'W4',
	[NaxpMessage.NAXP1022_UpperBoundLeadingZeros]: 'W4',
	[NaxpMessage.NAXP1023_LowerBoundExceedsUpper]: 'W4',
	[NaxpMessage.NAXP1024_DecimalRangeBoundTooLong]: 'W4',
	[NaxpMessage.NAXP1025_CharacterSetNotClosed]: SYNTAX,
	[NaxpMessage.NAXP1026_RangeUpperBoundIsBlockEscape]: SYNTAX,
	[NaxpMessage.NAXP1027_RangeReversed]: 'W4',
	[NaxpMessage.NAXP1028_CharacterSetEmpty]: SYNTAX,
	[NaxpMessage.NAXP1029_BackslashBeforeWhitespace]: SYNTAX,
	[NaxpMessage.NAXP1030_BackslashWithoutEscape]: SYNTAX,
	[NaxpMessage.NAXP1031_EscapeUndefined]: SYNTAX,
	[NaxpMessage.NAXP1032_CharacterNotAllowed]: SYNTAX,
	[NaxpMessage.NAXP1033_ElementRequired]: SYNTAX,
	[NaxpMessage.NAXP1034_AlternativeEmpty]: SYNTAX,
	[NaxpMessage.NAXP1035_UnifiedWithoutElement]: SYNTAX,
	[NaxpMessage.NAXP1036_NaxpIncomplete]: SYNTAX,
	[NaxpMessage.NAXP1037_ReservedCharacterHere]: SYNTAX,
	[NaxpMessage.NAXP1038_CharacterHere]: SYNTAX,
	[NaxpMessage.NAXP1039_UnifiedNested]: 'W2',
	[NaxpMessage.NAXP1040_ReproducedSubjectNotSingle]: 'W1',
	[NaxpMessage.NAXP1041_RenderingNotSingle]: 'W1',
	[NaxpMessage.NAXP1042_ElementNotDeletable]: 'W1',
	[NaxpMessage.NAXP1043_RenderingNotGenerated]: 'W1',
	[NaxpMessage.NAXP1044_UnificationNotSingleValued]: 'W3',
	[NaxpMessage.NAXP1045_UnificationNotSingleValuedWitness]: 'W3',
	[NaxpMessage.NAXP1046_TooManyValues]: 'W5',
	[NaxpMessage.NAXP1047_ElementTooLong]: STATE_BUDGET,
	[NaxpMessage.NAXP1048_TooManyStates]: STATE_BUDGET,
	[NaxpMessage.NAXP1049_TooManyCanonicalStates]: STATE_BUDGET,
	[NaxpMessage.NAXP1050_TooManyPairStates]: STATE_BUDGET,
	[NaxpMessage.NAXP1051_PairOutputAbandoned]: STATE_BUDGET,
	[NaxpMessage.NAXP1052_FoldInCharacterSet]: SYNTAX,
	[NaxpMessage.NAXP1053_DecimalRangeMarkSplit]: SYNTAX,
	[NaxpMessage.NAXP1054_DecimalRangeMarkOnNonZero]: 'W4',
	[NaxpMessage.NAXP1055_DecimalRangeMarkNotPadding]: 'W4',
	[NaxpMessage.NAXP1056_DecimalRangeMarkOnUpperBound]: 'W4',
	[NaxpMessage.NAXP1057_GroupNotOpened]: SYNTAX,
	[NaxpMessage.NAXP1058_FoldBeginsRendering]: SYNTAX,
	[NaxpMessage.NAXP1059_RepetitionUnbounded]: SYNTAX,
	[NaxpMessage.NAXP1060_AnyCharacter]: SYNTAX,
	[NaxpMessage.NAXP1061_Anchor]: SYNTAX,
	[NaxpMessage.NAXP1062_IntervalCountZero]: 'W4',
});

/**
 * The rule a message belongs to, spelled as the conformance data spells it.
 *
 * @param {string} message The message, a member of `NaxpMessage`.
 * @returns {string} The rule.
 */
export function ruleOf(message) {
	const rule = RULES[message];

	if (rule === undefined) { throw new Error(`${message} has no rule in this table.`); }

	return rule;
}

/**
 * Whether a message is the state budget, W6, rather than any other rule.
 *
 * Worth singling out because a naxp that breaks W6 is one this implementation stopped working
 * on, so a test comparing two ways of deciding something has nothing to compare.
 *
 * @param {string} message The message.
 * @returns {boolean} Whether it is W6.
 */
export function isStateBudget(message) {
	return ruleOf(message) === STATE_BUDGET;
}

/**
 * The rule a bare code belongs to, which is what a caller of the public surface has.
 *
 * The code is the member's name with the hint cut off, so this puts it back on. Nothing in the
 * library does this: a caller is given a code to log, not a thing to look up.
 *
 * @param {string} code The code, such as `NAXP1002`.
 * @returns {string} The rule.
 */
export function ruleOfCode(code) {
	const message = Object.keys(RULES).find(name => name.startsWith(`${code}_`));

	if (message === undefined) { throw new Error(`${code} names no message in this table.`); }

	return RULES[message];
}

/** Every message this table maps, so that a test can check none is missing. */
export const MAPPED = Object.freeze(Object.keys(RULES));
