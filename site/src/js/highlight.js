// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * Syntax colouring for code on the site, shared by the code generator, which colours its
 * fragment in the browser, and by the build, which colours the examples on the API page so that
 * they are coloured from the first paint and without a script.
 *
 * Nothing here touches the page, which is what lets the build run it.
 */

/**
 * How each language spells its keywords, and how its comments, strings and numbers are written.
 *
 * A generated fragment is a small, regular subset of its language, written by one emitter that
 * never nests a comment or breaks a string over a line, so telling three kinds of token apart
 * needs no parser. Anything this does not recognise stays plain, which is the right failure.
 *
 * The keywords are each language's reserved words rather than the handful an emitter happens to
 * write today, so a new construct in a fragment cannot quietly lose its colour. A reserved word
 * can never be an identifier, so nothing here can colour a name by mistake.
 */
const GRAMMARS = {
	CSharp: {
		comment: String.raw`\/\/.*`,
		literal: String.raw`"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\b0[xX][0-9A-Fa-f]+\b|\b\d[\d_]*(?:UL|U|L)?\b`,
		keywords: new Set([
			'abstract', 'as', 'base', 'bool', 'break', 'byte', 'case', 'catch', 'char', 'checked',
			'class', 'const', 'continue', 'decimal', 'default', 'delegate', 'do', 'double', 'else',
			'enum', 'event', 'explicit', 'extern', 'false', 'finally', 'fixed', 'float', 'for',
			'foreach', 'goto', 'if', 'implicit', 'in', 'int', 'interface', 'internal', 'is',
			'lock', 'long', 'namespace', 'new', 'null', 'object', 'operator', 'out', 'override',
			'params', 'private', 'protected', 'public', 'readonly', 'ref', 'return', 'sbyte',
			'sealed', 'short', 'sizeof', 'stackalloc', 'static', 'string', 'struct', 'switch',
			'this', 'throw', 'true', 'try', 'typeof', 'uint', 'ulong', 'unchecked', 'unsafe',
			'ushort', 'using', 'virtual', 'void', 'volatile', 'while',

			// Contextual, so each is here only because no generated name can collide with it.
			// `value` is contextual too and is deliberately absent: Decode takes a parameter of
			// that name, and colouring it would be a lie.
			'global', 'nameof', 'var',
		]),
	},
	JavaScript: {
		comment: String.raw`\/\*[\s\S]*?\*\/|\/\/.*`,
		literal: String.raw`'(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*"|\b0[xX][0-9A-Fa-f]+n?\b|\b\d[\d_]*n?\b`,
		keywords: new Set([
			'await', 'break', 'case', 'catch', 'class', 'const', 'continue', 'debugger', 'default',
			'delete', 'do', 'else', 'enum', 'export', 'extends', 'false', 'finally', 'for',
			'function', 'if', 'implements', 'import', 'in', 'instanceof', 'interface', 'let',
			'new', 'null', 'package', 'private', 'protected', 'public', 'return', 'static',
			'super', 'switch', 'this', 'throw', 'true', 'try', 'typeof', 'var', 'void', 'while',
			'with', 'yield',

			// Contextual, and safe for the same reason as the C# three above.
			'of',
		]),
	},
	C: {
		comment: String.raw`\/\*[\s\S]*?\*\/`,
		literal: String.raw`"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\b0[xX][0-9A-Fa-f]+\b|\b\d+(?:ULL|UL|LL|U|L)?\b`,
		keywords: new Set([
			'auto', 'break', 'case', 'char', 'const', 'continue', 'default', 'do', 'double', 'else',
			'enum', 'extern', 'float', 'for', 'goto', 'if', 'inline', 'int', 'long', 'register',
			'restrict', 'return', 'short', 'signed', 'sizeof', 'static', 'struct', 'switch',
			'typedef', 'union', 'unsigned', 'void', 'volatile', 'while',

			// From the headers the fragment names, and coloured because they read as types.
			'bool', 'false', 'true', 'size_t', 'int8_t', 'uint8_t', 'int16_t', 'uint16_t',
			'int32_t', 'uint32_t', 'int64_t', 'uint64_t', 'NULL',
		]),
	},
	Cpp: {
		comment: String.raw`\/\/.*`,
		literal: String.raw`"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\b0[xX][0-9A-Fa-f]+\b|\b\d[\d']*(?:ULL|UL|LL|U|L)?\b`,
		keywords: new Set([
			'alignas', 'alignof', 'auto', 'bool', 'break', 'case', 'catch', 'char', 'class',
			'const', 'constexpr', 'const_cast', 'continue', 'decltype', 'default', 'delete', 'do',
			'double', 'dynamic_cast', 'else', 'enum', 'explicit', 'export', 'extern', 'false',
			'float', 'for', 'friend', 'goto', 'if', 'inline', 'int', 'long', 'mutable',
			'namespace', 'new', 'noexcept', 'nullptr', 'operator', 'private', 'protected',
			'public', 'register', 'reinterpret_cast', 'return', 'short', 'signed', 'sizeof',
			'static', 'static_assert', 'static_cast', 'struct', 'switch', 'template', 'this',
			'throw', 'true', 'try', 'typedef', 'typeid', 'typename', 'union', 'unsigned', 'using',
			'virtual', 'void', 'volatile', 'while',
		]),
	},

	// The two below colour only the install snippets on the API page, never generated code.
	// Neither language reserves any words, so each colours the commands and keywords those
	// snippets use, and anything else stays plain.
	CMake: {
		comment: String.raw`#.*`,
		literal: String.raw`"(?:[^"\\]|\\.)*"`,
		keywords: new Set([
			'add_executable', 'add_library', 'cmake_minimum_required', 'find_package',
			'FetchContent_Declare', 'FetchContent_MakeAvailable', 'include', 'project', 'set',
			'target_link_libraries',

			'CONFIG', 'GIT_REPOSITORY', 'GIT_TAG', 'INTERFACE', 'LANGUAGES', 'PRIVATE', 'PUBLIC',
			'REQUIRED', 'SOURCE_SUBDIR', 'URL', 'URL_HASH', 'VERSION',
		]),
	},
	Shell: {
		comment: String.raw`#.*`,
		literal: String.raw`"(?:[^"\\]|\\.)*"|'[^']*'`,
		keywords: new Set(['dotnet', 'npm']),
	},
};

// R, for the API page's install snippet and example. A raw string comes first, so that the
// backslashes of a pattern inside one are not read as escapes; only the r"(...)" form is
// recognised, which is the one the examples use.
GRAMMARS.R = {
	comment: String.raw`#.*`,
	literal: String.raw`\b[rR]"\([\s\S]*?\)"|\b[rR]'\([\s\S]*?\)'|"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\b0[xX][0-9A-Fa-f]+L?\b|\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?L?\b`,
	keywords: new Set([
		'break', 'else', 'FALSE', 'for', 'function', 'if', 'in', 'Inf', 'NA', 'NA_character_',
		'NA_complex_', 'NA_integer_', 'NA_real_', 'NaN', 'next', 'NULL', 'repeat', 'TRUE',
		'while',
	]),

	// A name in R may hold dots, as in as.integer, and may start with one, but not as a number
	// such as .5 does.
	identifier: String.raw`(?:[A-Za-z]|\.(?!\d))[A-Za-z0-9._]*`,
};

// The same, written as pkgdown writes an example: what R printed follows on lines of its own, each
// marked #>, and is coloured as output rather than as the comment it would be to R.
GRAMMARS.RExample = { ...GRAMMARS.R, output: String.raw`^#>.*$` };

/** A name in every language but R, which says otherwise in its grammar. */
const IDENTIFIER = String.raw`[A-Za-z_][A-Za-z0-9_]*`;

/**
 * Splits code into the pieces to colour: comments, keywords, function names, and the literals in
 * between.
 *
 * A function name is any name directly followed by an opening parenthesis that is not a keyword,
 * so a declaration is coloured as well as a call, and `if (` and `sizeof(` stay keywords.
 *
 * @param {string} source The code.
 * @param {string} language The language it is written in: `CSharp`, `JavaScript`, `C` or `Cpp`,
 *     the names `OutputLanguage` gives them, `CMake` or `Shell` for an install snippet, or `R`
 *     or `RExample` for the R example.
 * @returns {{ kind: 'output' | 'comment' | 'keyword' | 'function' | 'literal' | null, text: string }[]}
 *     The pieces, in order, with a null kind for text left plain.
 */
export function tokenise(source, language) {
	const grammar = GRAMMARS[language];
	const output = grammar.output === undefined ? '' : `(?<output>${grammar.output})|`;
	const tokens = new RegExp(
		`${output}(?<comment>${grammar.comment})|(?<literal>${grammar.literal})|(?<word>${grammar.identifier ?? IDENTIFIER})`,
		'gm');

	// Tried where a word ends, so that it looks only at what follows the word.
	const opening = /\s*\(/y;

	const pieces = [];
	let at = 0;

	for (const match of source.matchAll(tokens)) {
		const [text] = match;
		const { comment, literal, output: printed } = match.groups;

		let kind = printed !== undefined ? 'output'
			: comment !== undefined ? 'comment'
			: literal !== undefined ? 'literal'
			: grammar.keywords.has(text) ? 'keyword'
			: null;

		if (kind === null) {
			opening.lastIndex = match.index + text.length;
			if (opening.test(source)) { kind = 'function'; }
		}

		// A word that is neither is left plain, and so is everything the pattern skipped.
		if (kind === null) { continue; }

		if (match.index > at) { pieces.push({ kind: null, text: source.slice(at, match.index) }); }

		pieces.push({ kind, text });

		at = match.index + text.length;
	}

	pieces.push({ kind: null, text: source.slice(at) });

	return pieces;
}
