// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCanonicalise } from '../lib/canonicaliser.js';
import { encode, tryDecode } from '../lib/codec.js';
import { tryParse } from '../lib/parser.js';
import { NaxpLanguage, convert } from '../lib/rx-converter.js';
import { RxFactory } from '../lib/rx.js';
import { tryBuild } from '../lib/state-map.js';
import { checkW3 } from '../lib/w3-checker.js';
import { check } from '../lib/well-formedness.js';
import { loadConformanceData } from './conformance.js';

const data = loadConformanceData();

/**
 * Both machines for a naxp, built from one factory so the derivatives are shared.
 *
 * This is what the compiler will do once it is written. Doing it by hand here means the encoding
 * can be held to the test data now rather than after another file.
 *
 * @param {string} naxp The source.
 * @returns {{ast: import('../lib/ast.js').Ast,
 *   canonical: import('../lib/state-map.js').StateMap,
 *   accepted: import('../lib/state-map.js').StateMap}} The tree and the two machines.
 */
function compile(naxp) {
	const { ast, error } = tryParse(naxp);

	assert.ok(ast !== null, `${naxp} did not parse: ${error}`);
	assert.equal(check(ast), null, `${naxp} failed W1 or W2`);

	const factory = new RxFactory();

	assert.equal(checkW3(ast, factory), null, `${naxp} failed W3`);

	const canonical = tryBuild(convert(ast, factory, NaxpLanguage.Canonical), factory);
	const accepted = tryBuild(convert(ast, factory, NaxpLanguage.Accepted), factory);

	assert.ok(canonical.map !== null, `${naxp}: no canonical machine: ${canonical.error}`);
	assert.ok(accepted.map !== null, `${naxp}: no accepted machine: ${accepted.error}`);

	return { ast, canonical: canonical.map, accepted: accepted.map };
}

test('the value count is the one the test data states', () => {
	// The size of the canonical language, which is the largest value the naxp can produce.
	const failures = [];

	for (const item of data.cases) {
		const { canonical } = compile(item.naxp);

		if (canonical.valueCount !== BigInt(item.valueCount)) {
			failures.push(
				`${item.naxp}: ${canonical.valueCount} values, `
				+ `and the test data says ${item.valueCount}.`);
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
});

test('the accepted count is the one the test data states', () => {
	// The size of the accepted language, which differs from the value count exactly where the
	// naxp holds a replaceable element.
	const failures = [];

	for (const item of data.cases) {
		if (item.acceptedCount === undefined) { continue; }

		const { accepted } = compile(item.naxp);

		if (accepted.valueCount !== BigInt(item.acceptedCount)) {
			failures.push(
				`${item.naxp}: ${accepted.valueCount} accepted, `
				+ `and the test data says ${item.acceptedCount}.`);
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
});

test('every string in the test data encodes to the value it states', () => {
	// The whole point of the file. A string is canonicalised and then ranked, which is what the
	// specification's procedure says and what the compiler will do in one call.
	const failures = [];
	let checked = 0;

	for (const item of data.cases) {
		const { ast, canonical } = compile(item.naxp);

		for (const value of item.values) {
			++checked;

			const canonicalForm = tryCanonicalise(ast, value.in);
			const encoded = canonicalForm === null ? 0n : encode(canonical, canonicalForm);

			if (encoded !== BigInt(value.out)) {
				failures.push(
					`${item.naxp}: '${value.in}' encodes to ${encoded}, `
					+ `and the test data says ${value.out}.`);
			}
		}
	}

	assert.deepEqual(failures, [], failures.slice(0, 20).join('\n'));
	assert.ok(checked > 400, `only ${checked} strings were checked`);
});

test('every value in the test data decodes to the canonical form it states', () => {
	// Decoding uses the canonical machine alone, and what comes back is the canonical form rather
	// than the string that was encoded.
	const failures = [];
	let checked = 0;

	for (const item of data.cases) {
		const { canonical } = compile(item.naxp);

		for (const value of item.values) {
			if (value.out === '0' || value.canon === undefined) { continue; }

			++checked;

			const decoded = tryDecode(canonical, BigInt(value.out));

			if (decoded !== value.canon) {
				failures.push(
					`${item.naxp}: ${value.out} decodes to '${decoded}', `
					+ `and the test data says '${value.canon}'.`);
			}
		}
	}

	assert.deepEqual(failures, [], failures.slice(0, 20).join('\n'));
	assert.ok(checked > 400, `only ${checked} values were checked`);
});

test('a string the test data marks as not accepted encodes to zero', () => {
	for (const item of data.cases) {
		const { ast, canonical } = compile(item.naxp);

		for (const refused of item.notAccepted) {
			const canonicalForm = tryCanonicalise(ast, refused);
			const encoded = canonicalForm === null ? 0n : encode(canonical, canonicalForm);

			assert.equal(encoded, 0n, `${item.naxp}: '${refused}'`);
		}
	}
});

test('a naxp whose values are all listed has exactly the listed values', () => {
	// Where a case is complete the data holds every string of the language, so nothing outside it
	// can encode and every value from one to the count must be present.
	let complete = 0;

	for (const item of data.cases) {
		if (item.complete !== true) { continue; }

		++complete;

		const { canonical } = compile(item.naxp);
		const listed = new Set(
			item.values.filter(value => value.out !== '0').map(value => value.out));

		assert.equal(
			BigInt(listed.size),
			canonical.valueCount,
			`${item.naxp} lists ${listed.size} distinct values of ${canonical.valueCount}`);

		for (let value = 1n; value <= canonical.valueCount; ++value) {
			assert.ok(listed.has(value.toString()), `${item.naxp} does not list ${value}`);
		}
	}

	assert.ok(complete > 0, 'no case in the data was marked complete');
});
