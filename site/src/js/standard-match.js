// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// Says when a naxp in a box is one of the standard naxps, for /interactive/ and /migrate/.
//
// A match is by behaviour, not by spelling: the naxp and the standard variant must accept the
// same text, give it the same values and print the same canonical forms, which is every axis of
// the library's comparison coming out Equal. So a standard retyped with other spacing, which
// the specification ignores, still matches, and one that has been changed in any way that
// matters does not.

import { Naxp, SetRelationship } from '/naxp/index.js';

/**
 * The standard naxps the build writes into the page, from standards/naxp-standards.json, at
 * `<script type="application/json" id="naxp-standards">`.
 *
 * @returns {Array<{id: string, name: string, variants: Array<{id: string, naxp: string, texts: string[]}>}>}
 * The standards, or none if the page carries no data or it cannot be read.
 */
export function readStandards() {
	try {
		return JSON.parse(document.getElementById('naxp-standards').textContent);
	}
	catch {
		return [];
	}
}

/** Each variant's naxp, parsed the first time it is wanted and kept. */
const parsed = new Map();

/**
 * Parses a variant's naxp, once.
 *
 * @param {{naxp: string}} variant The variant.
 * @returns {Naxp | null} The naxp, or null if it does not parse, which the tests on the
 * standards file rule out but which is no reason to break the page.
 */
function naxpOf(variant) {
	if (!parsed.has(variant)) { parsed.set(variant, Naxp.tryParse(variant.naxp).naxp); }

	return parsed.get(variant);
}

/**
 * Finds the standard variant a naxp behaves exactly as.
 *
 * Only variants with as many encoded values are compared, which is one or two of them for any
 * naxp at all, so this costs little more than the parse it follows.
 *
 * @param {Naxp | null} naxp The naxp, or null for an empty or invalid box.
 * @param {ReturnType<typeof readStandards>} standards The standards.
 * @returns {{standard: {id: string, name: string}, variant: {id: string}} | null} The match, or
 * null for none.
 */
export function findStandard(naxp, standards) {
	if (naxp === null) { return null; }

	for (const standard of standards) {
		for (const variant of standard.variants) {
			const candidate = naxpOf(variant);

			if (candidate === null || candidate.maxEncodedValue !== naxp.maxEncodedValue) { continue; }

			const comparison = Naxp.tryCompare(candidate, naxp);

			if (comparison !== null
				&& comparison.encoding === SetRelationship.Equal
				&& comparison.canonicalForms === SetRelationship.Equal) {
				return { standard, variant };
			}
		}
	}

	return null;
}

/**
 * Fills the note under a naxp box: the standard it is, linked to its section on /standard/, or
 * nothing at all, hidden, when it is none.
 *
 * @param {HTMLElement} note The note.
 * @param {Naxp | null} naxp The naxp in the box, or null for an empty or invalid one.
 * @param {ReturnType<typeof readStandards>} standards The standards.
 */
export function showStandard(note, naxp, standards) {
	const match = findStandard(naxp, standards);

	if (match === null) {
		note.hidden = true;
		note.replaceChildren();

		return;
	}

	const { standard, variant } = match;
	const link = document.createElement('a');
	const id = document.createElement('code');

	link.href = `/standard/#${standard.id}`;
	link.textContent = standard.name;
	id.textContent = standard.id;

	const after = [' ', id];

	if (variant.id !== 'default') {
		const name = document.createElement('code');

		name.textContent = variant.id;
		after.push(', variant ', name);
	}

	after.push('.');

	note.replaceChildren(link, ' standard', ...after);
	note.hidden = false;
}
