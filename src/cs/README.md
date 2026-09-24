# naxp

A **naxp** ('encoded ASCII expression') uses a regex-like syntax to define how ASCII strings should be mapped to a numerical index.

With one simple expression you can standardise conversion of alphanumeric codes to integer indexes unambiguously and consistently across multiple coding languages and hardware platforms.

```bash
dotnet add package naxp
```

The **naxp** package includes a source generator that compiles a **naxp** into your own type at build time, as C# does for regexes, and a library for a **naxp** not known until the program runs. It targets .NET 8 and .NET Standard 2.0, so it runs on .NET Framework too.

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
- **naxp**s ignore whitespace &ndash; use `\s` to mean an actual space.
- `!!` after the space makes it optional *without changing the encoding*, which ensures that `EC4M8AD` and `EC4M 8AD` are mapped to the same encoded value. (Using `?`, which might be your first instinct if you are used to regexes, would instead map each of these to *different* encoded values.) A **naxp** written with a `!` operator maps multiple texts to a single encoded value, but defines one of those texts as *canonical*.
- `GIR 0AA` is a single anomalous but real postcode, so the **naxp** specifies it explicitly.

## The naxp source generator

If a **naxp** is known at compile time then **use the source generator**.

Add the `[Naxp]` attribute to a `partial` class and the source generator will add the **naxp** functionality with no need to reference the `naxp` package at run time.

```csharp
using LogMu;

[Naxp(@"\A\A?\9\X? \s!! \9\A\A | GIR \s!! 0AA", typeof(int))]
static partial class Postcode
{
}

Postcode.MaxEncodedValue;               // 1755842401
Postcode.MaxLength;                     // 8
Postcode.Encode("EC4M 8AD");            // 278794572
Postcode.Encode("EC4M8AD");             // 278794572 (same as above)
Postcode.Decode(278794572);             // "EC4M 8AD" (canonical form)
Postcode.GetCanonicalForm("EC4M 8AD");  // "EC4M 8AD" (canonical form)
Postcode.GetCanonicalForm("EC4M8AD");   // "EC4M 8AD" (canonical form)
Postcode.Accepts("invalid");            // false
Postcode.Encode("invalid");             // 0

// Performant Try... and span-writing overloads are also generated -- see the API below.
```

| Argument | Is |
|:---|:---|
| The **naxp** | Required, and first, so that it stays the subject. |
| The value type | Required, as a `typeof`. Any C# integer type will do, and the encoded values must fit it. It is stated rather than inferred so that a **naxp** which outgrows its type is a build error rather than a silent change of type. |
| `Prefix` | Optional. It starts every generated name, so several **naxp**s can share one type. Left out, the names are bare: `Encode`, `Decode` and the rest. |

Each **naxp** generates static versions of the members of the library's `Naxp` object listed [below](#naxp-object-members), other than `Emit` and `ToString`, with `Pattern`, `MaxEncodedValue` and `MaxLength` as constants.

If you want to use the same class for more than one **naxp** then use `[Naxp]` multiple times but with different `Prefix` arguments to distinguish the systems.

The generated code needs C# 7.3 or later, and carries nullable annotations where the target framework supports them. The source generator needs the .NET 8 SDK or Visual Studio 2022 17.8 or later.

Error codes begin with 'NAXP' and are documented at [naxp.org/codes/](https://naxp.org/codes/).

## Using a naxp at run time

If a **naxp** is not known until run time (e.g. because it's read from configuration or chosen by a user) then use the library, which provides the same functionality but dynamically.

```csharp
using LogMu;

var postcode = Naxp.Parse(@"\A\A?\9\X? \s!! \9\A\A | GIR \s!! 0AA");

postcode.MaxEncodedValue;               // 1755842401
postcode.MaxLength;                     // 8
postcode.Encode("EC4M 8AD");            // 278794572
postcode.Encode("EC4M8AD");             // 278794572 (same as above)
postcode.Decode(278794572);             // "EC4M 8AD" (canonical form)
postcode.GetCanonicalForm("EC4M 8AD");  // "EC4M 8AD" (canonical form)
postcode.GetCanonicalForm("EC4M8AD");   // "EC4M 8AD" (canonical form)
postcode.Accepts("invalid");            // false
postcode.Encode("invalid");             // 0
```

## The API

This section sets out the library API.

The source-generated members are the same, as static members of your class, except that the encoded value type will be the integer type requested as opposed to the `ulong` type used by the library. See the example above under [naxp source generator](#the-naxp-source-generator).

### Factory methods

| Function | Returns |
|:---|:---|
| `Naxp.Parse(pattern)` | A `Naxp` object or throws `FormatException`. |
| `Naxp.TryParse(pattern, out naxp, out errorMessage)` | `true` if the pattern is a **naxp**, and throws nothing. |

The `pattern` argument of `Naxp.Parse` and `Naxp.TryParse` is a `ReadOnlySpan<char>`.

A longer overload of `TryParse` provides additional diagnostics if required:

```csharp
bool succeeded = Naxp.TryParse(
    "A{2-5}", out Naxp? naxp,
    out string? errorMessage,
    out int errorOffset, out int errorLength, out string? errorCode);

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
| `MaxLength` | The length of the longest text `Decode` can return, which bounds every canonical form. |
| `Accepts(text)` | Whether the text is valid for the **naxp**. |
| `Encode(text)` | The encoded value for the text, from `1` to `MaxEncodedValue`, or `0` if the text is invalid. |
| `TryEncode(text, out encoded)` | The same, as `false` and `0` rather than as a value you have to test. |
| `TryDecode(value, out text)` | The *canonical* text for an encoded value, or `false` if the encoded value is out of range. |
| `Decode(value)` | Same as `TryDecode(value, out text)` except that it throws `ArgumentOutOfRangeException` if the encoded value is out of range, i.e. `0` or greater than `MaxEncodedValue`. |
| `DecodeToBytes(value)` | Same as `Decode(value)` except that the text comes back as a `byte[]` of ASCII. |
| `TryDecode(value, destination, out written)` | Writes the *canonical* text for an encoded value into a `Span<char>` or `Span<byte>`, returning `false` if the encoded value is out of range or `destination` is too short. A `destination` of `MaxLength` always suffices. |
| `GetCanonicalForm(text)` | The canonical version of the text, or `null` if the text is invalid. |
| `TryGetCanonicalForm(text, out canonicalForm)` | The same, as a `bool`. |
| `TryGetCanonicalForm(text, destination, out written)` | Writes the canonical version of the text into a `Span<char>` or `Span<byte>` of the same kind as `text`, returning `false` if the text is invalid or `destination` is too short. |
| `Emit(language, prefix, valueType)` | Source code for this **naxp** in `OutputLanguage.CSharp`, `JavaScript`, `C` or `Cpp`, answering the same questions as the members above. It runs on its own, with no reference to this package. See [code generation](https://naxp.org/code-gen/). |
| `ToString()` | The same as `Pattern`. |

`Encode` always returns a `ulong` in order to avoid overflow (given that a **naxp** can hold up to `2^64 - 1` encoded values).

The `destination` argument of `TryDecode` and `TryGetCanonicalForm` is a text buffer of `Span<char>` or `Span<byte>`, with `written` being set to the number of characters written.

### Comparing two naxps

> **Note**
> 
> This functionality is provided primarily to enable the website **naxp** migration functionality.
> It is included here for completeness &ndash; it is unlikely that you will require it in normal use.

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

**Alpha**. This package implements **naxp 0.10**, the first published specification. The package version and the specification version each follow semantic versioning, and move independently of one another. The public surface
above is stable enough to build on, but the language is still on 0.x and nothing is promised until
version 1.

This package is validated against `conformance/naxp-v0.10.json`, the test data generated from that
specification.

## Licence

Apache-2.0.
