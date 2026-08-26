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
import { SingleStringOutcome, advance, tryGetSingleString } from './matcher.js';
import { NaxpLimits } from './naxp-limits.js';

/**
 * Computes ρ, the map from an accepted string to its canonical form.
 *
 * ρ(*w*) is *w* with the match of each replaceable element replaced by that element's rendering.
 * The tree is where that is visible, since the machines have already resolved it one way or the
 * other, so this works over the tree.
 *
 * It is the matcher's set of positions with one output carried alongside each position. A
 * replaceable element contributes its rendering whatever it matched, which is the whole of what
 * makes the canonical form differ from the input.
 *
 * One output per position is enough, and that rests on W3 being decided when the naxp was
 * compiled. Everything reached at a given point in the walk has the same future, since what
 * follows depends on the position alone; so two partial parses that meet at one position either
 * both reach the end or neither does. If both reach it they append the same remainder, and W3 says
 * the two totals agree, which forces the two outputs to have agreed already. Carrying the whole set
 * would therefore only ever record the same string twice — and it was what made this exponential,
 * since `([ab]|[ab]!a){17}` reaches 2^17 outputs on an all-`b` input.
 */
class Canonicaliser {
	/**
	 * @param {string} text The string being canonicalised.
	 */
	constructor(text) {
		this.text = text;
		/** @type {Map<AstReplaceable, string>} */
		this.renderings = new Map();
	}

	/**
	 * @param {import('./ast.js').Ast} node The node.
	 * @param {Map<number, string>} starts The positions reached so far, with their outputs.
	 * @returns {Map<number, string>} The positions reached.
	 */
	advance(node, starts) {
		if (starts.size === 0) { return starts; }

		if (node instanceof AstEmpty) { return starts; }

		if (node instanceof AstChars) {
			const result = new Map();

			for (const [at, output] of starts) {
				if (at >= this.text.length) { continue; }

				if (node.charSet.contains(this.text.charCodeAt(at))) {
					put(result, at + 1, output + this.text[at]);
				}
			}

			return result;
		}

		// A digits range emits what it consumed.
		if (node instanceof AstDigitsRange) { return this.consume(node, starts); }

		if (node instanceof AstSequence) {
			let current = starts;

			for (const child of node.children) {
				current = this.advance(child, current);

				if (current.size === 0) { break; }
			}

			return current;
		}

		if (node instanceof AstAlternation) {
			const result = new Map();

			for (const child of node.children) { putAll(result, this.advance(child, starts)); }

			return result;
		}

		if (node instanceof AstOptional) {
			const result = new Map(starts);

			putAll(result, this.advance(node.child, starts));

			return result;
		}

		if (node instanceof AstInterval) {
			const result = node.minCount === 0 ? new Map(starts) : new Map();
			let current = starts;

			for (let i = 1; i <= node.maxCount; ++i) {
				const next = this.advance(node.child, current);

				if (next.size === 0) { break; }

				if (i >= node.minCount) { putAll(result, next); }

				// A child that matches nothing reaches a fixed point at once.
				if (i >= node.minCount && sameAs(next, current)) { break; }

				current = next;
			}

			return result;
		}

		if (node instanceof AstReplaceable) {
			// This is the whole of the difference between a string and its canonical form:
			// whatever the subject matched, the rendering is what comes out.
			const rendering = this.renderingOf(node);
			const result = new Map();

			for (const [at, output] of starts) {
				for (const end of advance(node.subject, this.text, new Set([at]))) {
					put(result, end, output + rendering);
				}
			}

			return result;
		}

		throw new Error(`Unhandled node type ${node.constructor.name}.`);
	}

	/**
	 * Advances by a node that emits exactly the characters it consumed.
	 *
	 * @param {import('./ast.js').Ast} node The node.
	 * @param {Map<number, string>} starts The positions.
	 * @returns {Map<number, string>} The positions reached.
	 */
	consume(node, starts) {
		const result = new Map();

		for (const [at, output] of starts) {
			for (const end of advance(node, this.text, new Set([at]))) {
				put(result, end, output + this.text.slice(at, end));
			}
		}

		return result;
	}

	/**
	 * @param {AstReplaceable} replaceable The element.
	 * @returns {string} Its rendering.
	 */
	renderingOf(replaceable) {
		const cached = this.renderings.get(replaceable);

		if (cached !== undefined) { return cached; }

		// W1 has already established that the rendering generates exactly one string.
		const { outcome, result } = tryGetSingleString(replaceable.rendering);

		if (outcome !== SingleStringOutcome.Single) {
			throw new Error('A replaceable element passed W1 but has no single rendering.');
		}

		this.renderings.set(replaceable, result);

		return result;
	}
}

/**
 * Records an output for a position, keeping whichever arrived first.
 *
 * Under W3 a second one that matters cannot differ from the first; see the note on the class.
 *
 * @param {Map<number, string>} target Where to record it.
 * @param {number} end The position.
 * @param {string} output What was emitted to reach it.
 */
function put(target, end, output) {
	if (!target.has(end)) { target.set(end, output); }
}

/**
 * @param {Map<number, string>} target Where to record them.
 * @param {Map<number, string>} source What to record.
 */
function putAll(target, source) {
	for (const [end, output] of source) { put(target, end, output); }
}

/**
 * @param {Map<number, string>} left The first.
 * @param {Map<number, string>} right The second.
 * @returns {boolean} Whether they agree on every position and output.
 */
function sameAs(left, right) {
	if (left.size !== right.size) { return false; }

	for (const [end, output] of left) {
		if (right.get(end) !== output) { return false; }
	}

	return true;
}

/**
 * The canonical form of a string.
 *
 * @param {import('./ast.js').Ast} ast The parsed naxp, which must have been through the W3
 * checker.
 * @param {string} text The string.
 * @returns {string | null} The canonical form, or null where the naxp does not accept the string.
 */
export function tryCanonicalise(ast, text) {
	if (ast === null || ast === undefined) { throw new TypeError('ast is required.'); }

	// A naxp within the state budget has a longest string shorter than this, so anything longer
	// is not accepted rather than too costly to decide.
	if (text.length > NaxpLimits.maxStringLength) { return null; }

	const reached = new Canonicaliser(text).advance(ast, new Map([[0, '']]));
	const output = reached.get(text.length);

	return output === undefined ? null : output;
}
