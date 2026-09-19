# Differential testing

`generate.js` writes random naxps and what the JavaScript implementation makes of
them, in the shape of the conformance data beside this folder, so that another
implementation can be run against thousands of naxps nobody chose rather than the
sixty seven somebody did.

```
node conformance/fuzz/generate.js --seed 1 --count 2000 --out fuzz.json
```

The same seed gives the same file, so a failure is reproduced by its seed alone.
The output is not committed: it is a run, not a standard.

This is not conformance data. The generator in `../generator` reads no
implementation, which is what lets its output stand as the truth; this reads one,
so a disagreement means a defect in one of the two implementations and says
nothing about which. The JavaScript is a fair standard because it agrees with the
C# on the conformance data and on 82 500 lines of emitted code, but it is a
standard by agreement rather than by construction.

## What a file holds

The `cases` and `invalidNaxps` of the conformance data, plus:

- `code`, `offset` and `length` on every invalid naxp, so the fault is compared
  exactly and not only by rule;
- `pairs`, each two naxps with the three axes of their comparison, whether it was
  decided, and the first divergent value.

About half the naxps are invalid, spread across syntax and every rule W1 to W6.
The valid ones are exercised on every string they accept where that is under
four hundred, and on a sample of decoded values otherwise, plus text near each of
those, which is where the invalid text and the non-canonical accepted text come
from. Renderings are mostly taken from the subject's own language, so that `x!y`
passes W1 often enough to be worth walking.

## Who runs it

The C++ build does, as the `naxp_fuzz` test, when Node is on the machine: `ctest`
generates a file with the seed in `NAXP_FUZZ_SEED` (a CMake cache variable, default
1, `NAXP_FUZZ_COUNT` default 2000) and runs its conformance tests over it through
the `NAXP_CONFORMANCE_FILE` environment variable, which is how any file in this
shape is run through that binary:

```
NAXP_CONFORMANCE_FILE=fuzz.json build/tests/naxp_tests conformance
```

Five seeds of five thousand naxps each, over a million strings and 2 500 pairs,
were run on 2026-09-15 with no disagreement.
