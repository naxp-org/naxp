// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

import { tryCompile } from '../lib/compiler.js';
import { Emitter, NaxpValueType } from '../lib/emitter.js';
import { JavaScriptEmitter } from '../lib/javascript-emitter.js';
import { COPY_MARKER } from '../lib/tx.js';
import { loadConformanceData } from './conformance.js';

const data = loadConformanceData();

/**
 * @param {string} naxp The pattern.
 * @returns {import('../lib/compiler.js').Compilation} The compilation.
 */
function compile(naxp) {
	const { compilation, error } = tryCompile(naxp);

	assert.ok(compilation !== null, `${naxp} did not compile: ${error}`);

	return compilation;
}

/**
 * @param {string} naxp The pattern.
 * @param {string} prefix The prefix every generated name starts with.
 * @param {string} valueType The integer type for encoded values.
 * @param {string} initialIndent What every line starts with.
 * @returns {string} The fragment.
 */
function emit(naxp, prefix = '', valueType = NaxpValueType.UInt64, initialIndent = '') {
	return JavaScriptEmitter.instance.emit(compile(naxp), prefix, valueType, initialIndent);
}

/**
 * A naxp's fragment, evaluated, so the tests can ask the generated code what it does rather than
 * only what it says. The C# suite writes a file and runs Node over it, because the fragment has to
 * cross a language boundary to be run at all; here it does not, so the functions come back
 * directly and a failure names the case.
 *
 * @param {string} naxp The pattern.
 * @returns {{pattern: string, maxEncodedValue: number | bigint, maxLength: number,
 *   accepts: (text: string | Uint8Array) => boolean,
 *   encode: (text: string | Uint8Array) => number | bigint,
 *   decode: (value: number | bigint) => string,
 *   decodeToBytes: (value: number | bigint) => Uint8Array,
 *   tryDecode: (value: number | bigint) => string | null,
 *   getCanonicalForm: (text: string | Uint8Array) => string | null}} The generated members.
 */
function build(naxp) {
	const source = emit(naxp);
	const members = 'pattern, maxEncodedValue, maxLength, accepts, encode, decode, decodeToBytes, tryDecode, getCanonicalForm';

	return new Function(`${source}\nreturn { ${members} };`)();
}

/**
 * @param {string} text ASCII text.
 * @returns {Uint8Array} Its bytes.
 */
function toBytes(text) {
	const bytes = new Uint8Array(text.length);

	for (let i = 0; i < text.length; i++) { bytes[i] = text.charCodeAt(i); }

	return bytes;
}

/**
 * @param {Uint8Array} bytes ASCII bytes.
 * @returns {string} The text.
 */
function fromBytes(bytes) {
	return String.fromCharCode.apply(null, Array.from(bytes));
}

test('the prefix is camel cased onto every name', () => {
	const source = emit('\\A\\9', 'Postcode');

	assert.match(source, /const postcodePattern = '/);
	assert.match(source, /const postcodeMaxEncodedValue = 260;/);
	assert.match(source, /const postcodeMaxLength = 2;/);
	assert.match(source, /function postcodeAccepts\(text\) \{/);
	assert.match(source, /function postcodeEncode\(text\) \{/);
	assert.match(source, /function postcodeDecode\(value\) \{/);
	assert.match(source, /function postcodeDecodeToBytes\(value\) \{/);
	assert.match(source, /function postcodeTryDecode\(value\) \{/);
	assert.match(source, /function postcodeGetCanonicalForm\(text\) \{/);
});

test('the pattern is a literal that gives back the naxp', () => {
	// A quote, a backslash, a tab and a newline, which are what a pattern can hold that needs
	// escaping.
	for (const naxp of ['\\A\\9', "A'B", 'A\tB\nC', '\\\\']) {
		assert.equal(build(naxp).pattern, naxp);
	}
});

test('a blank prefix gives the bare names', () => {
	const source = emit('\\A\\9');

	assert.match(source, /function accepts\(text\) \{/);
	assert.match(source, /const maxEncodedValue = 260;/);
});

test('one function serves strings and bytes', () => {
	const source = emit('\\A\\9');

	assert.ok(source.includes("const bytes = typeof text !== 'string';"));
	assert.ok(source.includes('const c = bytes ? text[i] : text.charCodeAt(i);'));
	assert.ok(source.includes('c >= 0x41 && c <= 0x5A'));
});

test('values are numbers where every one of them fits a number exactly', () => {
	const source = emit('\\A\\9');

	assert.ok(source.includes('@returns {number}'));
	assert.ok(!source.includes('n;'));
	assert.ok(!source.includes('BigInt('));
});

test('values are BigInt above the safe range', () => {
	// Twelve letters is 95 428 956 661 682 176 values, past the 2^53 - 1 a JavaScript number holds
	// exactly, so the fragment turns to BigInt.
	const source = emit('\\A{12}');

	assert.ok(source.includes('const maxEncodedValue = 95_428_956_661_682_176n;'));
	assert.ok(source.includes('@returns {bigint}'));
	assert.ok(source.includes('acc.total += '));
	assert.ok(source.includes('BigInt(c - 0x41)'));
});

test('a naxp that replaces canonicalises before it ranks', () => {
	const source = emit('(B|b)!B');

	assert.ok(source.includes('canonicalStep(state,'));
	assert.ok(source.includes('finishCanonical(state, canonical)'));
	assert.ok(source.includes('rank(canonical)'));

	// The copy marker is internal to the transducer and must never survive into source.
	assert.ok(!source.includes(COPY_MARKER));
});

test('a naxp with nothing unified carries no canonicaliser', () => {
	const source = emit('AB|C');

	assert.ok(!source.includes('canonicalStep'));
	assert.ok(!source.includes('finishCanonical'));
	assert.ok(!source.includes('rank'));
});

test('a machine above the chunk size is split behind a dispatcher', () => {
	// Only long literal runs get here, so the naxp is three of them.
	const source = emit('A{99}B{99}C{99}');

	assert.ok(source.includes('if (state < 250) { return acceptStep0(state, c); }'));
	assert.ok(source.includes('function acceptStep1(state, c) {'));
	assert.ok(source.includes('const maxLength = 297;'));

	// The canonicalising machine chunks by the same rule.
	assert.ok(emit('(B|b)!BA{99}C{99}D{99}').includes('function canonicalStep1('));
});

test('the initial indent starts every line of the fragment', () => {
	for (const line of emit('A|B', '', NaxpValueType.UInt64, '\t\t').split('\n')) {
		if (line.length === 0) { continue; }

		assert.ok(line.startsWith('\t\t'), `'${line}' does not start with the initial indent`);
	}
});

test('the fragment carries no module paraphernalia', () => {
	const source = emit('A|B');

	assert.ok(!source.includes('import '));
	assert.ok(!source.includes('export '));
	assert.ok(!source.includes('use strict'));
});

test('emitting the same naxp twice gives the same text', () => {
	assert.equal(emit('\\A\\A?\\9\\X? \\s!! \\9\\A\\A', 'Postcode'), emit('\\A\\A?\\9\\X? \\s!! \\9\\A\\A', 'Postcode'));
});

test('a prefix that is not an ASCII identifier is refused', () => {
	for (const prefix of ['1Bad', 'Bad.Name', 'Bad Name', 'Ünicode']) {
		assert.throws(() => emit('A', prefix), TypeError, `'${prefix}' was accepted`);
	}
});

test('a value type the naxp outgrows is refused', () => {
	// 128 values: one more than a signed byte holds, exactly what an unsigned byte holds.
	assert.throws(() => emit('#[1-128]', '', NaxpValueType.Int8), RangeError);
	assert.ok(emit('#[1-128]', '', NaxpValueType.UInt8).includes('const maxEncodedValue = 128;'));
});

test('every conformance naxp emits', () => {
	for (const item of data.cases) {
		const source = emit(item.naxp);

		assert.ok(source.includes('function encode(text) {'), `${item.naxp} has no encode`);
		assert.ok(source.includes('function decode(value) {'), `${item.naxp} has no decode`);
		assert.ok(source.includes('function getCanonicalForm(text) {'), `${item.naxp} has no getCanonicalForm`);
		assert.ok(!source.includes(COPY_MARKER), `${item.naxp} leaked a copy marker`);
	}
});

test('the generated code answers the conformance data', () => {
	let checks = 0;

	for (const item of data.cases) {
		const generated = build(item.naxp);

		// Values are compared as text, so that a fragment using BigInt and one using numbers are
		// asked the same question.
		assert.equal(generated.pattern, item.naxp, `${item.naxp}: pattern is '${generated.pattern}'`);
		assert.equal(
			String(generated.maxEncodedValue),
			item.maxEncodedValue,
			`${item.naxp}: maxEncodedValue is ${generated.maxEncodedValue}, the test data says ${item.maxEncodedValue}`);
		++checks;

		for (const value of item.values) {
			const encoded = generated.encode(value.in);
			const accepted = value.out !== '0';

			assert.equal(String(encoded), value.out,
				`${item.naxp}: encode('${value.in}') is ${encoded}, the test data says ${value.out}`);
			assert.equal(String(generated.encode(toBytes(value.in))), value.out,
				`${item.naxp}: encode of the bytes of '${value.in}' disagrees with encode`);
			assert.equal(generated.accepts(value.in), accepted,
				`${item.naxp}: accepts('${value.in}') is ${generated.accepts(value.in)}, the test data says ${accepted}`);
			assert.equal(generated.accepts(toBytes(value.in)), accepted,
				`${item.naxp}: accepts of the bytes of '${value.in}' disagrees with accepts`);
			assert.equal(generated.getCanonicalForm(value.in), accepted ? value.canon : null,
				`${item.naxp}: getCanonicalForm('${value.in}') is '${generated.getCanonicalForm(value.in)}', the test data says '${value.canon}'`);
			assert.equal(generated.getCanonicalForm(toBytes(value.in)), accepted ? value.canon : null,
				`${item.naxp}: getCanonicalForm of the bytes of '${value.in}' disagrees with getCanonicalForm`);
			checks += 6;

			if (!accepted) { continue; }

			const decoded = generated.decode(encoded);

			assert.equal(decoded, value.canon,
				`${item.naxp}: decode(${encoded}) is '${decoded}', the test data says '${value.canon}'`);
			assert.equal(fromBytes(generated.decodeToBytes(encoded)), value.canon,
				`${item.naxp}: decodeToBytes(${encoded}) disagrees with decode`);
			assert.equal(generated.tryDecode(encoded), value.canon,
				`${item.naxp}: tryDecode(${encoded}) disagrees with decode`);
			assert.equal(String(generated.encode(decoded)), value.out,
				`${item.naxp}: '${decoded}' does not encode back to ${value.out}`);
			checks += 4;
		}

		for (const invalid of item.invalid) {
			assert.ok(!generated.accepts(invalid),
				`${item.naxp}: accepts('${invalid}'), which the test data says it must not`);
			assert.equal(String(generated.encode(invalid)), '0',
				`${item.naxp}: encode('${invalid}') is ${generated.encode(invalid)} rather than zero`);
			assert.equal(generated.getCanonicalForm(invalid), null,
				`${item.naxp}: getCanonicalForm('${invalid}') is not null`);
			checks += 3;
		}

		// Zero and one past the end, in the fragment's own number type.
		const one = typeof generated.maxEncodedValue === 'bigint' ? 1n : 1;

		assert.throws(() => generated.decode(one - one), RangeError, `${item.naxp}: decode(0) did not throw a RangeError`);
		assert.equal(generated.tryDecode(one - one), null, `${item.naxp}: tryDecode(0) is not null`);
		assert.equal(generated.tryDecode(generated.maxEncodedValue + one), null,
			`${item.naxp}: tryDecode past the end is not null`);
		checks += 3;
	}

	// Without this a run over no cases would pass silently.
	assert.ok(checks > 1000, `only ${checks} checks ran`);
});

test('the version a generated header would name is the package version', () => {
	const path = new URL('../package.json', import.meta.url);

	assert.equal(Emitter.packageVersion(), JSON.parse(readFileSync(path, 'utf8')).version);
});
