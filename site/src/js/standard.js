// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// Opens the standard a link or a fragment points at. Each standard on
// /standard/ is a <details> that starts closed, and a browser scrolls to a
// closed one without opening it, which leaves the reader looking at a heading
// with the thing they asked for still hidden under it.
//
// Without script the sections still open by hand, and find in page opens one
// whose text it matches in the browsers that support that.

// Each standard was once a section of this page under a longer id, and links to
// those are out in the world. Two of the four kept their names.
const renamed = {
	'ireland-eircode': 'eircode',
	'netherlands-postcode': 'nl-postcode',
};

/**
 * Opens the standard a fragment names and brings it into view.
 *
 * @param {string} hash The fragment, with its '#', as location.hash gives it.
 */
function reveal(hash) {
	const id = decodeURIComponent(hash.slice(1));
	const target = document.getElementById(renamed[id] ?? id);

	if (!(target instanceof HTMLDetailsElement)) { return; }

	if (renamed[id] !== undefined) { history.replaceState(null, '', `#${renamed[id]}`); }

	target.open = true;
	target.scrollIntoView();
}

// A click on a link to a section already named in the address fires no
// hashchange, and the reader may have closed it since, so the click opens it.
document.addEventListener('click', (event) => {
	const link = event.target instanceof Element ? event.target.closest('a[href^="#"]') : null;

	if (link !== null) { reveal(link.getAttribute('href')); }
});

window.addEventListener('hashchange', () => reveal(location.hash));

reveal(location.hash);
