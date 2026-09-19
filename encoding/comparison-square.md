# Comparing two naxps: extending the square to two roots

**Status: brief for review, 2026-09-02.** This asks for an adversarial review of a
proposed construction, in the manner of `w3-functionality.md`. Nothing here is built.
Code references are to `src/cs/Naxp`.

## What is being built, and why this is the hard part

`Naxp.Compare(a, b)` is to answer what happens to data encoded with `a` if `b` replaces
it, as three set relationships: the accepted text, the encoding, and the printed text.
The public shape is settled and is not at issue here.

Two of the three are done and green:

- **Accepted text** and **printed text** are language comparisons of two acyclic DFAs,
  by a product walk in `Relations.CompareLanguages`.
- **Rank agreement** – whether two canonical machines give the same value to every string
  they both hold – is `RankAgreement.Compare`. It carries the difference of the two
  running totals through the product, on the argument that the difference at a state pair
  is a property of the pair rather than of the path, so a second and unequal arrival is a
  disagreement rather than a branch.

**What remains is the encoding relation, and it turns entirely on ρ.** Encoding is
`encode(x) = rank(ρ(x))`, so comparing two encodings over the strings both naxps accept
needs to know whether the two canonicalisations agree there.

## What is already decided without ρ, and what is not

**Disagreement is exact and needs no ρ.** If a canonical string both naxps hold takes
different ranks, that is a witness on its own, because ρ fixes canonical strings. Every
"not safe" verdict therefore already follows from `RankAgreement`.

**Agreement is what needs ρ**, and only agreement. If the ranks agree, the two still
differ on some input whenever ρ does, so nothing can be concluded about the strings that
are not canonical.

That asymmetry was measured over 80 naxps – the 67 conformance cases plus the registry – 
and 165 realistic edit pairs (a case fold added, the space made optional or required or
dropped, an alternative added, a decimal range padded):

| outcome | share |
| --- | ---: |
| decided exactly | 72.1% |
| undecided | 27.9% |

Every undecided pair was on the "looks safe" side. Refusing to answer there is what makes
the fallback unshippable rather than merely imperfect: of the four comparisons the site
puts on `/compare/`, the two breaking ones decide exactly and **both safe ones go
undecided** – `\s` against `\s!!`, and the postcode against its case-folded self.

Register depth over the corpus: ρ is the identity for 41, depth 0 for 24, depth 1 for 7,
depth 2 for 7, depth 4 for 1. Note that **a case fold is depth 0**: `\C(...)` emits the
upper case of the character just read, so a copy marker appears but nothing is held. The
UK postcode is depth 2, because the variable-length outward code makes the machine hold
characters before it knows where the space belongs.

## The proposed construction

`W3Checker`'s `Square` already compares two transductions for equal output, and already
copes with output that runs ahead of the input and holds symbolic references – that is
what `Delay` and `TxReference.CouldReconcile` are for. `Square.TryRun` seeds

```csharp
int start = this.Add(new PairKey(root, root, Delay.None), -1, '\0');
```

so it is a two-machine product already, used with one machine twice.

**The proposal is to build both naxps' `Tx` trees through one shared `TxFactory` and seed
the square with the two roots**, taking a mismatch to mean that ρ differs rather than that
a naxp is invalid.

## The four questions

1. **Does the delay still collapse?** The existing termination argument rests on both
   sides being branches of one naxp, which W3 has already shown to be functional, so a
   common prefix is committed at every step. Two independent naxps carry no such
   guarantee, and their pending outputs may run ahead without bound. Does the construction
   still terminate? If it does not, the state budget stops being a size limit and becomes
   a non-termination guard, which is a different claim needing a different justification.

2. **What prunes a pair?** The square must range over the strings both naxps accept. A
   pair whose two sides continue on disjoint characters carries no shared string below it
   and must be dropped rather than reported as a mismatch. Is dropping it sound, and is
   the test for it the obvious intersection of the two transition sets?

3. **What does a mismatch mean here?** In W3 it means the naxp is invalid. Here it means ρ
   differs on some shared string, which is a legitimate finding about two valid naxps. Is
   the existing mismatch condition still exactly the right one under that reading, given
   the two sides no longer share a language?

4. **Is one shared `TxFactory` sound across two compilations?** Interning was designed for
   nodes from a single naxp. Does sharing it change what `PairKey` equality means, or what
   the state budget is counting?

## What an answer of "it cannot always be decided" would mean

That is an acceptable outcome and does not need to be avoided. `Naxp.TryCompare` returns
false with a reason, and `Compare` throws `InvalidOperationException`; the API was designed
with that channel in it. What matters is knowing *which* cases are undecidable and why,
rather than discovering it later from a wrong answer.

**A wrong answer in one direction is much worse than in the other.** Reporting `Equal` or
`SubsetOf` for an encoding that in fact differs would tell somebody their stored data
survives a change when it does not. Reporting `Incomparable`, or declining to decide,
costs only confidence.

## What follows, once the argument is settled

Building both trees through one factory, seeding with two roots, wiring the encoding
relation from the language relation refined by `RankAgreement` and this check, the tests
including a cross-check against enumeration on small naxps, then the JavaScript port. Only
then does `site/src/compare.html` come off the `copy-to-public.ps1` exclusion list, since
it is a static worked example until the libraries can do this.
