// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';
import {
	AstAlternation,
	AstChars,
	AstDecimalRange,
	AstEmpty,
	AstOptional,
	AstSequence,
	AstUnified,
	UnifiedForm,
} from './ast.js';

/**
 * The expansion of a decimal range whose lower bound carries marks, as in `#[0!0!0-105]`.
 *
 * A mark is shorthand and nothing downstream of the parser sees one. `0!` becomes `0!!` and `0?`
 * becomes `0!?`, so the language, the well-formedness rules and the encoding all apply to the
 * expansion and none of them needs a case for padding. W2 falls out of this in the same way it
 * does for a case fold: a marked range inside the operand of a `!` puts a `!` there, which the
 * existing nesting check refuses.
 *
 * The shape is one alternative per width the value itself takes, with the padding positions that
 * apply to that width written out in front. A padding position is mandatory where it carries no
 * mark, which is what makes an unmarked leading zero go on setting a minimum width.
 *
 * A unified element contributes its rendering whether or not its subject matched anything, so a
 * run of padding contributes a fixed string. That is what keeps `07` to one canonical form under
 * `0!! 0!!`, where either of the two could have been the one that matched.
 *
 * Every node made here takes the pattern offset of the range it replaces, so a fault found in an
 * expansion still points at what the author wrote.
 */

/** Ten to the power of the index, to the fifteen digits a bound may have. */
const POWERS_OF_TEN = Object.freeze(
	Array.from({ length: 16 }, (_unused, exponent) => 10 ** exponent));

/**
 * Expands a marked decimal range into an alternation of ordinary elements.
 *
 * @param {number} low The value of the lower bound.
 * @param {number} lowDigitCount The digits the lower bound was written with.
 * @param {number} high The value of the upper bound.
 * @param {number} highDigitCount The digits the upper bound was written with.
 * @param {string[]} marks One entry per digit of the lower bound, each `'!'`, `'?'` or the empty
 *   string where that digit carries no mark. The array may be longer than the bound.
 * @param {number} offset The offset of the range in the pattern, for diagnostics.
 * @returns {import('./ast.js').Ast} The expansion.
 */
export function expandPadding(low, lowDigitCount, high, highDigitCount, marks, offset) {
	const alternatives = [];

	for (let width = 1; width <= highDigitCount; ++width) {
		const first = Math.max(low, width === 1 ? 0 : POWERS_OF_TEN[width - 1]);
		const last = Math.min(high, POWERS_OF_TEN[width] - 1);

		if (first > last) { continue; }

		alternatives.push(alternative(first, last, width, lowDigitCount, marks, offset));
	}

	return alternatives.length === 1
		? alternatives[0]
		: at(new AstAlternation(alternatives), offset);
}

/**
 * The alternative for values of one width: its padding, then the values themselves.
 *
 * @param {number} first The lowest value of this width.
 * @param {number} last The highest.
 * @param {number} width The digits the value itself takes.
 * @param {number} lowDigitCount The digits the lower bound was written with.
 * @param {string[]} marks The marks, one per digit of the lower bound.
 * @param {number} offset The offset of the range in the pattern.
 * @returns {import('./ast.js').Ast} The alternative.
 */
function alternative(first, last, width, lowDigitCount, marks, offset) {
	const padCount = Math.max(0, lowDigitCount - width);
	const values = at(new AstDecimalRange(first, width, last, width), offset);

	if (padCount === 0) { return values; }

	const parts = [];

	for (let position = 0; position < padCount; ++position) {
		parts.push(paddingPosition(marks[position], offset));
	}

	parts.push(values);

	return at(new AstSequence(parts), offset);
}

/**
 * One padding position: a mandatory zero, or the unified element its mark stands for.
 *
 * @param {string} mark The mark, `'!'`, `'?'` or the empty string.
 * @param {number} offset The offset of the range in the pattern.
 * @returns {import('./ast.js').Ast} The element.
 */
function paddingPosition(mark, offset) {
	if (!mark) { return zero(offset); }

	// The expansions are the ones the specification gives: 0! is 0!!, which is 0?!(0), and 0? is
	// 0!?, which is 0?!().
	const reproduced = mark === '!';
	const subject = at(new AstOptional(zero(offset)), offset);
	const rendering = reproduced ? zero(offset) : at(new AstEmpty(), offset);
	const form = reproduced ? UnifiedForm.Reproduced : UnifiedForm.Dropped;

	return at(new AstUnified(subject, rendering, form), offset);
}

/**
 * A literal zero.
 *
 * @param {number} offset The offset of the range in the pattern.
 * @returns {import('./ast.js').Ast} The element.
 */
function zero(offset) {
	return at(new AstChars(AsciiCharSet.fromSingleChar(0x30)), offset);
}

/**
 * Puts a pattern offset on a node, which is what makes a fault in an expansion point at what the
 * author wrote.
 *
 * @template {import('./ast.js').Ast} T
 * @param {T} node The node.
 * @param {number} offset The offset.
 * @returns {T} The node.
 */
function at(node, offset) {
	node.patternOffset = offset;

	return node;
}
