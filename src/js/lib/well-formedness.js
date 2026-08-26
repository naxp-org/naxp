// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import {
	AstAlternation,
	AstInterval,
	AstOptional,
	AstReplaceable,
	AstSequence,
	ReplaceableForm,
	containsReplaceable,
} from './ast.js';
import { MAX_GENERATED_LENGTH, SingleStringOutcome, generates, tryGetSingleString } from './matcher.js';
import { NaxpError } from './naxp-error.js';
import { NaxpMessage } from './naxp-message.js';

/**
 * Checks the well-formedness rules that can be decided from the tree, which is W2 then W1.
 *
 * W4 is decided by the parser, where the tokens are read. W3 needs the single-valuedness of a
 * transduction and W5 needs the size of the canonical language, so both wait on the state map;
 * neither is checked here and a naxp that breaks one is currently accepted.
 *
 * W1 asks whether a rendering is one of the strings its subject generates, which is the matcher's
 * business rather than this module's.
 *
 * @param {import('./ast.js').Ast} ast The tree, as returned by `tryParse`.
 * @returns {NaxpError | null} The refusal, or null if the tree passes.
 */
export function check(ast) {
	if (ast === null || ast === undefined) { throw new TypeError('ast is required.'); }

	// W2 first, because W1 reads inside both operands of a '!' and the answer is only meaningful
	// once nothing is hidden in there.
	return checkW2(ast) ?? checkW1(ast);
}

/**
 * W2: `!` may not nest.
 *
 * @param {import('./ast.js').Ast} node The node to check from.
 * @returns {NaxpError | null} The refusal, or null.
 */
function checkW2(node) {
	if (node instanceof AstReplaceable
		&& (containsReplaceable(node.subject) || containsReplaceable(node.rendering))) {
		return new NaxpError(NaxpMessage.NAXP1040_ReplaceableNested);
	}

	for (const child of children(node)) {
		const error = checkW2(child);

		if (error !== null) { return error; }
	}

	return null;
}

/**
 * W1: a rendering must be one of the strings it replaces.
 *
 * @param {import('./ast.js').Ast} node The node to check from.
 * @returns {NaxpError | null} The refusal, or null.
 */
function checkW1(node) {
	if (node instanceof AstReplaceable) {
		const { outcome, result: rendering } = tryGetSingleString(node.rendering);

		if (outcome === SingleStringOutcome.TooLong) { return tooLongError(); }

		if (outcome === SingleStringOutcome.Multiple) {
			return new NaxpError(node.form === ReplaceableForm.Reproduced ? NaxpMessage.NAXP1041_ReproducedSubjectNotSingle : NaxpMessage.NAXP1042_RenderingNotSingle);
		}

		const { matched, tooLong } = generates(node.subject, rendering);

		if (!matched) {
			if (tooLong) { return tooLongError(); }

			return new NaxpError(rendering.length === 0 ? NaxpMessage.NAXP1043_ElementNotDeletable : NaxpMessage.NAXP1044_RenderingNotGenerated, rendering.length === 0 ? null : rendering);
		}
	}

	for (const child of children(node)) {
		const error = checkW1(child);

		if (error !== null) { return error; }
	}

	return null;
}

/**
 * The refusal for an element this implementation declines to materialise.
 *
 * The whole naxp is reported rather than the element, because the tree records where a node
 * starts and not where it ends. Narrowing this wants an end offset on every parser production.
 *
 * @returns {NaxpError} The refusal.
 */
function tooLongError() {
	return new NaxpError(NaxpMessage.NAXP1048_ElementTooLong);
}

/**
 * The children of a node.
 *
 * @param {import('./ast.js').Ast} node The node.
 * @returns {import('./ast.js').Ast[]} Its children, which may be none.
 */
function children(node) {
	if (node instanceof AstSequence || node instanceof AstAlternation) { return node.children; }

	if (node instanceof AstOptional || node instanceof AstInterval) { return [node.child]; }

	if (node instanceof AstReplaceable) { return [node.subject, node.rendering]; }

	return [];
}
