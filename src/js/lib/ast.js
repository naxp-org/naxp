// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * A node in the abstract syntax tree of a naxp.
 *
 * The tree keeps the structure the source was written in. Intervals and digits ranges are
 * deliberately *not* expanded here. The cap on an interval count exists so that an implementation
 * can reject a naxp before expanding it, and expanding at parse time would throw that away:
 * `(A{99}){99}` is eleven characters of source and nearly ten thousand characters of expansion.
 *
 * Groups do not survive parsing. `(A)` and `A` give the same tree, and the two abbreviated
 * replaceable forms are expanded into the general one, since version 0.4 defines them structurally
 * rather than textually.
 */
export class Ast {
	constructor() {
		/**
		 * The offset in the source at which this node starts. Diagnostics only.
		 *
		 * @type {number}
		 */
		this.sourceOffset = 0;
	}
}

/** The empty string, written `()`. */
export class AstEmpty extends Ast {
}

/** A set of characters matching one position, such as `A`, `\9` or `[A-F]`. */
export class AstChars extends Ast {
	/**
	 * @param {import('./ascii-char-set.js').AsciiCharSet} charSet The characters.
	 */
	constructor(charSet) {
		super();
		this.charSet = charSet;
	}
}

/**
 * A digits range, written `#[`*lo*`-`*hi*`]`.
 *
 * The digit counts are the counts *as written*, which is what fixes the widths generated:
 * `#[00-105]` does not match `7` while `#[0-105]` does.
 *
 * The bounds are ordinary numbers rather than BigInts. A bound may have at most fifteen digits,
 * which the specification chose precisely because that is what a double holds exactly, so no bound
 * can reach the point where a number stops being able to represent it.
 */
export class AstDigitsRange extends Ast {
	/**
	 * @param {number} low The lower bound.
	 * @param {number} lowDigitCount How many digits the lower bound was written with.
	 * @param {number} high The upper bound.
	 * @param {number} highDigitCount How many digits the upper bound was written with.
	 */
	constructor(low, lowDigitCount, high, highDigitCount) {
		super();
		this.low = low;
		this.lowDigitCount = lowDigitCount;
		this.high = high;
		this.highDigitCount = highDigitCount;
	}
}

/** Two or more elements in sequence. */
export class AstSequence extends Ast {
	/**
	 * @param {Ast[]} children The elements, in order.
	 */
	constructor(children) {
		super();
		this.children = children;
	}
}

/** Two or more alternatives separated by `|`. */
export class AstAlternation extends Ast {
	/**
	 * @param {Ast[]} children The alternatives, in the order written.
	 */
	constructor(children) {
		super();
		this.children = children;
	}
}

/** An optional element, written `x?`. */
export class AstOptional extends Ast {
	/**
	 * @param {Ast} child The element that may be absent.
	 */
	constructor(child) {
		super();
		this.child = child;
	}
}

/** A bounded interval, written `x{n}` or `x{m,n}`. */
export class AstInterval extends Ast {
	/**
	 * @param {Ast} child The element repeated.
	 * @param {number} minCount The fewest repetitions.
	 * @param {number} maxCount The most repetitions.
	 */
	constructor(child, minCount, maxCount) {
		super();
		this.child = child;
		this.minCount = minCount;
		this.maxCount = maxCount;
	}
}

/**
 * How a replaceable element was written, which is needed only so that a well-formedness message
 * can name the form the author used rather than the form it expands to.
 *
 * @enum {string}
 */
export const ReplaceableForm = Object.freeze({
	/** `x!y`. */
	Explicit: 'Explicit',
	/** `x!!`, which expands to `x?!(x)`. */
	Reproduced: 'Reproduced',
	/** `x!?`, which expands to `x?!()`. */
	Dropped: 'Dropped',
});

/**
 * A replaceable element, written `x!y`. Which of the strings the subject accepts was matched is not
 * part of the encoding, and the rendering is printed in its place.
 *
 * For the two abbreviated forms the subject is the {@link AstOptional} wrapping what was written,
 * so `subject` is always the expression whose choice goes unencoded. The `x!!` form shares one
 * subtree between `subject` and `rendering`; nothing in the tree is mutated after parsing, so that
 * is safe.
 */
export class AstReplaceable extends Ast {
	/**
	 * @param {Ast} subject The expression whose choice goes unencoded.
	 * @param {Ast} rendering What is printed in its place.
	 * @param {string} form How it was written, one of {@link ReplaceableForm}.
	 */
	constructor(subject, rendering, form) {
		super();
		this.subject = subject;
		this.rendering = rendering;
		this.form = form;
	}
}

/**
 * Whether a tree holds a replaceable element anywhere.
 *
 * This decides two things at once, which is why it is one function rather than living with either
 * of them. Without a replaceable element ρ is the identity, so W3 holds for nothing and the
 * canonical language is the accepted one.
 *
 * @param {Ast} node The node to search from.
 * @returns {boolean} Whether one was found.
 */
export function containsReplaceable(node) {
	if (node instanceof AstReplaceable) { return true; }

	if (node instanceof AstSequence || node instanceof AstAlternation) {
		return node.children.some(containsReplaceable);
	}

	if (node instanceof AstOptional || node instanceof AstInterval) {
		return containsReplaceable(node.child);
	}

	return false;
}
