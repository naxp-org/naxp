// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The reference implementation, copied into the built site from ../src/js/lib by
// eleventy.config.js. It is plain ESM with no Node dependencies, so the browser runs the same
// files the npm package ships.
import { Naxp } from '/naxp/index.js';
import { reserveHeight, textBox } from './boxes.js';

/**
 * The examples offered as chips. The first four are the first row of each table on the
 * pre-defined naxps page, in that page's order, so the two stay in step.
 */
const EXAMPLES = [
	{
		name: 'UK postcode',
		pattern: '\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA',
		tests: 'EC4M 8AD\nEC4M8AD\nM1 1AE\nGIR 0AA\nEC4M 8ADX',
	},
	{
		name: 'Ireland Eircode',
		pattern: '[AC-FHKNPRTV-Y]\\9[\\9AC-FHKNPRTV-Y] \\s!! [\\9AC-FHKNPRTV-Y]{4}',
		tests: 'A65 F4E2\nD6W F4E2\nA65F4E2\nA65 F4E2I',
	},
	{
		name: 'Netherlands postcode',
		pattern: '\\9\\9\\9\\9 \\s!! \\A\\A',
		tests: '1012 AB\n1012AB\n1012-AB',
	},
	{
		name: 'Flight designator',
		pattern: '(\\A\\A|\\A\\9|\\9\\A) #[0!0!0!1-9999]',
		tests: 'AC861\nAC0861\nU28\nBA02490',
	},
	{
		name: 'Hex colour',
		pattern: '\\#\\C[\\9A-F]{6}',
		tests: '#FF00AA\n#ff00aa\n#FF00A',
	},
];

/** The most test strings read from the box in one go. */
const MAX_TEST_LINES = 200;

const dom = {
	pattern: document.getElementById('naxp-pattern'),
	underlay: document.getElementById('naxp-underlay'),
	examples: document.getElementById('naxp-examples'),
	status: document.getElementById('naxp-status'),
	share: document.getElementById('naxp-share'),
	generate: document.getElementById('naxp-generate'),
	tests: document.getElementById('naxp-tests'),
	testResults: document.getElementById('naxp-test-results'),
	value: document.getElementById('naxp-value'),
	valueResult: document.getElementById('naxp-value-result'),
	samples: document.getElementById('naxp-samples'),
};

// The boxes fit their lines and carry a copy button. Each returns what to call after its value
// is set from code, which fires no input event.
const boxes = {
	pattern: textBox(dom.pattern),
	tests: textBox(dom.tests),
	value: textBox(dom.value),
};

/** The naxp currently parsed, or null while the pattern is empty or invalid. */
let current = null;

/**
 * Replaces an element's children.
 *
 * @param {Element} parent The element.
 * @param {...(Node | string)} children The children.
 */
function fill(parent, ...children) {
	parent.replaceChildren(...children);
}

/**
 * Builds an element.
 *
 * Everything on this page is built rather than assembled as markup, so a naxp or a test string
 * cannot be read as HTML however it is written.
 *
 * @param {string} tag The tag name.
 * @param {string | null} className The class attribute, or null for none.
 * @param {...(Node | string)} children The children.
 * @returns {HTMLElement} The element.
 */
function element(tag, className, ...children) {
	const made = document.createElement(tag);

	if (className) { made.className = className; }

	made.append(...children);

	return made;
}

/**
 * A cell holding a literal string, whose spaces the stylesheet keeps visible.
 *
 * @param {string} tag Either `td` or `th`.
 * @param {string} text The literal.
 * @returns {HTMLElement} The cell.
 */
function literalCell(tag, text) {
	const cell = element(tag, 'results__literal', text);

	if (tag === 'th') { cell.scope = 'row'; }

	return cell;
}

/**
 * A number with its digits grouped in threes.
 *
 * The groups are separate elements with a margin between them rather than separator characters,
 * so selecting the number and copying it gives the bare digits. That is what somebody reading a
 * developer page wants to paste into their own code. The text content is one unbroken run of
 * digits, so a screen reader reads a number rather than three of them.
 *
 * @param {bigint} value The number.
 * @returns {HTMLElement} The grouped digits.
 */
function grouped(value) {
	const digits = value.toString();
	const lead = digits.length % 3 || 3;
	const groups = [digits.slice(0, lead)];

	for (let at = lead; at < digits.length; at += 3) {
		groups.push(digits.slice(at, at + 3));
	}

	return element('span', 'digits', ...groups.map((g) => element('span', 'digits__group', g)));
}

/**
 * The count of bits needed to hold every encoded value a naxp produces, along with the zero that
 * stands for invalid text.
 *
 * @param {bigint} count The largest encoded value.
 * @returns {number} The bits.
 */
function bitsFor(count) {
	return count.toString(2).length;
}

/**
 * Builds a line of status.
 *
 * @param {string} kind The modifier: `ok`, `bad` or `idle`.
 * @param {string | null} flag The verdict, which takes a line of its own, or null for none.
 * @param {...(Node | string)} rest The words, and anything built rather than written.
 * @returns {HTMLElement} The line.
 */
function statusLine(kind, flag, ...rest) {
	const line = element('p', `status__line status__line--${kind}`);

	// The verdict is a word, coloured. The colour is never the only thing carrying it. The space
	// after it is a real one rather than the margin alone, so the verdict and the reason are two
	// words to anything reading the text rather than looking at it.
	if (flag !== null) { line.append(element('span', 'status__flag', flag), ' '); }

	line.append(...rest);

	return line;
}

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

/* ---------- the naxp itself ---------- */

/**
 * Reports a naxp that parsed.
 *
 * @param {Naxp} naxp The naxp.
 */
function showParsed(naxp) {
	const count = naxp.maxEncodedValue;

	clearUnderlay();

	fill(
		dom.status,
		statusLine(
			'ok',
			'Valid',
			grouped(count),
			` encoded values, held in ${bitsFor(count)} bits.`));
}

/**
 * The code of a fault, linked to the page that documents it.
 *
 * A reader meets a code at exactly this moment, so this is where the page is
 * worth offering. The anchor is the code in lower case, which every row of that
 * page carries as its id.
 *
 * @param {string} code The code, such as `NAXP1031`.
 * @returns {HTMLAnchorElement} The link.
 */
function codeLink(code) {
	const link = element('a', 'status__code', code);

	link.href = `/codes/#${code.toLowerCase()}`;
	link.title = `What ${code} means`;

	return link;
}

/**
 * Reports an invalid naxp, with the span at fault marked in the pattern.
 *
 * @param {string} pattern The pattern.
 * @param {{errorMessage: string, errorOffset: number, errorLength: number,
 * errorCode: string}} result The fault.
 */
function showInvalid(pattern, result) {
	const from = Math.min(result.errorOffset, pattern.length);
	const to = Math.min(pattern.length, from + result.errorLength);

	markUnderlay(pattern, from, to);

	fill(
		dom.status,
		statusLine(
			'bad',
			'Invalid',
			codeLink(result.errorCode),
			' ',
			result.errorMessage));
}

/** Reads the pattern box, parses it, and refreshes everything downstream. */
function refresh() {
	const pattern = dom.pattern.value;

	current = null;

	if (pattern.trim() === '') {
		// An empty box says all there is to say, so the status says nothing.
		clearUnderlay();
		fill(dom.status);
	}
	else {
		try {
			const result = Naxp.tryParse(pattern);

			current = result.naxp;

			if (result.naxp !== null) { showParsed(result.naxp); }
			else { showInvalid(pattern, result); }
		}
		catch (failure) {
			// Nothing in the library is meant to reach here. If something does, saying so beats
			// a page that has quietly stopped responding.
			clearUnderlay();
			fill(dom.status, statusLine('bad', 'Invalid', failure.message));
		}
	}

	updateGenerateLink();
	runTests();
	runDecode();
	runSamples();
}

/**
 * Points the code generation chip at the naxp in hand, or takes it out of service.
 *
 * `current` is null for an empty box and for one that does not parse, which are exactly the two
 * states where there is nothing to generate. Dropping the href rather than intercepting the
 * click is what makes the chip unreachable by keyboard too.
 */
function updateGenerateLink() {
	if (current === null) {
		dom.generate.removeAttribute('href');
		dom.generate.setAttribute('aria-disabled', 'true');

		return;
	}

	dom.generate.href = `/code-gen/#${encodeURIComponent(dom.pattern.value)}`;
	dom.generate.removeAttribute('aria-disabled');
}

/* ---------- encoding ---------- */

/** Encodes every line of the test box and tabulates the results. */
function runTests() {
	const raw = dom.tests.value;
	const lines = raw === '' ? [] : raw.split('\n');

	// A box ending in a newline is one somebody is still typing in, not a request to encode the
	// empty string.
	if (lines.length > 0 && lines[lines.length - 1] === '') { lines.pop(); }

	const overflow = lines.length > MAX_TEST_LINES;
	const wanted = overflow ? lines.slice(0, MAX_TEST_LINES) : lines;

	if (current === null || wanted.length === 0) {
		fill(dom.testResults, element('p', 'muted', 'Enter examples of text in the box above, one per line, to see how each is encoded.'));

		return;
	}

	const body = element('tbody', null);

	for (const line of wanted) {
		const value = current.encode(line);
		const valid = value !== 0n;

		body.append(
			element(
				'tr',
				valid ? null : 'is-invalid',
				literalCell('th', line),
				element('td', 'num', grouped(value)),
				valid
					? literalCell('td', current.getCanonicalForm(line))
					: element('td', 'muted', 'invalid')));
	}

	const table = element(
		'table',
		'results',
		element(
			'thead',
			null,
			element(
				'tr',
				null,
				element('th', null, 'Text'),
				element('th', 'num', 'Encoded value'),
				element('th', null, 'Canonical form'))),
		body);

	const parts = [element('div', 'table-scroll', table)];

	if (overflow) {
		parts.push(element('p', 'muted results__note', `Showing the first ${MAX_TEST_LINES} lines.`));
	}

	fill(dom.testResults, ...parts);
}

/* ---------- decoding ---------- */

/** Decodes the value box. */
function runDecode() {
	const text = dom.value.value.trim();

	if (current === null || text === '') {
		fill(dom.valueResult, element('p', 'muted', 'Nothing to decode yet.'));

		return;
	}

	let value;

	try {
		value = BigInt(text);
	}
	catch {
		fill(dom.valueResult, statusLine('bad', null, 'Not a whole number.'));

		return;
	}

	const decoded = current.tryDecode(value);

	if (decoded === null) {
		fill(
			dom.valueResult,
			statusLine(
				'bad',
				null,
				'This naxp encodes 1 to ',
				grouped(current.maxEncodedValue),
				'.'));

		return;
	}

	fill(dom.valueResult, element('p', 'decoded', decoded));
}

/**
 * Encoded values spread across a naxp's range: the first few, five steps through the middle, and
 * the last.
 *
 * @param {bigint} count The largest encoded value.
 * @returns {bigint[]} The encoded values, ascending.
 */
function sampleValues(count) {
	const wanted = new Set();
	const head = count < 5n ? count : 5n;

	for (let value = 1n; value <= head; value++) { wanted.add(value); }

	for (let step = 1n; step <= 5n; step++) { wanted.add((count * step) / 6n); }

	wanted.add(count);

	return [...wanted]
		.filter((value) => value >= 1n && value <= count)
		.sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
}

/** Decodes a spread of encoded values, which is what shows the ordering. */
function runSamples() {
	if (current === null) {
		fill(dom.samples, element('p', 'muted', 'Nothing to show yet.'));

		return;
	}

	const body = element('tbody', null);

	for (const value of sampleValues(current.maxEncodedValue)) {
		body.append(
			element(
				'tr',
				null,
				element('th', 'num', grouped(value)),
				literalCell('td', current.decode(value))));
	}

	const table = element(
		'table',
		'results',
		element(
			'thead',
			null,
			element('tr', null, element('th', 'num', 'Encoded value'), element('th', null, 'Text'))),
		body);

	fill(dom.samples, element('div', 'table-scroll', table));
}

/* ---------- sharing ---------- */

/**
 * Puts the pattern in the address bar, so the page's own URL is the naxp being worked on.
 *
 * Kept off the parsing path and on a slower timer of its own. Browsers rate-limit replaceState -
 * Safari at a hundred calls in thirty seconds - and somebody typing steadily would reach that in
 * well under a minute at the rate the parse runs.
 */
function writeShareLink() {
	const pattern = dom.pattern.value;
	const hash = pattern === '' ? '' : `#${encodeURIComponent(pattern)}`;

	history.replaceState(null, '', `${location.pathname}${location.search}${hash}`);
}

/**
 * Reads a pattern out of the address bar, for a link somebody was sent.
 *
 * @returns {string | null} The pattern, or null if there is none.
 */
function readShareLink() {
	if (location.hash.length < 2) { return null; }

	try {
		return decodeURIComponent(location.hash.slice(1));
	}
	catch {
		return null;
	}
}

/* ---------- wiring ---------- */

/**
 * Runs a function once the caller has stopped typing.
 *
 * @param {() => void} work The function.
 * @param {number} delay The pause, in milliseconds, that counts as stopping.
 * @returns {() => void} The debounced function.
 */
function debounce(work, delay) {
	let timer = 0;

	return () => {
		clearTimeout(timer);
		timer = setTimeout(work, delay);
	};
}

/** Builds the example chips. */
function buildExamples() {
	for (const example of EXAMPLES) {
		const chip = element('button', 'chip', example.name);

		chip.type = 'button';
		chip.addEventListener('click', () => {
			dom.pattern.value = example.pattern;
			dom.tests.value = example.tests;
			dom.value.value = '1';
			boxes.pattern();
			boxes.tests();
			boxes.value();

			refresh();
			writeShareLink();
			dom.pattern.focus();
		});

		dom.examples.append(chip);
	}
}

buildExamples();

const shareSoon = debounce(writeShareLink, 600);

dom.pattern.addEventListener('scroll', syncUnderlay);
dom.pattern.addEventListener('input', mirrorUnderlay);
dom.pattern.addEventListener('input', debounce(refresh, 120));
dom.pattern.addEventListener('input', shareSoon);
dom.tests.addEventListener('input', debounce(runTests, 120));
dom.value.addEventListener('input', debounce(runDecode, 120));

dom.share.addEventListener('click', async () => {
	// The address bar holds the naxp already, so a browser that refuses the clipboard - no
	// permission, or an insecure origin - has cost the reader nothing but the shortcut.
	writeShareLink();

	let said = 'Copied';

	try {
		await navigator.clipboard.writeText(location.href);
	}
	catch {
		said = 'Copy from the address bar';
	}

	dom.share.textContent = said;
	setTimeout(() => { dom.share.textContent = 'Copy link'; }, 1500);
});

// A link pasted into the address bar of the page it points at changes the fragment and nothing
// else, so the browser fires this rather than loading anything. Without it the link appears to do
// nothing, while the same link in a new window works, because that is a fresh load.
//
// Our own writes cannot reach here: `writeShareLink` uses replaceState, which does not fire it.
window.addEventListener('hashchange', () => {
	const pattern = readShareLink();

	if (pattern === null || pattern === dom.pattern.value) { return; }

	dom.pattern.value = pattern;
	boxes.pattern();
	refresh();
});

const shared = readShareLink();

if (shared !== null) {
	dom.pattern.value = shared;
	boxes.pattern();
}

refresh();
reserveHeight(document.querySelector('main'));
