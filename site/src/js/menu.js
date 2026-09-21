// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The header's menu button, which on a phone opens the page links in a panel
// under the bar. The stylesheet decides when the button shows and where the
// panel goes; this only keeps the button's state and the header's class in
// step, and closes the panel when the reader is plainly done with it.
//
// A link in the panel loads another page, so following one needs no handling
// here: the new page arrives with its panel shut.
//
// Loaded as a module, which gives it a scope of its own; theme.js is a classic
// script and its top-level names are the page's, so a second `button` would
// collide with it.

const button = document.getElementById('menu');
const header = button.closest('.siteheader');

/**
 * Whether the panel is open, read from the button so that the state has one
 * home and assistive technology sees the same answer.
 *
 * @returns {boolean}
 */
function isOpen() {
	return button.getAttribute('aria-expanded') === 'true';
}

/**
 * Opens or shuts the panel.
 *
 * @param {boolean} open
 */
function set(open) {
	button.setAttribute('aria-expanded', String(open));
	header.classList.toggle('siteheader--open', open);

	const action = open ? 'Close menu' : 'Menu';

	button.setAttribute('aria-label', action);
	button.title = action;
}

button.addEventListener('click', () => set(!isOpen()));

// Escape shuts it and puts focus back where it came from, as a dialog would.
document.addEventListener('keydown', (event) => {
	if (event.key === 'Escape' && isOpen()) {
		set(false);
		button.focus();
	}
});

// A press anywhere outside the header shuts it, so the page beneath does not
// have to be reached round an open panel.
document.addEventListener('click', (event) => {
	if (isOpen() && !header.contains(event.target)) {
		set(false);
	}
});
