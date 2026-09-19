# Copyright (c) Tim Gordon.
# This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

"""The language: the repertoire, the parser, the folds, and the rules decided from the tree.

Written from the specification and from nothing else. This module knows what a naxp is; it does
not know how any implementation of naxp is built, and it must never be made to agree with one.

A tree is a tuple whose first item names the node, which makes every node hashable and comparable
and lets the machine in `naxpmachine` intern them without a wrapper:

    ('empty',)                              the empty string
    ('chars', frozenset_of_characters)      one position
    ('seq', (child, ...))                   in sequence
    ('alt', (child, ...))                   alternatives, in the order written
    ('unified', subject, rendering, form)   `x!y`, `x!!` or `x!?`

Two constructs of the syntax do not reach the tree. An interval and a decimal range both expand
to ordinary expressions and add no expressive power, so they are expanded here, once, after their
own constraints have been checked. The specification's own reason for capping an interval count
is that an implementation must be able to find a naxp invalid before expanding it, which this
does: `check_w4` runs on the counts as written.

The three unified forms are kept apart because W1 says different things about them, and because
`\\C` distributes into a fold branch differently from an ordinary `!`.
"""

import itertools

# --- The repertoire -------------------------------------------------------

RESERVED = frozenset("!#(),-?[\\]{|}*+.^$")

#: Everything a naxp can match: the printable characters and the space.
MATCHABLE = frozenset(chr(c) for c in range(0x20, 0x7F))

#: Everything the pattern of a naxp may hold, whitespace apart.
PATTERN_CHARS = frozenset(chr(c) for c in range(0x21, 0x7F))

WHITESPACE = frozenset("\t\n\r ")

BLOCK_ESCAPES = {
    "9": frozenset("0123456789"),
    "A": frozenset(chr(c) for c in range(0x41, 0x5B)),
    "a": frozenset(chr(c) for c in range(0x61, 0x7B)),
    "X": frozenset("0123456789") | frozenset(chr(c) for c in range(0x41, 0x5B)),
    "x": frozenset("0123456789") | frozenset(chr(c) for c in range(0x61, 0x7B)),
}

FOLD_LETTERS = frozenset("Cc")

#: The letters a backslash may introduce, which is what keeps the rest available for a later
#: version to spend.
ESCAPE_LETTERS = frozenset("s") | frozenset(BLOCK_ESCAPES) | FOLD_LETTERS

MAX_INTERVAL_COUNT_DIGITS = 2
MAX_BOUND_DIGITS = 15

#: W6. Part of the language, not of any implementation.
MAX_STATES = 2000

#: W5. What an unsigned 64 bit integer holds once zero is reserved.
MAX_ENCODED_VALUE = 2 ** 64 - 1

EXPLICIT, REPRODUCED, DROPPED, FOLD = "Explicit", "Reproduced", "Dropped", "Fold"

#: A guard on the two constructs that expand, so a mistake here is a message and not a hang. No
#: naxp the conformance data holds comes near it.
MAX_EXPANSION = 200_000


class Invalid(Exception):
    """A naxp that breaks a rule, carrying the rule it breaks."""

    def __init__(self, rule, reason):
        super().__init__(f"{rule}: {reason}")
        self.rule = rule
        self.reason = reason


def syntax_error(reason):
    return Invalid("syntax", reason)


# --- The parser -----------------------------------------------------------


class Parser:
    """`naxp ::= expr`, and the productions below it, straight from the grammar."""

    def __init__(self, text):
        for c in text:
            if c not in PATTERN_CHARS and c not in WHITESPACE:
                raise syntax_error(f"{c!r} cannot appear in the pattern of a naxp")

        self.text = text
        self.pos = 0

    # -- scanning

    def peek(self, ahead=0):
        at = self.pos + ahead

        return self.text[at] if at < len(self.text) else ""

    def advance(self, by=1):
        self.pos += by

    def skip_whitespace(self):
        while self.peek() in WHITESPACE and self.peek() != "":
            self.advance()

    # -- productions

    def parse(self):
        self.skip_whitespace()
        node = self.parse_expr()
        self.skip_whitespace()

        if self.pos != len(self.text):
            raise syntax_error(f"unexpected {self.peek()!r} at offset {self.pos}")

        return node

    def parse_expr(self):
        alternatives = [self.parse_seq()]

        while True:
            self.skip_whitespace()

            if self.peek() != "|":
                break

            self.advance()
            alternatives.append(self.parse_seq())

        return alternatives[0] if len(alternatives) == 1 else ("alt", tuple(alternatives))

    def parse_seq(self):
        """`seq ::= element+ | element* case_fold expr`

        A case fold is the loosest operator: it runs from where it is written to the end of the
        enclosing group, across any `|`. So a fold ends the sequence it is written in, taking the
        rest of the expression as its operand."""
        elements = []

        while True:
            self.skip_whitespace()

            fold = self.parse_fold()

            if fold is not None:
                elements.append(apply_fold(self.parse_expr(), fold))
                break

            c = self.peek()

            if c == "" or c in "|)":
                break

            elements.append(self.parse_element())

        if not elements:
            raise syntax_error("an alternative must contain at least one element")

        return elements[0] if len(elements) == 1 else ("seq", tuple(elements))

    def parse_element(self):
        """`element ::= operand quantifier? text_unification?`"""
        node = self.parse_base()
        self.skip_whitespace()

        quantified = False

        if self.peek() == "?":
            self.advance()
            node = ("alt", (("empty",), node))
            quantified = True
        elif self.peek() == "{":
            node = self.parse_interval(node)
            quantified = True

        self.skip_whitespace()

        if quantified and self.peek() in ("?", "{"):
            raise syntax_error("an atom may take only one quantifier")

        if self.peek() == "!":
            node = self.parse_unified(node, quantified and self.text[self.pos - 1] == "?")

        return node

    def parse_fold(self):
        """`case_fold ::= "\\C" | "\\c"`, as many as are written, returning whether upper case
        is canonical. The outer fold governs, so a run of folds is the first of them."""
        fold = None

        while self.peek() == "\\" and self.peek(1) in FOLD_LETTERS:
            letter = self.peek(1)
            self.advance(2)
            self.skip_whitespace()

            if fold is None:
                fold = letter == "C"

        return fold

    def parse_unified(self, subject, subject_is_optional):
        self.advance()

        if self.peek() == "!":
            self.advance()

            if subject_is_optional:
                raise syntax_error("`!!` carries its own `?`, so it cannot follow one")

            return ("unified", ("alt", (("empty",), subject)), subject, REPRODUCED)

        if self.peek() == "?":
            self.advance()

            if subject_is_optional:
                raise syntax_error("`!?` carries its own `?`, so it cannot follow one")

            return ("unified", ("alt", (("empty",), subject)), ("empty",), DROPPED)

        self.skip_whitespace()

        if self.peek() == "" or self.peek() in "|)":
            raise syntax_error("a `!` must be followed by its rendering; there is no bare `x!`")

        if self.peek() == "\\" and self.peek(1) in FOLD_LETTERS:
            raise syntax_error("a case fold cannot begin the rendering of a `!`")

        return ("unified", subject, self.parse_element(), EXPLICIT)

    def parse_interval(self, child):
        self.advance()
        low = self.parse_count()
        self.skip_whitespace()

        if self.peek() == ",":
            self.advance()
            high = self.parse_count()
        elif self.peek() == "-":
            raise syntax_error("the counts of an interval are separated by a comma")
        else:
            high = low

        self.skip_whitespace()

        if self.peek() != "}":
            raise syntax_error("this interval is not closed")

        self.advance()
        check_w4_interval(low, high)

        return expand_interval(child, int(low), int(high))

    def parse_count(self):
        self.skip_whitespace()
        start = self.pos

        while self.peek().isdigit():
            self.advance()

        digits = self.text[start:self.pos]

        if not digits:
            raise syntax_error("an interval count must be a run of digits")

        after = self.pos
        self.skip_whitespace()

        if self.peek().isdigit():
            raise syntax_error("the digits of an interval count cannot be separated by whitespace")

        self.pos = after

        return digits

    def parse_base(self):
        c = self.peek()

        if c == "(":
            self.advance()
            self.skip_whitespace()

            if self.peek() == ")":
                self.advance()

                return ("empty",)

            inner = self.parse_expr()
            self.skip_whitespace()

            if self.peek() != ")":
                raise syntax_error("this group is not closed")

            self.advance()

            return inner

        if c == "#":
            return self.parse_decimal_range()

        if c == "[":
            return ("chars", self.parse_bracket_set())

        return ("chars", self.parse_char_atom()[0])

    def parse_bracket_set(self):
        self.advance()
        members = set()

        while True:
            if self.peek() == "]":
                self.advance()

                if not members:
                    raise syntax_error("a character set must contain at least one character")

                return frozenset(members)

            if self.peek() == "":
                raise syntax_error("this character set is not closed")

            low, low_char, low_is_block = self.parse_char_atom()

            if self.peek() == "-" and not low_is_block:
                self.advance()
                high, high_char, high_is_block = self.parse_char_atom()

                if high_is_block:
                    raise syntax_error("a range runs between two single characters")

                if ord(high_char) < ord(low_char):
                    raise Invalid("W4", "a range must be written lowest first")

                members.update(chr(c) for c in range(ord(low_char), ord(high_char) + 1))
            else:
                members.update(low)

    def parse_char_atom(self):
        """One item of a character set: its set, its character where it has one, and whether it
        was a block escape, which is what a range may not be built from."""
        c = self.peek()

        if c == "\\":
            self.advance()
            escaped = self.peek()

            if escaped == "" or escaped in WHITESPACE:
                raise syntax_error("a `\\` must be followed by an escape letter or a reserved "
                                   "character")

            self.advance()

            if escaped == "s":
                return frozenset(" "), " ", False

            if escaped in BLOCK_ESCAPES:
                return BLOCK_ESCAPES[escaped], "", True

            if escaped in FOLD_LETTERS:
                raise syntax_error("a case fold applies to a whole element, so it cannot appear "
                                   "inside a character set")

            if escaped in RESERVED:
                return frozenset(escaped), escaped, False

            raise syntax_error(f"`\\{escaped}` is not an escape")

        if c == "" or c in RESERVED or c in WHITESPACE:
            raise syntax_error(f"{c!r} cannot appear here")

        self.advance()

        return frozenset(c), c, False

    def parse_decimal_range(self):
        self.advance()

        if self.peek() != "[":
            raise syntax_error("a `#` introduces a decimal range and must be followed by `[`")

        self.advance()
        low, marks = self.parse_bound(allow_marks=True)
        self.skip_whitespace()

        if self.peek() != "-":
            raise syntax_error("the bounds of a decimal range are separated by `-`")

        self.advance()
        high, _ = self.parse_bound(allow_marks=False)
        self.skip_whitespace()

        if self.peek() != "]":
            raise syntax_error("this decimal range is not closed")

        self.advance()
        check_w4_decimal_range(low, high, marks)

        if not any(marks):
            return expand_decimal_range(low, high)

        return expand_marked_decimal_range(low, high, marks)

    def parse_bound(self, allow_marks):
        """One bound, and the mark on each of its digits where the lower bound carries any.

        A mark is part of the digit's token, so nothing is skipped between the two, and a mark
        parted from its digit is a split token rather than a stray character.
        """
        self.skip_whitespace()
        digits = []
        marks = []

        while self.peek().isdigit():
            digit = self.peek()
            self.advance()
            digits.append(digit)
            mark = self.peek()

            if mark not in "!?":
                marks.append("")
                continue

            if not allow_marks:
                raise Invalid("W4", "only the lower bound of a decimal range may carry padding "
                                    "marks")

            if digit != "0":
                raise Invalid("W4", "only a zero may be marked, since a mark says that a padding "
                                    "zero is optional")

            marks.append(mark)
            self.advance()

        if not digits:
            raise syntax_error("a decimal range bound must be a run of digits")

        after = self.pos
        self.skip_whitespace()

        if self.peek() in "!?":
            raise syntax_error("a padding mark belongs to the zero before it, so whitespace may "
                               "not come between them")

        if self.peek().isdigit():
            raise syntax_error("the digits of a decimal range bound cannot be separated by "
                               "whitespace")

        self.pos = after

        return "".join(digits), marks


def parse(text):
    """The tree of a naxp, with folds, intervals and decimal ranges expanded."""
    return Parser(text).parse()


# --- W4: the constraints on the two counted constructs ---------------------


def check_w4_interval(low, high):
    for count in (low, high):
        if len(count) > MAX_INTERVAL_COUNT_DIGITS:
            raise Invalid("W4", "an interval count may have at most two digits")

    if int(low) > int(high):
        raise Invalid("W4", "the first count of an interval may not exceed the second")

    if int(high) == 0:
        raise Invalid("W4", "a fixed count of zero matches only the empty string; write `()`")


def check_w4_decimal_range(low, high, marks=None):
    for bound in (low, high):
        if len(bound) > MAX_BOUND_DIGITS:
            raise Invalid("W4", "a decimal range bound may have at most fifteen digits")

    if len(low) > len(high):
        raise Invalid("W4", "the lower bound may not have more digits than the upper")

    if len(high) > len(low) and high[0] == "0":
        raise Invalid("W4", "where the upper bound is wider, it may not have leading zeros")

    if int(low) > int(high):
        raise Invalid("W4", "the lower bound may not exceed the upper")

    # A mark stands for a padding zero, so it may sit only in front of the digits of the value.
    # Which positions those are is known once the bound has been read.
    if marks is not None and any(marks[len(low) - len(str(int(low))):]):
        raise Invalid("W4", "this zero is a digit of the number rather than padding in front of "
                            "it, so it cannot be marked")


# --- The two constructs that expand ---------------------------------------


def expand_interval(child, low, high):
    """`x{m,n}` is anything from m to n copies of x in sequence."""
    alternatives = []

    for count in range(low, high + 1):
        if count == 0:
            alternatives.append(("empty",))
        elif count == 1:
            alternatives.append(child)
        else:
            alternatives.append(("seq", tuple([child] * count)))

    if size_of(("alt", tuple(alternatives))) > MAX_EXPANSION:
        raise Invalid("W6", "this interval expands beyond what this generator will build")

    return alternatives[0] if len(alternatives) == 1 else ("alt", tuple(alternatives))


def expand_decimal_range(low, high):
    """`#[lo-hi]` is the decimal representations of the integers from lo to hi.

    The digits written in each bound fix the widths generated. A string of w digits belongs when
    its value is in range and it has no leading zero, unless w is the width of the lower bound,
    where leading zeros are the point: `#[00-105]` does not match `7` and `#[0-105]` does.
    """
    lo_width, hi_width, lo, hi = len(low), len(high), int(low), int(high)
    strings = []

    for width in range(lo_width, hi_width + 1):
        first = max(lo, 0 if width == lo_width else 10 ** (width - 1))
        last = min(hi, 10 ** width - 1)

        for value in range(first, last + 1):
            text = str(value).rjust(width, "0")

            if len(text) == width and (width == lo_width or text[0] != "0"):
                strings.append(text)

    if not strings:
        raise Invalid("W4", "this decimal range denotes nothing")

    if len(strings) * hi_width > MAX_EXPANSION:
        raise Invalid("W6", "this decimal range expands beyond what this generator will build")

    return alternatives_of_strings(strings)


def expand_marked_decimal_range(low, high, marks):
    """A decimal range whose lower bound carries padding marks, as in `#[0!0!0-105]`.

    A mark makes its zero optional on input and fixes what that position contributes to the
    canonical form: `0!` is `0!!` and `0?` is `0!?`. The shape is one alternative per width the
    value itself takes, with the padding positions that width leaves over written out in front.
    An unmarked padding position is a mandatory zero, which is what makes an unmarked leading zero
    go on setting a minimum width.

    A unified element contributes its rendering whether or not its subject matched anything, so a
    run of padding contributes a fixed string and it does not matter which of two identical zeros
    the input supplied.
    """
    lo_width, hi_width, lo, hi = len(low), len(high), int(low), int(high)
    alternatives = []

    for width in range(1, hi_width + 1):
        first = max(lo, 0 if width == 1 else 10 ** (width - 1))
        last = min(hi, 10 ** width - 1)

        if first > last:
            continue

        values = alternatives_of_strings([str(value) for value in range(first, last + 1)])
        padding = [padding_position(marks[p]) for p in range(max(0, lo_width - width))]

        alternatives.append(values if not padding else ("seq", tuple(padding + [values])))

    if not alternatives:
        raise Invalid("W4", "this decimal range denotes nothing")

    node = alternatives[0] if len(alternatives) == 1 else ("alt", tuple(alternatives))

    if size_of(node) > MAX_EXPANSION:
        raise Invalid("W6", "this decimal range expands beyond what this generator will build")

    return node


def padding_position(mark):
    """One padding position: a mandatory zero, or the unified element its mark stands for."""
    zero = ("chars", frozenset("0"))

    if not mark:
        return zero

    optional_zero = ("alt", (("empty",), zero))

    if mark == "!":
        return ("unified", optional_zero, zero, REPRODUCED)

    return ("unified", optional_zero, ("empty",), DROPPED)


def alternatives_of_strings(strings):
    nodes = []

    for text in strings:
        chars = [("chars", frozenset(c)) for c in text]
        nodes.append(chars[0] if len(chars) == 1 else ("seq", tuple(chars)))

    return nodes[0] if len(nodes) == 1 else ("alt", tuple(nodes))


def size_of(node):
    kind = node[0]

    if kind in ("empty", "chars"):
        return 1

    if kind == "unified":
        return 1 + size_of(node[1]) + size_of(node[2])

    return 1 + sum(size_of(child) for child in node[1])


# --- Case folding ---------------------------------------------------------

UPPER = frozenset(chr(c) for c in range(0x41, 0x5B))
LOWER = frozenset(chr(c) for c in range(0x61, 0x7B))


def other_case(c):
    if c in UPPER:
        return chr(ord(c) + 32)

    return chr(ord(c) - 32) if c in LOWER else c


def canonical_case(c, to_upper):
    return c.upper() if to_upper else c.lower()


def apply_fold(node, to_upper):
    """A fold, which is shorthand for the unified elements it expands to.

    On a character set it adds the other case of each cased character and prints the canonical
    case of whichever was matched. On a unified element it widens the sets in the subject and puts
    the rendering into canonical case, adding no `!` of its own: the choice inside a `!` is
    unencoded already, so there is nothing there for a fold to unify. A fold branch it meets is
    treated the same, so where two folds meet the outer governs.
    """
    kind = node[0]

    if kind == "chars":
        return fold_chars(node[1], to_upper)

    if kind in ("seq", "alt"):
        return (kind, tuple(apply_fold(child, to_upper) for child in node[1]))

    if kind == "unified":
        # A fold already expanded within the extent is treated like any other unified element,
        # so its rendering takes the outer fold's case: the outer fold governs.
        return ("unified",
                map_chars(node[1], widen_set),
                map_chars(node[2], lambda s: frozenset(canonical_case(c, to_upper) for c in s)),
                node[3])

    return node


def fold_chars(chars, to_upper):
    uncased = frozenset(c for c in chars if c not in UPPER and c not in LOWER)
    canonical = frozenset(canonical_case(c, to_upper) for c in chars if c not in uncased)

    if not canonical:
        return ("chars", chars)

    branches = []

    if uncased:
        branches.append(("chars", uncased))

    for c in sorted(canonical):
        pair = frozenset((c, other_case(c)))
        branches.append(("unified", ("chars", pair), ("chars", frozenset(c)), FOLD))

    return branches[0] if len(branches) == 1 else ("alt", tuple(branches))


def widen_set(chars):
    return chars | frozenset(other_case(c) for c in chars)


def map_chars(node, mapping):
    kind = node[0]

    if kind == "chars":
        return ("chars", mapping(node[1]))

    if kind in ("seq", "alt"):
        return (kind, tuple(map_chars(child, mapping) for child in node[1]))

    if kind == "unified":
        return ("unified", map_chars(node[1], mapping), map_chars(node[2], mapping), node[3])

    return node


# --- The canonical naxp ---------------------------------------------------


def canonical_tree(node):
    """The naxp with every unified element rewritten in place, whose language is *C*.

    `x!y` rewrites to `y`, and the two abbreviations were expanded at parse time into that form,
    so one rule covers all three.
    """
    kind = node[0]

    if kind == "unified":
        return canonical_tree(node[2])

    if kind in ("seq", "alt"):
        return (kind, tuple(canonical_tree(child) for child in node[1]))

    return node


def contains_unified(node):
    kind = node[0]

    if kind == "unified":
        return True

    if kind in ("seq", "alt"):
        return any(contains_unified(child) for child in node[1])

    return False


# --- W1 and W2 ------------------------------------------------------------


def generated_strings(node, cap=MAX_EXPANSION):
    """Every string a tree generates, or None where there are more than `cap` of them."""
    kind = node[0]

    if kind == "empty":
        return {""}

    if kind == "chars":
        return set(node[1])

    if kind == "unified":
        return generated_strings(node[1], cap)

    if kind == "alt":
        out = set()

        for child in node[1]:
            part = generated_strings(child, cap)

            if part is None:
                return None

            out |= part

            if len(out) > cap:
                return None

        return out

    out = {""}

    for child in node[1]:
        part = generated_strings(child, cap)

        if part is None or len(out) * len(part) > cap:
            return None

        out = {a + b for a in out for b in part}

    return out


def check_w1_and_w2(node):
    """W2 first, because W1 reads inside both operands of a `!` and is only meaningful once
    nothing is hidden in there."""
    check_w2(node)
    check_w1(node)


def check_w2(node):
    kind = node[0]

    if kind == "unified":
        if contains_unified(node[1]) or contains_unified(node[2]):
            raise Invalid("W2", "a `!` may not nest")

    if kind in ("seq", "alt"):
        for child in node[1]:
            check_w2(child)
    elif kind == "unified":
        check_w2(node[1])
        check_w2(node[2])


def check_w1(node):
    kind = node[0]

    if kind == "unified":
        rendering = generated_strings(node[2])

        if rendering is None or len(rendering) != 1:
            raise Invalid("W1", "the rendering of a `!` must generate exactly one string")

        only = next(iter(rendering))
        subject = generated_strings(node[1])

        if subject is None:
            raise Invalid("W6", "this element generates more strings than this generator will "
                                "enumerate")

        if only not in subject:
            if only == "":
                raise Invalid("W1", "this element cannot be deleted, its subject not generating "
                                    "the empty string")

            raise Invalid("W1", f"the rendering {only!r} is not one of the strings its subject "
                                f"generates")

    if kind in ("seq", "alt"):
        for child in node[1]:
            check_w1(child)
    elif kind == "unified":
        check_w1(node[1])
        check_w1(node[2])


# --- The language and the transduction, by enumeration --------------------


def accepted_language(node, cap=MAX_EXPANSION):
    """*L*, or None where it holds more than `cap` strings."""
    return generated_strings(node, cap)


def outputs_of(node, text):
    """Every canonical form the tree gives `text`, as a set.

    W3 is exactly the requirement that this holds one string for every accepted text. This walks
    the tree rather than any machine, so it shares no reasoning with the state construction in
    `naxpmachine` and can be used to check it.
    """
    return {out for end, out in _walk(node, text, 0) if end == len(text)}


def _walk(node, text, at):
    """Every (end, output) the node reaches, having started at `at`."""
    kind = node[0]

    if kind == "empty":
        return {(at, "")}

    if kind == "chars":
        if at < len(text) and text[at] in node[1]:
            return {(at + 1, text[at])}

        return set()

    if kind == "alt":
        out = set()

        for child in node[1]:
            out |= _walk(child, text, at)

        return out

    if kind == "unified":
        rendering = next(iter(generated_strings(node[2])))

        return {(end, rendering) for end, _ in _walk(node[1], text, at)}

    reached = {(at, "")}

    for child in node[1]:
        stepped = set()

        for end, so_far in reached:
            for further, more in _walk(child, text, end):
                stepped.add((further, so_far + more))

        reached = stepped

        if not reached:
            return set()

    return reached


# --- The order ------------------------------------------------------------


def set_order_key(characters):
    """A character set as the string of its members in ascending ASCII, which is what the order
    compares ordinally. The empty set comes first."""
    return "".join(sorted(characters))


def continuations(language):
    """The continuation of the language after each character that can begin one of its strings."""
    after = {}

    for word in language:
        if word:
            after.setdefault(word[0], set()).add(word[1:])

    return after


def order_key(language):
    """A key that sorts a language into the order the specification defines.

    The empty string precedes every other string. Two others are ordered by the set order of their
    first classes where those differ, by ASCII within one class, and by recursion where the first
    characters are the same. Two characters share a first class when their continuations are equal
    and non-empty, so the classes are read off the language itself.
    """
    after = continuations(language)
    classes = {}

    for c, tail in after.items():
        classes.setdefault(frozenset(tail), set()).add(c)

    class_of = {}

    for tail, members in classes.items():
        key = set_order_key(members)

        for c in members:
            class_of[c] = key

    sub_keys = {}

    for c, tail in after.items():
        sub_keys[c] = order_key(tail) if tail else {}

    def key(word):
        if not word:
            return (0,)

        head = word[0]

        return (1, class_of[head], head) + tuple(sub_keys[head](word[1:]))

    return key


def ranked(language):
    """The encoded value of every string of a language, by ranking it under the order.

    This is the definition, and it needs no machine. `naxpmachine` computes the same numbers by
    walking one; the two agreeing is what the data rests on.
    """
    key = order_key(language)

    return {word: i + 1 for i, word in enumerate(sorted(language, key=key))}
