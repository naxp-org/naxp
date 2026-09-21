// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';

import { Naxp, NaxpFormatError } from '../lib/naxp.js';
import { OutputLanguage } from '../lib/output-language.js';
import { ruleOf, ruleOfCode } from './naxp-message-rules.js';
import { loadConformanceData } from './conformance.js';

const data = loadConformanceData();
const POSTCODE = '\\A \\A? \\9 \\X? \\s!! \\9 \\A \\A';

// #region Parsing

test('a well-formed naxp parses and remembers its pattern', () => {
	const naxp = Naxp.parse(POSTCODE);

	assert.equal(naxp.pattern, POSTCODE);
	assert.equal(naxp.toString(), POSTCODE);
	assert.equal(naxp.maxEncodedValue, 1755842400n);
});

test('parse throws, and the thrown message leads with the code and the span', () => {
	// A thrown error is all anybody gets, so the code and the span are in the words. Anything
	// wanting them apart asks tryParse.
	assert.throws(
		() => Naxp.parse('A{2-5}'),
		error => {
			assert.ok(error instanceof NaxpFormatError);
			assert.ok(error.message.startsWith('NAXP1002 at 3..4: '), error.message);
			assert.ok(error.message.includes('not by a hyphen'), error.message);

			return true;
		});
});

test('tryParse reports the reason, the span and the code rather than throwing', () => {
	const pattern = 'A{2-5}';
	const { naxp, errorMessage, errorOffset, errorLength, errorCode }
		= Naxp.tryParse(pattern);

	assert.equal(naxp, null);
	assert.equal(errorCode, 'NAXP1002');
	assert.equal(ruleOfCode(errorCode), 'syntax');

	// The reason alone, with no code and no position in it.
	assert.ok(errorMessage.startsWith('The counts of an interval'), errorMessage);
	assert.ok(!errorMessage.includes('NAXP'), errorMessage);

	// The span is the hyphen.
	assert.equal(pattern.slice(errorOffset, errorOffset + errorLength), '-');
});

test('tryParse clears every field on success', () => {
	const result = Naxp.tryParse('\\9{3}');

	assert.equal(result.errorMessage, null);
	assert.equal(result.errorCode, null);
	assert.equal(result.errorOffset, 0);
	assert.equal(result.errorLength, 0);
	assert.equal(result.naxp.maxEncodedValue, 1000n);
});

test('a fault with no position reports the whole naxp', () => {
	// W5 counts the canonical language, so it belongs to the naxp rather than to a place in it.
	// Reporting offset zero with a zero length would send a caller underlining the first
	// character, which is why the whole pattern is given instead.
	const pattern = '\\9{20}';
	const { naxp, errorOffset, errorLength, errorCode } = Naxp.tryParse(pattern);

	assert.equal(naxp, null);
	assert.equal(errorCode, 'NAXP1046');
	assert.equal(errorOffset, 0);
	assert.equal(errorLength, pattern.length);
});

test('the constructor is not the way in', () => {
	assert.throws(() => new Naxp(), TypeError);
	assert.throws(() => new Naxp('\\9{3}'), TypeError);
});

test('a naxp is frozen once parsed', () => {
	const naxp = Naxp.parse('\\9');

	assert.equal(Object.isFrozen(naxp), true);
});

// #endregion
// #region The counts

test('the value count is the size of the canonical language, not of the accepted one', () => {
	const plain = Naxp.parse('\\9{2}');

	assert.equal(plain.maxEncodedValue, 100n);

	// (A|a)!A accepts two strings and encodes one, since they share a canonical form. How many
	// it accepts is not on the surface: nothing encoding or decoding needs it, and the
	// conformance tests check it through the compiler instead.
	const unified = Naxp.parse('(A|a)!A');

	assert.equal(unified.maxEncodedValue, 1n);
	assert.equal(unified.accepts('A'), true);
	assert.equal(unified.accepts('a'), true);
});

test('the counts are bigints whatever the size of the naxp', () => {
	// One return type, so consumer code never branches on which naxp it holds.
	for (const pattern of ['A', '\\9{3}', '\\9{19}']) {
		assert.equal(typeof Naxp.parse(pattern).maxEncodedValue, 'bigint', pattern);
		assert.equal(typeof Naxp.parse(pattern).encode('A'), 'bigint', pattern);
	}
});

test('a naxp near the top of the range still compiles', () => {
	// 10^19 is below 2^64 - 1, so W5 lets it through and every value is exact.
	const naxp = Naxp.parse('\\9{19}');

	assert.equal(naxp.maxEncodedValue, 10000000000000000000n);
	assert.equal(naxp.encode('0000000000000000000'), 1n);
	assert.equal(naxp.encode('9999999999999999999'), 10000000000000000000n);
	assert.equal(naxp.decode(10000000000000000000n), '9999999999999999999');
});

// #endregion
// #region Accepting, encoding and decoding

test('the worked postcodes encode alike with and without the space', () => {
	const naxp = Naxp.parse(POSTCODE);
	const cases = [
		['M1 1AA', 'M11AA', 810639597n],
		['CR2 6XH', 'CR26XH', 180591302n],
		['DN55 1PT', 'DN551PT', 238906246n],
		['W1A 1AA', 'W1A1AA', 1486037957n],
		['EC1A 1BB', 'EC1A1BB', 277958384n],
	];

	for (const [spaced, tight, expected] of cases) {
		assert.equal(naxp.encode(spaced), expected, spaced);
		assert.equal(naxp.encode(tight), expected, tight);

		// Decoding gives the canonical form, which is the spelling with the space.
		assert.equal(naxp.decode(expected), spaced, `${expected}`);
	}
});

test('invalid text encodes to zero and has no canonical form', () => {
	const naxp = Naxp.parse('(A|a)!A');

	assert.equal(naxp.accepts('B'), false);
	assert.equal(naxp.encode('B'), 0n);
	assert.equal(naxp.getCanonicalForm('B'), null);

	assert.equal(naxp.accepts('a'), true);
	assert.equal(naxp.encode('a'), 1n);
	assert.equal(naxp.getCanonicalForm('a'), 'A');
});

test('a naxp with no unified element leaves every accepted string alone', () => {
	const naxp = Naxp.parse('\\A\\9');

	assert.equal(naxp.getCanonicalForm('A1'), 'A1');
	assert.equal(naxp.getCanonicalForm('a1'), null);
});

test('decode throws outside the range and tryDecode reports it', () => {
	const naxp = Naxp.parse('\\9{2}');

	assert.throws(() => naxp.decode(0), RangeError);
	assert.throws(() => naxp.decode(101n), RangeError);
	assert.throws(() => naxp.decode(-1n), RangeError);

	assert.equal(naxp.tryDecode(0), null);
	assert.equal(naxp.tryDecode(101n), null);
	assert.equal(naxp.decode(1), '00');
	assert.equal(naxp.decode(100n), '99');
});

test('decode takes a bigint or a safe integer, and throws on anything else', () => {
	const naxp = Naxp.parse('\\9{2}');

	assert.equal(naxp.decode(7), naxp.decode(7n));

	assert.throws(() => naxp.decode(1.5), TypeError);
	assert.throws(() => naxp.decode(2 ** 60), TypeError);
	assert.throws(() => naxp.decode('7'), TypeError);
});

test('ASCII bytes are accepted wherever a string is', () => {
	const naxp = Naxp.parse(POSTCODE);
	const bytes = new Uint8Array([...'M11AA'].map(c => c.charCodeAt(0)));

	assert.equal(naxp.accepts(bytes), true);
	assert.equal(naxp.encode(bytes), 810639597n);
	assert.equal(naxp.getCanonicalForm(bytes), 'M1 1AA');

	// A byte above 0x7E is a character no naxp can name, so it is invalid rather than wrapped.
	const high = new Uint8Array([0x4D, 0x31, 0x31, 0x41, 0xC1]);

	assert.equal(naxp.accepts(high), false);
	assert.equal(naxp.encode(high), 0n);
});

test('every reserved character has an escape that matches it', () => {
	// Driven by the list rather than by cases, because what this catches is a character joining
	// the reserved set in the specification and in one implementation but not the other. That is
	// exactly what happened when the comma became the interval separator: the C# reserved it, the
	// JavaScript did not, and until this existed nothing noticed.
	const reserved = [...'!#(),-?[\\]{|}*+.^$'];

	assert.equal(reserved.length, 18);

	for (const c of reserved) {
		const naxp = Naxp.parse(`\\${c}`);

		assert.equal(naxp.maxEncodedValue, 1n, c);
		assert.equal(naxp.accepts(c), true, `${c} is not matched by its escape`);
	}
});

test('a character outside the reserved set stands for itself', () => {
	for (const c of '"%&\'/:;<=>@_`~') {
		assert.equal(Naxp.parse(c).accepts(c), true, `${c} does not match itself`);
	}
});

test('a non-string argument is invalid rather than coerced', () => {
	const naxp = Naxp.parse('\\9');

	assert.throws(() => naxp.encode(7), TypeError);
	assert.throws(() => naxp.accepts(null), TypeError);
	assert.throws(() => Naxp.parse(7), TypeError);
});

// #endregion
// #region The rules the compiler owns

test('W5 invalidates a naxp with more encoded values than the encoding can produce', () => {
	// The rule the compiler owns outright: 10^20 is above 2^64 - 1, so the count saturates and
	// the naxp is invalid rather than being given values it cannot hold.
	const { naxp, errorMessage, errorCode } = Naxp.tryParse('\\9{20}');

	assert.equal(naxp, null);
	assert.equal(ruleOfCode(errorCode), 'W5');
	assert.ok(errorMessage.includes('18 446 744 073 709 551 615'), errorMessage);
});

test('W3 is invalid when the naxp is parsed, not when a string is encoded', () => {
	// Read the B of AB!!B?C as the unified element with the optional one absent and ABC
	// canonicalises to itself; read it the other way round and it canonicalises to ABBC.
	const { naxp, errorMessage, errorCode } = Naxp.tryParse('AB!!B?C');

	assert.equal(naxp, null);
	assert.equal(ruleOfCode(errorCode), 'W3');
	assert.ok(errorMessage.includes('more than one canonical form'), errorMessage);
});

test('a legal naxp can still be declined for its size, and the rule says so', () => {
	// This breaks no rule of the language; its size is the only thing wrong with it. The family
	// that used to stand here, [ab]{16}c|([ab]!a){16}d, is linear now that a character read but
	// not yet placed is held in a register, so a marked decimal range is the witness instead:
	// twelve digits, where deciding W3 passes the pair state budget.
	const { naxp, errorCode } = Naxp.tryParse('#[' + '0!'.repeat(11) + '1-' + '9'.repeat(12) + ']');

	assert.equal(naxp, null);
	assert.equal(ruleOfCode(errorCode), 'W6');
});

test('the family that once had no machine now has a small one', () => {
	const naxp = Naxp.parse('[ab]{16}c|([ab]!a){16}d');

	assert.equal(naxp.getCanonicalForm('abababababababaad'), 'aaaaaaaaaaaaaaaad');
});

// #endregion
// #region The whole of the test data, through the public surface

test('every naxp the test data accepts compiles, and every value round trips', () => {
	const failures = [];
	let checked = 0;

	for (const item of data.cases) {
		const { naxp, errorMessage } = Naxp.tryParse(item.naxp);

		if (naxp === null) {
			failures.push(`${item.naxp} was invalid: ${errorMessage}`);
			continue;
		}

		if (naxp.maxEncodedValue !== BigInt(item.maxEncodedValue)) {
			failures.push(
				`${item.naxp}: ${naxp.maxEncodedValue} values, the data says ${item.maxEncodedValue}.`);
		}

		for (const value of item.values) {
			++checked;

			const encoded = naxp.encode(value.in);

			if (encoded !== BigInt(value.out)) {
				failures.push(
					`${item.naxp}: '${value.in}' encodes to ${encoded}, `
					+ `the data says ${value.out}.`);
				continue;
			}

			if (value.out === '0') {
				if (naxp.accepts(value.in)) {
					failures.push(`${item.naxp}: '${value.in}' encodes to zero yet is accepted.`);
				}

				continue;
			}

			if (!naxp.accepts(value.in)) {
				failures.push(`${item.naxp}: '${value.in}' has an encoded value yet is invalid.`);
			}

			if (value.canon !== undefined) {
				if (naxp.getCanonicalForm(value.in) !== value.canon) {
					failures.push(
						`${item.naxp}: '${value.in}' canonicalises to `
						+ `'${naxp.getCanonicalForm(value.in)}', the data says '${value.canon}'.`);
				}

				if (naxp.decode(encoded) !== value.canon) {
					failures.push(
						`${item.naxp}: ${encoded} decodes to '${naxp.decode(encoded)}', `
						+ `the data says '${value.canon}'.`);
				}
			}
		}

		for (const invalid of item.invalid) {
			if (naxp.accepts(invalid) || naxp.encode(invalid) !== 0n) {
				failures.push(`${item.naxp}: '${invalid}' should not be accepted.`);
			}
		}
	}

	assert.deepEqual(failures, [], failures.slice(0, 20).join('\n'));
	assert.ok(checked > 400, `only ${checked} strings were checked`);
});

test('every naxp the test data marks invalid is invalid, for the rule it names', () => {
	// The whole invalidNaxps list, through the one call a consumer makes. Nothing is deferred now:
	// the parser owns syntax and W4, the tree pass owns W1 and W2, the checker owns W3, and the
	// compiler owns W5.
	const failures = [];

	for (const item of data.invalidNaxps) {
		const { naxp, errorCode } = Naxp.tryParse(item.naxp);

		if (naxp !== null) {
			failures.push(`${item.naxp} compiled, and the test data says ${item.rule}.`);
			continue;
		}

		if (ruleOfCode(errorCode) !== item.rule) {
			failures.push(
				`${item.naxp} was invalid as ${ruleOfCode(errorCode)}, `
				+ `and the test data says ${item.rule}.`);
		}
	}

	assert.deepEqual(failures, [], failures.join('\n'));
	assert.equal(data.invalidNaxps.length, 63);
});

// #endregion

// #region Code generation

test('one entry point reaches each language', () => {
	// The same naxp and the same prefix, two fragments in two languages: the language decides
	// nothing else.
	const naxp = Naxp.parse('\\A\\9');

	assert.ok(naxp.emit(OutputLanguage.CSharp, 'Postcode')
		.includes('public const ulong PostcodeMaxEncodedValue = 260UL;'));
	assert.ok(naxp.emit(OutputLanguage.JavaScript, 'Postcode')
		.includes('const postcodeMaxEncodedValue = 260;'));
});

test('a language that is not one is refused', () => {
	assert.throws(() => Naxp.parse('A').emit('Fortran'), TypeError);
	assert.throws(() => Naxp.parse('A').emit(), TypeError);
});

test('the line ending is the caller’s rather than the host’s', () => {
	const naxp = Naxp.parse('A|B');

	assert.ok(!naxp.emit(OutputLanguage.CSharp).includes('\r'));
	assert.ok(!naxp.emit(OutputLanguage.JavaScript).includes('\r'));

	const crlf = naxp.emit(OutputLanguage.CSharp, '', undefined, '', '\r\n');

	assert.ok(!crlf.split('\r\n').join('').includes('\n'));
	assert.equal(crlf.split('\r\n').join('\n'), naxp.emit(OutputLanguage.CSharp));
});

test('indentation is the caller’s too', () => {
	const source = Naxp.parse('A|B').emit(OutputLanguage.CSharp, '', undefined, '', '\n', '    ');

	assert.ok(source.includes('\n    int state = 0;'));
	assert.ok(!source.includes('\t'));
});

test('a formatting argument that is not a string is refused', () => {
	const naxp = Naxp.parse('A');

	assert.throws(() => naxp.emit(OutputLanguage.CSharp, '', undefined, '', null), TypeError);
	assert.throws(() => naxp.emit(OutputLanguage.CSharp, '', undefined, '', '\n', 4), TypeError);
	assert.throws(() => naxp.emit(OutputLanguage.CSharp, '', undefined, null), TypeError);
});

// #endregion
