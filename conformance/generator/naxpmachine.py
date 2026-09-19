# Copyright (c) Tim Gordon.
# This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

"""The algorithm: derivatives, the minimal machine, and the encoding computed by walking it.

`naxpref` defines the encoding as a rank in an order over the canonical language, with no machine
anywhere. This computes the same numbers the other way, and the two agreeing on every case small
enough to enumerate is what the test data rests on. It also reaches the cases enumeration cannot,
a naxp of 10^19 encoded values being perfectly ordinary and quite unlistable.

The machine is built by Brzozowski derivatives over a normalised expression, then minimised
exactly. Minimising is cheap here because a naxp's language is always finite, so the machine is
acyclic and its states can be interned bottom up in reverse topological order: two states with the
same accepting flag and the same interned transitions have the same residual language, and get the
same identity. That is the unique minimal trim acyclic machine the encoding is defined against.

Transitions are merged by the state they lead to, which is what makes the transition sets of the
minimal machine exactly the *first classes* of the order: two characters share a class when their
continuations are equal, and they lead to one state when their derivatives are.

An expression is a tuple:

    ('void',)                 matches nothing
    ('eps',)                  matches the empty string
    ('chars', frozenset)      one position
    ('cat', first, rest)      one after the other, always binary
    ('or', frozenset)         alternatives

`cat` is binary and right-nested so that a derivative rebuilds only the head of the chain. A flat
tuple would copy the whole of it at every step, which turns a naxp of a few thousand positions
from linear into quadratic.
"""

import naxpref as ref

VOID = ("void",)
EPS = ("eps",)


# --- Smart constructors ---------------------------------------------------


def rx_chars(characters):
    return ("chars", frozenset(characters)) if characters else VOID


def rx_cat(first, rest):
    if first == VOID or rest == VOID:
        return VOID

    if first == EPS:
        return rest

    if rest == EPS:
        return first

    return ("cat", first, rest)


def rx_seq(parts):
    result = EPS

    for part in reversed(list(parts)):
        result = rx_cat(part, result)

    return result


def rx_or(parts):
    flattened = set()

    for part in parts:
        if part == VOID:
            continue

        if part[0] == "or":
            flattened |= part[1]
        else:
            flattened.add(part)

    if not flattened:
        return VOID

    if len(flattened) == 1:
        return next(iter(flattened))

    return ("or", frozenset(flattened))


# --- From a tree to an expression -----------------------------------------


def rx_of(node, canonical):
    """The expression for a tree.

    With `canonical` false this is *L*, where a unified element accepts what its subject accepts.
    With it true this is *C*, where a unified element is its rendering, which is the rewrite the
    specification defines the canonical naxp by.
    """
    kind = node[0]

    if kind == "empty":
        return EPS

    if kind == "chars":
        return rx_chars(node[1])

    if kind == "seq":
        return rx_seq(rx_of(child, canonical) for child in node[1])

    if kind == "alt":
        return rx_or(rx_of(child, canonical) for child in node[1])

    return rx_of(node[2] if canonical else node[1], canonical)


# --- Nullability, first sets and derivatives ------------------------------


def nullable(r, memo=None):
    memo = {} if memo is None else memo

    if r in memo:
        return memo[r]

    kind = r[0]

    if kind in ("void", "chars"):
        answer = False
    elif kind == "eps":
        answer = True
    elif kind == "cat":
        answer = nullable(r[1], memo) and nullable(r[2], memo)
    else:
        answer = any(nullable(part, memo) for part in r[1])

    memo[r] = answer

    return answer


def first_chars(r, memo=None):
    """Every character that can begin a string the expression matches.

    Deriving on these alone rather than on the whole repertoire is what keeps a naxp of a few
    thousand positions affordable.
    """
    memo = {} if memo is None else memo

    if r in memo:
        return memo[r]

    kind = r[0]

    if kind in ("void", "eps"):
        answer = frozenset()
    elif kind == "chars":
        answer = r[1]
    elif kind == "cat":
        answer = first_chars(r[1], memo)

        if nullable(r[1]):
            answer = answer | first_chars(r[2], memo)
    else:
        answer = frozenset()

        for part in r[1]:
            answer = answer | first_chars(part, memo)

    memo[r] = answer

    return answer


def derivative(r, c, memo=None):
    """What is left of the expression once it has matched `c`."""
    memo = {} if memo is None else memo
    key = (r, c)

    if key in memo:
        return memo[key]

    kind = r[0]

    if kind in ("void", "eps"):
        answer = VOID
    elif kind == "chars":
        answer = EPS if c in r[1] else VOID
    elif kind == "cat":
        stepped = rx_cat(derivative(r[1], c, memo), r[2])
        answer = rx_or([stepped, derivative(r[2], c, memo)]) if nullable(r[1]) else stepped
    else:
        answer = rx_or([derivative(part, c, memo) for part in r[1]])

    memo[key] = answer

    return answer


# --- The minimal machine --------------------------------------------------


class State:
    """One state of the minimal machine: whether it accepts, and where each class of character
    leads."""

    __slots__ = ("accepting", "transitions", "count")

    def __init__(self, accepting, transitions):
        self.accepting = accepting
        #: (characters, next state), in the set order the encoding is defined by.
        self.transitions = transitions
        self.count = None


class Machine:
    def __init__(self, start, states):
        self.start = start
        self.states = states

    @property
    def state_count(self):
        return len(self.states)

    @property
    def string_count(self):
        return self.start.count


class TooManyStates(Exception):
    def __init__(self, built):
        super().__init__(f"more than {ref.MAX_STATES} states")
        self.built = built


def build(r, max_states=ref.MAX_STATES):
    """The minimal machine for an expression, or TooManyStates where W6 refuses it.

    The build stops as soon as the budget is passed, so a naxp that breaks W6 is found invalid
    without first being expanded into something oversized, which is the reason the specification
    caps an interval count.
    """
    if r == VOID:
        raise ValueError("an expression matching nothing has no machine")

    memo = {}
    order = []
    edges = {}
    seen = set()
    pending = [r]

    while pending:
        current = pending.pop()

        if current in seen:
            continue

        seen.add(current)

        if len(seen) > max_states + 1:
            raise TooManyStates(len(seen))

        by_next = {}

        for c in sorted(first_chars(current)):
            after = derivative(current, c, memo)

            if after == VOID:
                continue

            by_next.setdefault(after, set()).add(c)

            if after not in seen:
                pending.append(after)

        edges[current] = by_next
        order.append(current)

    # Reverse topological order: a state's successors are strictly closer to the end of a string
    # than it is, the language being finite, so ranking by the longest string from each orders
    # them safely.
    depth = {}

    def longest(expression):
        if expression in depth:
            return depth[expression]

        depth[expression] = 0
        answer = 0

        for after in edges[expression]:
            answer = max(answer, 1 + longest(after))

        depth[expression] = answer

        return answer

    for expression in order:
        longest(expression)

    interned = {}
    states = []
    state_of = {}

    for expression in sorted(order, key=lambda e: depth[e]):
        transitions = []

        for after, characters in edges[expression].items():
            transitions.append((frozenset(characters), state_of[after]))

        transitions.sort(key=lambda t: ref.set_order_key(t[0]))

        signature = (
            nullable(expression),
            tuple((ref.set_order_key(chars), id(state)) for chars, state in transitions),
        )

        if signature not in interned:
            state = State(nullable(expression), tuple(transitions))
            interned[signature] = state
            states.append(state)

        state_of[expression] = interned[signature]

    if len(states) > max_states:
        raise TooManyStates(len(states))

    machine = Machine(state_of[r], states)
    count_strings(machine)

    return machine


def count_strings(machine):
    """How many strings each state reaches, which is what the encoding counts past.

    Counted from the far end back: a state's successors are all strictly closer to the end of a
    string than it is, so ordering by the longest string still to come puts every state after the
    ones it needs.
    """
    distance = {}

    for state in machine.states:
        _distance(state, distance)

    for state in sorted(machine.states, key=lambda s: distance[id(s)]):
        total = 1 if state.accepting else 0

        for characters, after in state.transitions:
            total += len(characters) * after.count

        state.count = total


def _distance(state, memo):
    if id(state) in memo:
        return memo[id(state)]

    answer = 0

    for _, after in state.transitions:
        answer = max(answer, 1 + _distance(after, memo))

    memo[id(state)] = answer

    return answer


# --- The encoding, by walking the machine ---------------------------------


def encode(machine, text):
    """The encoded value of a string of the machine's language, or 0 where it is not one.

    The walk is the order read off the machine. At each state the empty string comes first, then
    the transitions in the set order of their character classes, and within one class the
    characters in ASCII order, which is exactly what the specification says.
    """
    state = machine.start
    passed = 0

    for i, c in enumerate(text):
        if state.accepting:
            passed += 1

        stepped = False

        for characters, after in state.transitions:
            if c in characters:
                passed += sorted(characters).index(c) * after.count
                state = after
                stepped = True
                break

            passed += len(characters) * after.count

        if not stepped:
            return 0

    return passed + 1 if state.accepting else 0


def decode(machine, value):
    """The string an encoded value stands for, or None where the machine does not produce it."""
    if value < 1 or value > machine.string_count:
        return None

    state = machine.start
    remaining = value - 1
    text = []

    while True:
        if state.accepting:
            if remaining == 0:
                return "".join(text)

            remaining -= 1

        for characters, after in state.transitions:
            block = len(characters) * after.count

            if remaining < block:
                index, remaining = divmod(remaining, after.count)
                text.append(sorted(characters)[index])
                state = after
                break

            remaining -= block
        else:
            return None
