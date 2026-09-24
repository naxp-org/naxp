// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { CFamilyEmitter, isIdentifier } from './c-family-emitter.js';

/**
 * Emits a compiled naxp as a C fragment, in C99.
 *
 * The fragment is two constants, nine public functions and their steppers, every name in
 * snake_case under the caller's prefix. It needs `stdbool.h`, `stddef.h`, `stdint.h` and
 * `string.h`, which the caller includes, and its first line says so. Everything has internal
 * linkage, the public functions `static inline` and the steppers `static`, so the fragment can
 * sit in a header or a source file alike and two translation units holding it never collide.
 *
 * Text comes in two ways, as a pointer and a length and as a NUL-terminated string, each public
 * function having a `_cstr` twin for the second. Decoding writes into the caller's buffer and
 * reports its length rather than returning anything, which is the only shape C has, and mirrors
 * the library's own C façade. The largest encoded value is a `static const` rather than a macro,
 * which stays out of the caller's namespace; the longest length is an enum constant, because it
 * sizes arrays and a `static const` cannot.
 */
export class CEmitter extends CFamilyEmitter {
	/** The shared instance, which is stateless and serves every call. */
	static get instance() {
		if (shared === null) { shared = new CEmitter(); }

		return shared;
	}

	/* ---------- the spelling of C ---------- */

	/** @inheritdoc */
	get stepLinkage() { return 'static'; }

	/** @inheritdoc */
	get typePrefix() { return ''; }

	/** C never adopted a digit separator. */
	get digitSeparator() { return null; }

	/** @inheritdoc */
	get textParameters() { return 'const char *text, size_t text_length'; }

	/** @inheritdoc */
	pointer(type, name) {
		return `${type} *${name}`;
	}

	/** @inheritdoc */
	byReference(type, name) {
		return `${type} *${name}`;
	}

	/** @inheritdoc */
	dereference(name) {
		return '*' + name;
	}

	/** @inheritdoc */
	increment(name) {
		return `(*${name})++`;
	}

	/** @inheritdoc */
	addressOf(name) {
		return '&' + name;
	}

	/** @inheritdoc */
	cast(type, expression) {
		return isIdentifier(expression) ? `(${type})${expression}` : `(${type})(${expression})`;
	}

	/** @inheritdoc */
	comment(writer, text) {
		writer.line(`/* ${text} */`);
	}

	/* ---------- the header ---------- */

	/** @inheritdoc */
	emitHeader(fragment) {
		const writer = fragment.writer;

		this.comment(writer, 'Needs <stdbool.h>, <stddef.h>, <stdint.h> and <string.h>.');
		writer.line();
		this.comment(writer, 'The largest encoded value this naxp produces, which is also how many it has.');
		writer.line(`static const ${fragment.valueKeyword} ${fragment.maxEncodedValueName} = ${fragment.maxEncodedValueLiteral};`);
		writer.line();
		this.comment(writer, 'The length of the longest string this naxp can decode a value to.');
		writer.line(`enum { ${fragment.maxLengthName} = ${fragment.maxLength} };`);
		writer.line();
	}

	/* ---------- the public functions ---------- */

	/** @inheritdoc */
	emitPublics(fragment) {
		this.emitPattern(fragment);
		this.emitAccepts(fragment);
		this.emitEncode(fragment);
		this.emitDecode(fragment);
		this.emitCanonicalForm(fragment);
	}

	/** @param {import('./c-family-emitter.js').Fragment} fragment The call's state. */
	emitPattern(fragment) {
		const writer = fragment.writer;
		const pattern = fragment.context.compilation.pattern;

		// A function rather than a constant, as the library's is, because an unused static array is
		// a warning in C where an unused static inline function is not.
		this.comment(writer, 'The naxp this code was generated from, NUL-terminated.');
		writer.line(`static inline const char *${fragment.patternName}(void)`);
		writer.openBlock();
		writer.line(`return ${stringLiteral(pattern)};`);
		writer.closeBlock();
		writer.line();
	}

	/** @param {import('./c-family-emitter.js').Fragment} fragment The call's state. */
	emitAccepts(fragment) {
		const writer = fragment.writer;
		const cstrName = fragment.name('AcceptsCstr');

		this.comment(writer, 'Whether this naxp accepts text. A byte outside ASCII is never accepted.');
		writer.line(`static inline bool ${fragment.acceptsName}(const char *text, size_t text_length)`);
		writer.openBlock();
		writer.line('int state = 0;');
		writer.line();
		writer.line('for (size_t i = 0; i < text_length; ++i)');
		writer.openBlock();
		writer.line(`state = ${fragment.acceptStepName}(state, (unsigned char)text[i]);`);
		writer.line();
		writer.line('if (state < 0) { return false; }');
		writer.closeBlock();
		writer.line();
		writer.line(`return ${fragment.isAcceptingName}(state);`);
		writer.closeBlock();
		writer.line();

		this.comment(writer, 'Whether this naxp accepts a NUL-terminated string.');
		writer.line(`static inline bool ${cstrName}(const char *text)`);
		writer.openBlock();
		writer.line(`return ${fragment.acceptsName}(text, strlen(text));`);
		writer.closeBlock();
		writer.line();
	}

	/** @param {import('./c-family-emitter.js').Fragment} fragment The call's state. */
	emitEncode(fragment) {
		const writer = fragment.writer;
		const cstrName = fragment.name('EncodeCstr');

		this.comment(writer, `The encoded value of text: from 1 to ${fragment.maxEncodedValueName}, or zero where the text is invalid.`);
		writer.line(`static inline ${fragment.valueKeyword} ${fragment.encodeName}(const char *text, size_t text_length)`);
		writer.openBlock();

		if (fragment.canonicalises) {
			writer.line(`char canonical[${fragment.bufferSize}] = { 0 };`);
			writer.line(`int length = ${fragment.canonicaliseName}(text, text_length, canonical);`);
			writer.line();
			writer.line(fragment.valueIsWidest
				? `return length < 0 ? 0ULL : ${fragment.rankName}(canonical, length);`
				: `return length < 0 ? (${fragment.valueKeyword})0 : (${fragment.valueKeyword})${fragment.rankName}(canonical, length);`);
		} else {
			writer.line('int state = 0;');
			writer.line('uint64_t total = 0ULL;');
			writer.line();
			writer.line('for (size_t i = 0; i < text_length; ++i)');
			writer.openBlock();
			writer.line(`state = ${fragment.encodeStepName}(state, (unsigned char)text[i], &total);`);
			writer.line();
			writer.line(`if (state < 0) { return ${fragment.valueZero}; }`);
			writer.closeBlock();
			writer.line();
			writer.line(fragment.valueIsWidest
				? `return ${fragment.isAcceptingName}(state) ? total + 1ULL : 0ULL;`
				: `return ${fragment.isAcceptingName}(state) ? (${fragment.valueKeyword})(total + 1ULL) : (${fragment.valueKeyword})0;`);
		}

		writer.closeBlock();
		writer.line();

		this.comment(writer, `The encoded value of a NUL-terminated string: from 1 to ${fragment.maxEncodedValueName}, or zero where the string is invalid.`);
		writer.line(`static inline ${fragment.valueKeyword} ${cstrName}(const char *text)`);
		writer.openBlock();
		writer.line(`return ${fragment.encodeName}(text, strlen(text));`);
		writer.closeBlock();
		writer.line();
	}

	/** @param {import('./c-family-emitter.js').Fragment} fragment The call's state. */
	emitDecode(fragment) {
		const writer = fragment.writer;
		const cstrName = fragment.name('DecodeCstr');

		writer.line('/*');
		writer.line('   Writes the string a value stands for, which is in canonical form, with no terminator.');
		writer.line(`   ${fragment.maxLengthName} bytes always suffice. length, which may be NULL, receives how many`);
		writer.line('   were written, or zero where none were.');
		writer.line();
		writer.line('   Returns false where the value is not one this naxp produces, or the destination is too');
		writer.line('   short, in which case nothing is written.');
		writer.line('*/');
		writer.line(`static inline bool ${fragment.decodeName}(${fragment.valueKeyword} value, char *destination, size_t capacity, size_t *length)`);
		writer.openBlock();
		writer.line(`char buffer[${fragment.bufferSize}] = { 0 };`);
		writer.line('int written;');
		writer.line();
		writer.line(`if (value < ${fragment.valueOne} || value > ${fragment.maxEncodedValueName})`);
		writer.openBlock();
		writer.line('if (length != NULL) { *length = 0; }');
		writer.line();
		writer.line('return false;');
		writer.closeBlock();
		writer.line();
		writer.line(`written = ${fragment.decodeCoreName}(${fragment.decodeCoreArgument}, buffer);`);
		writer.line();
		writer.line('if ((size_t)written > capacity)');
		writer.openBlock();
		writer.line('if (length != NULL) { *length = 0; }');
		writer.line();
		writer.line('return false;');
		writer.closeBlock();
		writer.line();
		writer.line('memcpy(destination, buffer, (size_t)written);');
		writer.line();
		writer.line('if (length != NULL) { *length = (size_t)written; }');
		writer.line();
		writer.line('return true;');
		writer.closeBlock();
		writer.line();

		writer.line('/*');
		writer.line('   Writes the string a value stands for, which is in canonical form, as a NUL-terminated');
		writer.line(`   string. ${fragment.maxLengthName} + 1 bytes always suffice.`);
		writer.line();
		writer.line('   Returns false where the value is not one this naxp produces, or the destination is too');
		writer.line('   short, in which case nothing is written.');
		writer.line('*/');
		writer.line(`static inline bool ${cstrName}(${fragment.valueKeyword} value, char *destination, size_t capacity)`);
		writer.openBlock();
		writer.line('size_t length;');
		writer.line();
		writer.line(`if (capacity == 0 || !${fragment.decodeName}(value, destination, capacity - 1, &length)) { return false; }`);
		writer.line();
		writer.line("destination[length] = '\\0';");
		writer.line();
		writer.line('return true;');
		writer.closeBlock();
		writer.line();
	}

	/** @param {import('./c-family-emitter.js').Fragment} fragment The call's state. */
	emitCanonicalForm(fragment) {
		const writer = fragment.writer;
		const cstrName = fragment.name('CanonicalFormCstr');

		writer.line('/*');
		writer.line('   Writes the canonical form of text, which is the text decoding its encoded value gives back,');
		writer.line(`   with no terminator. ${fragment.maxLengthName} bytes always suffice. length, which may be NULL,`);
		writer.line('   receives how many were written, or zero where none were.');
		writer.line();
		writer.line('   Returns false where the text is invalid, or the destination is too short, in which case');
		writer.line('   nothing is written.');
		writer.line('*/');
		writer.line(`static inline bool ${fragment.canonicalFormName}(const char *text, size_t text_length, char *destination, size_t capacity, size_t *length)`);
		writer.openBlock();
		writer.line(`char buffer[${fragment.bufferSize}] = { 0 };`);
		writer.line(`int written = ${fragment.canonicaliseName}(text, text_length, buffer);`);
		writer.line();
		writer.line('if (written < 0 || (size_t)written > capacity)');
		writer.openBlock();
		writer.line('if (length != NULL) { *length = 0; }');
		writer.line();
		writer.line('return false;');
		writer.closeBlock();
		writer.line();
		writer.line('memcpy(destination, buffer, (size_t)written);');
		writer.line();
		writer.line('if (length != NULL) { *length = (size_t)written; }');
		writer.line();
		writer.line('return true;');
		writer.closeBlock();
		writer.line();

		writer.line('/*');
		writer.line('   Writes the canonical form of a NUL-terminated string as a NUL-terminated string.');
		writer.line(`   ${fragment.maxLengthName} + 1 bytes always suffice.`);
		writer.line();
		writer.line('   Returns false where the string is invalid, or the destination is too short, in which case');
		writer.line('   nothing is written.');
		writer.line('*/');
		writer.line(`static inline bool ${cstrName}(const char *text, char *destination, size_t capacity)`);
		writer.openBlock();
		writer.line('size_t length;');
		writer.line();
		writer.line(`if (capacity == 0 || !${fragment.canonicalFormName}(text, strlen(text), destination, capacity - 1, &length)) { return false; }`);
		writer.line();
		writer.line("destination[length] = '\\0';");
		writer.line();
		writer.line('return true;');
		writer.closeBlock();
		writer.line();
	}

	/** @inheritdoc */
	emitCanonicalise(fragment) {
		const writer = fragment.writer;

		fragment.openCanonicalise();

		if (!fragment.canonicalises) {
			// Where nothing is unified an accepted string is its own canonical form, and being
			// accepted it fits the buffer.
			writer.line(`if (!${fragment.acceptsName}(text, text_length)) { return -1; }`);
			writer.line();
			writer.line('memcpy(canonical, text, text_length);');
			writer.line();
			writer.line('return (int)text_length;');
			writer.closeBlock();
			writer.line();

			return;
		}

		fragment.declareRegister('{ 0 }');
		writer.line('int length = 0;');
		writer.line('int state = 0;');
		writer.line();
		writer.line('for (size_t i = 0; i < text_length; ++i)');
		writer.openBlock();
		fragment.keepCharacter('text[i]');
		writer.line(`state = ${fragment.canonicalStepName}(state, (unsigned char)text[i], ${fragment.stepArguments('&length')});`);
		writer.line();
		writer.line('if (state < 0) { return -1; }');
		writer.closeBlock();
		writer.line();
		writer.line(`return ${fragment.finishCanonicalName}(state, ${fragment.finishArguments()});`);
		writer.closeBlock();
		writer.line();
	}
}

/**
 * A string as a C literal. A pattern may hold whitespace other than the space, which is escaped
 * along with the quote and the backslash, and a question mark after another is escaped too, so
 * that no pair of them starts a trigraph where a compiler still reads trigraphs.
 *
 * @param {string} text The string, which is ASCII.
 * @returns {string} The literal.
 */
export function stringLiteral(text) {
	let literal = '"';

	for (let i = 0; i < text.length; ++i) {
		const code = text.charCodeAt(i);

		if (code === 0x22 || code === 0x5c) {
			literal += '\\' + text[i];
		} else if (code === 0x3f && i > 0 && text[i - 1] === '?') {
			literal += '\\?';
		} else if (code === 0x09) {
			literal += '\\t';
		} else if (code === 0x0a) {
			literal += '\\n';
		} else if (code === 0x0d) {
			literal += '\\r';
		} else if (code < 0x20 || code > 0x7e) {
			// Three octal digits, because a hexadecimal escape runs on into any hexadecimal digit
			// that follows it.
			literal += '\\' + code.toString(8).padStart(3, '0');
		} else {
			literal += text[i];
		}
	}

	return literal + '"';
}

/** @type {CEmitter | null} */
let shared = null;
