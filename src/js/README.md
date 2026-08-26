# naxp

A **naxp** ('e**n**coded **A**SCII e**xp**ression') uses a RegEx-like syntax to define how a set of ASCII strings should be mapped to a compact set of integer keys from 1 to *N* (with 0 being reserved for non-matching strings).

```bash
npm install naxp@0.5.0-alpha
```

The **naxp** package has zero dependencies and comprises plain ECMAScript modules with no build step. TypeScript declarations are included. Requires Node 18 or later. Runs as is in a browser.

## Example

UK postcodes are a good example because they are sufficiently non-trivial that writing your own encoding would risk creating bugs. A UK postcode comprises one or two letters, a number, sometimes a further letter or number, a space, a number and two letters. Examples are `M1 1AA` and `EC1A 1BB`.

This can be represented as the following **naxp**:

```
\A\A?\9\X? \s \9\A\A
```

Some points to note:

- We've used the shortcuts `\A` for any uppercase letter, `\9` for any digit and `\X` for any uppercase letter or digit. We could have used `[A-Z]`, `[0-9]` and `[A-Z0-9]` instead.
- The `?` and `[...]` operators mean the same as in regexes, i.e. one or none and a character range respectively.
- naxps ignore whitespace -- we used the shortcut `\s` to mean an actual space.

Here's how this looks in code:

```js
import { Naxp } from 'naxp';

const postcode = Naxp.parse('\\A\\A?\\9\\X? \\s \\9\\A\\A');

postcode.valueCount;                     // 1755842400n
postcode.encode('M1 1AA');               // 810639597n
postcode.decode(810639597n);             // 'M1 1AA'
postcode.accepts('nonsense');            // false
postcode.encode('nonsense');             // 0n
```

## Why use a naxp rather than roll your own encoding?

1. A **naxp** is **simpler** and **safer** than writing your own custom encoding.

2. The mapping is **language and platform independent**.

3. All **naxp**s covering the same set of ASCII strings result in the same encoding, which means that **the mapping is robust**.

## The API

`Naxp.parse(text)` returns a `Naxp` or throws `NaxpFormatError`. `Naxp.tryParse(text)` returns a
result object instead and throws nothing.

| Member | Gives |
|:---|:---|
| `source` | The text the naxp was parsed from |
| `valueCount` | The number of encoded values (`bigint`) |
| `accepts(text)` | Whether the text matches the **naxp** |
| `encode(text)` | The encoded value, from `1n` to `valueCount`, or `0n` if the text did not match the **naxp** |
| `decode(value)` | The canonical string for a value; throws `RangeError` if the value is out of range |
| `tryDecode(value)` | The same, or `null` |
| `getCanonicalForm(text)` | The string with each replaceable element replaced, or `null` |

A **replaceable element** is a part of a naxp, written with `!`, whose exact text does not
change the value. It is how one value can stand for several spellings of the same thing: mark
the space in a postcode replaceable and `M11AA` and `M1 1AA` both encode to 810639597, with
`getCanonicalForm` telling you which of the two a value decodes back to. The example above
does not use one, so every string it accepts is its own canonical form.

`encode` always returns a `bigint`, whatever the naxp. A naxp may hold up to 2^64 - 1 values, which
is past what a `number` holds exactly, and a return type that changed with the expression would
make every caller branch on which naxp it was holding. `decode` accepts either a `bigint` or a safe
integer, since taking `decode(5)` costs nothing.

The text argument of `accepts`, `encode` and `getCanonicalForm` may also be a `Uint8Array`
of ASCII. `parse` and `tryParse` take the naxp itself, and that must be a string.

## Invalid naxp specifications

`tryParse` reports what is wrong, the location within the **naxp** text, and an error code (as text).

```js
const { naxp, errorMessage, errorTextOffset, errorTextLength, errorCode }
    = Naxp.tryParse('A{2-5}');

naxp;               // null
errorMessage;       // "The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'."
errorTextOffset;    // 3
errorTextLength;    // 1
errorCode;          // 'NAXP1002'
```

which is enough to point at the fault:

```
A{2-5}
   ^  The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'.
```

If the error relates to the whole **naxp** then the whole text range is specified.

`errorCode` is provided for logging or bug reporting.

## Status

Alpha. The specification is still being written and **version 1 will be the first release**, so
this package tracks a working draft rather than a published standard. The public surface above is
stable enough to build on, but nothing is promised until then.

The specification and its test data will be published at [naxp.org](https://naxp.org) when version
1 is ready.

## Licence

Apache-2.0.
