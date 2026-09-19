# Emitter identity

`generate.js` emits every conformance naxp in every output language through the
JavaScript implementation and writes the fragments to one file, so that another
implementation's emitters can be held byte-identical to them.

```
node conformance/emit/generate.js --out emit-expected.txt
```

The output is not committed: it is a run, not a standard. As with the fuzz
generator beside this, the JavaScript is a standard by agreement rather than by
construction: its emitters were held identical to the C# ones by hand while both
were written, and this file carries that agreement to each implementation that
follows. The C++ tests run it under `ctest` as `naxp_emit`.

## What a file holds

One record per fragment: a header line naming the language, the value type, the
prefix and the fragment's length in bytes, the naxp on a line of its own, and then
the fragment. Every case is emitted with the prefix `Case<i>` and the widest value
type; the first case is emitted again with the bare names, with narrower types, and
with a space indentation and CRLF, so that each of those paths is covered once.
