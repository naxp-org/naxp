// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';
import { containsReplaceable } from './ast.js';
import { NaxpError } from './naxp-error.js';
import { NaxpMessage } from './naxp-message.js';
import { NaxpLimits } from './naxp-limits.js';
import { minterms } from './state-map.js';
import { COPY_MARKER, EotKind, TxFactory, convert } from './tx.js';

/**
 * How far one branch's output runs ahead of the other's.
 *
 * At most one side is non-empty, because a common prefix is committed at every step. Where both
 * would be non-empty the outputs disagree at a position that is already fixed, so the delay
 * collapses to the mismatch mark and the strings stop mattering.
 */
class Delay {
	/**
	 * @param {string | null} left What the first branch has emitted beyond the second. Null when
	 * mismatched.
	 * @param {string | null} right What the second branch has emitted beyond the first.
	 */
	constructor(left, right) {
		this.left = left;
		this.right = right;
	}

	/** The two outputs already differ and can never agree again. */
	get isMismatch() {
		return this.left === null;
	}

	/**
	 * The delay after both branches have emitted, with their common prefix committed.
	 *
	 * @param {Delay} current The delay so far.
	 * @param {string} leftEmitted What the first branch emitted.
	 * @param {string} rightEmitted What the second branch emitted.
	 * @returns {Delay} The delay after.
	 */
	static after(current, leftEmitted, rightEmitted) {
		if (current.isMismatch) { return MISMATCH; }

		const left = current.left + leftEmitted;
		const right = current.right + rightEmitted;

		let common = 0;

		while (common < left.length && common < right.length && left[common] === right[common]) {
			++common;
		}

		// One of them is exhausted, or they differ here and will differ forever.
		if (common < left.length && common < right.length) { return MISMATCH; }

		return new Delay(left.slice(common), right.slice(common));
	}

	/** @returns {Delay} The delay with its two sides exchanged. */
	swapped() {
		return this.isMismatch ? MISMATCH : new Delay(this.right, this.left);
	}

	/** @returns {string} A string equal for equal delays. */
	key() {
		return this.isMismatch ? 'M' : `${JSON.stringify(this.left)}${JSON.stringify(this.right)}`;
	}
}

const MISMATCH = new Delay(null, null);
const NO_DELAY = new Delay('', '');

/**
 * A pair of live branches and the delay between them, as an unordered pair.
 */
class PairKey {
	/**
	 * @param {import('./tx.js').Tx} left The first branch.
	 * @param {import('./tx.js').Tx} right The second branch.
	 * @param {Delay} delay The delay between them.
	 */
	constructor(left, right, delay) {
		// The pair is unordered, so one orientation is chosen and the delay follows it.
		if (left.id <= right.id) {
			this.left = left;
			this.right = right;
			this.delay = delay;
		} else {
			this.left = right;
			this.right = left;
			this.delay = delay.swapped();
		}

		this.key = `${this.left.id}|${this.right.id}|${this.delay.key()}`;
	}
}

/**
 * Explores the pairs reachable on a common input, reporting the first that can accept with two
 * different outputs.
 */
class Square {
	/**
	 * @param {TxFactory} factory The factory that made the transduction.
	 * @param {number} maxStates The budget.
	 */
	constructor(factory, maxStates) {
		this.factory = factory;
		this.maxStates = maxStates;

		/** @type {Map<string, number>} */
		this.indexOf = new Map();
		/** @type {PairKey[]} */
		this.states = [];
		/** @type {number[]} */
		this.parents = [];
		/** @type {number[]} */
		this.arrivals = [];
	}

	/**
	 * @param {import('./tx.js').Tx} root The transduction.
	 * @returns {NaxpError | null} The violation, or null where there is none.
	 */
	run(root) {
		const start = this.add(new PairKey(root, root, NO_DELAY), -1, 0);
		const queue = [start];
		let head = 0;

		while (head < queue.length) {
			const index = queue[head++];
			const state = this.states[index];
			const { accepts, eotTooLong } = this.accepts(state);

			if (accepts) { return violation(this.witness(index)); }

			if (eotTooLong) { return abandoned(); }

			for (const block of this.blocks(state)) {
				const error = this.step(state, index, block, queue);

				if (error !== null) { return error; }
			}
		}

		return null;
	}

	/**
	 * Takes one step of the input, narrowing the block to single characters where what is emitted
	 * would otherwise stay undecided.
	 *
	 * @param {PairKey} state The pair.
	 * @param {number} index Its index.
	 * @param {AsciiCharSet} block The block to step by.
	 * @param {number[]} queue The queue to append to.
	 * @returns {NaxpError | null} The refusal, or null.
	 */
	step(state, index, block, queue) {
		const left = this.factory.derivative(state.left, block);
		const right = this.factory.derivative(state.right, block);

		if (left.tooLong || right.tooLong) { return abandoned(); }

		// Before the test for no moves, which an ambiguous skip can itself cause: the moves past
		// it are dropped because the verdict no longer depends on them.
		if (left.skipsAmbiguously || right.skipsAmbiguously) {
			return violation(this.witness(index) + String.fromCharCode(block.characterAt(0)));
		}

		if (left.moves.length === 0 || right.moves.length === 0) {
			// One side cannot consume this block, so there is no pair to follow. The other side's
			// own future is covered by its diagonal pair.
			return null;
		}

		if (this.needsNarrowing(state, left, right)) {
			for (const code of block) {
				const error = this.step(state, index, AsciiCharSet.fromSingleChar(code), queue);

				if (error !== null) { return error; }
			}

			return null;
		}

		const arrival = block.singleCharacter ?? block.characterAt(0);

		for (const leftMove of left.moves) {
			for (const rightMove of right.moves) {
				const next = new PairKey(
					leftMove.residual,
					rightMove.residual,
					Delay.after(state.delay, leftMove.emitted, rightMove.emitted));

				if (this.indexOf.has(next.key)) { continue; }

				if (this.states.length >= this.maxStates) { return this.tooLarge(); }

				queue.push(this.add(next, index, arrival));
			}
		}

		return null;
	}

	/**
	 * Whether this step has to be retried one character at a time.
	 *
	 * A character set emits the character read. Where the block holds more than one character
	 * that emission is not yet a known string, and comparing it against a rendering, or against a
	 * character copied at some other position, has no answer until the character is fixed. The
	 * one case that needs no narrowing is the common one: both branches emit the very same thing
	 * at the same step from equal delays, which cancels whatever the character turns out to be.
	 *
	 * @param {PairKey} state The pair.
	 * @param {import('./tx.js').TxDerivative} left The first branch's moves.
	 * @param {import('./tx.js').TxDerivative} right The second branch's moves.
	 * @returns {boolean} Whether to narrow.
	 */
	needsNarrowing(state, left, right) {
		const noDelay = state.delay.key() === NO_DELAY.key();

		for (const leftMove of left.moves) {
			for (const rightMove of right.moves) {
				const undecided = leftMove.emitted.includes(COPY_MARKER)
					|| rightMove.emitted.includes(COPY_MARKER);

				if (!undecided) { continue; }

				// Identical emissions from one step cancel exactly, whatever was read, but only
				// where there is no earlier delay to shift one against the other.
				if (noDelay && leftMove.emitted === rightMove.emitted) { continue; }

				return true;
			}
		}

		return false;
	}

	/**
	 * Whether both branches can accept here, with different outputs.
	 *
	 * @param {PairKey} state The pair.
	 * @returns {{accepts: boolean, eotTooLong: boolean}} The verdict.
	 */
	accepts(state) {
		if (!state.left.isNullable || !state.right.isNullable) {
			return { accepts: false, eotTooLong: false };
		}

		const left = state.left.getEot();
		const right = state.right.getEot();

		if (left.kind === EotKind.TooLong || right.kind === EotKind.TooLong) {
			return { accepts: false, eotTooLong: true };
		}

		// One residual with two end of text outputs is a violation on its own, which is how a
		// naxp such as 'A!?|A!!' is caught before any character is read.
		if (left.kind === EotKind.Multiple || right.kind === EotKind.Multiple) {
			return { accepts: true, eotTooLong: false };
		}

		// Both can accept, and their outputs already differ.
		if (state.delay.isMismatch) { return { accepts: true, eotTooLong: false }; }

		return {
			accepts: state.delay.left + left.text !== state.delay.right + right.text,
			eotTooLong: false,
		};
	}

	/**
	 * The blocks to step by: the minterms of both branches' first sets, refined so that every
	 * character appearing in a rendering stands alone.
	 *
	 * @param {PairKey} state The pair.
	 * @returns {AsciiCharSet[]} The blocks.
	 */
	blocks(state) {
		const sets = [...state.left.getFirstSets(), ...state.right.getFirstSets()];

		if (sets.length === 0) { return []; }

		let universe = AsciiCharSet.empty;

		for (const set of sets) { universe = universe.union(set); }

		for (const code of this.factory.renderingCharacters) {
			if (universe.contains(code)) { sets.push(AsciiCharSet.fromSingleChar(code)); }
		}

		return minterms(sets);
	}

	/**
	 * @param {PairKey} key The pair.
	 * @param {number} parent The index it was reached from.
	 * @param {number} arrival The character code that reached it.
	 * @returns {number} Its index.
	 */
	add(key, parent, arrival) {
		const index = this.states.length;

		this.indexOf.set(key.key, index);
		this.states.push(key);
		this.parents.push(parent);
		this.arrivals.push(arrival);

		return index;
	}

	/**
	 * The input that reaches a state, read back along the path that found it.
	 *
	 * @param {number} index The state.
	 * @returns {string} The input.
	 */
	witness(index) {
		const codes = [];

		for (let at = index; this.parents[at] >= 0; at = this.parents[at]) {
			codes.unshift(this.arrivals[at]);
		}

		return String.fromCharCode(...codes);
	}

	/** @returns {NaxpError} The refusal. */
	tooLarge() {
		return new NaxpError(NaxpMessage.NAXP1051_TooManyPairStates);
	}
}

/**
 * The decision was abandoned because an intermediate result grew too large, which is a different
 * thing from running out of pair states and must not claim to be that.
 *
 * @returns {NaxpError} The refusal.
 */
function abandoned() {
	return new NaxpError(NaxpMessage.NAXP1052_PairOutputAbandoned);
}

/**
 * @param {string} witness The input with two canonical forms.
 * @returns {NaxpError} The violation.
 */
function violation(witness) {
	return new NaxpError(NaxpMessage.NAXP1046_ReplacementNotSingleValuedWitness, witness);
}

/**
 * Checks W3: whether ρ is single valued, so that every accepted string has exactly one canonical
 * form and therefore exactly one value.
 *
 * W3 is a property of *pairs* of parses, so this tracks pairs and never sets. The obvious
 * alternative, a subset construction over sets of live branches, is a determinisation: it computes
 * ρ online, and for `[ab]{17}c|([ab]!a){17}d` that function provably needs 2^17 states even though
 * both of the naxp's machines have fewer than forty. Tracking pairs decides the same naxp in a few
 * dozen. The argument is in `encoding/w3-functionality.md`.
 *
 * A state is two residuals and a delay. The delay is what one branch has emitted beyond the other,
 * so at most one side of it is non-empty; the moment both are, the two outputs differ at a
 * position neither can revisit and the delay collapses to a mismatch, after which no output need
 * be tracked at all.
 *
 * The check is skipped outright for a naxp with no `!`, where ρ is the identity. That is the only
 * short-circuit: the by-eye rule in the specification is sufficient rather than necessary, and
 * putting an unproved condition in front of the decision is the mistake that
 * `encoding/canonicity.md` records.
 *
 * @param {import('./ast.js').Ast} ast The tree, which must already have passed W1 and W2.
 * @param {import('./rx.js').RxFactory} rxFactory The factory the machines will be built with,
 * reused for interning.
 * @param {{hasReplaceable?: boolean, maxStates?: number}} [options] `hasReplaceable` lets a
 * caller that already knows whether there is anything to check avoid walking the tree for it
 * twice. `maxStates` is lowered by tests so the cap can be reached cheaply.
 * @returns {NaxpError | null} The refusal, or null if the naxp passes.
 */
export function checkW3(ast, rxFactory, options = {}) {
	// Both arguments are checked before the tree is walked, so a bad call fails at once rather
	// than after the work.
	if (ast === null || ast === undefined) { throw new TypeError('ast is required.'); }
	if (rxFactory === null || rxFactory === undefined) {
		throw new TypeError('rxFactory is required.');
	}

	const hasReplaceable = options.hasReplaceable ?? containsReplaceable(ast);
	const maxStates = options.maxStates ?? NaxpLimits.maxStates;

	// Without a '!' the transduction is the identity, which is single valued for nothing.
	if (!hasReplaceable) { return null; }

	const factory = new TxFactory(rxFactory);

	return checkW3Transduction(convert(ast, factory, rxFactory), factory, maxStates);
}

/**
 * Checks a transduction that has already been built.
 *
 * The compiler needs the same transduction afterwards, to build the machine that canonicalises,
 * so it converts once and passes it to both rather than paying for the derivatives twice.
 *
 * @param {import('./tx.js').Tx} root The transduction.
 * @param {TxFactory} txFactory The factory that made it, whose derivative cache is reused.
 * @param {number} [maxStates] The budget, lowered by tests so the cap can be reached cheaply.
 * @returns {NaxpError | null} The violation, or null where there is none.
 */
export function checkW3Transduction(root, txFactory, maxStates = NaxpLimits.maxStates) {
	if (root === null || root === undefined) { throw new TypeError('root is required.'); }
	if (txFactory === null || txFactory === undefined) {
		throw new TypeError('txFactory is required.');
	}

	return new Square(txFactory, maxStates).run(root);
}
