// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * Generates random naxps, runs them through the JavaScript implementation, and writes what it
 * found in the shape of the conformance data, so that another implementation can be run
 * against thousands of naxps nobody chose rather than the sixty seven somebody did.
 *
 * This is differential testing and not conformance: the JavaScript implementation is the
 * standard here, and a disagreement is a defect in one of the two. The generator beside this
 * folder reads no implementation, which is what makes its data conformance data; this reads
 * one, which is why it lives apart and its output is not committed.
 *
 *     node conformance/fuzz/generate.js --seed 1 --count 2000 --out build/fuzz.json
 *
 * The same seed gives the same file, so a failure can be reproduced by its seed alone.
 */

import { writeFileSync } from 'node:fs';
import { tryCompile } from '../../src/js/lib/compiler.js';
import { Naxp } from '../../src/js/lib/index.js';
import { ruleOfCode } from '../../src/js/test/naxp-message-rules.js';

// The command line

const options = { seed: 1, count: 2000, out: 'fuzz.json' };

for (let i = 2; i < process.argv.length; i += 2) {
	const name = process.argv[i].replace(/^--/, '');
	const value = process.argv[i + 1];

	if (!(name in options) || value === undefined) {
		console.error('Usage: node generate.js [--seed N] [--count N] [--out FILE]');
		process.exit(2);
	}

	options[name] = name === 'out' ? value : Number(value);
}

// A seeded generator, so that a run can be repeated.

let state = options.seed >>> 0;

function random() {
	// mulberry32
	state = (state + 0x6D2B79F5) >>> 0;
	let t = state;
	t = Math.imul(t ^ (t >>> 15), t | 1);
	t ^= t + Math.imul(t ^ (t >>> 7), t | 61);

	return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}

function below(n) {
	return Math.floor(random() * n);
}

function chance(probability) {
	return random() < probability;
}

function pick(items) {
	return items[below(items.length)];
}

/** A random BigInt from 1 to `limit` inclusive. */
function bigBelow(limit) {
	const digits = limit.toString().length;
	let text = '';

	for (let i = 0; i < digits + 2; ++i) { text += below(10); }

	return (BigInt(text) % limit) + 1n;
}

// Random naxps, built from the grammar

const atoms = ['A', 'B', 'C', 'a', 'b', 'c', '0', '1', '9', '\\s', '\\9', '\\A', '\\a', '\\X', '\\-', '\\!', '~', 'Z', 'z'];
const setItems = ['A', 'B', 'a', 'b', '0', '9', 'A-C', 'a-c', '0-9', '\\9', '\\A', '\\s', '\\-', 'X-Z'];

function space() {
	return chance(0.1) ? pick([' ', '  ', '\t']) : '';
}

/**
 * One bound of a decimal range. The lower bound may carry leading zeros and marks on them; the
 * upper bound mostly does not, since a wider upper bound with a leading zero breaks W4 and a
 * few of those are enough.
 */
function bound(lower) {
	let text = '';
	const width = 1 + below(3);

	for (let i = 0; i < width; ++i) {
		const leading = i < width - 1 && chance(lower ? 0.5 : 0.1);
		const digit = leading ? '0' : String(below(10));
		text += digit;

		if (lower && digit === '0' && i < width - 1 && chance(0.6)) { text += pick(['!', '?']); }
	}

	return text;
}

function decimalRange() {
	const low = bound(true);
	let high = bound(false);

	// A lower bound wider than the upper breaks W4, so the upper is padded out to match most
	// of the time.
	const lowWidth = low.replace(/[!?]/g, '').length;

	if (chance(0.9)) { while (high.length < lowWidth) { high += String(below(10)); } }

	// Mostly well ordered, so the range is worth walking; the rest exercise W4.
	if (chance(0.8) && BigInt(low.replace(/[!?]/g, '')) > BigInt(high)) {
		high = low.replace(/[!?]/g, '') + String(below(10));
	}

	return `#[${low}${space()}-${space()}${high}]`;
}

function characterSet() {
	const count = 1 + below(3);
	let text = '[';

	for (let i = 0; i < count; ++i) { text += space() + pick(setItems); }

	return text + space() + ']';
}

function base(depth, insideUnified) {
	const roll = random();

	if (depth > 2 || roll < 0.55) { return pick(atoms); }
	if (roll < 0.7) { return characterSet(); }
	if (roll < 0.8) { return decimalRange(); }
	if (roll < 0.85) { return '()'; }

	return `(${space()}${expression(depth + 1, insideUnified)}${space()})`;
}

function quantifier() {
	const roll = random();

	if (roll < 0.5) { return '?'; }
	if (roll < 0.8) { return `{${1 + below(3)}}`; }

	const low = below(3);

	return `{${low}${space()},${space()}${low + 1 + below(3)}}`;
}

function element(depth, insideUnified) {
	let text = chance(0.1) ? pick(['\\C', '\\c']) : '';
	text += base(depth, insideUnified);

	let optional = false;

	if (chance(0.35)) {
		const chosen = quantifier();
		optional = chosen === '?';
		text += space() + chosen;
	}

	// A unified element inside another breaks W2, '!!' after a '?' is a syntax fault, '!!' on
	// a subject with more than one string and a rendering with more than one string both
	// break W1, so each is rare rather than impossible: some invalid naxps are wanted, but not
	// most.
	const holdsUnified = /(^|[^\\])!/.test(text);
	const single = /^[A-Za-z0-9~]$|^\\[s\-!]$/.test(text);

	if (chance(insideUnified || holdsUnified ? 0.03 : 0.25)) {
		const roll = random();

		if (roll < 0.4 && (!optional || chance(0.1)) && (single || chance(0.15))) { text += '!!'; }
		else if (roll < 0.6 && (!optional || chance(0.1))) { text += '!?'; }
		else { text += `!${space()}${chance(0.85) ? rendering(text) : element(depth + 1, true)}`; }
	}

	return text;
}

/**
 * A rendering for a subject: mostly one of the strings the subject generates, found by
 * compiling the subject on its own and decoding a value, since W1 asks for exactly that; the
 * rest are a short literal that may or may not be, so W1's faults are exercised too.
 */
function rendering(subject) {
	const { compilation } = tryCompile(subject);

	if (compilation !== null && chance(0.85)) {
		const text = compilation.tryDecode(bigBelow(compilation.maxEncodedValue));

		return text.length === 0 ? '()' : text.replace(/[!#(),\-?\[\\\]{|}]/g, c => `\\${c}`).replace(/ /g, '\\s');
	}

	const count = 1 + below(2);
	let text = '';

	for (let i = 0; i < count; ++i) { text += pick(['A', 'B', 'a', '0', '9', '\\s', '\\-', '()', 'Z']); }

	return count > 1 && chance(0.5) ? `(${text})` : text;
}

function sequence(depth, insideUnified) {
	const count = 1 + below(depth === 0 ? 4 : 3);
	let text = '';

	for (let i = 0; i < count; ++i) { text += space() + element(depth, insideUnified); }

	return text + space();
}

function expression(depth, insideUnified) {
	const count = chance(0.3) ? 2 + below(2) : 1;
	const parts = [];

	for (let i = 0; i < count; ++i) { parts.push(sequence(depth, insideUnified)); }

	return parts.join('|');
}

/** One random edit, so that near misses of the syntax are exercised as well as the syntax. */
function mutate(text) {
	const roll = random();
	const at = below(text.length + 1);

	if (roll < 0.4 && text.length > 1) { return text.slice(0, at) + text.slice(at + 1); }
	if (roll < 0.8) { return text.slice(0, at) + pick(['!', '#', '(', ')', ',', '-', '?', '[', '\\', ']', '{', '|', '}', ' ', 'x', 'é']) + text.slice(at); }

	return text.slice(0, at) + text.slice(at + 1, at + 2) + text.slice(at, at + 1) + text.slice(at + 2);
}

function randomNaxp() {
	let text = expression(0, false);

	if (chance(0.15)) { text = mutate(text); }

	return text;
}

// Text a naxp might or might not accept

/** Every string a machine's language holds, by walking it. */
function strings(map) {
	const result = [];
	const pending = [[map.start, '']];

	while (pending.length > 0) {
		const [current, prefix] = pending.pop();

		if (current.isTerminal) { result.push(prefix); continue; }

		for (const transition of current.transitions) {
			if (transition.set.isEmpty) { result.push(prefix); continue; }

			for (const code of transition.set) { pending.push([transition.next, prefix + String.fromCharCode(code)]); }
		}
	}

	return result;
}

/** Text near a string the naxp accepts: a character changed, dropped, doubled or moved. */
function nearby(text) {
	const roll = random();
	const at = below(text.length + 1);
	const replacement = pick(['A', 'a', 'Z', '0', '9', ' ', '-', '!', '~', 'é']);

	if (text.length === 0) { return replacement; }
	if (roll < 0.3) { return text.slice(0, at) + replacement + text.slice(at + 1); }
	if (roll < 0.5) { return text.slice(0, at) + text.slice(at + 1); }
	if (roll < 0.7) { return text.slice(0, at) + replacement + text.slice(at); }
	if (roll < 0.85) { return text.toLowerCase() === text ? text.toUpperCase() : text.toLowerCase(); }

	return text.replace(/ /g, '');
}

// The run

const cases = [];
const invalidNaxps = [];
const valid = [];
const seen = new Set();
const maxEnumerated = 400n;

while (cases.length + invalidNaxps.length < options.count) {
	const naxp = randomNaxp();

	if (seen.has(naxp)) { continue; }

	seen.add(naxp);

	const { compilation, error } = tryCompile(naxp);

	if (compilation === null) {
		// A fault of the naxp as a whole is given the whole pattern as its span, which is what
		// the public surface of every implementation reports.
		const whole = error.offset === 0 && error.length === 0;

		invalidNaxps.push({
			naxp,
			rule: ruleOfCode(error.code),
			code: error.code,
			offset: error.offset,
			length: whole ? naxp.length : error.length,
			note: 'generated',
		});

		continue;
	}

	const entry = {
		naxp,
		note: 'generated',
		maxEncodedValue: compilation.maxEncodedValue.toString(),
		acceptedCount: compilation.acceptedCount.toString(),
		complete: false,
		values: [],
		invalid: [],
	};

	const texts = new Set();

	if (compilation.acceptedCount <= maxEnumerated) {
		entry.complete = true;

		for (const text of strings(compilation.accepted)) { texts.add(text); }
	} else {
		for (let i = 0; i < 24; ++i) {
			texts.add(compilation.tryDecode(bigBelow(compilation.maxEncodedValue)));
		}
	}

	const accepted = [...texts];

	for (const text of accepted) {
		for (let i = 0; i < 2; ++i) { texts.add(nearby(text)); }
	}

	for (const text of texts) {
		const value = compilation.encode(text);

		if (value === 0n) {
			entry.invalid.push(text);
			continue;
		}

		entry.values.push({ in: text, out: value.toString(), canon: compilation.tryGetCanonicalForm(text) });
	}

	cases.push(entry);
	valid.push(naxp);
}

// Pairs: random pairs of valid naxps, and pairs where one is a small edit of the other, which
// is what anybody comparing two naxps is doing.

const pairs = [];
const pairCount = Math.min(valid.length, Math.max(50, Math.floor(options.count / 10)));

for (let i = 0; i < pairCount; ++i) {
	const a = pick(valid);
	let b = pick(valid);

	if (chance(0.7)) {
		// An edit that still parses, tried a few times.
		for (let attempt = 0; attempt < 5; ++attempt) {
			const candidate = mutate(a);

			if (tryCompile(candidate).compilation !== null) { b = candidate; break; }
		}
	}

	const left = Naxp.parse(a);
	const right = Naxp.parse(b);
	const comparison = Naxp.tryCompare(left, right);

	pairs.push({
		a,
		b,
		decided: comparison !== null,
		acceptedText: comparison === null ? 'Incomparable' : comparison.acceptedText,
		encoding: comparison === null ? 'Incomparable' : comparison.encoding,
		printedText: comparison === null ? 'Incomparable' : comparison.printedText,
		firstDivergentValue: Naxp.firstDivergentValue(left, right).toString(),
	});
}

const output = {
	naxpVersion: '0.10',
	testDataVersion: 9,
	note: `Generated by conformance/fuzz/generate.js from the JavaScript implementation with seed ${options.seed}. Differential test data, not conformance data.`,
	seed: options.seed,
	cases,
	invalidNaxps,
	pairs,
};

writeFileSync(options.out, JSON.stringify(output, null, '\t') + '\n', 'utf8');

const checks = cases.reduce((sum, entry) => sum + entry.values.length + entry.invalid.length, 0);

console.log(`${cases.length} valid naxps, ${invalidNaxps.length} invalid naxps, ${checks} strings, ${pairs.length} pairs -> ${options.out}`);
