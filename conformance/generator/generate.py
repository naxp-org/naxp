# Copyright (c) Tim Gordon.
# This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

"""Writes conformance/naxp-v0.10.json from the naxps in cases.py and nothing else.

    python conformance/generator/generate.py            # write the file
    python conformance/generator/generate.py --check    # verify the committed one

`--check` regenerates in memory and compares, so the committed data can be shown to be what this
produces rather than something edited beside it.

Every number in the output is derived here from the naxp alone, and where the language is small
enough to list it is derived twice by routes that share no reasoning: `naxpref` ranks the
canonical language under the order the specification defines, and `naxpmachine` walks the minimal
machine. `naxp.agreed_values` refuses to return unless the two agree on every value, in both
directions, and on both counts. Canonical forms are computed twice as well, by walking the tree
and by walking the transduction.

Nothing here reads an implementation of naxp. An implementation that both produced the data and
had to pass it would be defining its own truth.

Everything runs on a thread with a large stack. A naxp of a few thousand positions is a chain of
that many expressions, and the recursive walks over it go as deep.
"""

import json
import pathlib
import sys
import threading

import cases
import naxp as facade
import naxpref as ref

HERE = pathlib.Path(__file__).resolve().parent
TARGET = HERE.parent / "naxp-v0.10.json"

NOTE = (
    "Language-neutral conformance data for naxp v0.10. Generated from the specification, not from "
    "any implementation. `maxEncodedValue`, `acceptedCount` and `out` are written as decimal "
    "strings rather than JSON numbers, because W5 allows a naxp up to 2^64 - 1 encoded values and "
    "most JSON parsers read a number into a double. `out` is the encoded value, 0 meaning the text "
    "is invalid for the naxp. `canon` is the canonical form, which is what decoding `out` must "
    "yield. Where `complete` is true, `values` lists every valid text exactly once and the encoded "
    "values are a bijection onto 1..maxEncodedValue. conformance/README.md describes the shape "
    "in full."
)


class Wrong(Exception):
    """The authored data and the specification disagree, which is a fault in one of them."""


def build_case(authored):
    pattern = authored["naxp"]

    try:
        naxp = facade.Naxp(pattern)
    except ref.Invalid as e:
        raise Wrong(f"{pattern!r} is listed as a case and is invalid: {e}") from None

    ranked = naxp.agreed_values()
    complete = "samples" not in authored

    if complete and not naxp.enumerable:
        raise Wrong(f"{pattern!r} has no samples and its language is too large to list")

    if complete:
        inputs = sorted(naxp.accepted)
    else:
        inputs = list(authored["samples"])

    values = []

    for text in inputs:
        canon = naxp.canonical_form(text)

        if canon is None:
            raise Wrong(f"{pattern!r} does not accept {text!r}, which is listed among its values")

        values.append({"in": text, "out": str(naxp.encode(text)), "canon": canon})

    values.sort(key=lambda v: (int(v["out"]), v["in"]))

    for text in authored["invalid"]:
        if naxp.canonical_form(text) is not None:
            raise Wrong(f"{pattern!r} accepts {text!r}, which is listed as invalid for it")

    if complete and ranked is not None and len(values) != naxp.accepted_count:
        raise Wrong(f"{pattern!r} is complete and lists {len(values)} of {naxp.accepted_count}")

    return {
        "naxp": pattern,
        "note": authored["note"],
        "maxEncodedValue": str(naxp.max_encoded_value),
        "acceptedCount": str(naxp.accepted_count),
        "complete": complete,
        "values": values,
        "invalid": list(authored["invalid"]),
    }


def build_invalid(authored):
    pattern = authored["naxp"]
    rule = facade.check(pattern)

    if rule is None:
        raise Wrong(f"{pattern!r} is listed as invalid under {authored['rule']} and is well formed")

    if rule != authored["rule"]:
        raise Wrong(f"{pattern!r} is invalid under {rule} and is listed under {authored['rule']}")

    return {"naxp": pattern, "rule": rule, "note": authored["note"]}


def build():
    data = {
        "naxpVersion": "0.10",
        "testDataVersion": 1,
        "note": NOTE,
        "cases": [build_case(c) for c in cases.CASES],
        "invalidNaxps": [build_invalid(r) for r in cases.INVALID_NAXPS],
    }

    return json.dumps(data, indent=1, ensure_ascii=False) + "\n"


def main(argv):
    text = build()

    if "--check" in argv:
        if not TARGET.exists():
            raise Wrong(f"{TARGET.name} does not exist")

        if TARGET.read_text(encoding="utf-8") != text:
            raise Wrong(f"{TARGET.name} is not what this generator produces")

        print(f"{TARGET.name} is what this generator produces.")

        return

    TARGET.write_bytes(text.encode("utf-8"))

    data = json.loads(text)
    checks = sum(len(c["values"]) for c in data["cases"])
    complete = sum(1 for c in data["cases"] if c["complete"])

    print(f"wrote {TARGET.name}")
    print(f"  {len(data['cases'])} cases, {complete} of them complete")
    print(f"  {len(data['invalidNaxps'])} invalid naxps")
    print(f"  {checks} value checks")


def run(argv):
    """On a thread with room for the deep walks a few thousand positions need."""
    failure = []

    def work():
        try:
            main(argv)
        except (Wrong, AssertionError) as e:
            failure.append(e)

    sys.setrecursionlimit(200_000)
    threading.stack_size(64 * 1024 * 1024)
    thread = threading.Thread(target=work)
    thread.start()
    thread.join()

    if failure:
        print(f"generate: {failure[0]}", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    run(sys.argv[1:])
