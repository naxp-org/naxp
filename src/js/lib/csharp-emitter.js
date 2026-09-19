// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { Emitter, NaxpValueType } from './emitter.js';
import { COPY_MARKER } from './tx.js';
import { DEPTH_BASE } from './tx-machine.js';

/** The name the generated code keeps the characters it has read under. */
const HELD_NAME = 'held';

// The System types the fragment uses, fully qualified.
const READ_ONLY_SPAN_OF_CHAR = 'global::System.ReadOnlySpan<char>';
const READ_ONLY_SPAN_OF_BYTE = 'global::System.ReadOnlySpan<byte>';
const SPAN_OF_CHAR = 'global::System.Span<char>';
const SPAN_OF_BYTE = 'global::System.Span<byte>';
const ARGUMENT_OUT_OF_RANGE_EXCEPTION = 'global::System.ArgumentOutOfRangeException';

/** How each value type is spelled in C#, and what suffix its literals take. */
const VALUE_TYPES = Object.freeze({
	Int8: { keyword: 'sbyte', suffix: '' },
	UInt8: { keyword: 'byte', suffix: '' },
	Int16: { keyword: 'short', suffix: '' },
	UInt16: { keyword: 'ushort', suffix: '' },
	Int32: { keyword: 'int', suffix: '' },
	UInt32: { keyword: 'uint', suffix: 'U' },
	Int64: { keyword: 'long', suffix: 'L' },
	UInt64: { keyword: 'ulong', suffix: 'UL' },
});

/**
 * Emits a compiled naxp as a C# fragment.
 *
 * The fragment is a set of static members, two consts, the public methods and their private
 * steppers, answering the same questions as `Naxp` for its one naxp, without calling back into any
 * naxp library. Self-containment is a requirement rather than a taste: the code lands in the
 * caller's assembly, where nothing internal to a library is visible, and the source generator that
 * carries it cannot resolve package references of its own. The wrapping class, any namespace and
 * any header comment are the caller's job. System types are spelled `global::System.…`, the source
 * generator convention, so no using directive or shadowing type in the wrapper can disturb the
 * fragment.
 *
 * Every machine is emitted as a switch over states, never as a table. A table walk pays a popcount
 * over two ulongs per character to rank the character within its set; in a switch the rank
 * constant-folds into the arithmetic of its case. With the state cap at 2 000 no machine is large
 * enough to want a table.
 *
 * A switch is split into methods of at most {@link Emitter.chunkSize} states, dispatched by state
 * number; that constant carries the per-method limits behind the split.
 */
export class CSharpEmitter extends Emitter {
	/** The shared instance, which is stateless and serves every call. */
	static get instance() {
		if (shared === null) { shared = new CSharpEmitter(); }

		return shared;
	}

	/** @param {import('./emitter.js').Context} context What the call works from. */
	emitFragment(context) {
		new Fragment(this, context).emit();
	}

	/** @inheritdoc */
	openFunction(writer, name, parameters) {
		writer.line(`static int ${name}(${parameters})`);
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

	/**
	 * An unsigned literal, grouped with underscores the way this codebase writes big numbers.
	 *
	 * @param {bigint} value The value.
	 * @returns {string} The literal.
	 */
	literal(value) {
		return this.grouped(value) + 'UL';
	}

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
		if (first === last) { return skipped === 0n ? null : this.literal(skipped); }

		// The accumulator is unsigned, and C# will not mix ulong with the int this subtraction
		// gives, so the rank is converted here rather than at every use.
		const index = `(ulong)(c - ${charLiteral(first)})`;
		const term = count === 1n ? index : `${this.literal(count)} * ${index}`;

		return skipped === 0n ? term : `${this.literal(skipped)} + ${term}`;
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
			text += `index < ${this.literal(cumulative)} ? ${this.runCharacter(runs[i], offset)} : `;
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

		const index = offset === 0n ? '(int)index' : `(int)(index - ${this.literal(offset)})`;

		return `(char)(${charLiteral(run.first)} + ${index})`;
	}
}

/** @type {CSharpEmitter | null} */
let shared = null;

/**
 * One emission call's state: the context and the generated names built from the prefix. Per call so
 * the shared instance stays stateless.
 */
class Fragment {
	/**
	 * @param {CSharpEmitter} emitter The emitter, for the shared skeletons.
	 * @param {import('./emitter.js').Context} context What the call works from.
	 */
	constructor(emitter, context) {
		this.emitter = emitter;
		this.context = context;

		// The C# spelling of the chosen value type. The steppers work in ulong throughout whatever
		// the choice, because C# promotes narrow operands to int anyway and casts through the
		// switch bodies would be pure noise; only the public boundary changes.
		const spelling = VALUE_TYPES[context.valueType];

		if (spelling === undefined) {
			throw new TypeError(`Unhandled value type ${context.valueType}.`);
		}

		this.valueKeyword = spelling.keyword;
		this.valueIsWidest = context.valueType === NaxpValueType.UInt64;
		this.maxEncodedValueValue = emitter.grouped(context.compilation.maxEncodedValue) + spelling.suffix;
		this.valueZero = this.valueIsWidest ? '0UL' : '0';
		this.valueOne = this.valueIsWidest ? '1UL' : '1';

		// DecodeCore takes ulong. Only ulong reaches it unconverted; every other type needs the
		// cast, which the range check just made safe.
		this.decodeCoreArgument = this.valueIsWidest ? 'value' : '(ulong)value';

		// The generated names, each the prefix plus the bare member name.
		const prefix = context.prefix;

		this.maxEncodedValueName = prefix + 'MaxEncodedValue';
		this.maxLengthName = prefix + 'MaxLength';
		this.acceptsName = prefix + 'Accepts';
		this.encodeName = prefix + 'Encode';
		this.decodeName = prefix + 'Decode';
		this.decodeToBytesName = prefix + 'DecodeToBytes';
		this.tryDecodeName = prefix + 'TryDecode';
		this.rankName = prefix + 'Rank';
		this.decodeCoreName = prefix + 'DecodeCore';
		this.acceptStepName = prefix + 'AcceptStep';
		this.isAcceptingName = prefix + 'IsAccepting';
		this.encodeStepName = prefix + 'EncodeStep';
		this.isCanonicalAcceptingName = prefix + 'IsCanonicalAccepting';
		this.decodeStepName = prefix + 'DecodeStep';
		this.canonicalStepName = prefix + 'CanonicalStep';
		this.finishCanonicalName = prefix + 'FinishCanonical';
	}

	get writer() { return this.context.writer; }

	get acceptedStates() { return this.context.acceptedStates; }

	get canonicalStates() { return this.context.canonicalStates; }

	get transducerStates() { return this.context.transducerStates; }

	get maxLength() { return this.context.maxLength; }

	get registerDepth() { return this.context.registerDepth; }

	get needsRegister() { return this.context.needsRegister; }

	get canonicalises() { return this.transducerStates !== null; }

	emit() {
		this.emitConstants();
		this.emitAccepts(false);
		this.emitAccepts(true);
		this.emitEncode(false);
		this.emitEncode(true);
		this.emitDecodePublics();
		this.emitSteppers();
	}

	emitConstants() {
		this.writer.line('/// <summary>The largest encoded value this naxp produces, which is also how many it has.</summary>');
		this.writer.line(`public const ${this.valueKeyword} ${this.maxEncodedValueName} = ${this.maxEncodedValueValue};`);
		this.writer.line();
		this.writer.line('/// <summary>The length of the longest string this naxp can decode a value to.</summary>');
		this.writer.line(`public const int ${this.maxLengthName} = ${this.maxLength};`);
		this.writer.line();
	}

	/** @param {boolean} bytes Whether this is the byte overload. */
	emitAccepts(bytes) {
		if (bytes) {
			this.writer.line('/// <summary>Whether this naxp accepts the specified ASCII text. A byte outside ASCII is never accepted.</summary>');
			this.writer.line('/// <param name="text">The ASCII text to test.</param>');
			this.writer.line('/// <returns>Whether the naxp accepts it.</returns>');
			this.writer.line(`public static bool ${this.acceptsName}(${READ_ONLY_SPAN_OF_BYTE} text)`);
		} else {
			this.writer.line('/// <summary>Whether this naxp accepts the specified string.</summary>');
			this.writer.line('/// <param name="text">The string to test.</param>');
			this.writer.line('/// <returns>Whether the naxp accepts it.</returns>');
			this.writer.line(`public static bool ${this.acceptsName}(${READ_ONLY_SPAN_OF_CHAR} text)`);
		}

		this.writer.openBlock();
		this.writer.line('int state = 0;');
		this.writer.line();
		this.writer.line(bytes ? 'foreach (byte b in text)' : 'foreach (char c in text)');
		this.writer.openBlock();
		this.writer.line(bytes
			? `state = ${this.acceptStepName}(state, (char)b);`
			: `state = ${this.acceptStepName}(state, c);`);
		this.writer.line();
		this.writer.line('if (state < 0) { return false; }');
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line(`return ${this.isAcceptingName}(state);`);
		this.writer.closeBlock();
		this.writer.line();
	}

	/** @param {boolean} bytes Whether this is the byte overload. */
	emitEncode(bytes) {
		if (bytes) {
			this.writer.line('/// <summary>The encoded value of ASCII text.</summary>');
			this.writer.line('/// <param name="text">The ASCII text to encode.</param>');
			this.writer.line(`/// <returns>The encoded value, from 1 to <see cref="${this.maxEncodedValueName}"/>, or zero where the text is invalid.</returns>`);
			this.writer.line(`public static ${this.valueKeyword} ${this.encodeName}(${READ_ONLY_SPAN_OF_BYTE} text)`);
		} else {
			this.writer.line('/// <summary>The encoded value of a string.</summary>');
			this.writer.line('/// <param name="text">The string to encode.</param>');
			this.writer.line(`/// <returns>The encoded value, from 1 to <see cref="${this.maxEncodedValueName}"/>, or zero where the string is invalid.</returns>`);
			this.writer.line(`public static ${this.valueKeyword} ${this.encodeName}(${READ_ONLY_SPAN_OF_CHAR} text)`);
		}

		this.writer.openBlock();

		if (!this.canonicalises) {
			this.writer.line('int state = 0;');
			this.writer.line('ulong total = 0UL;');
			this.writer.line();
			this.writer.line(bytes ? 'foreach (byte b in text)' : 'foreach (char c in text)');
			this.writer.openBlock();
			this.writer.line(bytes
				? `state = ${this.encodeStepName}(state, (char)b, ref total);`
				: `state = ${this.encodeStepName}(state, c, ref total);`);
			this.writer.line();
			this.writer.line(`if (state < 0) { return ${this.valueZero}; }`);
			this.writer.closeBlock();
			this.writer.line();
			this.writer.line(this.valueIsWidest
				? `return ${this.isAcceptingName}(state) ? total + 1UL : 0UL;`
				: `return ${this.isAcceptingName}(state) ? (${this.valueKeyword})(total + 1UL) : (${this.valueKeyword})0;`);
		} else {
			this.writer.line(`${SPAN_OF_CHAR} canonical = stackalloc char[${this.maxLengthName}];`);

			if (this.needsRegister) {
				// The characters read, oldest first, so a reference of depth d is the one at
				// registerDepth - 1 - d. Shifting a buffer this small beats indexing a ring.
				this.writer.line(`${SPAN_OF_CHAR} ${HELD_NAME} = stackalloc char[${this.registerDepth}];`);
			}

			this.writer.line('int length = 0;');
			this.writer.line('int state = 0;');
			this.writer.line();
			this.writer.line(bytes ? 'foreach (byte b in text)' : 'foreach (char c in text)');
			this.writer.openBlock();

			if (this.needsRegister) {
				const top = this.registerDepth - 1;

				if (this.registerDepth > 1) {
					this.writer.line(`for (int h = 0; h < ${top}; ++h) { ${HELD_NAME}[h] = ${HELD_NAME}[h + 1]; }`);
				}

				this.writer.line(bytes ? `${HELD_NAME}[${top}] = (char)b;` : `${HELD_NAME}[${top}] = c;`);
			}

			this.writer.line(bytes
				? `state = ${this.canonicalStepName}(state, (char)b, ${this.stepArguments()});`
				: `state = ${this.canonicalStepName}(state, c, ${this.stepArguments()});`);
			this.writer.line();
			this.writer.line(`if (state < 0) { return ${this.valueZero}; }`);
			this.writer.closeBlock();
			this.writer.line();
			this.writer.line(`length = ${this.finishCanonicalName}(state, ${this.finishArguments()});`);
			this.writer.line();
			this.writer.line(this.valueIsWidest
				? `return length < 0 ? 0UL : ${this.rankName}(canonical.Slice(0, length));`
				: `return length < 0 ? (${this.valueKeyword})0 : (${this.valueKeyword})${this.rankName}(canonical.Slice(0, length));`);
		}

		this.writer.closeBlock();
		this.writer.line();
	}

	emitDecodePublics() {
		const throwStatement =
			`throw new ${ARGUMENT_OUT_OF_RANGE_EXCEPTION}(nameof(value), value, "This naxp encodes the values 1 to ${this.context.compilation.maxEncodedValue}.");`;

		this.writer.line('/// <summary>The string a value stands for.</summary>');
		this.writer.line(`/// <param name="value">The encoded value, from 1 to <see cref="${this.maxEncodedValueName}"/>.</param>`);
		this.writer.line('/// <returns>The string, which is in canonical form.</returns>');
		this.writer.line(`/// <exception cref="${ARGUMENT_OUT_OF_RANGE_EXCEPTION}">The value is not one this naxp produces.</exception>`);
		this.writer.line(`public static string ${this.decodeName}(${this.valueKeyword} value)`);
		this.writer.openBlock();
		this.writer.line(`if (value < ${this.valueOne} || value > ${this.maxEncodedValueName})`);
		this.writer.openBlock();
		this.writer.line(throwStatement);
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line(`${SPAN_OF_CHAR} destination = stackalloc char[${this.maxLengthName}];`);
		this.writer.line();
		this.writer.line(`return destination.Slice(0, ${this.decodeCoreName}(${this.decodeCoreArgument}, destination)).ToString();`);
		this.writer.closeBlock();
		this.writer.line();

		this.writer.line('/// <summary>The string a value stands for, as ASCII bytes.</summary>');
		this.writer.line(`/// <param name="value">The encoded value, from 1 to <see cref="${this.maxEncodedValueName}"/>.</param>`);
		this.writer.line('/// <returns>The bytes, which spell the string in canonical form.</returns>');
		this.writer.line(`/// <exception cref="${ARGUMENT_OUT_OF_RANGE_EXCEPTION}">The value is not one this naxp produces.</exception>`);
		this.writer.line(`public static byte[] ${this.decodeToBytesName}(${this.valueKeyword} value)`);
		this.writer.openBlock();
		this.writer.line(`if (value < ${this.valueOne} || value > ${this.maxEncodedValueName})`);
		this.writer.openBlock();
		this.writer.line(throwStatement);
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line(`${SPAN_OF_CHAR} buffer = stackalloc char[${this.maxLengthName}];`);
		this.writer.line(`int length = ${this.decodeCoreName}(${this.decodeCoreArgument}, buffer);`);
		this.writer.line('var result = new byte[length];');
		this.writer.line();
		this.writer.line('for (int i = 0; i < length; ++i) { result[i] = (byte)buffer[i]; }');
		this.writer.line();
		this.writer.line('return result;');
		this.writer.closeBlock();
		this.writer.line();

		this.writer.line('/// <summary>Tries to write the string a value stands for.</summary>');
		this.writer.line('/// <param name="value">The encoded value.</param>');
		this.writer.line('/// <param name="destination">Where the string is written.</param>');
		this.writer.line('/// <param name="charsWritten">How many characters were written, or zero where none were.</param>');
		this.writer.line('/// <returns>False where the value is not one this naxp produces, or the destination is too short.</returns>');
		this.writer.line(`public static bool ${this.tryDecodeName}(${this.valueKeyword} value, ${SPAN_OF_CHAR} destination, out int charsWritten)`);
		this.writer.openBlock();
		this.writer.line(`if (value < ${this.valueOne} || value > ${this.maxEncodedValueName})`);
		this.writer.openBlock();
		this.writer.line('charsWritten = 0;');
		this.writer.line('return false;');
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line(`if (destination.Length >= ${this.maxLengthName})`);
		this.writer.openBlock();
		this.writer.line(`charsWritten = ${this.decodeCoreName}(${this.decodeCoreArgument}, destination);`);
		this.writer.line('return true;');
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line(`${SPAN_OF_CHAR} buffer = stackalloc char[${this.maxLengthName}];`);
		this.writer.line(`int length = ${this.decodeCoreName}(${this.decodeCoreArgument}, buffer);`);
		this.writer.line();
		this.writer.line('if (length > destination.Length)');
		this.writer.openBlock();
		this.writer.line('charsWritten = 0;');
		this.writer.line('return false;');
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line('buffer.Slice(0, length).CopyTo(destination);');
		this.writer.line('charsWritten = length;');
		this.writer.line('return true;');
		this.writer.closeBlock();
		this.writer.line();

		this.writer.line('/// <summary>Tries to write the string a value stands for, as ASCII bytes.</summary>');
		this.writer.line('/// <param name="value">The encoded value.</param>');
		this.writer.line('/// <param name="destination">Where the bytes are written.</param>');
		this.writer.line('/// <param name="bytesWritten">How many bytes were written, or zero where none were.</param>');
		this.writer.line('/// <returns>False where the value is not one this naxp produces, or the destination is too short.</returns>');
		this.writer.line(`public static bool ${this.tryDecodeName}(${this.valueKeyword} value, ${SPAN_OF_BYTE} destination, out int bytesWritten)`);
		this.writer.openBlock();
		this.writer.line(`if (value < ${this.valueOne} || value > ${this.maxEncodedValueName})`);
		this.writer.openBlock();
		this.writer.line('bytesWritten = 0;');
		this.writer.line('return false;');
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line(`${SPAN_OF_CHAR} buffer = stackalloc char[${this.maxLengthName}];`);
		this.writer.line(`int length = ${this.decodeCoreName}(${this.decodeCoreArgument}, buffer);`);
		this.writer.line();
		this.writer.line('if (length > destination.Length)');
		this.writer.openBlock();
		this.writer.line('bytesWritten = 0;');
		this.writer.line('return false;');
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line('for (int i = 0; i < length; ++i) { destination[i] = (byte)buffer[i]; }');
		this.writer.line();
		this.writer.line('bytesWritten = length;');
		this.writer.line('return true;');
		this.writer.closeBlock();
		this.writer.line();
	}

	emitSteppers() {
		if (this.canonicalises) {
			this.writer.line('/// <summary>The rank of a canonical string within the canonical language, or zero where it is not in it.</summary>');
			this.writer.line(`static ulong ${this.rankName}(${READ_ONLY_SPAN_OF_CHAR} canonical)`);
			this.writer.openBlock();
			this.writer.line('int state = 0;');
			this.writer.line('ulong total = 0UL;');
			this.writer.line();
			this.writer.line('foreach (char c in canonical)');
			this.writer.openBlock();
			this.writer.line(`state = ${this.encodeStepName}(state, c, ref total);`);
			this.writer.line();
			this.writer.line('if (state < 0) { return 0UL; }');
			this.writer.closeBlock();
			this.writer.line();
			this.writer.line(`return ${this.isCanonicalAcceptingName}(state) ? total + 1UL : 0UL;`);
			this.writer.closeBlock();
			this.writer.line();
		}

		this.writer.line(`/// <summary>Writes the string of a value that was already checked against <see cref="${this.maxEncodedValueName}"/>, and returns its length.</summary>`);
		this.writer.line(`static int ${this.decodeCoreName}(ulong value, ${SPAN_OF_CHAR} destination)`);
		this.writer.openBlock();
		this.writer.line('ulong remaining = value;');
		this.writer.line('int state = 0;');
		this.writer.line('int length = 0;');
		this.writer.line();
		this.writer.line('while (state >= 0)');
		this.writer.openBlock();
		this.writer.line(`state = ${this.decodeStepName}(state, ref remaining, destination, ref length);`);
		this.writer.closeBlock();
		this.writer.line();
		this.writer.line('return length;');
		this.writer.closeBlock();
		this.writer.line();

		this.writer.line("/// <summary>The acceptor's transition: the next state, or -1 where the character fits nothing.</summary>");
		this.emitter.emitStepFunctions(
			this.writer,
			this.acceptStepName,
			'int state, char c',
			'state, c',
			this.acceptedStates.length,
			id => this.emitAcceptCase(id));
		this.writer.line();

		this.emitAcceptingPredicate(this.isAcceptingName, this.acceptedStates);
		this.writer.line();

		this.writer.line("/// <summary>The canonical machine's transition, accumulating the values skipped: the next state, or -1.</summary>");
		this.emitter.emitStepFunctions(
			this.writer,
			this.encodeStepName,
			'int state, char c, ref ulong total',
			'state, c, ref total',
			this.canonicalStates.length,
			id => this.emitEncodeCase(id));
		this.writer.line();

		if (this.canonicalises) {
			this.emitAcceptingPredicate(this.isCanonicalAcceptingName, this.canonicalStates);
			this.writer.line();
		}

		this.writer.line('/// <summary>One step of decoding: appends at most one character and returns the next state, or -1 when the string is complete.</summary>');
		this.emitter.emitStepFunctions(
			this.writer,
			this.decodeStepName,
			`int state, ref ulong remaining, ${SPAN_OF_CHAR} destination, ref int length`,
			'state, ref remaining, destination, ref length',
			this.canonicalStates.length,
			id => this.emitDecodeCase(id),
			'ulong index;',
			id => Emitter.needsIndex(this.canonicalStates[id]));

		if (this.canonicalises) {
			const heldParameter = this.needsRegister ? `${SPAN_OF_CHAR} ${HELD_NAME}, ` : '';

			this.writer.line();
			this.writer.line('/// <summary>The canonicalising transition, appending what reading the character emits: the next state, or -1.</summary>');
			this.emitter.emitStepFunctions(
				this.writer,
				this.canonicalStepName,
				`int state, char c, ${heldParameter}${SPAN_OF_CHAR} canonical, ref int length`,
				`state, c, ${this.stepArguments()}`,
				this.transducerStates.length,
				id => this.emitCanonicalCase(id));
			this.writer.line();

			this.writer.line('/// <summary>Appends what ending the input emits and returns the final length, or -1 where the input may not end here.</summary>');
			this.emitter.emitStepFunctions(
				this.writer,
				this.finishCanonicalName,
				`int state, ${heldParameter}${SPAN_OF_CHAR} canonical, int length`,
				`state, ${this.finishArguments()}`,
				this.transducerStates.length,
				id => this.emitFinishCase(id),
				null,
				null,
				'-1',
				id => this.transducerStates[id].endOutput !== null);
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

		this.writer.line(`case ${id}:`);
		this.writer.indentBy();

		for (const arc of state.arcs) {
			let offset = 0n;

			for (const run of Emitter.getRuns(arc.set)) {
				// Passing this run skips the values below it: those skipped before the whole
				// transition, and this transition's earlier runs. Within the run the character's
				// rank folds into (c - first).
				const skipped = arc.skippedBefore + (arc.nextCount * offset);
				const added = this.emitter.addedExpression(skipped, arc.nextCount, run.first, run.last);
				const condition = this.emitter.runCondition(run.first, run.last);

				this.writer.line(added === null
					? `if (${condition}) { return ${arc.next}; }`
					: `if (${condition}) { total += ${added}; return ${arc.next}; }`);

				offset += BigInt(run.last - run.first + 1);
			}
		}

		this.writer.line('break;');
		this.writer.outdent();
	}

	/** @param {number} id The state. */
	emitDecodeCase(id) {
		const state = this.canonicalStates[id];

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
			this.writer.line('if (remaining == 1UL) { return -1; }');
			this.writer.line();
			this.writer.line('remaining -= 1UL;');
		}

		for (let i = 0; i < state.arcs.length; ++i) {
			const arc = state.arcs[i];
			const block = arc.nextCount * BigInt(arc.set.count);

			if (i < state.arcs.length - 1) {
				if (i > 0 || state.acceptsEnd) { this.writer.line(); }

				this.writer.line(`if (remaining <= ${this.emitter.literal(block)})`);
				this.writer.openBlock();
				this.emitDecodeArc(arc);
				this.writer.closeBlock();
				this.writer.line();
				this.writer.line(`remaining -= ${this.emitter.literal(block)};`);
			} else {
				// The last transition takes whatever is left, by the same invariant as the
				// terminal state above.
				if (i > 0 || state.acceptsEnd) { this.writer.line(); }

				this.emitDecodeArc(arc);
			}
		}

		this.writer.closeBlock();
	}

	/** @param {import('./emitter.js').ArcModel} arc The transition. */
	emitDecodeArc(arc) {
		const runs = Emitter.getRuns(arc.set);

		if (arc.set.count === 1) {
			// One character leaves the remaining value untouched: its rank is zero and the whole
			// block belongs to the next state.
			this.writer.line(`destination[length++] = ${charLiteral(runs[0].first)};`);
			this.writer.line(`return ${arc.next};`);

			return;
		}

		// index is declared once at the top of the method, because these arcs sit at differing
		// brace depths within one switch and sibling declarations there collide.
		this.writer.line(arc.nextCount === 1n
			? 'index = remaining - 1UL;'
			: `index = (remaining - 1UL) / ${this.emitter.literal(arc.nextCount)};`);
		this.writer.line(`destination[length++] = ${this.emitter.characterExpression(runs)};`);
		this.writer.line(arc.nextCount === 1n
			? 'remaining = 1UL;'
			: `remaining = ((remaining - 1UL) % ${this.emitter.literal(arc.nextCount)}) + 1UL;`);
		this.writer.line(`return ${arc.next};`);
	}

	/** @param {number} id The state. */
	emitCanonicalCase(id) {
		const state = this.transducerStates[id];

		this.writer.line(`case ${id}:`);
		this.writer.indentBy();

		for (const arc of state.arcs) {
			const condition = this.emitter.setCondition(arc.set);
			const outputs = this.outputExpressions(arc.output, false);

			if (outputs.length === 0) {
				this.writer.line(`if (${condition}) { return ${arc.next}; }`);
			} else if (outputs.length === 1) {
				this.writer.line(`if (${condition}) { canonical[length++] = ${outputs[0]}; return ${arc.next}; }`);
			} else {
				this.writer.line(`if (${condition})`);
				this.writer.openBlock();

				for (const expression of outputs) {
					this.writer.line(`canonical[length++] = ${expression};`);
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

	/** The arguments the canonicalising step takes after its character. */
	stepArguments() {
		return this.needsRegister ? `${HELD_NAME}, canonical, ref length` : 'canonical, ref length';
	}

	/** The arguments the finishing step takes after its state. */
	finishArguments() {
		return this.needsRegister ? `${HELD_NAME}, canonical, length` : 'canonical, length';
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
			// there. The finish function has no character, so it reads even that one back.
			if (depth === 0 && !forFinish) {
				expressions.push('c');
				continue;
			}

			expressions.push(`${HELD_NAME}[${this.registerDepth - 1 - depth}]`);
		}

		return expressions;
	}

	/**
	 * @param {string} name The method's name.
	 * @param {import('./emitter.js').StateModel[]} states The machine.
	 */
	emitAcceptingPredicate(name, states) {
		this.writer.line('/// <summary>Whether the input may end in this state.</summary>');
		this.writer.line(`static bool ${name}(int state)`);
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
}

/**
 * A character literal, quoted and escaped as C# writes one.
 *
 * @param {number} code The character's code.
 * @returns {string} The literal.
 */
function charLiteral(code) {
	if (code === 0x27) { return "'\\''"; }

	if (code === 0x5c) { return "'\\\\'"; }

	return code >= 0x20 && code <= 0x7e
		? `'${String.fromCharCode(code)}'`
		: `'\\u${code.toString(16).toUpperCase().padStart(4, '0')}'`;
}
