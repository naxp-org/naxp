// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * Whether two machines give the same encoded value to every string they both hold.
 */
export const Agreement = Object.freeze({
	/** Every string both machines hold takes the same value under each. */
	Agrees: 'Agrees',

	/** At least one string both hold takes different values. */
	Differs: 'Differs',

	/**
	 * Not decided. Either a count saturated, so the arithmetic is not the true rank, or the walk
	 * outgrew its budget.
	 */
	Undecided: 'Undecided',
});

/**
 * How many state pairs the walk may visit before giving up.
 *
 * W6 caps each machine at 2000 states, so a product can in principle reach four million pairs.
 * This is far below that, because a comparison worth making is between two naxps that resemble
 * one another, and one that explodes is telling you they do not.
 */
export const MAX_PAIRS = 200_000;

/**
 * What taking one character adds to the running total, which is the count of strings the machine
 * passes over to reach it.
 *
 * The same arithmetic as `codec.encode`, and it has to stay the same: the transitions before the
 * one taken are skipped whole, and within the one taken the character's own position counts once
 * for each string its successor holds. `ValueAgreement` walks canonical outputs with it for the
 * same reason.
 *
 * @param {import('./state-map.js').State} state The state the character is read in.
 * @param {import('./state-map.js').Transition} taken The transition the character belongs to.
 * @param {number} code The character's code.
 * @returns {bigint} The amount added.
 */
export function contribution(state, taken, code) {
	let skipped = 0n;

	for (const transition of state.transitions) {
		const count = transition.next.stringCount;

		if (transition.set.equals(taken.set)) {
			return skipped + (count * BigInt(taken.set.indexOf(code)));
		}

		// An empty set is the end of text transition, which stands for one string.
		skipped += count * (transition.set.isEmpty ? 1n : BigInt(transition.set.count));
	}

	return skipped;
}

/**
 * Whether `a` and `b` give the same value to every string they both hold.
 *
 * A value is a rank: the count of strings that precede this one. Walking a machine accumulates
 * that count a character at a time, so walking two machines together and carrying the difference
 * of the two running totals says whether they will end up agreeing.
 *
 * The difference at a state pair is a property of the pair, not of the path that reached it. Two
 * strings that arrive at the same pair with different differences have the same completions
 * available to both machines from there, so whatever suffix takes one to an accepting pair takes
 * the other as well, and the two cannot both come out at zero. A second, unequal arrival is
 * therefore a disagreement rather than something to explore - which is what keeps this linear in
 * the size of the product rather than exponential in the alphabet.
 *
 * @param {import('./state-map.js').StateMap} a The first canonical machine.
 * @param {import('./state-map.js').StateMap} b The second canonical machine.
 * @param {number} [maxPairs] How many state pairs to allow.
 * @returns {string} One of {@link Agreement}.
 */
export function compare(a, b, maxPairs = MAX_PAIRS) {
	// A saturated count is a stand-in for the real one, so the arithmetic below would be comparing
	// two approximations rather than two ranks.
	if (a.countSaturated || b.countSaturated) { return Agreement.Undecided; }

	/** @type {Map<string, bigint>} */
	const difference = new Map();

	/** @type {Array<[import('./state-map.js').State, import('./state-map.js').State]>} */
	const conflicts = [];

	let outgrew = false;

	/**
	 * @param {import('./state-map.js').State} p The state in `a`.
	 * @param {import('./state-map.js').State} q The state in `b`.
	 * @param {bigint} diff The running total of `a` less that of `b`.
	 */
	const visit = (p, q, diff) => {
		if (outgrew) { return; }

		const key = `${p.id},${q.id}`;
		const seen = difference.get(key);

		if (seen !== undefined) {
			if (seen !== diff) { conflicts.push([p, q]); }

			return;
		}

		if (difference.size >= maxPairs) { outgrew = true; return; }

		difference.set(key, diff);

		// Both ending here is a string they share, and the value each gives it is the running
		// total plus one, so the difference decides it.
		if (p.acceptsEndOfText && q.acceptsEndOfText && diff !== 0n) {
			conflicts.push([p, q]);
		}

		for (const left of p.transitions) {
			if (left.set.isEmpty) { continue; }

			for (const right of q.transitions) {
				if (right.set.isEmpty) { continue; }

				const shared = left.set.intersect(right.set);

				if (shared.isEmpty) { continue; }

				// The rank a character contributes depends on where it sits in its own set, so
				// each shared character is its own step. They agree on the pair they move to,
				// which is what the memo above collapses them onto.
				for (const code of shared) {
					visit(
						left.next,
						right.next,
						diff + contribution(p, left, code) - contribution(q, right, code));
				}
			}
		}
	};

	visit(a.start, b.start, 0n);

	if (outgrew) { return Agreement.Undecided; }

	// A conflict only matters where the two machines still share a string from that pair. Where
	// the shared language dies out below it, nothing reaches an accepting pair and the differing
	// totals are never compared.
	/** @type {Map<string, boolean>} */
	const reaches = new Map();

	/**
	 * Whether the two states still hold a string in common, which is what makes a difference
	 * between their totals something anybody can observe.
	 *
	 * @param {import('./state-map.js').State} p The state in `a`.
	 * @param {import('./state-map.js').State} q The state in `b`.
	 * @returns {boolean} Whether some string is held from both.
	 */
	const sharesAnEnding = (p, q) => {
		const key = `${p.id},${q.id}`;
		const known = reaches.get(key);

		if (known !== undefined) { return known; }

		// False while the answer is being worked out, so a pair cannot depend on itself.
		reaches.set(key, false);

		let found = p.acceptsEndOfText && q.acceptsEndOfText;

		if (!found) {
			for (const left of p.transitions) {
				if (left.set.isEmpty || found) { continue; }

				for (const right of q.transitions) {
					if (right.set.isEmpty) { continue; }

					if (left.set.intersectsWith(right.set) && sharesAnEnding(left.next, right.next)) {
						found = true;
						break;
					}
				}
			}
		}

		reaches.set(key, found);

		return found;
	};

	for (const [p, q] of conflicts) {
		if (sharesAnEnding(p, q)) { return Agreement.Differs; }
	}

	return Agreement.Agrees;
}
