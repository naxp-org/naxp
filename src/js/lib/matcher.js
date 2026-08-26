// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import {
	AstAlternation,
	AstChars,
	AstDigitsRange,
	AstEmpty,
	AstInterval,
	AstOptional,
	AstReplaceable,
	AstSequence,
} from './ast.js';
import { NaxpLimits } from './naxp-limits.js';

/**
 * The most characters this implementation will materialise for a single generated string.
 *
 * Not a rule of the language. A naxp generating a longer string than this would be refused by the
 * state budget a moment later in any case.
 */
export const MAX_GENERATED_LENGTH = NaxpLimits.maxStringLength;

/**
 * Whether an expression generates exactly one string.
 *
 * @enum {string}
 */
export const SingleStringOutcome = Object.freeze({
	/** The expression generates exactly one string. */
	Single: 'Single',
	/** It generates none, or more than one. */
	Multiple: 'Multiple',
	/** It generates one string, but longer than {@link MAX_GENERATED_LENGTH}. */
	TooLong: 'TooLong',
});

/**
 * The parts of a string being built, with the running length the C# reads off `StringBuilder`.
 *
 * Joining once at the end rather than concatenating as it goes, because several of the cases
 * below build a child into a builder of its own purely to ask how long it came out.
 */
class TextBuilder {
	constructor() {
		/** @type {string[]} */
		this.parts = [];
		/** @type {number} */
		this.length = 0;
	}

	/**
	 * @param {string} text The text to add.
	 */
	append(text) {
		this.parts.push(text);
		this.length += text.length;
	}

	/** @returns {string} Everything appended so far. */
	toString() {
		return this.parts.join('');
	}
}

/**
 * The one string an expression generates, if it generates exactly one.
 *
 * @param {import('./ast.js').Ast} node The expression.
 * @returns {{outcome: string, result: string | null}} The outcome, and the string where there is
 * one.
 */
export function tryGetSingleString(node) {
	const builder = new TextBuilder();
	const outcome = appendSingleString(node, builder);

	return {
		outcome,
		result: outcome === SingleStringOutcome.Single ? builder.toString() : null,
	};
}

/**
 * Appends the one string an expression generates.
 *
 * @param {import('./ast.js').Ast} node The expression.
 * @param {TextBuilder} builder Where to put it.
 * @returns {string} The outcome, one of {@link SingleStringOutcome}.
 */
function appendSingleString(node, builder) {
	if (node instanceof AstEmpty) { return SingleStringOutcome.Single; }

	if (node instanceof AstChars) {
		const single = node.charSet.singleCharacter;

		if (single === null) { return SingleStringOutcome.Multiple; }

		builder.append(String.fromCharCode(single));

		return within(builder);
	}

	if (node instanceof AstDigitsRange) {
		// One string only where the two bounds are the same number written to the same width.
		if (node.low !== node.high || node.lowDigitCount !== node.highDigitCount) {
			return SingleStringOutcome.Multiple;
		}

		builder.append(String(node.low).padStart(node.lowDigitCount, '0'));

		return within(builder);
	}

	if (node instanceof AstSequence) {
		for (const child of node.children) {
			const outcome = appendSingleString(child, builder);

			if (outcome !== SingleStringOutcome.Single) { return outcome; }
		}

		return SingleStringOutcome.Single;
	}

	if (node instanceof AstAlternation) {
		// Every alternative must give the same one string, so 'A|A' generates one.
		const firstBuilder = new TextBuilder();
		const firstOutcome = appendSingleString(node.children[0], firstBuilder);

		if (firstOutcome !== SingleStringOutcome.Single) { return firstOutcome; }

		const first = firstBuilder.toString();

		for (let i = 1; i < node.children.length; ++i) {
			const otherBuilder = new TextBuilder();
			const otherOutcome = appendSingleString(node.children[i], otherBuilder);

			if (otherOutcome !== SingleStringOutcome.Single) { return otherOutcome; }

			if (otherBuilder.toString() !== first) { return SingleStringOutcome.Multiple; }
		}

		builder.append(first);

		return within(builder);
	}

	if (node instanceof AstOptional) {
		// x? always generates the empty string, so it is single valued only where x does too.
		const inner = new TextBuilder();
		const outcome = appendSingleString(node.child, inner);

		if (outcome === SingleStringOutcome.TooLong) { return outcome; }

		return outcome === SingleStringOutcome.Single && inner.length === 0
			? SingleStringOutcome.Single
			: SingleStringOutcome.Multiple;
	}

	if (node instanceof AstInterval) {
		// A zero count denotes the empty string whatever the child generates.
		if (node.maxCount === 0) { return SingleStringOutcome.Single; }

		const inner = new TextBuilder();
		const outcome = appendSingleString(node.child, inner);

		if (outcome !== SingleStringOutcome.Single) { return outcome; }

		if (inner.length === 0) { return SingleStringOutcome.Single; }

		if (node.minCount !== node.maxCount) { return SingleStringOutcome.Multiple; }

		if (inner.length * node.minCount > MAX_GENERATED_LENGTH) {
			return SingleStringOutcome.TooLong;
		}

		const once = inner.toString();

		for (let i = 0; i < node.minCount; ++i) { builder.append(once); }

		return within(builder);
	}

	if (node instanceof AstReplaceable) {
		// The strings x!y generates are the strings x accepts. W2 has already refused any tree
		// that reaches this case from within another '!'.
		return appendSingleString(node.subject, builder);
	}

	throw new Error(`Unhandled node type ${node.constructor.name}.`);
}

/**
 * @param {TextBuilder} builder The builder.
 * @returns {string} Whether what has been built is still within budget.
 */
function within(builder) {
	return builder.length <= MAX_GENERATED_LENGTH
		? SingleStringOutcome.Single
		: SingleStringOutcome.TooLong;
}

/**
 * Whether an expression generates a string exactly.
 *
 * @param {import('./ast.js').Ast} node The expression.
 * @param {string} text The string it must generate in full.
 * @returns {{matched: boolean, tooLong: boolean}} Whether it generates the string, and whether
 * the answer was abandoned as too large to compute.
 */
export function generates(node, text) {
	if (node === null || node === undefined) { throw new TypeError('node is required.'); }

	if (text.length > MAX_GENERATED_LENGTH) { return { matched: false, tooLong: true }; }

	const ends = advance(node, text, new Set([0]));

	return { matched: ends.has(text.length), tooLong: false };
}

/**
 * The set of positions reachable by matching an expression from each of `starts`.
 *
 * Working with sets of positions rather than backtracking keeps the cost polynomial. There are at
 * most one more positions than there are characters, so an alternation cannot multiply the work.
 *
 * @param {import('./ast.js').Ast} node The expression.
 * @param {string} text The string being matched.
 * @param {Set<number>} starts The positions to match from.
 * @returns {Set<number>} The positions reached.
 */
export function advance(node, text, starts) {
	if (starts.size === 0) { return starts; }

	if (node instanceof AstEmpty) { return starts; }

	if (node instanceof AstChars) {
		const result = new Set();

		for (const p of starts) {
			if (p < text.length && node.charSet.contains(text.charCodeAt(p))) { result.add(p + 1); }
		}

		return result;
	}

	if (node instanceof AstDigitsRange) { return advanceDigitsRange(node, text, starts); }

	if (node instanceof AstSequence) {
		let current = starts;

		for (const child of node.children) {
			current = advance(child, text, current);

			if (current.size === 0) { break; }
		}

		return current;
	}

	if (node instanceof AstAlternation) {
		const result = new Set();

		for (const child of node.children) {
			for (const p of advance(child, text, starts)) { result.add(p); }
		}

		return result;
	}

	if (node instanceof AstOptional) {
		const result = new Set(starts);

		for (const p of advance(node.child, text, starts)) { result.add(p); }

		return result;
	}

	if (node instanceof AstInterval) {
		const result = node.minCount === 0 ? new Set(starts) : new Set();
		let current = starts;

		for (let i = 1; i <= node.maxCount; ++i) {
			const next = advance(node.child, text, current);

			if (next.size === 0) { break; }

			if (i >= node.minCount) { for (const p of next) { result.add(p); } }

			// A child that matches the empty string reaches a fixed point at once, and the
			// remaining repetitions add nothing. Without this the largest count costs that many
			// passes over the string for no gain.
			if (i >= node.minCount && sameSet(next, current)) { break; }

			current = next;
		}

		return result;
	}

	if (node instanceof AstReplaceable) {
		// x!y accepts whatever x accepts.
		return advance(node.subject, text, starts);
	}

	throw new Error(`Unhandled node type ${node.constructor.name}.`);
}

/**
 * Matches a digits range without expanding it.
 *
 * A string of `w` digits is generated when `w` lies between the two written widths, its value is
 * at least the lower bound if `w` is the lower width, its value is at most the upper bound if `w`
 * is the upper width, and it has no leading zero unless `w` is the lower width. That last clause
 * is what makes `#[0-105]` expand to `[0-9] | [1-9][0-9] | 10[0-5]` rather than admitting `07`.
 *
 * The value is an ordinary number. The loop runs at most `highDigitCount` times and a bound has
 * at most fifteen digits, so it stays below 10^15 and well inside what a double holds exactly.
 *
 * @param {AstDigitsRange} range The digits range.
 * @param {string} text The string being matched.
 * @param {Set<number>} starts The positions to match from.
 * @returns {Set<number>} The positions reached.
 */
function advanceDigitsRange(range, text, starts) {
	const result = new Set();

	for (const p of starts) {
		let value = 0;

		for (let width = 1; width <= range.highDigitCount; ++width) {
			const index = (p + width) - 1;

			if (index >= text.length) { break; }

			const code = text.charCodeAt(index);

			if (code < 0x30 || code > 0x39) { break; }

			value = (value * 10) + (code - 0x30);

			if (width < range.lowDigitCount) { continue; }
			if (width > range.lowDigitCount && text[p] === '0') { continue; }
			if (width === range.lowDigitCount && value < range.low) { continue; }
			if (width === range.highDigitCount && value > range.high) { continue; }

			result.add(p + width);
		}
	}

	return result;
}

/**
 * Whether two sets hold the same members.
 *
 * @param {Set<number>} left The first set.
 * @param {Set<number>} right The second set.
 * @returns {boolean} Whether they are equal.
 */
function sameSet(left, right) {
	if (left.size !== right.size) { return false; }

	for (const item of left) {
		if (!right.has(item)) { return false; }
	}

	return true;
}
