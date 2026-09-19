// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';
import { Agreement, contribution } from './rank-agreement.js';
import { RxFactory } from './rx.js';
import { minterms } from './state-map.js';
import { COPY_MARKER, EotKind, TxFactory, convert } from './tx.js';

/**
 * How many product states the walk may build before giving up.
 *
 * The same figure as `MAX_PAIRS` in `rank-agreement.js`, for the same reason: a comparison worth
 * making is between two naxps that resemble one another, and none of the pairs anybody has reason
 * to compare has needed more than a few hundred.
 */
export const MAX_TUPLES = 200_000;

/**
 * Walks a concrete emission through a canonical machine, returning what it adds to the running
 * total and where it ends up.
 *
 * @param {import('./state-map.js').State} from The state to walk from.
 * @param {string} emitted The emission.
 * @returns {{added: bigint, to: import('./state-map.js').State}} The amount and the state.
 */
function walk(from, emitted) {
	let added = 0n;
	let state = from;

	for (const c of emitted) {
		if (c === COPY_MARKER) { throw new Error('An undecided copy reached the walk.'); }

		const code = c.charCodeAt(0);
		let taken = null;

		for (const transition of state.transitions) {
			if (!transition.set.isEmpty && transition.set.contains(code)) {
				taken = transition;
				break;
			}
		}

		// A partial parse's output is a prefix of a canonical string, so it cannot fall off the
		// machine of the canonical language.
		if (taken === null) {
			throw new Error('A canonical form is not accepted by its own machine.');
		}

		added += contribution(state, taken, code);
		state = taken.next;
	}

	return { added, to: state };
}

/**
 * Whether any of a derivative's moves emits a character whose identity the block has not fixed.
 *
 * @param {import('./tx.js').TxDerivative} derivative The moves.
 * @returns {boolean} Whether one copies.
 */
function copies(derivative) {
	for (const move of derivative.moves) {
		if (move.emitted.includes(COPY_MARKER)) { return true; }
	}

	return false;
}

/**
 * Explores the product states reachable on a common input, reporting the first shared string the
 * two value differently.
 */
class Product {
	/**
	 * @param {import('./compiler.js').Compilation} a The first naxp.
	 * @param {import('./compiler.js').Compilation} b The second naxp.
	 * @param {TxFactory} leftFactory The first naxp's transducer factory.
	 * @param {TxFactory} rightFactory The second naxp's transducer factory.
	 * @param {number} maxTuples The budget.
	 */
	constructor(a, b, leftFactory, rightFactory, maxTuples) {
		this.a = a;
		this.b = b;
		this.leftFactory = leftFactory;
		this.rightFactory = rightFactory;
		this.maxTuples = maxTuples;

		/** @type {Map<string, number>} */
		this.indexOf = new Map();
		/** @type {Array<{left: import('./tx.js').Tx, right: import('./tx.js').Tx,
		 *   leftState: import('./state-map.js').State, rightState: import('./state-map.js').State}>} */
		this.keys = [];
		/** @type {bigint[]} */
		this.differences = [];
		/** @type {number[]} */
		this.parents = [];
		/** @type {number[]} */
		this.arrivals = [];
		/** @type {Array<{existing: number, parent: number, arrival: number}>} */
		this.conflicts = [];
		/** @type {Map<string, string | null>} */
		this.completions = new Map();
	}

	/**
	 * @param {import('./tx.js').Tx} left The first naxp's transduction.
	 * @param {import('./tx.js').Tx} right The second naxp's transduction.
	 * @returns {{agreement: string, witness: string | null}} The verdict, and where they differ.
	 */
	run(left, right) {
		const start = this.add(
			{ left, right, leftState: this.a.canonical.start, rightState: this.b.canonical.start },
			0n,
			-1,
			0);

		const queue = [start];
		let head = 0;

		while (head < queue.length) {
			const index = queue[head++];
			const ending = this.atEndOfText(index);

			if (ending === Agreement.Differs) {
				return { agreement: Agreement.Differs, witness: this.path(index) };
			}

			if (ending === Agreement.Undecided) {
				return { agreement: Agreement.Undecided, witness: null };
			}

			const key = this.keys[index];
			const sets = [...key.left.getFirstSets(), ...key.right.getFirstSets()];

			if (sets.length === 0) { continue; }

			// `minterms` returns a fresh list, so narrowing, which re-enters the step, cannot
			// disturb the one being iterated.
			for (const block of minterms(sets)) {
				if (!this.tryStep(index, block, queue)) {
					return { agreement: Agreement.Undecided, witness: null };
				}
			}
		}

		// A differing arrival matters only where the two residuals still share a string, so that
		// the differing totals are ever compared. Where they do, the two paths through the state
		// finish at different differences, so at least one of them is a shared string the two
		// value differently; but only one need be, and which is not known without the rest of the
		// walk, so each is asked the direct way.
		for (const { existing, parent, arrival } of this.conflicts) {
			const key = this.keys[existing];
			const completion = this.completion(key.left, key.right);

			if (completion === null) { continue; }

			let first = this.path(existing) + completion;
			let second = this.path(parent) + String.fromCharCode(arrival) + completion;

			if (second.length < first.length) { [first, second] = [second, first]; }

			if (this.a.encode(first) !== this.b.encode(first)) {
				return { agreement: Agreement.Differs, witness: first };
			}

			if (this.a.encode(second) !== this.b.encode(second)) {
				return { agreement: Agreement.Differs, witness: second };
			}

			throw new Error('Two arrivals differed and neither is a witness.');
		}

		return { agreement: Agreement.Agrees, witness: null };
	}

	/**
	 * Whether both sides can end the input here with different values.
	 *
	 * @param {number} index The product state.
	 * @returns {string} One of {@link Agreement}.
	 */
	atEndOfText(index) {
		const key = this.keys[index];

		if (!key.left.isNullable || !key.right.isNullable) { return Agreement.Agrees; }

		const left = key.left.getEot();
		const right = key.right.getEot();

		if (left.kind === EotKind.TooLong || right.kind === EotKind.TooLong) {
			return Agreement.Undecided;
		}

		if (left.kind !== EotKind.Single || right.kind !== EotKind.Single) {
			throw new Error('A compiled naxp emits more than one thing at end of text.');
		}

		// Ending emits whatever the residual still owes, which the canonical machine has to be
		// walked over. The end of text transition itself contributes nothing on either side, and
		// the one that turns a total into a value cancels.
		const leftWalk = walk(key.leftState, left.text);
		const rightWalk = walk(key.rightState, right.text);

		if (!leftWalk.to.acceptsEndOfText || !rightWalk.to.acceptsEndOfText) {
			throw new Error('A canonical form is not accepted by its own machine.');
		}

		return this.differences[index] + leftWalk.added - rightWalk.added === 0n
			? Agreement.Agrees
			: Agreement.Differs;
	}

	/**
	 * Takes one step of the input, narrowing the block to single characters where either side
	 * would copy one.
	 *
	 * A copied character has to be known before it can be walked through a canonical machine,
	 * since the rank it contributes depends on which character it is. That is the only reason to
	 * narrow. The W3 checker can let two identical copies cancel because it compares them with
	 * each other; here they are walked through two different machines, so they cannot.
	 *
	 * @param {number} index The product state.
	 * @param {AsciiCharSet} block The block to step by.
	 * @param {number[]} queue The queue to append to.
	 * @returns {boolean} Whether the step was decided.
	 */
	tryStep(index, block, queue) {
		const key = this.keys[index];
		const left = this.leftFactory.derivative(key.left, block);
		const right = this.rightFactory.derivative(key.right, block);

		if (left.tooLong || right.tooLong) { return false; }

		if (left.skipsAmbiguously || right.skipsAmbiguously) {
			throw new Error('A compiled naxp skips ambiguously.');
		}

		// One side cannot consume this block, so no shared string passes through it.
		if (left.moves.length === 0 || right.moves.length === 0) { return true; }

		if (block.singleCharacter === null && (copies(left) || copies(right))) {
			for (const code of block) {
				if (!this.tryStep(index, AsciiCharSet.fromSingleChar(code), queue)) { return false; }
			}

			return true;
		}

		const arrival = block.singleCharacter ?? block.characterAt(0);
		const difference = this.differences[index];

		for (const leftMove of left.moves) {
			for (const rightMove of right.moves) {
				const leftWalk = walk(key.leftState, leftMove.emitted);
				const rightWalk = walk(key.rightState, rightMove.emitted);
				const next = difference + leftWalk.added - rightWalk.added;
				const nextKey = {
					left: leftMove.residual,
					right: rightMove.residual,
					leftState: leftWalk.to,
					rightState: rightWalk.to,
				};

				const existing = this.indexOf.get(keyOf(nextKey));

				if (existing !== undefined) {
					if (this.differences[existing] !== next) {
						this.conflicts.push({ existing, parent: index, arrival });
					}

					continue;
				}

				if (this.keys.length >= this.maxTuples) { return false; }

				queue.push(this.add(nextKey, next, index, arrival));
			}
		}

		return true;
	}

	/**
	 * @param {{left: import('./tx.js').Tx, right: import('./tx.js').Tx,
	 *   leftState: import('./state-map.js').State,
	 *   rightState: import('./state-map.js').State}} key The product state.
	 * @param {bigint} difference The first side's running total less the second's.
	 * @param {number} parent The index it was reached from.
	 * @param {number} arrival The character code that reached it.
	 * @returns {number} Its index.
	 */
	add(key, difference, parent, arrival) {
		const index = this.keys.length;

		this.indexOf.set(keyOf(key), index);
		this.keys.push(key);
		this.differences.push(difference);
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
	path(index) {
		const codes = [];

		for (let at = index; this.parents[at] >= 0; at = this.parents[at]) {
			codes.unshift(this.arrivals[at]);
		}

		return String.fromCharCode(...codes);
	}

	/**
	 * A string both residuals accept, or null where there is none.
	 *
	 * Existence needs no narrowing: what a block emits varies by character, but whether it is
	 * consumed does not.
	 *
	 * @param {import('./tx.js').Tx} left The first residual.
	 * @param {import('./tx.js').Tx} right The second residual.
	 * @returns {string | null} The completion, or null.
	 */
	completion(left, right) {
		const key = `${left.id},${right.id}`;
		const known = this.completions.get(key);

		if (known !== undefined) { return known; }

		// Null while the answer is being worked out, so a pair cannot depend on itself.
		this.completions.set(key, null);

		let found = null;

		if (left.isNullable && right.isNullable) {
			found = '';
		} else {
			const sets = [...left.getFirstSets(), ...right.getFirstSets()];

			for (const block of minterms(sets)) {
				const leftDerivative = this.leftFactory.derivative(left, block);
				const rightDerivative = this.rightFactory.derivative(right, block);

				if (leftDerivative.moves.length === 0 || rightDerivative.moves.length === 0) {
					continue;
				}

				for (const leftMove of leftDerivative.moves) {
					for (const rightMove of rightDerivative.moves) {
						const rest = this.completion(leftMove.residual, rightMove.residual);

						if (rest !== null) {
							found = String.fromCharCode(block.characterAt(0)) + rest;
							break;
						}
					}

					if (found !== null) { break; }
				}

				if (found !== null) { break; }
			}
		}

		this.completions.set(key, found);

		return found;
	}
}

/**
 * A product state written out: where each parse has got to, and where each canonical machine has
 * got to on what that parse has emitted.
 *
 * The two transductions come from different factories, so identity is by side and id rather than
 * by reference alone; the same holds of the two machines.
 *
 * @param {{left: import('./tx.js').Tx, right: import('./tx.js').Tx,
 *   leftState: import('./state-map.js').State,
 *   rightState: import('./state-map.js').State}} key The product state.
 * @returns {string} A string equal for equal states.
 */
function keyOf(key) {
	return `${key.left.id}|${key.right.id}|${key.leftState.id}|${key.rightState.id}`;
}

/**
 * Whether `a` and `b` give the same value to every string both accept.
 *
 * This is the question the encoding relation turns on, and it is not the question of whether the
 * two canonicalise alike. `(A|B)!A` and `(A|B)!B` print different things for `B` and give it the
 * same value, because each rank is taken in its own canonical language. So canonical forms are
 * never compared here. Each side's output is fed into its own canonical machine as it is emitted,
 * and what is carried is the difference of the two running rank totals, as `rank-agreement.js`
 * carries it over two machines.
 *
 * A state is a pair of transduction residuals, one from each naxp, with the state each canonical
 * machine has reached on its side's output so far. Nothing is held back: a copied character is
 * fixed by narrowing the block to single characters before it is walked, a rendering is a fixed
 * string, and a skipped element's end of text output is a fixed string, so every emission is
 * concrete at the step that makes it. That is why there is no delay here where the W3 checker
 * needs one. The square compares two strings that arrive at different times; this compares two
 * numbers, and a number can be accumulated as its string arrives.
 *
 * The difference at a state is a property of the state, not of the path: from a state every
 * completion both sides accept has one output per side, by W3, so the rank each side adds over it
 * is fixed, and two arrivals with different differences cannot both finish at zero. A second and
 * unequal arrival is therefore a disagreement rather than a branch, with one proviso that
 * `rank-agreement.js` also makes: it counts only where the two residuals still share a completion.
 * That proviso is exercised constantly rather than rarely. The postcode against itself reaches the
 * same state by different inputs through cross pairs of parses, `\X?` taken against `\X?` skipped,
 * whose residuals want a digit and a letter next and so share nothing.
 *
 * The argument, the pairs it was tried on and the construction it replaces are in
 * `encoding/comparison-square-review.md`, with a harness in `encoding/comparison-check/`.
 *
 * @param {import('./compiler.js').Compilation} a The first naxp.
 * @param {import('./compiler.js').Compilation} b The second naxp.
 * @param {number} [maxTuples] How many product states to allow.
 * @returns {{agreement: string, witness: string | null}} The verdict, and where they differ, a
 * string both accept and value differently, the shortest the walk found.
 */
export function compare(a, b, maxTuples = MAX_TUPLES) {
	// Each side has its own factories. The sides are ordered and never compared for identity, so
	// there is nothing to gain from sharing and nothing to get wrong.
	const leftRxFactory = new RxFactory();
	const leftTxFactory = new TxFactory(leftRxFactory);
	const left = convert(a.ast, leftTxFactory, leftRxFactory);

	const rightRxFactory = new RxFactory();
	const rightTxFactory = new TxFactory(rightRxFactory);
	const right = convert(b.ast, rightTxFactory, rightRxFactory);

	return new Product(a, b, leftTxFactory, rightTxFactory, maxTuples).run(left, right);
}
