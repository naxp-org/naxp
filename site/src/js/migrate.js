// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The reference implementation, copied into the built site from ../src/js/lib by
// eleventy.config.js, exactly as the developer page uses it. The comparison runs in the browser,
// so every figure on this page comes from the same code the npm package ships.
import { Naxp, SetRelationship } from '/naxp/index.js';
import { reserveHeight, textBox } from './boxes.js';

const dom = {
	a: document.getElementById('naxp-a'),
	b: document.getElementById('naxp-b'),
	verdict: document.getElementById('compare-verdict'),
	axesTable: document.getElementById('compare-axes-table'),
	axes: document.getElementById('compare-axes'),
	counts: document.getElementById('compare-counts'),
	divergence: document.getElementById('compare-divergence'),
	divergenceTable: document.getElementById('compare-divergence-table'),
	divergenceRows: document.getElementById('compare-divergence-rows'),
	swap: document.getElementById('naxp-swap'),
	clear: document.getElementById('naxp-clear'),
};

// The boxes fit their lines and carry a copy button. Each returns what to call after its value
// is set from code, which the swap does.
const boxes = {
	a: textBox(dom.a),
	b: textBox(dom.b),
};

/**
 * What each relationship means on each axis, in the reader's terms rather than the library's.
 * The relationship is always shown as well, so this explains the word rather than replacing it.
 *
 * `{a}` and `{b}` become chips, as the two naxps are written everywhere else on the page.
 */
const MEANING = {
	acceptedText: {
		Equal: 'Both accept exactly the same text.',
		SubsetOf: '{b} accepts everything {a} accepts, and more besides. Nothing that worked starts being refused.',
		SupersetOf: '{b} refuses text that {a} accepts. Some of what you hold stops being valid.',
		Incomparable: 'Each accepts text the other refuses.',
	},
	encoding: {
		Equal: 'Every string both accept has the same value under each. This is the one that decides the verdict.',
		SubsetOf: '{b} gives every string {a} accepts the value {a} gave it, and numbers the rest above them. This is the one that decides the verdict.',
		SupersetOf: '{a} defines values {b} does not produce. This is the one that decides the verdict.',
		Incomparable: 'At least one string encodes to a different value. This is the one that decides the verdict.',
	},
	printedText: {
		Equal: 'Decoding prints the same forms under each.',
		SubsetOf: '{b} can print forms {a} cannot, and no others differ.',
		SupersetOf: '{a} can print forms {b} cannot, and no others differ.',
		Incomparable: 'Each prints forms the other does not.',
	},
};

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
 * Everything variable on this page is built rather than assembled as markup, so a naxp or a
 * decoded string cannot be read as HTML however it is written.
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
 * A number with its digits grouped in threes.
 *
 * The groups are separate elements with a margin between them rather than separator characters,
 * so selecting the number and copying it gives the bare digits.
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

/**
 * What the two naxps are called on the page. The strings below say `{a}` and `{b}`, so the
 * names can change here without touching a sentence; the labels and table headings in
 * migrate.html carry them literally and change with them.
 */
const NAMES = { a: 'old', b: 'new' };

/**
 * The nodes that name one of the two naxps: the word naxp in bold, then the name as a chip, as
 * `<strong>naxp</strong> <code>old</code>`.
 *
 * @param {'a' | 'b'} key Which of the two.
 * @returns {Array<Node | string>} The nodes.
 */
function chip(key) {
	return [element('strong', null, 'naxp'), ' ', element('code', null, NAMES[key])];
}

/**
 * Turns a sentence written with `{a}` and `{b}` into nodes, with each of those named as
 * {@link chip} does.
 *
 * @param {string} sentence The sentence.
 * @returns {Array<Node | string>} The words and the names.
 */
function chipped(sentence) {
	return sentence
		.split(/\{([ab])\}/)
		.flatMap((part, at) => (at % 2 === 0 ? [part] : chip(part)));
}

/**
 * A cell holding a decoded string, whose spaces the stylesheet keeps visible. A postcode that
 * differs from another only in its spacing is exactly the case this page is for.
 *
 * @param {string} text The string.
 * @returns {HTMLElement} The cell.
 */
function literalCell(text) {
	return element('td', null, element('code', 'results__literal', text));
}

/**
 * Whether a change is safe for data already encoded.
 *
 * Safe exactly when the encoding relationship is `Equal` or `SubsetOf`: the graph of the old
 * naxp is contained in the new one's, so every (text, value) pair still holds. Anything else
 * means at least one stored value now means something different, or is no longer produced.
 *
 * @param {string} encoding The encoding relationship.
 * @returns {boolean} Whether the change is safe.
 */
function isSafe(encoding) {
	return encoding === SetRelationship.Equal || encoding === SetRelationship.SubsetOf;
}

/* ---------- parsing the two boxes ---------- */

/**
 * Parses one box, reporting the fault where it will not parse.
 *
 * @param {HTMLTextAreaElement} box The box.
 * @param {string} name What the box is called, `a` or `b`.
 * @returns {{naxp: Naxp | null, fault: HTMLElement | null}} The naxp, or a line saying why not.
 */
function read(box, name) {
	const pattern = box.value;

	if (pattern.trim() === '') {
		return {
			naxp: null,
			fault: statusLine('idle', null, ...chipped(`Write {${name}} to begin.`)),
		};
	}

	const result = Naxp.tryParse(pattern);

	if (result.naxp !== null) { return { naxp: result.naxp, fault: null }; }

	return {
		naxp: null,
		fault: statusLine(
			'bad',
			'Invalid',
			...chip(name),
			': ',
			element('span', 'status__code', result.errorCode),
			' ',
			result.errorMessage),
	};
}

/* ---------- the three axes ---------- */

/**
 * Fills the verdict, the three axes and the two counts.
 *
 * @param {Naxp} a The naxp the data was encoded with.
 * @param {Naxp} b The naxp proposed to replace it.
 */
function showComparison(a, b) {
	const comparison = Naxp.tryCompare(a, b);

	if (comparison === null) {
		// Unreachable for any naxp anybody has reason to write: the walk that decides the
		// encoding is given two hundred thousand product states and the postcode uses a few
		// hundred. It is reported rather than guessed at all the same.
		fill(
			dom.verdict,
			statusLine(
				'bad',
				'Undecided',
				'These two naxps are too tangled to compare within the work this page will do. '
				+ 'Nothing is wrong with either of them.'));

		fill(dom.axes);
		dom.axesTable.hidden = true;
		fill(dom.counts);

		return;
	}

	dom.axesTable.hidden = false;

	const safe = isSafe(comparison.encoding);

	fill(
		dom.verdict,
		safe
			? statusLine(
				'ok',
				'Safe',
				...chipped('Every value already stored under {a} means the same under {b}.'))
			: statusLine(
				'bad',
				'Not safe',
				...chipped('Values already stored under {a} mean something else under {b}.')));

	const axes = [
		['Accepted text', comparison.acceptedText, MEANING.acceptedText],
		['Encoding', comparison.encoding, MEANING.encoding],
		['Printed text', comparison.printedText, MEANING.printedText],
	];

	fill(
		dom.axes,
		...axes.map(([name, relationship, meaning]) => {
			const heading = element('th', null, name);

			heading.scope = 'row';

			return element(
				'tr',
				null,
				heading,
				element('td', null, element('code', null, relationship)),
				element('td', null, ...chipped(meaning[relationship])));
		}));

	fill(
		dom.counts,
		countLine('a', a.maxEncodedValue),
		countLine('b', b.maxEncodedValue));
}

/**
 * One naxp's count of encoded values, and the bits that holds.
 *
 * @param {string} name What the naxp is called, `a` or `b`.
 * @param {bigint} count The largest encoded value.
 * @returns {HTMLElement} The line.
 */
function countLine(name, count) {
	return element(
		'p',
		null,
		...chip(name),
		' holds ',
		grouped(count),
		` encoded values · ${bitsFor(count)} bits`);
}

/* ---------- where the two first disagree ---------- */

/**
 * Fills the band that names the lowest value the two decode differently.
 *
 * @param {Naxp} a The first naxp.
 * @param {Naxp} b The second naxp.
 */
function showDivergence(a, b) {
	const value = Naxp.firstDivergentValue(a, b);

	if (value === 0n) {
		fill(
			dom.divergence,
			'Every value the two both hold decodes to the same text under each. That says nothing '
			+ 'about values only one of them holds, which the two counts above cover.');

		fill(dom.divergenceRows);
		dom.divergenceTable.hidden = true;

		return;
	}

	// At 1 there is nothing below it, so saying so would be saying nothing.
	fill(
		dom.divergence,
		'The lowest value the two disagree about is ',
		element('strong', null, grouped(value)),
		value === 1n ? '.' : '. Everything below it is untouched.');

	const rows = [];

	// The value below is shown for context where there is one, so the reader can see the two
	// agreeing right up to the point they stop.
	if (value > 1n) { rows.push(divergenceRow(a, b, value - 1n, 'agree', false)); }

	rows.push(divergenceRow(a, b, value, 'the first disagreement', true));

	fill(dom.divergenceRows, ...rows);
	dom.divergenceTable.hidden = false;
}

/**
 * One row of the divergence table.
 *
 * @param {Naxp} a The first naxp.
 * @param {Naxp} b The second naxp.
 * @param {bigint} value The encoded value.
 * @param {string} note What to say about the row.
 * @param {boolean} differs Whether this is the row where they part.
 * @returns {HTMLElement} The row.
 */
function divergenceRow(a, b, value, note, differs) {
	const row = element(
		'tr',
		differs ? 'is-invalid' : null,
		element('td', 'num', grouped(value)),
		literalCell(a.decode(value)),
		literalCell(b.decode(value)),
		element('td', null, note));

	return row;
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

/** Reads both boxes and refreshes everything below them. */
function refresh() {
	const left = read(dom.a, 'a');
	const right = read(dom.b, 'b');

	if (left.naxp === null || right.naxp === null) {
		// Both faults are shown. Somebody who has pasted two naxps in wants to be told about
		// both of them, not sent back a second time.
		fill(dom.verdict, ...[left.fault, right.fault].filter((line) => line !== null));

		fill(dom.axes);
		dom.axesTable.hidden = true;
		fill(dom.counts);
		fill(dom.divergence);
		fill(dom.divergenceRows);
		dom.divergenceTable.hidden = true;

		return;
	}

	showComparison(left.naxp, right.naxp);
	showDivergence(left.naxp, right.naxp);
}

dom.a.addEventListener('input', debounce(refresh, 200));
dom.b.addEventListener('input', debounce(refresh, 200));

// Swapping asks the reverse question: whether going back would be safe.
dom.swap.addEventListener('click', () => {
	const a = dom.a.value;

	dom.a.value = dom.b.value;
	dom.b.value = a;
	boxes.a();
	boxes.b();
	refresh();
});

dom.clear.addEventListener('click', () => {
	dom.a.value = '';
	dom.b.value = '';
	boxes.a();
	boxes.b();
	refresh();
	dom.a.focus();
});

refresh();
reserveHeight(document.querySelector('main'));
