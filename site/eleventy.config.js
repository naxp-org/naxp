// GitHub's heading slug rule, so links written in the markdown against the
// version rendered on GitHub keep working here.
function slugify(headingHtml)
{
  return headingHtml
    .replace(/<[^>]+>/g, "")
    .trim()
    .toLowerCase()
    .replace(/[^\w\s-]/g, "")
    .replace(/\s+/g, "-")
    .replace(/^-+|-+$/g, "");
}

export default function (eleventyConfig)
{
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

    return content.replace(/<(h[2-4])>([\s\S]*?)<\/\1>/g, (match, tag, inner) =>
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

  eleventyConfig.addPassthroughCopy({ "src/css": "css" });
  eleventyConfig.addPassthroughCopy({ "src/img": "img" });
  eleventyConfig.addPassthroughCopy("src/CNAME");

  // The card icons are authored in the repo's own `icons/` folder and copied
  // straight through, so that folder stays the only copy. The glob keeps it to
  // the finished icons: `icons/icons-dev/` is working material and stays out of
  // the built site.
  eleventyConfig.addPassthroughCopy({ "../icons/*.svg": "img" });

  // Passthrough files outside the input directory are not watched by default.
  eleventyConfig.addWatchTarget("../icons");

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
