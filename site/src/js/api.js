// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * The language tabs on /api/.
 *
 * Which panel shows is decided before the first paint by the inline script in the page's head,
 * which sets `data-language` on the root element for the stylesheet to act on. This module only
 * follows on from there: it marks the tab that matches, and moves the choice when a tab is
 * pressed. Without it every panel shows, one after another, each under its own name.
 *
 * The choice is shared with the code generator's language chips, under the same key and the same
 * names, so that a reader who picks C# on one page finds C# on the other. It also goes into the
 * address, so that a link such as /api/#csharp opens on that language.
 */

import { copyButton, keep } from './boxes.js';

/** Each panel's language, as its tab and the code generator's chip name it. */
const NAMES = { c: 'C', cpp: 'C++', csharp: 'C#', javascript: 'JavaScript', r: 'R' };

const root = document.documentElement;
const header = document.querySelector('.siteheader');
const switcher = document.querySelector('.api-switcher');
const tablist = document.getElementById('api-languages');
const tabs = [...tablist.querySelectorAll('[role="tab"]:not([aria-disabled="true"])')];

/** How far down the window the tabs stick: under the site header, or at the top where it does not stick. */
let tabsTop = 0;

/**
 * Measures where the tabs stick and tells the stylesheet. The header's height is the fonts' to
 * decide, and it only sticks on a screen tall enough to spare it, so this is measured rather than
 * written into the stylesheet, and measured again whenever the header changes size.
 */
function placeTabs() {
	tabsTop = header !== null && getComputedStyle(header).position === 'sticky'
		? header.getBoundingClientRect().height
		: 0;

	root.style.setProperty('--api-tabs-top', `${tabsTop}px`);
}

/**
 * Shows a language's panel and marks its tab.
 *
 * @param {string} language The language, as a key of {@link NAMES}.
 */
function show(language) {
	root.dataset.language = language;

	for (const tab of tabs) {
		const selected = tab.dataset.language === language;

		tab.setAttribute('aria-selected', String(selected));
		tab.tabIndex = selected ? 0 : -1;
	}
}

/**
 * Shows a language the reader chose, and remembers it.
 *
 * @param {HTMLElement} tab The tab chosen.
 */
function choose(tab) {
	show(tab.dataset.language);
	keep('generate.language', NAMES[tab.dataset.language]);
	history.replaceState(null, '', `#${tab.dataset.language}`);

	// Chosen from tabs held at the top part way down a panel, the new panel would open part way
	// down too, wherever the old one's length happened to leave it. So it opens at its start.
	const above = switcher.getBoundingClientRect().top - tabsTop;

	if (above < 0) { window.scrollBy(0, above); }
}

for (const tab of tabs) {
	tab.addEventListener('click', () => choose(tab));
}

// The arrow keys move along the tabs, skipping the languages not yet offered, and Home and End go
// to either end, as a tab list does everywhere else.
tablist.addEventListener('keydown', (event) => {
	const at = tabs.indexOf(document.activeElement);

	if (at < 0) { return; }

	const to = {
		ArrowLeft: (at + tabs.length - 1) % tabs.length,
		ArrowRight: (at + 1) % tabs.length,
		Home: 0,
		End: tabs.length - 1
	}[event.key];

	if (to === undefined) { return; }

	event.preventDefault();
	choose(tabs[to]);
	tabs[to].focus();
});

// A link to another language from within the page, or the reader editing the address.
window.addEventListener('hashchange', () => {
	const language = location.hash.slice(1).toLowerCase();

	if (Object.hasOwn(NAMES, language)) { show(language); }
});

for (const listing of document.querySelectorAll('.listing')) {
	copyButton(listing, () => listing.querySelector('pre').textContent);
}

if (header !== null) { new ResizeObserver(placeTabs).observe(header); }

window.addEventListener('resize', placeTabs);

placeTabs();
show(root.dataset.language);
