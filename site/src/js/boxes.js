// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * What every text box on the site shares, so that the pattern boxes, the test text and the
 * encoded value behave alike wherever they appear.
 *
 * A box grows to fit its lines rather than scrolling or being dragged: a new line adds a line to
 * the box. Each carries a small copy button in its top right corner, the same button the
 * generated code box has. And each remembers what it holds for as long as the tab is open, so
 * that a reader who leaves a page to check something and comes back finds it as they left it.
 * The box sits inside a `field__stack`, which is what positions the button and, on the pattern
 * boxes, the layer that marks a fault.
 */

/** The copy icon, two sheets with one behind the other. */
const COPY_ICON = '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" '
	+ 'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
	+ '<rect x="5.5" y="5.5" width="8" height="8" rx="1.5"/>'
	+ '<path d="M10.5 5.5V3.5a1 1 0 0 0-1-1h-6a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2"/></svg>';

/** The tick that stands in for it once the copy has happened. */
const DONE_ICON = '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="2" '
	+ 'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 8.5l3 3 7-7"/></svg>';

/** How long the tick shows for. */
const DONE_FOR = 1500;

/** What every remembered value is stored under, ahead of the page's own key. */
const STORE = 'naxp.';

/**
 * What a control held when the reader last left it, or null where nothing was kept.
 *
 * Session storage rather than local: it is one tab's memory, gone when the tab closes, never
 * shared with another tab and never sent anywhere. A browser that refuses it simply gives a
 * page that starts afresh.
 *
 * Exported with {@link keep} for the one page whose settings are not controls of their own, the
 * language chips on the code generator.
 *
 * @param {string} key The page's key for the control.
 * @returns {string | null} The value.
 */
export function recall(key) {
	try { return sessionStorage.getItem(STORE + key); }
	catch { return null; }
}

/**
 * Keeps a control's value for the tab's lifetime.
 *
 * @param {string} key The page's key for the control.
 * @param {string} value The value.
 */
export function keep(key, value) {
	try { sessionStorage.setItem(STORE + key, value); }
	catch { /* Nothing to be done, and nothing lost but the memory. */ }
}

/**
 * Gives a control the memory alone: restored on arrival, kept on every change. For the controls
 * beside the text boxes, such as a prefix or a choice of type.
 *
 * The key is the control's `data-remember` attribute, which is also what the inline script every
 * page carries reads to put the memory back before the first paint. One attribute, two readers,
 * so they cannot drift.
 *
 * @param {HTMLInputElement | HTMLSelectElement} control The control.
 * @returns {boolean} Whether a value was restored.
 */
export function remember(control) {
	const key = keyOf(control);
	const kept = recall(key);

	if (kept !== null) { control.value = kept; }

	control.addEventListener('input', () => keep(key, control.value));
	control.addEventListener('change', () => keep(key, control.value));

	return kept !== null;
}

/**
 * Fits a box's height to its content, so that it shows every line and no scrollbar.
 *
 * @param {HTMLTextAreaElement} box The box.
 */
export function fit(box) {
	// Collapsing it first is what lets it shrink when lines are deleted. The border is added back
	// because the box is sized to its border edge and scrollHeight stops at the padding.
	box.style.height = 'auto';

	// An empty box keeps its one row: its placeholder counts towards scrollHeight, and a long
	// one wraps in a narrow box, which would size the box to a hint rather than to its text.
	if (box.value.length === 0) { return; }

	box.style.height = `${box.scrollHeight + box.offsetHeight - box.clientHeight}px`;
}

/**
 * Puts a copy button in the top right corner of a container, copying whatever the callback
 * returns at the time.
 *
 * @param {HTMLElement} container The element the button sits in, which positions it.
 * @param {() => string} text What to copy.
 * @param {(() => void) | null} fallback What to do where the clipboard is refused, or null.
 * @returns {HTMLButtonElement} The button, so the caller can disable it while there is nothing to copy.
 */
export function copyButton(container, text, fallback = null) {
	const button = document.createElement('button');
	let timer = 0;

	button.type = 'button';
	button.className = 'copy';
	button.title = 'Copy';
	button.setAttribute('aria-label', 'Copy');
	button.innerHTML = COPY_ICON;

	button.addEventListener('click', async () => {
		let said = 'Copied';

		try {
			await navigator.clipboard.writeText(text());
			button.innerHTML = DONE_ICON;
		} catch {
			// A browser that refuses the clipboard, or a page served in a way it will not trust.
			// The best that can be done is to leave the text selected for the reader to copy.
			said = 'Select the text and copy it';

			if (fallback !== null) { fallback(); }
		}

		button.title = said;
		clearTimeout(timer);
		timer = setTimeout(() => {
			button.innerHTML = COPY_ICON;
			button.title = 'Copy';
		}, DONE_FOR);
	});

	container.append(button);

	return button;
}

/**
 * Keeps the page's height for its next visit, and lets go of the height the inline script held
 * it at for this one.
 *
 * The inline script every page carries sets a minimum height on `main` before the first paint,
 * from the height it had when the reader last left, so that the footer does not paint high and
 * then drop as the module fills the results in. The module calls this once it has drawn: the
 * height is released once the module has drawn, by which time the real content holds the page up, and
 * from then on the page keeps its height on every change and on leaving.
 *
 * @param {HTMLElement} main The page's `main`, carrying `data-reserve`.
 */
export function reserveHeight(main) {
	const key = `height.${main.dataset.reserve}`;
	const keepHeight = () => keep(key, String(main.offsetHeight));

	// A timeout rather than an animation frame, which a background tab never gets: the module's
	// first draw is done by the time this runs, and that is all the release needs.
	setTimeout(() => {
		main.style.minHeight = '';
		keepHeight();
	}, 0);

	window.addEventListener('pagehide', keepHeight);
	document.addEventListener('visibilitychange', () => { if (document.hidden) { keepHeight(); } });
}

/**
 * The key a control is remembered under, from its `data-remember` attribute.
 *
 * @param {HTMLElement} control The control.
 * @returns {string} The key.
 */
function keyOf(control) {
	const key = control.dataset.remember;

	if (key === undefined) { throw new Error(`${control.id || control.tagName} has no data-remember.`); }

	return key;
}

/**
 * Gives a text box the shared treatment: it fits its lines, it has a copy button that is
 * disabled while the box is empty, and it is remembered for the tab's lifetime under its
 * `data-remember` key, which the inline script every page carries reads too.
 *
 * Whatever a page then sets from code - a naxp in the link, an example - wins over the memory,
 * because it comes later and is what the reader just asked for.
 *
 * @param {HTMLTextAreaElement | HTMLInputElement} box The box, inside its `field__stack`.
 * @returns {() => void} What to call after setting the box's value from code, which fires no event.
 */
export function textBox(box) {
	const grows = box instanceof HTMLTextAreaElement;
	const button = copyButton(box.parentElement, () => box.value, () => box.select());
	const key = keyOf(box);
	const kept = recall(key);

	if (kept !== null) { box.value = kept; }

	const sync = () => {
		if (grows) { fit(box); }

		button.disabled = box.value.length === 0;
		keep(key, box.value);
	};

	box.addEventListener('input', sync);

	if (grows) {
		// Where the lines wrap changes with the width, and the metrics change when the font arrives.
		window.addEventListener('resize', sync);
		document.fonts.ready.then(sync);
	}

	sync();

	return sync;
}
