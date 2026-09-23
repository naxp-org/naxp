// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { CodeWriter } from './code-writer.js';
import { COPY_MARKER } from './tx.js';

/** The version a generated header names, which writing one is the caller's job. */
const PACKAGE_VERSION = '0.11.0';

/**
 * The integer type generated code uses for encoded values.
 *
 * The default everywhere is `UInt64`, which every naxp fits: W5 caps the value count at 2^64 - 1,
 * so nothing narrower is guaranteed to hold one. A narrower choice is validated against the naxp's
 * largest encoded value when emitting, so a naxp that outgrows the type its caller pinned is
 * refused rather than silently widened. Each emitter maps the choice to its language's own types;
 * a language without unsigned integers, such as Java, rules out the unsigned members.
 */
export const NaxpValueType = Object.freeze({
	Int8: 'Int8',
	UInt8: 'UInt8',
	Int16: 'Int16',
	UInt16: 'UInt16',
	Int32: 'Int32',
	UInt32: 'UInt32',
	Int64: 'Int64',
	UInt64: 'UInt64',
});

/** The largest encoded value each type holds, remembering that zero is reserved. */
const CAPACITY = Object.freeze({
	Int8: 127n,
	UInt8: 255n,
	Int16: 32767n,
	UInt16: 65535n,
	Int32: 2147483647n,
	UInt32: 4294967295n,
	Int64: 9223372036854775807n,
	UInt64: 18446744073709551615n,
});

/**
 * The base of the language emitters, which turn a compiled naxp into recogniser and codec source
 * in one language.
 *
 * An emitter writes a fragment: function definitions and the constants they share, every name
 * prefixed with the caller's prefix. The language paraphernalia around the fragment, a header
 * comment, imports, a namespace or a wrapping class, is the caller's job, which is what lets the
 * same fragment land in a bundler's module and on a web page alike.
 *
 * An instance holds only its language. Everything the naxp decides, the renumbered machines, the
 * constants encode and decode fold their arithmetic into, the buffer bound, is computed per call
 * into a context, so one instance serves every naxp. The derived emitters each cache such an
 * instance.
 *
 * States are renumbered breadth first from the start, so the start is state zero and an ordinary
 * naxp's whole machine lands in the first chunk.
 */
export class Emitter {
	/**
	 * The most states one generated function may hold. A machine above this is emitted as
	 * functions of this many states behind a dispatcher.
	 *
	 * One function holding 2 000 states would meet per-function limits: the .NET JIT stops
	 * optimising very large methods, and Java has a hard 64 KB of bytecode per method. Machines
	 * above this size only arise from long literal runs, whose cases are trivial, so the split
	 * costs an ordinary naxp nothing. The five naxps on naxp.org's landing page are all under
	 * fifty states.
	 */
	static get chunkSize() { return 250; }

	/**
	 * Constructs an emitter over its language's block syntax, with the meanings
	 * {@link CodeWriter} gives the arguments. The defaults suit the brace languages.
	 *
	 * Indentation and the newline are not here. A caller may reasonably want either, and no
	 * language is broken by the choice, so they are parameters of {@link Emitter#emit} instead.
	 * Block syntax is not a taste: a caller cannot want C# with different braces.
	 *
	 * @param {string | null} blockOpen The line that opens a block, or null for none.
	 * @param {string | null} blockClose The line that closes a block, or null for none.
	 */
	constructor(blockOpen = '{', blockClose = '}') {
		this.blockOpen = blockOpen;
		this.blockClose = blockClose;
	}

	/**
	 * Emits a compiled naxp as a source fragment.
	 *
	 * @param {import('./compiler.js').Compilation} compilation The naxp.
	 * @param {string} prefix The prefix every generated name starts with, so several naxps can
	 * share one scope. May be empty, where the bare names are wanted.
	 * @param {string} valueType The {@link NaxpValueType} the generated code uses for encoded
	 * values. The naxp's largest encoded value must fit it.
	 * @param {string} initialIndent What every line of the fragment is indented with ahead of its
	 * own depth, so it can sit inside an already-indented wrapper.
	 * @param {string} newLine What ends every line. The caller's choice rather than the host's, so
	 * that one naxp gives the same fragment on every machine.
	 * @param {string} indent What one level of indentation is written as.
	 * @returns {string} The fragment.
	 * @throws {TypeError} The prefix is neither empty nor an ASCII identifier, or one of the
	 * formatting arguments is not a string.
	 * @throws {RangeError} The largest encoded value does not fit `valueType`.
	 */
	emit(compilation, prefix = '', valueType = NaxpValueType.UInt64, initialIndent = '', newLine = '\n', indent = '\t') {
		if (compilation === null || compilation === undefined) {
			throw new TypeError('compilation is required.');
		}

		if (typeof prefix !== 'string') { throw new TypeError('prefix must be a string.'); }

		checkFormat(initialIndent, newLine, indent);

		if (prefix.length !== 0) { validateIdentifier(prefix); }

		const capacity = Emitter.capacity(valueType);

		if (compilation.maxEncodedValue > capacity) {
			throw new RangeError(
				`This naxp encodes ${compilation.maxEncodedValue} values, which does not fit ${valueType}.`);
		}

		const writer = new CodeWriter(initialIndent, indent, this.blockOpen, this.blockClose, newLine);

		this.emitFragment(new Context(compilation, prefix, valueType, writer));

		return writer.toString();
	}

	/**
	 * Writes one naxp's fragment in the derived emitter's language.
	 *
	 * @param {Context} context What the call works from.
	 */
	emitFragment(context) {
		throw new Error(`${this.constructor.name} does not implement emitFragment.`);
	}

	/**
	 * The largest encoded value a type can hold, remembering that zero is reserved.
	 *
	 * @param {string} valueType One of {@link NaxpValueType}.
	 * @returns {bigint} The largest value.
	 */
	static capacity(valueType) {
		const capacity = Object.prototype.hasOwnProperty.call(CAPACITY, valueType)
			? CAPACITY[valueType]
			: undefined;

		if (capacity === undefined) {
			throw new TypeError(`'${valueType}' is not a value type.`);
		}

		return capacity;
	}

	/** The version for a generated header, which writing one is the caller's job. */
	static packageVersion() {
		return PACKAGE_VERSION;
	}

	/**
	 * The naxp's pattern, made safe for a line comment.
	 *
	 * @param {string} text The pattern.
	 * @returns {string} The pattern with its control characters spaced out.
	 */
	static commentText(text) {
		let safe = '';

		for (const character of text) {
			safe += character < ' ' ? ' ' : character;
		}

		return safe;
	}

	/**
	 * The set's characters as inclusive runs of consecutive codes, in ascending order.
	 *
	 * @param {import('./ascii-char-set.js').AsciiCharSet} set The characters.
	 * @returns {{first: number, last: number}[]} The runs.
	 */
	static getRuns(set) {
		const runs = [];
		let first = 0;
		let previous = 0;
		let open = false;

		for (const code of set) {
			if (!open) {
				first = code;
				open = true;
			} else if (code !== previous + 1) {
				runs.push({ first, last: previous });
				first = code;
			}

			previous = code;
		}

		if (open) { runs.push({ first, last: previous }); }

		return runs;
	}

	/**
	 * Whether any of a state's decode arcs picks a character by rank, wanting a local for it.
	 *
	 * @param {StateModel} state The state.
	 * @returns {boolean} Whether it does.
	 */
	static needsIndex(state) {
		return state.arcs.some(arc => arc.set.count > 1);
	}

	/**
	 * What separates groups of three digits in a numeric literal, or null where the language has
	 * no such thing.
	 *
	 * An underscore in C#, JavaScript, Java and Python; an apostrophe in C++14; nothing at all in
	 * C, which never adopted a separator.
	 */
	get digitSeparator() { return '_'; }

	/**
	 * The digits of a value, in groups of three where the language has a separator for them, and
	 * without a type suffix.
	 *
	 * @param {bigint} value The value.
	 * @returns {string} The literal's digits.
	 */
	grouped(value) {
		const digits = value.toString();
		const separator = this.digitSeparator;

		if (digits.length <= 4 || separator === null) { return digits; }

		let leading = digits.length % 3;

		if (leading === 0) { leading = 3; }

		let text = digits.slice(0, leading);

		for (let i = leading; i < digits.length; i += 3) {
			text += separator + digits.slice(i, i + 3);
		}

		return text;
	}

	/**
	 * Writes a function's header and opens its body.
	 *
	 * A language's whole function syntax sits behind this and {@link Emitter#closeFunction}: C#
	 * writes a return type and a brace on a line of its own, JavaScript a keyword and a brace at
	 * the end of the line, and a Python emitter would write `def` and a colon and let the
	 * indenting do the rest. Every function these skeletons write returns the same thing, a state
	 * or a flag, so the return type belongs to the language rather than to the call.
	 *
	 * @param {CodeWriter} writer Where the fragment is going.
	 * @param {string} name The function's name.
	 * @param {string} parameters The parameter list.
	 */
	openFunction(writer, name, parameters) {
		throw new Error(`${this.constructor.name} does not implement openFunction.`);
	}

	/**
	 * Closes a body opened by {@link Emitter#openFunction}.
	 *
	 * @param {CodeWriter} writer Where the fragment is going.
	 */
	closeFunction(writer) {
		throw new Error(`${this.constructor.name} does not implement closeFunction.`);
	}

	/**
	 * Opens the dispatch on the state, by whatever construct the language dispatches with.
	 *
	 * @param {CodeWriter} writer Where the fragment is going.
	 */
	openDispatch(writer) {
		throw new Error(`${this.constructor.name} does not implement openDispatch.`);
	}

	/**
	 * Writes the result for a state the dispatch does not name, and closes it.
	 *
	 * @param {CodeWriter} writer Where the fragment is going.
	 * @param {string} result What such a state returns.
	 */
	closeDispatch(writer, result) {
		throw new Error(`${this.constructor.name} does not implement closeDispatch.`);
	}

	/**
	 * Returns an expression, as one statement.
	 *
	 * @param {CodeWriter} writer Where the fragment is going.
	 * @param {string} expression What is returned.
	 */
	writeReturn(writer, expression) {
		throw new Error(`${this.constructor.name} does not implement writeReturn.`);
	}

	/**
	 * Returns an expression where a condition holds, on one line.
	 *
	 * @param {CodeWriter} writer Where the fragment is going.
	 * @param {string} condition The test.
	 * @param {string} expression What is returned where it holds.
	 */
	writeGuardedReturn(writer, condition, expression) {
		throw new Error(`${this.constructor.name} does not implement writeGuardedReturn.`);
	}

	/**
	 * The test that the character in hand is one particular character.
	 *
	 * @param {number} code The character's code.
	 * @returns {string} The test.
	 */
	equalsCharacter(code) {
		throw new Error(`${this.constructor.name} does not implement equalsCharacter.`);
	}

	/**
	 * The test that the character in hand lies within an inclusive run.
	 *
	 * @param {number} first The run's first character code.
	 * @param {number} last The run's last character code.
	 * @returns {string} The test.
	 */
	withinRun(first, last) {
		throw new Error(`${this.constructor.name} does not implement withinRun.`);
	}

	/** How the language spells 'or', which joins the tests of a set's runs. */
	get orOperator() { return '||'; }

	/**
	 * The test for one inclusive run, which is an equality where the run holds one character.
	 *
	 * @param {number} first The run's first character code.
	 * @param {number} last The run's last character code.
	 * @returns {string} The test.
	 */
	runCondition(first, last) {
		return first === last ? this.equalsCharacter(first) : this.withinRun(first, last);
	}

	/**
	 * Membership of a whole set, as a test over its runs.
	 *
	 * @param {import('./ascii-char-set.js').AsciiCharSet} set The characters.
	 * @returns {string} The test.
	 */
	setCondition(set) {
		const runs = Emitter.getRuns(set);

		if (runs.length === 1) { return this.runCondition(runs[0].first, runs[0].last); }

		// A run of more than one character is a conjunction in most languages, so it is bracketed
		// where it sits beside another test.
		return runs
			.map(run => run.first === run.last
				? this.equalsCharacter(run.first)
				: `(${this.withinRun(run.first, run.last)})`)
			.join(` ${this.orOperator} `);
	}

	/**
	 * Emits a dispatch over states as one function, or over {@link Emitter.chunkSize} states at a
	 * time as a dispatcher and one function per chunk.
	 *
	 * @param {CodeWriter} writer Where the fragment is going.
	 * @param {string} name The function name, which chunks suffix with their number.
	 * @param {string} parameters The parameter list. The first parameter must be the state.
	 * @param {string} argumentList The same list as arguments, for the dispatcher to pass on.
	 * @param {number} stateCount The count of states.
	 * @param {(id: number) => void} emitCase Writes one state's whole case, label included, or
	 * nothing to leave it to the default.
	 * @param {string | null} preamble A declaration each function needs ahead of its dispatch, or
	 * null for none.
	 * @param {((id: number) => boolean) | null} preambleNeeded Whether a state's case uses the
	 * preamble. A function holding no such state leaves the declaration out, since an unused local
	 * is a warning in some of the target languages.
	 * @param {string} defaultResult What a state the dispatch does not name returns.
	 * @param {((id: number) => boolean) | null} caseNeeded Whether a state writes a case at all.
	 * A function holding no such state has nothing to dispatch on, and writes only the default.
	 * @param {((firstState: number, stateCount: number) => void) | null} prologue Writes whatever
	 * the language needs at the top of a function's body, given the first state the function
	 * holds and how many, or null for nothing. C and C++ use it to say which parameters the range
	 * leaves unused, which their compilers otherwise warn of.
	 */
	emitStepFunctions(
		writer,
		name,
		parameters,
		argumentList,
		stateCount,
		emitCase,
		preamble = null,
		preambleNeeded = null,
		defaultResult = '-1',
		caseNeeded = null,
		prologue = null
	) {
		const chunkSize = Emitter.chunkSize;
		const chunkCount = Emitter.chunkCount(stateCount);

		if (chunkCount === 1) {
			this.emitStepFunction(
				writer, name, parameters, 0, stateCount, emitCase, preamble, preambleNeeded, defaultResult,
				caseNeeded, prologue);

			return;
		}

		this.openFunction(writer, name, parameters);

		for (let chunk = 0; chunk < chunkCount - 1; ++chunk) {
			this.writeGuardedReturn(
				writer, `state < ${(chunk + 1) * chunkSize}`, `${name}${chunk}(${argumentList})`);
		}

		writer.line();
		this.writeReturn(writer, `${name}${chunkCount - 1}(${argumentList})`);
		this.closeFunction(writer);

		for (let chunk = 0; chunk < chunkCount; ++chunk) {
			const first = chunk * chunkSize;

			writer.line();
			this.emitStepFunction(
				writer,
				`${name}${chunk}`,
				parameters,
				first,
				Math.min(chunkSize, stateCount - first),
				emitCase,
				preamble,
				preambleNeeded,
				defaultResult,
				caseNeeded,
				prologue);
		}
	}

	/**
	 * How many functions a machine of this many states is emitted as: one up to
	 * {@link Emitter.chunkSize}, and above that one per chunk, which the dispatcher then fronts.
	 *
	 * Here rather than inside the skeleton because C and C++ have to declare every function
	 * before its first use, so their prototypes need the chunk names ahead of the skeleton writing
	 * them.
	 *
	 * @param {number} stateCount The count of states.
	 * @returns {number} The count of functions.
	 */
	static chunkCount(stateCount) {
		const chunkSize = Emitter.chunkSize;

		return stateCount <= chunkSize ? 1 : Math.floor((stateCount - 1) / chunkSize) + 1;
	}

	/**
	 * One dispatching function, over a run of states.
	 *
	 * @param {CodeWriter} writer Where the fragment is going.
	 * @param {string} name The function name.
	 * @param {string} parameters The parameter list.
	 * @param {number} firstState The first state the function holds.
	 * @param {number} stateCount How many states it holds.
	 * @param {(id: number) => void} emitCase Writes one state's whole case.
	 * @param {string | null} preamble The declaration ahead of the dispatch, or null.
	 * @param {((id: number) => boolean) | null} preambleNeeded Whether a state's case uses it.
	 * @param {string} defaultResult What an unnamed state returns.
	 * @param {((id: number) => boolean) | null} caseNeeded Whether a state writes a case at all.
	 * @param {((firstState: number, stateCount: number) => void) | null} prologue What the
	 * language writes at the top of the body, or null.
	 */
	emitStepFunction(
		writer,
		name,
		parameters,
		firstState,
		stateCount,
		emitCase,
		preamble,
		preambleNeeded,
		defaultResult,
		caseNeeded,
		prologue
	) {
		this.openFunction(writer, name, parameters);

		if (prologue !== null) { prologue(firstState, stateCount); }

		// A chunk of a machine where no state writes a case has nothing to dispatch on, and an
		// empty switch is both noise and, in C#, a warning. Only the finishing step can be in that
		// position, and only when chunked: its cases belong to the states where the input may end,
		// and those can all fall outside one chunk.
		if (!anyCase(firstState, stateCount, caseNeeded)) {
			this.writeReturn(writer, defaultResult);
			this.closeFunction(writer);

			return;
		}

		if (preamble !== null && needsPreamble(firstState, stateCount, preambleNeeded)) {
			writer.line(preamble);
			writer.line();
		}

		this.openDispatch(writer);

		for (let id = firstState; id < firstState + stateCount; ++id) {
			emitCase(id);
		}

		this.closeDispatch(writer, defaultResult);
		this.closeFunction(writer);
	}
}

/**
 * Throws where any of the three formatting arguments is not a string.
 *
 * @param {string} initialIndent What every line starts with.
 * @param {string} newLine What ends every line.
 * @param {string} indent What one level of indentation is written as.
 */
function checkFormat(initialIndent, newLine, indent) {
	if (typeof initialIndent !== 'string') { throw new TypeError('initialIndent must be a string.'); }

	if (typeof newLine !== 'string') { throw new TypeError('newLine must be a string.'); }

	if (typeof indent !== 'string') { throw new TypeError('indent must be a string.'); }
}

/**
 * Whether any state in a function's range writes a case.
 *
 * @param {number} firstState The first state.
 * @param {number} stateCount How many.
 * @param {((id: number) => boolean) | null} caseNeeded The test, or null where every state does.
 * @returns {boolean} Whether any does.
 */
function anyCase(firstState, stateCount, caseNeeded) {
	if (caseNeeded === null) { return true; }

	for (let id = firstState; id < firstState + stateCount; ++id) {
		if (caseNeeded(id)) { return true; }
	}

	return false;
}

/**
 * Whether any state in a function's range uses the preamble.
 *
 * @param {number} firstState The first state.
 * @param {number} stateCount How many.
 * @param {((id: number) => boolean) | null} preambleNeeded The test, or null where every function
 * needs it.
 * @returns {boolean} Whether it is needed.
 */
function needsPreamble(firstState, stateCount, preambleNeeded) {
	if (preambleNeeded === null) { return true; }

	for (let id = firstState; id < firstState + stateCount; ++id) {
		if (preambleNeeded(id)) { return true; }
	}

	return false;
}

/**
 * Throws where a name is not an ASCII identifier: an ASCII letter or underscore, then ASCII
 * letters, digits and underscores. ASCII rather than the target language's own rule, because one
 * name feeds emitters in several languages and this is what they all accept.
 *
 * @param {string} name The name.
 * @throws {TypeError} It is not an ASCII identifier.
 */
export function validateIdentifier(name) {
	const reason = tryValidateIdentifier(name);

	if (reason !== null) { throw new TypeError(reason); }
}

/**
 * Why a name fails {@link validateIdentifier}, rather than throwing. This is what a caller
 * reporting to a user, such as a code generation page, needs.
 *
 * @param {string} name The name.
 * @returns {string | null} The reason, or null where the name is an ASCII identifier.
 */
export function tryValidateIdentifier(name) {
	if (typeof name !== 'string' || name.length === 0) { return 'The name must not be empty.'; }

	if (!isAsciiLetter(name[0]) && name[0] !== '_') {
		return `'${name}' is not an ASCII identifier: it starts with '${name[0]}'.`;
	}

	for (const character of name) {
		if (!isAsciiLetter(character) && (character < '0' || character > '9') && character !== '_') {
			return `'${name}' is not an ASCII identifier: it contains '${character}'.`;
		}
	}

	return null;
}

/**
 * @param {string} character One character.
 * @returns {boolean} Whether it is an ASCII letter.
 */
function isAsciiLetter(character) {
	return (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');
}

/**
 * What one emission call works from: the naxp's machines in the form generated code takes, the
 * prefix, and the writer the fragment goes through.
 */
export class Context {
	/**
	 * @param {import('./compiler.js').Compilation} compilation The naxp.
	 * @param {string} prefix The prefix every generated name starts with, possibly empty.
	 * @param {string} valueType The integer type for encoded values, already validated against the
	 * largest one.
	 * @param {CodeWriter} writer The writer the fragment goes through, configured for the language.
	 */
	constructor(compilation, prefix, valueType, writer) {
		this.compilation = compilation;
		this.prefix = prefix;
		this.valueType = valueType;
		this.writer = writer;

		/** The machine for the canonical language *C*, which encode and decode rank over. */
		this.canonicalStates = buildMachine(compilation.canonical, true);

		/** The machine for the accepted language *L*. */
		this.acceptedStates = compilation.canonicalIsIdentity
			? this.canonicalStates
			: buildMachine(compilation.accepted, false);

		/** The canonicalisation machine, or null where ρ is the identity. */
		this.transducerStates = compilation.canonicalMachine === null
			? null
			: buildTransducer(compilation.canonicalMachine);

		/**
		 * How many characters read the generated code has to keep, so that an output can reach
		 * back to one it has not yet placed.
		 *
		 * Zero where there is no canonicalisation machine and one where every reference is to the
		 * character being read, which is the character the step already has. Only two or more
		 * needs a buffer, which is what {@link Context#needsRegister} reports.
		 */
		this.registerDepth = compilation.canonicalMachine === null
			? 0
			: compilation.canonicalMachine.registerDepth;

		/** The length of the longest canonical string, which bounds every buffer generated code needs. */
		this.maxLength = longestPath(compilation.canonical);
	}

	/**
	 * Whether the generated code has to keep characters beyond the one being read.
	 *
	 * A depth of one is every reference standing for the character being read, which a step
	 * already holds. An end output is different: the finish function has no character, so a
	 * reference there needs the buffer whatever the depth.
	 */
	get needsRegister() {
		return this.registerDepth > 1 || this.endOutputReachesBack();
	}

	/** @returns {boolean} Whether any end output reads a character back. */
	endOutputReachesBack() {
		if (this.transducerStates === null) { return false; }

		return this.transducerStates.some(
			state => state.endOutput !== null && state.endOutput.indexOf(COPY_MARKER) >= 0);
	}
}

/**
 * Renumbers a machine breadth first from its start.
 *
 * @param {import('./state-map.js').StateMap} map The machine.
 * @param {boolean} withCounts Whether to carry each transition's string counts, which encode and
 * decode fold into their constants. The accepted machine's counts may be saturated and nothing
 * generated reads them, so it does not carry them.
 * @returns {StateModel[]} The states, the start first.
 */
function buildMachine(map, withCounts) {
	const idOf = new Map();
	const ordered = [map.start];
	const queue = [map.start];

	idOf.set(map.start, 0);

	for (let head = 0; head < queue.length; ++head) {
		for (const transition of queue[head].transitions) {
			if (transition.set.isEmpty || idOf.has(transition.next)) { continue; }

			idOf.set(transition.next, ordered.length);
			ordered.push(transition.next);
			queue.push(transition.next);
		}
	}

	return ordered.map(state => {
		const arcs = [];
		let skipped = 0n;

		for (const transition of state.transitions) {
			// The end of text transition sorts first, so where it exists every arc's skipped count
			// starts from the one value it stands for.
			if (transition.set.isEmpty) {
				skipped = 1n;
				continue;
			}

			const count = withCounts ? transition.next.stringCount : 0n;

			arcs.push({
				set: transition.set,
				next: idOf.get(transition.next),

				/** The string count of the state this arc reaches. */
				nextCount: count,

				/** The count of strings sitting below this arc in its state's order. */
				skippedBefore: skipped,
			});

			skipped += count * BigInt(transition.set.count);
		}

		return { acceptsEnd: state.acceptsEndOfText, arcs };
	});
}

/**
 * Renumbers a canonicalisation machine breadth first from its start.
 *
 * @param {import('./tx-machine.js').TxMachine} machine The machine.
 * @returns {TxStateModel[]} The states, the start first.
 */
function buildTransducer(machine) {
	const idOf = new Map();
	const ordered = [machine.start];
	const queue = [machine.start];

	idOf.set(machine.start, 0);

	for (let head = 0; head < queue.length; ++head) {
		for (const transition of queue[head].transitions) {
			if (idOf.has(transition.next)) { continue; }

			idOf.set(transition.next, ordered.length);
			ordered.push(transition.next);
			queue.push(transition.next);
		}
	}

	return ordered.map(state => ({
		endOutput: state.endOutput,
		arcs: state.transitions.map(transition => ({
			set: transition.set,
			output: transition.output,
			next: idOf.get(transition.next),
		})),
	}));
}

/**
 * The length of the longest string a machine generates.
 *
 * The states are listed in creation order and every transition points at an earlier state, because
 * the builder interns each state's successors before the state itself, so a single pass has every
 * target's length ready when it is read.
 *
 * @param {import('./state-map.js').StateMap} map The machine.
 * @returns {number} The length.
 */
function longestPath(map) {
	const lengths = new Array(map.states.length).fill(0);

	for (let id = 0; id < map.states.length; ++id) {
		let longest = 0;

		for (const transition of map.states[id].transitions) {
			if (transition.set.isEmpty) { continue; }

			const viaTransition = lengths[transition.next.id] + 1;

			if (viaTransition > longest) { longest = viaTransition; }
		}

		lengths[id] = longest;
	}

	return lengths[map.start.id];
}

/**
 * @typedef {object} StateModel A state of a renumbered machine.
 * @property {boolean} acceptsEnd Whether the input may end here.
 * @property {ArcModel[]} arcs The transitions out, in the set order.
 */

/**
 * @typedef {object} ArcModel One transition of a renumbered machine.
 * @property {import('./ascii-char-set.js').AsciiCharSet} set The characters it reads.
 * @property {number} next The state it reaches.
 * @property {bigint} nextCount The string count of the state it reaches.
 * @property {bigint} skippedBefore The count of strings sitting below it in its state's order.
 */

/**
 * @typedef {object} TxStateModel A state of a renumbered canonicalisation machine.
 * @property {string | null} endOutput What ending the input here emits, or null where it may not end.
 * @property {TxArcModel[]} arcs The transitions out.
 */

/**
 * @typedef {object} TxArcModel One transition of a renumbered canonicalisation machine.
 * @property {import('./ascii-char-set.js').AsciiCharSet} set The characters it reads.
 * @property {string} output What reading one of them emits, over literals and references.
 * @property {number} next The state it reaches.
 */
