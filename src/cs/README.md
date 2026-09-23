# naxp

A **naxp** ('encoded ASCII expression') uses a regex-like syntax to define how ASCII strings should be mapped to a numerical index.

With one simple expression you can standardise conversion of alphanumeric codes to integer indexes unambiguously and consistently across multiple coding languages and hardware platforms.

```bash
dotnet add package naxp
```

The **naxp** package targets .NET 8 and .NET Standard 2.0, so it runs on .NET Framework too. It includes a source generator that compiles a **naxp** at build time, as C# does for regexes.

## Why use a naxp rather than roll your own encoding?

1. A **naxp** is **simpler** and **safer** than writing your own custom encoding.

2. The mapping is **language and platform independent**.

3. All **naxp**s covering the same set of ASCII strings result in the same encoding, which means that **the mapping is robust**.

## Example

UK postcodes are sufficiently non-trivial that writing your own encoding risks creating bugs. A UK postcode comprises one or two letters, a number, an optional additional letter or number, a space separator (that may be omitted in practice), and then a number and two letters. Examples are `EC4M 8AD` and `M1 1AA`.

This is the [standard **naxp**](https://naxp.org/pre-defined/#uk-postcode) for a UK postcode:

```
\A\A?\9\X? \s!! \9\A\A | GIR \s!! 0AA
```

Some points to note:

- We've used the shortcuts `\A` for any uppercase letter, `\9` for any digit and `\X` for any uppercase letter or digit. We could have written `[A-Z]`, `[0-9]` and `[A-Z0-9]` instead.
- `?`, `|` and `[...]` mean the same as in regexes, i.e. one or none, `a` or `b`, and character range respectively.
- **naxp**s ignore whitespace -- use `\s` to mean an actual space.
- `!!` after the space makes it optional *without changing the encoding*, which ensures that `EC4M8AD` and `EC4M 8AD` are mapped to the same encoded value. (Using `?`, which might be your first instinct if you are used to regexes, would instead map each of these to *different* encoded values.) A **naxp** written with a `!` operator maps multiple texts to a single encoded value, but defines one of those texts as *canonical*.
- `GIR 0AA` is a single anomalous but real postcode, so the **naxp** specifies it explicitly.

This is what it looks like in code:

```csharp
using LogMu;

var postcode = Naxp.Parse(@"\A\A?\9\X? \s!! \9\A\A | GIR \s!! 0AA");

postcode.MaxEncodedValue;                    // 1755842401
postcode.Encode("EC4M 8AD");                 // 278794572
postcode.Encode("EC4M8AD");                  // 278794572 (same as above)
postcode.Decode(278794572);                  // "EC4M 8AD" (canonical form)
postcode.GetCanonicalForm("EC4M 8AD");       // "EC4M 8AD" (canonical form)
postcode.GetCanonicalForm("EC4M8AD");        // "EC4M 8AD" (canonical form)
postcode.Accepts("invalid");                 // false
postcode.Encode("invalid");                  // 0
```

The naxp is written as a verbatim string (`@"..."`) so that its backslashes stand for themselves.

## The source generator

Put `[Naxp]` on a `partial` type and the generator writes the recogniser and the codec into it at build time. The generated code answers on its own, with no reference to this package at run time, so a **naxp** you know when you compile costs nothing to parse and nothing to hold.

```csharp
using LogMu;

[Naxp(@"\A\A?\9\X? \s!! \9\A\A | GIR \s!! 0AA", typeof(int), Prefix = "Postcode")]
internal static partial class Codes
{
}

Codes.PostcodeMaxEncodedValue;       // 1755842401
Codes.PostcodeMaxLength;             // 8
Codes.PostcodeEncode("EC4M 8AD");    // 278794572
Codes.PostcodeEncode("EC4M8AD");     // 278794572 (same as above)
Codes.PostcodeDecode(278794572);     // "EC4M 8AD" (canonical form)
Codes.PostcodeAccepts("invalid");    // false
```

| Argument | Is |
|:---|:---|
| The **naxp** | Required, and first, so that it stays the subject. |
| The value type | Required, as a `typeof`. Any C# integer type will do, and the encoded values must fit it. It is stated rather than inferred so that a **naxp** which outgrows its type is a build error rather than a silent change of type. |
| `Prefix` | Optional. It starts every generated name, so several **naxp**s can share one type. Left out, the names are bare: `Encode`, `Decode` and the rest. |

Each **naxp** generates `MaxEncodedValue`, `MaxLength`, `Accepts`, `Encode`, `Decode`, `DecodeToBytes` and `TryDecode`, each under its prefix, and each working on `ReadOnlySpan<char>` or `ReadOnlySpan<byte>`. `[Naxp]` may appear several times on one type.

A fault is a build error naming the character at fault:

```
Program.cs(11,16): error NAXP1002: The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'.
Program.cs(16,50): error NAXP0008: This naxp encodes 1755842401 values, which does not fit byte. Pass typeof(int) or wider as the value type, or narrow the naxp.
```

A fault in the **naxp** is reported under the language's own code for the rule it breaks, NAXP1001 upwards, so a build can suppress one rule without suppressing the rest. NAXP0001 upwards are the attribute and its surroundings, which the generator judges for itself.

## The API

### Factory methods

| Function | Returns |
|:---|:---|
| `Naxp.Parse(pattern)` | A `Naxp` object or throws `FormatException`. |
| `Naxp.TryParse(pattern, out naxp, out errorMessage)` | `true` if the pattern is a **naxp**, and throws nothing. |

The `pattern` argument of `Naxp.Parse` and `Naxp.TryParse` is a `ReadOnlySpan<char>`, which a `string` satisfies.

A longer overload of `TryParse` provides diagnostics if the parse fails:

```csharp
bool succeeded = Naxp.TryParse(
    "A{2-5}",
    out Naxp? naxp,
    out string? errorMessage,
    out int errorOffset,
    out int errorLength,
    out string? errorCode);

succeeded;      // false
naxp;           // null
errorMessage;   // "The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'."
errorOffset;    // 3 (zero-based offset)
errorLength;    // 1
errorCode;      // "NAXP1002" (always text)
```

Use `errorOffset` and `errorLength` to locate the issue:

```
A{2-5}
   ^  The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'.
```

If the error relates to the whole **naxp** then the whole text range is specified.

### `Naxp` object members

| Member | Returns |
|:---|:---|
| `Pattern` | The text pattern defining the **naxp**. |
| `MaxEncodedValue` | The largest encoded value, which is also the number of valid encoded values (`ulong`). |
| `Accepts(text)` | Whether the text is valid for the **naxp**. |
| `Encode(text)` | The encoded value for the text, from `1` to `MaxEncodedValue`, or `0` if the text is invalid. |
| `TryEncode(text, out encoded)` | The same, as `false` and `0` rather than as a value you have to test. |
| `TryDecode(value, out text)` | The *canonical* text for an encoded value, or `false` if the encoded value is out of range. |
| `Decode(value)` | Same as `TryDecode(value, out text)` except that it throws `ArgumentOutOfRangeException` if the encoded value is out of range, i.e. `0` or greater than `MaxEncodedValue`. |
| `GetCanonicalForm(text)` | The canonical version of the text, or `null` if the text is invalid. |
| `TryGetCanonicalForm(text, out canonicalForm)` | The same, as a `bool`. |
| `Emit(language, prefix, valueType)` | Source code for this **naxp** in `OutputLanguage.CSharp`, `JavaScript`, `C` or `Cpp`, answering the same questions as the members above. It runs on its own, with no reference to this package. See [code generation](https://naxp.org/code-gen/). |
| `ToString()` | The same as `Pattern`. |

`Encode` always returns a `ulong` in order to avoid overflow (given that a **naxp** can hold up to `2^64 - 1` encoded values). The source generator is where a narrower type comes from, since there the **naxp** is known when you compile.

The `text` argument of `Accepts`, `Encode`, `TryEncode`, `GetCanonicalForm` and `TryGetCanonicalForm` can be a `ReadOnlySpan<char>` or a `ReadOnlySpan<byte>` of ASCII.

### Comparing two naxps

> **Note**
> 
> This functionality is provided primarily to enable the website **naxp** migration functionality.
> It is included here for completeness -- it is unlikely that you will require it in normal use.

| Function | Returns |
|:---|:---|
| `Naxp.Compare(a, b)` | Whether the two **naxp**s are `Equal`, `SubsetOf`, `SupersetOf` or `Incomparable` on each of these three aspects: the text they accept, the encoded values to which text is mapped, and the text they print. |
| `Naxp.Compare(a, b, budget)` | The same, with the budget given rather than left at its default. |
| `Naxp.TryCompare(a, b, out comparison)` | Same as `Naxp.Compare(a, b)` except that it returns `false` rather than throwing when the budget runs out. |
| `Naxp.TryCompare(a, b, out comparison, budget)` | The same, with the budget given rather than left at its default. |
| `Naxp.DefaultBudget` | The budget the overloads above that take no budget use, which is `200000`. Read it to scale from it rather than writing the number yourself. |
| `Naxp.FirstDivergentValue(a, b)` | The lowest encoded value that both **naxp**s hold but decode to different text, or `0` if none. Values produced by only one of them are ignored. |

Use these to determine whether replacing one **naxp** with another retains the meaning of stored encoded values. See [migrating](https://naxp.org/migrate/).

`Compare` addresses text inputs and `FirstDivergentValue` addresses text outputs. Both are required in the case where two **naxp**s encode alike but print differently. For example, `(A|B)!A` and `(A|B)!B` accept the same two strings (`A` and `B`) and map each to the same encoded value (`1`). But they print differently (as `A` or `B` respectively), which is why `FirstDivergentValue` returns `1`.

Determining the relationship between two **naxp**s can be combinatorially expensive (especially for unrelated **naxp**s) and so `budget` caps the number of states the comparison may build, defaulting to 200 000. `Compare` throws `InvalidOperationException` when that is not enough. `FirstDivergentValue` needs no budget, since it never builds more states than the two **naxp**s themselves hold.

## Status

Alpha. This package implements **naxp 0.10**, the first published specification. The package version and the specification version each follow semantic versioning, and move independently of one another. The public surface
above is stable enough to build on, but the language is still on 0.x and nothing is promised until
version 1.

This package is validated against `conformance/naxp-v0.10.json`, the test data generated from that
specification.

## Licence

Apache-2.0.
