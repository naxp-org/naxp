// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

import { findStale } from '../../../standards/generate.js';
import { findChanges } from '../../../standards/freeze.js';
import { Naxp } from '../lib/naxp.js';
import { SetRelationship } from '../lib/set-relationship.js';

/**
 * `standards/naxp-standards.json`, the standard naxps every library carries and the site's
 * `/standard/` page is built from. These tests are the rules a standard has to keep, so a new
 * entry or variant that breaks one fails here rather than in a library that vendors the file.
 *
 * Values are decimal strings for the reason the conformance data's are: a naxp may hold up to
 * 2^64 - 1 values and a JSON number is not safe above 2^53.
 */
const data = JSON.parse(readFileSync(new URL('../../../standards/naxp-standards.json', import.meta.url), 'utf8'));

// Lower case letters, digits and single hyphens, starting with a letter. Ids are matched without
// regard to case, so they are written in lower case to leave one spelling of each.
const ID = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;

// Printable ASCII, which is all a name or a note may hold.
const ASCII = /^[\x20-\x7E\n]*$/;

const parsed = new Map();

function naxpOf(variant) {
	if (!parsed.has(variant.naxp)) { parsed.set(variant.naxp, Naxp.parse(variant.naxp)); }

	return parsed.get(variant.naxp);
}

test('the file names its format', () => {
	assert.equal(data.format, 'naxp-standards/0.1');
	assert.ok(data.standards.length > 0);
});

test('standard ids are well formed and unique', () => {
	const ids = data.standards.map((standard) => standard.id);

	for (const id of ids) { assert.match(id, ID); }

	assert.equal(new Set(ids).size, ids.length);
});

test('standard names are ASCII and unique without regard to case', () => {
	const names = data.standards.map((standard) => standard.name);

	for (const name of names) {
		assert.match(name, /^[\x20-\x7E]+$/);
		assert.equal(name, name.trim());
	}

	assert.equal(new Set(names.map((name) => name.toLowerCase())).size, names.length);
});

for (const standard of data.standards) {
	const variants = standard.variants;
	const defaultVariant = variants[0];

	test(`${standard.id}: the first variant is the default and variant ids are unique`, () => {
		assert.equal(defaultVariant.id, 'default');

		for (const variant of variants) { assert.match(variant.id, ID); }

		assert.equal(new Set(variants.map((variant) => variant.id)).size, variants.length);
	});

	for (const variant of variants) {
		const where = `${standard.id}/${variant.id}`;

		// The same guard the map format has against a lost backslash: a naxp that still parses
		// after a careless edit will almost never keep its largest value.
		test(`${where}: parses to the encoded values it declares`, () => {
			assert.equal(naxpOf(variant).maxEncodedValue, BigInt(variant.maxEncodedValue));
		});

		// Notes may use three marks of Markdown and nothing else: backticks for code, and single
		// and double asterisks for emphasis and strong emphasis. A newline is a line break.
		test(`${where}: notes use only the permitted marks`, () => {
			assert.match(variant.notes, ASCII);
			assert.doesNotMatch(variant.notes, /[<>&]/);

			const prose = variant.notes
				.replace(/`[^`\n]+`/g, '')
				.replace(/\*\*[^*\n]+\*\*/g, '')
				.replace(/\*[^*\n]+\*/g, '');

			assert.doesNotMatch(prose, /[`*]/, `unpaired mark in: ${variant.notes}`);
		});

		test(`${where}: valid examples encode and print as stated`, () => {
			const naxp = naxpOf(variant);

			assert.ok(variant.valid.length > 0);

			for (const { text, value, canonical } of variant.valid) {
				assert.equal(naxp.encode(text), BigInt(value), `encode(${JSON.stringify(text)})`);
				assert.equal(naxp.getCanonicalForm(text), canonical, `canonical form of ${JSON.stringify(text)}`);
				assert.equal(naxp.decode(BigInt(value)), canonical, `decode(${value})`);
			}
		});

		test(`${where}: invalid examples are refused`, () => {
			const naxp = naxpOf(variant);

			assert.ok(variant.invalid.length > 0);

			for (const text of variant.invalid) {
				assert.equal(naxp.encode(text), 0n, `encode(${JSON.stringify(text)})`);
			}
		});
	}

	// Variants encode consistently: wherever two variants both accept a text, they give it the
	// same value. That is what any relation on the encoding axis other than Incomparable means.
	test(`${standard.id}: variants encode consistently`, () => {
		for (const a of variants) {
			for (const b of variants) {
				if (a === b) { continue; }

				const { encoding } = Naxp.compare(naxpOf(a), naxpOf(b));

				assert.notEqual(encoding, SetRelationship.Incomparable, `${a.id} and ${b.id}`);
			}
		}
	});

	// Every variant accepts each canonical form of the default and encodes it to the default's
	// value. Where the variant's (text, value) pairs include the default's, that follows at once.
	// Where they are a subset of the default's, it follows when everything the default prints the
	// variant prints too: the variant then decodes some value to that text, and since its pairs
	// are the default's, that value is the default's. Anything else is not decided here and fails.
	test(`${standard.id}: every variant reads the default's canonical forms`, () => {
		const reference = naxpOf(defaultVariant);

		for (const variant of variants.slice(1)) {
			const comparison = Naxp.compare(reference, naxpOf(variant));
			const includesDefault = comparison.encoding === SetRelationship.Equal
				|| comparison.encoding === SetRelationship.SubsetOf;
			const printsDefault = comparison.encoding === SetRelationship.SupersetOf
				&& (comparison.canonicalForms === SetRelationship.Equal
					|| comparison.canonicalForms === SetRelationship.SubsetOf);

			assert.ok(includesDefault || printsDefault,
				`${variant.id}: encoding ${comparison.encoding}, canonical forms ${comparison.canonicalForms}`);

			for (const { canonical, value } of defaultVariant.valid) {
				assert.equal(naxpOf(variant).encode(canonical), BigInt(value), `${variant.id} on ${JSON.stringify(canonical)}`);
			}
		}
	});
}

// The libraries

test('every library carries the file as standards/generate.js writes it', () => {
	assert.deepEqual(findStale(), []);
});

test('allStandardNaxps lists the file row for row', () => {
	const expected = data.standards.flatMap((standard) => standard.variants.map((variant) => ({
		id: standard.id,
		name: standard.name,
		variant: variant.id,
		pattern: variant.naxp,
		notes: variant.notes,
	})));

	assert.deepEqual(Naxp.allStandardNaxps().map((row) => ({ ...row })), expected);
	assert.ok(Object.isFrozen(Naxp.allStandardNaxps()));
	assert.ok(Naxp.allStandardNaxps().every((row) => Object.isFrozen(row)));
});

test('standardNaxp gives every variant, and its examples encode as the file says', () => {
	for (const standard of data.standards) {
		for (const variant of standard.variants) {
			const naxp = Naxp.standardNaxp(standard.id, variant.id);

			assert.equal(naxp.pattern, variant.naxp);
			assert.equal(naxp.maxEncodedValue, BigInt(variant.maxEncodedValue));

			for (const { text, value, canonical } of variant.valid) {
				assert.equal(naxp.encode(text), BigInt(value));
				assert.equal(naxp.getCanonicalForm(text), canonical);
			}

			for (const text of variant.invalid) { assert.equal(naxp.encode(text), 0n); }
		}
	}
});

test('standardNaxp matches ids without regard to ASCII case', () => {
	for (const row of Naxp.allStandardNaxps()) {
		assert.equal(Naxp.tryStandardNaxp(row.id.toUpperCase(), row.variant.toUpperCase())?.pattern, row.pattern);
	}

	// A long s and a Kelvin sign fold to s and k outside ASCII, which must not make them letters
	// of an id.
	assert.equal(Naxp.tryStandardNaxp('uk-po\u017Ftcode'), null);
	assert.equal(Naxp.tryStandardNaxp('u\u212A-postcode'), null);
});

test('standardNaxp takes an absent, null or empty variant as the default', () => {
	const expected = Naxp.standardNaxp('uk-postcode', 'default').pattern;

	assert.equal(Naxp.standardNaxp('uk-postcode').pattern, expected);
	assert.equal(Naxp.standardNaxp('uk-postcode', null).pattern, expected);
	assert.equal(Naxp.standardNaxp('uk-postcode', '').pattern, expected);
	assert.equal(Naxp.standardNaxp('uk-postcode', 'DEFAULT').pattern, expected);
});

test('standardNaxp refuses an unknown id or variant', () => {
	assert.throws(() => Naxp.standardNaxp('uk-postcod'), {
		name: 'RangeError',
		message: "There is no standard naxp 'uk-postcod'. The standard naxps are uk-postcode, eircode, nl-postcode and flight-designator.",
	});
	assert.throws(() => Naxp.standardNaxp('UK-Postcode', 'strict'), {
		name: 'RangeError',
		message: "The standard naxp 'uk-postcode' has no variant 'strict'. Its variants are default, padding-tolerant and padding-canonical.",
	});
	assert.throws(() => Naxp.standardNaxp(''), RangeError);
	assert.equal(Naxp.tryStandardNaxp('uk-postcod'), null);
	assert.equal(Naxp.tryStandardNaxp('uk-postcode', 'strict'), null);
	assert.throws(() => Naxp.standardNaxp(undefined), TypeError);
	assert.throws(() => Naxp.tryStandardNaxp('uk-postcode', 1), TypeError);
});

// The freeze

test('the freeze passes an unchanged file and new standards and variants', () => {
	const grown = structuredClone(data);

	grown.standards[0].variants.push({ id: 'extra', naxp: 'A', maxEncodedValue: '1' });
	grown.standards.push({ id: 'new', variants: [{ id: 'default', naxp: 'B', maxEncodedValue: '1' }] });
	grown.standards[0].name = 'Renamed';
	grown.standards[0].variants[0].notes = 'Rewritten.';

	assert.deepEqual(findChanges(data, data), []);
	assert.deepEqual(findChanges(data, grown), []);
});

test('the freeze stops a changed naxp, a changed size and a removal', () => {
	const changed = structuredClone(data);
	const [uk, eircode, nl] = changed.standards;

	uk.variants[0].naxp = `${uk.variants[0].naxp} `;
	eircode.variants[1].maxEncodedValue = '1';
	nl.variants.pop();
	changed.standards.pop();

	assert.deepEqual(findChanges(data, changed), [
		`uk-postcode/default was ${JSON.stringify(data.standards[0].variants[0].naxp)} and is now ${JSON.stringify(uk.variants[0].naxp)}.`,
		'eircode/no-separator declared 1464843750 values and now declares 1.',
		'nl-postcode/hyphen-tolerant has been removed.',
		'flight-designator/default has been removed.',
		'flight-designator/strict has been removed.',
	]);
});
