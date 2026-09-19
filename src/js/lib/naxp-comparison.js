// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { SetRelationship } from './set-relationship.js';

/**
 * How one naxp stands to another, as three set relationships, each from the first naxp's point of
 * view.
 *
 * The three are independent and each answers a different question about replacing the first naxp
 * with the second. `acceptedText` says whether text that was valid stays valid. `encoding` says
 * whether values already stored still mean what they meant. `printedText` says whether what is
 * printed on decoding can change.
 *
 * `encoding` is the strongest of the three: `Equal` or `SubsetOf` there forces the same on
 * `acceptedText`, since the pairs' texts are the accepted strings. Nothing else follows. Two naxps
 * can give every string the same value and print it differently, as `(A|B)!A` and `(A|B)!B` do,
 * and can print the same strings and value a shared one differently.
 *
 * Built with no arguments it is `Incomparable` on every axis, which is what a comparison that was
 * not made should say.
 *
 * Instances are immutable.
 */
export class NaxpComparison {
	/**
	 * @param {string} [acceptedText] How the set of strings the first naxp accepts stands to the
	 * second's, as one of {@link SetRelationship}.
	 * @param {string} [encoding] How the set of (text, value) pairs the first naxp defines stands
	 * to the second's.
	 * @param {string} [printedText] How the set of strings the first naxp prints, its canonical
	 * forms, stands to the second's. These are compared as sets of strings, so two naxps that
	 * print every value in different forms are incomparable here however closely the forms
	 * correspond.
	 */
	constructor(
		acceptedText = SetRelationship.Incomparable,
		encoding = SetRelationship.Incomparable,
		printedText = SetRelationship.Incomparable) {
		this.acceptedText = acceptedText;
		this.encoding = encoding;
		this.printedText = printedText;

		Object.freeze(this);
	}
}
