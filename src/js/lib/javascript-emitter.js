// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { Emitter } from './emitter.js';
import { COPY_MARKER } from './tx.js';
import { DEPTH_BASE } from './tx-machine.js';

/** The largest integer a JavaScript number holds exactly, `Number.MAX_SAFE_INTEGER`. */
const MAX_SAFE_INTEGER = 9007199254740991n;

/** The name the generated code keeps the code points it has read under. */
const HELD_NAME = 'held';

/** The name of the flag saying that a public function was given bytes rather than a string. */
const BYTES_NAME = 'bytes';

/**
 * The JavaScript emitter: one naxp as a fragment of declarations, in the shape a module or a
 * script tag can hold.
 *
 * The fragment is a set of const and function declarations, three consts, the public functions and
 * their private steppers, every name prefixed with the caller's prefix and camel cased, which is
 * what JavaScript readers expect. Nothing is exported: the module wrapper, the export list and any
 * header comment are the caller's job.
 *
 * Characters are handled as ASCII code points rather than one-character strings, so the steppers
 * compare numbers and bytes feed straight in. That is why each public function takes a string or a
 * `Uint8Array` alike, as the library's `Naxp` does, where the C# emitter needs an overload and a
 * cast.
 *
 * JavaScript has one number type, exact to 2^53 - 1, so the emitter reads the naxp's largest
 * encoded value and picks: ordinary numbers where every value and every intermediate rank fits,
 * BigInt above that. Ranks are bounded by that, so in the number case the arithmetic is exact.
 * The BigInt case needs ES2020; everything else needs ES2015.
 */
export class JavaScriptEmitter extends Emitter {
	/**
	 * JavaScript puts an opening brace at the end of the line it belongs to, so the fragment
	 * writes its own braces and takes only the indenting from the writer.
	 */
	constructor() {
		super(null, null);
	}

	/** The shared instance, which is stateless and serves every call. */
	static get instance() {
		if (shared === null) { shared = new JavaScriptEmitter(); }

		return shared;
	}

	/** @param {import('./emitter.js').Context} context What the call works from. */
	emitFragment(context) {
		new Fragment(this, context).emit();
	}

	/** @inheritdoc */
	openFunction(writer, name, parameters) {
		writer.line(`function ${name}(${parameters}) {`);
		writer.indentBy();
	}

	/** @inheritdoc */
	closeFunction(writer) {
		writer.outdent();
		writer.line('}');
	}

	/** @inheritdoc */
	openDispatch(writer) {
		writer.line('switch (state) {');
		writer.indentBy();
	}

	/** @inheritdoc */
	closeDispatch(writer, result) {
		writer.outdent();
		writer.line('}');
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
		return `c === ${codeLiteral(code)}`;
	}

	/** @inheritdoc */
	withinRun(first, last) {
		return `c >= ${codeLiteral(first)} && c <= ${codeLiteral(last)}`;
	}
}

/** @type {JavaScriptEmitter | null} */
let shared = null;

/**
 * One emission call's state: the context, the generated names, and the choice between numbers and
 * BigInt. Per call so the shared instance stays stateless.
 */
class Fragment {
	/**
	 * @param {JavaScriptEmitter} emitter The emitter, for the shared skeletons.
	 * @param {import('./emitter.js').Context} context What the call works from.
	 */
	constructor(emitter, context) {
		this.emitter = emitter;
		this.context = context;

		/** Whether values are BigInt rather than number. */
		this.big = context.compilation.maxEncodedValue > MAX_SAFE_INTEGER;

		this.zero = this.big ? '0n' : '0';
		this.one = this.big ? '1n' : '1';

		// The generated names, each the prefix plus the bare member name, camel cased.
		const prefix = context.prefix;

		this.patternName = camel(prefix, 'Pattern');
		this.maxEncodedValueName = camel(prefix, 'MaxEncodedValue');
		this.maxLengthName = camel(prefix, 'MaxLength');
		this.acceptsName = camel(prefix, 'Accepts');
		this.encodeName = camel(prefix, 'Encode');
		this.decodeName = camel(prefix, 'Decode');
		this.decodeToBytesName = camel(prefix, 'DecodeToBytes');
		this.tryDecodeName = camel(prefix, 'TryDecode');
		this.getCanonicalFormName = camel(prefix, 'GetCanonicalForm');
		this.canonicaliseName = camel(prefix, 'Canonicalise');
		this.rankName = camel(prefix, 'Rank');
		this.decodeCoreName = camel(prefix, 'DecodeCore');
		this.acceptStepName = camel(prefix, 'AcceptStep');
		this.isAcceptingName = camel(prefix, 'IsAccepting');
		this.encodeStepName = camel(prefix, 'EncodeStep');
		this.isCanonicalAcceptingName = camel(prefix, 'IsCanonicalAccepting');
		this.decodeStepName = camel(prefix, 'DecodeStep');
		this.canonicalStepName = camel(prefix, 'CanonicalStep');
		this.finishCanonicalName = camel(prefix, 'FinishCanonical');
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
		this.emitAccepts();
		this.emitEncode();
		this.emitDecodePublics();
		this.emitGetCanonicalForm();
		this.emitSteppers();
	}

	emitConstants() {
		this.writer.line('/** The naxp this code was generated from. */');
		this.writer.line(`const ${this.patternName} = ${stringLiteral(this.context.compilation.pattern)};`);
		this.writer.line();
		this.writer.line('/** The largest encoded value this naxp produces, which is also how many it has. */');
		this.writer.line(`const ${this.maxEncodedValueName} = ${this.value(this.context.compilation.maxEncodedValue)};`);
		this.writer.line();
		this.writer.line('/** The length of the longest string this naxp can decode a value to. */');
		this.writer.line(`const ${this.maxLengthName} = ${this.maxLength};`);
		this.writer.line();
	}

	emitAccepts() {
		this.writer.line('/** Whether this naxp accepts a string, or the ASCII text in a Uint8Array. A byte outside ASCII is never accepted.');
		this.writer.line(' * @param {string | Uint8Array} text');
		this.writer.line(' * @returns {boolean}');
		this.writer.line(' */');
		this.writer.line(`function ${this.acceptsName}(text) {`);
		this.writer.indentBy();
		this.writer.line(`const ${BYTES_NAME} = typeof text !== 'string';`);
		this.writer.line('let state = 0;');
		this.writer.line();
		this.emitReadLoop(`state = ${this.acceptStepName}(state, c);`, 'return false;');
		this.writer.line();
		this.writer.line(`return ${this.isAcceptingName}(state);`);
		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();
	}

	emitEncode() {
		this.writer.line('/** The encoded value of a string, or of the ASCII text in a Uint8Array, from 1 to the largest encoded value, or zero where it is invalid.');
		this.writer.line(' * @param {string | Uint8Array} text');
		this.writer.line(` * @returns {${this.numberType}}`);
		this.writer.line(' */');
		this.writer.line(`function ${this.encodeName}(text) {`);
		this.writer.indentBy();

		if (this.canonicalises) {
			this.writer.line(`const canonical = ${this.canonicaliseName}(text);`);
			this.writer.line();
			this.writer.line(`return canonical === null ? ${this.zero} : ${this.rankName}(canonical);`);
		} else {
			this.writer.line(`const ${BYTES_NAME} = typeof text !== 'string';`);
			this.writer.line(`const acc = { total: ${this.zero} };`);
			this.writer.line('let state = 0;');
			this.writer.line();
			this.emitReadLoop(`state = ${this.encodeStepName}(state, c, acc);`, `return ${this.zero};`);
			this.writer.line();
			this.writer.line(`return ${this.isAcceptingName}(state) ? acc.total + ${this.one} : ${this.zero};`);
		}

		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();
	}

	/**
	 * The loop every entry point reads its input with. A byte is already the code point, and
	 * anything above ASCII fits no transition, so one stepper serves strings and bytes alike; the
	 * function has already set the flag saying which it holds.
	 *
	 * @param {string} step The stepping statement, over the code point `c`.
	 * @param {string} onFault What a failed step does.
	 * @param {string | null} before A statement between reading the code point and stepping, or
	 * null.
	 */
	emitReadLoop(step, onFault, before = null) {
		this.writer.line('for (let i = 0; i < text.length; i++) {');
		this.writer.indentBy();
		this.writer.line(`const c = ${BYTES_NAME} ? text[i] : text.charCodeAt(i);`);
		this.writer.line();

		if (before !== null) { this.writer.line(before); }

		this.writer.line(step);
		this.writer.line();
		this.writer.line(`if (state < 0) { ${onFault} }`);
		this.writer.outdent();
		this.writer.line('}');
	}

	emitDecodePublics() {
		const message = `'This naxp encodes the values 1 to ${this.context.compilation.maxEncodedValue}.'`;

		this.writer.line('/** The string a value stands for, which is in canonical form.');
		this.writer.line(` * @param {${this.numberType}} value`);
		this.writer.line(' * @returns {string}');
		this.writer.line(' * @throws {RangeError} The value is not one this naxp produces.');
		this.writer.line(' */');
		this.writer.line(`function ${this.decodeName}(value) {`);
		this.writer.indentBy();
		this.emitRangeCheck(message);
		this.writer.line(`return String.fromCharCode.apply(null, ${this.decodeCoreName}(value));`);
		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();

		this.writer.line('/** The string a value stands for, as ASCII bytes.');
		this.writer.line(` * @param {${this.numberType}} value`);
		this.writer.line(' * @returns {Uint8Array}');
		this.writer.line(' * @throws {RangeError} The value is not one this naxp produces.');
		this.writer.line(' */');
		this.writer.line(`function ${this.decodeToBytesName}(value) {`);
		this.writer.indentBy();
		this.emitRangeCheck(message);
		this.writer.line(`return Uint8Array.from(${this.decodeCoreName}(value));`);
		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();

		this.writer.line('/** The string a value stands for, which is in canonical form, or null where the value is not one this naxp produces.');
		this.writer.line(` * @param {${this.numberType}} value`);
		this.writer.line(' * @returns {string | null}');
		this.writer.line(' */');
		this.writer.line(`function ${this.tryDecodeName}(value) {`);
		this.writer.indentBy();
		this.writer.line(`if (value < ${this.one} || value > ${this.maxEncodedValueName}) { return null; }`);
		this.writer.line();
		this.writer.line(`return String.fromCharCode.apply(null, ${this.decodeCoreName}(value));`);
		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();
	}

	emitGetCanonicalForm() {
		this.writer.line('/** The canonical form of a string, or of the ASCII text in a Uint8Array, or null where it is invalid.');
		this.writer.line(' * @param {string | Uint8Array} text');
		this.writer.line(' * @returns {string | null}');
		this.writer.line(' */');
		this.writer.line(`function ${this.getCanonicalFormName}(text) {`);
		this.writer.indentBy();

		if (this.canonicalises) {
			this.writer.line(`const canonical = ${this.canonicaliseName}(text);`);
			this.writer.line();
			this.writer.line('return canonical === null ? null : String.fromCharCode.apply(null, canonical);');
		} else {
			// Where nothing is unified an accepted string is its own canonical form, so only bytes
			// need anything done to them.
			this.writer.line(`if (!${this.acceptsName}(text)) { return null; }`);
			this.writer.line();
			this.writer.line("return typeof text === 'string' ? text : String.fromCharCode.apply(null, text);");
		}

		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();
	}

	/** @param {string} message The message the range error carries. */
	emitRangeCheck(message) {
		this.writer.line(`if (value < ${this.one} || value > ${this.maxEncodedValueName}) {`);
		this.writer.indentBy();
		this.writer.line(`throw new RangeError(${message});`);
		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();
	}

	emitSteppers() {
		if (this.canonicalises) {
			this.writer.line('/** The code points of the canonical form of a string, or of the ASCII text in a Uint8Array, or null where it is invalid. */');
			this.writer.line(`function ${this.canonicaliseName}(text) {`);
			this.writer.indentBy();
			this.writer.line(`const ${BYTES_NAME} = typeof text !== 'string';`);
			this.writer.line('const canonical = [];');
			this.writer.line('let state = 0;');
			this.writer.line();

			if (this.needsRegister) {
				// The code points read, oldest first, so a reference of depth d is the one at
				// registerDepth - 1 - d. Shifting a buffer this small beats indexing a ring.
				this.writer.line(`const ${HELD_NAME} = new Array(${this.registerDepth}).fill(0);`);
				this.writer.line();
			}

			this.emitReadLoop(
				`state = ${this.canonicalStepName}(state, c, ${this.stepArguments()});`,
				'return null;',
				this.needsRegister ? `${HELD_NAME}.shift(); ${HELD_NAME}.push(c);` : null);
			this.writer.line();
			this.writer.line(`return ${this.finishCanonicalName}(state, ${this.finishArguments()}) ? canonical : null;`);
			this.writer.outdent();
			this.writer.line('}');
			this.writer.line();

			this.writer.line('/** The rank of a canonical string, as code points, within the canonical language, or zero where it is not in it. */');
			this.writer.line(`function ${this.rankName}(codes) {`);
			this.writer.indentBy();
			this.writer.line(`const acc = { total: ${this.zero} };`);
			this.writer.line('let state = 0;');
			this.writer.line();
			this.writer.line('for (let i = 0; i < codes.length; i++) {');
			this.writer.indentBy();
			this.writer.line(`state = ${this.encodeStepName}(state, codes[i], acc);`);
			this.writer.line();
			this.writer.line(`if (state < 0) { return ${this.zero}; }`);
			this.writer.outdent();
			this.writer.line('}');
			this.writer.line();
			this.writer.line(`return ${this.isCanonicalAcceptingName}(state) ? acc.total + ${this.one} : ${this.zero};`);
			this.writer.outdent();
			this.writer.line('}');
			this.writer.line();
		}

		this.writer.line('/** The code points of an encoded value already checked against the largest one. */');
		this.writer.line(`function ${this.decodeCoreName}(value) {`);
		this.writer.indentBy();
		this.writer.line('const codes = [];');
		this.writer.line('const box = { remaining: value };');
		this.writer.line('let state = 0;');
		this.writer.line();
		this.writer.line('while (state >= 0) {');
		this.writer.indentBy();
		this.writer.line(`state = ${this.decodeStepName}(state, box, codes);`);
		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();
		this.writer.line('return codes;');
		this.writer.outdent();
		this.writer.line('}');
		this.writer.line();

		this.writer.line("/** The acceptor's transition: the next state, or -1 where the code point fits nothing. */");
		this.emitter.emitStepFunctions(
			this.writer,
			this.acceptStepName,
			'state, c',
			'state, c',
			this.acceptedStates.length,
			id => this.emitAcceptCase(id));
		this.writer.line();

		this.emitAcceptingPredicate(this.isAcceptingName, this.acceptedStates);
		this.writer.line();

		this.writer.line("/** The canonical machine's transition, accumulating the values skipped: the next state, or -1. */");
		this.emitter.emitStepFunctions(
			this.writer,
			this.encodeStepName,
			'state, c, acc',
			'state, c, acc',
			this.canonicalStates.length,
			id => this.emitEncodeCase(id));
		this.writer.line();

		if (this.canonicalises) {
			this.emitAcceptingPredicate(this.isCanonicalAcceptingName, this.canonicalStates);
			this.writer.line();
		}

		this.writer.line('/** One step of decoding: appends at most one code point and returns the next state, or -1 when the string is complete. */');
		this.emitter.emitStepFunctions(
			this.writer,
			this.decodeStepName,
			'state, box, codes',
			'state, box, codes',
			this.canonicalStates.length,
			id => this.emitDecodeCase(id),
			'let index;',
			id => Emitter.needsIndex(this.canonicalStates[id]));

		if (this.canonicalises) {
			this.writer.line();
			this.writer.line('/** The canonicalising transition, appending what reading the code point emits: the next state, or -1. */');
			this.emitter.emitStepFunctions(
				this.writer,
				this.canonicalStepName,
				`state, c, ${this.stepArguments()}`,
				`state, c, ${this.stepArguments()}`,
				this.transducerStates.length,
				id => this.emitCanonicalCase(id));
			this.writer.line();

			this.writer.line('/** Appends what ending the input emits, and returns whether the input may end here. */');
			this.emitter.emitStepFunctions(
				this.writer,
				this.finishCanonicalName,
				`state, ${this.finishArguments()}`,
				`state, ${this.finishArguments()}`,
				this.transducerStates.length,
				id => this.emitFinishCase(id),
				null,
				null,
				'false',
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
				// transition, and this transition's earlier runs. Within the run the code point's
				// rank folds into (c - first).
				const skipped = arc.skippedBefore + (arc.nextCount * offset);
				const added = this.addedExpression(skipped, arc.nextCount, run.first, run.last);
				const condition = this.emitter.runCondition(run.first, run.last);

				this.writer.line(added === null
					? `if (${condition}) { return ${arc.next}; }`
					: `if (${condition}) { acc.total += ${added}; return ${arc.next}; }`);

				offset += BigInt(run.last - run.first + 1);
			}
		}

		this.writer.line('break;');
		this.writer.outdent();
	}

	/** @param {number} id The state. */
	emitDecodeCase(id) {
		const state = this.canonicalStates[id];

		this.writer.line(`case ${id}: {`);
		this.writer.indentBy();

		if (state.arcs.length === 0) {
			// The terminal state. The remaining value is one here, because the caller checked the
			// value against the count of the start state and every step keeps it within the count
			// of the state it moves to.
			this.writer.line('return -1;');
			this.writer.outdent();
			this.writer.line('}');

			return;
		}

		if (state.acceptsEnd) {
			this.writer.line(`if (box.remaining === ${this.one}) { return -1; }`);
			this.writer.line();
			this.writer.line(`box.remaining -= ${this.one};`);
		}

		for (let i = 0; i < state.arcs.length; ++i) {
			const arc = state.arcs[i];
			const block = arc.nextCount * BigInt(arc.set.count);

			if (i < state.arcs.length - 1) {
				if (i > 0 || state.acceptsEnd) { this.writer.line(); }

				this.writer.line(`if (box.remaining <= ${this.value(block)}) {`);
				this.writer.indentBy();
				this.emitDecodeArc(arc);
				this.writer.outdent();
				this.writer.line('}');
				this.writer.line();
				this.writer.line(`box.remaining -= ${this.value(block)};`);
			} else {
				// The last transition takes whatever is left, by the same invariant as the
				// terminal state above.
				if (i > 0 || state.acceptsEnd) { this.writer.line(); }

				this.emitDecodeArc(arc);
			}
		}

		this.writer.outdent();
		this.writer.line('}');
	}

	/** @param {import('./emitter.js').ArcModel} arc The transition. */
	emitDecodeArc(arc) {
		const runs = Emitter.getRuns(arc.set);

		if (arc.set.count === 1) {
			// One code point leaves the remaining value untouched: its rank is zero and the whole
			// block belongs to the next state.
			this.writer.line(`codes.push(${codeLiteral(runs[0].first)});`);
			this.writer.line(`return ${arc.next};`);

			return;
		}

		// index is declared once at the top of the function, because these arcs sit at differing
		// brace depths within one switch and sibling declarations there collide.
		if (arc.nextCount === 1n) {
			this.writer.line(`index = box.remaining - ${this.one};`);
		} else if (this.big) {
			this.writer.line(`index = (box.remaining - 1n) / ${this.value(arc.nextCount)};`);
		} else {
			this.writer.line(`index = Math.floor((box.remaining - 1) / ${this.value(arc.nextCount)});`);
		}

		this.writer.line(`codes.push(${this.characterExpression(runs)});`);
		this.writer.line(arc.nextCount === 1n
			? `box.remaining = ${this.one};`
			: `box.remaining = ((box.remaining - ${this.one}) % ${this.value(arc.nextCount)}) + ${this.one};`);
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

			if (arc.output.length === 0) {
				this.writer.line(`if (${condition}) { return ${arc.next}; }`);
			} else if (arc.output.length === 1) {
				this.writer.line(`if (${condition}) { canonical.push(${outputs[0]}); return ${arc.next}; }`);
			} else {
				this.writer.line(`if (${condition}) {`);
				this.writer.indentBy();

				for (const expression of outputs) {
					this.writer.line(`canonical.push(${expression});`);
				}

				this.writer.line(`return ${arc.next};`);
				this.writer.outdent();
				this.writer.line('}');
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
			this.writer.line(`canonical.push(${expression});`);
		}

		this.writer.line('return true;');
		this.writer.outdent();
	}

	/** The arguments the canonicalising step takes after its code point. */
	stepArguments() {
		return this.needsRegister ? `${HELD_NAME}, canonical` : 'canonical';
	}

	/** The arguments the finishing step takes after its state. */
	finishArguments() {
		return this.needsRegister ? `${HELD_NAME}, canonical` : 'canonical';
	}

	/**
	 * One expression per code point an output emits, with each reference resolved against the code
	 * points kept.
	 *
	 * @param {string} output The output, over literals and references.
	 * @param {boolean} forFinish Whether this is an end output, which has no code point in hand.
	 * @returns {string[]} The expressions.
	 */
	outputExpressions(output, forFinish) {
		const expressions = [];

		for (let i = 0; i < output.length; ++i) {
			if (output[i] !== COPY_MARKER) {
				expressions.push(codeLiteral(output.charCodeAt(i)));
				continue;
			}

			const depth = output.charCodeAt(i + 1) - DEPTH_BASE;

			++i;

			// A step already holds the code point it is reading, so depth zero needs no buffer
			// there. The finish function has none, so it reads even that one back.
			if (depth === 0 && !forFinish) {
				expressions.push('c');
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
		this.writer.line('/** Whether the input may end in this state. */');
		this.writer.line(`function ${name}(state) {`);
		this.writer.indentBy();
		this.writer.line('switch (state) {');
		this.writer.indentBy();

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
		this.writer.outdent();
		this.writer.line('}');
		this.writer.outdent();
		this.writer.line('}');
	}

	/** How a JSDoc comment names the value type. */
	get numberType() { return this.big ? 'bigint' : 'number'; }

	/**
	 * A value literal, BigInt or number as the naxp's size decided.
	 *
	 * @param {bigint} value The value.
	 * @returns {string} The literal.
	 */
	value(value) {
		return this.emitter.grouped(value) + (this.big ? 'n' : '');
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
		if (first === last) { return skipped === 0n ? null : this.value(skipped); }

		// The code point's rank is a number either way, so BigInt arithmetic converts it.
		const index = this.big ? `BigInt(c - ${codeLiteral(first)})` : `(c - ${codeLiteral(first)})`;
		const term = count === 1n ? index : `${this.value(count)} * ${index}`;

		return skipped === 0n ? term : `${this.value(skipped)} + ${term}`;
	}

	/**
	 * The code point at position `index` within a set, as an expression over its runs.
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
			text += `index < ${this.value(cumulative)} ? ${this.runCharacter(runs[i], offset)} : `;
		}

		return text + this.runCharacter(runs[runs.length - 1], cumulative);
	}

	/**
	 * @param {{first: number, last: number}} run The run.
	 * @param {bigint} offset Where the run starts within the set.
	 * @returns {string} The code point at `index`, given that it lies in this run.
	 */
	runCharacter(run, offset) {
		if (run.first === run.last) { return codeLiteral(run.first); }

		// A code point is a number, so the BigInt index converts back on the way out.
		const index = this.big
			? offset === 0n ? 'Number(index)' : `Number(index - ${this.value(offset)})`
			: offset === 0n ? 'index' : `(index - ${this.value(offset)})`;

		return `${codeLiteral(run.first)} + ${index}`;
	}
}

/**
 * An ASCII code point, in hexadecimal. Bare, with no comment naming the character: the comparisons
 * sit two or three to a line, and the annotations cost more in noise than they return in clarity
 * when `charCodeAt` is in plain view above.
 *
 * @param {number} code The code point.
 * @returns {string} The literal.
 */
function codeLiteral(code) {
	return '0x' + code.toString(16).toUpperCase().padStart(2, '0');
}

/**
 * A string as a single-quoted JavaScript literal. A pattern may hold whitespace other than the
 * space, which is escaped along with the quote and the backslash.
 *
 * @param {string} text The string, which is ASCII.
 * @returns {string} The literal.
 */
function stringLiteral(text) {
	let literal = "'";

	for (let i = 0; i < text.length; ++i) {
		const code = text.charCodeAt(i);

		if (code === 0x27 || code === 0x5c) {
			literal += '\\' + text[i];
		} else if (code === 0x09) {
			literal += '\\t';
		} else if (code === 0x0a) {
			literal += '\\n';
		} else if (code === 0x0d) {
			literal += '\\r';
		} else if (code < 0x20 || code > 0x7e) {
			literal += '\\x' + code.toString(16).toUpperCase().padStart(2, '0');
		} else {
			literal += text[i];
		}
	}

	return literal + "'";
}

/**
 * @param {string} prefix The caller's prefix, possibly empty.
 * @param {string} member The bare member name.
 * @returns {string} The two, camel cased as JavaScript writes function names.
 */
function camel(prefix, member) {
	const name = prefix + member;

	return name[0].toLowerCase() + name.slice(1);
}
