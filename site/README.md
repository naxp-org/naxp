# naxp.org

Source for the naxp website. Built with [Eleventy](https://www.11ty.dev/) and
deployed to GitHub Pages.

## Look at it without installing anything

The landing page has no build step and no JavaScript. Serve this folder and open
it:

```bash
python -m http.server 8000 --directory src
```

Then go to <http://localhost:8000/>. The three card icons will be missing:
Eleventy copies those in from `../icons/`, so only a proper build has them.

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
| `src/_includes/` | Nunjucks layouts |
| `src/css/naxp.css` | The whole stylesheet |
| `src/img/` | Brand assets, copied from the `brand/` folder |
| `src/CNAME` | The custom domain |

## Versions

The site publishes no specification yet. **Version 1 will be the first release**,
and the versions worked through so far are development documents held back from
this repository along with the pages that render them, `src/spec/`, `versions.njk`
and `latest.njk`.

When v1 lands, each file in `src/spec/` carries `version` and `permalink` in its
front matter, so `naxp-v1.md` is published at `/v1/`. Adding a version means
adding a file; the version index and the `/latest/` redirect both pick it up from
the `spec` collection.

A published version never changes. That is what lets implementations pin to one.

## Fonts

The stylesheet asks for **Lisnoti** by family name, which resolves against a
local install during development. Before deployment, replace the `@font-face`
rules at the top of `naxp.css` with self-hosted WOFF2 subsets split by
`unicode-range`, and preload the regular Latin file.

Lisnoti is used for body text, inline naxp literals and code alike, so generated
code must use hanging indents and must never align to an opening delimiter.

## Deployment

`.github/workflows/pages.yml` builds on every push to `main` and deploys to
GitHub Pages. For the custom domain, point four `A` records at GitHub's Pages
addresses for the apex and a `CNAME` record for `www`, then turn on Enforce
HTTPS once the certificate has been issued.
