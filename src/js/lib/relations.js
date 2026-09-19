// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';
import { Agreement, compare as compareRanks } from './rank-agreement.js';
import { SetRelationship } from './set-relationship.js';
import { MAX_TUPLES, compare as compareValues } from './value-agreement.js';

/**
 * How the languages of two machines, and the encodings of two naxps, stand to one another.
 *
 * States are interned within one build, so two languages built through the same factory are equal
 * exactly when their start states are the same object. Two naxps parsed separately do not share a
 * factory, so that shortcut is unavailable here and the walk below is what decides it.
 */

/**
 * The characters a state can consume, the end of text transition excepted.
 *
 * @param {import('./state-map.js').State} state The state.
 * @returns {AsciiCharSet} The union of its transition sets.
 */
function taken(state) {
	let union = AsciiCharSet.empty;

	for (const transition of state.transitions) { union = union.union(transition.set); }

	return union;
}

/**
 * How the language of `a` stands to the language of `b`.
 *
 * A walk of the product, carrying two facts: whether `a` holds a string `b` does not, and whether
 * the reverse holds. Both machines are acyclic and trimmed, so a state reached with characters the
 * other side cannot take is a witness on its own and needs no completion.
 *
 * @param {import('./state-map.js').StateMap} a The first machine.
 * @param {import('./state-map.js').StateMap} b The second machine.
 * @returns {string} The relationship, from `a`'s point of view, as one of {@link SetRelationship}.
 */
export function compareLanguages(a, b) {
	let aHasExtra = false;
	let bHasExtra = false;

	/** @type {Set<string>} */
	const visited = new Set();

	/**
	 * @param {import('./state-map.js').State} p The state in `a`.
	 * @param {import('./state-map.js').State} q The state in `b`.
	 */
	const visit = (p, q) => {
		// Nothing further can change the answer once both are known.
		if (aHasExtra && bHasExtra) { return; }

		const key = `${p.id},${q.id}`;

		if (visited.has(key)) { return; }

		visited.add(key);

		if (p.acceptsEndOfText !== q.acceptsEndOfText) {
			if (p.acceptsEndOfText) { aHasExtra = true; } else { bHasExtra = true; }
		}

		const takenByP = taken(p);
		const takenByQ = taken(q);

		// A character one side can take and the other cannot leads somewhere, because every state
		// of a trimmed machine holds at least one string.
		for (const transition of p.transitions) {
			if (transition.set.isEmpty) { continue; }

			if (!transition.set.subtract(takenByQ).isEmpty) { aHasExtra = true; }
		}

		for (const transition of q.transitions) {
			if (transition.set.isEmpty) { continue; }

			if (!transition.set.subtract(takenByP).isEmpty) { bHasExtra = true; }
		}

		for (const left of p.transitions) {
			if (left.set.isEmpty) { continue; }

			for (const right of q.transitions) {
				if (right.set.isEmpty) { continue; }

				if (left.set.intersectsWith(right.set)) { visit(left.next, right.next); }
			}
		}
	};

	visit(a.start, b.start);

	if (aHasExtra) {
		return bHasExtra ? SetRelationship.Incomparable : SetRelationship.SupersetOf;
	}

	return bHasExtra ? SetRelationship.SubsetOf : SetRelationship.Equal;
}

/**
 * How the encoding of `a` stands to the encoding of `b`, each taken as the set of (text, value)
 * pairs it defines.
 *
 * One graph contains another exactly when its domain does and the two functions agree on the
 * smaller domain. So the relationship is the one between the accepted languages, provided every
 * string both accept takes the same value under each, and `Incomparable` otherwise, since a string
 * valued differently puts a pair in each graph that the other lacks.
 *
 * Value agreement is asked of the values and never of the canonical forms. `(A|B)!A` and `(A|B)!B`
 * print `B` differently and value it alike, and their encodings are equal. Where neither naxp
 * holds a unified element the canonical machines are the accepted ones and `rank-agreement.js`
 * decides exactly. Otherwise its disagreement is still exact, because a shared canonical string is
 * fixed by both canonicalisations, and it is tried first for that reason; its agreement says
 * nothing about the strings a unified element rewrites, which `value-agreement.js` then decides
 * over parses.
 *
 * This is the one axis of the comparison that can fail to be decided, when a walk outgrows its
 * budget. It then reports `decided` as false and leaves the relationship as `Incomparable`, which
 * is the value that claims nothing.
 *
 * @param {import('./compiler.js').Compilation} a The first naxp.
 * @param {import('./compiler.js').Compilation} b The second naxp.
 * @param {number} [budget] How many product states either walk may build.
 * @returns {{decided: boolean, relationship: string}} Whether it was decided, and the
 * relationship from `a`'s point of view as one of {@link SetRelationship}.
 */
export function tryCompareEncodings(a, b, budget = MAX_TUPLES) {
	// With no string held in common, or with each holding strings the other lacks, the graphs are
	// incomparable before any value is looked at.
	const languages = compareLanguages(a.accepted, b.accepted);

	if (languages === SetRelationship.Incomparable) {
		return { decided: true, relationship: SetRelationship.Incomparable };
	}

	const ranks = compareRanks(a.canonical, b.canonical, budget);

	if (ranks === Agreement.Differs) {
		return { decided: true, relationship: SetRelationship.Incomparable };
	}

	// Every accepted string is canonical where ρ is the identity on both sides, so the rank walk
	// has already seen them all.
	const values = a.canonicalIsIdentity && b.canonicalIsIdentity
		? ranks
		: compareValues(a, b, budget).agreement;

	if (values === Agreement.Undecided) {
		return { decided: false, relationship: SetRelationship.Incomparable };
	}

	return {
		decided: true,
		relationship: values === Agreement.Agrees ? languages : SetRelationship.Incomparable,
	};
}

/**
 * A state's enumeration as chunks in order: the empty string, then one chunk per character in
 * class order and ASCII order within a class. A null state marks the end of text.
 *
 * @param {import('./state-map.js').State} state The state.
 * @returns {Array<{code: number, next: import('./state-map.js').State | null}>} The chunks.
 */
function chunksOf(state) {
	const chunks = [];

	for (const transition of state.transitions) {
		if (transition.set.isEmpty) { chunks.push({ code: 0, next: null }); continue; }

		for (const code of transition.set) { chunks.push({ code, next: transition.next }); }
	}

	if (state.isTerminal) { chunks.push({ code: 0, next: null }); }

	return chunks;
}

/**
 * The lowest value both machines hold that they decode to different strings, or zero where every
 * value both hold decodes alike.
 *
 * This is a different question from the encoding relation. `(A|B)!A` and `(A|B)!B` give every
 * string the same value and decode value 1 to `A` and to `B`; equal encodings, divergent at 1. A
 * naxp extended by values that sort after all its own decodes every value it had as before, so the
 * answer is zero however many were added, and the count of values says the rest.
 *
 * The walk enumerates both canonical languages in the order the specification defines, the empty
 * string first and then each first class in set order, each character of it in ASCII order and
 * each continuation in turn, and compares the two enumerations position by position. Where two
 * continuations are reached by the same character the comparison recurses and a whole chunk is
 * stepped over by its count, which is what keeps the walk linear in the product of the machines
 * rather than in the number of values. The result at a pair of states does not depend on how the
 * pair was reached, so it is memoised.
 *
 * Counts are trusted, which W5 guarantees for a canonical machine: the count of values is capped
 * at 2^64 - 1, so no count here is saturated.
 *
 * @param {import('./state-map.js').StateMap} a The first canonical machine.
 * @param {import('./state-map.js').StateMap} b The second canonical machine.
 * @returns {bigint} The value, or zero for none.
 */
export function firstDivergentValue(a, b) {
	/** @type {Map<string, {found: boolean, index: bigint}>} */
	const memo = new Map();

	/**
	 * Whether the two enumerations differ within the length of the shorter, and where.
	 *
	 * @param {import('./state-map.js').State} p The state in `a`.
	 * @param {import('./state-map.js').State} q The state in `b`.
	 * @returns {{found: boolean, index: bigint}} The verdict.
	 */
	const diverge = (p, q) => {
		const key = `${p.id},${q.id}`;
		const known = memo.get(key);

		if (known !== undefined) { return known; }

		const found = compareChunks(p, q);

		memo.set(key, found);

		return found;
	};

	/**
	 * @param {import('./state-map.js').State} p The state in `a`.
	 * @param {import('./state-map.js').State} q The state in `b`.
	 * @returns {{found: boolean, index: bigint}} The verdict.
	 */
	const compareChunks = (p, q) => {
		const left = chunksOf(p);
		const right = chunksOf(q);
		let i = 0;
		let j = 0;
		let offset = 0n;

		while (i < left.length && j < right.length) {
			const x = left[i];
			const y = right[j];

			// Both end here: one value each, the same string.
			if (x.next === null && y.next === null) {
				++offset;
				++i;
				++j;
				continue;
			}

			// One ends where the other reads on, or they read different characters, so the
			// strings at this position differ in this very character.
			if (x.next === null || y.next === null || x.code !== y.code) {
				return { found: true, index: offset };
			}

			const inner = diverge(x.next, y.next);

			if (inner.found) { return { found: true, index: offset + inner.index }; }

			const leftCount = x.next.stringCount;
			const rightCount = y.next.stringCount;

			if (leftCount === rightCount) {
				offset += leftCount;
				++i;
				++j;
				continue;
			}

			// The shorter continuation ran out inside the chunk. The longer side's next string
			// still begins with this character; the shorter side's next chunk begins with another,
			// so they differ there, unless the shorter side has no next chunk and its whole
			// enumeration has ended.
			const shorterIsLeft = leftCount < rightCount;
			const shorterHasMore = shorterIsLeft ? i + 1 < left.length : j + 1 < right.length;

			if (shorterHasMore) {
				return { found: true, index: offset + (shorterIsLeft ? leftCount : rightCount) };
			}

			return { found: false, index: 0n };
		}

		// One enumeration ended without a difference, so there is none within the shorter.
		return { found: false, index: 0n };
	};

	const result = diverge(a.start, b.start);

	return result.found ? result.index + 1n : 0n;
}
