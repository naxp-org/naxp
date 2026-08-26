// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';

/**
 * What an {@link Rx} node is.
 *
 * @enum {string}
 */
export const RxKind = Object.freeze({
	/** The empty language, which arises only as a derivative. */
	EmptySet: 'EmptySet',
	/** The language holding only the empty string. */
	Epsilon: 'Epsilon',
	/** A non-empty set of characters matching one position. */
	Chars: 'Chars',
	/** Two or more expressions in sequence. */
	Concat: 'Concat',
	/** Two or more expressions in alternation. */
	Union: 'Union',
	/** An expression repeated between `minCount` and `maxCount` times. */
	Interval: 'Interval',
});

/**
 * What `maxLength` saturates at.
 *
 * The C# saturates at `long.MaxValue`; this is smaller, and the difference cannot show. A
 * language whose longest string is *n* needs at least *n* + 1 states, so anything whose length
 * approaches this is refused by the state budget long before the ordering that reads `maxLength`
 * is reached. Saturation only keeps the arithmetic honest while converting a naxp that is going
 * to be refused anyway.
 */
const MAX_LENGTH = Number.MAX_SAFE_INTEGER;

const NO_CHILDREN = Object.freeze([]);

/**
 * An expression in the algebra the state map is built over.
 *
 * This is deliberately not an `Ast`. The tree records what was written, whereas these nodes are
 * what derivatives are taken of: they carry an empty language, they are normalised by their
 * factory, and they are interned, so equal expressions are the same object and identity can be
 * relied on as a map key.
 *
 * Normalisation does not have to reduce every expression denoting the same language to one form,
 * and it does not. Making the machine canonical is the job of hash-consing on transition lists in
 * `StateMapBuilder`; normalisation here only keeps derivatives from growing and makes memoisation
 * bite.
 *
 * Intervals stay symbolic. Expanding `(A{99}){99}` into nearly ten thousand nodes would throw
 * away the reason the count cap exists.
 */
export class Rx {
	/**
	 * @param {number} id A number unique within the factory that made this node.
	 * @param {string} kind One of {@link RxKind}.
	 * @param {AsciiCharSet} charSet The characters, for `Chars`.
	 * @param {Rx[]} children The operands, for `Concat`, `Union` and `Interval`.
	 * @param {number} minCount The fewest repetitions, for `Interval`.
	 * @param {number} maxCount The most repetitions, for `Interval`.
	 * @param {boolean} isNullable Whether the language holds the empty string.
	 * @param {number} maxLength The length of the longest string in the language.
	 */
	constructor(id, kind, charSet, children, minCount, maxCount, isNullable, maxLength) {
		this.id = id;
		this.kind = kind;
		this.charSet = charSet;
		this.children = children;
		this.minCount = minCount;
		this.maxCount = maxCount;
		this.isNullable = isNullable;

		/**
		 * The length of the longest string in the language, or zero where the language is empty.
		 *
		 * Exact rather than an upper bound, and it strictly decreases along every derivative,
		 * which is what lets the builder order the states without a topological sort.
		 */
		this.maxLength = maxLength;

		/** @type {AsciiCharSet[] | null} */
		this.cachedFirstSets = null;
	}

	/**
	 * The character sets that can match the first character of a string in this language.
	 *
	 * These overlap in general. The minterms of them refine the first classes the specification
	 * defines, and the builder recovers the classes themselves by merging afterwards.
	 *
	 * @returns {AsciiCharSet[]} The sets.
	 */
	getFirstSets() {
		if (this.cachedFirstSets !== null) { return this.cachedFirstSets; }

		const sets = [];

		this.collectFirstSets(sets);
		this.cachedFirstSets = sets;

		return sets;
	}

	/**
	 * @param {AsciiCharSet[]} sets Where to put them.
	 */
	collectFirstSets(sets) {
		switch (this.kind) {
			case RxKind.EmptySet:
			case RxKind.Epsilon:
				return;

			case RxKind.Chars:
				sets.push(this.charSet);
				return;

			case RxKind.Concat:
				for (const child of this.children) {
					child.collectFirstSets(sets);

					if (!child.isNullable) { return; }
				}

				return;

			case RxKind.Union:
				for (const child of this.children) { child.collectFirstSets(sets); }
				return;

			case RxKind.Interval:
				this.children[0].collectFirstSets(sets);
				return;

			default:
				throw new Error(`Unhandled kind ${this.kind}.`);
		}
	}
}

/**
 * Makes {@link Rx} nodes, normalising and interning as it goes.
 *
 * One factory per build. Interning is not shared between naxps, so nothing accumulates.
 */
export class RxFactory {
	constructor() {
		/** @type {Map<string, Rx>} */
		this.interned = new Map();
		/** @type {Map<string, Rx>} */
		this.derivatives = new Map();
		this.nextId = 0;

		/** The empty language. */
		this.emptySet = this.intern(RxKind.EmptySet, AsciiCharSet.empty, NO_CHILDREN, 0, 0, false, 0);

		/** The language holding only the empty string. */
		this.epsilon = this.intern(RxKind.Epsilon, AsciiCharSet.empty, NO_CHILDREN, 0, 0, true, 0);
	}

	/** How many distinct expressions this factory has made. */
	get count() {
		return this.interned.size;
	}

	/**
	 * @param {AsciiCharSet} set The characters.
	 * @returns {Rx} The expression.
	 */
	chars(set) {
		return set.isEmpty
			? this.emptySet
			: this.intern(RxKind.Chars, set, NO_CHILDREN, 0, 0, false, 1);
	}

	/**
	 * Concatenation, flattened, with the empty string dropped and the empty language absorbing.
	 *
	 * @param {Rx[]} parts The operands, in order.
	 * @returns {Rx} The expression.
	 */
	concat(parts) {
		const flattened = [];

		for (const part of parts) {
			if (part.kind === RxKind.EmptySet) { return this.emptySet; }
			if (part.kind === RxKind.Epsilon) { continue; }

			if (part.kind === RxKind.Concat) { flattened.push(...part.children); }
			else { flattened.push(part); }
		}

		if (flattened.length === 0) { return this.epsilon; }
		if (flattened.length === 1) { return flattened[0]; }

		let isNullable = true;
		let maxLength = 0;

		for (const part of flattened) {
			isNullable = isNullable && part.isNullable;
			maxLength = saturatingAdd(maxLength, part.maxLength);
		}

		return this.intern(RxKind.Concat, AsciiCharSet.empty, flattened, 0, 0, isNullable, maxLength);
	}

	/**
	 * @param {Rx} first The first operand.
	 * @param {Rx} second The second operand.
	 * @returns {Rx} The concatenation.
	 */
	concatTwo(first, second) {
		return this.concat([first, second]);
	}

	/**
	 * Alternation, flattened, with the empty language dropped and duplicates removed.
	 *
	 * The operands are sorted by id. Ids differ between runs, but within one run two unions over
	 * the same operands sort the same way, which is all interning needs.
	 *
	 * @param {Rx[]} alternatives The operands.
	 * @returns {Rx} The expression.
	 */
	union(alternatives) {
		const flattened = [];

		for (const alternative of alternatives) {
			if (alternative.kind === RxKind.EmptySet) { continue; }

			if (alternative.kind === RxKind.Union) { flattened.push(...alternative.children); }
			else { flattened.push(alternative); }
		}

		flattened.sort((left, right) => left.id - right.id);

		const distinct = [];

		for (const alternative of flattened) {
			if (distinct.length === 0 || distinct[distinct.length - 1] !== alternative) {
				distinct.push(alternative);
			}
		}

		if (distinct.length === 0) { return this.emptySet; }
		if (distinct.length === 1) { return distinct[0]; }

		let isNullable = false;
		let maxLength = 0;

		for (const alternative of distinct) {
			isNullable = isNullable || alternative.isNullable;
			maxLength = Math.max(maxLength, alternative.maxLength);
		}

		return this.intern(RxKind.Union, AsciiCharSet.empty, distinct, 0, 0, isNullable, maxLength);
	}

	/**
	 * @param {Rx} first The first alternative.
	 * @param {Rx} second The second alternative.
	 * @returns {Rx} The alternation.
	 */
	unionTwo(first, second) {
		return this.union([first, second]);
	}

	/**
	 * Between `minCount` and `maxCount` copies in sequence.
	 *
	 * @param {Rx} child The expression repeated.
	 * @param {number} minCount The fewest repetitions.
	 * @param {number} maxCount The most repetitions.
	 * @returns {Rx} The expression.
	 */
	interval(child, minCount, maxCount) {
		if (maxCount === 0) { return this.epsilon; }
		if (child.kind === RxKind.Epsilon) { return this.epsilon; }
		if (child.kind === RxKind.EmptySet) { return minCount === 0 ? this.epsilon : this.emptySet; }

		// Where the child accepts the empty string, so does every count above the minimum, and
		// x{m,n} and x{0,n} are the same language. Normalising here is what lets isNullable be
		// read off the minimum alone.
		const low = child.isNullable ? 0 : minCount;

		if (low === 1 && maxCount === 1) { return child; }

		const maxLength = saturatingMultiply(child.maxLength, maxCount);

		return this.intern(
			RxKind.Interval,
			AsciiCharSet.empty,
			[child],
			low,
			maxCount,
			low === 0,
			maxLength);
	}

	/**
	 * The derivative of an expression after any character of a minterm.
	 *
	 * @param {Rx} expression The expression to differentiate.
	 * @param {AsciiCharSet} minterm A minterm of the expression's first sets. Every character in
	 * it must behave alike, which is what makes one derivative stand for the whole set.
	 * @returns {Rx} The derivative, which is the empty language where nothing follows.
	 */
	derivative(expression, minterm) {
		const key = `${expression.id}|${minterm.key()}`;
		const cached = this.derivatives.get(key);

		if (cached !== undefined) { return cached; }

		const result = this.computeDerivative(expression, minterm);

		this.derivatives.set(key, result);

		return result;
	}

	/**
	 * @param {Rx} expression The expression to differentiate.
	 * @param {AsciiCharSet} minterm The minterm.
	 * @returns {Rx} The derivative.
	 */
	computeDerivative(expression, minterm) {
		switch (expression.kind) {
			case RxKind.EmptySet:
			case RxKind.Epsilon:
				return this.emptySet;

			case RxKind.Chars:
				// The minterm is wholly inside the set or wholly outside it.
				return minterm.intersectsWith(expression.charSet) ? this.epsilon : this.emptySet;

			case RxKind.Concat: {
				const alternatives = [];

				for (let i = 0; i < expression.children.length; ++i) {
					const head = this.derivative(expression.children[i], minterm);

					if (head.kind !== RxKind.EmptySet) {
						alternatives.push(this.concat([head, ...expression.children.slice(i + 1)]));
					}

					// Only a part that can match nothing lets the character be consumed later on.
					if (!expression.children[i].isNullable) { break; }
				}

				return this.union(alternatives);
			}

			case RxKind.Union: {
				const alternatives = expression.children.map(
					child => this.derivative(child, minterm));

				return this.union(alternatives);
			}

			case RxKind.Interval: {
				const child = expression.children[0];
				const head = this.derivative(child, minterm);

				if (head.kind === RxKind.EmptySet) { return this.emptySet; }

				const minCount = expression.minCount === 0 ? 0 : expression.minCount - 1;

				return this.concatTwo(
					head,
					this.interval(child, minCount, expression.maxCount - 1));
			}

			default:
				throw new Error(`Unhandled kind ${expression.kind}.`);
		}
	}

	/**
	 * The identity of an expression is its shape and its operands, which are already interned and
	 * so are named by id.
	 *
	 * @param {string} kind One of {@link RxKind}.
	 * @param {AsciiCharSet} charSet The characters.
	 * @param {Rx[]} children The operands.
	 * @param {number} minCount The fewest repetitions.
	 * @param {number} maxCount The most repetitions.
	 * @param {boolean} isNullable Whether the language holds the empty string.
	 * @param {number} maxLength The longest string.
	 * @returns {Rx} The interned expression.
	 */
	intern(kind, charSet, children, minCount, maxCount, isNullable, maxLength) {
		const key = `${kind}|${charSet.key()}|${minCount}|${maxCount}|`
			+ children.map(child => child.id).join(',');

		const existing = this.interned.get(key);

		if (existing !== undefined) { return existing; }

		const created = new Rx(
			this.nextId++,
			kind,
			charSet,
			children,
			minCount,
			maxCount,
			isNullable,
			maxLength);

		this.interned.set(key, created);

		return created;
	}
}

/**
 * @param {number} left The first length.
 * @param {number} right The second length.
 * @returns {number} Their sum, saturated.
 */
function saturatingAdd(left, right) {
	const sum = left + right;

	return sum > MAX_LENGTH ? MAX_LENGTH : sum;
}

/**
 * @param {number} left The length.
 * @param {number} right The repetitions.
 * @returns {number} Their product, saturated.
 */
function saturatingMultiply(left, right) {
	if (left === 0 || right === 0) { return 0; }

	return left > MAX_LENGTH / right ? MAX_LENGTH : left * right;
}
