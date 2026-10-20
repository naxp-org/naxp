// Every file in this folder is the prose for one standard naxp, named by its id
// in standards/naxp-standards.json, and is that standard's page at
// /standard/<id>/, set out by _includes/standard-page.njk. loadStandards in
// eleventy.config.js checks that the files and the ids match one for one.
export default {
  layout: "standard-page.njk",
  permalink: (data) => `/standard/${data.page.fileSlug}/index.html`,
  eleventyComputed: {
    title: (data) => `${data.standards.byId[data.page.fileSlug].name} – standard naxp`,
    description: (data) => `${data.standards.byId[data.page.fileSlug].name} (${data.page.fileSlug}): the standard naxp and its variants, with what each accepts and prints.`
  }
};
