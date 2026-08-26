# Conformance data

`naxp-v0.5.json` is language-neutral test data for version 0.5 of naxp. It was
generated from the specification rather than from any implementation, so an
implementation cannot define its own truth by passing it.

`naxp-v0.4.json` is kept for version 0.4 and is no longer the one the reference
implementation reads.

The specification itself is not published yet. Version 1 will be the first
release; 0.4 and 0.5 are development documents, so this data is here without the
document it was generated from. That is temporary and it is the wrong way round -
data one cannot check against its source is worth less - so it is worth reading
these files as a record of what the implementations agree on rather than as an
independent authority, until v1 lands.

## Shape

`cases` holds well-formed naxps. Each has a `valueCount`, the size of the
canonical language, and an `acceptedCount`, the size of the accepted language;
the two differ only where the naxp contains a `!`. Each entry under `values`
gives an input string, the value it encodes to, and its canonical form:

- `out` is `0` exactly when the naxp does not accept `in`;
- otherwise `decode(out)` must equal `canon`, and `canon` must itself encode
  back to `out`.

Where `complete` is true, `values` lists every accepted string, so the values
are also a bijection onto 1..`valueCount`. Four cases are sampled rather than
enumerated.

`valueCount`, `acceptedCount` and `out` are decimal strings rather than JSON
numbers. W5 allows a naxp up to 2^64 - 1 values, and most JSON parsers read a
number into a double, which is exact only to 2^53. Writing them as strings keeps
one type per field rather than one that changes with the magnitude.

`rejected` holds naxps that must be refused, each tagged with the rule it
breaks: `syntax`, or `W1` to `W5`.

## Version 0.5

The v0.5 file was carried forward from v0.4 **by hand rather than regenerated**,
because the generator is not in this repository.

`testDataVersion` 1 covered the first v0.5 change, the bound on an interval
count, from four digits to two. No case in the data used a count of more than two
digits, so every `cases` entry was unchanged and still correct. Two entries under
`rejected` differed: `A{12345}` kept its rejection with a reworded note, and
`A{123}` was new, being legal under v0.4 and refused under v0.5.

`testDataVersion` 3 covers the interval separator, which became a comma: `A{2,4}` is how an
interval is written and `A{2-5}` is refused. It is the one change so far that alters the
syntax rather than a bound, and it was made because departing from the regular expression
convention bought nothing and would have caught out anyone fluent in them. Two `cases`
entries were respelled, `A{2-4}` and `A{0-3}`, and three under `rejected` swapped places:
`A{2,5}` is now the refusal and `A{2,}` and `A{5,2}` the near misses. No value changed,
because no language changed.

`testDataVersion` 2 covers W5's rise from 2^63 - 1 to 2^64 - 1 encodable values.
The rejection of `\9{19}` became a rejection of `\9{20}`, 10^19 values now being
legal. Three further changes follow from the wider range:

- `valueCount`, `acceptedCount` and `out` became strings, as above;
- `\9{19}` is a new sampled case, whose values pass both 2^53 and 2^63, so it is
  the one case whose order a signed 64 bit integer cannot show;
- `\A{7}\9{9}` is a new sampled case, a mixed radix count above 2^53.

No existing `cases` entry changed.

## Status

Every value carried forward from version 0.4 is produced twice by independent
routes, once by ranking the canonical language directly and once by the machine
construction, and the two agree. The 21 cases expressible in the older syntax
were also checked against `prior-work/NXOld`, which agrees on all 392 of its
values.

The two cases added at `testDataVersion` 2 do not have that. Their values were
ranked directly from the order the specification defines, and the only thing they
were then checked against is the reference implementation, which agrees with them
in the library, in the generated C# and in the generated JavaScript. An
implementation agreeing with data it also has to pass is a weaker guarantee than
two derivations agreeing, and it stays weaker until the generator produces these
two cases as well.
