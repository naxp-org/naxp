// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';
import {
	AstAlternation,
	AstChars,
	AstInterval,
	AstOptional,
	AstSequence,
	AstUnified,
	UnifiedForm,
} from './ast.js';

/**
 * The rewrite a case fold stands for, written `\C` for upper case canonical and `\c` for lower.
 *
 * A fold is shorthand and nothing downstream of the parser sees one. `\CA` becomes `[Aa]!A` and
 * `\C[A-F]` becomes the six way alternation the specification writes out, so the language, the
 * well-formedness rules and the encoding all apply to the expansion and none of them needs a case
 * for folding. W2 falls out of this: a fold inside the operand of a `!` puts a `!` there, which the
 * existing nesting check refuses.
 *
 * The expansion grows with the alphabet rather than with the pattern, `\C\a` being twenty six
 * alternatives. That is affordable because the branches are disjoint on their first character, so
 * the machines built from them merge back into one state with a wider transition table.
 *
 * Every node made here takes the pattern offset of the node it replaces, so a fault found in an
 * expansion still points at what the author wrote.
 */

const UPPER_A = 0x41;
const UPPER_Z = 0x5a;
const LOWER_A = 0x61;
const LOWER_Z = 0x7a;
const CASE_GAP = LOWER_A - UPPER_A;

/**
 * Expands a fold over the element it binds to.
 *
 * @param {import('./ast.js').Ast} node The element, already parsed.
 * @param {boolean} toUpper Whether upper case is canonical, which is `\C`.
 * @returns {import('./ast.js').Ast} The element with the fold expanded, or the element itself
 *   where nothing in it has case.
 */
export function applyFold(node, toUpper) {
	if (node instanceof AstChars) { return foldChars(node, toUpper); }

	if (node instanceof AstSequence) {
		return at(new AstSequence(node.children.map(child => applyFold(child, toUpper))), node);
	}

	if (node instanceof AstAlternation) {
		return at(new AstAlternation(node.children.map(child => applyFold(child, toUpper))), node);
	}

	if (node instanceof AstOptional) {
		return at(new AstOptional(applyFold(node.child, toUpper)), node);
	}

	if (node instanceof AstInterval) {
		return at(new AstInterval(applyFold(node.child, toUpper), node.minCount, node.maxCount), node);
	}

	if (node instanceof AstUnified) { return foldUnified(node, toUpper); }

	// The empty string and a decimal range have no character to fold.
	return node;
}

/**
 * Expands a fold over one character set, which is where a fold does its work.
 *
 * The characters with no case stay in a set of their own and are matched as they were. Each letter
 * present in either case becomes one unified element accepting the pair and rendering the canonical
 * one, so `\C[\9A-F]` is `[\9]` beside six of them.
 *
 * @param {AstChars} chars The set.
 * @param {boolean} toUpper Whether upper case is canonical.
 * @returns {import('./ast.js').Ast} The expansion, or the set where it holds nothing with case.
 */
function foldChars(chars, toUpper) {
	let uncased = AsciiCharSet.empty;
	let canonicalLetters = AsciiCharSet.empty;

	for (const code of chars.charSet) {
		if (isCased(code)) {
			canonicalLetters = canonicalLetters.union(AsciiCharSet.fromSingleChar(canonical(code, toUpper)));
		} else {
			uncased = uncased.union(AsciiCharSet.fromSingleChar(code));
		}
	}

	// A fold over characters with no case is not an error, it just has nothing to do.
	if (canonicalLetters.isEmpty) { return chars; }

	/** @type {import('./ast.js').Ast[]} */
	const branches = [];

	if (!uncased.isEmpty) { branches.push(at(new AstChars(uncased), chars)); }

	for (const code of canonicalLetters) {
		const pair = AsciiCharSet.fromSingleChar(code).union(AsciiCharSet.fromSingleChar(otherCase(code)));

		branches.push(at(
			new AstUnified(
				at(new AstChars(pair), chars),
				at(new AstChars(AsciiCharSet.fromSingleChar(code)), chars),
				UnifiedForm.Fold),
			chars));
	}

	return branches.length === 1 ? branches[0] : at(new AstAlternation(branches), chars);
}

/**
 * Folds a unified element, which widens its subject and canonicalises its rendering.
 *
 * No `!` is added. Which of the strings the subject accepts was matched is unencoded already, so
 * there is nothing there for a fold to unify; all that is wanted is that the subject accept both
 * cases and that the rendering come out in the canonical one. W1 then holds without being checked,
 * the rendering being a case variant of a string the subject generated and the widened subject
 * generating every case variant of what it generated before.
 *
 * @param {AstUnified} unified The unified element.
 * @param {boolean} toUpper Whether upper case is canonical.
 * @returns {import('./ast.js').Ast} The folded element.
 */
function foldUnified(unified, toUpper) {
	// A fold already expanded within the extent is treated like any other unified element: its
	// subject is widened again, which changes nothing, and its rendering takes the outer fold's
	// case. The outer fold governs.
	return at(
		new AstUnified(
			mapCharSets(unified.subject, widenSet),
			mapCharSets(unified.rendering, set => canonicaliseSet(set, toUpper)),
			unified.form),
		unified);
}

/**
 * Rebuilds a subtree with every character set passed through `map`.
 *
 * The `x!!` form shares one subtree between its subject and its rendering. Rebuilding rather than
 * mutating is what allows the two to be mapped differently, which is exactly what folding one of
 * them does.
 *
 * @param {import('./ast.js').Ast} node The subtree.
 * @param {(set: AsciiCharSet) => AsciiCharSet} map What to do to each set.
 * @returns {import('./ast.js').Ast} The rebuilt subtree.
 */
function mapCharSets(node, map) {
	if (node instanceof AstChars) { return at(new AstChars(map(node.charSet)), node); }

	if (node instanceof AstSequence) {
		return at(new AstSequence(node.children.map(child => mapCharSets(child, map))), node);
	}

	if (node instanceof AstAlternation) {
		return at(new AstAlternation(node.children.map(child => mapCharSets(child, map))), node);
	}

	if (node instanceof AstOptional) {
		return at(new AstOptional(mapCharSets(node.child, map)), node);
	}

	if (node instanceof AstInterval) {
		return at(new AstInterval(mapCharSets(node.child, map), node.minCount, node.maxCount), node);
	}

	if (node instanceof AstUnified) {
		// Nesting is W2's to refuse, and it reads a tree this has already been through.
		return at(
			new AstUnified(
				mapCharSets(node.subject, map),
				mapCharSets(node.rendering, map),
				node.form),
			node);
	}

	return node;
}

/**
 * Adds the other case of every cased character, leaving the rest alone.
 *
 * @param {AsciiCharSet} set The set.
 * @returns {AsciiCharSet} The widened set.
 */
function widenSet(set) {
	let widened = set;

	for (const code of set) {
		if (isCased(code)) { widened = widened.union(AsciiCharSet.fromSingleChar(otherCase(code))); }
	}

	return widened;
}

/**
 * Replaces every cased character by its canonical case, which may shrink the set.
 *
 * @param {AsciiCharSet} set The set.
 * @param {boolean} toUpper Whether upper case is canonical.
 * @returns {AsciiCharSet} The canonicalised set.
 */
function canonicaliseSet(set, toUpper) {
	let result = AsciiCharSet.empty;

	for (const code of set) {
		result = result.union(AsciiCharSet.fromSingleChar(canonical(code, toUpper)));
	}

	return result;
}

/**
 * @param {number} code A character code.
 * @returns {boolean} Whether it is a letter.
 */
function isCased(code) {
	return (code >= UPPER_A && code <= UPPER_Z) || (code >= LOWER_A && code <= LOWER_Z);
}

/**
 * @param {number} code A character code.
 * @param {boolean} toUpper Whether upper case is canonical.
 * @returns {number} The canonical case of it.
 */
function canonical(code, toUpper) {
	if (toUpper) { return code >= LOWER_A && code <= LOWER_Z ? code - CASE_GAP : code; }

	return code >= UPPER_A && code <= UPPER_Z ? code + CASE_GAP : code;
}

/**
 * @param {number} code A character code.
 * @returns {number} The other case of it, or it unchanged where it has none.
 */
function otherCase(code) {
	if (code >= UPPER_A && code <= UPPER_Z) { return code + CASE_GAP; }
	if (code >= LOWER_A && code <= LOWER_Z) { return code - CASE_GAP; }

	return code;
}

/**
 * @param {import('./ast.js').Ast} node The node to place.
 * @param {import('./ast.js').Ast} source The node it replaces.
 * @returns {import('./ast.js').Ast} The node, at the source's offset.
 */
function at(node, source) {
	node.patternOffset = source.patternOffset;

	return node;
}
