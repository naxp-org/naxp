# Copyright (c) Tim Gordon.
# This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

"""W3: the canonicalising transduction, and whether it is a function.

Every other rule can be read off the tree or off the machine. W3 cannot: it says that replacing
each unified element by its rendering gives exactly one result for every string the naxp accepts,
which is a statement about a transduction rather than about a language.

Where the accepted language is small enough to list, `naxpref.outputs_of` decides W3 by walking
the tree once per string, and that is the check to trust. This module decides it for the rest, by
determinising the transduction: a configuration is a set of threads, each a residual paired with
what it has emitted and not yet committed, and W3 fails at the first configuration where two
threads could finish with different output.

The longest common prefix of the pendings in a configuration is stripped, which is what keeps the
configuration space finite: what every thread has emitted is committed and cannot come apart
again, so only the difference matters. That loses the output itself, which is why this decides W3
and does not compute a canonical form; `canonical_form` below walks the same threads without
stripping when the answer is wanted for one string.

Determinising is the obvious construction and it is not the affordable one. There are naxps a few
dozen characters long whose configuration count is exponential -- `[ab]{17}c|([ab]!a){17}d` is
well formed and drives it through 2^17 -- and a production implementation has to decide W3 with a
product of the transduction with itself instead, pairwise rather than by sets. That matters for an
implementation held to W6. It does not matter here: this runs over naxps chosen for the test data,
and `MAX_CONFIGURATIONS` makes it say so rather than run away.
"""

import naxpref as ref
import naxpmachine as machine

TVOID = ("tvoid",)
TEPS = ("teps",)

#: Where determinising a transduction stops and says it cannot answer.
MAX_CONFIGURATIONS = 20_000


class NotDecidable(Exception):
    """The determinisation grew past what this generator will build."""


# --- Smart constructors ---------------------------------------------------


def t_cat(first, rest):
    if first == TVOID or rest == TVOID:
        return TVOID

    if first == TEPS:
        return rest

    if rest == TEPS:
        return first

    return ("tcat", first, rest)


def t_or(parts):
    flattened = set()

    for part in parts:
        if part == TVOID:
            continue

        if part[0] == "tor":
            flattened |= part[1]
        else:
            flattened.add(part)

    if not flattened:
        return TVOID

    if len(flattened) == 1:
        return next(iter(flattened))

    return ("tor", frozenset(flattened))


def tx_of(node):
    """The transduction of a tree: what it matches, and what it emits doing so."""
    kind = node[0]

    if kind == "empty":
        return TEPS

    if kind == "chars":
        return ("tchars", node[1])

    if kind == "seq":
        result = TEPS

        for child in reversed(node[1]):
            result = t_cat(tx_of(child), result)

        return result

    if kind == "alt":
        return t_or(tx_of(child) for child in node[1])

    # A unified element emits its rendering and nothing else, whatever its subject matched. W1
    # has already put that rendering among the strings the subject generates.
    rendering = next(iter(ref.generated_strings(node[2])))

    return ("tunified", machine.rx_of(node[1], False), rendering)


# --- Nullability, first sets and derivatives ------------------------------


def null_outputs(t, memo=None):
    """Everything the transduction can emit while matching the empty string."""
    memo = {} if memo is None else memo

    if t in memo:
        return memo[t]

    kind = t[0]

    if kind == "tvoid" or kind == "tchars":
        answer = frozenset()
    elif kind == "teps":
        answer = frozenset({""})
    elif kind == "tunified":
        answer = frozenset({t[2]}) if machine.nullable(t[1]) else frozenset()
    elif kind == "tcat":
        left, right = null_outputs(t[1], memo), null_outputs(t[2], memo)
        answer = frozenset(a + b for a in left for b in right)
    else:
        answer = frozenset()

        for part in t[1]:
            answer |= null_outputs(part, memo)

    memo[t] = answer

    return answer


def t_first_chars(t, memo=None):
    memo = {} if memo is None else memo

    if t in memo:
        return memo[t]

    kind = t[0]

    if kind in ("tvoid", "teps"):
        answer = frozenset()
    elif kind == "tchars":
        answer = t[1]
    elif kind == "tunified":
        answer = machine.first_chars(t[1])
    elif kind == "tcat":
        answer = t_first_chars(t[1], memo)

        if null_outputs(t[1], memo):
            answer = answer | t_first_chars(t[2], memo)
    else:
        answer = frozenset()

        for part in t[1]:
            answer = answer | t_first_chars(part, memo)

    memo[t] = answer

    return answer


def t_derivative(t, c):
    """Every (residual, emitted) the transduction reaches on one character.

    A unified element emits nothing here. What it emits is its rendering, and that is emitted when
    the element completes, which is what `null_outputs` reports and what the end of the text
    collects. Comparing pendings without that was the mistake that made an earlier design of this
    both miss violations and invent them.
    """
    kind = t[0]

    if kind in ("tvoid", "teps", ):
        return frozenset()

    if kind == "tchars":
        return frozenset({(TEPS, c)}) if c in t[1] else frozenset()

    if kind == "tunified":
        after = machine.derivative(t[1], c)

        if after == machine.VOID:
            return frozenset()

        return frozenset({(("tunified", after, t[2]), "")})

    if kind == "tcat":
        stepped = {(t_cat(rest, t[2]), out) for rest, out in t_derivative(t[1], c)}

        for committed in null_outputs(t[1]):
            for rest, out in t_derivative(t[2], c):
                stepped.add((rest, committed + out))

        return frozenset(stepped)

    stepped = set()

    for part in t[1]:
        stepped |= t_derivative(part, c)

    return frozenset(stepped)


# --- One string -----------------------------------------------------------


def canonical_form(t, text):
    """Every canonical form the transduction gives a string, by carrying every output.

    Nothing is stripped, so this answers with the forms themselves. It agrees with
    `naxpref.outputs_of`, which walks the tree instead and shares none of this reasoning.
    """
    threads = {(t, "")}

    for c in text:
        stepped = set()

        for current, pending in threads:
            for rest, out in t_derivative(current, c):
                stepped.add((rest, pending + out))

        threads = stepped

        if not threads:
            return frozenset()

    return frozenset(
        pending + finish
        for current, pending in threads
        for finish in null_outputs(current)
    )


# --- W3 -------------------------------------------------------------------


def strip_common_prefix(threads):
    pendings = [pending for _, pending in threads]
    shortest = min(pendings, key=len)
    cut = len(shortest)

    for i, c in enumerate(shortest):
        if any(pending[i] != c for pending in pendings):
            cut = i
            break

    return frozenset((current, pending[cut:]) for current, pending in threads)


def check_w3(t):
    """Decides W3, and returns how many configurations deciding it took.

    That count is what W6 measures for the machine that computes canonical forms and for the
    machine that decides W3, which this construction makes one and the same.
    """
    start = strip_common_prefix(frozenset({(t, "")}))
    seen = {start}
    pending = [start]
    configurations = 0

    while pending:
        configuration = pending.pop()
        configurations += 1

        finishes = {
            held + finish
            for current, held in configuration
            for finish in null_outputs(current)
        }

        if len(finishes) > 1:
            witness = sorted(finishes)
            raise ref.Invalid(
                "W3",
                f"one string has more than one canonical form, {witness[0]!r} and {witness[1]!r}")

        characters = frozenset()

        for current, _ in configuration:
            characters = characters | t_first_chars(current)

        for c in sorted(characters):
            stepped = set()

            for current, held in configuration:
                for rest, out in t_derivative(current, c):
                    stepped.add((rest, held + out))

            if not stepped:
                continue

            after = strip_common_prefix(frozenset(stepped))

            if after not in seen:
                if len(seen) > MAX_CONFIGURATIONS:
                    raise NotDecidable(
                        f"determinising this transduction passed {MAX_CONFIGURATIONS} "
                        "configurations")

                seen.add(after)
                pending.append(after)

    return configurations
