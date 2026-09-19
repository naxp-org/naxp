// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The reference implementation, copied into the built site from ../src/js/lib by
// eleventy.config.js. The emitters run here in the browser, so the fragment on this page is
// written by the same code the npm package ships and the source generator uses.
import { Naxp, NaxpValueType, OutputLanguage } from '/naxp/index.js';
import { copyButton, keep, recall, remember, reserveHeight, textBox } from './boxes.js';

/**
 * The languages the emitters cover. Everything else on the page is a chip marked unavailable,
 * listed there so a reader can see what is coming rather than wonder whether it exists.
 */
const LANGUAGES = {
	'C#': OutputLanguage.CSharp,
	JavaScript: OutputLanguage.JavaScript,
	C: OutputLanguage.C,
	'C++': OutputLanguage.Cpp,
};

/**
 * The integer types C#, C and C++ can hold an encoded value in, narrowest first, with the
 * largest value each holds. Zero is reserved for text the naxp does not accept, so a type holds
 * its own maximum rather than one more. The keyword is C#'s, for the source generator hint.
 */
const INTEGER_TYPES = [
	{ name: NaxpValueType.Int8, holds: 127n, keyword: 'sbyte' },
	{ name: NaxpValueType.UInt8, holds: 255n, keyword: 'byte' },
	{ name: NaxpValueType.Int16, holds: 32767n, keyword: 'short' },
	{ name: NaxpValueType.UInt16, holds: 65535n, keyword: 'ushort' },
	{ name: NaxpValueType.Int32, holds: 2147483647n, keyword: 'int' },
	{ name: NaxpValueType.UInt32, holds: 4294967295n, keyword: 'uint' },
	{ name: NaxpValueType.Int64, holds: 9223372036854775807n, keyword: 'long' },
	{ name: NaxpValueType.UInt64, holds: 18446744073709551615n, keyword: 'ulong' },
];

/** The largest value a JavaScript number holds exactly, `Number.MAX_SAFE_INTEGER`. */
const MAX_SAFE_INTEGER = 9007199254740991n;

/** How many lines the box shows before it has to be asked to open. */
const CLIPPED_LINES = 40;

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
	[OutputLanguage.CSharp]: {
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
	[OutputLanguage.JavaScript]: {
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
	[OutputLanguage.C]: {
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
	[OutputLanguage.Cpp]: {
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
};

const dom = {
	pattern: document.getElementById('naxp-pattern'),
	underlay: document.getElementById('naxp-underlay'),
	status: document.getElementById('naxp-status'),
	languages: document.getElementById('naxp-languages'),
	prefix: document.getElementById('naxp-prefix'),
	valueType: document.getElementById('naxp-valuetype'),
	valueNote: document.getElementById('naxp-valuetype-note'),
	codebox: document.getElementById('naxp-codebox'),
	output: document.getElementById('naxp-output'),
	expand: document.getElementById('naxp-expand'),
	generatorHint: document.getElementById('naxp-generator-hint'),
};

// The pattern box fits its lines and carries a copy button; the generated code carries the same
// button, disabled while there is nothing to copy. The output is read when the button is pressed,
// so it copies whatever is showing.
const syncPattern = textBox(dom.pattern);

dom.copy = copyButton(dom.codebox, () => dom.output.textContent);

// The settings are remembered with the naxp. The language and the value type are restored
// further down, once the chips are wired and the naxp is parsed, since each needs the other.
remember(dom.prefix);

/** The naxp currently parsed, or null while the pattern is empty or invalid. */
let current = null;

/** The language chip pressed. */
let language = OutputLanguage.JavaScript;

/**
 * Whether the reader has picked an integer type themselves. Until they do, the narrowest type
 * that fits follows the naxp as it is edited; once they have, their choice is left alone for as
 * long as it still holds every value.
 */
let typeChosen = recall('generate.valueType') !== null;

/** The type the reader chose, which is kept across languages that cannot offer it. */
let chosenType = recall('generate.valueType') ?? NaxpValueType.UInt64;

/** Whether the reader has opened the code box, which outlives any one fragment. */
let expanded = recall('generate.expanded') === 'true';

/* ---------- small helpers ---------- */

/**
 * Replaces an element's children.
 *
 * @param {Element} parent The element.
 * @param {...(Node | string)} children What it should hold.
 * @returns {Element} The element.
 */
function fill(parent, ...children) {
	parent.replaceChildren(...children);

	return parent;
}

/**
 * Builds an element.
 *
 * @param {string} tag The tag name.
 * @param {string | null} className Its classes, or null for none.
 * @param {...(Node | string)} children What it should hold.
 * @returns {HTMLElement} The element.
 */
function element(tag, className, ...children) {
	const made = document.createElement(tag);

	if (className !== null) { made.className = className; }

	made.append(...children);

	return made;
}

/**
 * Builds a line of status.
 *
 * @param {string} kind The modifier: `ok`, `bad` or `idle`.
 * @param {string | null} flag The verdict, which takes a word of its own, or null for none.
 * @param {...(Node | string)} rest The words.
 * @returns {HTMLElement} The line.
 */
function statusLine(kind, flag, ...rest) {
	const line = element('p', `status__line status__line--${kind}`);

	if (flag !== null) { line.append(element('span', 'status__flag', flag), ' '); }

	line.append(...rest);

	return line;
}

/**
 * Runs work once the caller has stopped asking for it.
 *
 * @param {() => void} work What to run.
 * @param {number} delay How long to wait.
 * @returns {() => void} The debounced call.
 */
function debounce(work, delay) {
	let timer = 0;

	return () => {
		clearTimeout(timer);
		timer = setTimeout(work, delay);
	};
}

/**
 * @param {string} value One of {@link OutputLanguage}.
 * @returns {string} What the chip for it says.
 */
function nameOf(value) {
	return Object.keys(LANGUAGES).find(name => LANGUAGES[name] === value) ?? value;
}

/* ---------- marking a fault in the naxp ---------- */

/** Clears the layer that marks a fault in the pattern box. */
function clearUnderlay() {
	fill(dom.underlay);
}

/**
 * Puts the box's text into the layer behind it with nothing marked.
 *
 * This runs on every keystroke, where the parse is debounced, so the layer's text can never lag
 * the box's. A mark is placed by offset, and an offset into text that is one edit out of date
 * lands under the wrong character.
 */
function mirrorUnderlay() {
	fill(dom.underlay, dom.pattern.value);
	syncUnderlay();
}

/**
 * Marks the span at fault in the pattern box itself, under the text being edited.
 *
 * @param {string} pattern The pattern.
 * @param {number} from Where the span starts.
 * @param {number} to Where it ends.
 */
function markUnderlay(pattern, from, to) {
	// A fault of no width still has to point somewhere, and the mark cannot be given a width of
	// its own: anything that takes space in this layer moves its text out of step with the box
	// behind it. So a character beside the offset is marked instead.
	let start = from;
	let end = to;

	if (end === start) {
		if (start < pattern.length) { end = start + 1; }
		else if (start > 0) { start -= 1; }
	}

	fill(
		dom.underlay,
		pattern.slice(0, start),
		element('span', 'errmark', pattern.slice(start, end)),
		pattern.slice(end));

	syncUnderlay();
}

/** Keeps the layer behind the pattern box scrolled with it. */
function syncUnderlay() {
	dom.underlay.scrollTop = dom.pattern.scrollTop;
	dom.underlay.scrollLeft = dom.pattern.scrollLeft;
}

/* ---------- the value type ---------- */

/**
 * The narrowest integer type that holds every value a naxp encodes.
 *
 * @param {bigint} count The largest encoded value.
 * @returns {string} The type.
 */
function narrowestFor(count) {
	// W5 caps a naxp at 2^64 - 1, so the last type always fits and the fallback is belt and braces.
	return (INTEGER_TYPES.find(type => count <= type.holds) ?? INTEGER_TYPES[7]).name;
}

/**
 * Offers what the chosen language can hold a value in, and picks where the reader has not.
 *
 * The languages differ in kind here rather than in degree. C#, C and C++ have eight integer
 * types and the choice is real: it changes the constants, the signatures and the casts.
 * JavaScript has one number type, exact to 2^53 - 1, and BigInt above that, and its emitter
 * reads the naxp's own size to decide between them. There is nothing there for a reader to
 * choose, so the control says what will happen rather than pretending otherwise.
 */
function offerValueTypes() {
	const count = current === null ? 0n : current.maxEncodedValue;

	if (language === OutputLanguage.JavaScript) {
		const spelling = count > MAX_SAFE_INTEGER ? 'BigInt' : 'number';

		fill(dom.valueType, element('option', null, spelling));
		dom.valueType.disabled = true;
		dom.valueNote.textContent = count > MAX_SAFE_INTEGER
			? 'Past the 2^53 - 1 a JavaScript number holds exactly, so the fragment uses BigInt.'
			: 'JavaScript has one number type, so there is nothing to choose. BigInt above 2^53 - 1.';

		return;
	}

	const narrowest = narrowestFor(count);
	const wanted = typeChosen ? chosenType : narrowest;

	fill(dom.valueType, ...INTEGER_TYPES.map((type) => {
		const option = element('option', null, type.name);

		// A type too small is disabled rather than dropped, so the list does not change length as
		// the naxp grows and a reader can see why a choice went away.
		option.disabled = count > type.holds;

		return option;
	}));

	dom.valueType.disabled = false;
	dom.valueType.value = INTEGER_TYPES.some(type => type.name === wanted && count <= type.holds)
		? wanted
		: narrowest;
	dom.valueNote.textContent = 'Starts at the narrowest that holds every value.';
}

/* ---------- highlighting ---------- */

/**
 * The fragment as coloured spans: comments, keywords, and the literals in between.
 *
 * @param {string} source The fragment.
 * @param {string} of The {@link OutputLanguage} it is written in.
 * @returns {(Node | string)[]} The pieces, in order.
 */
function highlight(source, of) {
	const grammar = GRAMMARS[of];
	const tokens = new RegExp(
		`(?<comment>${grammar.comment})|(?<literal>${grammar.literal})|(?<word>[A-Za-z_][A-Za-z0-9_]*)`,
		'g');

	const pieces = [];
	let at = 0;

	for (const match of source.matchAll(tokens)) {
		const [text] = match;
		const { comment, literal } = match.groups;

		// A word that is not a keyword is left plain, and so is everything the pattern skipped.
		if (comment === undefined && literal === undefined && !grammar.keywords.has(text)) {
			continue;
		}

		if (match.index > at) { pieces.push(source.slice(at, match.index)); }

		pieces.push(element(
			'span',
			`code__${comment !== undefined ? 'comment' : literal !== undefined ? 'literal' : 'keyword'}`,
			text));

		at = match.index + text.length;
	}

	pieces.push(source.slice(at));

	return pieces;
}

/* ---------- emitting ---------- */

/** Reads the pattern box, parses it, and refreshes everything downstream. */
function refresh() {
	const pattern = dom.pattern.value;

	current = null;

	if (pattern.trim() === '') {
		clearUnderlay();
		offerValueTypes();
		fill(dom.status, statusLine('idle', null, 'Write a naxp to generate code for.'));
		showNothing('Write a naxp above and the code appears here.');

		return;
	}

	const result = Naxp.tryParse(pattern);

	if (result.naxp === null) {
		const from = Math.min(result.errorOffset, pattern.length);
		const to = Math.min(pattern.length, from + result.errorLength);

		markUnderlay(pattern, from, to);
		fill(
			dom.status,
			statusLine(
				'bad',
				'Invalid',
				element('span', 'status__code', result.errorCode),
				' ',
				result.errorMessage));
		showNothing('There is nothing to generate until the naxp is valid.');

		return;
	}

	current = result.naxp;

	clearUnderlay();
	offerValueTypes();
	emit();
}

/** Writes the fragment for the naxp, the language and the options in hand. */
function emit() {
	if (current === null) { return; }

	// JavaScript's emitter reads the naxp's size rather than this, so the widest type is passed
	// and the control beside it says as much.
	const valueType = dom.valueType.disabled ? NaxpValueType.UInt64 : dom.valueType.value;

	try {
		const source = current.emit(language, dom.prefix.value.trim(), valueType);
		const lines = source.split('\n').length - 1;

		fill(dom.output, ...highlight(source, language));
		dom.copy.disabled = false;
		clip(lines);

		fill(dom.status, statusLine('ok', 'Valid', `${lines} lines of ${nameOf(language)}.`));
		showGeneratorHint(valueType);
	}
	catch (error) {
		// The library's own wording, which knows why a prefix or a type was refused.
		fill(dom.status, statusLine('bad', 'Cannot generate', error.message));
		showNothing('Fix the settings above and the code appears here.');
	}
}

/**
 * Offers the source generator to anybody generating C#, with the attribute they would write.
 *
 * The whole page is about pasting a fragment in, and for one language that is the second best
 * way to do it: the generator writes the same members at compile time from the naxp itself, so
 * the naxp stays the thing in the source rather than the code it expanded to. Shown only for C#,
 * because it is the only language with a generator.
 *
 * @param {string} valueType The {@link NaxpValueType} the fragment was emitted with.
 */
function showGeneratorHint(valueType) {
	if (language !== OutputLanguage.CSharp) {
		dom.generatorHint.hidden = true;

		return;
	}

	const prefix = dom.prefix.value.trim();
	const keyword = (INTEGER_TYPES.find(type => type.name === valueType) ?? INTEGER_TYPES[7]).keyword;

	// A verbatim string, because a naxp is mostly backslashes. A quotation mark is a legal naxp
	// character and doubles inside one.
	const attribute = `[Naxp(@"${dom.pattern.value.replace(/"/g, '""')}", typeof(${keyword})`
		+ (prefix === '' ? '' : `, Prefix = "${prefix}"`)
		+ ')]';

	const packages = element('a', null, element('strong', null, 'naxp'), ' NuGet package');

	packages.href = '/libraries/';

	fill(
		dom.generatorHint,
		element('span', 'admonition__flag', 'Tip'),
		'For C#, prefer the ',
		packages,
		' source generator, which produces identical code simply by annotating a partial class with ',
		element('code', null, attribute),
		'. This means you get the best of both worlds, i.e. the ',
		element('strong', null, 'naxp'),
		' defined in your source ',
		element('em', null, 'plus'),
		' compile time code gen.');

	dom.generatorHint.hidden = false;
}

/**
 * Cuts the box down where the fragment is long, and offers to open it.
 *
 * @param {number} lines How many lines the fragment runs to.
 */
function clip(lines) {
	if (lines <= CLIPPED_LINES) {
		dom.codebox.classList.remove('codebox--clipped');
		dom.expand.hidden = true;

		return;
	}

	// A reader who opened the box is reading it, and every setting on this page changes the
	// fragment: shutting it on each keystroke would mean opening it again to see the effect of
	// the change they just made. So the choice outlives the fragment it was made on, and a short
	// fragment that hides the button leaves it standing for the next long one.
	dom.codebox.classList.toggle('codebox--clipped', !expanded);
	dom.expand.hidden = false;
	dom.expand.textContent = expanded ? 'Show less' : `Show all ${lines} lines`;
	dom.expand.setAttribute('aria-expanded', String(expanded));
}

/**
 * Empties the output pane and says why it is empty.
 *
 * @param {string} why The reason.
 */
function showNothing(why) {
	dom.generatorHint.hidden = true;
	fill(dom.output, why);
	dom.copy.disabled = true;
	dom.codebox.classList.remove('codebox--clipped');
	dom.expand.hidden = true;
}

/* ---------- wiring ---------- */

for (const chip of dom.languages.querySelectorAll('.chip')) {
	if (chip.getAttribute('aria-disabled') === 'true') { continue; }

	chip.addEventListener('click', () => {
		language = LANGUAGES[chip.textContent.trim()];

		for (const other of dom.languages.querySelectorAll('.chip')) {
			if (other.getAttribute('aria-disabled') !== 'true') {
				other.setAttribute('aria-pressed', String(other === chip));
			}
		}

		keep('generate.language', chip.textContent.trim());
		offerValueTypes();
		emit();
	});
}

// The language last chosen, pressed as the reader would press it. Before the first refresh, so
// the naxp is parsed once, in that language.
const keptLanguage = recall('generate.language');

for (const chip of dom.languages.querySelectorAll('.chip')) {
	if (chip.getAttribute('aria-disabled') !== 'true' && chip.textContent.trim() === keptLanguage) {
		chip.click();
	}
}

dom.pattern.addEventListener('scroll', syncUnderlay);
dom.pattern.addEventListener('input', mirrorUnderlay);
dom.pattern.addEventListener('input', debounce(refresh, 120));
dom.prefix.addEventListener('input', debounce(emit, 120));

dom.valueType.addEventListener('change', () => {
	typeChosen = true;
	chosenType = dom.valueType.value;
	keep('generate.valueType', chosenType);
	emit();
});

dom.expand.addEventListener('click', () => {
	expanded = !expanded;
	keep('generate.expanded', String(expanded));
	clip(dom.output.textContent.split('\n').length - 1);
});

/**
 * A naxp handed over by another page, in the fragment.
 *
 * /interactive/ writes its naxp there for its own Copy link chip, and its code generation chip
 * links here with the same encoding, so one function reads either. Read once, at load: this page
 * does not maintain the fragment afterwards, so editing the box here leaves the URL behind.
 *
 * @returns {string | null} The pattern, or null where there is none or it will not decode.
 */
function readHandover() {
	if (location.hash.length < 2) { return null; }

	try { return decodeURIComponent(location.hash.slice(1)); }
	catch { return null; }
}

const handedOver = readHandover();

if (handedOver !== null) {
	dom.pattern.value = handedOver;
	syncPattern();
}

mirrorUnderlay();
refresh();
reserveHeight(document.querySelector('main'));
