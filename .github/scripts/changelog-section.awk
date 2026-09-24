# Prints one release's section of library-changelog.md as one library sees it: the lines under
# the heading `## [version]`, up to the next level 2 heading, less the text marked for other
# libraries.
#
#     awk -v version=0.12.0 -v library=C -f changelog-section.awk library-changelog.md
#
# Text for one library only sits between `<!-- START name -->` and `<!-- END -->`, each on a
# line of its own, the name being C, C# or JavaScript in any case. The marker lines themselves
# are never printed, and a run of blank lines they leave behind is printed as one, except
# inside a code block.
#
# Anything that looks like a marker but is not a well-formed one fails, as do an unknown name,
# a START inside a block, an END outside one, a block left open and a missing section, so that a
# slip in the changelog stops the release rather than publishing the wrong notes. Written for
# POSIX awk, since the runner's is mawk.

function fail(message)
{
	printf "library-changelog.md line %d: %s\n", NR, message > "/dev/stderr"
	failed = 1
	exit 1
}

BEGIN {
	library = tolower(library)

	if (version == "" || (library != "c" && library != "c#" && library != "javascript"))
	{
		print "Usage: awk -v version=X.Y.Z -v library=C|C#|JavaScript -f changelog-section.awk library-changelog.md" > "/dev/stderr"
		failed = 1
		exit 1
	}

	heading = "## [" version "]"
}

!found {
	if (index($0, heading) == 1)
	{
		found = 1
	}

	next
}

/^## / { exit }

# A fence is tracked so that a marker shown inside a code block is printed as it stands.
/^[ \t]*```/ { fenced = !fenced }

!fenced && tolower($0) ~ /^[ \t]*<!--[ \t]*(start|end)([^a-z]|$)/ {
	line = tolower($0)

	if (line ~ /^[ \t]*<!--[ \t]*end[ \t]*-->[ \t]*$/)
	{
		if (!open) { fail("an END with no START before it") }

		open = 0
		next
	}

	if (line !~ /^[ \t]*<!--[ \t]*start[ \t]+[^ \t].*-->[ \t]*$/)
	{
		fail("a marker that is neither <!-- START name --> nor <!-- END -->")
	}

	if (open) { fail("a START inside a block that has not ended") }

	name = line
	sub(/^[ \t]*<!--[ \t]*start[ \t]+/, "", name)
	sub(/[ \t]*-->[ \t]*$/, "", name)

	if (name != "c" && name != "c#" && name != "javascript")
	{
		fail("the library '" name "' is not C, C# or JavaScript")
	}

	open = 1
	block = name
	next
}

open && block != library { next }

!fenced && /^[ \t]*$/ {
	if (!blank && printed) { pending = 1 }

	blank = 1
	next
}

{
	item = $0 ~ /^[-*+] /

	# A blank line between two items would make the list a loose one, spaced as paragraphs,
	# where the blank was only ever there to set a marker apart from the list around it.
	if (pending && !(item && last_item)) { print "" }

	print
	printed = 1
	blank = 0
	pending = 0

	# Still in a list after an item, a nested item or an indented continuation.
	last_item = item || $0 ~ /^[ \t]+[^ \t]/
}

END {
	if (failed) { exit 1 }

	if (!found)
	{
		printf "library-changelog.md has no section headed %s.\n", heading > "/dev/stderr"
		exit 1
	}

	if (open)
	{
		print "library-changelog.md: a START block is never ended." > "/dev/stderr"
		exit 1
	}
}
