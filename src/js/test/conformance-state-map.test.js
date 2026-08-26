// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryParse } from '../lib/parser.js';
import { NaxpLanguage, convert } from '../lib/rx-converter.js';
import { RxFactory } from '../lib/rx.js';
import { tryBuild } from '../lib/state-map.js';
import { check } from '../lib/well-formedness.js';
import { loadConformanceData } from './conformance.js';

const data = loadConformanceData();

/**
 * Both machines for a naxp, built from one factory so derivatives stay interned across the two.
 *
 * @param {string} naxp The source.
 * @returns {{canonical: import('../lib/state-map.js').StateMap,
 *   accepted: import('../lib/state-map.js').StateMap}} The machines.
 */
function machines(naxp) {
	const { ast, error } = tryParse(naxp);

	assert.ok(ast !== null, `${naxp} did not parse: ${error}`);
	assert.equal(check(ast), null, `${naxp} failed well-formedness`);

	const factory = new RxFactory();
	const canonical = tryBuild(convert(ast, factory, NaxpLanguage.Canonical), factory);
	const accepted = tryBuild(convert(ast, factory, NaxpLanguage.Accepted), factory);

	assert.ok(canonical.map !== null, `${naxp} canonical: ${canonical.error}`);
	assert.ok(accepted.map !== null, `${naxp} accepted: ${accepted.error}`);

	return { canonical: canonical.map, accepted: accepted.map };
}

test('every case builds both machines inside the state budget', () => {
	for (const item of data.cases) { machines(item.naxp); }
});

test('the canonical machine holds the value count the test data states', () => {
	// The first thing in this port that computes a number the oracle can check.
	const failures = [];

	for (const item of data.cases) {
		const { canonical } = machines(item.naxp);
		const expected = BigInt(item.valueCount);

		if (canonical.valueCount !== expected) {
			failures.push(
				`${item.naxp} counts ${canonical.valueCount}, and the test data says ${expected}.`);
		}

		if (canonical.countSaturated) {
			failures.push(`${item.naxp} saturated, and every case in the data is within W5.`);
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
});

test('the accepted machine holds the accepted count the test data states', () => {
	// The two differ only where the naxp contains a '!', which is what makes this worth asking
	// separately: it is the only check that the accepted language is built from the subject
	// rather than the rendering.
	const failures = [];
	let differing = 0;

	for (const item of data.cases) {
		const { accepted } = machines(item.naxp);
		const expected = BigInt(item.acceptedCount);

		if (accepted.valueCount !== expected) {
			failures.push(
				`${item.naxp} accepts ${accepted.valueCount}, and the test data says ${expected}.`);
		}

		if (item.acceptedCount !== item.valueCount) { ++differing; }
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.ok(differing > 0, 'no case had an accepted count differing from its value count');
});

test('the accepted machine accepts exactly what the test data says it does', () => {
	const failures = [];
	let checked = 0;

	for (const item of data.cases) {
		const { accepted } = machines(item.naxp);

		for (const value of item.values) {
			++checked;

			const shouldAccept = value.out !== '0';

			if (accepted.accepts(value.in) !== shouldAccept) {
				failures.push(
					`${item.naxp} answers accepts('${value.in}') as `
					+ `${accepted.accepts(value.in)}, and the test data says ${shouldAccept}.`);
			}
		}

		for (const refused of item.notAccepted) {
			++checked;

			if (accepted.accepts(refused)) {
				failures.push(`${item.naxp} accepts '${refused}', and the test data says not.`);
			}
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.ok(checked > 500, `only ${checked} strings were checked`);
});

test('the canonical machine accepts every canonical form the test data gives', () => {
	// canon is what decoding must yield, so the canonical language has to hold it.
	const failures = [];

	for (const item of data.cases) {
		const { canonical } = machines(item.naxp);

		for (const value of item.values) {
			if (value.out === '0' || value.canon === undefined) { continue; }

			if (!canonical.accepts(value.canon)) {
				failures.push(`${item.naxp} does not hold the canonical form '${value.canon}'.`);
			}
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
});

test('the naxp the test data refuses for W5 saturates', () => {
	// W5 itself is not checked yet, because that belongs to the compiler. The mechanism it will
	// read is here, so this pins that the mechanism fires.
	const w5 = data.rejected.filter(item => item.rule === 'W5');

	assert.equal(w5.length, 1);

	for (const item of w5) {
		const { ast } = tryParse(item.naxp);
		const factory = new RxFactory();
		const { map } = tryBuild(convert(ast, factory, NaxpLanguage.Canonical), factory);

		assert.ok(map !== null, `${item.naxp} was refused before it could saturate`);
		assert.equal(map.countSaturated, true, item.naxp);
	}
});
