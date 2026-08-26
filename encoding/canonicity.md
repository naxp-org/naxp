# Canonicity of the naxp encoding

**Status: working note, 2026-08-06.** This settles a question left open in the development plan, which is not in
this repository: whether generative equivalence implies
encoding equivalence, and whether every naxp has a unique minimal form. It is an
input to the encoding specification, not itself normative. Grammar references
are to version 0.3 of the grammar, which is not published yet; code references are to
`prior-work/NXOld`.

## Summary

For naxps without `!`, the answer is yes on both counts, and the note gives two
independent routes to it. The encoding can be defined as a rank within a total
order on the accepted language, with no reference to any machine, which makes
canonicity true by construction. Separately, the bottom-up hash-consed
construction in NXOld provably builds the unique minimal automaton, and a
43-case mechanical check against NXOld confirms it. `Ast_Simplification.cs` is
not needed for the encoding to be correct.

For naxps with `!`, the encoding is defined as the composition of
canonicalisation with the rank over the canonical sublanguage, under the
sublanguage's own order. That is canonical with no new proof. The
single-machine design sketched earlier, transitions labelled
`(charSet, weight, canonicalChar)`, is unsound: two well-formed counterexamples
are given below.

## Setting

Write Σ for the matchable characters, `0x20` to `0x7E`. A naxp without `!`
denotes a language L ⊆ Σ*, which is finite and non-empty (v0.3 has no unbounded
repetition, and every base matches at least one string).

For a character c, the derivative c⁻¹L = { w : cw ∈ L } is the set of
continuations after c. Say c ≡ d when c⁻¹L = d⁻¹L and both are non-empty. The
classes of ≡ partition the characters that can begin a string of L, and every
character in a class has the same continuations.

Order character sets as follows: write each set as the string of its members in
ascending ASCII order, and compare those strings ordinally, with the empty set
first. This is `AsciiCharSet.CompareTo`. On pairwise disjoint non-empty sets it
is a strict total order.

## The encoding is a rank

Define a total order on L by recursion. For distinct u, v ∈ L:

- ε precedes everything.
- Otherwise u = a·u′ and v = b·v′. If a and b lie in different classes of ≡,
  order u and v by the set order of their classes. If they lie in the same
  class and a ≠ b, order by ASCII order of a and b. If a = b, recurse on u′ and
  v′ within a⁻¹L.

The encoding of w is then its 1-based rank: one plus the number of accepted
strings that precede it.

`State.GetEncoding` computes exactly this. At each state the transition array
lists the classes in set order with end of text first; the walk accumulates the
full value counts of the transitions it passes, and within the matching
transition the term `n · IndexOf(c)` makes the character's rank within its
class the primary key and the suffix value the secondary key. The dominance is
strict because a suffix value never exceeds n.

Two consequences. The order, and hence the encoding, is defined from L alone,
so generative equivalence implies encoding equivalence with no appeal to
automata at all. And the specification can state the encoding this way, at the
language level, with the automaton demoted to an algorithm that computes it.
The worry that transition order must be pinned normatively is then discharged
in one line: the order is part of the definition, not a property of a machine.

The order is deliberately not shortlex and not plain lexicographic. In
`AB|B`, the class `[A]` precedes `[B]`, so `AB` takes value 1. In `#[0-10]`,
the classes at the first position are `[02-9]` and `[1]`, in that order, which
yields `0`→1, `2`→2 … `9`→9, `1`→10, `10`→11. Both match NXOld's behaviour.

## The construction builds the unique minimal automaton

Define the canonical machine N(L) by recursion on the longest string in L:

- N({ε}) is the single end-of-text state.
- Otherwise N(L) is the state whose transitions are: end of text, if ε ∈ L;
  and one transition (D, N(c⁻¹L)) for each class D of ≡, where c is any member
  of D; sorted by the set order, which places end of text first.

The recursion is well-founded because a derivative strictly shortens the
longest string, and N is injective because L is recoverable from N(L): ε ∈ L
iff the end-of-text transition is present, and L = ⋃ D·L_D over the character
transitions. So N(L₁) = N(L₂) exactly when L₁ = L₂, and the states reachable in
N(L) correspond one-to-one with the distinct non-empty derivatives of L. That
is the Myhill–Nerode machine: the unique minimal trim DFA, acyclic since L is
finite.

**Claim: `StateMapGenerator.CreateStateMapRecursive` computes N(L).** By
induction on the longest remaining string. The recursion carries the unrolled
paths and a position, which together denote a residual language L′.

1. `UpdateDisjointCharSets` produces the minterms of the character sets present
   at the position. Every character in one minterm lies in exactly the same
   path sets, so it selects the same `pathsFromNextState` and gets the same
   next state. The minterm partition *refines* the classes of ≡; it need not
   equal them.
2. Two minterms in the same class of ≡ have equal derivative languages, so by
   the induction hypothesis their recursive calls produce equal state values,
   which the `HashSet` collapses to one representative.
   `MergeTransitionsToSameState` then unions their character sets. This step,
   not the minterm computation, is what makes the partition coarsest, and it
   works only because equal derivatives are guaranteed to yield identical
   representatives. `[AB]C|[BC]C` illustrates it: the minterms at the first
   position are `[A]`, `[B]` and `[C]`, all with derivative {C}, and the merge
   recombines them into the single class `[ABC]`.
3. After merging, the character sets are pairwise disjoint and non-empty apart
   from end of text, so the sort by `CompareTo` has a unique outcome and the
   stability of `Array.Sort` is irrelevant. End of text needs no special
   placement: the empty set is least in the order.

So the state built for L′ is N(L′), value-identical however the naxp was
written. `State.Equals` compares transition arrays deeply, so hash-consing
merges on state value, which by injectivity of N is merging on residual
language: exactly the Myhill–Nerode condition.

Two corollaries. `NX.Equals`, which compares start states, decides generative
equivalence. And `Ast_Simplification.cs` plays no part in any of this: the
state map is canonical for any AST denoting L, so simplification affects only
what `Rehydrate` prints, a separate and weaker question.

## Mechanical check

A scratch harness against the freshly built NXOld ran 43 checks and all
passed:

- 23 pairs of equivalent naxps written as differently as the old syntax
  allows, among them `A(B|C)` vs `AB|AC`, `A?A?` vs `(AA)?|A`, `[ABC]C` vs
  `[AB]C|[BC]C` (the minterm-coarsening case), `#[0-255]` vs its five-branch
  manual expansion, and `\9\9` vs `\9\9|1[0-5]` (an absorbed overlapping
  alternative). Each pair: `NX.Equals` true both ways, encodings identical on
  every string over the test alphabet, encodings a bijection onto 1..k.
- 4 non-equivalent control pairs, all correctly distinguished.
- The pinned values for `#[0-10]` and `#[00-10]` from the grammar's ordering
  section.

## Replaceable elements

A naxp with `!` denotes a pair (L, ρ): the accepted language and the
canonicalisation map that replaces each replaceable element's match by its
rendering. W3 is precisely the requirement that ρ is a function.

**Lemma.** Given W1 and W3, ρ fixes every canonical form: ρ(ρ(w)) = ρ(w).
W1 puts each rendering among the strings its subject generates, so a canonical
form has a parse with renderings in the replaceable slots, and applying ρ along
that parse changes nothing; W3 says no other parse can disagree. It follows
that the canonical sublanguage C = ρ(L) equals the language of the naxp with
each `x!y` rewritten to `y`, which is what v0.3 already asserts about the count
of encodable values. C is a plain finite language with no replaceables, so
everything above applies to it.

**Definition, adopted 2026-08-06: encode(w) = rank of ρ(w) within C**, under
C's own order as defined above. Both ρ and C are semantic objects determined by the
naxp's meaning, and the rank over C is canonical by the plain-language result,
so the composite is canonical with no new proof. Choffrut's uniqueness theorem
for minimal subsequential transducers would give a canonical machine for ρ as
well, but nothing on the encoding's critical path needs it: an implementation
may realise ρ however it likes. W3 becomes a decidable check on a built
transducer, and on a finite language it is decidable by enumeration if nothing
cleverer is to hand.

### The weighted single machine is unsound

The design sketched earlier put `(charSet, weight, canonicalChar)` on each
transition of the L-machine, weight 1 inside a replaceable region. Two
well-formed naxps break it; both pass W1, W2 and W3.

**Order flips.** `(A|b)!bX|BY`. Here L = {AX, bX, BY} and C = {bX, BY}. On the
L-machine the first-position classes are `[Ab]` and `[B]`, and `[Ab]` sorts
first, giving the fibre {AX, bX} the value 1. But in C the classes are `[B]`
and `[b]`, and `[B]` sorts first: rank(BY) = 1, rank(bX) = 2. The collapse to
the canonical character moved the set across the sort order.

**Fibres span transitions.** `(a|A)!AX|AY`. Here L = {aX, AX, AY} with
ρ(aX) = AX, and a⁻¹L = {X} differs from A⁻¹L = {X, Y}, so `aX` and `AX` leave
the start state by different transitions yet must take the same value. No
assignment of per-transition weights whose offsets are prefix sums can express
that: whether a replaceable region's values duplicate another transition's
values depends on the rest of the naxp, not on the transition.

Under the composition definition both examples are unproblematic: encode is
rank-in-C of the canonical form, and the L-side machine is only ever used to
compute ρ. A product of the ρ-transducer with the C-machine remains available
as an implementation strategy where a single pass is wanted.

### The adopted reading

v0.3 says "the encoding is defined on the string with every replaceable
element replaced by its rendering", which left two readings. **Reading (a) is
adopted, 2026-08-06**: rank within C under C's own order, as defined above.
Decoding needs only the C-machine; a naxp with `!` encodes exactly like the
naxp with the renderings substituted; the digits-range ordering guarantee
transfers to canonical forms unchanged.

The rejected reading (b) ranked within C under L's order restricted to C. It
is also canonical, and it happens to rescue the first counterexample, but not
the second, and decoding under it needs fibre-aware structure on the
L-machine, which is the machinery just shown not to exist in per-transition
form. The two readings disagree in general: in `(A|b)!bX|BY`, reading (a)
gives BY the value 1 where (b) gives bX the value 1.

## What the encoding specification must still pin

- The total order itself: the set order (members ascending, compared as
  strings, empty first), class order at each position, character order within
  a class, end of text first. Stated at the language level, per above.
- The value-count bound. `CharacterCombinationCount` is a `ulong` and
  overflows silently (`State.cs`, `Transition.cs`). The specification must
  bound the count of encodable values, which is |C|, and say that a naxp
  exceeding the bound is rejected, together with when: the cap on interval
  counts exists so that this rejection is affordable. That cap was four
  digits when this note was written and is two from v0.5.
- The composition definition for `!` as adopted: encode(w) is the rank of
  ρ(w) within C under C's own order. The spec must state this normatively,
  since it is what makes the two counterexamples above encode determinately.
