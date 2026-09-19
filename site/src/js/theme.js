// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The header's theme button. Three states rather than two, so a reader who has
// pinned the page can put it back to following the system.
//
// The icon shows the state the page is in, not the state a press would give.
// With three states a press has no single destination to draw, and the button
// doubles as the only indication of which state is in force. What a press does
// is in the label and the tooltip instead, which also makes the current state
// plain: only one of the three states leaves 'Switch to light mode' to offer.
//
// Setting the theme is not done here. It has to happen before the first paint
// or the page flashes the wrong colours, so a few lines inline in each <head>
// read the stored choice and stamp the root. This file only draws the button
// and handles the click.

/** The stored choice. Shared with the inline script in each page's head. */
const KEY = 'naxp-theme';

/** What the button offers, in the order it cycles through them. */
const STATES = ['auto', 'light', 'dark'];

const ICONS = {
	// A circle half filled: neither one thing nor the other, which is the state.
	auto: '<circle cx="8" cy="8" r="6.2" fill="none" stroke="currentColor" stroke-width="1.6"/>'
		+ '<path d="M8 1.8a6.2 6.2 0 0 1 0 12.4Z"/>',
	light: '<circle cx="8" cy="8" r="3.1"/>'
		+ '<path d="M8 0.4v2.1M8 13.5v2.1M0.4 8h2.1M13.5 8h2.1M2.6 2.6l1.5 1.5'
		+ 'M11.9 11.9l1.5 1.5M13.4 2.6l-1.5 1.5M4.1 11.9l-1.5 1.5" '
		+ 'fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>',
	dark: '<path d="M13.6 10.4A6.2 6.2 0 1 1 5.6 2.4 5.2 5.2 0 0 0 13.6 10.4Z"/>',
};

/** What a press does, keyed by the state the page is in now. */
const ACTIONS = {
	auto: 'Switch to light mode',
	light: 'Switch to dark mode',
	dark: 'Switch to system preference',
};

const button = document.getElementById('theme');

/**
 * The state the page is in, which the inline script has already applied.
 *
 * @returns {string} One of {@link STATES}.
 */
function current() {
	const stamped = document.documentElement.dataset.theme;

	return STATES.includes(stamped) ? stamped : 'auto';
}

/**
 * Applies a state and draws the button to match.
 *
 * @param {string} state One of {@link STATES}.
 */
function apply(state) {
	if (state === 'auto') { delete document.documentElement.dataset.theme; }
	else { document.documentElement.dataset.theme = state; }

	try {
		if (state === 'auto') { localStorage.removeItem(KEY); }
		else { localStorage.setItem(KEY, state); }
	}
	catch {
		// A browser refusing storage costs the reader the choice on the next
		// page and nothing else, so there is nothing to report.
	}

	button.innerHTML = `<svg viewBox="0 0 16 16" aria-hidden="true">${ICONS[state]}</svg>`;
	button.setAttribute('aria-label', ACTIONS[state]);
	button.title = ACTIONS[state];
}

if (button !== null) {
	apply(current());

	button.addEventListener('click', () => {
		apply(STATES[(STATES.indexOf(current()) + 1) % STATES.length]);
	});
}
