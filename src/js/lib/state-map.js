// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';
import { NaxpError } from './naxp-error.js';
import { NaxpMessage } from './naxp-message.js';
import { NaxpLimits } from './naxp-limits.js';
import { RxKind } from './rx.js';

/**
 * A character set paired with the state reached by consuming one of its characters.
 *
 * An empty set is the end of text transition. The empty set is least in the set order, so it
 * sorts first and needs no special handling.
 */
export class Transition {
	/**
	 * @param {AsciiCharSet} set The characters.
	 * @param {State} next The state reached.
	 */
	constructor(set, next) {
		this.set = set;
		this.next = next;
	}
}

/**
 * A state of the machine, which stands for a language.
 *
 * States are shared: two languages that are equal give the same object. That is what makes the
 * machine the minimal one and the encoding a property of the language rather than of the
 * spelling.
 */
export class State {
	/**
	 * @param {number} id The state's number.
	 * @param {Transition[]} transitions Sorted by the set order, end of text first where present.
	 * @param {bigint} valueCount The count of strings the state's language holds, saturated at
	 * 2^64 - 1.
	 */
	constructor(id, transitions, valueCount) {
		this.id = id;
		this.transitions = transitions;
		this.valueCount = valueCount;
	}

	/** Whether this is the terminal state, whose language is the empty string alone. */
	get isTerminal() {
		return this.transitions.length === 0;
	}

	/** Whether the language holds the empty string. */
	get acceptsEndOfText() {
		return this.isTerminal || this.transitions[0].set.isEmpty;
	}
}

/**
 * The machine for one of a naxp's languages.
 */
export class StateMap {
	/**
	 * @param {State} start The start state.
	 * @param {State[]} states Every state.
	 * @param {boolean} countSaturated Whether the true count exceeds 2^64 - 1, in which case
	 * `valueCount` is that limit rather than the count.
	 */
	constructor(start, states, countSaturated) {
		this.start = start;
		this.states = states;
		this.countSaturated = countSaturated;
	}

	/** The size of the language, saturated at 2^64 - 1. */
	get valueCount() {
		return this.start.valueCount;
	}

	/**
	 * Whether this machine's language holds a string.
	 *
	 * One transition per character. A string longer than any the machine generates runs out of
	 * transitions and is refused, so no length guard is needed.
	 *
	 * @param {string} text The string to test.
	 * @returns {boolean} Whether the language holds it.
	 */
	accepts(text) {
		let state = this.start;

		for (let i = 0; i < text.length; ++i) {
			const code = text.charCodeAt(i);
			let next = null;

			for (const transition of state.transitions) {
				if (transition.set.contains(code)) { next = transition.next; break; }
			}

			if (next === null) { return false; }

			state = next;
		}

		return state.acceptsEndOfText;
	}
}

/**
 * Splits the characters covered by `sets` into the coarsest blocks that each set is a union of.
 *
 * @param {AsciiCharSet[]} sets The sets to separate.
 * @returns {AsciiCharSet[]} The blocks.
 */
export function minterms(sets) {
	let universe = AsciiCharSet.empty;

	for (const set of sets) { universe = universe.union(set); }

	if (universe.isEmpty) { return []; }

	const blocks = [universe];

	for (const set of sets) {
		// Once every block is a single character no further set can split anything.
		if (blocks.length === universe.count) { break; }

		// Only the blocks already present can be cut by this set. What gets appended below is the
		// part that fell outside it, which this set cannot cut again.
		const count = blocks.length;

		for (let i = 0; i < count; ++i) {
			const { intersection, thisLessOther } = blocks[i].getDisjointCombinations(set);

			// The block lies wholly inside the set or wholly outside it, so it stands.
			if (intersection.isEmpty || thisLessOther.isEmpty) { continue; }

			blocks[i] = intersection;
			blocks.push(thisLessOther);
		}
	}

	return blocks;
}

/**
 * Builds the machine the specification defines, by symbolic derivatives.
 *
 * The specification defines a state as a language, with one transition per first class. The
 * minterms of the first sets refine those classes rather than equalling them, so the classes are
 * recovered afterwards by merging transitions that reach the same state. Where `[AB]C|[BC]C`
 * gives minterms `[A]`, `[B]` and `[C]`, all three have the derivative `C`, and the merge
 * recombines them into the single class `[ABC]`.
 *
 * Nothing here recurses over the machine. States are built in order of the longest string
 * remaining, which strictly decreases along every derivative, so each state's successors are
 * already built when it is reached. A long chain of states would otherwise want nine thousand
 * stack frames.
 */
class StateMapBuilder {
	/**
	 * @param {import('./rx.js').RxFactory} factory The factory that made the expression.
	 * @param {number} maxStates The budget.
	 */
	constructor(factory, maxStates) {
		this.factory = factory;
		this.maxStates = maxStates;

		/** @type {Map<string, State>} */
		this.interned = new Map();
		/** @type {State[]} */
		this.states = [];

		this.saturated = false;
	}

	/**
	 * @param {import('./rx.js').Rx} start The expression, as produced by the converter.
	 * @returns {{map: StateMap | null, error: NaxpError | null}} The machine, or the refusal.
	 */
	build(start) {
		const explored = this.explore(start);

		if (explored.error !== null) { return { map: null, error: explored.error }; }

		// The successors of an expression all have a strictly shorter longest string, so this
		// ordering puts every state after the states it points at. The terminal expressions,
		// whose longest string is empty, come first.
		explored.expressions.sort((left, right) => left.maxLength - right.maxLength);

		const terminal = this.intern([]);

		/** @type {Map<import('./rx.js').Rx, State>} */
		const stateOf = new Map();

		for (const expression of explored.expressions) {
			const outgoing = explored.edges.get(expression);

			if (outgoing === undefined) {
				// No first sets, so the language is the empty string alone.
				stateOf.set(expression, terminal);
				continue;
			}

			/** @type {Map<State, AsciiCharSet>} */
			const byNext = new Map();

			for (const edge of outgoing) {
				const next = stateOf.get(edge.derivative);
				const already = byNext.get(next);

				byNext.set(next, already === undefined ? edge.set : already.union(edge.set));
			}

			const transitions = [];

			if (expression.isNullable) {
				transitions.push(new Transition(AsciiCharSet.empty, terminal));
			}

			for (const [next, set] of byNext) { transitions.push(new Transition(set, next)); }

			// After merging the sets are disjoint and non-empty apart from end of text, so the
			// sort has one outcome and its stability does not matter.
			transitions.sort((left, right) => left.set.compareTo(right.set));

			stateOf.set(expression, this.intern(transitions));
		}

		return {
			map: new StateMap(stateOf.get(start), this.states, this.saturated),
			error: null,
		};
	}

	/**
	 * Walks the derivatives breadth first, collecting the distinct expressions and the edges
	 * between them.
	 *
	 * @param {import('./rx.js').Rx} start The expression.
	 * @returns {{expressions: import('./rx.js').Rx[],
	 *   edges: Map<import('./rx.js').Rx, Array<{set: AsciiCharSet, derivative: import('./rx.js').Rx}>>,
	 *   error: NaxpError | null}} What was found.
	 */
	explore(start) {
		const expressions = [start];
		const edges = new Map();
		const seen = new Set([start]);
		const queue = [start];
		let head = 0;

		while (head < queue.length) {
			const expression = queue[head++];
			const firstSets = expression.getFirstSets();

			if (firstSets.length === 0) { continue; }

			const outgoing = [];

			for (const minterm of minterms(firstSets)) {
				const derivative = this.factory.derivative(expression, minterm);

				if (derivative.kind === RxKind.EmptySet) { continue; }

				outgoing.push({ set: minterm, derivative });

				if (seen.has(derivative)) { continue; }

				seen.add(derivative);
				expressions.push(derivative);
				queue.push(derivative);

				if (expressions.length > this.maxStates) {
					return {
						expressions: [],
						edges,
						error: new NaxpError(NaxpMessage.NAXP1049_TooManyStates),
					};
				}
			}

			edges.set(expression, outgoing);
		}

		return { expressions, edges, error: null };
	}

	/**
	 * The identity of a state is its transition list and nothing else. Two states are equal when
	 * their transitions are, which by induction means their languages are.
	 *
	 * @param {Transition[]} transitions The transitions.
	 * @returns {State} The interned state.
	 */
	intern(transitions) {
		const key = transitions.map(t => `${t.set.key()}>${t.next.id}`).join(';');
		const existing = this.interned.get(key);

		if (existing !== undefined) { return existing; }

		const created = new State(this.states.length, transitions, this.countValues(transitions));

		this.interned.set(key, created);
		this.states.push(created);

		return created;
	}

	/**
	 * The count of strings a state's language holds, which is the sum over its transitions of
	 * max(1, size of the set) times the count of the next state.
	 *
	 * The C# has to test every step before taking it, because a `ulong` wraps and a wrap cannot
	 * be told from a legal result afterwards. A BigInt does not wrap, so the arithmetic is done
	 * and then compared once. The intermediate cannot run away: every operand is already
	 * saturated at 2^64 - 1 and there are at most 128 transitions.
	 *
	 * @param {Transition[]} transitions The transitions.
	 * @returns {bigint} The count, saturated at 2^64 - 1.
	 */
	countValues(transitions) {
		if (transitions.length === 0) { return 1n; }

		let total = 0n;

		for (const transition of transitions) {
			const width = transition.set.isEmpty ? 1n : BigInt(transition.set.count);

			total += width * transition.next.valueCount;
		}

		if (total > NaxpLimits.maxValueCount) {
			this.saturated = true;

			return NaxpLimits.maxValueCount;
		}

		return total;
	}
}

/**
 * Builds the machine for an expression.
 *
 * @param {import('./rx.js').Rx} start The expression, as produced by the converter.
 * @param {import('./rx.js').RxFactory} factory The factory that made it, reused so derivatives
 * stay interned.
 * @param {number} [maxStates] The budget, lowered by tests so the cap can be reached cheaply.
 * @returns {{map: StateMap | null, error: NaxpError | null}} The machine, or the refusal.
 */
export function tryBuild(start, factory, maxStates = NaxpLimits.maxStates) {
	return new StateMapBuilder(factory, maxStates).build(start);
}
