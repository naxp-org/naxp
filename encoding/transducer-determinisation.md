# Determinising ρ into a machine

**Status: working note, 2026-08-18.** This is an adversarial review of the construction in
`src/cs/Naxp/TxMachine.cs`, which determinises the canonicalising transduction ρ so that the
language emitters have a table to write out. Grammar references are to version 0.4 of the grammar, which is not published yet;
the composition definition of the encoding is in `encoding/canonicity.md`; the squaring
construction that decides W3, and the exponential lower bound this review leans on, are in
`encoding/w3-functionality.md`. Every figure quoted below was measured, not predicted: a scratch
program outside the repo compiled the `src/cs/Naxp` sources directly, ran the builder on each naxp
named, and re-checked the witness naxps against `Canonicaliser` by full enumeration.

## Summary

Question 1 splits. Termination is confirmed, though the reason the code gives is the wrong one:
what terminates the construction is acyclicity, since every transition consumes a character and
`MaxLength` strictly decreases, and the delay bound the doc comment cites is true but carries no
weight. The size half fails. The number of states is exponential on a legal naxp:
`[ab]{k}c|([ab]!a){k}d` builds exactly 2<sup>k+1</sup> states, measured from k = 4 to 15, so at
k = 16 the builder refuses a naxp the compiler has just accepted. That family comes from
`encoding/w3-functionality.md`, and its lower-bound argument applies here in full, because this
construction is exactly the determinisation the W3 check was moved off. The code is not at
fault; an online finite-state canonicaliser for that family needs those states. The consequence
is that `TryBuild` can fail on a compiled naxp, which the remark on `TxMachineBuilder` says
cannot happen, and which whatever sits above this machine must handle before the C# emitter is
written.

Question 2 is confirmed on all three parts. The narrowing rule is sufficient; keeping a marker
inside the committed prefix is sound, and turns out to be possible only when every pending is
identical; and given that the runtime resolves a marker against the character read at that very
transition, no weaker rule works.

Question 3 is confirmed, subject to one invariant the argument silently uses: a `Tx` node that
is not `EmptySet` denotes a non-empty language. The factories maintain it by construction, so
the empty-residual case the question asks about cannot arise.

Question 4: the two `Violation()` sites and the end-of-text disagreement are unreachable when
the builder runs after `W3Checker`, and were verified to fire when it is bypassed, so they
should stay as defence in depth. `TooLarge()` is reachable, by the family above. The class
remark that every reported failure duplicates one of the checker's is therefore false.

Question 5: the machine is not minimal, witnessed by
`A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)`, which builds 8 states where 5 suffice. Every realistic naxp
measured was already minimal, the postcode included, so a minimisation pass is cheap insurance
rather than a blocker. A minimal machine here would not be canonical the way `StateMap` is, and
nothing downstream needs it to be.

## The construction, stated against the earlier review

The identity proved in `encoding/w3-functionality.md` carries over unchanged. After reading a
prefix *u* the live branch set F(*u*) satisfies ρ̂(*u·v*) = { c(*u*)·p·(emissions over *v*) }
ranging over branches and parses, where c(*u*) is the committed output. The builder's states are
the LCP-stripped branch sets K(*u*), its transition outputs realise c, and `EndOutput` realises
the set {p·f} over accepting branches, refused unless it is a singleton. Lemmas 1 to 3 of that
review (commit safety, verdict invariance, sharing safety) were proved for exactly this
stripping, so correctness of the machine on a W3-passing naxp follows from them plus the two
things this review checks fresh: the marker mechanics, which the earlier review did not have in
this form, and the dedupe.

One structural difference from `W3Checker` is worth recording. The checker refines its minterms
so that every rendering character stands alone, because a copy compared against a rendering
resolves differently across a block. The builder does not refine at all. It represents the copy
symbolically as `Tx.CopyMarker` and narrows only when the symbol would outlive the step. That is
sound, because the copied character is the only emission that is not uniform over a block, and
the marker keeps it undecided until the LCP computation reveals whether its identity matters.
The two tactics reach the same verdicts; the builder's is lazier and keeps blocks wide in the
common case.

## Question 1: termination and size

**Termination: confirmed, on acyclicity.** Every transition consumes one character, and the
input projection of every branch residual strictly drops its `MaxLength`, so all branches of a
state at depth *j* have `MaxLength` at most the longest accepted string less *j*. The reachable
graph is therefore a DAG of depth at most the longest string of *L*, and the BFS visits each
state once. The doc comment's reason, that a finite language bounds the delay, is true — a
pending is a suffix of the output of a partial parse, every residual is completable, so a
pending never exceeds the longest string of *C* — but it is neither what stops the recursion nor
what bounds the machine. Bounded delay with unboundedly many residual sets would still be
infinite, and bounded delay with exponentially many stripped branch sets is exactly what
happens next.

**Size: refuted as an affordability claim.** For `[ab]{k}c|([ab]!a){k}d` the scratch program measured
32, 64, 128, … 65 536 states for k = 4 … 15: exactly 2<sup>k+1</sup>. At k = 16 the count would
be 131 072, which exceeds `NaxpLimits.MaxStates` = 100 000, and the observed behaviour is that
`Compiler.TryCompile` succeeds and `TxMachineBuilder.TryBuild` then fails with
`ImplementationLimit`. At k = 17, the same. The blow-up cannot be engineered away, because the
lower-bound argument in `encoding/w3-functionality.md` is about the function being computed:
after reading *w* the machine may have emitted at most *w*'s leading run of `a`s, and on then
reading `c` it must emit the rest of *w*, so its state must determine *w* beyond that run, and
there are 2<sup>k</sup> such *w*. Any determinisation of ρ into a finite-state online emitter
pays this. The squaring construction escaped it for W3 only because a verdict needs less
information than an output stream.

The same states are visible in miniature in the postcode. After `\A\A?\9`, a digit may be the
`\X` or, with the space skipped, the second-part `\9`, and which it was decides whether the
canonical form puts a space before it or after it. The machine cannot emit the digit yet, so it
holds one state per digit read: ten states whose transitions are `[ ]/'0'`, `[0-9]/'0 ￿'`,
`[A-Z]/' 0￿'` and so on through `'9'`. Nineteen states in all, of whose 51 transitions 22 are
singletons. This is the intrinsic cost of finite-state output, at a size that is entirely
affordable; the k-family is the same cost at a size that is not.

**The `MaxSkippedCopies` interaction.** The cap of 64 bounds the per-step fan-out where skipping
a copy of an interval emits, which needs a replaceable with a nullable subject inside an
interval whose count can vary. It plays no part in the 2<sup>k</sup> blow-up, whose branches come
from alternation, and it cannot be reached by the builder on a compiled naxp at all: the checker
computes the same derivatives first, so `(A!!){66}` is refused during compilation, while
`(A!!){65}` compiles and builds a machine of 66 states. Two things are off about that refusal,
though neither is in `TxMachine.cs`. The naxp is legal — every parse of `(A!!){66}` emits
exactly 66 `A`s, so it passes W3 — and the message it gets is the checker's pair-state message,
which blames a count of pair states when the cause was the skip cap.

## Question 2: the marker rule

**Keeping a marker in committed output is sound.** The facts that make it so are narrower than
they first look. A marker can only enter a pending from the current step, because
`Branch.Pending` is marker-free by the invariant re-established below; each move emits at most
one marker, since one character is consumed per step and only the consuming `Chars` node emits
symbolically, with skipped elements contributing concrete eot text before it; and the marker is
therefore the final character of any pending that holds one. Now suppose the LCP contains a
marker. Every pending has the marker at that offset, and since each pending's marker is its last
character, every pending ends there, so all pendings are equal and the LCP is the whole of each.
A marker is committed only when every live parse owes exactly the same string, ending in the
character just read. The transition output is then correct for every parse and every character
of the block, and `TxMachine.AppendOutput` resolves it against the character the walk just
consumed, which is the character every parse meant.

**The rule is sufficient.** If a marker falls at or beyond the LCP, the builder retries the step
one character at a time, and a singleton block produces concrete emissions, so the retry cannot
recur — the guard that throws on a single-character block is genuinely unreachable. After the
subtraction, every surviving pending is a suffix of a marker-free-or-committed string beyond the
LCP, so `Branch.Pending` stays marker-free, which closes the invariant.

**The rule is necessary, for this machine.** The runtime resolves every marker in a transition's
output against the character read on that transition. A marker carried in a pending would be
committed by some later transition and resolved against that later character, which is the wrong
one. So no representation that keeps this runtime can narrow less. Nor is the narrowing wasteful
in a way a cleverer rule could fix: whenever a marker survives past the LCP, the machine owes
the concrete character as future output, so its states must distinguish which character was
read whether the transition was narrowed or not. The ten digit states of the postcode are forced
by the function, and so is the k-family. The only way out is a different machine model — output
registers, or a buffer the emitted code copies input spans from — which is a design question for
the emitters, noted at the end, and no criticism of this construction.

## Question 3: the dedupe

**Confirmed, given the non-emptiness invariant.** The reasoning in `TryStep` is: two parses of
one input reach the same residual owing different outputs, so any accepted continuation gives
one input two canonical forms. The step the question probes is "any accepted continuation": if
the residual's language were empty there would be none, and the refusal would be wrong. It
cannot be empty. Both factories normalise emptiness away structurally — an empty character set
becomes the `EmptySet` node, an `EmptySet` child annihilates a concatenation, a union of
nothing is `EmptySet`, and derivatives return `EmptySet` where nothing follows — so by induction
every other node denotes a non-empty language, and moves are only ever created with such
residuals. A completion therefore exists; fix one parse of it, run it from the shared residual,
and both branches append the same suffix to different pendings. The state being stepped is
reachable by construction, so a witness input exists, and the naxp is genuinely ill-formed.

One wrinkle the code comment does not mention: at the moment of the comparison the pendings can
still hold markers, and the equality test is textual. That is right, but it deserves its
argument. All markers in one step denote the same read character, so textually equal pendings
denote equal strings whatever was read. Textually unequal pendings, each holding at most one
marker as its final character, cannot denote equal strings for more than one character of the
block, and a block that produced a marker holds at least two; move existence is uniform over a
minterm, so the differing character realises both parses. A textual mismatch therefore implies a
genuine one. No false refusal, and no missed one.

## Question 4: the refusal paths

**The `Violation()` paths are unreachable on a compiled naxp.** Each of the three triggers — an
`EotKind.Multiple`, a disagreement between accepting branches' end outputs, and the dedupe —
certifies an input with two canonical forms, by the arguments above and the `SkipsAmbiguously`
remark in `Tx.cs`. The checker is complete, per `encoding/w3-functionality.md`, so any naxp that
would trip them was refused before the builder ran. The scratch program confirmed the other half: with
`W3Checker` bypassed, the builder alone refused all six of `[ab]|[ab]!a`, `A!!|()`, `A!?|A!!`,
`A!!A?`, `AB!!B?C` and `(A!!){0,2}`. They should stay. `TryBuild` is callable on any `Tx`, the
checks cost almost nothing, and a machine built from an unchecked expression would be silently
wrong rather than refused, which is what the class remark already argues.

**`TooLarge()` is reachable, and the class remark is false.** `[ab]{16}c|([ab]!a){16}d`
compiles and then fails `TryBuild` at the state cap. The remark on `TxMachineBuilder` — "Every
failure this reports is one `W3Checker` would already have reported, since both decide
single-valuedness over the same derivatives" — conflates the violation paths, where it is true,
with the size path, where it is not: the square decides that family in a few dozen pair states
precisely because it tracks less than the determinisation must. The `TooLong` route into
`TooLarge()` is dead on a compiled naxp, since every builder-reachable residual appears as a
diagonal pair in the square with the same cached derivatives, so the checker hits any oversized
skip or eot first. The state-cap route is alive, and it is the one failure mode of this file
that reaches users with legal naxps.

## Question 5: minimality

**The machine is not minimal.** States are deduplicated on the stripped branch set, which is a
property of how the expression was written, and behaviourally equal states with different branch
sets do occur. Witness: `A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)`, a well-formed naxp whose two arms
denote the same transduction after their first character. After `AQ` the live branch is
`Repl·X·(B|C)`; after `BQ` it is `Repl·(XB|XC)`; structurally distinct residuals, identical
behaviour. One step later the first arm holds the single branch `(B|C)` where the second holds
the pair {`B`, `C`}, distinct again. The built machine has 8 states; a bottom-up behavioural
merge — reverse topological order, hash-consing on end output plus the transition list with
targets replaced by their merged class — leaves 5.

**Measured impact: nil on everything realistic.** With that merge implemented exactly in the
program, every other naxp tested was already minimal: the postcode (19 states), `(A|a)!A`,
`(A|BB|CCC)!(BB)`, `(ABC|abc|AbC)!(ABC)`, `((AA|aa)!(AA)){2}`, `((()|A)!(A)){3}`,
`(A|a)!A(B|b)!B|(A|a)!A(C|c)!C`, and `[ab]{8}c|([ab]!a){8}d` at 512 of 512, since the k-family's
states are all behaviourally distinct. Duplication needs the same transduction region written
two structurally different ways on converging paths, which real naxps have little reason to
contain. So a minimisation pass is worth having before the C# emitter — every duplicate state
becomes a duplicated `switch` case in generated code, the pass is around fifty lines on an
acyclic machine, and it also enables re-merging transitions that share an output and a target —
but it is insurance, and it does not gate the emitter's design the way the size question does.

**A minimal machine here would still not be canonical.** `StateMap` is canonical because
hash-consing on behaviour lands on the Myhill–Nerode machine, unique from the language alone.
The merged transducer is minimal only relative to this construction's emission timing, which is
"commit when every live parse agrees", and live parses are parses of the spelling. Choffrut's
canonical minimal sequential transducer requires output normalisation on top: pushing every
output as early as the function allows, with an initial output string. This machine is visibly
not in that form — for `(A|BB|CCC)!(BB)` the whole language canonicalises to `BB`, and an onward
machine would emit `BB` on the first transition where this one emits nothing until end of text.
Choffrut normalisation would buy a spelling-independent machine at the cost of longer lookahead
in the delays, and nothing on the project's critical path wants it: the encoding's canonicity
rests on `StateMap` and on ρ being a function, settled in `encoding/canonicity.md`, and the
emitters need a correct table, not a unique one. The determinism test
`Build_OfTheSameNaxpTwice_GivesTheSameShape` already pins what matters, reproducibility.

## Findings outside the questions

No correctness bug was found in the machine or the builder. The scratch program re-verified full
enumeration agreement with `Canonicaliser` on the witness naxps used above, on top of the
author's 80 000-string evidence.

Three doc comments claim more than the code does.

- `TxMachineBuilder`'s class remark, quoted under question 4, is false for the state cap.
- `TxState`'s remark says the machine is shared "in the same way `StateMap` is". `StateMap`
  shares on behaviour and is minimal for it; this shares on the branch set and is not, which is
  the whole of question 5. The remark should claim identity of parse sets, nothing more.
- `TxMachine`'s remark, "It terminates because a naxp denotes a finite language, so the delay is
  bounded", offers the delay bound as the termination argument. Acyclicity is the argument, and
  the sentence invites the affordability misreading that question 1 refutes.

One message defect sits next door: `W3Checker`'s `TooLarge()` text blames the pair-state budget,
but the same error is returned for a `TooLong` derivative, so `(A!!){66}` — refused because of
`TxFactory.MaxSkippedCopies`, with two pair states explored — is told it needed more than
100 000 pair states.

And one decision belongs above this file but blocks the emitter. A legal naxp can now compile
and still have no machine. The emitters cannot be written until the pipeline says what happens
then: refuse the naxp at compile time by running the builder eagerly, fall back to the tree walk
at runtime and have the generator decline only the generated-canonicaliser feature, or emit
canonicalisation in a different model — a buffered mark-and-copy pass escapes the
2<sup>k</sup> bound entirely, because the bound is about finite-state online emission, and it
would also dissolve the per-character states the narrowing creates. That choice shapes what the
C# generator emits, so it comes first.

## Test cases

Naxps this review adds, with the observed outcome and the reason each earns a place.

- `[ab]{16}c|([ab]!a){16}d` — compiles, then `TryBuild` fails at the state cap. The one
  reachable failure of the builder on a legal naxp; pins whatever pipeline behaviour is chosen.
  With a lowered cap the family exercises the same path cheaply at small k.
- `[ab]{k}c|([ab]!a){k}d` for a small k — builds 2<sup>k+1</sup> states; a regression pin on
  the growth rate, and on the fact that behavioural merging cannot shrink it.
- `A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)` — well formed, 8 states, 5 after merging. The
  non-minimality witness; becomes the merge pass's first test if one is written.
- `(A!!){65}` and `(A!!){66}` — the first compiles and builds 66 states; the second is legal
  but refused during compilation by the skip cap, with a message that misstates the cause.
- The six bypass cases `[ab]|[ab]!a`, `A!!|()`, `A!?|A!!`, `A!!A?`, `AB!!B?C`, `(A!!){0,2}` —
  each refused by the builder alone with `W3Checker` skipped, which is the only way the
  `Violation()` paths can be exercised and the reason they stay.
- The postcode `\A\A?\9\X?\s!!\9\A\A` — 19 states with ten single-digit states; pins the
  narrowing behaviour and the intrinsic cost it pays.

## What should change in `src/cs/Naxp/TxMachine.cs`

1. Rewrite the `TxMachineBuilder` class remark. The violation checks duplicate `W3Checker`; the
   state cap does not, and legal naxps reach it. Name `[ab]{16}c|([ab]!a){16}d` as the witness.
2. Rewrite the sharing sentence in `TxState`'s remark so it claims identity of branch sets, and
   not parity with `StateMap`.
3. Replace the termination sentence in `TxMachine`'s remark with the acyclicity argument, and
   state plainly that the number of states can be exponential in the naxp's length even when
   both language machines are small, with the pointer into `encoding/w3-functionality.md`.
4. Decide, above this file, what a compiled naxp with no buildable machine does — eager build
   at compile time, runtime fallback to the tree walk, or a buffered emitter model — and record
   the decision where the emitters will find it. This precedes the C# emitter.
5. Add a bottom-up behavioural merge pass over the built machine, in reverse topological order,
   hash-consing on end output and the transition list with merged targets, and re-merge
   transitions that agree on output and target. Small, and worth doing before states are turned
   into generated `switch` cases; not urgent on the measured evidence.
6. Add the test cases above, including the bypass cases that exercise `Violation()`.
7. Optionally, give the builder's `Violation()` a witness string as `W3Checker` has, so that a
   defence-in-depth refusal is debuggable if it ever fires. Low value while unreachable.

## Postscript

Written before the C# emitter existed. `CSharpEmitter.cs` and `JavaScriptEmitter.cs` have since
been written and both emit from `TxMachine`, so every sentence above of the form "before the C#
emitter is written" has been overtaken. The numbered list is left as it stood: the merging pass in
item 5 was done, and the rest has not been revisited against the finished emitters. Read this as a
record of the reasoning rather than as a list of outstanding work.
