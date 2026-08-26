# Deciding W3 at construction time

**Status: working note, 2026-08-11.** This is an adversarial review of the proposed
procedure for checking W3 when a naxp is compiled: a transducer algebra built alongside
`Rx`, determinised by a subset construction over (residual, pending) pairs, with the
longest common prefix of the pendings committed at each step. Grammar references are to
version 0.4 of the grammar, which is not published yet; the encoding definition and the failure of the earlier weighted
design are in `encoding/canonicity.md`; code references are to `src/cs/Naxp`. Every naxp
cited below was run against the current per-string canonicaliser
(`Canonicaliser.TryCanonicalise`) via a scratch program outside the repo, so the
outcomes quoted are observed rather than predicted.

## Summary

Claim A fails as stated, for two reasons that have nothing to do with prefix stripping.
The acceptance test compares pendings and ignores what a branch emits at end of text.
That produces false negatives (`A!!|()` breaks W3 on the empty string and would be
missed) and false positives (`(B|BA)!(BA)|BA!!` is well formed and would be flagged).
Separately, emissions are not uniform over first-set minterms: in `[ab]|[ab]!a` the
copying branch and the replacing branch agree on `a` and disagree on `b`, and a
construction that derives by the single minterm `[ab]` either misses the violation or
reports a wrong witness. Both faults are repairable, and the repairs are given below.
Prefix stripping itself is sound and complete. The proof is short, and it is the one
part of the proposal that survives intact.

Claim B fails on the half that matters. Termination is true and needs none of the
transducer literature: MaxLength strictly decreases along every derivative, so the
construction bottoms out at a depth bounded by the longest accepted string, and pendings
are bounded by the longest canonical string. Affordability is false. The well-formed
naxp `[ab]{17}c|([ab]!a){17}d` compiles to machines of 19 and 36 states, yet drives the
subset construction through more than 2^17 configurations, so the procedure would refuse
a legal naxp at `MaxStates`. The ill-formed `([ab]|[ab]!a){17}` is refused the same way
instead of being diagnosed, although the diagnosis fits in about 2 × 18 states. The
blow-up is intrinsic: the subset construction computes the online canonicaliser, and for
the first family that object provably needs 2^17 states. The corrected procedure tracks
pairs of branches rather than sets of them. This is the squaring construction from the
transducer-functionality literature, and it decides both families in a few dozen states.
The remark in `Compiler.cs` that W3 "needs a product of the canonicalising transduction
with itself" was right all along; the subset proposal is the wrong product.

Along the way three W3 violations smaller than `AB!!B?C` turned up: `A!!A?`, `A?A!!` and
`A!?A?`, each five characters. All are confirmed Ambiguous by the current
implementation, and all should join the test data.

## The transduction, stated precisely

The facts the review rests on, confirmed against the spec rather than assumed: a naxp
has no unbounded repetition, so its accepted language *L* is finite and non-empty
(v0.4, "Three languages"); W1 makes the rendering of every replaceable element a single
fixed string; ρ maps an accepted string to that string with each replaceable element's
match replaced by its rendering; and W3 is exactly the requirement that ρ is
single-valued.

A parse of an input *w* is one way the tree matches *w*. Each parse yields one output:
the input with each replaceable match replaced by its rendering. Write ρ̂(*w*) for the
set of outputs over all parses of *w*. W3 says |ρ̂(*w*)| = 1 for every *w* in *L*.

The transducer algebra Tx mirrors `Rx` with one extra node. `repl(s, y)` carries the
residual *s* of a replaceable subject (an input-only expression) and the rendering *y*
(a fixed string, by W1, recoverable with `Matcher.TryGetSingleString`). This node is the
pairing that `RxConverter` discards at line 79, where a replaceable becomes either its
subject or its rendering; the W3 check is precisely about keeping the two together.

Two functions matter. The derivative δ_b(t) of a Tx *t* by an input block *b* is a
finite set of pairs (emitted string, residual). The end-of-text set eot(*t*) is the set
of strings emitted by accepting the empty string from *t*, one element per ε-parse:

- eot(ε) = {ε}; eot of a character set is empty.
- eot(repl(s, y)) = {y} when *s* is nullable, else empty. Completing a replaceable
  emits its rendering even though nothing is consumed.
- eot of a concatenation is the elementwise product of the children's eots; of a union,
  the union; of an interval t{m,n}, the products of j copies of eot(t) for each legal
  count j, with the j = 0 term contributing {ε} when m = 0.

The derivative rules put mid-string emissions in the right place:

- δ_b(chars S) = {(the character read, ε)} when b ⊆ S, else empty. See the minterm
  fault below: "the character read" is not a fixed string.
- δ_b(repl(s, y)) = {(ε, repl(s′, y))} where s′ is the input derivative of s. Nothing
  is emitted while the subject is being consumed.
- δ_b of a concatenation t1…tn is the union over i, taken while t1…t(i-1) are all
  nullable, of {(f1⋯f(i-1)·e, concat(t′, t(i+1)…tn))} for fj ∈ eot(tj) and
  (e, t′) ∈ δ_b(ti). Skipping a nullable element means choosing one of its ε-parses,
  and that choice can emit: this is where a completed replaceable's rendering enters
  the stream, triggered by the next consumed character.
- Intervals behave as bounded concatenations under the same skip rule.

The correctness statement, provable by induction on |w| from these equations, is that
after reading *w* the set of live branches F(*w*) = {(t, o)} satisfies

> ρ̂(*w*) = { o·f : (t, o) ∈ F(*w*), t nullable, f ∈ eot(t) }.

Emissions triggered mid-string live in *o*; emissions triggered by running out of input
live in *f*. Both are real, and a test that sees only *o* is testing the wrong set.

## Claim A

### The acceptance test as stated is wrong in both directions

Step 3 of the proposal: "wherever a residual is nullable, that branch can accept; if two
branches that can accept have different pendings, the naxp violates W3". By the identity
above, the set that must be a singleton is {p·f}, not {p}. The two sets order violations
differently, and concrete naxps separate them.

**False negative: `A!!|()`.** Here L = {ε, A} and the input ε has two parses. Through
`A!!` the subject matches nothing and the rendering `A` is emitted at end of text;
through `()` nothing is emitted. So ρ̂(ε) = {A, ε} and the naxp breaks W3, which the
implementation confirms: `rho("") -> Ambiguous`. But no character is ever read, so no
emission ever moves into a pending. The start configuration has branches (A!!, ε) and
((), ε), pendings equal, and the stated test passes the naxp. The violation is visible
only in the eots, {A} against {ε}. `A!?|A!!` fails the same way on ε (observed
Ambiguous on both "" and "A") and is the minimal case where a single residual carries
two eot elements, so even a one-branch configuration can witness a violation.

**False positive: `(B|BA)!(BA)|BA!!`.** This naxp is well formed. Both alternatives map
`B` and `BA` to `BA`; the implementation reports Single "BA" for both inputs, and |C| =
1. Now run the construction on input `B`. The first alternative consumes `B` inside its
subject; the subject residual is `(ε|A)`, nullable but able to continue, so the
rendering cannot yet be emitted and the branch is (repl((ε|A), BA), ε) with
eot = {BA}. The second alternative copies `B` and stands before `A!!`, giving the
branch (A?!(A), B) with eot = {A}. Both residuals are nullable. Pendings ε and `B`
differ, so the stated test reports a violation; the totals ε·BA and B·A are both `BA`,
so there is none. No emission-timing convention rescues the stated test here: the first
alternative's subject residual is nullable but continuable, so its rendering genuinely
cannot be emitted before the input ends.

**The corrected test.** A configuration K with committed prefix *c* violates W3 exactly
when |{p·f : (t, p) ∈ K, t nullable, f ∈ eot(t)}| > 1. Equivalently, extend the input
alphabet with an end marker ⊣, derive every branch by ⊣ with emission f for each
f ∈ eot(t), and compare the resulting pendings; acceptance emissions then become
ordinary emissions and step 3 becomes literally true. Either phrasing works. What does
not work is comparing pendings at nullable residuals.

### Emissions are not uniform over first-set minterms

The proposal derives by "character-set minterms", meaning minterms of the first sets, as
`StateMapBuilder.Minterms` computes for the language machines. For input behaviour that
is exactly right: every character of a minterm has the same continuations. For output
behaviour it is too coarse, because a character-set node emits the character actually
read, while a replaceable emits a fixed string, and whether the two agree depends on
which character of the minterm was read.

`[ab]|[ab]!a` is the minimal case. Its first sets give the single minterm `[ab]`. The
copying branch emits the character read; the replacing branch emits `a`. On input `a`
the outputs agree and ρ(a) = a is single; on input `b` they disagree and ρ̂(b) = {b, a}.
The implementation confirms Single on "a" and Ambiguous on "b". A construction that
treats the minterm `[ab]` as one transition must give one answer for both characters.
Treating the symbolic copy as unequal to `a` reports a violation whose witness set
wrongly includes `a`; treating it as equal misses the violation on `b`.

The repair is to refine the minterms. Split out every character that occurs in any
rendering as a singleton block before computing minterms. Then each block is either a
single rendering character or disjoint from all rendering characters, and every
copy-against-rendering comparison resolves uniformly over the block: equal when the
block is that rendering character's singleton, unequal otherwise. Since the input side
is already uniform over the coarser minterms, refinement cannot change which inputs are
accepted; it only multiplies transitions, and renderings are few and short in practice.

One comparison shape survives refinement: a copied character against a copied character
from a different input position. That arises only when one branch's output is ahead of
the other's (a replaceable whose rendering is longer or shorter than what it consumed)
and both branches subsequently copy. Within a multi-character block the outcome then
genuinely varies by character. A sound and simple treatment is to concretise: when a
configuration or pair state carries a non-empty delay and a copy lands in the
disagreement window, derive that step by single characters rather than blocks. The cost
is bounded by the alphabet (95) and is paid only at delay-bearing states, which collapse
quickly (see the square below). A cleverer symbolic scheme is possible, because each
input character is emitted at most once per branch and each output slot is compared at
most once per branch pair, so the equality constraints form chains of degree at most
two; but the concrete fallback is enough for a first implementation.

### Prefix stripping is sound and complete

With the acceptance test corrected, the stripping claim holds. Fix an input prefix *u*
and let F(*u*) be the branch set with full outputs, c(*u*) the committed string, and
K(*u*) the stripped configuration.

**Lemma 1 (commit safety).** c(*u*) is a prefix of *o* for every (t, o) ∈ F(*u*), and
o = c(*u*)·p for the corresponding (t, p) ∈ K(*u*). By induction on the steps: each
commit is the longest common prefix of the pendings of all branches live at that moment,
so it extends a common prefix of all live outputs; branches created later are
derivatives of live branches and only extend their outputs. A branch that dies later was
live when each earlier commit was taken, so every commit was a prefix of its output too;
its death retracts nothing.

**Lemma 2 (verdict invariance).** At any configuration the corrected test compares the
set {p·f} over accepting branches. By Lemma 1 the corresponding full-output set is
{c·p·f} with one shared c. Prepending a fixed string to every element of a set of
strings preserves both the count of distinct elements and which pairs differ. So the
stripped test and the unstripped test flag exactly the same configurations, and hence
exactly the same witness inputs.

**Lemma 3 (sharing safety).** The successor of K under a block, and the verdict at K,
depend on K alone and not on c: derivatives and eots are functions of residuals, and
stripping is a function of pendings. So interning stripped configurations, and letting
two different inputs with different committed prefixes share one configuration, changes
no verdict and no witness. This is the point of stripping; without it configurations
carry full outputs and never merge across prefixes.

The three stress cases dissolve. Branches that die after a commit: covered by Lemma 1;
commits are never retracted and never needed to be. Live-set LCP against
eventual-accepter LCP: the two can diverge, but only in the safe direction. The live-set
LCP is a prefix of the eventual accepters' common prefix, because accepters descend from
live branches, so stripping can under-commit and carry longer pendings than strictly
necessary; it can never over-commit. Under-committing costs merges, not correctness. A
branch accepting now against a branch accepting later with a difference confined to the
committed region: impossible, because acceptance comparisons happen within one
configuration, meaning one input, and within a configuration every total shares the
committed prefix, so any difference lies wholly in the uncommitted region. Acceptances
at different configurations belong to different inputs, and W3 never compares outputs of
different inputs.

One implementation note. The committed prefix is path-dependent and is not stored in
shared configurations, so a violation report that wants to print the two canonical forms
must recompute the commit along the witness path. The verdict and the witness string
need no such recomputation.

## Claim B

### Termination and the pending bound

Both hold, and neither needs transducer theory. `Rx.MaxLength` is exact and strictly
decreases along every derivative (`Rx.cs`); the same is true of the input projection of
every Tx residual. Every branch of every configuration therefore drops its MaxLength by
at least one per consumed character, so the construction's depth is bounded by the
longest string of *L* and the exploration is finite. A pending is a suffix of the output
of a partial parse; every residual's language is non-empty (empty ones are dropped as
`EmptySet`), so every partial parse extends to a full one, whose output is a canonical
form; hence |pending| ≤ the longest string of *C*, which is at most
`NaxpLimits.MaxStringLength` for any naxp whose machines fit the budget.

### The configuration count blows up on a well-formed naxp with tiny machines

The interesting half of Claim B is affordability, and it is false. Two families, one
ill-formed and one well-formed, both verified against the implementation.

**The ill-formed family: `([ab]|[ab]!a){k}`.** Each position independently either copies
its character or replaces it with `a`. For k = 17 both machines have 18 states and
|L| = |C| = 2^17. The naxp breaks W3 on any input containing a `b`
(observed: Ambiguous on `b` followed by sixteen `a`), and the per-string canonicaliser
answers that instantly for such inputs; on all-`b` input its output set is 2^17 strings
and it reports TooLarge.

Now count subset configurations. Reading character x from residual G{m}, where
G = `([ab]|[ab]!a)`, yields branches (G{m-1}, p·x) and (G{m-1}, p·a); they coincide when
x = a and differ when x = b. After reading w ∈ {a,b}^j the configuration is

> { (G{k-j}, p) : p ∈ D(w) }, where D(w) is w with any subset of its `b`s replaced by `a`,

then LCP-stripped. Write w = aᵗbv (t leading `a`s, then the first `b`, then v). The LCP
is aᵗ and the stripped pending set is {b, a}·D(v). The maximal element of D(v) is v
itself, so the stripped configuration determines v, and at depth j it determines w. So
there are exactly 2^j distinct configurations at depth j, and Σ over j ≤ 17 gives
2^18 − 1 = 262 143. The cumulative count crosses `MaxStates` = 100 000 during depth 16.
No violation can be reported earlier, because G{m} is not nullable until m = 0, so the
first acceptance check sits at depth 17. The construction therefore refuses this naxp as
an implementation limit, where the per-string check diagnoses it from a one-`b` input in
microseconds. For a compiler that naxp.org will point at strangers' input, an
exponential path that ends in refusal is also a denial-of-service surface.

**The well-formed family: `[ab]{k}c|([ab]!a){k}d`.** The first alternative copies k
letters and demands a final `c`; the second replaces each letter with `a` and demands a
final `d`. The last character decides the alternative, so every accepted string has one
parse: w·c maps to w·c, and w·d maps to aᵏ·d. For k = 17 the implementation confirms
well-formedness on spot checks (Single "bbbbbbbbbbbbbbbbbc" and Single
"aaaaaaaaaaaaaaaaad"), with the accepted machine at 19 states and the canonical machine
at 36.

After reading w = aᵗbv (length j) the configuration is two branches,

> ( `[ab]{k-j}c`, w ) and ( `([ab]!a){k-j}d`, aʲ ),

whose LCP is aᵗ, leaving stripped pendings (bv, a^(|v|+1)). Distinct v at a given depth
give distinct configurations, and the all-`a` prefix gives one more, so again 2^j
configurations at depth j and about 2^(k+1) in total: over 260 000 for k = 17, against a
budget of 100 000. Each configuration holds just two branches; the blow-up is in how
many configurations exist, not in how big any one is. **The subset construction refuses
a legal naxp whose machines have a few dozen states.** That is the counterexample Claim
B asked for, and it should be a test case for whatever procedure replaces the proposal.

A structural fact sharpens the picture. In a well-formed naxp, no reachable
configuration can hold two branches with the same residual and different pendings: the
residual's language is non-empty, any of its accepting continuations extends both
branches over the same input, and W3 then forces the pendings equal. So well-formed
configurations are partial functions from residuals to pendings. The family above obeys
this, holding two branches with different residuals whose continuation languages are
disjoint (`…c` against `…d`), which is exactly how it stays well formed while the
pendings disagree. The lemma bounds the width of a configuration, and does nothing to
bound the number of configurations, which is where the cost lives.

### The blow-up is intrinsic to determinisation

The subset construction is a determinisation: its configurations are the states, and its
committed prefix is the output, of a deterministic machine that reads the input once and
emits ρ online. For `[ab]{k}c|([ab]!a){k}d` any such machine needs 2^k states. After
reading w, what has been emitted must be a common prefix of ρ(wc) = wc and
ρ(wd) = aᵏd, so it is at most w's leading run of `a`s; on then reading `c` the machine
must emit the rest of wc, so its state after w must determine w beyond that leading run.
Distinct w of length k therefore need distinct states, and there are 2^k of them. No
representation tweak, commit policy or configuration encoding escapes this, because the
lower bound is about the function being computed, and the subset construction computes
it by definition. A decision procedure for W3 does not have to compute ρ online, and the
verdict for every w in that family is the same, so a procedure that tracks less can
merge what the subset construction must keep apart.

### The corrected construction is the square

W3 is a pairwise property. ρ is single-valued unless some input has two parses with
different outputs, and two parses are compared two at a time. So track pairs of
branches, never sets.

A pair state is an unordered pair of Tx residuals with a delay: ((t1, t2), d), where d
is either the pair of stripped pendings with at least one side empty, or the mismatch
mark #. The start state is the root paired with itself, delay (ε, ε). Deriving by a
refined block b takes every (e1, t1′) ∈ δ_b(t1) and (e2, t2′) ∈ δ_b(t2), appends the
emissions to the respective sides, strips the common prefix, and collapses to # the
moment both sides are non-empty, since outputs that disagree at some position disagree
there forever. The state ((t1, t2), #) needs no further output tracking at all. A state
is a violation when t1 and t2 are both nullable and some f1 ∈ eot(t1), f2 ∈ eot(t2)
make the totals differ: always, when d = #, since nullable residuals have non-empty
eots; otherwise when d1·f1 ≠ d2·f2 for some choice. The diagonal matters: (t, t) with
|eot(t)| ≥ 2 is a violation, which is how `A!?|A!!` is caught at the start state before
any input. Completeness is immediate, because any two parses of one input are a pair of
branches and every pair of branches is tracked; soundness because every flagged pair is
realised by the input along the path, with characters chosen concretely under the
refined minterms. The witness set is identical to the subset construction's, since both
characterise the same property of inputs. Termination is the same MaxLength argument,
now applied to both components at once.

On the two families the square is small. For `([ab]|[ab]!a){k}`: from the diagonal, the
refined block [a] gives equal emissions and delay ε; the block [b] gives emissions b
against a, so the copy-against-replace pair collapses to #. Every reachable state is
(G{j}, G{j}) with delay ε or #, about 2k states, and the # state at depth k is nullable
on both sides: violation, witness `b` followed by aᵏ⁻¹. For `[ab]{k}c|([ab]!a){k}d`:
the cross pair mismatches on the first `b` and dies when the input reaches `c` or `d`,
one branch of each pair lacking the final character; no state is ever co-nullable, so
the naxp passes, in O(k) states. Both verdicts in a few dozen states, against 2^18
configurations for the subset construction.

Costs. Pair states number at most (branch residuals)² × (delays + 1). Branch residuals
are Antimirov-style partial derivatives and stay modest for real naxps, though interval
counters multiply them in the same way they multiply `Rx` states, so the `MaxStates`
budget should cap the square exactly as it caps the machines. Delay strings are bounded
in length by the longest canonical string. Whether their variety can be made large
enough to hurt is open; the routes that suggest themselves force the copied region to
equal a fixed rendering and then collapse the remaining choice by periodicity, which is
the shape of the twinning arguments in the literature. The budget makes the question
non-blocking: a naxp that exceeds it is refused as an implementation limit, exactly as
now, and no naxp anyone has a reason to write goes anywhere near it.

### What the transducer literature is for here

The delay-bound machinery of Béal, Carton, Prieur and Sakarovitch, and the
Gurari–Ibarra polynomial bound, exist to make functionality decidable for transducers
with cycles, where exploration does not terminate by itself and the theorems bound how
much delay must be tracked before a verdict is forced. Acyclicity makes all of that
redundant for termination: the construction bottoms out at the depth of the longest
string with no help. What this review takes from that literature is the squaring idea
itself, which is what replaces the subset construction, and which the remark in
`Compiler.cs` already named. The quantitative delay bounds would only be needed to prove
the square polynomial in the worst case, and an implementation with a state budget can
ship without that proof.

## Question C: cheaper procedures

The interaction is non-local, and a pair of naxps pins that down. `AB!!B?C` breaks W3 on
`ABC` (observed Ambiguous), while `AB!!BC`, the same naxp with the `?` removed three
tokens after the `!!`, is well formed (observed Single "ABBC" for both its inputs). Any
purely local condition on a replaceable and its neighbours judges these two alike. So
there is no complete local check, confirming the intuition in the task.

Two shortcuts are worth having, and one is worth avoiding.

The shortcut that costs nothing: a naxp with no `!` satisfies W3 vacuously, since ρ is
the identity. Most naxps will take this path, and it is trivially sound.

The shortcut to treat with suspicion: the spec's by-eye rule, disjointness between a
replaceable's characters and the characters legal at its position when it is omitted,
generalises to a checkable condition on first and follow sets. It is sufficient only,
it goes inconclusive on anything interesting (`AB!!BC` trips it and is well formed),
and, most importantly, a precise statement covering alternation, nesting under
intervals and boundary ambiguity has not been proved here. The weighted design in
`encoding/canonicity.md` is the cautionary tale for this exact move: a condition that
looks obviously right, adopted without a proof, on the construct that this project got
wrong once already. Since the square on a sane naxp costs about as much as building its
machines, a heuristic on the trust path buys microseconds and risks a wrong verdict.
Run the square always; keep the no-`!` short-circuit; put nothing else in front of it.

One genuine simplification falls out once W3 moves to construction time. For a compiled
naxp every parse of an accepted string yields the same output, so ρ(w) can be computed
from any single successful parse, greedily, with no set of (position, output) pairs at
all. `Canonicaliser` currently carries that set precisely because W3 might fail, and its
Ambiguous and TooLarge outcomes exist for that reason; the observed TooLarge on
`([ab]|[ab]!a){17}` with all-`b` input is the set reaching 2^17 elements. With W3
guaranteed at compile time, Ambiguous becomes unreachable and the set collapses to a
`Matcher`-style position set, which removes the per-string blow-up for legal naxps as a
side effect.

## Test cases

Naxps this review adds, with the observed outcome and the reason each earns a place.

- `A!!A?`, `A?A!!`, `A!?A?` — Ambiguous on `A`. Five-character W3 violations, smaller
  than `AB!!B?C`; the first two need end-of-text emissions on one side to be caught.
- `A!!|()` — Ambiguous on the empty string. Violation witnessed by ε alone; any checker
  that ignores end-of-text emissions passes it. `A!?|A!!` is the same point with both
  canonical forms produced by eots, and a single-residual configuration.
- `(B|BA)!(BA)|BA!!` — well formed, |C| = 1. Accepting branches with pendings ε and `B`
  whose totals agree; flags any checker that compares pendings instead of totals.
  `(B|BA)!(BA)X|BA!!X` is the same shape with a tail, so the disagreement-then-agreement
  survives past a consumed character.
- `A!!B`, `BA!!`, `A!?A`, `A!!A` — well formed. Four-character near misses bracketing
  the five-character violations.
- `[ab]|[ab]!a` — Single on `a`, Ambiguous on `b`. Minimal witness that emissions are
  not uniform over first-set minterms; the correct verdict needs `[ab]` split into
  `[a]` and `[b]`.
- `AB!!BC` alongside `AB!!B?C` — the non-locality pair; a `?` three tokens away flips
  the verdict.
- `([ab]|[ab]!a){17}` — ill formed; machines of 18 states; subset construction exceeds
  100 000 configurations before its first possible acceptance check; square decides it
  in about 36 states. Also the per-string TooLarge case on all-`b` input.
- `[ab]{17}c|([ab]!a){17}d` — well formed; machines of 19 and 36 states; subset
  construction exceeds 100 000 configurations, so the proposal refuses a legal naxp;
  square passes it in O(k) states. The Claim B counterexample.
- `\A\A?\9\X?\s!!\9\A\A` — the postcode, well formed; the benign case every procedure
  must pass quickly.
