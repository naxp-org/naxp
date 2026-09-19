// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { AsciiCharSet, ALL_DIGITS } from '../lib/ascii-char-set.js';
import {
	AstAlternation,
	AstChars,
	AstDecimalRange,
	AstInterval,
	AstOptional,
	AstUnified,
	UnifiedForm,
	containsUnified,
} from '../lib/ast.js';
import { tryCompile } from '../lib/compiler.js';
import { Naxp } from '../lib/naxp.js';
import { tryParse } from '../lib/parser.js';
import { check } from '../lib/well-formedness.js';
import { ruleOf } from './naxp-message-rules.js';

// #region Helpers

/**
 * Parses and checks, failing the test if the pattern is invalid.
 *
 * @param {string} text The pattern.
 * @returns {import('../lib/ast.js').Ast} The tree.
 */
function parse(text) {
	const { ast, error } = tryParse(text);

	assert.ok(ast !== null, `${text} did not parse: ${error}`);
	assert.equal(check(ast), null, `${text} was invalid.`);

	return ast;
}

/**
 * Parses and checks, failing the test if the pattern is accepted.
 *
 * @param {string} text The pattern.
 * @returns {import('../lib/naxp-error.js').NaxpError} The fault.
 */
function faultOf(text) {
	const { ast, error } = tryParse(text);

	if (ast === null) { return error; }

	const wellFormedness = check(ast);

	assert.ok(wellFormedness !== null, `${text} was accepted.`);

	return wellFormedness;
}

/**
 * A set built from the characters of a string.
 *
 * @param {string} characters The characters.
 * @returns {AsciiCharSet} The set.
 */
function setOf(characters) {
	let set = AsciiCharSet.empty;

	for (const c of characters) { set = set.union(AsciiCharSet.fromSingleChar(c.charCodeAt(0))); }

	return set;
}

/**
 * The canonical characters a folded set prints, in the order the branches hold them.
 *
 * @param {string} pattern The pattern.
 * @returns {string} The renderings, joined.
 */
function renderingsOf(pattern) {
	const renderings = [];

	collect(parse(pattern));

	return renderings.join('');

	/** @param {import('../lib/ast.js').Ast} node The node to collect from. */
	function collect(node) {
		if (node instanceof AstUnified && node.form === UnifiedForm.Fold) {
			renderings.push(String.fromCharCode(node.rendering.charSet.singleCharacter));
		} else if (node instanceof AstAlternation) {
			for (const child of node.children) { collect(child); }
		}
	}
}

// #endregion
// #region What a fold expands to

test('a fold over one letter is a pair unified to the canonical case', () => {
	const upper = parse('\\CA');

	assert.ok(upper instanceof AstUnified);
	assert.equal(upper.form, UnifiedForm.Fold);
	assert.ok(upper.subject.charSet.equals(setOf('Aa')));
	assert.ok(upper.rendering.charSet.equals(setOf('A')));

	const lower = parse('\\cA');

	assert.ok(lower instanceof AstUnified);
	assert.ok(lower.subject.charSet.equals(setOf('Aa')));
	assert.ok(lower.rendering.charSet.equals(setOf('a')));
});

test('the case a set is written in does not decide the canonical case', () => {
	assert.equal(renderingsOf('\\C[A-F]'), renderingsOf('\\C[a-f]'));
	assert.equal(renderingsOf('\\C[A-F]'), renderingsOf('\\C[a-fA-F]'));
	assert.equal(renderingsOf('\\C[a-f]'), 'ABCDEF');
	assert.equal(renderingsOf('\\c[A-F]'), 'abcdef');
});

test('a fold over a mixed set leaves the uncased characters alone', () => {
	const alternation = parse('\\C[\\9A-F]');

	assert.ok(alternation instanceof AstAlternation);
	assert.equal(alternation.children.length, 7);
	assert.ok(alternation.children[0] instanceof AstChars);
	assert.ok(alternation.children[0].charSet.equals(ALL_DIGITS));
	assert.equal(renderingsOf('\\C[\\9A-F]'), 'ABCDEF');
});

test('a fold over characters with no case does nothing', () => {
	const digits = parse('\\C\\9');

	assert.ok(digits instanceof AstChars);
	assert.ok(digits.charSet.equals(ALL_DIGITS));
	assert.ok(parse('\\C#[0-10]') instanceof AstDecimalRange);
	assert.equal(containsUnified(parse('\\C\\9{4}')), false);
});

// #endregion
// #region What a fold binds to

test('a case fold runs to the end of the enclosing group', () => {
	const naxp = Naxp.parse('\\CAB');

	assert.ok(naxp.accepts('aB'));
	assert.ok(naxp.accepts('Ab'));
	assert.ok(naxp.accepts('ab'));
	assert.equal(naxp.getCanonicalForm('ab'), 'AB');
});

test('grouping the case fold is how it is stopped short', () => {
	const naxp = Naxp.parse('(\\CA)B');

	assert.ok(naxp.accepts('aB'));
	assert.ok(!naxp.accepts('Ab'));
});

test('a case fold crosses an alternation', () => {
	const naxp = Naxp.parse('\\CA|b');

	assert.ok(naxp.accepts('a'));
	assert.ok(naxp.accepts('B'));
	assert.equal(naxp.getCanonicalForm('b'), 'B');
	assert.equal(naxp.maxEncodedValue, 2n);
});

test('a case fold written after another sits inside it, and the outer governs', () => {
	const naxp = Naxp.parse('\\Ca\\cb');

	assert.equal(naxp.getCanonicalForm('ab'), 'AB');
	assert.equal(naxp.maxEncodedValue, 1n);
});

test('a case fold cannot begin a rendering', () => {
	const error = faultOf('A!\\CA');

	assert.equal(error.message, 'NAXP1058_FoldBeginsRendering');
	assert.equal(ruleOf(error.message), 'syntax');
	assert.equal(error.offset, 2);
});

test('a fold binds looser than unification', () => {
	const unified = parse('\\CA!A');

	assert.ok(unified instanceof AstUnified);
	assert.equal(unified.form, UnifiedForm.Explicit);
	assert.ok(unified.subject.charSet.equals(setOf('Aa')));
	assert.ok(unified.rendering.charSet.equals(setOf('A')));
});

test('a fold binds looser than a quantifier', () => {
	assert.ok(parse('\\CA?') instanceof AstOptional);
	assert.ok(parse('\\CA{2}') instanceof AstInterval);
	assert.ok(Naxp.parse('\\CA{2}').accepts('aA'));
});

test("a fold over a '!!' widens the subject and keeps the rendering", () => {
	const naxp = Naxp.parse('\\C((AB)!!)');

	assert.ok(naxp.accepts('ab'));
	assert.ok(naxp.accepts('aB'));
	assert.ok(naxp.accepts(''));
	assert.equal(naxp.getCanonicalForm('ab'), 'AB');
	assert.equal(naxp.maxEncodedValue, 1n);
});

test('the outer fold governs a fold within its extent', () => {
	const naxp = Naxp.parse('\\C(AB\\cC)');

	assert.equal(naxp.getCanonicalForm('abc'), 'ABC');
	assert.equal(naxp.getCanonicalForm('ABC'), 'ABC');
});

test('a fold written on a fold is the outer one', () => {
	const naxp = Naxp.parse('\\C\\cA');

	assert.equal(naxp.getCanonicalForm('a'), 'A');
	assert.equal(naxp.maxEncodedValue, 1n);
	assert.equal(naxp.getCanonicalForm('a'), Naxp.parse('\\CA').getCanonicalForm('a'));
});

// #endregion
// #region Wrapping an existing naxp

test('wrapping the postcode adds spellings and moves no value', () => {
	const postcode = '\\A \\A? \\9 \\X? \\s!! \\9 \\A \\A';

	const plain = Naxp.parse(postcode);
	const folded = Naxp.parse(`\\C(${postcode})`);

	assert.equal(plain.maxEncodedValue, 1_755_842_400n);
	assert.equal(folded.maxEncodedValue, plain.maxEncodedValue);

	for (const spelling of ['EC1A 1BB', 'ec1a 1bb', 'Ec1A 1bB', 'ec1a1bb']) {
		assert.ok(folded.accepts(spelling), spelling);
		assert.equal(folded.getCanonicalForm(spelling), 'EC1A 1BB');
		assert.equal(folded.encode(spelling), plain.encode('EC1A 1BB'));
	}

	assert.ok(!plain.accepts('ec1a 1bb'));
	assert.equal(folded.decode(folded.encode('ec1a 1bb')), plain.decode(plain.encode('EC1A 1BB')));
});

test('the wrong fold keeps the values and changes what they decode to', () => {
	const plain = Naxp.parse('[a-f]{2}');
	const wrapped = Naxp.parse('\\C([a-f]{2})');

	assert.equal(plain.maxEncodedValue, 36n);
	assert.equal(wrapped.maxEncodedValue, 36n);
	assert.equal(wrapped.encode('cd'), plain.encode('cd'));
	assert.equal(plain.decode(plain.encode('cd')), 'cd');
	assert.equal(wrapped.decode(wrapped.encode('cd')), 'CD');

	// \c is the fold that preserves this naxp, since its canonical strings are lower case.
	const right = Naxp.parse('\\c([a-f]{2})');

	assert.equal(right.decode(right.encode('CD')), 'cd');
});

test('folding can break W3', () => {
	assert.notEqual(tryCompile('A!?|a').compilation, null);

	const { compilation, error } = tryCompile('\\C(A!?|a)');

	assert.equal(compilation, null);
	assert.equal(ruleOf(error.message), 'W3');
});

// #endregion
// #region Where a fold may not go

test('a fold inside a character set is a syntax error', () => {
	const error = faultOf('[\\CA]');

	assert.equal(error.message, 'NAXP1052_FoldInCharacterSet');
	assert.ok(error.text.includes('before the set'), error.text);
});

test("a fold inside a unified operand breaks W2", () => {
	assert.equal(faultOf('(\\CA)!A').message, 'NAXP1039_UnifiedNested');
	assert.equal(faultOf('A!(\\CA)').message, 'NAXP1039_UnifiedNested');
});

// #endregion
