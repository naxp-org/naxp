// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { containsReplaceable } from './ast.js';
import { encode as rank, tryDecode as unrank } from './codec.js';
import { NaxpError } from './naxp-error.js';
import { NaxpMessage } from './naxp-message.js';
import { NaxpLimits } from './naxp-limits.js';
import { tryParse as parseNaxp } from './parser.js';
import { NaxpLanguage, convert as convertRx } from './rx-converter.js';
import { RxFactory } from './rx.js';
import { tryBuild as buildStateMap } from './state-map.js';
import { tryBuildTxMachine } from './tx-machine.js';
import { TxFactory, convert as convertTx } from './tx.js';
import { checkW3Transduction } from './w3-checker.js';
import { check as checkWellFormedness } from './well-formedness.js';

/**
 * A naxp that has been parsed, checked and turned into machines.
 */
export class Compilation {
	/**
	 * @param {string} source The source the naxp was parsed from.
	 * @param {import('./ast.js').Ast} ast The tree.
	 * @param {import('./state-map.js').StateMap} accepted The machine for the accepted language.
	 * @param {import('./state-map.js').StateMap} canonical The machine for the canonical language.
	 * @param {boolean} canonicalIsIdentity Whether ρ is the identity.
	 * @param {import('./tx-machine.js').TxMachine | null} canonicalMachine The machine that
	 * canonicalises, or null where ρ is the identity.
	 */
	constructor(source, ast, accepted, canonical, canonicalIsIdentity, canonicalMachine) {
		this.source = source;
		this.ast = ast;

		/** The machine for the accepted language *L*. */
		this.accepted = accepted;

		/** The machine for the canonical language *C*, which the encoding ranks over. */
		this.canonical = canonical;

		/**
		 * Whether ρ is the identity, so that every accepted string is its own canonical form.
		 *
		 * True exactly when the tree holds no replaceable element, since that is the only thing
		 * that makes the canonical form differ from the input. Then *C* and *L* are the same
		 * language and encoding is a walk of the machine, with no canonicalisation.
		 */
		this.canonicalIsIdentity = canonicalIsIdentity;

		/**
		 * The machine that canonicalises, or null where ρ is the identity and there is nothing to
		 * canonicalise.
		 *
		 * Non-null exactly when `canonicalIsIdentity` is false. The compiler builds it and refuses
		 * the naxp where it will not fit the budget, so a compilation that succeeded always has
		 * one when it needs one.
		 */
		this.canonicalMachine = canonicalMachine;
	}

	/** The count of encodable values, which is the size of *C*. */
	get valueCount() {
		return this.canonical.valueCount;
	}

	/** The count of strings the naxp accepts, which is the size of *L*. */
	get acceptedCount() {
		return this.accepted.valueCount;
	}

	/**
	 * Whether the naxp accepts a string.
	 *
	 * This walks the machine for *L*, which is one transition per character. {@link encode}
	 * answers the same question, but where the naxp has a replaceable element it canonicalises
	 * first and then ranks, so it is two walks rather than one and the wrong way round to ask it.
	 *
	 * @param {string} text The string to test.
	 * @returns {boolean} Whether the naxp accepts it.
	 */
	accepts(text) {
		return this.accepted.accepts(text);
	}

	/**
	 * The value of a string, which is zero exactly when the naxp does not accept it.
	 *
	 * Encoding cannot fail. Every rule is decided when the naxp is compiled, W3 among them, so the
	 * string either has one value or is not in the language.
	 *
	 * @param {string} text The string to encode.
	 * @returns {bigint} The value, from 1 to the value count, or zero.
	 */
	encode(text) {
		if (this.canonicalIsIdentity) { return rank(this.canonical, text); }

		const canonical = this.tryGetCanonicalForm(text);

		return canonical === null ? 0n : rank(this.canonical, canonical);
	}

	/**
	 * The string a value stands for, which is a canonical form.
	 *
	 * @param {bigint} value The value, from 1 to the value count.
	 * @returns {string | null} The string, or null if the value is not one this naxp produces.
	 */
	tryDecode(value) {
		return unrank(this.canonical, value);
	}

	/**
	 * The canonical form of a string, which is the string with the match of each replaceable
	 * element replaced by that element's rendering.
	 *
	 * @param {string} text The string.
	 * @returns {string | null} The canonical form, or null if the naxp does not accept the string.
	 */
	tryGetCanonicalForm(text) {
		// Where ρ is the identity an accepted string is its own canonical form, so the answer is
		// the machine's and walking anything else would only rebuild what was passed in.
		if (this.canonicalIsIdentity) { return this.accepts(text) ? text : null; }

		// The tree walk in canonicaliser.js walks the same relation and agrees everywhere, which
		// the tests check exhaustively. The machine is linear in the length of the input where the
		// tree walk is not, and it is the form the emitters need, so it is the one the runtime
		// uses. The tree walk stays as the reference the machine is tested against.
		return this.canonicalMachine.tryCanonicalise(text);
	}
}

/**
 * Parses a naxp, checks it and builds its machines.
 *
 * Every rule is checked here or below: W4 in the parser, W2 and W1 in the well-formedness pass, W3
 * in the checker and W5 from the size of the canonical language. A compilation that succeeds is a
 * well-formed naxp.
 *
 * @param {string} text The source of the naxp.
 * @returns {{compilation: Compilation | null, error: NaxpError | null}} The compilation, or why it
 * was refused.
 */
export function tryCompile(text) {
	const parsed = parseNaxp(text);

	if (parsed.ast === null) { return { compilation: null, error: parsed.error }; }

	const ast = parsed.ast;
	const wellFormedness = checkWellFormedness(ast);

	if (wellFormedness !== null) { return { compilation: null, error: wellFormedness }; }

	// One factory across both languages and the W3 check, so the shared sub-expressions and their
	// derivatives are computed once.
	const factory = new RxFactory();

	// Everything below turns on this, so the tree is walked for it once.
	const hasReplaceable = containsReplaceable(ast);

	// The transduction is wanted twice, by the W3 check and then by the machine that
	// canonicalises, so it is converted once and both are given it.
	let txFactory = null;
	let txRoot = null;

	if (hasReplaceable) {
		txFactory = new TxFactory(factory);
		txRoot = convertTx(ast, txFactory, factory);

		// Before the machines, because a naxp that breaks W3 has no well defined encoding and
		// building its machines would say nothing about that.
		const w3 = checkW3Transduction(txRoot, txFactory);

		if (w3 !== null) { return { compilation: null, error: w3 }; }
	}

	// A replaceable element is the only node the converter reads the language at, so without one
	// the two conversions would give the same expression and the same machine.
	const canonicalIsIdentity = !hasReplaceable;

	const canonicalBuild = buildStateMap(
		convertRx(ast, factory, NaxpLanguage.Canonical),
		factory);

	if (canonicalBuild.map === null) {
		return { compilation: null, error: canonicalBuild.error };
	}

	const canonical = canonicalBuild.map;

	if (canonical.countSaturated) {
		return {
			compilation: null,
			error: new NaxpError(NaxpMessage.NAXP1047_TooManyValues),
		};
	}

	// The accepted language can legitimately be larger than the canonical one, and W5 says nothing
	// about it, so its count is allowed to saturate.
	let accepted = canonical;

	if (!canonicalIsIdentity) {
		const acceptedBuild = buildStateMap(
			convertRx(ast, factory, NaxpLanguage.Accepted),
			factory);

		if (acceptedBuild.map === null) {
			return { compilation: null, error: acceptedBuild.error };
		}

		accepted = acceptedBuild.map;
	}

	// Last, because it is the only budget a naxp can fail after passing every rule, and the
	// cheaper refusals should come first. Where it fails the naxp is legal and this implementation
	// is declining it.
	let canonicalMachine = null;

	if (hasReplaceable) {
		const built = tryBuildTxMachine(txRoot, txFactory, NaxpLimits.maxCanonicalStates);

		if (built.machine === null) { return { compilation: null, error: built.error }; }

		canonicalMachine = built.machine;
	}

	return {
		compilation: new Compilation(
			text,
			ast,
			accepted,
			canonical,
			canonicalIsIdentity,
			canonicalMachine),
		error: null,
	};
}
