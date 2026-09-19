import fs from "node:fs";
import path from "node:path";

// The specification is authored once, as spec/naxp-v<version>.md at the root
// of the repository, and nothing under this folder is edited by hand to
// publish it. This writes the page Eleventy builds from: the same text with
// the front matter it needs on top, into src/spec/, which is ignored by git.
// It runs when the configuration loads, so that latestVersion below and the
// spec collection both see the pages, and again before every build so that a
// watch picks up an edit on its next rebuild.
//
// The drafts under spec/drafts/ are not published and are not read here.
//
// The page is written rather than pointed at, because pointing Eleventy at a
// path containing `..` is the trap described beside the brand assets below.
function publishSpecifications(configDirectory)
{
  const source = path.join(configDirectory, "..", "spec");
  const target = path.join(configDirectory, "src", "spec");

  if (!fs.existsSync(source)) { return; }

  fs.mkdirSync(target, { recursive: true });

  for (const name of fs.readdirSync(source))
  {
    const found = name.match(/^naxp-v(\d+\.\d+)\.md$/);

    if (found === null) { continue; }

    const version = found[1];
    const text = fs.readFileSync(path.join(source, name), "utf8");
    const heading = text.match(/^# (.+)$/m);
    // An HTML comment on the heading line, such as the editor's `omit from toc`
    // marker, is not part of the title.
    const title = heading === null
      ? `naxp spec v${version}`
      : heading[1].replace(/<!--[\s\S]*?-->/g, "").trim();
    const page = [
      "---",
      "layout: spec.njk",
      `title: "${title}"`,
      `version: "${version}"`,
      "tags: spec",
      `permalink: /spec/v${version}/index.html`,
      "---",
      "",
      text
    ].join("\n");

    const destination = path.join(target, name);

    if (!fs.existsSync(destination) || fs.readFileSync(destination, "utf8") !== page)
    {
      fs.writeFileSync(destination, page);
    }
  }
}

// The newest specification version, for the site header. Read from the front
// matter of src/spec rather than written down anywhere, so it follows the
// collection and cannot be left behind by a new version.
//
// Null where there is no specification to point at.
function latestVersion(configDirectory)
{
  const directory = path.join(configDirectory, "src", "spec");

  if (!fs.existsSync(directory)) { return null; }

  const versions = fs.readdirSync(directory)
    .filter((name) => name.endsWith(".md"))
    .map((name) => fs.readFileSync(path.join(directory, name), "utf8").match(/^version:\s*"([^"]+)"/m))
    .filter((found) => found !== null)
    .map((found) => found[1])
    .sort((a, b) => b.localeCompare(a, undefined, { numeric: true }));

  return versions.length === 0 ? null : versions[0];
}

// Every point a path visits, control points included. The bounding box of a
// quadratic Bezier is contained by the box of its endpoints and its control
// point, so collecting those and taking their extremes gives a box that always
// contains the curve. It can be a shade generous and it can never clip, which
// is the right way round for a crop.
//
// Throws on a command it does not know rather than skipping it, so a redrawn
// logo using something new fails the build instead of losing a letter.
function pathPoints(d)
{
  const tokens = d.match(/[A-Za-z]|-?\d*\.?\d+(?:e[-+]?\d+)?/gi) || [];
  const points = [];

  let at = 0;
  let command = "";
  let x = 0;
  let y = 0;
  let startX = 0;
  let startY = 0;

  const number = () => Number(tokens[at++]);
  const visit = (px, py) => { points.push([px, py]); };

  while (at < tokens.length)
  {
    if (/[A-Za-z]/.test(tokens[at])) { command = tokens[at++]; }

    switch (command)
    {
      case "M": x = number(); y = number(); startX = x; startY = y; visit(x, y); command = "L"; break;
      case "m": x += number(); y += number(); startX = x; startY = y; visit(x, y); command = "l"; break;
      case "L": x = number(); y = number(); visit(x, y); break;
      case "l": x += number(); y += number(); visit(x, y); break;
      case "H": x = number(); visit(x, y); break;
      case "h": x += number(); visit(x, y); break;
      case "V": y = number(); visit(x, y); break;
      case "v": y += number(); visit(x, y); break;
      case "Q": { const cx = number(); const cy = number(); visit(cx, cy); x = number(); y = number(); visit(x, y); break; }
      case "q": { const cx = x + number(); const cy = y + number(); visit(cx, cy); x += number(); y += number(); visit(x, y); break; }
      case "C": { for (let i = 0; i < 2; ++i) { visit(number(), number()); } x = number(); y = number(); visit(x, y); break; }
      case "c": { for (let i = 0; i < 2; ++i) { visit(x + number(), y + number()); } x += number(); y += number(); visit(x, y); break; }
      case "Z": case "z": x = startX; y = startY; visit(x, y); break;
      default: throw new Error(`buildWordmark does not know the path command '${command}'.`);
    }
  }

  return points;
}

// The wordmark and the 'n' icon are both inlined rather than linked, because
// their letterforms take currentColor and so follow the theme while the
// underline keeps the brand magenta. An <img> cannot do that: a referenced SVG
// is a separate document and sees none of this page's CSS.
//
// Both are built from the brand folder at build time rather than pasted in,
// because pasting went stale twice: the paths get redrawn and their origins
// move, and nothing in the page says so.
//
// The crop is measured from the drawing rather than taken from the guide
// rectangle in the brand file, because a redraw can outgrow its own guide and
// did.
function buildMark(configDirectory, name, className)
{
  const file = path.join(configDirectory, "..", "brand", name);
  const source = fs.readFileSync(file, "utf8");

  const transform = source.match(/<g transform="([^"]+)">/)[1];
  const letters = [...source.matchAll(/<path d="([^"]+)"\s*\/>/g)].map((m) => m[1]);
  const rects = [...source.matchAll(/<rect x="([\d.]+)" y="([\d.]+)" width="([\d.]+)" height="([\d.]+)"\s*\/>/g)];

  if (letters.length === 0 || rects.length === 0)
  {
    throw new Error(`${file} does not have the shape buildMark expects.`);
  }

  const points = letters.flatMap(pathPoints);

  for (const [, rx, ry, rw, rh] of rects)
  {
    points.push([Number(rx), Number(ry)]);
    points.push([Number(rx) + Number(rw), Number(ry) + Number(rh)]);
  }

  // A unit of air, so the crop is not flush against the ink.
  const margin = 1;
  const left = Math.min(...points.map((q) => q[0])) - margin;
  const top = Math.min(...points.map((q) => q[1])) - margin;
  const right = Math.max(...points.map((q) => q[0])) + margin;
  const bottom = Math.max(...points.map((q) => q[1])) + margin;

  // The group carries the transform, so the crop has to be in the same space.
  const scale = Number(transform.match(/scale\(([\d.]+)\)/)[1]);
  const [shiftX, shiftY] = transform.match(/translate\(([^)]+)\)/)[1].split(",").map(Number);
  const round = (n) => Math.round(n * 100) / 100;

  const viewBox = [
    round(left * scale + shiftX),
    round(top * scale + shiftY),
    round((right - left) * scale),
    round((bottom - top) * scale)
  ].join(" ");

  const paths = letters.map((d) => `      <path d="${d}" />`).join("\n");
  const underline = rects
    .map(([, rx, ry, rw, rh]) => `      <rect x="${rx}" y="${ry}" width="${rw}" height="${rh}" />`)
    .join("\n");

  return [
    `<svg class="${className}" viewBox="${viewBox}" role="img" aria-label="naxp" xmlns="http://www.w3.org/2000/svg">`,
    `  <g transform="${transform}">`,
    `    <g fill="currentColor">`,
    paths,
    `    </g>`,
    `    <g fill="#F0A">`,
    underline,
    `    </g>`,
    `  </g>`,
    `</svg>`
  ].join("\n");
}

// GitHub's heading slug rule, so links written in the markdown against the
// version rendered on GitHub keep working here.
function slugify(headingHtml)
{
  return headingHtml
    .replace(/<[^>]+>/g, "")
    .trim()
    .toLowerCase()
    .replace(/[^\w\s-]/g, "")
    // One hyphen per space, not per run: GitHub keeps both hyphens in the
    // slug of "8.1 `?`: optional", which is 81--optional once the punctuation
    // has gone, and a contents list generated against GitHub links to that.
    .replace(/\s/g, "-")
    .replace(/^-+|-+$/g, "");
}

// The site header on every public page. One template here rather than a copy in
// each page, because the copies drifted apart in content and so in height. Each
// page marks where it goes with <!-- siteheader -->, and the transform stamps
// this in, replacing the link that points at the page itself with an inert
// greyed label.
// The order is the question a reader arrives with. Is one already written? Then
// the three things you do to a naxp of your own - design it, test it, and check
// what changing it would cost - and then the two ways to use one.
//
// Labels are one word each because the site header supplies the context the
// words leave out: the wordmark is three inches to the left and every item is
// about a naxp. A page title carries no such context and is written out in
// full, which is why 'Guidelines' here is 'Design guidelines for new naxps'
// there. One word also keeps the bar on one line on a narrow display, which
// is what drove the shortening.
//
// `hint` is what the label had to drop, shown on hover. It is a description
// rather than a name, so it is a title attribute and not the accessible name;
// the label remains what a screen reader announces. Nothing depends on a
// reader seeing a hint, because a mouse is needed to get one.
//
// A dropdown was considered and turned down: the page it would have hidden,
// /migrate/, is the one nobody could find.
//
// An entry naming a `page` is dropped where that page is not in this
// repository. That is how a page held back from the public site keeps its
// place in the site header here without leaving a dead link there, the same
// way latestVersion returns null where there is no specification to point at.
// When the exclusion comes off copy-to-public.ps1, the header follows on its
// own.
const siteHeaderLinks = [
  { href: "/", label: "Home", hint: "Home page" },
  { href: "/pre-defined/", label: "Pre&#8209;defined", hint: "Pre-written standardised naxps available for immediate use" },
  { href: "/guidelines/", label: "Guidelines", hint: "Guidelines for designing a naxp" },
  { href: "/interactive/", label: "Interactive", hint: "Test and develop a naxp interactively" },
  { href: "/migrate/", label: "Migrate", hint: "Understand the impact of changing a naxp" },
  { href: "/code-gen/", label: "Code&#8209;gen", hint: "Given a naxp, generate custom code to encode text and get text from encoded values", page: "code-gen.html" },
  { href: "/libraries/", label: "Libraries", hint: "Find a package or code implementation in your chosen language" },
  { href: "/spec/", label: "Spec", hint: "The naxp specification", page: "latest.njk", section: "/spec/" }
];

// The entries whose page is present here, in order. The Spec entry is pointed
// straight at the latest version rather than at /spec/, which is a redirect
// page: a click on the header went through it, and the redirect's own line of
// text showed for a frame before the specification arrived. Only a typed or
// linked /spec/ goes through the redirect now.
function availableLinks(configDirectory, version)
{
  return siteHeaderLinks
    .filter(({ page }) => page === undefined || fs.existsSync(path.join(configDirectory, "src", page)))
    .map((entry) => entry.section === "/spec/" && version !== null
      ? { ...entry, href: `/spec/v${version}/` }
      : entry);
}

function buildSiteHeader(entries, icon, versionMark, pageUrl)
{
  const links = entries
    .map(({ href, label, hint, section }) => href === pageUrl || (section !== undefined && pageUrl.startsWith(section))
      ? `    <span class="navbtn" aria-current="page" title="${hint}">${label}</span>`
      : `    <a class="navbtn" href="${href}" title="${hint}">${label}</a>`)
    .join("\n");

  // English only for now, so it is inert rather than a picker.
  return [
    `<header class="siteheader band--edge-bottom">`,
    `  <div class="siteheader__inner">`,
    `    <div class="siteheader__brand">`,
    `      <a class="siteheader__logo" href="/" aria-label="naxp home" title="naxp home">${icon}</a>`,
    `      ${versionMark}`,
    `    </div>`,
    links,
    `    <div class="siteheader__tools">`,
    `      <span class="navbtn" role="button" aria-disabled="true">English</span>`,
    `      <button type="button" class="navbtn navbtn--theme navbtn--icon" id="theme"></button>`,
    `      <a class="navbtn navbtn--icon" href="https://github.com/naxp-org" aria-label="naxp on GitHub" title="naxp on GitHub">`,
    `      <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/></svg>`,
    `      </a>`,
    `    </div>`,
    `  </div>`,
    `</header>`
  ].join("\n");
}

// The site footer on every public page. Here for the same reason the site header
// above is, though this one arrived before it drifted rather than after: eight
// copies of it were in the repository, byte for byte the same, with nothing
// keeping them that way. Each page marks its place with <!-- sitefooter -->.
//
// Unlike the site header, this is a constant rather than a function of the
// page. The header has to know which page it is on, to mark that entry inert;
// the footer says the same three things everywhere.
const siteFooter = [
  `<footer class="sitefooter">`,
  `  <div class="sitefooter__inner">`,
  `    <p>Created by <a href="https://timgord.com/">Tim Gordon</a></p>`,
  `    <p><strong>naxp</strong> is released under the <a href="https://www.apache.org/licenses/LICENSE-2.0">Apache Licence</a></p>`,
  `    <p>Typeset in <a href="https://lisnoti.com/">Lisnoti</a></p>`,
  `  </div>`,
  `</footer>`
].join("\n");


// Curly quotes, applied to the rendered prose at build time so the source can
// be typed with ordinary straight ones.
//
// Only the text between tags is rewritten, and never inside the elements in
// `skipped`. That is what keeps it safe: an apostrophe in an attribute, a
// quote inside a naxp pattern, and the `'light'` in the head's inline theme
// script are all left exactly as written. Rewriting that script would break
// it, so this is load bearing rather than tidiness.
let curlyQuoteCount = 0;

// Whether a mark opens or closes is decided by the character before it, which
// means the character before it has to survive crossing a tag. `naxp</strong>'s`
// must see the `p`, or the apostrophe comes out as an opening quote. So an
// inline tag carries the previous character across and a block tag resets it,
// since text after a block tag genuinely starts afresh.
const inlineTags = new Set([
  "a", "abbr", "b", "cite", "code", "em", "i", "kbd", "mark", "q",
  "small", "span", "strong", "sub", "sup", "time", "var"
]);

function curlyQuotes(html)
{
  const skipped = new Set(["code", "pre", "script", "style", "textarea"]);
  const tag = /<!--[\s\S]*?-->|<(\/?)([a-zA-Z][a-zA-Z0-9-]*)\b[^>]*?(\/?)>/g;

  let depth = 0;
  let from = 0;
  let previous = "";
  let out = "";
  let match;

  const convert = (chunk) =>
    chunk.replace(/['"]/g, (mark, at, text) =>
    {
      const before = at === 0 ? previous : text[at - 1];

      // An opening mark follows nothing, a space, or an opening bracket or
      // dash. Everything else closes, which is also what makes an apostrophe
      // come out right: the letter before it closes the mark.
      const opens = before === "" || /[\s(\[{–—]/.test(before);

      curlyQuoteCount += 1;

      if (mark === "\"") { return opens ? "“" : "”"; }

      return opens ? "‘" : "’";
    });

  const take = (chunk) =>
  {
    if (chunk === "") { return; }

    out += depth === 0 ? convert(chunk) : chunk;
    previous = chunk[chunk.length - 1];
  };

  while ((match = tag.exec(html)) !== null)
  {
    take(html.slice(from, match.index));

    const [whole, closing, name, selfClosing] = match;
    const lower = (name || "").toLowerCase();

    if (skipped.has(lower) && !selfClosing)
    {
      depth = closing ? Math.max(0, depth - 1) : depth + 1;
    }

    // A comment has no tag name, and resets like a block.
    if (!name || !inlineTags.has(lower)) { previous = ""; }

    out += whole;
    from = tag.lastIndex;
  }

  take(html.slice(from));

  return out;
}

export default function (eleventyConfig)
{
  // Eleventy reads .gitignore as its own ignore list unless told not to, and
  // src/spec/ is in there because publishSpecifications writes it. What that
  // list exists for, node_modules and _site, Eleventy ignores anyway.
  eleventyConfig.setUseGitIgnore(false);

  publishSpecifications(import.meta.dirname);

  const wordmark = buildMark(import.meta.dirname, "naxp-logo-clear.svg", "wordmark");
  const icon = buildMark(import.meta.dirname, "naxp-icon-clear.svg", "naxpicon");
  const version = latestVersion(import.meta.dirname);
  const links = availableLinks(import.meta.dirname, version);
  const versionMark = version === null
    ? ""
    : `<a class="siteheader__version" href="/spec/v${version}/">v${version}</a>`;

  // The memory of the text boxes, put back before the first paint. Inline
  // rather than a file, because a script fetched at the end of the body can
  // arrive after the paint it is there to get ahead of; and one source rather
  // than a copy per page, for the reason the header and footer are.
  const restoreScript = [
    "<script>",
    fs.readFileSync(path.join(import.meta.dirname, "src", "_includes", "restore.js"), "utf8").trimEnd(),
    "</script>"
  ].join("\n");

  eleventyConfig.addTransform("siteheader", function (content)
  {
    if (!(this.page.outputPath || "").endsWith(".html"))
    {
      return content;
    }

    return content
      .split("<!-- siteheader -->")
      .join(buildSiteHeader(links, icon, versionMark, this.page.url));
  });

  // Each page marks where a shared piece goes and this puts it there, so each
  // has one home and no page can drift from it. Every marker, not just the
  // first. Two drawings, the version tag, and the foot of the page.
  eleventyConfig.addTransform("markers", function (content)
  {
    if (!(this.page.outputPath || "").endsWith(".html"))
    {
      return content;
    }

    return content
      .split("<!-- wordmark -->").join(wordmark)
      .split("<!-- naxp icon -->").join(icon)
      .split("<!-- naxp version -->").join(versionMark)
      .split("<!-- sitefooter -->").join(siteFooter)
      .split("<!-- restore -->").join(restoreScript);
  });

  // markdown-it does not number headings, so the cross-references inside the
  // specification would otherwise go nowhere. Done as a transform to avoid
  // taking on markdown-it-anchor for twenty lines of work.
  eleventyConfig.addTransform("headingAnchors", function (content)
  {
    if (!(this.page.outputPath || "").endsWith(".html"))
    {
      return content;
    }

    const seen = new Map();

    return content.replace(/<(h[1-4])>([\s\S]*?)<\/\1>/g, (match, tag, inner) =>
    {
      let id = slugify(inner);

      if (!id)
      {
        return match;
      }

      const count = seen.get(id) || 0;
      seen.set(id, count + 1);

      if (count > 0)
      {
        id = `${id}-${count}`;
      }

      return `<${tag} id="${id}">${inner}</${tag}>`;
    });
  });

  // Registered after the other transforms so it sees the finished page, the
  // stamped-in site header included. The specification pages are excluded: a
  // published version is immutable, so its text is reproduced as written and
  // not restyled.
  eleventyConfig.addTransform("curlyQuotes", function (content)
  {
    const input = this.page.inputPath || "";

    if (!(this.page.outputPath || "").endsWith(".html") || input.includes("/spec/"))
    {
      return content;
    }

    return curlyQuotes(content);
  });

  eleventyConfig.on("eleventy.before", () =>
  {
    curlyQuoteCount = 0;
    publishSpecifications(import.meta.dirname);
  });

  eleventyConfig.on("eleventy.after", () =>
  {
    if (curlyQuoteCount > 0)
    {
      console.log(`[quotes] ${curlyQuoteCount} straight quote(s) set curly.`);
    }
  });


  eleventyConfig.addPassthroughCopy({ "src/css": "css" });
  eleventyConfig.addPassthroughCopy({ "src/js": "js" });
  eleventyConfig.addPassthroughCopy("src/CNAME");

  // Two things outside this folder still have to reach the built site: the
  // brand assets, and the JavaScript reference implementation that the
  // interactive developer page runs in the browser. Both stay single copies
  // where they are authored; a second copy under `src/` went stale once
  // already.
  //
  // They are copied here rather than with addPassthroughCopy, and that is
  // load bearing. **Never give Eleventy a path containing `..`.** A watch
  // target or a passthrough reached that way puts the watcher's common base at
  // the repository root, so a change is reported as `site/src/index.html` while
  // the templates are keyed on `src/index.html`. Eleventy sees the change,
  // rebuilds, matches it to no template, and re-emits what it cached at
  // startup. The page then never updates, the log looks perfectly healthy, and
  // it costs an afternoon to find.
  //
  // Copying after each build has the same effect and none of that.
  eleventyConfig.on("eleventy.after", ({ dir }) =>
  {
    const from = (...parts) => path.join(import.meta.dirname, "..", ...parts);
    const into = (...parts) => path.join(import.meta.dirname, dir.output, ...parts);

    fs.mkdirSync(into("img"), { recursive: true });
    fs.copyFileSync(from("brand", "naxp-icon-solid-white.svg"), into("img", "naxp-icon-solid-white.svg"));
    fs.copyFileSync(from("brand", "naxp-social-card-1280x640.png"), into("img", "naxp-social-card-1280x640.png"));

    fs.mkdirSync(into("naxp"), { recursive: true });
    for (const name of fs.readdirSync(from("src", "js", "lib")))
    {
      fs.copyFileSync(from("src", "js", "lib", name), into("naxp", name));
    }

    // The test data for the published version, and that version only. The
    // earlier files belong to specifications the site does not serve, and
    // pinning to data whose document cannot be read is worth nothing.
    //
    // The href in libraries.html names this file, so a new version moves both.
    if (version !== null)
    {
      const data = `naxp-v${version}.json`;
      fs.mkdirSync(into("conformance"), { recursive: true });
      fs.copyFileSync(from("conformance", data), into("conformance", data));
    }
  });

  // Changing a brand asset or the library therefore shows up on the next build
  // rather than immediately, since neither is watched. Both change rarely, and
  // saving anything under `src/` triggers the copy.

  // Newest version first. Numeric compare so 0.10 sorts above 0.9.
  eleventyConfig.addCollection("spec", (collectionApi) =>
    collectionApi
      .getFilteredByTag("spec")
      .sort((a, b) =>
        String(b.data.version).localeCompare(String(a.data.version), undefined, { numeric: true })));

  return {
    dir: {
      input: "src",
      includes: "_includes",
      output: "_site"
    },
    // The grammar documents are full of braces and hashes. Leaving the
    // template engines off for markdown and html means nothing in the
    // specification text is mistaken for template syntax. Layouts still run.
    markdownTemplateEngine: false,
    htmlTemplateEngine: false
  };
}
