// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryParse } from '../lib/parser.js';
import { NaxpLanguage, convert } from '../lib/rx-converter.js';
import { RxFactory } from '../lib/rx.js';
import { tryBuild } from '../lib/state-map.js';
import { checkW3 } from '../lib/w3-checker.js';
import { check } from '../lib/well-formedness.js';
import { ReferenceOutcome, tryCanonicalise } from './reference-canonicaliser.js';
import { isImplementationLimit, ruleOf } from './naxp-message-rules.js';

/** Naxps with more strings than this are skipped rather than enumerated. */
const MAX_LANGUAGE = 4096n;

/**
 * Sequences and alternations over a pool of elements chosen to put replaceable and
 * non-replaceable ways of matching the same characters next to one another.
 *
 * The naxps are generated rather than listed, because the interesting cases are the ones nobody
 * thinks to write down.
 *
 * @returns {string[]} The naxps.
 */
function generatedNaxps() {
	const units = [
		'A', 'B', 'A?', 'B?', 'A!!', 'B!!', 'A!?', 'B!?', '(A|B)!A', '(A|B)!B', '(A|B)?',
	];

	const naxps = [];

	for (const first of units) {
		naxps.push(first);

		for (const second of units) {
			naxps.push(first + second);
			naxps.push(`${first}|${second}`);

			for (const third of units) { naxps.push(first + second + third); }
		}
	}

	// Intervals, where skipping a copy is itself an emission and a fixed count is the difference
	// between one output and several.
	const counts = ['{2}', '{3}', '{0,2}', '{1,2}', '{1,3}', '{0,3}'];

	for (const unit of units) {
		for (const count of counts) {
			naxps.push(`(${unit})${count}`);
			naxps.push(`(${unit})${count}A`);
			naxps.push(`A(${unit})${count}`);

			for (const tail of units) { naxps.push(`(${unit})${count}${tail}`); }
		}
	}

	return naxps;
}

/**
 * Every string the naxp accepts, or null where there are too many to be worth checking.
 *
 * The C# decodes each value in turn, which needs the codec. Walking the machine reaches the same
 * strings and needs only what is ported.
 *
 * @param {import('../lib/ast.js').Ast} ast The tree.
 * @returns {string[] | null} The language, or null.
 */
function enumerateLanguage(ast) {
	const factory = new RxFactory();
	const { map } = tryBuild(convert(ast, factory, NaxpLanguage.Accepted), factory);

	if (map === null || map.countSaturated || map.valueCount > MAX_LANGUAGE) { return null; }

	const language = [];
	const walk = (state, prefix) => {
		if (state.acceptsEndOfText) { language.push(prefix); }

		for (const transition of state.transitions) {
			if (transition.set.isEmpty) { continue; }

			for (const code of transition.set) {
				walk(transition.next, prefix + String.fromCharCode(code));
			}
		}
	};

	walk(map.start, '');

	assert.equal(
		BigInt(language.length),
		map.valueCount,
		'the walk of the machine and its own count disagree');

	return language;
}

test('the static rule agrees with the per-string rule over generated naxps', () => {
	// The reference canonicaliser decides ambiguity for one string by walking the tree and
	// carrying every output, which shares no reasoning with the square. A naxp's language is
	// finite and can be enumerated, so the two can be compared exhaustively: the square must
	// refuse a naxp exactly when some string of its language has more than one canonical form.
	const failures = [];
	let compared = 0;

	for (const naxp of generatedNaxps()) {
		const { ast } = tryParse(naxp);

		if (ast === null) { continue; }
		if (check(ast) !== null) { continue; }

		const language = enumerateLanguage(ast);

		if (language === null) { continue; }

		let ambiguous = null;

		for (const text of language) {
			if (tryCanonicalise(ast, text).outcome === ReferenceOutcome.Ambiguous) {
				ambiguous = text;
				break;
			}
		}

		const error = checkW3(ast, new RxFactory());

		if (error !== null && isImplementationLimit(error.message)) { continue; }

		++compared;

		if (error === null && ambiguous !== null) {
			failures.push(`${naxp} was accepted, but '${ambiguous}' has more than one canonical form.`);
		} else if (error !== null && ambiguous === null) {
			failures.push(`${naxp} was refused (${error}), but no string of its language is ambiguous.`);
		}
	}

	assert.ok(compared > 2000, `only ${compared} naxps were compared, which is too few to mean anything`);
	assert.deepEqual(failures, [], `${failures.length} disagreements:\n${failures.join('\n')}`);
});

/**
 * A second pool, over character sets rather than single characters.
 *
 * The pool above is all single characters, so a copied character is always already concrete and
 * the checker never has to narrow a block. These reach the code that does: `A!![ab]{2}|[ab]{2}` is
 * the smallest naxp found that compares an undecided copy against a non-empty delay.
 *
 * @returns {string[]} The naxps.
 */
function generatedSetNaxps() {
	const units = [
		'A', '[ab]', '[ab]?', '[ab]{2}', '[ab]!a', '[ab]!b', 'A!!', 'a!!', '(a|ab)!a',
		'(a|ab)!(ab)', '([ab]!a)?',
	];

	const naxps = new Set();

	for (const first of units) {
		naxps.add(first);

		for (const second of units) {
			naxps.add(first + second);
			naxps.add(`${first}|${second}`);

			for (const third of units) {
				naxps.add(first + second + third);
				naxps.add(`${first}${second}|${third}`);
				naxps.add(`${first}|${second}${third}`);
			}
		}
	}

	return [...naxps];
}

test('the static rule agrees with the per-string rule over naxps with character sets', () => {
	const failures = [];
	let compared = 0;

	for (const naxp of generatedSetNaxps()) {
		const { ast } = tryParse(naxp);

		if (ast === null) { continue; }
		if (check(ast) !== null) { continue; }

		const language = enumerateLanguage(ast);

		if (language === null) { continue; }

		let ambiguous = null;

		for (const text of language) {
			if (tryCanonicalise(ast, text).outcome === ReferenceOutcome.Ambiguous) {
				ambiguous = text;
				break;
			}
		}

		const error = checkW3(ast, new RxFactory());

		if (error !== null && isImplementationLimit(error.message)) { continue; }

		++compared;

		if (error === null && ambiguous !== null) {
			failures.push(`${naxp} was accepted, but '${ambiguous}' has more than one canonical form.`);
		} else if (error !== null && ambiguous === null) {
			failures.push(`${naxp} was refused (${error}), but no string of its language is ambiguous.`);
		}
	}

	assert.ok(compared > 1000, `only ${compared} naxps were compared`);
	assert.deepEqual(failures, [], `${failures.length} disagreements:\n${failures.join('\n')}`);
});

test('an undecided copy is compared against a delay rather than guessed', () => {
	// The four naxps found that reach that comparison at all. Both branches emit a copied
	// character whose identity the block has not fixed, while one of them is already a rendering
	// ahead of the other.
	for (const naxp of [
		'A!![ab]{2}|[ab]{2}',
		'B!![ab]{2}|[ab]{2}',
		'[ab]{2}|A!![ab]{2}',
		'[ab]{2}|B!![ab]{2}',
	]) {
		const { ast } = tryParse(naxp);

		assert.equal(check(ast), null, naxp);

		const error = checkW3(ast, new RxFactory());

		assert.ok(error !== null, `${naxp} was accepted`);
		assert.equal(ruleOf(error.message), 'W3', `${naxp}: ${error.text}`);

		// And the oracle agrees, so the refusal is right rather than merely present.
		const language = enumerateLanguage(ast);
		const ambiguous = language.filter(
			text => tryCanonicalise(ast, text).outcome === ReferenceOutcome.Ambiguous);

		assert.deepEqual(ambiguous, ['aa', 'ab', 'ba', 'bb'], naxp);
	}
});

test('the reference canonicaliser sees the ambiguity the specification describes', () => {
	// A guard on the oracle itself. If it stopped finding this it would agree with anything.
	const { ast } = tryParse('AB!!B?C');

	// ABC is the string the specification works through: read the B as the replaceable element
	// with the optional one absent and the form is ABC, read it the other way round and it is
	// ABBC.
	assert.equal(tryCanonicalise(ast, 'ABC').outcome, ReferenceOutcome.Ambiguous);

	// The rest of the neighbourhood is unambiguous, which is what makes ABC the interesting one.
	assert.deepEqual(
		tryCanonicalise(ast, 'ABBC'),
		{ outcome: ReferenceOutcome.Single, canonical: 'ABBC' });

	// A replaceable emits its rendering even where it matched nothing, so AC gains a B.
	assert.deepEqual(
		tryCanonicalise(ast, 'AC'),
		{ outcome: ReferenceOutcome.Single, canonical: 'ABC' });

	assert.equal(tryCanonicalise(ast, 'ABBBC').outcome, ReferenceOutcome.NotAccepted);
});

test('the reference canonicaliser agrees with the specification on a well-formed naxp', () => {
	const { ast } = tryParse('\\A\\9\\s!!\\9\\A');

	assert.deepEqual(tryCanonicalise(ast, 'M1 1A'), {
		outcome: ReferenceOutcome.Single,
		canonical: 'M1 1A',
	});

	assert.deepEqual(tryCanonicalise(ast, 'M11A'), {
		outcome: ReferenceOutcome.Single,
		canonical: 'M1 1A',
	});
});

// #region Abandoned decisions

test('an abandoned derivative does not blame the pair state budget', () => {
	// (A!!){66} passes more skipped copies of an interval than the derivative will follow, so it
	// gives up. The naxp is legal and the message has to say so, and must not claim to have run
	// out of pair states, which is a different limit and was not the one hit.
	const { ast } = tryParse('(A!!){66}');

	assert.equal(check(ast), null);

	const error = checkW3(ast, new RxFactory());

	assert.ok(error !== null, 'accepted');
	assert.equal(ruleOf(error.message), 'ImplementationLimit');
	assert.ok(!error.text.includes('pair states'), error.text);
	assert.ok(error.text.includes('may well be legal'), error.text);
});

test('a spent pair state budget says so', () => {
	const { ast } = tryParse('[ab]{6}c|([ab]!a){6}d');

	assert.equal(check(ast), null);

	const error = checkW3(ast, new RxFactory(), { maxStates: 8 });

	assert.ok(error !== null, 'accepted');
	assert.equal(ruleOf(error.message), 'ImplementationLimit');
	assert.ok(error.text.includes('pair states'), error.text);
});

// #endregion
