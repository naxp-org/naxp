// Every file in this folder is the prose for one standard naxp, named by its id
// in standards/naxp-standards.json. None is a page of its own: each is gathered
// into the 'standard' collection and set out in a collapsed section on
// /standard/, under the table of them all. loadStandards in
// eleventy.config.js checks that the files and the ids match one for one.
export default {
  permalink: false,
  tags: "standard"
};
