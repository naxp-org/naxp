// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { NaxpMessage } from '../lib/naxp-message.js';

const SYNTAX = 'syntax';
const LIMIT = 'ImplementationLimit';

/**
 * Which rule of the specification each `NaxpMessage` belongs to.
 *
 * The library does not carry this. A rule is a way of organising the language's requirements
 * rather than something a caller acts on, so in `naxp-message.js` it survives only as a comment
 * above each group, and no code there branches on it.
 *
 * The tests do need it, because the conformance data tags every rejection with a rule and the point
 * of those cases is that the right naxp is refused *for the right reason*. So the mapping lives
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
	[NaxpMessage.NAXP1014_ReplacementMissing]: SYNTAX,
	[NaxpMessage.NAXP1015_HashSplitFromBracket]: SYNTAX,
	[NaxpMessage.NAXP1016_HashWithoutBracket]: SYNTAX,
	[NaxpMessage.NAXP1017_DigitsRangeBoundsSeparator]: SYNTAX,
	[NaxpMessage.NAXP1018_DigitsRangeNotClosed]: SYNTAX,
	[NaxpMessage.NAXP1019_DigitsRangeBoundNotDigits]: SYNTAX,
	[NaxpMessage.NAXP1020_DigitsRangeBoundSplit]: SYNTAX,
	[NaxpMessage.NAXP1021_LowerBoundWiderThanUpper]: 'W4',
	[NaxpMessage.NAXP1022_UpperBoundLeadingZeros]: 'W4',
	[NaxpMessage.NAXP1023_LowerBoundExceedsUpper]: 'W4',
	[NaxpMessage.NAXP1024_DigitsRangeBoundTooLong]: 'W4',
	[NaxpMessage.NAXP1025_CharacterSetNotClosed]: SYNTAX,
	[NaxpMessage.NAXP1026_RangeUpperBoundIsBlockEscape]: SYNTAX,
	[NaxpMessage.NAXP1027_RangeReversed]: SYNTAX,
	[NaxpMessage.NAXP1028_CharacterSetEmpty]: SYNTAX,
	[NaxpMessage.NAXP1029_BackslashBeforeWhitespace]: SYNTAX,
	[NaxpMessage.NAXP1030_BackslashWithoutEscape]: SYNTAX,
	[NaxpMessage.NAXP1031_HexEscapeRemoved]: SYNTAX,
	[NaxpMessage.NAXP1032_EscapeUndefined]: SYNTAX,
	[NaxpMessage.NAXP1033_CharacterNotAllowed]: SYNTAX,
	[NaxpMessage.NAXP1034_ElementRequired]: SYNTAX,
	[NaxpMessage.NAXP1035_AlternativeEmpty]: SYNTAX,
	[NaxpMessage.NAXP1036_ReplaceableWithoutElement]: SYNTAX,
	[NaxpMessage.NAXP1037_NaxpIncomplete]: SYNTAX,
	[NaxpMessage.NAXP1038_ReservedCharacterHere]: SYNTAX,
	[NaxpMessage.NAXP1039_CharacterHere]: SYNTAX,
	[NaxpMessage.NAXP1040_ReplaceableNested]: 'W2',
	[NaxpMessage.NAXP1041_ReproducedSubjectNotSingle]: 'W1',
	[NaxpMessage.NAXP1042_RenderingNotSingle]: 'W1',
	[NaxpMessage.NAXP1043_ElementNotDeletable]: 'W1',
	[NaxpMessage.NAXP1044_RenderingNotGenerated]: 'W1',
	[NaxpMessage.NAXP1045_ReplacementNotSingleValued]: 'W3',
	[NaxpMessage.NAXP1046_ReplacementNotSingleValuedWitness]: 'W3',
	[NaxpMessage.NAXP1047_TooManyValues]: 'W5',
	[NaxpMessage.NAXP1048_ElementTooLong]: LIMIT,
	[NaxpMessage.NAXP1049_TooManyStates]: LIMIT,
	[NaxpMessage.NAXP1050_TooManyCanonicalStates]: LIMIT,
	[NaxpMessage.NAXP1051_TooManyPairStates]: LIMIT,
	[NaxpMessage.NAXP1052_PairOutputAbandoned]: LIMIT,
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
 * Whether a message is a budget of this implementation rather than a rule of the language.
 *
 * @param {string} message The message.
 * @returns {boolean} Whether it is a limit.
 */
export function isImplementationLimit(message) {
	return ruleOf(message) === LIMIT;
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
