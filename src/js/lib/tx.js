// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { AsciiCharSet } from './ascii-char-set.js';
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
import { MAX_GENERATED_LENGTH, SingleStringOutcome, tryGetSingleString } from './matcher.js';
import { NaxpLanguage, convert as convertRx } from './rx-converter.js';
import { RxKind } from './rx.js';

/**
 * What a {@link Tx} node is.
 *
 * @enum {string}
 */
export const TxKind = Object.freeze({
	/** The empty relation, which arises only as a derivative. */
	EmptySet: 'EmptySet',
	/** Reads the empty string and emits nothing. */
	Epsilon: 'Epsilon',
	/** Reads one character of a set and emits that same character. */
	Chars: 'Chars',
	/** Reads any string of a subject and emits a fixed rendering when it completes. */
	Repl: 'Repl',
	/** Two or more in sequence. */
	Concat: 'Concat',
	/** Two or more in alternation. */
	Union: 'Union',
	/** One repeated between `minCount` and `maxCount` times. */
	Interval: 'Interval',
});

/**
 * Whether an expression has exactly one way of emitting at end of text.
 *
 * @enum {string}
 */
export const EotKind = Object.freeze({
	/** There is no ε-parse, so the expression cannot accept end of text. */
	None: 'None',
	/** Every ε-parse emits the same string. */
	Single: 'Single',
	/** Two ε-parses emit different strings, which is a W3 violation wherever it is reached. */
	Multiple: 'Multiple',
	/** Deciding would build a string longer than this implementation will materialise. */
	TooLong: 'TooLong',
});

/**
 * Stands for a copied character whose identity the block has not yet fixed.
 *
 * Outside ASCII, so it cannot collide with anything a naxp emits. Where one of these survives
 * into a delay the comparison it takes part in is undecided, and the W3 checker retries that step
 * one character at a time.
 */
export const COPY_MARKER = '￿';

/**
 * What an expression emits when it accepts the empty string.
 */
export class Eot {
	/**
	 * @param {string} kind One of {@link EotKind}.
	 * @param {string | null} text The emitted string, for `Single` only.
	 */
	constructor(kind, text) {
		this.kind = kind;
		this.text = text;
	}

	/**
	 * @param {string} text The emitted string.
	 * @returns {Eot} The behaviour.
	 */
	static single(text) {
		return text.length > MAX_GENERATED_LENGTH ? EOT_TOO_LONG : new Eot(EotKind.Single, text);
	}

	/**
	 * The end of text behaviour of two expressions in sequence, which is the product of theirs.
	 *
	 * @param {Eot} left The first.
	 * @param {Eot} right The second.
	 * @returns {Eot} The behaviour.
	 */
	static concat(left, right) {
		if (left.kind === EotKind.None || right.kind === EotKind.None) { return EOT_NONE; }
		if (left.kind === EotKind.TooLong || right.kind === EotKind.TooLong) { return EOT_TOO_LONG; }

		if (left.kind === EotKind.Multiple || right.kind === EotKind.Multiple) {
			return EOT_MULTIPLE;
		}

		return Eot.single(left.text + right.text);
	}

	/**
	 * The end of text behaviour of two expressions in alternation, which is the union of theirs.
	 *
	 * @param {Eot} left The first.
	 * @param {Eot} right The second.
	 * @returns {Eot} The behaviour.
	 */
	static union(left, right) {
		if (left.kind === EotKind.None) { return right; }
		if (right.kind === EotKind.None) { return left; }
		if (left.kind === EotKind.TooLong || right.kind === EotKind.TooLong) { return EOT_TOO_LONG; }

		if (left.kind === EotKind.Multiple || right.kind === EotKind.Multiple) {
			return EOT_MULTIPLE;
		}

		return left.text === right.text ? left : EOT_MULTIPLE;
	}
}

/** There is no ε-parse. */
export const EOT_NONE = new Eot(EotKind.None, null);

/** Two ε-parses emit different strings. */
export const EOT_MULTIPLE = new Eot(EotKind.Multiple, null);

/** Deciding would build too long a string. */
export const EOT_TOO_LONG = new Eot(EotKind.TooLong, null);

/** One ε-parse, emitting nothing. */
export const EOT_EMPTY = new Eot(EotKind.Single, '');

/**
 * One way of consuming a block of characters: what was emitted, and what is left to do.
 */
export class TxMove {
	/**
	 * @param {string} emitted What this step emits. A copied character appears as
	 * {@link COPY_MARKER} where the block holds more than one character, since which character
	 * was read is not yet decided.
	 * @param {Tx} residual What is left to do.
	 */
	constructor(emitted, residual) {
		this.emitted = emitted;
		this.residual = residual;
	}
}

/**
 * The result of differentiating, cached whole because the ambiguity flag belongs to the step
 * rather than to any one move.
 */
export class TxDerivative {
	/**
	 * @param {TxMove[]} moves The ways of consuming the block.
	 * @param {boolean} skipsAmbiguously Whether a nullable element was skipped over that emits
	 * two different strings at end of text, with a live continuation beyond it. That is a W3
	 * violation on its own: the two skips give one input two outputs, and the continuation is
	 * non-empty because empty residuals are dropped, so both parses reach an accepting string.
	 * @param {boolean} tooLong Whether the step was abandoned as too large to compute.
	 */
	constructor(moves, skipsAmbiguously, tooLong) {
		this.moves = moves;
		this.skipsAmbiguously = skipsAmbiguously;
		this.tooLong = tooLong;
	}
}

const NOTHING = new TxDerivative([], false, false);

/**
 * The most skipped copies of an interval this implementation will follow separately.
 *
 * Only reached where skipping a copy emits, which needs a replaceable element with a nullable
 * subject inside an interval whose count can vary. Nothing a naxp is for goes near it, and a naxp
 * that does is refused as an implementation limit rather than judged.
 */
const MAX_SKIPPED_COPIES = 64;

const NO_CHILDREN = Object.freeze([]);

/**
 * The transduction ρ as an expression, so that derivatives of it can be taken.
 *
 * This is what the Rx converter throws away. There a replaceable element becomes either its
 * subject or its rendering, depending on which language is being built, and W3 is exactly the
 * question of how the two behave together. `Repl` is the node that keeps them paired.
 *
 * Emission is deferred to the end of the element. A replaceable consumes its subject one
 * character at a time emitting nothing, then emits the whole rendering when it completes, which
 * is why the difference between two branches' outputs has to be carried as a delay rather than
 * compared character by character.
 *
 * Nodes are interned by their factory, so identity is structural equality and a node can be a map
 * key.
 */
export class Tx {
	/**
	 * @param {number} id A number unique within the factory that made this node.
	 * @param {string} kind One of {@link TxKind}.
	 * @param {AsciiCharSet} charSet The characters, for `Chars`.
	 * @param {import('./rx.js').Rx | null} subject What is consumed, for `Repl`.
	 * @param {string | null} rendering What is emitted, for `Repl`. One string, by W1.
	 * @param {Tx[]} children The operands.
	 * @param {number} minCount The fewest repetitions.
	 * @param {number} maxCount The most repetitions.
	 * @param {boolean} isNullable Whether the empty string can be consumed. This is about input
	 * alone.
	 */
	constructor(id, kind, charSet, subject, rendering, children, minCount, maxCount, isNullable) {
		this.id = id;
		this.kind = kind;
		this.charSet = charSet;
		this.subject = subject;
		this.rendering = rendering;
		this.children = children;
		this.minCount = minCount;
		this.maxCount = maxCount;
		this.isNullable = isNullable;

		/** @type {AsciiCharSet[] | null} */
		this.cachedFirstSets = null;
		/** @type {Eot | null} */
		this.cachedEot = null;
	}

	/**
	 * What is emitted where the empty string is consumed.
	 *
	 * @returns {Eot} The behaviour.
	 */
	getEot() {
		if (this.cachedEot !== null) { return this.cachedEot; }

		this.cachedEot = this.computeEot();

		return this.cachedEot;
	}

	/**
	 * The character sets that can match the first character consumed.
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

	/** @returns {Eot} The behaviour. */
	computeEot() {
		switch (this.kind) {
			case TxKind.EmptySet:
			case TxKind.Chars:
				return EOT_NONE;

			case TxKind.Epsilon:
				return EOT_EMPTY;

			case TxKind.Repl:
				// Completing a replaceable emits its rendering even though nothing was consumed.
				return this.subject.isNullable ? Eot.single(this.rendering) : EOT_NONE;

			case TxKind.Concat: {
				let result = EOT_EMPTY;

				for (const child of this.children) { result = Eot.concat(result, child.getEot()); }

				return result;
			}

			case TxKind.Union: {
				let result = EOT_NONE;

				for (const child of this.children) { result = Eot.union(result, child.getEot()); }

				return result;
			}

			case TxKind.Interval: {
				const child = this.children[0];

				// A count of zero denotes the empty string whatever the child would emit.
				if (!child.isNullable) { return this.minCount === 0 ? EOT_EMPTY : EOT_NONE; }

				const inner = child.getEot();

				if (inner.kind !== EotKind.Single) { return inner; }

				// Repeating something that emits nothing emits nothing however often it happens.
				if (inner.text.length === 0) { return EOT_EMPTY; }

				// Otherwise every count between the two bounds consumes nothing and emits a
				// different length, so a free count is more than one output. A fixed count is one
				// output, which is why '(A!!){2}' is well formed and '(A!!){0,2}' is not.
				if (this.minCount !== this.maxCount) { return EOT_MULTIPLE; }

				if (inner.text.length * this.minCount > MAX_GENERATED_LENGTH) {
					return EOT_TOO_LONG;
				}

				return Eot.single(inner.text.repeat(this.minCount));
			}

			default:
				throw new Error(`Unhandled kind ${this.kind}.`);
		}
	}

	/**
	 * @param {AsciiCharSet[]} sets Where to put them.
	 */
	collectFirstSets(sets) {
		switch (this.kind) {
			case TxKind.EmptySet:
			case TxKind.Epsilon:
				return;

			case TxKind.Chars:
				sets.push(this.charSet);
				return;

			case TxKind.Repl:
				sets.push(...this.subject.getFirstSets());
				return;

			case TxKind.Concat:
				for (const child of this.children) {
					child.collectFirstSets(sets);

					if (!child.isNullable) { return; }
				}

				return;

			case TxKind.Union:
				for (const child of this.children) { child.collectFirstSets(sets); }
				return;

			case TxKind.Interval:
				this.children[0].collectFirstSets(sets);
				return;

			default:
				throw new Error(`Unhandled kind ${this.kind}.`);
		}
	}
}

/**
 * Makes {@link Tx} nodes, normalising and interning as it goes, and differentiates them.
 */
export class TxFactory {
	/**
	 * @param {import('./rx.js').RxFactory} rxFactory The factory for the input side.
	 */
	constructor(rxFactory) {
		this.rxFactory = rxFactory;

		/** @type {Map<string, Tx>} */
		this.interned = new Map();
		/** @type {Map<string, TxDerivative>} */
		this.derivatives = new Map();

		/**
		 * Every character code that appears in some rendering.
		 *
		 * Splitting these out as singleton blocks is what makes emission uniform over a block. A
		 * character set emits the character read and a replaceable emits a fixed string, so
		 * whether the two agree depends on which character of the block was read: in
		 * `[ab]|[ab]!a` they agree on `a` and disagree on `b`. Refining costs transitions, never
		 * states, and cannot change what is accepted, since the input side is already uniform
		 * over the coarser blocks.
		 *
		 * @type {number[]}
		 */
		this.renderingCharacters = [];

		this.nextId = 0;

		this.emptySet = this.intern(
			TxKind.EmptySet, AsciiCharSet.empty, null, null, NO_CHILDREN, 0, 0, false);
		this.epsilon = this.intern(
			TxKind.Epsilon, AsciiCharSet.empty, null, null, NO_CHILDREN, 0, 0, true);
	}

	/** How many distinct expressions this factory has made. */
	get count() {
		return this.interned.size;
	}

	/**
	 * @param {AsciiCharSet} set The characters.
	 * @returns {Tx} The expression.
	 */
	chars(set) {
		return set.isEmpty
			? this.emptySet
			: this.intern(TxKind.Chars, set, null, null, NO_CHILDREN, 0, 0, false);
	}

	/**
	 * A replaceable element: consume any string of the subject, emit the rendering.
	 *
	 * @param {import('./rx.js').Rx} subject What is consumed.
	 * @param {string} rendering What is emitted.
	 * @returns {Tx} The expression.
	 */
	repl(subject, rendering) {
		if (subject.kind === RxKind.EmptySet) { return this.emptySet; }

		for (let i = 0; i < rendering.length; ++i) {
			const code = rendering.charCodeAt(i);

			if (!this.renderingCharacters.includes(code)) { this.renderingCharacters.push(code); }
		}

		return this.intern(
			TxKind.Repl,
			AsciiCharSet.empty,
			subject,
			rendering,
			NO_CHILDREN,
			0,
			0,
			subject.isNullable);
	}

	/**
	 * @param {Tx[]} parts The operands, in order.
	 * @returns {Tx} The expression.
	 */
	concat(parts) {
		const flattened = [];

		for (const part of parts) {
			if (part.kind === TxKind.EmptySet) { return this.emptySet; }

			// An epsilon emits nothing, so dropping it changes neither input nor output.
			if (part.kind === TxKind.Epsilon) { continue; }

			if (part.kind === TxKind.Concat) { flattened.push(...part.children); }
			else { flattened.push(part); }
		}

		if (flattened.length === 0) { return this.epsilon; }
		if (flattened.length === 1) { return flattened[0]; }

		let isNullable = true;

		for (const part of flattened) { isNullable = isNullable && part.isNullable; }

		return this.intern(
			TxKind.Concat, AsciiCharSet.empty, null, null, flattened, 0, 0, isNullable);
	}

	/**
	 * @param {Tx} first The first operand.
	 * @param {Tx} second The second operand.
	 * @returns {Tx} The concatenation.
	 */
	concatTwo(first, second) {
		return this.concat([first, second]);
	}

	/**
	 * Duplicates are removed by identity, which is safe: two identical alternatives are one parse
	 * repeated, not two parses, so removing one removes no output.
	 *
	 * @param {Tx[]} alternatives The operands.
	 * @returns {Tx} The expression.
	 */
	union(alternatives) {
		const flattened = [];

		for (const alternative of alternatives) {
			if (alternative.kind === TxKind.EmptySet) { continue; }

			if (alternative.kind === TxKind.Union) { flattened.push(...alternative.children); }
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

		for (const alternative of distinct) { isNullable = isNullable || alternative.isNullable; }

		return this.intern(
			TxKind.Union, AsciiCharSet.empty, null, null, distinct, 0, 0, isNullable);
	}

	/**
	 * @param {Tx} first The first alternative.
	 * @param {Tx} second The second alternative.
	 * @returns {Tx} The alternation.
	 */
	unionTwo(first, second) {
		return this.union([first, second]);
	}

	/**
	 * @param {Tx} child The expression repeated.
	 * @param {number} minCount The fewest repetitions.
	 * @param {number} maxCount The most repetitions.
	 * @returns {Tx} The expression.
	 */
	interval(child, minCount, maxCount) {
		if (maxCount === 0) { return this.epsilon; }
		if (child.kind === TxKind.EmptySet) { return minCount === 0 ? this.epsilon : this.emptySet; }

		// Unlike Rx, an epsilon child is not dropped here unless it emits nothing: a replaceable
		// with a nullable subject consumes nothing and still emits, and how often that happens is
		// what makes '(A!!){0,3}' ambiguous.
		if (child.kind === TxKind.Epsilon) { return this.epsilon; }

		// Rx drives the minimum to zero where the child is nullable, because for input alone x{2}
		// and x{0,2} then accept the same language. That is not available here: the count decides
		// how many renderings are emitted, and '(A!!){2}' emits 'AA' where '(A!!){0,2}' emits one
		// of three strings.
		if (minCount === 1 && maxCount === 1) { return child; }

		return this.intern(
			TxKind.Interval,
			AsciiCharSet.empty,
			null,
			null,
			[child],
			minCount,
			maxCount,
			minCount === 0 || child.isNullable);
	}

	/**
	 * Every way of consuming one character of a block.
	 *
	 * @param {Tx} expression The expression to differentiate.
	 * @param {AsciiCharSet} block A block of characters that behave alike on the input side, and
	 * on the output side too once the rendering characters have been split out.
	 * @returns {TxDerivative} The moves, with the emitted string of each.
	 */
	derivative(expression, block) {
		const key = `${expression.id}|${block.key()}`;
		const cached = this.derivatives.get(key);

		if (cached !== undefined) { return cached; }

		const result = this.computeDerivative(expression, block);

		this.derivatives.set(key, result);

		return result;
	}

	/**
	 * @param {Tx} expression The expression to differentiate.
	 * @param {AsciiCharSet} block The block.
	 * @returns {TxDerivative} The derivative.
	 */
	computeDerivative(expression, block) {
		switch (expression.kind) {
			case TxKind.EmptySet:
			case TxKind.Epsilon:
				return NOTHING;

			case TxKind.Chars: {
				if (!block.intersectsWith(expression.charSet)) { return NOTHING; }

				// A block of one character is already concrete; a wider one is not, and what it
				// emits stays undecided until the checker narrows it.
				const single = block.singleCharacter;
				const emitted = single === null ? COPY_MARKER : String.fromCharCode(single);

				return new TxDerivative([new TxMove(emitted, this.epsilon)], false, false);
			}

			case TxKind.Repl: {
				const residual = this.rxFactory.derivative(expression.subject, block);

				if (residual.kind === RxKind.EmptySet) { return NOTHING; }

				// Nothing is emitted while the subject is being consumed.
				return new TxDerivative(
					[new TxMove('', this.repl(residual, expression.rendering))],
					false,
					false);
			}

			case TxKind.Union: {
				const moves = [];
				let skipsAmbiguously = false;
				let tooLong = false;

				for (const child of expression.children) {
					const sub = this.derivative(child, block);

					moves.push(...sub.moves);
					skipsAmbiguously = skipsAmbiguously || sub.skipsAmbiguously;
					tooLong = tooLong || sub.tooLong;
				}

				return new TxDerivative(moves, skipsAmbiguously, tooLong);
			}

			case TxKind.Concat: {
				const moves = [];
				let skipsAmbiguously = false;
				let tooLong = false;

				// What the elements skipped over so far emit. Skipping a nullable element means
				// choosing one of its end of text parses, and that choice can emit.
				let skipped = EOT_EMPTY;

				for (let i = 0; i < expression.children.length; ++i) {
					const sub = this.derivative(expression.children[i], block);

					skipsAmbiguously = skipsAmbiguously || sub.skipsAmbiguously;
					tooLong = tooLong || sub.tooLong;

					if (sub.moves.length > 0) {
						if (skipped.kind === EotKind.Multiple) {
							// Two ways of skipping emit differently and both continue, so one
							// input has two outputs. There is nothing left to decide.
							skipsAmbiguously = true;
						} else if (skipped.kind === EotKind.TooLong) {
							tooLong = true;
						} else {
							for (const move of sub.moves) {
								const rest = [move.residual, ...expression.children.slice(i + 1)];

								moves.push(new TxMove(
									skipped.text + move.emitted,
									this.concat(rest)));
							}
						}
					}

					// Only an element that can consume nothing lets a later one take the
					// character.
					if (!expression.children[i].isNullable) { break; }

					skipped = Eot.concat(skipped, expression.children[i].getEot());
				}

				return new TxDerivative(moves, skipsAmbiguously, tooLong);
			}

			case TxKind.Interval: {
				const child = expression.children[0];
				const sub = this.derivative(child, block);

				if (sub.moves.length === 0) { return NOTHING; }

				let skipsAmbiguously = sub.skipsAmbiguously;
				let tooLong = sub.tooLong;

				// Copies before the one that consumes may be skipped, and a skipped copy emits
				// what its child emits at end of text.
				const inner = child.isNullable ? child.getEot() : EOT_NONE;
				let skips = 0;

				if (child.isNullable && expression.maxCount >= 2) {
					if (inner.kind === EotKind.Multiple) {
						// Two ways of skipping one copy emit differently and leave the same work
						// behind them, so the totals differ whatever follows.
						skipsAmbiguously = true;
					} else if (inner.kind === EotKind.TooLong) {
						tooLong = true;
					} else if (inner.text.length > 0) {
						// Skipping emits, so each count is a separate parse and has to be
						// followed. What it leaves behind shrinks as more are skipped, and that
						// can pay the difference back: '(A!!){2}' emits 'AA' by either route.
						skips = expression.maxCount - 1;
					}
				}

				// Where a skipped copy emits nothing the parses differ only in a residual that
				// the unskipped one already covers, so one move stands for all of them.
				if (skips > MAX_SKIPPED_COPIES) {
					return new TxDerivative([], skipsAmbiguously, true);
				}

				const moves = [];
				let emittedBySkips = '';

				for (let skippedCount = 0; skippedCount <= skips; ++skippedCount) {
					const used = skippedCount + 1;
					const rest = this.interval(
						child,
						expression.minCount <= used ? 0 : expression.minCount - used,
						expression.maxCount - used);

					for (const move of sub.moves) {
						moves.push(new TxMove(
							emittedBySkips + move.emitted,
							this.concatTwo(move.residual, rest)));
					}

					if (inner.kind === EotKind.Single) { emittedBySkips += inner.text; }
				}

				return new TxDerivative(moves, skipsAmbiguously, tooLong);
			}

			default:
				throw new Error(`Unhandled kind ${expression.kind}.`);
		}
	}

	/**
	 * @param {string} kind One of {@link TxKind}.
	 * @param {AsciiCharSet} charSet The characters.
	 * @param {import('./rx.js').Rx | null} subject What is consumed.
	 * @param {string | null} rendering What is emitted.
	 * @param {Tx[]} children The operands.
	 * @param {number} minCount The fewest repetitions.
	 * @param {number} maxCount The most repetitions.
	 * @param {boolean} isNullable Whether the empty string can be consumed.
	 * @returns {Tx} The interned expression.
	 */
	intern(kind, charSet, subject, rendering, children, minCount, maxCount, isNullable) {
		const key = `${kind}|${charSet.key()}|${subject === null ? '' : subject.id}`
			+ `|${rendering === null ? '' : JSON.stringify(rendering)}`
			+ `|${minCount}|${maxCount}|${children.map(child => child.id).join(',')}`;

		const existing = this.interned.get(key);

		if (existing !== undefined) { return existing; }

		const created = new Tx(
			this.nextId++,
			kind,
			charSet,
			subject,
			rendering,
			children,
			minCount,
			maxCount,
			isNullable);

		this.interned.set(key, created);

		return created;
	}
}

/**
 * Turns a parsed naxp into the transducer algebra.
 *
 * @param {import('./ast.js').Ast} node The tree.
 * @param {TxFactory} factory The factory to build with.
 * @param {import('./rx.js').RxFactory} rxFactory The factory for the input side.
 * @returns {Tx} The transduction.
 */
export function convert(node, factory, rxFactory) {
	if (node instanceof AstEmpty) { return factory.epsilon; }

	if (node instanceof AstChars) { return factory.chars(node.charSet); }

	if (node instanceof AstDigitsRange) {
		// A digits range emits what it consumed, so its expansion needs no output of its own and
		// the one the Rx converter already knows how to build can be lifted.
		return lift(convertRx(node, rxFactory, NaxpLanguage.Accepted), factory);
	}

	if (node instanceof AstSequence) {
		return factory.concat(node.children.map(child => convert(child, factory, rxFactory)));
	}

	if (node instanceof AstAlternation) {
		return factory.union(node.children.map(child => convert(child, factory, rxFactory)));
	}

	if (node instanceof AstOptional) {
		return factory.unionTwo(factory.epsilon, convert(node.child, factory, rxFactory));
	}

	if (node instanceof AstInterval) {
		return factory.interval(
			convert(node.child, factory, rxFactory),
			node.minCount,
			node.maxCount);
	}

	if (node instanceof AstReplaceable) {
		// W1 has already established that the rendering generates exactly one string.
		const { outcome, result } = tryGetSingleString(node.rendering);

		if (outcome !== SingleStringOutcome.Single) {
			throw new Error('A replaceable element passed W1 but has no single rendering.');
		}

		return factory.repl(
			convertRx(node.subject, rxFactory, NaxpLanguage.Accepted),
			result);
	}

	throw new Error(`Unhandled node type ${node.constructor.name}.`);
}

/**
 * Reads an expression with no replaceable elements as a transduction, which copies.
 *
 * @param {import('./rx.js').Rx} expression The expression.
 * @param {TxFactory} factory The factory to build with.
 * @returns {Tx} The transduction.
 */
function lift(expression, factory) {
	switch (expression.kind) {
		case RxKind.EmptySet:
			return factory.emptySet;

		case RxKind.Epsilon:
			return factory.epsilon;

		case RxKind.Chars:
			return factory.chars(expression.charSet);

		case RxKind.Concat:
			return factory.concat(expression.children.map(child => lift(child, factory)));

		case RxKind.Union:
			return factory.union(expression.children.map(child => lift(child, factory)));

		case RxKind.Interval:
			return factory.interval(
				lift(expression.children[0], factory),
				expression.minCount,
				expression.maxCount);

		default:
			throw new Error(`Unhandled kind ${expression.kind}.`);
	}
}
