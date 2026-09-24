// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { CEmitter } from '../lib/c-emitter.js';
import { tryCompile } from '../lib/compiler.js';
import { NaxpValueType } from '../lib/emitter.js';
import { COPY_MARKER } from '../lib/tx.js';
import { loadConformanceData } from './conformance.js';
import { compileAndRun, cString } from './native-harness.js';

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
 * @returns {string} The fragment.
 */
function emit(naxp, prefix = '', valueType = NaxpValueType.UInt64) {
	return CEmitter.instance.emit(compile(naxp), prefix, valueType);
}

test('the prefix is snake cased onto every name', () => {
	const source = emit('\\A\\9', 'Postcode');

	assert.ok(source.includes('static const uint64_t postcode_max_encoded_value = 260ULL;'));
	assert.ok(source.includes('enum { postcode_max_length = 2 };'));
	assert.ok(source.includes('static inline bool postcode_accepts(const char *text, size_t text_length)'));
	assert.ok(source.includes('static inline bool postcode_accepts_cstr(const char *text)'));
	assert.ok(source.includes('static inline uint64_t postcode_encode(const char *text, size_t text_length)'));
	assert.ok(source.includes('static inline uint64_t postcode_encode_cstr(const char *text)'));
	assert.ok(source.includes('static inline bool postcode_decode(uint64_t value, char *destination, size_t capacity, size_t *length)'));
	assert.ok(source.includes('static inline bool postcode_decode_cstr(uint64_t value, char *destination, size_t capacity)'));
	assert.ok(source.includes('static inline const char *postcode_pattern(void)'));
	assert.ok(source.includes('static inline bool postcode_canonical_form(const char *text, size_t text_length, char *destination, size_t capacity, size_t *length)'));
	assert.ok(source.includes('static inline bool postcode_canonical_form_cstr(const char *text, char *destination, size_t capacity)'));
});

test('a question mark after another is escaped, so that no trigraph can form', () => {
	assert.ok(emit('A\\??').includes('return "A\\\\?\\?";'));
});

test('a run of capitals is one word until the letter that starts the next', () => {
	for (const [prefix, accepts] of [
		['UKPostcode', 'uk_postcode_accepts'],
		['Postcode2', 'postcode2_accepts'],
		['uk_postcode', 'uk_postcode_accepts'],
		['ABC', 'abc_accepts'],
		['_', '_accepts'],
	]) {
		assert.ok(
			emit('\\A\\9', prefix).includes(`static inline bool ${accepts}(const char *text, size_t text_length)`),
			`${prefix} did not give ${accepts}`);
	}
});

test('a blank prefix gives the bare names', () => {
	const source = emit('\\A\\9');

	assert.ok(source.includes('static inline bool accepts(const char *text, size_t text_length)'));
	assert.ok(source.includes('static const uint64_t max_encoded_value = 260ULL;'));
});

test('the steppers are declared ahead of the public functions', () => {
	const source = emit('\\A\\9');
	const prototype = source.indexOf('static int accept_step(int state, int c);');
	const use = source.indexOf('state = accept_step(state, (unsigned char)text[i]);');
	const definition = source.indexOf('static int accept_step(int state, int c)\n');

	assert.ok(prototype >= 0 && prototype < use && use < definition);
});

test('the first line names the headers the fragment needs', () => {
	assert.ok(emit('\\A\\9').startsWith('/* Needs <stdbool.h>, <stddef.h>, <stdint.h> and <string.h>. */'));
});

test('no digit separators are written', () => {
	const source = emit('\\A{12}');

	assert.ok(source.includes('= 95428956661682176ULL;'));
});

test('the value type maps onto stdint and leaves the steppers in uint64_t', () => {
	const source = emit('\\A\\9', 'Postcode', NaxpValueType.UInt16);

	assert.ok(source.includes('static const uint16_t postcode_max_encoded_value = 260;'));
	assert.ok(source.includes('static inline uint16_t postcode_encode(const char *text, size_t text_length)'));
	assert.ok(source.includes('return postcode_is_accepting(state) ? (uint16_t)(total + 1ULL) : (uint16_t)0;'));
	assert.ok(source.includes('static inline bool postcode_decode(uint16_t value, char *destination, size_t capacity, size_t *length)'));
	assert.ok(source.includes('postcode_decode_core((uint64_t)value, buffer)'));
	assert.ok(source.includes('static int postcode_encode_step(int state, int c, uint64_t *total)'));
});

test('a value type the naxp outgrows is refused', () => {
	assert.throws(() => emit('\\A\\9', '', NaxpValueType.UInt8), RangeError);
});

test('the parameters a range leaves unused are voided', () => {
	// A literal run adds nothing to the total, and the compiler would say so.
	const source = emit('ABC');

	assert.ok(source.includes('static int encode_step(int state, int c, uint64_t *total)\n{\n\t(void)total;\n'));
	assert.ok(source.includes('static int decode_step(int state, uint64_t *remaining, char *destination, int *length)\n{\n\t(void)remaining;\n'));
	assert.ok(!source.includes('(void)c;'));
});

test('a naxp that replaces canonicalises before it ranks', () => {
	const source = emit('(B|b)!B');

	assert.ok(source.includes('canonical_step(state, (unsigned char)text[i], canonical, &length);'));
	assert.ok(source.includes('return finish_canonical(state, canonical, length);'));
	assert.ok(source.includes('int length = canonicalise(text, text_length, canonical);'));
	assert.ok(source.includes('rank(canonical, length)'));
	assert.ok(!source.includes(COPY_MARKER));
});

test('a naxp reaching back to an earlier character keeps a register', () => {
	const source = emit('\\A\\A?\\9\\X? \\s!! \\9\\A\\A');

	assert.ok(source.includes('char held[2] = { 0 };'));
	assert.ok(source.includes('for (int h = 0; h < 1; ++h) { held[h] = held[h + 1]; }'));
	assert.ok(source.includes('canonical[(*length)++] = held[0];'));
});

test('the empty naxp gets a one-byte buffer', () => {
	// A zero-length array is illegal, so the byte is there for the compiler and nothing writes it.
	const source = emit('()');

	assert.ok(source.includes('enum { max_length = 0 };'));
	assert.ok(source.includes('char buffer[1] = { 0 };'));
});

test('a machine above the chunk size is split and its chunks declared', () => {
	const source = emit('A{99}B{99}C{99}');

	assert.ok(source.includes('static int accept_step0(int state, int c);'));
	assert.ok(source.includes('static int accept_step1(int state, int c);'));
	assert.ok(source.includes('if (state < 250) { return accept_step0(state, c); }'));
});

test('a prefix that is not an ASCII identifier is refused', () => {
	for (const prefix of ['1Bad', 'Bad.Name', 'Bad Name']) {
		assert.throws(() => emit('A', prefix), TypeError);
	}
});

/**
 * Every naxp of the conformance data emitted into one file, compiled by gcc as C99 with every
 * warning fatal, and the whole of the test data checked by the result.
 */
test('the generated C matches the conformance data under gcc', () => {
	compileAndRun('gcc', '-std=c99', 'conformance.c', buildHarness());
});

/** @returns {string} The fragments for every conformance naxp, a table over them, and the checks. */
function buildHarness() {
	let source = '/* Generated by c-emitter.test.js. One fragment per conformance case. */\n\n';

	source += '#include <stdbool.h>\n#include <stddef.h>\n#include <stdint.h>\n#include <string.h>\n#include <stdio.h>\n\n';

	data.cases.forEach((item, i) => {
		source += `/* ${i}: ${item.naxp.replace(/[\x00-\x1f]/g, ' ')} */\n`;
		source += CEmitter.instance.emit(compile(item.naxp), `Case${i}`);
		source += '\n';
	});

	source += TYPES;

	data.cases.forEach((item, i) => {
		source += `static const value_row case${i}_values[] =\n{\n`;

		for (const value of item.values) {
			const canon = value.canon ?? '';

			source += `\t{ ${cString(value.in)}, ${value.in.length}, ${value.out}ULL, ${cString(canon)}, ${canon.length} },\n`;
		}

		for (const invalid of item.invalid) {
			source += `\t{ ${cString(invalid)}, ${invalid.length}, 0ULL, "", 0 },\n`;
		}

		// A trailing row, so the array is never empty and the count can be read off it.
		source += '\t{ NULL, 0, 0ULL, NULL, 0 },\n};\n\n';
	});

	source += 'static const naxp_case cases[] =\n{\n';

	data.cases.forEach((item, i) => {
		const snake = `case${i}`;

		source += `\t{ ${cString(item.naxp)}, ${item.naxp.length}, ${item.maxEncodedValue}ULL, ${snake}_max_encoded_value, ${snake}_pattern, ${snake}_accepts, ${snake}_accepts_cstr, ${snake}_encode, ${snake}_encode_cstr, ${snake}_decode, ${snake}_decode_cstr, ${snake}_canonical_form, ${snake}_canonical_form_cstr, ${snake}_values },\n`;
	});

	source += '};\n\n';

	return source + DRIVER;
}

const TYPES = `typedef struct
{
	const char *in;
	size_t in_length;
	uint64_t out;
	const char *canon;
	size_t canon_length;
} value_row;

typedef struct
{
	const char *naxp;
	size_t naxp_length;
	uint64_t max_encoded_value;
	uint64_t fragment_max_encoded_value;
	const char *(*pattern)(void);
	bool (*accepts)(const char *, size_t);
	bool (*accepts_cstr)(const char *);
	uint64_t (*encode)(const char *, size_t);
	uint64_t (*encode_cstr)(const char *);
	bool (*decode)(uint64_t, char *, size_t, size_t *);
	bool (*decode_cstr)(uint64_t, char *, size_t);
	bool (*canonical_form)(const char *, size_t, char *, size_t, size_t *);
	bool (*canonical_form_cstr)(const char *, char *, size_t);
	const value_row *values;
} naxp_case;

`;

/** The checks, which are the same questions the conformance tests ask the library. */
const DRIVER = `static int checks = 0;
static int failures = 0;

static void check(bool condition, const char *naxp, const char *what, const char *text)
{
	++checks;

	if (condition) { return; }

	if (++failures <= 40) { printf("  %s: %s '%s'\\n", naxp, what, text); }
}

int main(void)
{
	char out[2048];
	size_t case_count = sizeof cases / sizeof cases[0];

	for (size_t i = 0; i < case_count; ++i)
	{
		const naxp_case *c = &cases[i];

		const char *pattern = c->pattern();

		check(c->fragment_max_encoded_value == c->max_encoded_value, c->naxp, "max_encoded_value", "");
		check(strlen(pattern) == c->naxp_length && memcmp(pattern, c->naxp, c->naxp_length) == 0, c->naxp, "pattern", "");

		for (const value_row *row = c->values; row->in != NULL; ++row)
		{
			bool valid = row->out != 0ULL;
			bool plain = strlen(row->in) == row->in_length;
			bool plain_canon = strlen(row->canon) == row->canon_length;
			uint64_t encoded = c->encode(row->in, row->in_length);
			size_t length;

			check(encoded == row->out, c->naxp, "encode", row->in);
			check(c->accepts(row->in, row->in_length) == valid, c->naxp, "accepts", row->in);

			if (plain)
			{
				check(c->encode_cstr(row->in) == row->out, c->naxp, "encode_cstr", row->in);
				check(c->accepts_cstr(row->in) == valid, c->naxp, "accepts_cstr", row->in);
			}

			check(c->canonical_form(row->in, row->in_length, out, sizeof out, &length) == valid
				&& (valid ? length == row->canon_length && memcmp(out, row->canon, length) == 0 : length == 0),
				c->naxp, "canonical_form", row->in);

			if (plain)
			{
				check(c->canonical_form_cstr(row->in, out, sizeof out) == valid
					&& (!valid || (strlen(out) == row->canon_length && memcmp(out, row->canon, row->canon_length) == 0)),
					c->naxp, "canonical_form_cstr", row->in);
			}

			if (!valid) { continue; }

			/* Too short by one, then exactly long enough. */
			if (row->canon_length > 0)
			{
				check(!c->canonical_form(row->in, row->in_length, out, row->canon_length - 1, &length) && length == 0, c->naxp, "canonical_form short", row->in);
			}

			check(c->canonical_form(row->in, row->in_length, out, row->canon_length, NULL), c->naxp, "canonical_form exact", row->in);

			check(c->decode(encoded, out, sizeof out, &length) && length == row->canon_length && memcmp(out, row->canon, length) == 0,
				c->naxp, "decode", row->in);
			check(c->encode(row->canon, row->canon_length) == row->out, c->naxp, "round trip", row->in);

			if (plain_canon)
			{
				check(c->decode_cstr(encoded, out, sizeof out) && strlen(out) == row->canon_length && memcmp(out, row->canon, row->canon_length) == 0,
					c->naxp, "decode_cstr", row->in);
			}

			/* Too short by one, then exactly long enough. */
			if (row->canon_length > 0)
			{
				check(!c->decode(encoded, out, row->canon_length - 1, &length) && length == 0, c->naxp, "decode short", row->in);
			}

			check(c->decode(encoded, out, row->canon_length, NULL), c->naxp, "decode exact", row->in);
			check(!c->decode_cstr(encoded, out, row->canon_length), c->naxp, "decode_cstr short", row->in);
			check(c->decode_cstr(encoded, out, row->canon_length + 1), c->naxp, "decode_cstr exact", row->in);
		}

		check(!c->decode(0ULL, out, sizeof out, NULL), c->naxp, "decode zero", "");
		check(!c->decode_cstr(0ULL, out, sizeof out), c->naxp, "decode_cstr zero", "");

		if (c->max_encoded_value != UINT64_MAX)
		{
			check(!c->decode(c->max_encoded_value + 1ULL, out, sizeof out, NULL), c->naxp, "decode past the end", "");
		}
	}

	if (failures != 0)
	{
		printf("%d of %d checks failed.\\n", failures, checks);

		return 1;
	}

	printf("%d checks passed over %d naxps.\\n", checks, (int)case_count);

	return 0;
}
`;
