// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { Emitter, NaxpValueType } from './emitter.js';
import { COPY_MARKER } from './tx.js';
import { DEPTH_BASE } from './tx-machine.js';

/** The name the generated code keeps the characters it has read under. */
export const HELD_NAME = 'held';

/** The stdint spelling of each value type, and what suffix its literals take. */
const VALUE_TYPES = Object.freeze({
	Int8: { bare: 'int8_t', suffix: '' },
	UInt8: { bare: 'uint8_t', suffix: '' },
	Int16: { bare: 'int16_t', suffix: '' },
	UInt16: { bare: 'uint16_t', suffix: '' },
	Int32: { bare: 'int32_t', suffix: '' },
	UInt32: { bare: 'uint32_t', suffix: 'U' },
	Int64: { bare: 'int64_t', suffix: 'LL' },
	UInt64: { bare: 'uint64_t', suffix: 'ULL' },
});

/**
 * What the C and C++ emitters share: the steppers, the prototypes C and C++ both need ahead of a
 * function's first use, and the snake_case names.
 *
 * The two fragments differ at their public surface, where C takes a pointer and a length and C++
 * a `string_view`, and in spelling: a pointer against a reference, a C cast against
 * `static_cast`, a block comment against a line comment, `static` against `inline`. The steppers,
 * which are most of a fragment, are otherwise the same text, so they are written once here over
 * those spellings and each language supplies its own.
 *
 * Both compilers warn of a parameter a function never reads, and with `-Wextra -Werror` the
 * warning is fatal, so every stepper starts by voiding whatever its range of states leaves
 * unused: a literal run skips nothing, so its encode step never touches the total, and a function
 * holding only such states would otherwise not compile clean. The predicates deciding that sit
 * beside the case emitters they mirror.
 */
export class CFamilyEmitter extends Emitter {
	/** @param {import('./emitter.js').Context} context What the call works from. */
	emitFragment(context) {
		const fragment = new Fragment(this, context);

		this.emitHeader(fragment);
		fragment.emitPrototypes();
		this.emitPublics(fragment);
		fragment.emitSteppers();
	}

	/* ---------- the shape of the family ---------- */

	/** @inheritdoc */
	openFunction(writer, name, parameters) {
		writer.line(`${this.stepLinkage} int ${name}(${parameters})`);
		writer.openBlock();
	}

	/** @inheritdoc */
	closeFunction(writer) {
		writer.closeBlock();
	}

	/** @inheritdoc */
	openDispatch(writer) {
		writer.line('switch (state)');
		writer.openBlock();
	}

	/** @inheritdoc */
	closeDispatch(writer, result) {
		writer.closeBlock();
		writer.line();
		writer.line(`return ${result};`);
	}

	/** @inheritdoc */
	writeReturn(writer, expression) {
		writer.line(`return ${expression};`);
	}

	/** @inheritdoc */
	writeGuardedReturn(writer, condition, expression) {
		writer.line(`if (${condition}) { return ${expression}; }`);
	}

	/** @inheritdoc */
	equalsCharacter(code) {
		return `c == ${charLiteral(code)}`;
	}

	/** @inheritdoc */
	withinRun(first, last) {
		return `c >= ${charLiteral(first)} && c <= ${charLiteral(last)}`;
	}

	/* ---------- what each language spells ---------- */

	/**
	 * Writes the fragment's opening comment and its two constants.
	 *
	 * @param {Fragment} fragment The call's state.
	 */
	emitHeader(fragment) {
		throw new Error(`${this.constructor.name} does not implement emitHeader.`);
	}

	/**
	 * Writes the public functions, which are the whole difference between the two languages.
	 *
	 * @param {Fragment} fragment The call's state.
	 */
	emitPublics(fragment) {
		throw new Error(`${this.constructor.name} does not implement emitPublics.`);
	}

	/** The linkage a stepper is declared with: `static` in C, `inline` in C++. */
	get stepLinkage() {
		throw new Error(`${this.constructor.name} does not implement stepLinkage.`);
	}

	/** What the fixed-width integer types are qualified with: nothing in C, `std::` in C++. */
	get typePrefix() {
		throw new Error(`${this.constructor.name} does not implement typePrefix.`);
	}

	/**
	 * A pointer parameter, which the two languages space differently.
	 *
	 * @param {string} type The pointed-to type.
	 * @param {string} name The parameter.
	 * @returns {string} The declaration.
	 */
	pointer(type, name) {
		throw new Error(`${this.constructor.name} does not implement pointer.`);
	}

	/**
	 * A parameter the stepper writes through: a pointer in C, a reference in C++.
	 *
	 * @param {string} type The type written.
	 * @param {string} name The parameter.
	 * @returns {string} The declaration.
	 */
	byReference(type, name) {
		throw new Error(`${this.constructor.name} does not implement byReference.`);
	}

	/**
	 * Reading or writing a {@link CFamilyEmitter#byReference} parameter.
	 *
	 * @param {string} name The parameter.
	 * @returns {string} The expression.
	 */
	dereference(name) {
		throw new Error(`${this.constructor.name} does not implement dereference.`);
	}

	/**
	 * Post-incrementing a {@link CFamilyEmitter#byReference} parameter, as an array index.
	 *
	 * @param {string} name The parameter.
	 * @returns {string} The expression.
	 */
	increment(name) {
		throw new Error(`${this.constructor.name} does not implement increment.`);
	}

	/**
	 * Passing a local on to a {@link CFamilyEmitter#byReference} parameter.
	 *
	 * @param {string} name The local.
	 * @returns {string} The argument.
	 */
	addressOf(name) {
		throw new Error(`${this.constructor.name} does not implement addressOf.`);
	}

	/**
	 * An explicit conversion of an expression, which the language brackets as it needs.
	 *
	 * @param {string} type The type converted to.
	 * @param {string} expression The expression, unbracketed.
	 * @returns {string} The conversion.
	 */
	cast(type, expression) {
		throw new Error(`${this.constructor.name} does not implement cast.`);
	}

	/**
	 * A one-line comment.
	 *
	 * @param {import('./code-writer.js').CodeWriter} writer Where the fragment is going.
	 * @param {string} text The comment.
	 */
	comment(writer, text) {
		throw new Error(`${this.constructor.name} does not implement comment.`);
	}

	/** The 64-bit unsigned type the steppers work in. */
	get uint64() { return this.typePrefix + 'uint64_t'; }

	/**
	 * An unsigned 64-bit literal, grouped where the language has a separator.
	 *
	 * @param {bigint} value The value.
	 * @returns {string} The literal.
	 */
	literal(value) {
		return this.grouped(value) + 'ULL';
	}
}

/**
 * The prefix and a member name, in snake_case. An underscore goes in wherever the case turns
 * upward and the previous character was a letter or digit, and before an upper case letter that
 * starts a new word after a run of them, so `UKPostcode` gives `uk_postcode`; a prefix already in
 * snake_case comes through as it is.
 *
 * @param {string} prefix The caller's prefix, possibly empty.
 * @param {string} member The member name, in PascalCase.
 * @returns {string} The name.
 */
export function snake(prefix, member) {
	const name = prefix + member;
	let text = '';

	for (let i = 0; i < name.length; ++i) {
		const c = name[i];

		if (i > 0 && isUpper(c)) {
			const previous = name[i - 1];
			const boundary = isLower(previous)
				|| isDigit(previous)
				|| (isUpper(previous) && i + 1 < name.length && isLower(name[i + 1]));

			if (boundary) { text += '_'; }
		}

		text += isUpper(c) ? c.toLowerCase() : c;
	}

	return text;
}

/**
 * @param {string} c One character.
 * @returns {boolean} Whether it is an ASCII capital.
 */
function isUpper(c) {
	return c >= 'A' && c <= 'Z';
}

/**
 * @param {string} c One character.
 * @returns {boolean} Whether it is an ASCII small letter.
 */
function isLower(c) {
	return c >= 'a' && c <= 'z';
}

/**
 * @param {string} c One character.
 * @returns {boolean} Whether it is an ASCII digit.
 */
function isDigit(c) {
	return c >= '0' && c <= '9';
}

/**
 * An ASCII character as a literal, with the two that need it escaped and the unprintable ones in
 * hexadecimal. A hexadecimal escape runs to the closing quote, so it is safe here where it would
 * not be inside a string.
 *
 * @param {number} code The character's code.
 * @returns {string} The literal.
 */
export function charLiteral(code) {
	if (code === 0x27) { return "'\\''"; }

	if (code === 0x5c) { return "'\\\\'"; }

	return code >= 0x20 && code <= 0x7e
		? `'${String.fromCharCode(code)}'`
		: `'\\x${code.toString(16).toUpperCase().padStart(2, '0')}'`;
}

/**
 * Whether an identifier needs no brackets under a C cast.
 *
 * @param {string} expression The expression.
 * @returns {boolean} Whether it is a bare identifier.
 */
export function isIdentifier(expression) {
	return /^[A-Za-z0-9_]+$/.test(expression);
}

/**
 * Whether an output reads the register, which mirrors {@link Fragment#outputExpressions}: a step
 * reads it for any reference past the character in hand, and the finish function for any
 * reference at all.
 *
 * @param {string} output The output, over literals and references.
 * @param {boolean} forFinish Whether this is an end output.
 * @returns {boolean} Whether it reaches back.
 */
function reachesBack(output, forFinish) {
	for (let i = 0; i < output.length; ++i) {
		if (output[i] !== COPY_MARKER) { continue; }

		const depth = output.charCodeAt(i + 1) - DEPTH_BASE;

		++i;

		if (depth > 0 || forFinish) { return true; }
	}

	return false;
}

/**
 * One emission call's state: the context, the generated names, the value type's spelling, and
 * the steppers, which both languages write through this. Per call so the shared instance stays
 * stateless.
 */
export class Fragment {
	/**
	 * @param {CFamilyEmitter} emitter The emitter, for the shared skeletons and the spellings.
	 * @param {import('./emitter.js').Context} context What the call works from.
	 */
	constructor(emitter, context) {
		this.emitter = emitter;
		this.context = context;

		const spelling = VALUE_TYPES[context.valueType];

		if (spelling === undefined) {
			throw new TypeError(`Unhandled value type ${context.valueType}.`);
		}

		// The spelling of the chosen value type. The steppers work in the 64-bit unsigned type
		// throughout whatever the choice; only the public boundary changes.
		this.valueKeyword = emitter.typePrefix + spelling.bare;
		this.valueIsWidest = context.valueType === NaxpValueType.UInt64;
		this.maxEncodedValueLiteral = emitter.grouped(context.compilation.maxEncodedValue) + spelling.suffix;
		this.valueZero = this.valueIsWidest ? '0ULL' : '0';
		this.valueOne = this.valueIsWidest ? '1ULL' : '1';

		// decodeCore takes the widest type. Only that reaches it unconverted; every other type
		// needs the cast, which the range check just made safe.
		this.decodeCoreArgument = this.valueIsWidest ? 'value' : emitter.cast(emitter.uint64, 'value');

		// The generated names, each the prefix plus the bare member name in snake_case.
		const prefix = context.prefix;

		this.maxEncodedValueName = snake(prefix, 'MaxEncodedValue');
		this.maxLengthName = snake(prefix, 'MaxLength');
		this.acceptsName = snake(prefix, 'Accepts');
		this.encodeName = snake(prefix, 'Encode');
		this.decodeName = snake(prefix, 'Decode');
		this.rankName = snake(prefix, 'Rank');
		this.decodeCoreName = snake(prefix, 'DecodeCore');
		this.acceptStepName = snake(prefix, 'AcceptStep');
		this.isAcceptingName = snake(prefix, 'IsAccepting');
		this.encodeStepName = snake(prefix, 'EncodeStep');
		this.isCanonicalAcceptingName = snake(prefix, 'IsCanonicalAccepting');
		this.decodeStepName = snake(prefix, 'DecodeStep');
		this.canonicalStepName = snake(prefix, 'CanonicalStep');
		this.finishCanonicalName = snake(prefix, 'FinishCanonical');
	}

	get writer() { return this.context.writer; }

	get acceptedStates() { return this.context.acceptedStates; }

	get canonicalStates() { return this.context.canonicalStates; }

	get transducerStates() { return this.context.transducerStates; }

	get maxLength() { return this.context.maxLength; }

	get registerDepth() { return this.context.registerDepth; }

	get needsRegister() { return this.context.needsRegister; }

	get canonicalises() { return this.transducerStates !== null; }

	/**
	 * The prefix in the language's own casing, for the public names each language adds.
	 *
	 * @param {string} member The member name, in PascalCase.
	 * @returns {string} The name.
	 */
	name(member) {
		return snake(this.context.prefix, member);
	}

	/**
	 * What sizes a character buffer. The longest string's length, except that a zero-length array
	 * is illegal in both languages, so a naxp whose only string is empty gets one byte that
	 * nothing writes.
	 */
	get bufferSize() { return this.maxLength === 0 ? '1' : this.maxLengthName; }

	/** The largest encoded value in plain digits, for a message. */
	get maxEncodedValueDigits() { return this.context.compilation.maxEncodedValue.toString(); }

	/* ---------- the steppers' signatures ---------- */

	// Each parameter list is written once here and read by the prototype and the definition
	// alike, so the two cannot drift.

	get rankParameters() { return `${this.emitter.pointer('const char', 'canonical')}, int length`; }

	get decodeCoreParameters() { return `${this.emitter.uint64} value, ${this.emitter.pointer('char', 'destination')}`; }

	get acceptStepParameters() { return 'int state, int c'; }

	get encodeStepParameters() { return `int state, int c, ${this.emitter.byReference(this.emitter.uint64, 'total')}`; }

	get decodeStepParameters() {
		return `int state, ${this.emitter.byReference(this.emitter.uint64, 'remaining')}, ${this.emitter.pointer('char', 'destination')}, ${this.emitter.byReference('int', 'length')}`;
	}

	get heldParameter() { return this.needsRegister ? `${this.emitter.pointer('const char', HELD_NAME)}, ` : ''; }

	get canonicalStepParameters() {
		return `int state, int c, ${this.heldParameter}${this.emitter.pointer('char', 'canonical')}, ${this.emitter.byReference('int', 'length')}`;
	}

	get finishCanonicalParameters() {
		return `int state, ${this.heldParameter}${this.emitter.pointer('char', 'canonical')}, int length`;
	}

	/**
	 * The arguments the canonicalising step takes after its character, as a public function
	 * passes them.
	 *
	 * @param {string} lengthArgument How the length local is passed on.
	 * @returns {string} The arguments.
	 */
	stepArguments(lengthArgument) {
		return this.needsRegister ? `${HELD_NAME}, canonical, ${lengthArgument}` : `canonical, ${lengthArgument}`;
	}

	/** @returns {string} The arguments the finishing step takes after its state. */
	finishArguments() {
		return this.needsRegister ? `${HELD_NAME}, canonical, length` : 'canonical, length';
	}

	/* ---------- prototypes ---------- */

	/**
	 * Declares every stepper ahead of the public functions that call it. Both languages need a
	 * function declared before its first use, and the public functions come first because they
	 * are what a reader is looking for.
	 */
	emitPrototypes() {
		const linkage = this.emitter.stepLinkage;

		this.emitter.comment(this.writer, 'The steppers, which are defined below the public functions.');

		if (this.canonicalises) {
			this.writer.line(`${linkage} ${this.emitter.uint64} ${this.rankName}(${this.rankParameters});`);
		}

		this.writer.line(`${linkage} int ${this.decodeCoreName}(${this.decodeCoreParameters});`);
		this.emitStepPrototypes(this.acceptStepName, this.acceptStepParameters, this.acceptedStates.length);
		this.writer.line(`${linkage} bool ${this.isAcceptingName}(int state);`);
		this.emitStepPrototypes(this.encodeStepName, this.encodeStepParameters, this.canonicalStates.length);

		if (this.canonicalises) {
			this.writer.line(`${linkage} bool ${this.isCanonicalAcceptingName}(int state);`);
		}

		this.emitStepPrototypes(this.decodeStepName, this.decodeStepParameters, this.canonicalStates.length);

		if (this.canonicalises) {
			this.emitStepPrototypes(this.canonicalStepName, this.canonicalStepParameters, this.transducerStates.length);
			this.emitStepPrototypes(this.finishCanonicalName, this.finishCanonicalParameters, this.transducerStates.length);
		}

		this.writer.line();
	}

	/**
	 * A stepper's prototype, and its chunks' where the machine is split.
	 *
	 * @param {string} name The stepper.
	 * @param {string} parameters Its parameter list.
	 * @param {number} stateCount The count of states it dispatches over.
	 */
	emitStepPrototypes(name, parameters, stateCount) {
		this.writer.line(`${this.emitter.stepLinkage} int ${name}(${parameters});`);

		const chunkCount = Emitter.chunkCount(stateCount);

		if (chunkCount === 1) { return; }

		for (let chunk = 0; chunk < chunkCount; ++chunk) {
			this.writer.line(`${this.emitter.stepLinkage} int ${name}${chunk}(${parameters});`);
		}
	}

	/* ---------- the steppers ---------- */

	emitSteppers() {
		const linkage = this.emitter.stepLinkage;
		const uint64 = this.emitter.uint64;

		if (this.canonicalises) {
			this.emitter.comment(this.writer, 'The rank of a canonical string within the canonical language, or zero where it is not in it.');
			this.writer.line(`${linkage} ${uint64} ${this.rankName}(${this.rankParameters})`);
			this.writer.openBlock();
			this.writer.line('int state = 0;');
			this.writer.line(`${uint64} total = 0ULL;`);
			this.writer.line();
			this.writer.line('for (int i = 0; i < length; ++i)');
			this.writer.openBlock();
			this.writer.line(`state = ${this.encodeStepName}(state, canonical[i], ${this.emitter.addressOf('total')});`);
			this.writer.line();
			this.writer.line('if (state < 0) { return 0ULL; }');
			this.writer.closeBlock();
			this.writer.line();
			this.writer.line(`return ${this.isCanonicalAcceptingName}(state) ? total + 1ULL : 0ULL;`);
			this.writer.closeBlock();
			this.writer.line();
		}

		this.emitter.comment(this.writer, `Writes the string of a value that was already checked against ${this.maxEncodedValueName}, and returns its length.`);
		this.writer.line(`${linkage} int ${this.decodeCoreName}(${this.decodeCoreParameters})`);
		this.writer.openBlock();
		this.writer.line(`${uint64} remaining = value;`);
		this.writer.line('int state = 0;');
		this.writer.line('int length = 0;');
		this.writer.line();
		this.writer.line('while (state >= 0)');
		this.writer.openBlock();
		this.writer.line(`state = ${this.decodeStepName}(state, ${this.emitter.addressOf('remaining')}, destination, ${this.emitter.addressOf('length')});`);
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line('return length;');
		this.writer.closeBlock();
		this.writer.line();

		this.emitter.comment(this.writer, "The acceptor's transition: the next state, or -1 where the character fits nothing.");
		this.emitter.emitStepFunctions(
			this.writer,
			this.acceptStepName,
			this.acceptStepParameters,
			'state, c',
			this.acceptedStates.length,
			id => this.emitAcceptCase(id),
			null,
			null,
			'-1',
			null,
			(first, count) => this.acceptPrologue(first, count));
		this.writer.line();

		this.emitAcceptingPredicate(this.isAcceptingName, this.acceptedStates);
		this.writer.line();

		this.emitter.comment(this.writer, "The canonical machine's transition, accumulating the values skipped: the next state, or -1.");
		this.emitter.emitStepFunctions(
			this.writer,
			this.encodeStepName,
			this.encodeStepParameters,
			'state, c, total',
			this.canonicalStates.length,
			id => this.emitEncodeCase(id),
			null,
			null,
			'-1',
			null,
			(first, count) => this.encodePrologue(first, count));
		this.writer.line();

		if (this.canonicalises) {
			this.emitAcceptingPredicate(this.isCanonicalAcceptingName, this.canonicalStates);
			this.writer.line();
		}

		this.emitter.comment(this.writer, 'One step of decoding: appends at most one character and returns the next state, or -1 when the string is complete.');
		this.emitter.emitStepFunctions(
			this.writer,
			this.decodeStepName,
			this.decodeStepParameters,
			'state, remaining, destination, length',
			this.canonicalStates.length,
			id => this.emitDecodeCase(id),
			`${uint64} index;`,
			id => Emitter.needsIndex(this.canonicalStates[id]),
			'-1',
			null,
			(first, count) => this.decodePrologue(first, count));

		if (this.canonicalises) {
			this.writer.line();
			this.emitter.comment(this.writer, 'The canonicalising transition, appending what reading the character emits: the next state, or -1.');
			this.emitter.emitStepFunctions(
				this.writer,
				this.canonicalStepName,
				this.canonicalStepParameters,
				`state, c, ${this.stepArguments('length')}`,
				this.transducerStates.length,
				id => this.emitCanonicalCase(id),
				null,
				null,
				'-1',
				null,
				(first, count) => this.canonicalPrologue(first, count));
			this.writer.line();

			this.emitter.comment(this.writer, 'Appends what ending the input emits and returns the final length, or -1 where the input may not end here.');
			this.emitter.emitStepFunctions(
				this.writer,
				this.finishCanonicalName,
				this.finishCanonicalParameters,
				`state, ${this.finishArguments()}`,
				this.transducerStates.length,
				id => this.emitFinishCase(id),
				null,
				null,
				'-1',
				id => this.transducerStates[id].endOutput !== null,
				(first, count) => this.finishPrologue(first, count));
		}
	}

	/** @param {number} id The state. */
	emitAcceptCase(id) {
		const state = this.acceptedStates[id];

		this.writer.line(`case ${id}:`);
		this.writer.indentBy();

		for (const arc of state.arcs) {
			this.writer.line(`if (${this.emitter.setCondition(arc.set)}) { return ${arc.next}; }`);
		}

		this.writer.line('break;');
		this.writer.outdent();
	}

	/** @param {number} id The state. */
	emitEncodeCase(id) {
		const state = this.canonicalStates[id];
		const total = this.emitter.dereference('total');

		this.writer.line(`case ${id}:`);
		this.writer.indentBy();

		for (const arc of state.arcs) {
			let offset = 0n;

			for (const run of Emitter.getRuns(arc.set)) {
				// Passing this run skips the values below it: those skipped before the whole
				// transition, and this transition's earlier runs. Within the run the character's
				// rank folds into (c - first).
				const skipped = arc.skippedBefore + (arc.nextCount * offset);
				const added = this.addedExpression(skipped, arc.nextCount, run.first, run.last);
				const condition = this.emitter.runCondition(run.first, run.last);

				this.writer.line(added === null
					? `if (${condition}) { return ${arc.next}; }`
					: `if (${condition}) { ${total} += ${added}; return ${arc.next}; }`);

				offset += BigInt(run.last - run.first + 1);
			}
		}

		this.writer.line('break;');
		this.writer.outdent();
	}

	/** @param {number} id The state. */
	emitDecodeCase(id) {
		const state = this.canonicalStates[id];
		const remaining = this.emitter.dereference('remaining');

		this.writer.line(`case ${id}:`);
		this.writer.openBlock();

		if (state.arcs.length === 0) {
			// The terminal state. The remaining value is one here, because the caller checked the
			// value against the count of the start state and every step keeps it within the count
			// of the state it moves to.
			this.writer.line('return -1;');
			this.writer.closeBlock();

			return;
		}

		if (state.acceptsEnd) {
			this.writer.line(`if (${remaining} == 1ULL) { return -1; }`);
			this.writer.line();
			this.writer.line(`${remaining} -= 1ULL;`);
		}

		for (let i = 0; i < state.arcs.length; ++i) {
			const arc = state.arcs[i];
			const block = arc.nextCount * BigInt(arc.set.count);

			if (i < state.arcs.length - 1) {
				if (i > 0 || state.acceptsEnd) { this.writer.line(); }

				this.writer.line(`if (${remaining} <= ${this.emitter.literal(block)})`);
				this.writer.openBlock();
				this.emitDecodeArc(arc);
				this.writer.closeBlock();
				this.writer.line();
				this.writer.line(`${remaining} -= ${this.emitter.literal(block)};`);
			} else {
				// The last transition takes whatever is left, by the same invariant as the
				// terminal state above.
				if (i > 0 || state.acceptsEnd) { this.writer.line(); }

				this.emitDecodeArc(arc);
			}
		}

		this.writer.closeBlock();
	}

	/** @param {import('./emitter.js').ArcModel} arc The arc. */
	emitDecodeArc(arc) {
		const remaining = this.emitter.dereference('remaining');
		const append = `destination[${this.emitter.increment('length')}]`;
		const runs = Emitter.getRuns(arc.set);

		if (arc.set.count === 1) {
			// One character leaves the remaining value untouched: its rank is zero and the whole
			// block belongs to the next state.
			this.writer.line(`${append} = ${charLiteral(runs[0].first)};`);
			this.writer.line(`return ${arc.next};`);

			return;
		}

		// index is declared once at the top of the function, because these arcs sit at differing
		// brace depths within one switch and sibling declarations there collide.
		this.writer.line(arc.nextCount === 1n
			? `index = ${remaining} - 1ULL;`
			: `index = (${remaining} - 1ULL) / ${this.emitter.literal(arc.nextCount)};`);
		this.writer.line(`${append} = ${this.characterExpression(runs)};`);
		this.writer.line(arc.nextCount === 1n
			? `${remaining} = 1ULL;`
			: `${remaining} = ((${remaining} - 1ULL) % ${this.emitter.literal(arc.nextCount)}) + 1ULL;`);
		this.writer.line(`return ${arc.next};`);
	}

	/** @param {number} id The state. */
	emitCanonicalCase(id) {
		const state = this.transducerStates[id];
		const append = `canonical[${this.emitter.increment('length')}]`;

		this.writer.line(`case ${id}:`);
		this.writer.indentBy();

		for (const arc of state.arcs) {
			const condition = this.emitter.setCondition(arc.set);
			const outputs = this.outputExpressions(arc.output, false);

			if (outputs.length === 0) {
				this.writer.line(`if (${condition}) { return ${arc.next}; }`);
			} else if (outputs.length === 1) {
				this.writer.line(`if (${condition}) { ${append} = ${outputs[0]}; return ${arc.next}; }`);
			} else {
				this.writer.line(`if (${condition})`);
				this.writer.openBlock();

				for (const expression of outputs) {
					this.writer.line(`${append} = ${expression};`);
				}

				this.writer.line(`return ${arc.next};`);
				this.writer.closeBlock();
			}
		}

		this.writer.line('break;');
		this.writer.outdent();
	}

	/** @param {number} id The state. */
	emitFinishCase(id) {
		const state = this.transducerStates[id];

		// A state where the input may not end has no case, so it falls to the default.
		if (state.endOutput === null) { return; }

		this.writer.line(`case ${id}:`);
		this.writer.indentBy();

		for (const expression of this.outputExpressions(state.endOutput, true)) {
			this.writer.line(`canonical[length++] = ${expression};`);
		}

		this.writer.line('return length;');
		this.writer.outdent();
	}

	/**
	 * One expression per character an output emits, with each reference resolved against the
	 * characters kept.
	 *
	 * @param {string} output The output, over literals and references.
	 * @param {boolean} forFinish Whether this is an end output, which has no character in hand.
	 * @returns {string[]} The expressions.
	 */
	outputExpressions(output, forFinish) {
		const expressions = [];

		for (let i = 0; i < output.length; ++i) {
			if (output[i] !== COPY_MARKER) {
				expressions.push(charLiteral(output.charCodeAt(i)));
				continue;
			}

			const depth = output.charCodeAt(i + 1) - DEPTH_BASE;

			++i;

			// A step already holds the character it is reading, so depth zero needs no buffer
			// there. The finish function has no character, so it reads even that one back. The
			// character in hand is an int, so it is converted going in.
			if (depth === 0 && !forFinish) {
				expressions.push(this.emitter.cast('char', 'c'));
				continue;
			}

			expressions.push(`${HELD_NAME}[${this.registerDepth - 1 - depth}]`);
		}

		return expressions;
	}

	/**
	 * @param {string} name The function's name.
	 * @param {import('./emitter.js').StateModel[]} states The machine.
	 */
	emitAcceptingPredicate(name, states) {
		this.emitter.comment(this.writer, 'Whether the input may end in this state.');
		this.writer.line(`${this.emitter.stepLinkage} bool ${name}(int state)`);
		this.writer.openBlock();
		this.writer.line('switch (state)');
		this.writer.openBlock();

		for (let id = 0; id < states.length; ++id) {
			if (states[id].acceptsEnd) { this.writer.line(`case ${id}:`); }
		}

		this.writer.indentBy();
		this.writer.line('return true;');
		this.writer.outdent();
		this.writer.line('default:');
		this.writer.indentBy();
		this.writer.line('return false;');
		this.writer.outdent();
		this.writer.closeBlock();
		this.writer.closeBlock();
	}

	/* ---------- what a range of states leaves unused ---------- */

	// Each predicate mirrors the case emitter above it: a parameter is used exactly when some
	// state in the range writes a line that names it. The compiler harness in the tests is what
	// keeps them honest.

	/**
	 * @param {number} first The first state the function holds.
	 * @param {number} count How many it holds.
	 */
	acceptPrologue(first, count) {
		this.markUnused([['c', anyArcs(this.acceptedStates, first, count)]]);
	}

	/**
	 * @param {number} first The first state the function holds.
	 * @param {number} count How many it holds.
	 */
	encodePrologue(first, count) {
		this.markUnused([
			['c', anyArcs(this.canonicalStates, first, count)],
			['total', this.anyAddition(first, count)],
		]);
	}

	/**
	 * @param {number} first The first state the function holds.
	 * @param {number} count How many it holds.
	 */
	decodePrologue(first, count) {
		const appends = anyArcs(this.canonicalStates, first, count);

		this.markUnused([
			['remaining', this.anyRemaining(first, count)],
			['destination', appends],
			['length', appends],
		]);
	}

	/**
	 * @param {number} first The first state the function holds.
	 * @param {number} count How many it holds.
	 */
	canonicalPrologue(first, count) {
		const appends = this.anyOutput(first, count);

		this.markUnused([
			['c', anyArcs(this.transducerStates, first, count)],
			[HELD_NAME, !this.needsRegister || this.anyReachBack(first, count, false)],
			['canonical', appends],
			['length', appends],
		]);
	}

	/**
	 * @param {number} first The first state the function holds.
	 * @param {number} count How many it holds.
	 */
	finishPrologue(first, count) {
		const any = this.anyEndOutput(first, count);

		this.markUnused([
			['state', any],
			[HELD_NAME, !this.needsRegister || this.anyReachBack(first, count, true)],
			['canonical', this.anyEndText(first, count)],
			['length', any],
		]);
	}

	/**
	 * Voids each parameter the range leaves unused, so the compiler does not warn of it. A
	 * parameter the function does not have counts as used, which is how the register is passed
	 * where there is none.
	 *
	 * @param {[string, boolean][]} parameters Each parameter's name and whether the range uses it.
	 */
	markUnused(parameters) {
		let any = false;

		for (const [name, used] of parameters) {
			if (used) { continue; }

			this.writer.line(`(void)${name};`);
			any = true;
		}

		if (any) { this.writer.line(); }
	}

	/**
	 * Whether any run in the range adds to the total, which mirrors
	 * {@link Fragment#addedExpression}.
	 *
	 * @param {number} first The first state.
	 * @param {number} count How many states.
	 * @returns {boolean} Whether any does.
	 */
	anyAddition(first, count) {
		for (let id = first; id < first + count; ++id) {
			if (this.canonicalStates[id].arcs.some(arc => arc.set.count > 1 || arc.skippedBefore !== 0n)) {
				return true;
			}
		}

		return false;
	}

	/**
	 * Whether any state in the range reads the remaining value, which mirrors
	 * {@link Fragment#emitDecodeCase}.
	 *
	 * @param {number} first The first state.
	 * @param {number} count How many states.
	 * @returns {boolean} Whether any does.
	 */
	anyRemaining(first, count) {
		for (let id = first; id < first + count; ++id) {
			const state = this.canonicalStates[id];

			// The terminal state returns before it reads anything.
			if (state.arcs.length === 0) { continue; }

			if (state.acceptsEnd || state.arcs.length > 1 || Emitter.needsIndex(state)) { return true; }
		}

		return false;
	}

	/**
	 * @param {number} first The first state.
	 * @param {number} count How many states.
	 * @returns {boolean} Whether any arc in the range writes anything.
	 */
	anyOutput(first, count) {
		for (let id = first; id < first + count; ++id) {
			if (this.transducerStates[id].arcs.some(arc => arc.output.length > 0)) { return true; }
		}

		return false;
	}

	/**
	 * @param {number} first The first state.
	 * @param {number} count How many states.
	 * @returns {boolean} Whether the input may end in any state of the range.
	 */
	anyEndOutput(first, count) {
		for (let id = first; id < first + count; ++id) {
			if (this.transducerStates[id].endOutput !== null) { return true; }
		}

		return false;
	}

	/**
	 * @param {number} first The first state.
	 * @param {number} count How many states.
	 * @returns {boolean} Whether ending in any state of the range writes anything.
	 */
	anyEndText(first, count) {
		for (let id = first; id < first + count; ++id) {
			const endOutput = this.transducerStates[id].endOutput;

			if (endOutput !== null && endOutput.length > 0) { return true; }
		}

		return false;
	}

	/**
	 * Whether any output in the range reads the register.
	 *
	 * @param {number} first The first state.
	 * @param {number} count How many states.
	 * @param {boolean} forFinish Whether to ask the end outputs rather than the arcs'.
	 * @returns {boolean} Whether any does.
	 */
	anyReachBack(first, count, forFinish) {
		for (let id = first; id < first + count; ++id) {
			const state = this.transducerStates[id];

			if (forFinish) {
				if (state.endOutput !== null && reachesBack(state.endOutput, true)) { return true; }

				continue;
			}

			if (state.arcs.some(arc => reachesBack(arc.output, false))) { return true; }
		}

		return false;
	}

	/* ---------- expression helpers ---------- */

	/**
	 * What passing this run adds to the total, or null where it adds nothing.
	 *
	 * @param {bigint} skipped The values below the run.
	 * @param {bigint} count The string count of the state the arc reaches.
	 * @param {number} first The run's first character code.
	 * @param {number} last The run's last character code.
	 * @returns {string | null} The expression, or null.
	 */
	addedExpression(skipped, count, first, last) {
		if (first === last) { return skipped === 0n ? null : this.emitter.literal(skipped); }

		// The accumulator is unsigned and the subtraction is an int, so the rank is converted
		// here rather than at every use.
		const index = this.emitter.cast(this.emitter.uint64, `c - ${charLiteral(first)}`);
		const term = count === 1n ? index : `${this.emitter.literal(count)} * ${index}`;

		return skipped === 0n ? term : `${this.emitter.literal(skipped)} + ${term}`;
	}

	/**
	 * The character at position `index` within a set, as an expression over its runs.
	 *
	 * @param {{first: number, last: number}[]} runs The set's runs.
	 * @returns {string} The expression.
	 */
	characterExpression(runs) {
		if (runs.length === 1) { return this.runCharacter(runs[0], 0n); }

		let text = '';
		let cumulative = 0n;

		for (let i = 0; i < runs.length - 1; ++i) {
			const offset = cumulative;

			cumulative += BigInt(runs[i].last - runs[i].first + 1);
			text += `index < ${this.emitter.literal(cumulative)} ? ${this.runCharacter(runs[i], offset)} : `;
		}

		return text + this.runCharacter(runs[runs.length - 1], cumulative);
	}

	/**
	 * @param {{first: number, last: number}} run The run.
	 * @param {bigint} offset Where the run starts within the set.
	 * @returns {string} The character at `index`, given that it lies in this run.
	 */
	runCharacter(run, offset) {
		if (run.first === run.last) { return charLiteral(run.first); }

		const index = offset === 0n
			? this.emitter.cast('int', 'index')
			: this.emitter.cast('int', `index - ${this.emitter.literal(offset)}`);

		return this.emitter.cast('char', `${charLiteral(run.first)} + ${index}`);
	}
}

/**
 * @param {import('./emitter.js').StateModel[] | import('./emitter.js').TxStateModel[]} states The machine.
 * @param {number} first The first state.
 * @param {number} count How many states.
 * @returns {boolean} Whether any state in the range has an arc.
 */
function anyArcs(states, first, count) {
	for (let id = first; id < first + count; ++id) {
		if (states[id].arcs.length > 0) { return true; }
	}

	return false;
}
