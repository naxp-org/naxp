// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCompile } from '../lib/compiler.js';
import { Naxp } from '../lib/naxp.js';
import { Agreement, compare as compareRanks } from '../lib/rank-agreement.js';
import { compare } from '../lib/value-agreement.js';

// #region Helpers

/**
 * Compiles a naxp, failing the test where it is invalid.
 *
 * @param {string} pattern The pattern.
 * @returns {import('../lib/compiler.js').Compilation} The compilation.
 */
function compiled(pattern) {
	const { compilation, error } = tryCompile(pattern);

	assert.ok(compilation !== null, `${pattern}: ${error}`);

	return compilation;
}

/**
 * Whether two naxps give the same encoded value to every string they both accept.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {string} One of {@link Agreement}.
 */
function values(a, b) {
	return compare(compiled(a), compiled(b)).agreement;
}

/**
 * Every string of a machine's language.
 *
 * @param {import('../lib/state-map.js').StateMap} map The machine.
 * @yields {string} Each string.
 */
function* strings(map) {
	const pending = [{ state: map.start, prefix: '' }];

	while (pending.length > 0) {
		const { state, prefix } = pending.pop();

		if (state.isTerminal) { yield prefix; continue; }

		for (const transition of state.transitions) {
			if (transition.set.isEmpty) { yield prefix; continue; }

			for (const code of transition.set) {
				pending.push({ state: transition.next, prefix: prefix + String.fromCharCode(code) });
			}
		}
	}
}

/**
 * Every string the two share, one at a time through the accepted machines and the public surface.
 * The slow and obvious way, so that the walk has something independent to answer to. Unlike the
 * enumeration in `rank-agreement.test.js` this starts from the accepted language rather than the
 * canonical one, because the strings a unified element rewrites are exactly the ones in question.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @returns {string} One of {@link Agreement}.
 */
function valuesByEnumeration(a, b) {
	const left = compiled(a);
	const right = compiled(b);

	assert.ok(
		left.acceptedCount <= 200000n && right.acceptedCount <= 200000n,
		'Too large to enumerate.');

	for (const side of [left, right]) {
		for (const text of strings(side.accepted)) {
			if (!left.accepts(text) || !right.accepts(text)) { continue; }

			if (left.encode(text) !== right.encode(text)) { return Agreement.Differs; }
		}
	}

	return Agreement.Agrees;
}

/**
 * A witness has to be a string both accept and value differently, or it is no witness.
 *
 * @param {string} a The first pattern.
 * @param {string} b The second pattern.
 * @param {string | null} witness The string the walk offered.
 */
function assertWitness(a, b, witness) {
	assert.notEqual(witness, null);

	const left = Naxp.parse(a);
	const right = Naxp.parse(b);

	assert.ok(left.accepts(witness), `'${witness}' is not accepted by a.`);
	assert.ok(right.accepts(witness), `'${witness}' is not accepted by b.`);
	assert.notEqual(left.encode(witness), right.encode(witness));
}

// #endregion
// #region Canonical forms are not values

// Two naxps can canonicalise a string differently and still value it alike, because each rank is
// taken in its own canonical language. These pairs disagree about what they print and agree about
// every value, which is the distinction the specification draws under "Values". The string given
// is one they print differently, so that the case is not vacuous. Any implementation that decides
// values by comparing canonical forms fails here.
test('different canonical forms can still agree', () => {
	const cases = [
		['(A|B)!A', '(A|B)!B', 'A'],
		['(A|B)!A|C', '(A|B)!B|C', 'A'],
		['[\\s\\-]?!\\-', '[\\s\\-]!?', ''],
		['\\C(\\A\\9\\s!!\\A)', '\\c(\\A\\9\\s!!\\A)', 'A0A'],
	];

	for (const [a, b, printedDifferently] of cases) {
		assert.notEqual(
			Naxp.parse(a).getCanonicalForm(printedDifferently),
			Naxp.parse(b).getCanonicalForm(printedDifferently),
			`${a} against ${b}`);

		assert.equal(values(a, b), Agreement.Agrees, `${a} against ${b}`);
		assert.equal(valuesByEnumeration(a, b), Agreement.Agrees, `${a} against ${b}`);
	}
});

// The other direction. Where the shared canonical strings keep their ranks but a unified element
// sends other strings somewhere else, rank agreement on the canonical languages says nothing, and
// only the values decide. Here the one shared canonical string is 'aa' at value 1 under both, and
// 'ab' is 2 under one and 1 under the other.
test('rank agreement on canonical strings is not enough', () => {
	const cases = [
		['[ab]{2}', '([ab]{2})!(aa)'],
		['[ab]{17}', '([ab]{17})!(a{17})'],
	];

	for (const [a, b] of cases) {
		assert.equal(
			compareRanks(compiled(a).canonical, compiled(b).canonical),
			Agreement.Agrees,
			`${a} against ${b}`);

		const { agreement, witness } = compare(compiled(a), compiled(b));

		assert.equal(agreement, Agreement.Differs, `${a} against ${b}`);
		assertWitness(a, b, witness);
	}
});

// #endregion
// #region Agreement

// The safe edits: a space made optional, a case fold added, an alternative added after everything
// else. Each accepts more and values what it shared exactly as before.
test('safe edits agree', () => {
	const cases = [
		['\\A\\9\\s\\A', '\\A\\9\\s!!\\A'],
		['\\A\\9\\A', '\\C(\\A\\9\\A)'],
		['A!?|B', '\\C(A!?|B)'],
		['[ab]{16}c|([ab]!a){16}d', '[ab]{16}c|([ab]!a){16}d|e'],
		['\\A\\A?\\9\\X? \\s \\9\\A\\A', '\\A\\A?\\9\\X? \\s!! \\9\\A\\A'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '\\C(\\A\\A?\\9\\X? \\s!! \\9\\A\\A)'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '[A-Y]\\A?\\9\\X? \\s!! \\9\\A\\A'],
	];

	for (const [a, b] of cases) {
		assert.equal(values(a, b), Agreement.Agrees, `${a} against ${b}`);
	}
});

// A naxp against itself reaches the same product state by two inputs with different running totals
// many times over, through cross pairs of parses such as '\X?' taken against '\X?' skipped. None
// of those pairs shares a completion, so none is a witness, and an implementation that takes a
// differing arrival as a witness without asking fails on the naxp the site leads with.
test('the postcode against itself agrees', () => {
	const postcode = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';

	assert.equal(values(postcode, postcode), Agreement.Agrees);
});

// #endregion
// #region Disagreement

// The breaking edits, each with a witness that both accept and value differently. Adding GIR 0AA
// and removing a middle letter move the codes around them; dropping the space rather than
// reproducing it, and marking the padding of a decimal range, change what the shared strings
// canonicalise to.
test('breaking edits differ', () => {
	const cases = [
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '[A-PR-Z]\\A?\\9\\X? \\s!! \\9\\A\\A'],
		['\\A\\A?\\9\\X? \\s!! \\9\\A\\A', '\\A\\A?\\9\\X? \\s!? \\9\\A\\A'],
		['#[0-105]', '#[0?0!0-105]'],
		['#[0!0-105]', '#[0?0-105]'],
	];

	for (const [a, b] of cases) {
		const { agreement, witness } = compare(compiled(a), compiled(b));

		assert.equal(agreement, Agreement.Differs, `${a} against ${b}`);
		assertWitness(a, b, witness);
	}
});

// The site's first divergent value for the GIR 0AA comparison, which the walk finds as the
// shortest witness.
test('adding GIR to the postcode finds the first divergent value', () => {
	const without = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A';
	const withGir = '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA';

	const { agreement, witness } = compare(compiled(without), compiled(withGir));

	assert.equal(agreement, Agreement.Differs);
	assert.equal(Naxp.parse(without).encode(witness), 405194401n);
	assert.equal(Naxp.parse(withGir).encode(witness), 1688310001n);
});

// #endregion
// #region Against the slow way

// The walk and an enumeration of every shared string must reach the same verdict, over naxps with
// and without unified elements. Those without are the pairs `rank-agreement.test.js` checks the
// same way, so the two walks are held to one answer on them.
test('the walk agrees with enumeration', () => {
	const cases = [
		['[AB]', '[ABC]'],
		['[AC]', '[ABC]'],
		['AB|AC', 'AB|AC|AD'],
		['AB|AD', 'AB|AC|AD'],
		['\\A\\9', '\\A\\X'],
		['A|BB', 'A|AB|BB'],
		['\\A{2}', '\\A{2,3}'],
		['#[00-99]', '#[00-99]|AA'],
		['(A|B)!A', '(A|B)!B'],
		['(A|B)!A|C', '(A|B)!B|C'],
		['[\\s\\-]?!\\-', '[\\s\\-]!?'],
		['[ab]{2}', '([ab]{2})!(aa)'],
		['[ab]{3}', '([ab]{3})!(aab)'],
		['\\A\\9\\s\\A', '\\A\\9\\s!!\\A'],
		['\\A\\9\\A', '\\C(\\A\\9\\A)'],
		['\\C(\\A\\9\\s!!\\A)', '\\c(\\A\\9\\s!!\\A)'],
		['A!?|B', '\\C(A!?|B)'],
		['A B!! C', 'A B? C'],
		['A B!? C', 'A B? C'],
		['[ab]{8}c|([ab]!a){8}d', '[ab]{8}c|([ab]!a){8}d|e'],
		['[ab]{8}c|([ab]!a){8}d', '[ab]{8}(c|e)|([ab]!a){8}d'],
		['#[0-105]', '#[0?0!0-105]'],
		['#[0!0-105]', '#[0?0-105]'],
		['#[00!0-999]', '#[0!00-999]'],
	];

	for (const [a, b] of cases) {
		assert.equal(values(a, b), valuesByEnumeration(a, b), `${a} against ${b}`);
	}
});

// Where neither naxp holds a unified element the two walks decide the same question by different
// routes, one over the canonical machines and one over parses, and must agree.
test('without unified elements it agrees with rank agreement', () => {
	const cases = [
		['[AB]', '[ABC]'],
		['[AC]', '[ABC]'],
		['AC', 'AB|AC'],
		['\\9\\A', '\\X\\A'],
		['\\A{2,3}', '\\A{2}'],
	];

	for (const [a, b] of cases) {
		assert.ok(compiled(a).canonicalIsIdentity && compiled(b).canonicalIsIdentity, `${a} against ${b}`);

		assert.equal(
			values(a, b),
			compareRanks(compiled(a).canonical, compiled(b).canonical),
			`${a} against ${b}`);
	}
});

// #endregion
// #region Direction

// The question is symmetric. It asks about the strings both hold, and neither side is privileged.
test('swapping the arguments keeps the verdict', () => {
	const cases = [
		['[ab]{2}', '([ab]{2})!(aa)'],
		['(A|B)!A', '(A|B)!B'],
		['\\A\\9\\s\\A', '\\A\\9\\s!!\\A'],
		['#[0-105]', '#[0?0!0-105]'],
	];

	for (const [a, b] of cases) {
		assert.equal(values(a, b), values(b, a), `${a} against ${b}`);
	}
});

// #endregion
// #region The budget

// A walk given no room says so rather than guessing, and offers no witness.
test('a budget too small is undecided', () => {
	const a = compiled('\\A\\A?\\9\\X? \\s!! \\9\\A\\A');
	const b = compiled('\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA');

	const { agreement, witness } = compare(a, b, 2);

	assert.equal(agreement, Agreement.Undecided);
	assert.equal(witness, null);
});

// The pair on which a square seeded with two roots would hold a distinct delay for every distinct
// prefix, 2^17 of them, and exhaust any budget. Values need nothing held, so this is decided in a
// handful of product states.
test('a held rendering against a copy is cheap', () => {
	const a = compiled('[ab]{17}');
	const b = compiled('([ab]{17})!(a{17})');

	assert.equal(compare(a, b, 64).agreement, Agreement.Differs);
});

// #endregion
