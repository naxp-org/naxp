# Generator

What produces the test data beside this folder, so that a value in it can be
questioned rather than taken on trust.

```
python conformance/generator/generate.py            # write naxp-v0.10.json
python conformance/generator/generate.py --check    # verify the committed one
```

`--check` regenerates in memory and compares, so the committed data can be shown
to be what this produces rather than something edited beside it.

Nothing here reads an implementation of naxp. An implementation that both
produced the data and had to pass it would be defining its own truth.

## What is here

| | |
|:---|:---|
| `cases.py` | The authored half: which naxps, why each is worth a case, what must be invalid for it, and which strings to sample where the language is too large to list. No derived number appears in it. |
| `naxpref.py` | The language. The repertoire, the parser, case folds, the two constructs that expand, W1, W2 and W4, enumeration of a language, and **the order**, which is where the encoding is defined. |
| `naxpmachine.py` | The algorithm. Brzozowski derivatives, the minimal machine, how many strings each state reaches, and encode and decode by walking it. |
| `naxptx.py` | The canonicalising transduction, and W3. |
| `naxp.py` | The facade: every rule decided, and the encoding computed by both routes. |
| `generate.py` | Writes the file. |

## Why the encoding is computed twice

`naxpref` ranks the canonical language under the order the specification defines,
with no machine anywhere: the empty string first, then by the set order of first
classes, then by ASCII within a class, then by recursion. `naxpmachine` builds
the minimal machine by derivatives and counts past the strings that sort before.
The two share no reasoning, and `naxp.agreed_values` will not return unless they
agree on every encoded value, in both directions, and on both counts.

Canonical forms are computed twice as well. `naxpref.outputs_of` walks the tree
and `naxptx.canonical_form` walks the transduction, and `naxp.canonical_form`
compares them before answering.

**65 of the 70 cases get both routes.** Four of the other five have between 1.7
billion and 10^19 encoded values, which no enumeration will reach, so the machine
is the only witness for them, and the fifth is sampled by choice. That is stated
here rather than glossed.

## What is checked, every run

- Every naxp under `CASES` must be well formed, and every naxp under
  `INVALID_NAXPS` must break **exactly** the rule claimed for it. A naxp listed
  as breaking W2 that turns out to break W1 stops the generator.
- Every canonical string must encode to its rank and decode back to itself.
- Every string listed under `invalid` for a case must actually be invalid for it.
- A complete case must list every string its naxp accepts, checked against the
  count the machine gives independently.

## What this does not do

**W3 is decided by the construction that is known to be unaffordable.** This
determinises the transduction: a configuration is a set of threads, each a
residual paired with what it has emitted and not committed, with the longest
common prefix stripped to keep the space finite. That is correct, and there are
well-formed naxps it cannot decide – `[ab]{17}c|([ab]!a){17}d` drives it through
2^17 configurations. `MAX_CONFIGURATIONS` makes it say so rather than run away.

A production implementation cannot use this. It has to decide W3 with a product
of the transduction with itself, pairwise rather than by sets, because it is held
to W6 and must answer for any naxp a stranger writes. The distinction matters:
this runs over naxps chosen for the test data, where the exponential families
simply are not present.

**W6 is measured for two of its four machines.** The rule caps the machine for
the canonical language, the machine for the accepted language, the machine that
computes canonical forms and the product machine that decides W3. The first two
are built and counted here. The other two are the configuration count of the
determinisation above, which this reports as one number because the construction
serves both, and which no case in the data comes close to. The W6 cases that are
in the data break the rule on string length, which is the way the specification's
own example breaks it.

**Deep naxps need a big stack.** A naxp of a few thousand positions is a chain of
that many expressions and the walks over it recurse as deep, so `generate.py`
runs its work on a thread with a 64 MB stack and a raised recursion limit.
