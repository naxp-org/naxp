# Conformance data

`naxp-v0.10.json` is language-neutral test data for version 0.10 of naxp. It was
generated from the specification rather than from any implementation, so an
implementation cannot define its own truth by passing it. `generator/` is what
produced it, and `generator/README.md` says how.

## Shape

`cases` holds well-formed naxps. Each has a `maxEncodedValue`, the size of the
canonical language, and an `acceptedCount`, the size of the accepted language;
the two differ only where the naxp contains a `!`. Each entry under `values`
gives a piece of text, the encoded value it takes, and its canonical form:

- `out` is `0` exactly when `in` is invalid;
- otherwise `decode(out)` must equal `canon`, and `canon` must itself encode
  back to `out`.

Where `complete` is true, `values` lists every accepted string, so the encoded
values are also a bijection onto 1..`maxEncodedValue`. Five cases are sampled
rather than enumerated. `invalid` lists text that is invalid for the naxp.

`maxEncodedValue`, `acceptedCount` and `out` are decimal strings rather than JSON
numbers. W5 allows a naxp up to 2^64 - 1 encoded values, and most JSON parsers
read a number into a double, which is exact only to 2^53. Writing them as strings
keeps one type per field rather than one that changes with the magnitude.

`invalidNaxps` holds naxps that must be invalid, each tagged with the rule it
breaks: `syntax`, or `W1` to `W6`.

## The two version numbers

`naxpVersion` says which version of the language the data targets.
`testDataVersion` says whether the data itself has moved: it goes up when a case
is added, removed or corrected for the same version of the language, so a
consumer holding the current number needs no new copy.

## What this data cannot cover

**The W6 ceiling is not tested here, and cannot be.** A marked decimal range
reaches eleven digits, and what stops it is the machine that decides W3 rather
than the one that canonicalises. Neither limit can have a case in this file,
because `naxpref` builds a decimal range by listing the strings between its
bounds: an eleven digit range is 10^11 of them. The generator refuses such a naxp
long before it reaches any machine, and would refuse it whatever the language
said. The W6 cases that are here break the rule on string length instead, which
is the way the specification's own example breaks it.

Making the generator structural – building a range from the shape of its bounds
rather than from what lies between them – is the change that would let this file
cover the ceiling. It is not hard and it is not done.

`generator/README.md` sets out the rest. In short: W3 is decided there by
determinising the transduction, which is correct and is the construction known to
be unaffordable in general, so there are well-formed naxps the generator cannot
decide and refuses rather than guesses. W6 is measured for two of its four
machines.

## Status

Every case the generator can enumerate – 65 of the 70 – has its values produced
twice by independent routes, once by ranking the canonical language under the
order and once by the machine construction, and the two agree. Its canonical
forms are produced twice as well, by walking the tree and by walking the
transduction.

Five cases have one witness rather than two. `\9{19}`, `\A{7}\9{9}` and the
two postcodes hold between 1.7 billion and 10^19 encoded values, which no
enumeration reaches, so only the machine has spoken for them, and `\9{3}` is
sampled by choice so that a small sampled case is tested too. The reference
implementations agree with those values, in the library and in the generated
code, but an implementation agreeing with data it also has to pass is a weaker
guarantee than two derivations agreeing, and it stays weaker.
