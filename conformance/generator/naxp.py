# Copyright (c) Tim Gordon.
# This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

"""The facade: a naxp, every rule decided, and the encoding computed twice.

The point of computing it twice is that the two routes share no reasoning. `naxpref` ranks the
canonical language under the order the specification defines, with no machine anywhere.
`naxpmachine` builds the minimal machine by derivatives and counts past the strings that sort
first. Either could be wrong on its own. Both being wrong the same way is the thing this is meant
to make unlikely, and it is the reason the data is worth more than an implementation's output.

Where the accepted language is too large to list -- 10^19 strings is an ordinary naxp -- only the
machine can answer, and `agreed` says so rather than pretending otherwise.
"""

import naxpref as ref
import naxpmachine as machine
import naxptx as tx

#: Above this, a language is not enumerated and only the machine route is available.
ENUMERATION_LIMIT = 100_000


class Naxp:
    """A well-formed naxp, with both routes to its encoding built."""

    def __init__(self, pattern):
        self.pattern = pattern
        self.tree = ref.parse(pattern)

        # W2 before W1, because W1 reads inside both operands of a `!` and is only meaningful
        # once nothing is hidden in there. W4 was decided by the parser, where the counts are.
        ref.check_w1_and_w2(self.tree)

        self.transduction = tx.tx_of(self.tree)
        self.has_unified = ref.contains_unified(self.tree)

        # A naxp with no `!` satisfies W3 vacuously, rho being the identity. That is the only
        # short-circuit the rule admits.
        self.w3_configurations = 1 if not self.has_unified else tx.check_w3(self.transduction)

        try:
            self.canonical_machine = machine.build(machine.rx_of(self.tree, True))
            self.accepted_machine = machine.build(machine.rx_of(self.tree, False))
        except machine.TooManyStates as e:
            raise ref.Invalid("W6", f"this naxp needs {e.built} states or more") from None

        if self.w3_configurations > ref.MAX_STATES:
            raise ref.Invalid("W6", "canonicalising this naxp needs more states than W6 allows")

        if self.max_encoded_value > ref.MAX_ENCODED_VALUE:
            raise ref.Invalid("W5", f"this naxp has {self.max_encoded_value} encoded values")

        self.accepted = ref.accepted_language(self.tree, ENUMERATION_LIMIT)
        self.canonical = (
            None if self.accepted is None
            else ref.accepted_language(ref.canonical_tree(self.tree), ENUMERATION_LIMIT)
        )

    # -- what the data records

    @property
    def max_encoded_value(self):
        return self.canonical_machine.string_count

    @property
    def accepted_count(self):
        return self.accepted_machine.string_count

    @property
    def state_counts(self):
        """The four machines W6 caps, in the order the rule names them."""
        return {
            "canonical": self.canonical_machine.state_count,
            "accepted": self.accepted_machine.state_count,
            "canonicalising": self.w3_configurations,
            "pair": self.w3_configurations,
        }

    def encode(self, text):
        """The encoded value of a piece of text, or 0 where the naxp does not accept it."""
        forms = self.canonical_form(text)

        if forms is None:
            return 0

        return machine.encode(self.canonical_machine, forms)

    def decode(self, value):
        return machine.decode(self.canonical_machine, value)

    def canonical_form(self, text):
        """The canonical form of a piece of text, or None where the naxp does not accept it.

        Computed by walking the tree and by walking the transduction, which are different
        algorithms, and W3 has already said there is only one answer for either to find.
        """
        by_tree = ref.outputs_of(self.tree, text)
        by_transduction = tx.canonical_form(self.transduction, text)

        if by_tree != by_transduction:
            raise AssertionError(
                f"{self.pattern!r} canonicalises {text!r} to {sorted(by_tree)} by the tree and "
                f"{sorted(by_transduction)} by the transduction")

        if not by_tree:
            return None

        return next(iter(by_tree))

    # -- the two routes

    @property
    def enumerable(self):
        return self.accepted is not None

    def agreed_values(self):
        """Every encoded value, checked by both routes where both can answer.

        Returns the values of the canonical language by ranking it directly, having first made
        the machine agree with every one of them. Where the language is too large to list this
        returns None and only the machine has spoken.
        """
        if not self.enumerable:
            return None

        ranked = ref.ranked(self.canonical)

        if len(ranked) != self.max_encoded_value:
            raise AssertionError(
                f"{self.pattern!r} has {len(ranked)} canonical strings by enumeration and "
                f"{self.max_encoded_value} by the machine")

        for word, value in ranked.items():
            walked = machine.encode(self.canonical_machine, word)

            if walked != value:
                raise AssertionError(
                    f"{self.pattern!r} ranks {word!r} at {value} and walks it to {walked}")

            back = machine.decode(self.canonical_machine, value)

            if back != word:
                raise AssertionError(
                    f"{self.pattern!r} decodes {value} to {back!r} and should give {word!r}")

        if len(self.accepted) != self.accepted_count:
            raise AssertionError(
                f"{self.pattern!r} accepts {len(self.accepted)} strings by enumeration and "
                f"{self.accepted_count} by the machine")

        return ranked


def check(pattern):
    """The rule a naxp breaks, or None where it is well formed.

    Every rule is decided here, W6 included, which is what lets the data hold cases for it.
    """
    try:
        Naxp(pattern)
    except ref.Invalid as e:
        return e.rule

    return None
