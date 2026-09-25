# naxp.org

Source for the naxp website. Built with [Eleventy](https://www.11ty.dev/) and
deployed to GitHub Pages.

## Look at it without installing anything

The landing page needs no build step. It carries a few lines of JavaScript, for
the theme button and nothing else, so it reads fine without any. Serve this
folder and open it:

```bash
python -m http.server 8000 --directory src
```

Then go to <http://localhost:8000/>. The wordmark, the favicon, the social card
and the **naxp** library the developer page runs on will all be missing:
Eleventy builds or copies those in from outside this folder, so only a proper
build has them.

## Build it properly

Needs Node 20 or newer.

```bash
npm install
npm start
```

That serves the whole site at <http://localhost:8080/> and rebuilds on save.
`npm run build` writes a deployable copy to `_site/`.

## Layout

| Path | Contents |
| --- | --- |
| `src/index.html` | Landing page, plain HTML |
| `src/dev.html` | Interactive developer page, published at `/dev/` |
| `src/_includes/` | Nunjucks layouts |
| `src/css/naxp.css` | The whole stylesheet |
| `src/js/` | The site's own scripts, served from `/js/` |
| `src/js/theme.js` | The header's light / dark / auto button |
| `src/CNAME` | The custom domain |

Brand assets are not held here. `eleventy.config.js` copies them straight from
`../brand/` to `/img/`, so that folder stays the only copy. They were once kept
as a second copy under `src/img/` and went stale, which is why they are not.

The wordmark works differently again. It has to be inlined, so the letterforms
can take `currentColor` in dark mode while the underline keeps the brand
magenta, and an `<img>` cannot do that. So `buildWordmark` in
`eleventy.config.js` reads `brand/naxp-logo-clear.svg` at build time and
substitutes it for the `<!-- wordmark -->` marker in `src/index.html`. Redraw
the brand file and the page follows; there is nothing to copy across.

The crop is measured from the drawing rather than taken from the blue guide
rectangle the brand file draws in its own comment, because a redraw can outgrow
its guide and has. `pathPoints` collects every point each path visits, control
points included, which bounds the curves without having to solve them.

## The developer page

`/dev/` parses a naxp, encodes strings and decodes values, all in the browser.
It runs the JavaScript reference implementation itself: `eleventy.config.js`
copies `../src/js/lib` to `/naxp/`, so the files served are the files the npm
package ships and there is no second copy to keep in step. The library is plain
ESM with no Node dependencies, and no bundler is involved.

## Versions

**Version 0.10 is the current specification.** It is authored once, as
`spec/naxp-v0.10.md` at the root of the repository, and nothing under this
folder is edited to publish it: `eleventy.config.js` writes `src/spec/` from
`../spec/` on every build, adding the `version` and `permalink` front matter
that publishes `naxp-v0.10.md` at `/spec/v0.10/`. `src/spec/` is ignored by
git. Adding a version means adding a file under `spec/`; the version index at
`/spec/versions/` and the `/spec/` redirect both pick it up from the `spec`
collection.

A published version never changes. That is what lets implementations pin to one.

## Light and dark

Three states. With nothing chosen the page follows `prefers-color-scheme`; the
button in the header pins it light or dark by stamping `data-theme` on the root,
and `auto` clears the stamp again. The choice is kept in `localStorage` under
`naxp-theme`.

Applying it is split in two on purpose. A few lines inline in each `<head>` read
the stored choice and stamp the root **before the first paint**, so a pinned
theme never flashes the other one; `theme.js` then draws the button and handles
the click. The same inline script adds a `js` class to the root, which is what
reveals the button - it is hidden without it, since nothing but script can work
it.

The dark token set is written twice in `naxp.css`, once for the media query and
once for `[data-theme="dark"]`. They must stay identical.

## Fonts

**Lisnoti** sets prose and **Lisnoti Code** sets code. They are self-hosted in
`src/fonts/Lisnoti-woff2/` and `src/fonts/LisnotiCode-woff2/`, each its release's
own woff2 folder copied unchanged, with its licence beside it. Every page links
`lisnoti.css` and `lisnoti-code.css` ahead of `naxp.css`, and a page fetches only
the subsets its text uses. Every page also preloads `Lisnoti-Regular-latin.woff2`
and `LisnotiCode-Regular-latin.woff2`, so a new release must keep those file
names or each page's preload links must follow them. To take a new release, copy
its woff2 folder over the old one.

Code has `calt` on, for Lisnoti Code's narrow single space and short hyphen, and
`liga` off; `naxp.css` explains why that takes `font-feature-settings`, and why
WebKit (Safari and every iPhone browser) has `calt` off as well. Lisnoti
Code is proportional, so generated code must use hanging indents and must never
align to an opening delimiter.

## Deployment

`.github/workflows/pages.yml` at the repository root builds this folder on
every push to `main` and deploys it to GitHub Pages. The site is served from
the public `naxp` repository itself, so there is no separate site repository:
Pages will serve a custom domain from any repository, and `naxp.org` is one.
For the custom domain, point four `A` records at GitHub's Pages addresses for
the apex and a `CNAME` record for `www`, then turn on Enforce HTTPS once the
certificate has been issued.
