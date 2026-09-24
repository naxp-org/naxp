// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { tryCompile } from '../lib/compiler.js';
import { CppEmitter } from '../lib/cpp-emitter.js';
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
	return CppEmitter.instance.emit(compile(naxp), prefix, valueType);
}

test('the prefix is snake cased onto every name', () => {
	const source = emit('\\A\\9', 'Postcode');

	assert.ok(source.includes('inline constexpr std::uint64_t postcode_max_encoded_value = 260ULL;'));
	assert.ok(source.includes('inline constexpr int postcode_max_length = 2;'));
	assert.ok(source.includes('inline bool postcode_accepts(std::string_view text)'));
	assert.ok(source.includes('inline std::uint64_t postcode_encode(std::string_view text)'));
	assert.ok(source.includes('inline std::string postcode_decode(std::uint64_t value)'));
	assert.ok(source.includes('inline bool postcode_try_decode(std::uint64_t value, std::string& text)'));
	assert.ok(source.includes('inline bool postcode_try_decode(std::uint64_t value, char* destination, std::size_t capacity, std::size_t& length)'));
	assert.ok(source.includes('inline constexpr std::string_view postcode_pattern = R"(\\A\\9)";'));
	assert.ok(source.includes('inline std::optional<std::string> postcode_canonical_form(std::string_view text)'));
	assert.ok(source.includes('inline bool postcode_try_canonical_form(std::string_view text, std::string& canonical_form)'));
	assert.ok(source.includes('inline bool postcode_try_canonical_form(std::string_view text, char* destination, std::size_t capacity, std::size_t& length)'));
});

test('a pattern that would close a raw string early is escaped instead', () => {
	assert.ok(emit('\\)"').includes('inline constexpr std::string_view pattern = "\\\\)\\"";'));
});

test('a blank prefix gives the bare names', () => {
	const source = emit('\\A\\9');

	assert.ok(source.includes('inline bool accepts(std::string_view text)'));
	assert.ok(source.includes('inline constexpr std::uint64_t max_encoded_value = 260ULL;'));
});

test('the steppers are declared ahead of the public functions', () => {
	const source = emit('\\A\\9');
	const prototype = source.indexOf('inline int accept_step(int state, int c);');
	const use = source.indexOf('state = accept_step(state, static_cast<unsigned char>(c));');
	const definition = source.indexOf('inline int accept_step(int state, int c)\n');

	assert.ok(prototype >= 0 && prototype < use && use < definition);
});

test('the first line names the headers the fragment needs', () => {
	assert.ok(emit('\\A\\9').startsWith('// Needs <cstdint>, <optional>, <stdexcept>, <string> and <string_view>.'));
});

test('digits are grouped with apostrophes', () => {
	assert.ok(emit('\\A{12}').includes("= 95'428'956'661'682'176ULL;"));
});

test('the steppers take references and cast the C++ way', () => {
	const source = emit('\\A\\9');

	assert.ok(source.includes('inline int encode_step(int state, int c, std::uint64_t& total)'));
	assert.ok(source.includes('inline int decode_step(int state, std::uint64_t& remaining, char* destination, int& length)'));
	assert.ok(source.includes("total += 10ULL * static_cast<std::uint64_t>(c - 'A');"));
	assert.ok(source.includes("destination[length++] = static_cast<char>('A' + static_cast<int>(index));"));
	assert.ok(!source.includes('(*'));
});

test('the value type maps onto cstdint', () => {
	const source = emit('\\A\\9', 'Postcode', NaxpValueType.Int32);

	assert.ok(source.includes('inline constexpr std::int32_t postcode_max_encoded_value = 260;'));
	assert.ok(source.includes('inline std::int32_t postcode_encode(std::string_view text)'));
	assert.ok(source.includes('return postcode_is_accepting(state) ? static_cast<std::int32_t>(total + 1ULL) : static_cast<std::int32_t>(0);'));
	assert.ok(source.includes('inline std::string postcode_decode(std::int32_t value)'));
	assert.ok(source.includes('postcode_decode_core(static_cast<std::uint64_t>(value), buffer)'));
});

test('a value type the naxp outgrows is refused', () => {
	assert.throws(() => emit('\\A\\9', '', NaxpValueType.Int8), RangeError);
});

test('the parameters a range leaves unused are voided', () => {
	assert.ok(emit('ABC').includes('inline int encode_step(int state, int c, std::uint64_t& total)\n{\n\t(void)total;\n'));
});

test('a naxp that replaces canonicalises before it ranks', () => {
	const source = emit('(B|b)!B');

	assert.ok(source.includes('canonical_step(state, static_cast<unsigned char>(c), canonical, length);'));
	assert.ok(source.includes('return finish_canonical(state, canonical, length);'));
	assert.ok(source.includes('int length = canonicalise(text, canonical);'));
	assert.ok(source.includes('rank(canonical, length)'));
	assert.ok(!source.includes(COPY_MARKER));
});

test('decode throws out_of_range', () => {
	assert.ok(emit('\\A\\9').includes('throw std::out_of_range("This naxp encodes the values 1 to 260.");'));
});

test('a prefix that is not an ASCII identifier is refused', () => {
	for (const prefix of ['1Bad', 'Bad.Name', 'Bad Name']) {
		assert.throws(() => emit('A', prefix), TypeError);
	}
});

/**
 * Every naxp of the conformance data emitted into one file, compiled by g++ as C++17 with every
 * warning fatal, and the whole of the test data checked by the result.
 */
test('the generated C++ matches the conformance data under g++', () => {
	compileAndRun('g++', '-std=c++17', 'conformance.cpp', buildHarness());
});

/** @returns {string} The fragments for every conformance naxp, a table over them, and the checks. */
function buildHarness() {
	let source = '// Generated by cpp-emitter.test.js. One fragment per conformance case.\n\n';

	source += '#include <cstdint>\n#include <cstdio>\n#include <cstring>\n#include <optional>\n#include <stdexcept>\n#include <string>\n#include <string_view>\n\n';

	data.cases.forEach((item, i) => {
		source += `// ${i}: ${item.naxp.replace(/[\x00-\x1f]/g, ' ')}\n`;
		source += CppEmitter.instance.emit(compile(item.naxp), `Case${i}`);
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
		source += '\t{ nullptr, 0, 0ULL, nullptr, 0 },\n};\n\n';
	});

	source += 'static const naxp_case cases[] =\n{\n';

	data.cases.forEach((item, i) => {
		const snake = `case${i}`;

		source += `\t{ ${cString(item.naxp)}, ${item.naxp.length}, ${item.maxEncodedValue}ULL, ${snake}_max_encoded_value, ${snake}_pattern, ${snake}_accepts, ${snake}_encode, ${snake}_decode, ${snake}_try_decode, ${snake}_try_decode, ${snake}_canonical_form, ${snake}_try_canonical_form, ${snake}_try_canonical_form, ${snake}_values },\n`;
	});

	source += '};\n\n';

	return source + DRIVER;
}

const TYPES = `struct value_row
{
	const char* in;
	std::size_t in_length;
	std::uint64_t out;
	const char* canon;
	std::size_t canon_length;
};

struct naxp_case
{
	const char* naxp;
	std::size_t naxp_length;
	std::uint64_t max_encoded_value;
	std::uint64_t fragment_max_encoded_value;
	std::string_view pattern;
	bool (*accepts)(std::string_view);
	std::uint64_t (*encode)(std::string_view);
	std::string (*decode)(std::uint64_t);
	bool (*try_decode)(std::uint64_t, std::string&);
	bool (*try_decode_to)(std::uint64_t, char*, std::size_t, std::size_t&);
	std::optional<std::string> (*canonical_form)(std::string_view);
	bool (*try_canonical_form)(std::string_view, std::string&);
	bool (*try_canonical_form_to)(std::string_view, char*, std::size_t, std::size_t&);
	const value_row* values;
};

`;

/** The checks, which are the same questions the conformance tests ask the library. */
const DRIVER = `static int checks = 0;
static int failures = 0;

static void check(bool condition, const char* naxp, const char* what, const char* text)
{
	++checks;

	if (condition) { return; }

	if (++failures <= 40) { std::printf("  %s: %s '%s'\\n", naxp, what, text); }
}

static bool throws_out_of_range(const naxp_case& c, std::uint64_t value)
{
	try { c.decode(value); }
	catch (const std::out_of_range&) { return true; }
	catch (...) { return false; }

	return false;
}

int main()
{
	std::size_t case_count = sizeof cases / sizeof cases[0];

	for (const naxp_case& c : cases)
	{
		check(c.fragment_max_encoded_value == c.max_encoded_value, c.naxp, "max_encoded_value", "");
		check(c.pattern == std::string_view(c.naxp, c.naxp_length), c.naxp, "pattern", "");

		for (const value_row* row = c.values; row->in != nullptr; ++row)
		{
			bool valid = row->out != 0ULL;
			std::string_view in(row->in, row->in_length);
			std::string canon(row->canon, row->canon_length);
			std::uint64_t encoded = c.encode(in);
			std::string text = "untouched";

			char out[2048];
			std::size_t length = 99;
			std::optional<std::string> canonical_form = c.canonical_form(in);

			check(encoded == row->out, c.naxp, "encode", row->in);
			check(c.accepts(in) == valid, c.naxp, "accepts", row->in);
			check(valid ? canonical_form == canon : !canonical_form.has_value(), c.naxp, "canonical_form", row->in);
			check(c.try_canonical_form(in, text) == valid && text == (valid ? canon : std::string("untouched")), c.naxp, "try_canonical_form", row->in);
			check(c.try_canonical_form_to(in, out, sizeof out, length) == valid
				&& (valid ? std::string_view(out, length) == canon : length == 0),
				c.naxp, "try_canonical_form into a buffer", row->in);

			if (!valid) { continue; }

			check(c.decode(encoded) == canon, c.naxp, "decode", row->in);
			check(c.try_decode(encoded, text) && text == canon, c.naxp, "try_decode", row->in);
			check(c.try_decode_to(encoded, out, sizeof out, length) && std::string_view(out, length) == canon, c.naxp, "try_decode into a buffer", row->in);
			check(c.encode(canon) == row->out, c.naxp, "round trip", row->in);

			// Too short by one, then exactly long enough.
			if (!canon.empty())
			{
				check(!c.try_decode_to(encoded, out, canon.size() - 1, length) && length == 0, c.naxp, "try_decode short", row->in);
				check(!c.try_canonical_form_to(in, out, canon.size() - 1, length) && length == 0, c.naxp, "try_canonical_form short", row->in);
			}

			check(c.try_decode_to(encoded, out, canon.size(), length) && length == canon.size(), c.naxp, "try_decode exact", row->in);
			check(c.try_canonical_form_to(in, out, canon.size(), length) && length == canon.size(), c.naxp, "try_canonical_form exact", row->in);
		}

		std::string text = "untouched";

		check(throws_out_of_range(c, 0ULL), c.naxp, "decode zero", "");
		check(!c.try_decode(0ULL, text) && text == "untouched", c.naxp, "try_decode zero", "");

		char out[16];
		std::size_t length = 99;

		check(!c.try_decode_to(0ULL, out, sizeof out, length) && length == 0, c.naxp, "try_decode into a buffer zero", "");

		if (c.max_encoded_value != UINT64_MAX)
		{
			check(throws_out_of_range(c, c.max_encoded_value + 1ULL), c.naxp, "decode past the end", "");
		}
	}

	if (failures != 0)
	{
		std::printf("%d of %d checks failed.\\n", failures, checks);

		return 1;
	}

	std::printf("%d checks passed over %d naxps.\\n", checks, static_cast<int>(case_count));

	return 0;
}
`;
