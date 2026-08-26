// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// Mechanical check of the canonicity claim against NXOld:
// generatively equivalent naxps must produce value-identical state maps
// (NX.Equals compares start states deeply), identical encodings for every
// string, and each language must be encoded bijectively onto 1..k.

using Naxp;

int failures = 0;
int checksRun = 0;

void Fail(string message)
{
    ++failures;
    Console.WriteLine($"FAIL: {message}");
}

IEnumerable<string> AllStrings(string alphabet, int maxLength)
{
    yield return "";
    var current = new List<string> { "" };
    for (int length = 1; length <= maxLength; ++length)
    {
        var next = new List<string>(current.Count * alphabet.Length);
        foreach (var prefix in current)
        {
            foreach (var c in alphabet)
            {
                var s = prefix + c;
                next.Add(s);
                yield return s;
            }
        }
        current = next;
    }
}

// checkContiguity requires that alphabet/maxLength cover the whole language.
void CheckPair(string textA, string textB, bool expectEquivalent, string alphabet, int maxLength, bool checkContiguity)
{
    ++checksRun;
    var nxA = NX.Parse(textA);
    var nxB = NX.Parse(textB);

    bool mapsEqual = nxA.Equals(nxB) && nxB.Equals(nxA);
    if (mapsEqual != expectEquivalent)
    {
        Fail($"'{textA}' vs '{textB}': NX.Equals returned {mapsEqual}, expected {expectEquivalent}");
    }

    var encodingsA = new List<ulong>();
    bool behaviourIdentical = true;
    string? firstDifference = null;
    foreach (var s in AllStrings(alphabet, maxLength))
    {
        var eA = nxA.GetEncoding(s);
        var eB = nxB.GetEncoding(s);
        if (eA != eB && firstDifference is null)
        {
            behaviourIdentical = false;
            firstDifference = $"'{s}' -> {eA} vs {eB}";
        }
        if (nxA.Accepts(s) != (eA != 0))
        {
            Fail($"'{textA}': Accepts/GetEncoding disagree on '{s}'");
        }
        if (eA != 0) { encodingsA.Add(eA); }
    }

    if (behaviourIdentical != expectEquivalent)
    {
        Fail($"'{textA}' vs '{textB}': encodings {(behaviourIdentical ? "all agree" : $"differ ({firstDifference})")}, expected {(expectEquivalent ? "agreement" : "a difference")}");
    }

    if (checkContiguity)
    {
        encodingsA.Sort();
        for (int i = 0; i < encodingsA.Count; ++i)
        {
            if (encodingsA[i] != (ulong)(i + 1))
            {
                Fail($"'{textA}': encodings not contiguous 1..k at index {i}: got {encodingsA[i]}");
                break;
            }
        }
    }
}

void CheckValue(string text, string input, ulong expected)
{
    ++checksRun;
    var actual = NX.Parse(text).GetEncoding(input);
    if (actual != expected)
    {
        Fail($"'{text}' on '{input}': got {actual}, expected {expected}");
    }
}

// ---- Equivalent pairs, written as differently as the old syntax allows ----

CheckPair("A", "A|A", true, "AB", 3, true);
CheckPair("[ABC]", "A|B|C", true, "ABCD", 3, true);
CheckPair("[ABC]", "C|A|B", true, "ABCD", 3, true);
CheckPair("A[BC]", "AB|AC", true, "ABCD", 4, true);
CheckPair("A(B|C)", "AB|AC", true, "ABCD", 4, true);
CheckPair("[ABC]C", "[AB]C|[BC]C", true, "ABCD", 4, true); // minterms finer than derivative classes
CheckPair("A?B", "AB|B", true, "ABD", 4, true);
CheckPair("A?", "A?|A", true, "AB", 3, true);
CheckPair("[AC]B", "AB|CB", true, "ABCD", 4, true);
CheckPair("(A|B)(C|D)", "AC|AD|BC|BD", true, "ABCD", 4, true);
CheckPair("A?A?", "(AA)?|A", true, "AB", 4, true);
CheckPair("A?B?", "A?B?|AB", true, "ABD", 4, true);
CheckPair("[AB]?C", "A?C|BC", true, "ABCD", 4, true);
CheckPair("(A|B)?C", "C|AC|BC", true, "ABCD", 4, true);
CheckPair("(AB)?C|ABD", "AB(C|D)|C", true, "ABCD", 4, true);
CheckPair("((A))", "A", true, "AB", 3, true);
CheckPair("[A-C]  B", "[ABC]B", true, "ABCD", 4, true);
CheckPair("\\9\\9", "\\9\\9|1[0-5]", true, "0123456789", 3, true);
CheckPair("#[0-10]", "[0-9]|10", true, "0123456789", 3, true);
CheckPair("#[00-105]", "[0-9][0-9]|10[0-5]", true, "0123456789", 4, true);
CheckPair("#[007-012]", "00[7-9]|01[0-2]", true, "0123456789", 4, true);
CheckPair("#[0-255]", "[0-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-5]", true, "0123456789", 4, true);
CheckPair("\\A", "[A-Z]", true, "AMZa", 2, false);

// ---- Non-equivalent controls ----

CheckPair("A", "B", false, "AB", 3, true);
CheckPair("A", "A?", false, "AB", 3, true);
CheckPair("AB", "AB?", false, "ABD", 3, true);
CheckPair("#[0-10]", "#[00-10]", false, "0123456789", 3, true);

// ---- Pinned values from the grammar's ordering discussion ----

CheckValue("#[0-10]", "0", 1);
CheckValue("#[0-10]", "2", 2);
CheckValue("#[0-10]", "9", 9);
CheckValue("#[0-10]", "1", 10);
CheckValue("#[0-10]", "10", 11);
for (int i = 0; i <= 10; ++i)
{
    CheckValue("#[00-10]", i.ToString("D2"), (ulong)(i + 1));
}

Console.WriteLine(failures == 0
    ? $"All {checksRun} checks passed."
    : $"{failures} failure(s) out of {checksRun} checks.");
return failures == 0 ? 0 : 1;
