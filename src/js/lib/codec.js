// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * The encoding and its inverse, as walks of the canonical machine.
 *
 * The value is a mixed radix positional number, most significant digit first. Passing a transition
 * skips every value below it, the rank of the character within its set is the leading digit, and
 * the value of the remainder of the string is the rest.
 *
 * Neither walk recurses. The specification writes both as recursions, but the encoding one only
 * ever adds its result to what the caller accumulated, so it flattens into a loop, and the longest
 * string a naxp may generate would otherwise want as many stack frames as it has characters.
 *
 * Decoding uses the canonical machine only. The accepted language plays no part in it.
 */

/**
 * The value of a canonical string, or zero if the machine does not accept it.
 *
 * @param {import('./state-map.js').StateMap} map The machine for the canonical language.
 * @param {string} text The string, which must already be in canonical form.
 * @returns {bigint} The value, from 1 upwards, or zero.
 */
export function encode(map, text) {
	if (map === null || map === undefined) { throw new TypeError('map is required.'); }

	let state = map.start;
	let total = 0n;

	for (let i = 0; i < text.length; ++i) {
		const code = text.charCodeAt(i);
		let skipped = 0n;
		let next = null;

		for (const transition of state.transitions) {
			const count = transition.next.valueCount;

			if (transition.set.contains(code)) {
				total += skipped + (count * BigInt(transition.set.indexOf(code)));
				next = transition.next;
				break;
			}

			// An empty set is the end of text transition, which stands for one value.
			skipped += count * (transition.set.isEmpty ? 1n : BigInt(transition.set.count));
		}

		if (next === null) { return 0n; }

		state = next;
	}

	return state.acceptsEndOfText ? total + 1n : 0n;
}

/**
 * The string of a value, which is the value's position in the canonical language.
 *
 * @param {import('./state-map.js').StateMap} map The machine for the canonical language.
 * @param {bigint} value The value, from 1 to the size of that language.
 * @returns {string | null} The string, or null if the value is not one the naxp can produce.
 */
export function tryDecode(map, value) {
	if (map === null || map === undefined) { throw new TypeError('map is required.'); }

	// Zero is reserved for a string the naxp does not accept, so it decodes to nothing.
	if (value <= 0n || value > map.valueCount) { return null; }

	const characters = [];
	let state = map.start;
	let remaining = value;

	while (!state.isTerminal) {
		let next = null;

		for (const transition of state.transitions) {
			if (transition.set.isEmpty) {
				if (remaining === 1n) { return characters.join(''); }

				remaining -= 1n;
				continue;
			}

			const perCharacter = transition.next.valueCount;
			const block = BigInt(transition.set.count) * perCharacter;

			if (remaining <= block) {
				characters.push(String.fromCharCode(
					transition.set.characterAt(Number((remaining - 1n) / perCharacter))));

				remaining = ((remaining - 1n) % perCharacter) + 1n;
				next = transition.next;
				break;
			}

			remaining -= block;
		}

		// The value was checked against the count of the start state, and each step leaves it
		// within the count of the state it moves to, so this cannot be reached.
		if (next === null) { return null; }

		state = next;
	}

	return characters.join('');
}
