# naxp

A **naxp** ('encoded ASCII expression') uses a regex-like syntax to define how ASCII strings should be mapped to a numerical index.

With one simple expression you can standardise conversion of alphanumeric codes to integer indexes unambiguously and consistently across multiple coding languages and hardware platforms.

```bash
npm install @naxp/naxp@0.12.0
```

The **naxp** package has zero dependencies and comprises plain ECMAScript modules with no build step. TypeScript declarations are included. Requires Node 18 or later. Runs as is in a browser.

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

```js
import { Naxp } from '@naxp/naxp';

const postcode = Naxp.parse('\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA');

postcode.maxEncodedValue;                // 1755842401n
postcode.encode('EC4M 8AD');             // 278794572n
postcode.encode('EC4M8AD');              // 278794572n (same as above)
postcode.decode(278794572n);             // 'EC4M 8AD' (canonical form)
postcode.getCanonicalForm('EC4M 8AD');   // 'EC4M 8AD' (canonical form)
postcode.getCanonicalForm('EC4M8AD');    // 'EC4M 8AD' (canonical form)
postcode.accepts('invalid');             // false
postcode.encode('invalid');              // 0n
```

## The API

### Factory methods

| Function | Returns |
|:---|:---|
|`Naxp.parse(pattern)` | A `Naxp` object or throws `NaxpFormatError`. |
| `Naxp.tryParse(pattern)` |A result object and throws nothing.|

The `pattern` argument of `Naxp.parse` and `Naxp.tryParse` must be a `String`.

The object returned by `tryParse` provides diagnostics if the parse fails:

```js
const { naxp, errorMessage, errorOffset, errorLength, errorCode }
    = Naxp.tryParse('A{2-5}');

naxp;           // null
errorMessage;   // "The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'."
errorOffset;    // 3 (zero-based offset)
errorLength;    // 1
errorCode;      // 'NAXP1002' (always text)
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
| `pattern` | The text pattern defining the **naxp**. |
| `maxEncodedValue` | The largest encoded value, which is also the number of valid encoded values (`bigint`). |
| `maxLength` | The length of the longest text `decode` can return, which bounds every canonical form. |
| `accepts(text)` | Whether the text is valid for the **naxp**. |
| `encode(text)` | The encoded value for the text, from `1n` to `maxEncodedValue`, or `0n` if the text is invalid. |
| `tryDecode(value)` | The *canonical* text for an encoded value or `null` if the encoded value is out of range. |
| `decode(value)` | Same as `tryDecode(value)` except that it throws `RangeError` if the encoded value is out of range, i.e. less than `1n` or greater than `maxEncodedValue`. |
| `decodeToBytes(value)` | Same as `decode(value)` except that the text comes back as a `Uint8Array` of ASCII. |
| `getCanonicalForm(text)` | The canonical version of the text, or `null` if the text is invalid. Same as `tryDecode(encode(text))`. |
| `emit(language, prefix, valueType)` | Source code for this **naxp** in `CSharp`, `JavaScript`, `C` or `Cpp`, answering the same questions as the members above. It runs on its own, with no reference to this package. See [code generation](https://naxp.org/code-gen/). |
| `toString()` | The same as `pattern`. |

`encode` always returns a `bigint` in order to avoid overflow (given that a **naxp** can hold up to `2^64 - 1` encoded values).

`decode` accepts either a `bigint` or a safe integer.

The `text` argument of `accepts`, `encode` and `getCanonicalForm` can be a `String` or a `Uint8Array`
of ASCII.

### Comparing two naxps

> **Note**
> 
> This functionality is provided primarily to enable the website **naxp** migration functionality.
> It is included here for completeness -- it is unlikely that you will require it in normal use.

| Function | Returns |
|:---|:---|
| `Naxp.compare(a, b, budget?)` | Whether the two **naxp**s are `Equal`, `SubsetOf`, `SupersetOf` or `Incomparable` on each of these three aspects: the text they accept, the encoded values to which text is mapped, and the text they print. |
| `Naxp.tryCompare(a, b, budget?)` | Same as `Naxp.compare(a, b, budget?)` except that it returns `null` rather than throwing when the budget runs out. |
| `Naxp.defaultBudget` | The budget a comparison given none uses, which is `200000`. Read it to scale from it rather than writing the number yourself. |
| `Naxp.firstDivergentValue(a, b)` | The lowest encoded value that both **naxp**s hold but decode to different text, or `0n` if none. Values produced by only one of them are ignored. |

Use these to determine whether replacing one **naxp** with another retains the meaning of stored encoded values. See [migrating](https://naxp.org/migrate/).

`compare` addresses text inputs and `firstDivergentValue` addresses text outputs. Both are required in the case where two **naxp**s encode alike but print differently. For example, `(A|B)!A` and `(A|B)!B` accept the same two strings (`A` and `B`) and map each to the same encoded value (`1n`). But they print differently (as `A` or `B` respectively), which is why `firstDivergentValue` returns `1n`.

Determining the relationship between two **naxp**s can be combinatorially expensive (especially for unrelated **naxp**s) and so `budget` caps the number of states the comparison may build, defaulting to 200 000. `firstDivergentValue` needs no budget, since it never builds more states than the two **naxp**s themselves hold.

## Status

Alpha. This package implements **naxp 0.10**, the first published specification. The package version and the specification version each follow semantic versioning, and move independently of one another. The public surface
above is stable enough to build on, but the language is still on 0.x and nothing is promised until
version 1.

This package is validated against `conformance/naxp-v0.10.json`, the test data generated from that
specification.

## Licence

Apache-2.0.
