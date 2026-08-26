// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';
import { NaxpError } from './naxp-error.js';
import { NaxpMessage } from './naxp-message.js';
import { NaxpLimits } from './naxp-limits.js';
import { minterms } from './state-map.js';
import { COPY_MARKER, EotKind } from './tx.js';

/**
 * A transition of the canonicalisation machine.
 *
 * The sets of a state are disjoint, because they come from the minterms, so a walk can stop at the
 * first one that holds the character.
 */
export class TxTransition {
	/**
	 * @param {AsciiCharSet} set The characters.
	 * @param {string} output What reading a character of the set emits. A {@link COPY_MARKER} in
	 * it stands for the character just read, which is what lets a whole set share one transition
	 * rather than needing one per character.
	 * @param {TxState} next The state reached.
	 */
	constructor(set, output, next) {
		this.set = set;
		this.output = output;
		this.next = next;
	}
}

/**
 * A state of the canonicalisation machine.
 *
 * A state stands for a set of parses that agree on everything emitted so far, each carrying
 * whatever it has emitted beyond their common prefix. Two states are the same object when those
 * sets are equal.
 *
 * That is sharing on the construction, not on behaviour, so it is weaker than what the acceptor
 * gives. The acceptor is the minimal machine because it is acyclic and hash-consed on what a state
 * does; this one can hold two states that behave alike because their branch sets differ.
 * `A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)` builds eight states where five would do, which is what the
 * merging pass afterwards is for.
 */
export class TxState {
	/**
	 * @param {number} id The state's number.
	 */
	constructor(id) {
		this.id = id;

		/**
		 * The transitions, sorted by the set order. Filled after every state object exists,
		 * because a transition names its target and a target may have been discovered before the
		 * state that reaches it.
		 *
		 * @type {TxTransition[]}
		 */
		this.transitions = [];

		/**
		 * What is emitted where the input ends here, or null where it may not.
		 *
		 * This is never empty of meaning: a replaceable element that has consumed its subject
		 * emits its whole rendering at this point, so the machine can emit more after the last
		 * character than it did on any transition.
		 *
		 * @type {string | null}
		 */
		this.endOutput = null;
	}

	/** Whether the input may end here. */
	get acceptsEndOfText() {
		return this.endOutput !== null;
	}
}

/**
 * The canonicalisation ρ as a machine, so that it can be walked rather than recursed over the
 * tree, and emitted as a table or a switch in another language.
 *
 * The canonicaliser does the same job over the tree. That walk is the reference, and this is the
 * form the emitters need, because a tree walk has no table to emit. It is also linear in the
 * length of the input, which the tree walk is not.
 *
 * The construction is the classical determinisation of a transducer: a state is a set of live
 * parses, each with the output it owes beyond what the others have already emitted, and a
 * transition emits the longest common prefix of what they all owe. That delay is needed because a
 * replaceable element emits nothing until it completes, so two branches can disagree about what
 * has been emitted for as long as the input has not yet told them apart.
 *
 * The state count can be **exponential in the length of the naxp**, even where both language
 * machines are small. `[ab]{k}c|([ab]!a){k}d` builds exactly 2^(k+1) states, because nothing
 * before the final character says which branch was taken. That is not a weakness of this
 * construction: the lower bound in `encoding/w3-functionality.md` holds for any finite-state
 * machine that emits ρ as it reads.
 */
export class TxMachine {
	/**
	 * @param {TxState} start The start state.
	 * @param {TxState[]} states Every state.
	 */
	constructor(start, states) {
		this.start = start;
		this.states = states;
	}

	/**
	 * The canonical form of a string, which is the string with each replaceable element replaced
	 * by its rendering.
	 *
	 * @param {string} text The string, which must be one the accepted language holds.
	 * @returns {string | null} The canonical form, or null where the string is not accepted.
	 */
	tryCanonicalise(text) {
		const parts = [];
		let state = this.start;

		for (let i = 0; i < text.length; ++i) {
			const code = text.charCodeAt(i);
			let next = null;

			for (const transition of state.transitions) {
				if (!transition.set.contains(code)) { continue; }

				parts.push(resolveOutput(transition.output, text[i]));
				next = transition.next;
				break;
			}

			if (next === null) { return null; }

			state = next;
		}

		if (state.endOutput === null) { return null; }

		parts.push(state.endOutput);

		return parts.join('');
	}
}

/**
 * A transition's output, with the copy marker resolved to the character read.
 *
 * @param {string} output The output.
 * @param {string} read The character read.
 * @returns {string} The resolved output.
 */
function resolveOutput(output, read) {
	if (!output.includes(COPY_MARKER)) { return output; }

	return output.split(COPY_MARKER).join(read);
}

/**
 * One live parse: what is left to consume, and what it owes beyond the others.
 */
class Branch {
	/**
	 * @param {import('./tx.js').Tx} residual What is left to consume.
	 * @param {string} pending What this parse has emitted that the machine has not. Never holds a
	 * copy marker: the builder narrows a block to single characters rather than carry one past
	 * the step that read it, since nothing downstream could resolve it.
	 */
	constructor(residual, pending) {
		this.residual = residual;
		this.pending = pending;
	}

	/** @returns {string} A string equal for equal branches. */
	key() {
		return `${this.residual.id}|${this.pending}`;
	}
}

/**
 * Builds a {@link TxMachine} from a transduction by determinisation.
 *
 * The single-valuedness refusals here duplicate the W3 checker, which decides the same question
 * over the same derivatives, so on an expression the checker has passed they are unreachable. They
 * are kept as defence in depth, because the two walk different shapes — the checker walks pairs,
 * this walks sets — and a machine built from an unchecked expression would otherwise be silently
 * wrong rather than refused.
 *
 * The state cap is **not** a duplicate, and it is reachable on a naxp that is entirely legal.
 * `[ab]{16}c|([ab]!a){16}d` passes every rule, compiles, and then has no machine.
 */
class Builder {
	/**
	 * @param {import('./tx.js').TxFactory} factory The factory that made the transduction.
	 * @param {number} maxStates The budget.
	 */
	constructor(factory, maxStates) {
		this.factory = factory;
		this.maxStates = maxStates;

		/** @type {Map<string, number>} */
		this.indexOf = new Map();
		/** @type {Branch[][]} */
		this.branchSets = [];
		/** @type {Array<Array<{set: AsciiCharSet, output: string, next: number}>>} */
		this.transitionsOf = [];
		/** @type {Array<string | null>} */
		this.endOutputOf = [];
	}

	/**
	 * @param {import('./tx.js').Tx} root The transduction.
	 * @returns {{machine: TxMachine | null, error: NaxpError | null}} The machine, or the failure.
	 */
	run(root) {
		const queue = [];
		const start = this.add([new Branch(root, '')], queue);

		if (start.error !== null) { return { machine: null, error: start.error }; }

		let head = 0;

		while (head < queue.length) {
			const index = queue[head++];
			const ended = this.setEndOutput(index);

			if (ended !== null) { return { machine: null, error: ended }; }

			const explored = this.explore(index, queue);

			if (explored !== null) { return { machine: null, error: explored }; }
		}

		return { machine: merge(this.materialise(start.index)), error: null };
	}

	/**
	 * Records what the state emits where the input ends, refusing where the parses disagree.
	 *
	 * @param {number} index The state.
	 * @returns {NaxpError | null} The refusal, or null.
	 */
	setEndOutput(index) {
		let endOutput = null;

		for (const branch of this.branchSets[index]) {
			const eot = branch.residual.getEot();

			if (eot.kind === EotKind.None) { continue; }

			if (eot.kind === EotKind.TooLong) { return tooLarge(this.maxStates); }

			if (eot.kind === EotKind.Multiple) { return violation(); }

			if (eot.kind !== EotKind.Single) {
				throw new Error(`Unhandled kind ${eot.kind}.`);
			}

			const candidate = branch.pending + eot.text;

			if (endOutput === null) { endOutput = candidate; }
			else if (endOutput !== candidate) { return violation(); }
		}

		this.endOutputOf[index] = endOutput;

		return null;
	}

	/**
	 * Follows every block of characters the state can read.
	 *
	 * @param {number} index The state.
	 * @param {number[]} queue The queue to append to.
	 * @returns {NaxpError | null} The refusal, or null.
	 */
	explore(index, queue) {
		const firstSets = [];

		for (const branch of this.branchSets[index]) {
			firstSets.push(...branch.residual.getFirstSets());
		}

		if (firstSets.length === 0) { return null; }

		for (const block of minterms(firstSets)) {
			const error = this.step(index, block, queue);

			if (error !== null) { return error; }
		}

		return null;
	}

	/**
	 * Takes one step, narrowing the block to single characters where what is emitted would
	 * otherwise stay undecided past this step.
	 *
	 * @param {number} index The state.
	 * @param {AsciiCharSet} block The block to step by.
	 * @param {number[]} queue The queue to append to.
	 * @returns {NaxpError | null} The refusal, or null.
	 */
	step(index, block, queue) {
		/** @type {Map<import('./tx.js').Tx, string>} */
		const pendingOf = new Map();

		for (const branch of this.branchSets[index]) {
			const derivative = this.factory.derivative(branch.residual, block);

			if (derivative.tooLong) { return tooLarge(this.maxStates); }

			if (derivative.skipsAmbiguously) { return violation(); }

			for (const move of derivative.moves) {
				const pending = branch.pending + move.emitted;
				const existing = pendingOf.get(move.residual);

				if (existing === undefined) { pendingOf.set(move.residual, pending); }
				else if (existing !== pending) {
					// Same continuation, two outputs. Every string the continuation accepts would
					// have two canonical forms.
					return violation();
				}
			}
		}

		if (pendingOf.size === 0) { return null; }

		const pendings = [...pendingOf.values()];
		const common = longestCommonPrefix(pendings);

		if (carriesUndecidedCopy(pendings, common.length)) {
			if (block.singleCharacter !== null) {
				// A single character block decides every copy, so this cannot recur.
				throw new Error('A copy stayed undecided on a single character.');
			}

			for (const code of block) {
				const error = this.step(index, AsciiCharSet.fromSingleChar(code), queue);

				if (error !== null) { return error; }
			}

			return null;
		}

		const branches = [...pendingOf].map(
			([residual, pending]) => new Branch(residual, pending.slice(common.length)));

		branches.sort((left, right) => (left.residual.id - right.residual.id)
			|| compareOrdinal(left.pending, right.pending));

		const next = this.add(branches, queue);

		if (next.error !== null) { return next.error; }

		this.transitionsOf[index].push({ set: block, output: common, next: next.index });

		return null;
	}

	/**
	 * Finds a state, adding it and queueing it where it is new.
	 *
	 * @param {Branch[]} branches The branch set, already sorted and deduplicated.
	 * @param {number[]} queue The queue to append to.
	 * @returns {{index: number, error: NaxpError | null}} Its index, or the refusal.
	 */
	add(branches, queue) {
		const key = branches.map(branch => branch.key()).join(';');
		const existing = this.indexOf.get(key);

		if (existing !== undefined) { return { index: existing, error: null }; }

		if (this.branchSets.length >= this.maxStates) {
			return { index: -1, error: tooLarge(this.maxStates) };
		}

		const index = this.branchSets.length;

		this.branchSets.push(branches);
		this.transitionsOf.push([]);
		this.endOutputOf.push(null);
		this.indexOf.set(key, index);

		queue.push(index);

		return { index, error: null };
	}

	/**
	 * Turns the recorded indices into linked state objects.
	 *
	 * @param {number} start The start state's index.
	 * @returns {TxMachine} The machine.
	 */
	materialise(start) {
		const states = this.branchSets.map((_, i) => {
			const state = new TxState(i);

			state.endOutput = this.endOutputOf[i];

			return state;
		});

		for (let i = 0; i < states.length; ++i) {
			const transitions = this.transitionsOf[i].map(
				pending => new TxTransition(pending.set, pending.output, states[pending.next]));

			transitions.sort((left, right) => left.set.compareTo(right.set));

			states[i].transitions = transitions;
		}

		return new TxMachine(states[start], states);
	}
}

/**
 * @param {string[]} pendings The outputs owed.
 * @returns {string} Their longest common prefix.
 */
function longestCommonPrefix(pendings) {
	let shortest = null;

	for (const pending of pendings) {
		if (shortest === null || pending.length < shortest.length) { shortest = pending; }
	}

	let common = shortest.length;

	for (const pending of pendings) {
		let at = 0;

		while (at < common && pending[at] === shortest[at]) { ++at; }

		common = at;

		if (common === 0) { break; }
	}

	return shortest.slice(0, common);
}

/**
 * Whether any parse would carry a copy marker past this step, where nothing could later say which
 * character it stood for.
 *
 * @param {string[]} pendings The outputs owed.
 * @param {number} from Where the committed prefix ends.
 * @returns {boolean} Whether one would be carried.
 */
function carriesUndecidedCopy(pendings, from) {
	for (const pending of pendings) {
		if (pending.indexOf(COPY_MARKER, from) >= 0) { return true; }
	}

	return false;
}

/**
 * @param {string} left The first string.
 * @param {string} right The second string.
 * @returns {number} Their ordinal order.
 */
function compareOrdinal(left, right) {
	if (left === right) { return 0; }

	return left < right ? -1 : 1;
}

/** @returns {NaxpError} The refusal. */
function violation() {
	return new NaxpError(NaxpMessage.NAXP1045_ReplacementNotSingleValued);
}

/**
 * @param {number} maxStates The budget.
 * @returns {NaxpError} The refusal.
 */
function tooLarge(maxStates) {
	return new NaxpError(NaxpMessage.NAXP1050_TooManyCanonicalStates);
}

/**
 * Merges states of a built machine that behave alike.
 *
 * The builder shares a state only where two branch sets are equal, which is a property of the
 * construction rather than of behaviour, so it can leave two states that do the same thing.
 * `A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)` is the smallest witness found: eight states built, five after
 * this pass.
 *
 * The machine is acyclic, so a post-order walk reaches every successor before the state that
 * reaches it, and one bottom-up sweep suffices. A state is keyed on what it emits at end of text
 * and on its transitions once their targets have been replaced by the representatives already
 * chosen, which is the usual hash-consing. Merging targets can leave two transitions agreeing on
 * output and target, and those are unioned, which is safe because their sets were disjoint.
 *
 * This makes the machine smaller. It does not make it canonical the way the acceptor is: that
 * would need an onward normalisation of where output is emitted, which nothing downstream asks
 * for.
 *
 * @param {TxMachine} machine The machine.
 * @returns {TxMachine} The merged machine.
 */
function merge(machine) {
	const order = postOrder(machine.start);

	/** @type {Map<TxState, TxState>} */
	const representative = new Map();
	/** @type {Map<string, TxState>} */
	const canonical = new Map();
	const merged = [];

	for (const state of order) {
		const rebuilt = [];

		for (const transition of state.transitions) {
			const target = representative.get(transition.next);
			const at = rebuilt.findIndex(
				candidate => candidate.next === target && candidate.output === transition.output);

			if (at >= 0) {
				rebuilt[at] = new TxTransition(
					rebuilt[at].set.union(transition.set),
					transition.output,
					target);
			} else {
				rebuilt.push(new TxTransition(transition.set, transition.output, target));
			}
		}

		rebuilt.sort((left, right) => left.set.compareTo(right.set));

		const key = mergedKey(state.endOutput, rebuilt);
		const existing = canonical.get(key);

		if (existing !== undefined) {
			representative.set(state, existing);
			continue;
		}

		const created = new TxState(merged.length);

		created.endOutput = state.endOutput;
		created.transitions = rebuilt;

		merged.push(created);
		canonical.set(key, created);
		representative.set(state, created);
	}

	return new TxMachine(representative.get(machine.start), merged);
}

/**
 * What makes two states the same once their successors have been merged.
 *
 * @param {string | null} endOutput What the state emits at end of text.
 * @param {TxTransition[]} transitions Its transitions, with merged targets.
 * @returns {string} The key.
 */
function mergedKey(endOutput, transitions) {
	const head = endOutput === null ? '~' : JSON.stringify(endOutput);
	const rest = transitions.map(
		transition => `${transition.set.key()}>${JSON.stringify(transition.output)}`
			+ `>${transition.next.id}`);

	return `${head}|${rest.join(';')}`;
}

/**
 * Post-order, so that every successor is ordered before the state that reaches it.
 *
 * Iterative rather than recursive. A naxp is allowed to be a long chain — `(\A!A){99}` is legal,
 * linear and ten thousand states — and recursing over that overflows the stack, which cannot be
 * caught.
 *
 * @param {TxState} start The start state.
 * @returns {TxState[]} The states, successors first.
 */
function postOrder(start) {
	const order = [];
	const seen = new Set([start]);
	const pending = [{ state: start, index: 0 }];

	while (pending.length > 0) {
		const step = pending.pop();

		if (step.index === step.state.transitions.length) {
			// Every successor has been finished, so this state may be finished too.
			order.push(step.state);
			continue;
		}

		pending.push({ state: step.state, index: step.index + 1 });

		const next = step.state.transitions[step.index].next;

		if (!seen.has(next)) {
			seen.add(next);
			pending.push({ state: next, index: 0 });
		}
	}

	return order;
}

/**
 * Builds the machine for a transduction.
 *
 * @param {import('./tx.js').Tx} root The transduction.
 * @param {import('./tx.js').TxFactory} factory The factory that made it, whose derivative cache is
 * reused.
 * @param {number} [maxStates] The budget, lowered by tests so the cap can be reached cheaply.
 * @returns {{machine: TxMachine | null, error: NaxpError | null}} The machine, or the failure.
 */
export function tryBuildTxMachine(root, factory, maxStates = NaxpLimits.maxStates) {
	if (root === null || root === undefined) { throw new TypeError('root is required.'); }
	if (factory === null || factory === undefined) { throw new TypeError('factory is required.'); }

	return new Builder(factory, maxStates).run(root);
}
