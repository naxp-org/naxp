// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * How one set stands to another, for two sets called `a` and `b`.
 */
export const SetRelationship = Object.freeze({
	/**
	 * Neither set contains the other. They may be wholly disjoint or overlap in part.
	 *
	 * This is what a comparison that was not made reports, so that it says nothing rather than
	 * claiming the two sets are alike.
	 */
	Incomparable: 'Incomparable',

	/** The two sets have exactly the same members. */
	Equal: 'Equal',

	/** Every member of `a` is a member of `b`, and `b` has at least one more. */
	SubsetOf: 'SubsetOf',

	/** Every member of `b` is a member of `a`, and `a` has at least one more. */
	SupersetOf: 'SupersetOf',
});
