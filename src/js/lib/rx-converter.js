// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { ALL_DIGITS, AsciiCharSet } from './ascii-char-set.js';
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

/**
 * Which of a naxp's two languages an expression is being built for.
 *
 * @enum {string}
 */
export const NaxpLanguage = Object.freeze({
	/** The accepted language *L*, the strings the naxp matches. */
	Accepted: 'Accepted',

	/**
	 * The canonical language *C*, which is *L* with each replaceable element replaced by its
	 * rendering. The encoding is a rank over this one.
	 */
	Canonical: 'Canonical',
});

/**
 * Powers of ten up to the fifteen digit cap on a digits range bound.
 *
 * Ordinary numbers, because 10^15 is below 2^53 and so is held exactly. That cap was chosen for
 * this reason, and it is why nothing in a digits range needs a BigInt.
 */
const POWERS_OF_TEN = buildPowersOfTen();

/**
 * Turns a parsed naxp into the algebra the state map is built over.
 *
 * This is step 1 of the specification's procedure. The canonicalisation table there has three
 * rows, `x!y` to `y`, `x!!` to `x` and `x!?` to `()`, but the parser already expanded the two
 * abbreviations into the general form, so all three collapse to taking the rendering.
 *
 * Digits ranges are expanded here, because a bound of fifteen digits expands to about fifteen
 * alternatives and costs nothing. Intervals are not, because their counts multiply when nested.
 *
 * @param {import('./ast.js').Ast} node The tree.
 * @param {import('./rx.js').RxFactory} factory The factory to build with.
 * @param {string} language One of {@link NaxpLanguage}.
 * @returns {import('./rx.js').Rx} The expression.
 */
export function convert(node, factory, language) {
	if (node instanceof AstEmpty) { return factory.epsilon; }

	if (node instanceof AstChars) { return factory.chars(node.charSet); }

	if (node instanceof AstDigitsRange) { return convertDigitsRange(node, factory); }

	if (node instanceof AstSequence) {
		return factory.concat(node.children.map(child => convert(child, factory, language)));
	}

	if (node instanceof AstAlternation) {
		return factory.union(node.children.map(child => convert(child, factory, language)));
	}

	if (node instanceof AstOptional) {
		return factory.unionTwo(factory.epsilon, convert(node.child, factory, language));
	}

	if (node instanceof AstInterval) {
		return factory.interval(
			convert(node.child, factory, language),
			node.minCount,
			node.maxCount);
	}

	if (node instanceof AstReplaceable) {
		return convert(
			language === NaxpLanguage.Canonical ? node.rendering : node.subject,
			factory,
			language);
	}

	throw new Error(`Unhandled node type ${node.constructor.name}.`);
}

/**
 * Expands a digits range into an ordinary expression.
 *
 * One alternative per width. The lower width admits the leading zeros the lower bound was written
 * with; every width above it does not, which is what makes `#[0-105]` stand for
 * `[0-9] | [1-9][0-9] | 10[0-5]` rather than admitting `07`.
 *
 * @param {AstDigitsRange} range The digits range.
 * @param {import('./rx.js').RxFactory} factory The factory to build with.
 * @returns {import('./rx.js').Rx} The expression.
 */
function convertDigitsRange(range, factory) {
	const widths = [];

	for (let width = range.lowDigitCount; width <= range.highDigitCount; ++width) {
		const low = width === range.lowDigitCount ? range.low : POWERS_OF_TEN[width - 1];
		const high = width === range.highDigitCount ? range.high : POWERS_OF_TEN[width] - 1;

		if (low > high) { continue; }

		widths.push(fixedWidthRange(low, high, width, factory));
	}

	return factory.union(widths);
}

/**
 * The strings of exactly `width` digits whose value lies between `low` and `high` inclusive,
 * leading zeros included.
 *
 * @param {number} low The lowest value.
 * @param {number} high The highest value.
 * @param {number} width How many digits.
 * @param {import('./rx.js').RxFactory} factory The factory to build with.
 * @returns {import('./rx.js').Rx} The expression.
 */
function fixedWidthRange(low, high, width, factory) {
	if (width === 0) { return factory.epsilon; }

	// Every string of this width qualifies, so there is nothing to split on.
	if (low === 0 && high === POWERS_OF_TEN[width] - 1) {
		return factory.interval(factory.chars(ALL_DIGITS), width, width);
	}

	const place = POWERS_OF_TEN[width - 1];
	const lowLead = Math.floor(low / place);
	const highLead = Math.floor(high / place);
	const lowRest = low % place;
	const highRest = high % place;

	if (lowLead === highLead) {
		return factory.concatTwo(
			digitChars(lowLead, lowLead, factory),
			fixedWidthRange(lowRest, highRest, width - 1, factory));
	}

	const alternatives = [
		factory.concatTwo(
			digitChars(lowLead, lowLead, factory),
			fixedWidthRange(lowRest, place - 1, width - 1, factory)),
	];

	if (highLead - lowLead >= 2) {
		alternatives.push(
			factory.concatTwo(
				digitChars(lowLead + 1, highLead - 1, factory),
				factory.interval(factory.chars(ALL_DIGITS), width - 1, width - 1)));
	}

	alternatives.push(
		factory.concatTwo(
			digitChars(highLead, highLead, factory),
			fixedWidthRange(0, highRest, width - 1, factory)));

	return factory.union(alternatives);
}

/**
 * @param {number} lowDigit The lowest digit.
 * @param {number} highDigit The highest digit.
 * @param {import('./rx.js').RxFactory} factory The factory to build with.
 * @returns {import('./rx.js').Rx} The characters.
 */
function digitChars(lowDigit, highDigit, factory) {
	return factory.chars(AsciiCharSet.fromCharRange(0x30 + lowDigit, 0x30 + highDigit));
}

/** @returns {number[]} Powers of ten from 10^0 to 10^15. */
function buildPowersOfTen() {
	const powers = new Array(16);

	powers[0] = 1;

	for (let i = 1; i < powers.length; ++i) { powers[i] = powers[i - 1] * 10; }

	return powers;
}
