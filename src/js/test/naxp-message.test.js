// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { NaxpError } from '../lib/naxp-error.js';
import { ALL_NAXP_MESSAGES, NaxpMessage, formatNaxpMessage } from '../lib/naxp-message.js';
import { NaxpLimits } from '../lib/naxp-limits.js';
import { MAPPED, ruleOf } from './naxp-message-rules.js';

const CSHARP = fileURLToPath(new URL('../../cs/Naxp/NaxpMessage.cs', import.meta.url));

/**
 * Whether a message interpolates, judged by reading its format rather than by trying it.
 *
 * @param {string} message The message.
 * @returns {boolean} Whether it takes an argument.
 */
function takesAnArgument(message) {
	return formatNaxpMessage(message, null).includes('{0}');
}

// #region The tables agree with each other

test('the enum and the text hold the same members in the same order', () => {
	assert.deepEqual(Object.keys(NaxpMessage), ALL_NAXP_MESSAGES);
});

test('a member names itself, so the code is the member', () => {
	for (const name of ALL_NAXP_MESSAGES) {
		assert.equal(NaxpMessage[name], name);
	}
});

test('the codes are numbered by position', () => {
	// A member inserted without renumbering is caught here rather than by a message coming out
	// under the wrong code.
	ALL_NAXP_MESSAGES.forEach((name, i) => {
		assert.ok(name.startsWith(`NAXP${1001 + i}_`), `${name} is at position ${i}`);
	});
});

test('every message has a rule', () => {
	for (const name of ALL_NAXP_MESSAGES) {
		assert.ok(ruleOf(name).length > 0, name);
	}

	assert.equal(MAPPED.length, ALL_NAXP_MESSAGES.length);
});

// #endregion
// #region The text itself

test('every message says something, and ends in a full stop', () => {
	for (const name of ALL_NAXP_MESSAGES) {
		const text = formatNaxpMessage(name, null);

		assert.ok(text.trim().length > 0, name);
		assert.ok(text.trimEnd().endsWith('.'), `${name}: ${text}`);
	}
});

test('the messages taking an argument are exactly those that use it', () => {
	assert.deepEqual(ALL_NAXP_MESSAGES.filter(takesAnArgument), [
		'NAXP1027_RangeReversed',
		'NAXP1032_EscapeUndefined',
		'NAXP1033_CharacterNotAllowed',
		'NAXP1038_ReservedCharacterHere',
		'NAXP1039_CharacterHere',
		'NAXP1044_RenderingNotGenerated',
		'NAXP1046_ReplacementNotSingleValuedWitness',
	]);
});

test('a message taking an argument interpolates every place it appears', () => {
	// NAXP1038 uses its argument twice, once for the character and once for the escape that
	// matches it, so a replace that stopped at the first would leave a placeholder behind.
	for (const name of ALL_NAXP_MESSAGES) {
		if (!takesAnArgument(name)) { continue; }

		const text = formatNaxpMessage(name, 'WITNESS');

		assert.ok(text.includes('WITNESS'), name);
		assert.ok(!text.includes('{0}'), `${name} left a placeholder: ${text}`);
	}
});

test('a message naming a budget names the real one', () => {
	const named = {
		NAXP1048_ElementTooLong: NaxpLimits.maxStringLength,
		NAXP1049_TooManyStates: NaxpLimits.maxStates,
		NAXP1050_TooManyCanonicalStates: NaxpLimits.maxCanonicalStates,
		NAXP1051_TooManyPairStates: NaxpLimits.maxStates,
	};

	for (const [name, budget] of Object.entries(named)) {
		assert.ok(formatNaxpMessage(name, null).includes(String(budget)), name);
	}
});

// #endregion
// #region The two implementations agree

test('the C# names the same messages in the same order', () => {
	// The two tables were generated from one source and must not drift. Names and order are what
	// matter most: a member added to one and not the other changes what every later code means.
	const source = readFileSync(CSHARP, 'utf8');
	const body = source.slice(source.indexOf('enum NaxpMessage'), source.indexOf('/// <summary>\n/// What each'));
	const names = [...body.matchAll(/^\t(NAXP\d+_\w+),$/gm)].map(m => m[1]);

	assert.deepEqual(names, ALL_NAXP_MESSAGES);
});

test('the C# says the same words', () => {
	// Only the messages with no argument and no budget are compared literally, which is
	// forty-one of the fifty-two. The rest differ in how each language spells interpolation,
	// and the ones that matter are pinned by name above and by the tests either side.
	const source = readFileSync(CSHARP, 'utf8');
	const table = source.slice(source.indexOf('return\n\t\t['));
	const entries = [...table.matchAll(/\/\/ (NAXP\d+_\w+)\n\t\t\t(\$?)"((?:[^"\\]|\\.)*)",/g)];

	assert.equal(entries.length, ALL_NAXP_MESSAGES.length);

	let compared = 0;

	for (const [, name, dollar, literal] of entries) {
		if (dollar === '$' || takesAnArgument(name)) { continue; }

		++compared;

			// A C# literal to the string it denotes. These messages escape only the backslash
			// and the quote, which JSON escapes the same way, so parsing the literal as a JSON
			// string is exact and needs no unescaping here.
			const csharp = JSON.parse(`"${literal}"`);

		assert.equal(formatNaxpMessage(name, null), csharp, name);
	}

	assert.ok(compared >= 40, `only ${compared} messages were compared`);
});

test('the code is the number alone, never the hint beside it', () => {
	// A member is spelled NAXP1002_IntervalHyphen so that a line of the library says which refusal
	// it is about at a glance. That half is a note to ourselves: it would read as a promise about
	// wording nobody has made, so it stops at the boundary.
	for (const name of ALL_NAXP_MESSAGES) {
		const { code } = new NaxpError(name);

		assert.match(code, /^NAXP[0-9]{4}$/);
		assert.ok(name.startsWith(`${code}_`), `${name} does not start with ${code}_`);
	}
});

test('no message quotes its own member name', () => {
	// The other route out: the hint reaching a caller by way of the words rather than the code.
	for (const name of ALL_NAXP_MESSAGES) {
		const error = new NaxpError(name, takesAnArgument(name) ? 'x' : null);

		assert.ok(!error.text.includes(name), `${name} names itself in its text`);
		assert.ok(!error.toString().includes(name), `${name} names itself in toString`);
	}
});

// #endregion
// #region The whole-naxp convention

test('a refusal with a position is not mistaken for the whole naxp', () => {
	assert.equal(new NaxpError(NaxpMessage.NAXP1047_TooManyValues).isWholeNaxp, true);
	assert.equal(new NaxpError(NaxpMessage.NAXP1002_IntervalHyphen, null, 0, 1).isWholeNaxp, false);
	assert.equal(new NaxpError(NaxpMessage.NAXP1002_IntervalHyphen, null, 3, 1).isWholeNaxp, false);
});

// #endregion
