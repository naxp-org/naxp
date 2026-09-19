// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCompile } from '../lib/compiler.js';
import { NaxpValueType } from '../lib/emitter.js';
import { CSharpEmitter } from '../lib/csharp-emitter.js';
import { COPY_MARKER } from '../lib/tx.js';
import { loadConformanceData } from './conformance.js';

const data = loadConformanceData();

/**
 * @param {string} naxp The pattern.
 * @param {string} prefix The prefix every generated name starts with.
 * @param {string} valueType The integer type for encoded values.
 * @param {string} initialIndent What every line starts with.
 * @returns {string} The fragment.
 */
function emit(naxp, prefix = '', valueType = NaxpValueType.UInt64, initialIndent = '') {
	const { compilation, error } = tryCompile(naxp);

	assert.ok(compilation !== null, `${naxp} did not compile: ${error}`);

	return CSharpEmitter.instance.emit(compilation, prefix, valueType, initialIndent);
}

test('the constants carry the count and the longest string', () => {
	const source = emit('#[1-12]');

	assert.ok(source.includes('public const ulong MaxEncodedValue = 12UL;'));
	assert.ok(source.includes('public const int MaxLength = 2;'));
});

test('emitting the same naxp twice gives the same text', () => {
	assert.equal(emit('K9\\9 9K9'), emit('K9\\9 9K9'));
});

test('the fragment carries no namespace, class or using', () => {
	const source = emit('A|B');

	assert.ok(!source.includes('namespace'));
	assert.ok(!source.includes('class'));
	assert.ok(!source.includes('using'));
});

test('the prefix starts every name', () => {
	const source = emit('A|B', 'Postcode');

	assert.ok(source.includes('public const ulong PostcodeMaxEncodedValue = 2UL;'));
	assert.ok(source.includes('public static bool PostcodeAccepts(global::System.ReadOnlySpan<char> text)'));
	assert.ok(source.includes('public static string PostcodeDecode(ulong value)'));
	assert.ok(source.includes('static int PostcodeAcceptStep(int state, char c)'));

	// No name escapes the prefix: the bare names never appear followed by their own syntax.
	assert.ok(!source.includes(' MaxEncodedValue'));
	assert.ok(!source.includes(' Accepts('));
	assert.ok(!source.includes('"MaxEncodedValue"'));
});

test('a blank prefix gives the bare names', () => {
	assert.ok(emit('A').includes('public static bool Accepts(global::System.ReadOnlySpan<char> text)'));
});

test('a prefix that is not an ASCII identifier is refused', () => {
	// A name of Unicode letters must be invalid too, because the identifier rule is ASCII across
	// every target language.
	for (const prefix of ['1Bad', 'Bad.Name', 'Bad Name', 'Näxp']) {
		assert.throws(() => emit('A', prefix), TypeError, `'${prefix}' was accepted`);
	}
});

test('the value type changes the boundary and leaves the steppers in ulong', () => {
	const source = emit('#[1-12]', '', NaxpValueType.UInt8);

	assert.ok(source.includes('public const byte MaxEncodedValue = 12;'));
	assert.ok(source.includes('public static byte Encode(global::System.ReadOnlySpan<char> text)'));
	assert.ok(source.includes('public static string Decode(byte value)'));
	assert.ok(source.includes('public static bool TryDecode(byte value, global::System.Span<char> destination, out int charsWritten)'));
	assert.ok(source.includes('static int EncodeStep(int state, char c, ref ulong total)'));
});

test('a value type the naxp outgrows is refused', () => {
	// 128 values: one more than a signed byte holds, exactly what an unsigned byte holds.
	assert.throws(() => emit('#[1-128]', '', NaxpValueType.Int8), RangeError);
	assert.ok(emit('#[1-128]', '', NaxpValueType.UInt8).includes('public const byte MaxEncodedValue = 128;'));
});

test('the initial indent starts every line of the fragment', () => {
	for (const line of emit('A|B', '', NaxpValueType.UInt64, '\t\t').split('\n')) {
		if (line.length === 0) { continue; }

		assert.ok(line.startsWith('\t\t'), `'${line}' does not start with the initial indent`);
	}
});

test('a machine above the chunk size is split behind a dispatcher', () => {
	// Only long literal runs get here, so the naxp is three of them.
	const source = emit('A{99}B{99}C{99}');

	assert.ok(source.includes('if (state < 250) { return AcceptStep0(state, c); }'));
	assert.ok(source.includes('static int AcceptStep1(int state, char c)'));
	assert.ok(source.includes('public const int MaxLength = 297;'));

	// The canonicalising machine chunks by the same rule.
	assert.ok(emit('(B|b)!BA{99}C{99}D{99}').includes('static int CanonicalStep1('));
});

test('a naxp with nothing unified carries no canonicaliser', () => {
	const source = emit('AB|C');

	assert.ok(!source.includes('CanonicalStep'));
	assert.ok(!source.includes('FinishCanonical'));
	assert.ok(!source.includes('Rank'));
});

test('a naxp that replaces canonicalises before it ranks', () => {
	const source = emit('(B|b)!B');

	assert.ok(source.includes('static int CanonicalStep('));
	assert.ok(source.includes('static int FinishCanonical('));
	assert.ok(source.includes('static ulong Rank('));

	// The copy marker is internal to the transducer and must never survive into source.
	assert.ok(!source.includes(COPY_MARKER));
});

test('a naxp reaching back to an earlier character keeps a register', () => {
	const source = emit('\\A\\A?\\9\\X? \\s!! \\9\\A\\A');

	assert.ok(source.includes('global::System.Span<char> held = stackalloc char[2];'));
	assert.ok(source.includes('for (int h = 0; h < 1; ++h) { held[h] = held[h + 1]; }'));
	assert.ok(source.includes('canonical[length++] = held[0];'));
});

test('every conformance naxp emits', () => {
	for (const item of data.cases) {
		const source = emit(item.naxp);

		assert.ok(
			source.includes('public static ulong Encode(global::System.ReadOnlySpan<char> text)'),
			`${item.naxp} has no Encode over chars`);
		assert.ok(
			source.includes('public static ulong Encode(global::System.ReadOnlySpan<byte> text)'),
			`${item.naxp} has no Encode over bytes`);
		assert.ok(source.includes('public static string Decode(ulong value)'), `${item.naxp} has no Decode`);
		assert.ok(!source.includes(COPY_MARKER), `${item.naxp} leaked a copy marker`);
	}
});
