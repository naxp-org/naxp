// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The header's menu button, which on a phone opens the page links in a panel
// under the bar. The stylesheet decides when the button shows and where the
// panel goes; this only keeps the button's state and the header's class in
// step, and closes the panel when the reader is plainly done with it.
//
// It also looks after the header's own menus, such as Standard's. Each is a
// <details> and opens and shuts without help; this shuts one when the reader
// is plainly done with it, and on a phone, where the stylesheet shows a menu
// as part of the panel's list, keeps it open while the panel is.
//
// A link in the panel loads another page, so following one needs no handling
// here: the new page arrives with its panel shut.
//
// Loaded as a module, which gives it a scope of its own; theme.js is a classic
// script and its top-level names are the page's, so a second `button` would
// collide with it.

const button = document.getElementById('menu');
const header = button.closest('.siteheader');
const menus = [...header.querySelectorAll('.navmenu')];

/** The widths at which the links fold into the panel, as the stylesheet has them. */
const phone = window.matchMedia('(max-width: 63.99rem)');

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
 * Opens or shuts the panel, and the header's menus with it.
 *
 * @param {boolean} open
 */
function set(open) {
	button.setAttribute('aria-expanded', String(open));
	header.classList.toggle('siteheader--open', open);

	const action = open ? 'Close menu' : 'Menu';

	button.setAttribute('aria-label', action);
	button.title = action;

	for (const menu of menus) { menu.open = open; }
}

/** Shuts every header menu open in the bar. */
function shutMenus() {
	for (const menu of menus) { menu.open = false; }
}

button.addEventListener('click', () => set(!isOpen()));

// Escape shuts the panel or a menu and puts focus back where it came from, as a
// dialog would.
document.addEventListener('keydown', (event) => {
	if (event.key !== 'Escape') { return; }

	if (isOpen()) {
		set(false);
		button.focus();

		return;
	}

	const open = menus.find((menu) => menu.open);

	if (open !== undefined) {
		open.open = false;
		open.querySelector('summary').focus();
	}
});

// A press anywhere outside the header shuts the panel, so the page beneath does
// not have to be reached round it; and a press outside a menu in the bar shuts
// that menu.
document.addEventListener('click', (event) => {
	if (isOpen()) {
		if (!header.contains(event.target)) { set(false); }

		return;
	}

	for (const menu of menus) {
		if (menu.open && !menu.contains(event.target)) { menu.open = false; }
	}
});

// Crossing the fold either way starts from a closed header, so that a menu
// opened as part of the panel does not hang open over the page in the bar.
phone.addEventListener('change', () => {
	set(false);
	shutMenus();
});
