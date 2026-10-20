// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The reference implementation, copied into the built site from ../src/js/lib by
// eleventy.config.js, exactly as the developer page uses it. The comparison runs in the browser,
// so every figure on this page comes from the same code the npm package ships.
import { Naxp, SetRelationship } from '/naxp/index.js';
import { reserveHeight, textBox } from './boxes.js';
import { readStandards, showStandard } from './standard-match.js';

/**
 * The standard naxps, written into the page by the build, so that the note under each box can
 * say when its naxp is one of them. Leaving a standard is worth knowing about when migrating.
 */
const STANDARDS = readStandards();

const dom = {
	a: document.getElementById('naxp-a'),
	b: document.getElementById('naxp-b'),
	aStandard: document.getElementById('naxp-a-standard'),
	bStandard: document.getElementById('naxp-b-standard'),
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
 * `{a}` and `{b}` become chips, as the two naxps are written everywhere else on the page, and
 * `{naxp}` becomes the word in bold, as the brand is written in the page's prose.
 */
const MEANING = {
	acceptedText: {
		Equal: 'Both {naxp}s accept the same text.',
		SubsetOf: '{b} accepts everything {a} accepts, and more. Everything that worked before still works.',
		SupersetOf: '{b} treats some text that {a} accepts as invalid.',
		Incomparable: 'Each {naxp} accepts text the other treats as invalid.',
	},
	encoding: {
		Equal: 'Both {naxp}s treat the same texts as valid and give each text the same encoded value. This is the critical test for these {naxp}s.',
		SubsetOf: 'Text accepted by {a} has the same encoded value as {b}, but {b} accepts text that is invalid under {a}. This is the critical test for these {naxp}s.',
		SupersetOf: 'Text accepted by {b} has the same encoded value as {a}, but {a} accepts text that is invalid under {b}. This is the critical test for these {naxp}s.',
		Incomparable: 'At least one text or encoded value means something different under each {naxp}. This is the critical test for these {naxp}s.',
	},
	canonicalForms: {
		Equal: 'The {naxp}s print the same canonical forms.',
		SubsetOf: '{b} prints canonical forms {a} does not.',
		SupersetOf: '{a} prints canonical forms {b} does not.',
		Incomparable: 'Each {naxp} prints canonical forms the other does not.',
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
 * The code of an error, linked to the page that documents it.
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

	link.href = `/error-codes/#${code.toLowerCase()}`;
	link.title = `What ${code} means`;

	return link;
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
 * {@link chip} does, and `{naxp}` as the word in bold.
 *
 * @param {string} sentence The sentence.
 * @returns {Array<Node | string>} The words and the names.
 */
function chipped(sentence) {
	return sentence
		.split(/\{(a|b|naxp)\}/)
		.flatMap((part, at) => {
			if (at % 2 === 0) { return [part]; }

			return part === 'naxp' ? [element('strong', null, 'naxp')] : chip(part);
		});
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
 * The verdict on moving data from one naxp to the other, and why.
 *
 * The encoding relationship says whether every text keeps its encoded value, but not what an
 * encoded value prints as, so the first divergent shared encoded value is read beside it; and
 * a relationship short of safe can still leave stored data decoding as it did, which the two
 * counts settle. The cases are taken in order and the first that holds is the verdict:
 *
 * - Equal or SubsetOf, nothing divergent: safe.
 * - Equal or SubsetOf, something divergent: every text keeps its encoded value, but some encoded
 *   values print differently, so take care.
 * - SupersetOf, the counts equal, nothing divergent: stored data decodes as it did, but the new
 *   naxp refuses some text the old one took, so take care.
 * - Anything else is not safe, for the first of three reasons that applies: an encoded value
 *   decodes differently, an encoded value is gone, or some text has a different encoded value.
 *
 * @param {Naxp} a The naxp the data was encoded with.
 * @param {Naxp} b The naxp proposed to replace it.
 * @param {NaxpComparison} comparison The comparison of the two.
 * @param {bigint} divergent The first divergent shared encoded value, or 0 for none.
 * @returns {HTMLElement} The verdict line.
 */
function verdict(a, b, comparison, divergent) {
	const ok = (sentence) => statusLine('ok', 'Safe', ...chipped(sentence));
	const warn = (sentence) => statusLine('warn', 'Take care', ...chipped(sentence));
	const bad = (sentence) => statusLine('bad', 'Not safe', ...chipped(sentence));
	const { encoding } = comparison;

	if (encoding === SetRelationship.Equal) {
		return divergent === 0n
			? ok('The {naxp}s are completely equivalent.')
			: warn('The {naxp}s encode text identically, but they decode at least some encoded values '
				+ 'to different text.');
	}

	if (encoding === SetRelationship.SubsetOf) {
		return divergent === 0n
			? ok('Every encoded value already stored under {a} decodes to the same text under {b}. '
				+ 'Note that {b} accepts text that {a} did not.')
			: warn('{b} encodes the text {a} accepted identically, but {b} (a) accepts text that {a} '
				+ 'did not, and (b) decodes at least some existing encoded values to different text.');
	}

	if (encoding === SetRelationship.SupersetOf
		&& a.maxEncodedValue === b.maxEncodedValue
		&& divergent === 0n) {
		return warn('Stored encoded values decode the same, but {b} rejects text that {a} accepted.');
	}

	if (divergent !== 0n) {
		return bad('Some encoded values stored using {a} decode to different text under {b}.');
	}

	if (b.maxEncodedValue < a.maxEncodedValue) {
		return bad('Some encoded values stored using {a} are invalid under {b}.');
	}

	return bad('Although stored encoded values decode the same, some text will be encoded '
		+ 'differently by {b}, so new data may not match old.');
}

/* ---------- parsing the two boxes ---------- */

/**
 * Parses one box, reporting the error where it will not parse.
 *
 * @param {HTMLTextAreaElement} box The box.
 * @param {string} name What the box is called, `a` or `b`.
 * @returns {{naxp: Naxp | null, error: HTMLElement | null}} The naxp, or a line saying why not.
 */
function read(box, name) {
	const pattern = box.value;

	if (pattern.trim() === '') {
		return {
			naxp: null,
			error: statusLine('idle', null, ...chipped(`Write {${name}} to begin.`)),
		};
	}

	const result = Naxp.tryParse(pattern);

	if (result.naxp !== null) { return { naxp: result.naxp, error: null }; }

	return {
		naxp: null,
		error: statusLine(
			'bad',
			'Invalid',
			...chip(name),
			': ',
			codeLink(result.errorCode),
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
 * @param {bigint} divergent The first divergent shared encoded value, or 0 for none.
 * @returns {NaxpComparison | null} The comparison, or null where it could not be decided.
 */
function showComparison(a, b, divergent) {
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
				'These two naxps are too tangled to compare.'));

		fill(dom.axes);
		dom.axesTable.hidden = true;
		fill(dom.counts);

		return null;
	}

	dom.axesTable.hidden = false;

	fill(dom.verdict, verdict(a, b, comparison, divergent));

	const axes = [
		['Accepted text', comparison.acceptedText, MEANING.acceptedText],
		['Encoding', comparison.encoding, MEANING.encoding],
		['Canonical forms', comparison.canonicalForms, MEANING.canonicalForms],
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

	return comparison;
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
 * What to say when no shared encoded value decodes differently, which depends on how else the
 * two differ: not at all, in their counts, or only in the text they accept.
 *
 * The first divergent value has no budget, so it is known even where the comparison is not;
 * what is then unknown is whether they differ in any other way.
 *
 * @param {Naxp} a The first naxp.
 * @param {Naxp} b The second naxp.
 * @param {NaxpComparison | null} comparison The comparison, or null where it was undecided.
 * @returns {Array<Node | string>} The sentence.
 */
function sameDecoding(a, b, comparison) {
	if (comparison === null) {
		return [
			'Shared encoded values decode the same. But the comparison above is undecided, so it '
			+ 'is unknown how else the two differ.'];
	}

	if (comparison.acceptedText === SetRelationship.Equal
		&& comparison.encoding === SetRelationship.Equal
		&& comparison.canonicalForms === SetRelationship.Equal) {
		return ['These naxps are completely equivalent.'];
	}

	if (a.maxEncodedValue !== b.maxEncodedValue) {
		const [more, fewer] = b.maxEncodedValue > a.maxEncodedValue ? ['b', 'a'] : ['a', 'b'];

		return chipped(
			'The decoded text is the same for the shared encoded values, but '
			+ `{${more}} produces more encoded values than {${fewer}}. See the above table for details.`);
	}

	return [
		'The valid encoded values and matching decoded texts are the same for both naxps. But they '
		+ 'differ in other respects – see the above table for details.'];
}

/**
 * Fills the band that names the lowest value the two decode differently.
 *
 * @param {Naxp} a The first naxp.
 * @param {Naxp} b The second naxp.
 * @param {NaxpComparison | null} comparison The comparison, or null where it was undecided.
 * @param {bigint} value The first divergent shared encoded value, or 0 for none.
 */
function showDivergence(a, b, comparison, value) {
	if (value === 0n) {
		fill(dom.divergence, ...sameDecoding(a, b, comparison));

		fill(dom.divergenceRows);
		dom.divergenceTable.hidden = true;

		return;
	}

	// At 1 there is nothing below it, so saying so would be saying nothing.
	fill(
		dom.divergence,
		'The lowest shared encoded value that decodes differently is ',
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

	showStandard(dom.aStandard, left.naxp, STANDARDS);
	showStandard(dom.bStandard, right.naxp, STANDARDS);

	if (left.naxp === null || right.naxp === null) {
		// Both errors are shown. Somebody who has pasted two naxps in wants to be told about
		// both of them, not sent back a second time.
		fill(dom.verdict, ...[left.error, right.error].filter((line) => line !== null));

		fill(dom.axes);
		dom.axesTable.hidden = true;
		fill(dom.counts);
		fill(dom.divergence);
		fill(dom.divergenceRows);
		dom.divergenceTable.hidden = true;

		return;
	}

	// Read once, since the verdict and the section further down both turn on it. It has no
	// budget, so it is known even where the comparison is not.
	const divergent = Naxp.firstDivergentValue(left.naxp, right.naxp);
	const comparison = showComparison(left.naxp, right.naxp, divergent);

	showDivergence(left.naxp, right.naxp, comparison, divergent);
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
