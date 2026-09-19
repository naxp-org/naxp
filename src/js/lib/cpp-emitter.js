// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { CFamilyEmitter, HELD_NAME } from './c-family-emitter.js';

/**
 * Emits a compiled naxp as a C++ fragment, in C++17.
 *
 * The fragment is two constants, four public functions and their steppers, every name in
 * snake_case under the caller's prefix, and its surface is the library's own `logmu::naxp`: a
 * `string_view` in, a `std::string` out, `decode` throwing `std::out_of_range` and `try_decode`
 * reporting instead. It needs `cstdint`, `stdexcept`, `string` and `string_view`, which the
 * caller includes, and its first line says so.
 *
 * Every function is `inline` and both constants `inline constexpr`, so the fragment can sit in a
 * header that several translation units include. That is also why the caller gives each naxp its
 * own prefix where several share a program: two inline functions of one name and different bodies
 * in different translation units break the one definition rule, and no compiler is obliged to
 * notice.
 */
export class CppEmitter extends CFamilyEmitter {
	/** The shared instance, which is stateless and serves every call. */
	static get instance() {
		if (shared === null) { shared = new CppEmitter(); }

		return shared;
	}

	/* ---------- the spelling of C++ ---------- */

	/** @inheritdoc */
	get stepLinkage() { return 'inline'; }

	/** @inheritdoc */
	get typePrefix() { return 'std::'; }

	/** C++14's digit separator. */
	get digitSeparator() { return "'"; }

	/** @inheritdoc */
	pointer(type, name) {
		return `${type}* ${name}`;
	}

	/** @inheritdoc */
	byReference(type, name) {
		return `${type}& ${name}`;
	}

	/** @inheritdoc */
	dereference(name) {
		return name;
	}

	/** @inheritdoc */
	increment(name) {
		return `${name}++`;
	}

	/** @inheritdoc */
	addressOf(name) {
		return name;
	}

	/** @inheritdoc */
	cast(type, expression) {
		return `static_cast<${type}>(${expression})`;
	}

	/** @inheritdoc */
	comment(writer, text) {
		writer.line(`/// ${text}`);
	}

	/* ---------- the header ---------- */

	/** @inheritdoc */
	emitHeader(fragment) {
		const writer = fragment.writer;

		writer.line('// Needs <cstdint>, <stdexcept>, <string> and <string_view>. Everything is inline, so the');
		writer.line('// fragment can sit in a header; give each naxp its own prefix where several share a program.');
		writer.line();
		this.comment(writer, 'The largest encoded value this naxp produces, which is also how many it has.');
		writer.line(`inline constexpr ${fragment.valueKeyword} ${fragment.maxEncodedValueName} = ${fragment.maxEncodedValueLiteral};`);
		writer.line();
		this.comment(writer, 'The length of the longest string this naxp can decode a value to.');
		writer.line(`inline constexpr int ${fragment.maxLengthName} = ${fragment.maxLength};`);
		writer.line();
	}

	/* ---------- the public functions ---------- */

	/** @inheritdoc */
	emitPublics(fragment) {
		this.emitAccepts(fragment);
		this.emitEncode(fragment);
		this.emitDecode(fragment);
	}

	/** @param {import('./c-family-emitter.js').Fragment} fragment The call's state. */
	emitAccepts(fragment) {
		const writer = fragment.writer;

		writer.line('/// Whether this naxp accepts text. A byte outside ASCII is never accepted.');
		writer.line('///');
		writer.line('/// @param text The text to test.');
		writer.line('/// @returns Whether the naxp accepts it.');
		writer.line(`inline bool ${fragment.acceptsName}(std::string_view text)`);
		writer.openBlock();
		writer.line('int state = 0;');
		writer.line();
		writer.line('for (char c : text)');
		writer.openBlock();
		writer.line(`state = ${fragment.acceptStepName}(state, static_cast<unsigned char>(c));`);
		writer.line();
		writer.line('if (state < 0) { return false; }');
		writer.closeBlock();
		writer.line();
		writer.line(`return ${fragment.isAcceptingName}(state);`);
		writer.closeBlock();
		writer.line();
	}

	/** @param {import('./c-family-emitter.js').Fragment} fragment The call's state. */
	emitEncode(fragment) {
		const writer = fragment.writer;

		writer.line('/// The encoded value of text.');
		writer.line('///');
		writer.line('/// @param text The text to encode.');
		writer.line(`/// @returns The encoded value, from 1 to ${fragment.maxEncodedValueName}, or zero where the text is invalid.`);
		writer.line(`inline ${fragment.valueKeyword} ${fragment.encodeName}(std::string_view text)`);
		writer.openBlock();

		if (fragment.canonicalises) {
			writer.line(`char canonical[${fragment.bufferSize}] = {};`);

			if (fragment.needsRegister) {
				// The characters read, oldest first, so a reference of depth d is the one at
				// registerDepth - 1 - d. Shifting a buffer this small beats indexing a ring, and
				// it starts zeroed so that an early shift reads nothing undefined.
				writer.line(`char ${HELD_NAME}[${fragment.registerDepth}] = {};`);
			}

			writer.line('int length = 0;');
			writer.line('int state = 0;');
			writer.line();
			writer.line('for (char c : text)');
			writer.openBlock();

			if (fragment.needsRegister) {
				const top = fragment.registerDepth - 1;

				if (fragment.registerDepth > 1) {
					writer.line(`for (int h = 0; h < ${top}; ++h) { ${HELD_NAME}[h] = ${HELD_NAME}[h + 1]; }`);
				}

				writer.line(`${HELD_NAME}[${top}] = c;`);
			}

			writer.line(`state = ${fragment.canonicalStepName}(state, static_cast<unsigned char>(c), ${fragment.stepArguments('length')});`);
			writer.line();
			writer.line(`if (state < 0) { return ${fragment.valueZero}; }`);
			writer.closeBlock();
			writer.line();
			writer.line(`length = ${fragment.finishCanonicalName}(state, ${fragment.finishArguments()});`);
			writer.line();
			writer.line(fragment.valueIsWidest
				? `return length < 0 ? 0ULL : ${fragment.rankName}(canonical, length);`
				: `return length < 0 ? static_cast<${fragment.valueKeyword}>(0) : static_cast<${fragment.valueKeyword}>(${fragment.rankName}(canonical, length));`);
		} else {
			writer.line('int state = 0;');
			writer.line('std::uint64_t total = 0ULL;');
			writer.line();
			writer.line('for (char c : text)');
			writer.openBlock();
			writer.line(`state = ${fragment.encodeStepName}(state, static_cast<unsigned char>(c), total);`);
			writer.line();
			writer.line(`if (state < 0) { return ${fragment.valueZero}; }`);
			writer.closeBlock();
			writer.line();
			writer.line(fragment.valueIsWidest
				? `return ${fragment.isAcceptingName}(state) ? total + 1ULL : 0ULL;`
				: `return ${fragment.isAcceptingName}(state) ? static_cast<${fragment.valueKeyword}>(total + 1ULL) : static_cast<${fragment.valueKeyword}>(0);`);
		}

		writer.closeBlock();
		writer.line();
	}

	/** @param {import('./c-family-emitter.js').Fragment} fragment The call's state. */
	emitDecode(fragment) {
		const writer = fragment.writer;
		const tryDecodeName = fragment.name('TryDecode');

		writer.line('/// The string a value stands for.');
		writer.line('///');
		writer.line(`/// @param value The encoded value, from 1 to ${fragment.maxEncodedValueName}.`);
		writer.line('/// @returns The string, which is in canonical form.');
		writer.line('/// @throws std::out_of_range The value is not one this naxp produces.');
		writer.line(`inline std::string ${fragment.decodeName}(${fragment.valueKeyword} value)`);
		writer.openBlock();
		writer.line(`if (value < ${fragment.valueOne} || value > ${fragment.maxEncodedValueName})`);
		writer.openBlock();
		writer.line(`throw std::out_of_range("This naxp encodes the values 1 to ${fragment.maxEncodedValueDigits}.");`);
		writer.closeBlock();
		writer.line();
		writer.line(`char buffer[${fragment.bufferSize}] = {};`);
		writer.line(`int length = ${fragment.decodeCoreName}(${fragment.decodeCoreArgument}, buffer);`);
		writer.line();
		writer.line('return std::string(buffer, buffer + length);');
		writer.closeBlock();
		writer.line();

		writer.line('/// Tries to find the string a value stands for.');
		writer.line('///');
		writer.line('/// @param value The encoded value.');
		writer.line('/// @param text Where the string goes, which is in canonical form. Untouched where the value is not one this naxp produces.');
		writer.line('/// @returns Whether the value is one this naxp produces.');
		writer.line(`inline bool ${tryDecodeName}(${fragment.valueKeyword} value, std::string& text)`);
		writer.openBlock();
		writer.line(`if (value < ${fragment.valueOne} || value > ${fragment.maxEncodedValueName}) { return false; }`);
		writer.line();
		writer.line(`char buffer[${fragment.bufferSize}] = {};`);
		writer.line(`int length = ${fragment.decodeCoreName}(${fragment.decodeCoreArgument}, buffer);`);
		writer.line();
		writer.line('text.assign(buffer, buffer + length);');
		writer.line();
		writer.line('return true;');
		writer.closeBlock();
		writer.line();
	}
}

/** @type {CppEmitter | null} */
let shared = null;
