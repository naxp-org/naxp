# naxp

A **naxp** ('e**n**coded **A**SCII e**xp**ression') uses a regex-like syntax to define how ASCII strings should be mapped to a numerical index. With one simple expression you can standardise conversion of alphanumeric codes to integer indexes unambiguously and consistently across multiple coding languages and hardware platforms.

A **naxp** maps all valid text to a range of consecutive integers starting at 1, and all invalid text to 0. It doesn't matter how you write a **naxp**: if two **naxp**s accept the same text then they produce exactly the same encoded values. Because the encoding is compact and ordered, a **naxp** works as an index for stored data. UK postcodes are the example that prompted this.

The website is [naxp.org](https://naxp.org): the [specification](https://naxp.org/spec/), a [quick tour](https://naxp.org/#quick-tour) of the syntax, [pre-defined standard naxps](https://naxp.org/pre-defined/), an [interactive page](https://naxp.org/interactive/) for developing one, and [code generation](https://naxp.org/code-gen/) for a specific **naxp** with no library to link.

## Status

Early draft. **Version 0.10 is the current specification.** Nothing is stable yet and anything may change without notice.

Packages for npm, NuGet, vcpkg and Conan are being published. Until one is listed on its registry, build from the source in `src/` as described below.

## Example

A UK postcode comprises one or two letters, a number, sometimes a further letter or number, a space, a number and two letters, as in `M1 1AA` and `EC1A 1BB`. As a **naxp**:

```
\A\A?\9\X? \s \9\A\A
```

`\A` is any uppercase letter, `\9` any digit and `\X` either. `?` makes the preceding item optional, as in a regex. Whitespace in a **naxp** is ignored, so the actual space is written `\s`. This **naxp** has 1 755 842 400 encoded values, which fit in 31 bits.

## Libraries

A library provides the following run-time functionality given a **naxp** as text: *encoding*, i.e. mapping text to its encoded value; *decoding*, i.e. mapping an encoded value back to (canonical) text; *validation* and other checks; and *code generation* for a specific **naxp**.

### C++ / C

A C++17 library, plus a plain C interface for C programs and for any language that can call C. Install `naxp` from vcpkg or Conan, use it from another CMake project with `FetchContent` pointed at `src/cpp`, or copy the amalgamated `naxp.cpp` plus `naxp.hpp` (C++) or `naxp.h` (C) from the [latest release](https://github.com/naxp-org/naxp/releases/latest) into any build.

```cpp
#include <naxp/naxp.hpp>

const logmu::naxp postcode = logmu::naxp::parse("\\A\\A?\\9\\X? \\s \\9\\A\\A");

postcode.encode("M1 1AA");      // 810639597
postcode.decode(810639597);     // "M1 1AA"
postcode.encode("nonsense");    // 0
```

The library is in [`src/cpp`](src/cpp), and its own [README](src/cpp/README.md) covers building, the C face and the amalgamation.

### C# / .NET

The `naxp` NuGet package targets .NET 8 and .NET Standard 2.0, so it runs on .NET Framework too.

```csharp
using LogMu;

Naxp postcode = Naxp.Parse(@"\A\A?\9\X? \s \9\A\A");

ulong encoded = postcode.Encode("M1 1AA");     // 810639597
string text = postcode.Decode(810639597);      // "M1 1AA"
```

It includes a source generator that compiles a **naxp** at build time, as C# does for regexes. Put `[Naxp]` on a partial type and the codec for that **naxp** is written as members of it, with nothing to call at run time:

```csharp
[Naxp(@"\A\A?\9\X? \s \9\A\A", typeof(int), Prefix = "Postcode")]
internal static partial class Codes
{
}

int encoded = Codes.PostcodeEncode("SW1A 1AA");     // 1273435957
string text = Codes.PostcodeDecode(encoded);        // "SW1A 1AA"
```

The second argument is the integer type the values are encoded to. You state it rather than let it be inferred, so that a **naxp** which later outgrows it is a build error instead of a silent widening of everything the generated members return. `Prefix` starts every generated member name, so one type can hold several **naxp**s; leave it out and the names are bare, `Accepts` and `Encode`.

The library and the generator are in [`src/cs`](src/cs).

### JavaScript

The `@naxp/naxp` npm package has zero dependencies and no build step: plain ES modules with TypeScript declarations, for Node 18 or later and for the browser. It is the library that runs the interactive pages on the website.

```js
import { Naxp } from '@naxp/naxp';

const postcode = Naxp.parse('\\A\\A?\\9\\X? \\s \\9\\A\\A');

postcode.encode('M1 1AA');      // 810639597n
postcode.decode(810639597n);    // 'M1 1AA'
postcode.encode('nonsense');    // 0n
```

`encode` returns a `bigint`, since a **naxp** may hold up to 2<sup>64</sup> − 1 values; `decode` accepts a `bigint` or a safe integer. The package is in [`src/js`](src/js), and its own [README](src/js/README.md) covers the whole surface.

### Other languages

Implementations in R and Python are planned. If you want to write an implementation in a language not already covered, please open an [issue](https://github.com/naxp-org/naxp/issues) first so that work is not duplicated, and test it against the conformance data described below.

## Testing

```
cmake --preset gcc && cmake --build --preset gcc && ctest --preset gcc
```

```
dotnet test src/cs/Naxp.UnitTests/Naxp.UnitTests.csproj
```

```
cd src/js && npm test
```

[Node](https://nodejs.org/) is required by all three. The C# suite runs the JavaScript its emitter generates against the conformance data, and the C++ suite fuzzes against the JavaScript implementation and checks its emitters against it; without Node those tests fail rather than skip. The C++ presets are described in [`src/cpp/README.md`](src/cpp/README.md).

All three suites are held to the same file, `conformance/naxp-v0.10.json`: language-neutral test data generated from the specification itself rather than from any implementation, so an implementation cannot define its own truth by passing it. That is what keeps the implementations honest with each other, and it is why they share one repository.

## Layout

| Path | Contents |
| --- | --- |
| `conformance/` | Test data generated from the specification, and the generator that produces it |
| `src/cpp/` | The C++ library with its C face, and the amalgamation tool |
| `src/cs/` | The reference implementation, in C#, with the source generator |
| `src/js/` | The JavaScript implementation, published to npm as `@naxp/naxp` |
| `encoding/` | The reasoning behind the hardest decisions, cited from the code |
| `samples/` | `try-naxp`, which consumes the packed NuGet package as a stranger would |
| `site/` | The source of [naxp.org](https://naxp.org), including the specification under `site/src/spec/` |
| `prior-work/` | `NXOld`, the earlier implementation the benchmarks measure against |
| `brand/`, `icons/` | Logos, icons and brand assets |

## Licence

Apache Licence 2.0. See [LICENSE](LICENSE).

## Trade marks

The Apache Licence 2.0 covering this repository does not grant permission to use the **naxp** name, logos or icons.

Saying that your software implements, supports or is compatible with **naxp** is always fine and needs no permission. So is a descriptive package name such as `rust-naxp`.

Please don't name a package or product plainly `naxp`, imply that your work is the official one, or alter the logo files in `brand/`.

If you want to do something not covered by these guidelines then please open an issue in this GitHub repo.
