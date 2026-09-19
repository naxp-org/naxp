// Inlined at <!-- restore --> on every page with remembered boxes, after the boxes and before
// the first paint, by the transform in eleventy.config.js.
//
// The page's module restores the same values again when it runs, but a module loads after the
// page has painted, and a reader coming back to the page saw the boxes' defaults for a frame
// before their own text replaced them. This puts the memory in first. It reads the same keys
// boxes.js does, from the data-remember attribute the module also reads, and fits a textarea to
// its lines the way boxes.js does so that the box does not grow when the module gets to it.
//
// The results under the boxes are the module's to draw, so they cannot be put back here. What
// can be is their room: main is held at the height it had when the reader left, kept by
// reserveHeight in boxes.js, so the footer paints where it will end up rather than high and
// then dropping as the results arrive. The module lets go of the height once it has drawn.
(function ()
{
	var main = document.querySelector('main[data-reserve]');
	var boxes = document.querySelectorAll('[data-remember]');

	if (main !== null)
	{
		var height = null;

		try { height = sessionStorage.getItem('naxp.height.' + main.dataset.reserve); }
		catch (e) { }

		if (height !== null) { main.style.minHeight = height + 'px'; }
	}

	for (var i = 0; i < boxes.length; i++)
	{
		var box = boxes[i];
		var kept = null;

		try { kept = sessionStorage.getItem('naxp.' + box.dataset.remember); }
		catch (e) { }

		if (kept === null) { continue; }

		box.value = kept;

		if (box.tagName === 'TEXTAREA' && kept.length > 0)
		{
			box.style.height = 'auto';
			box.style.height = (box.scrollHeight + box.offsetHeight - box.clientHeight) + 'px';
		}
	}
})();
