# naxp

**naxp** ('e**n**coded **A**SCII e**xp**ression') is a standard for encoding ASCII strings as unsigned integers, written in a RegEx-like syntax.

A **naxp** describes a set of ASCII strings and defines the mapping between each string in that set and an unsigned integer. From one you can generate code that converts text to 8, 16, 32 or 64-bit integers and converts those integers back to text. Because the encoding is compact and ordered, a **naxp** works as an index for stored data: UK postcodes are the example that prompted this.

## Status

Early draft. The specification is still being written, and **version 1 will be the first release**; the versions worked through so far are development documents and are not published. Nothing is stable yet and anything may change without notice.

## Using it from JavaScript

It is not on npm yet. The registry refuses the name `naxp` as too similar to
existing packages, which is under appeal; until that resolves, install it from a
clone of this repository:

```bash
npm install ./src/js
```

```js
import { Naxp } from 'naxp';

const postcode = Naxp.parse('\\A\\A?\\9\\X? \\s \\9\\A\\A');

postcode.encode('M1 1AA');      // 810639597n
postcode.decode(810639597n);    // 'M1 1AA'
postcode.encode('nonsense');    // 0n
```

The package is in [`src/js`](src/js), and its own
[README](src/js/README.md) covers the whole surface.

## Generating code

The `naxp` NuGet package carries a C# source generator. Put `[Naxp]` on a partial type and the recogniser and codec for that naxp are written as members of it, with nothing to call at run time:

```csharp
[Naxp(@"\A\A?\9\X? \s \9\A\A", typeof(int), Prefix = "Postcode")]
internal static partial class Codes
{
}

int encoded = Codes.PostcodeEncode("SW1A 1AA");     // 1273435957
string text = Codes.PostcodeDecode(encoded);        // SW1A 1AA
```

The second argument is the integer type the values are encoded to. You state it rather than let it be inferred, so that a naxp which later outgrows it is a build error instead of a silent widening of everything the generated members return.

`Prefix` starts every generated member name, so one type can hold several naxps. Leave it out and the names are bare, `Accepts` and `Encode`.

## Testing

```
dotnet test src/cs/Naxp.UnitTests/Naxp.UnitTests.csproj
```

```
cd src/js && npm test
```

[Node](https://nodejs.org/) is required by both. The C# suite needs it because the JavaScript emitter is tested by running the code it generates against the conformance data, so without Node those tests fail rather than skip.

Both suites are held to the same file, `conformance/naxp-v0.5.json`. That is what keeps the implementations honest with each other, and it is why they share one repository.

## Layout

| Path | Contents |
| --- | --- |
| `conformance/` | Test data generated from the specification |
| `src/` | Implementations, one folder per language |
| `src/cs/` | The reference implementation, in C#, with the source generator |
| `src/js/` | The JavaScript implementation, to be published to npm as `naxp` |
| `encoding/` | The reasoning behind the hardest decisions, cited from the code |
| `samples/` | `try-naxp`, which consumes the packed package as a stranger would |
| `site/` | The source of [naxp.org](https://naxp.org) |
| `prior-work/` | `NXOld`, the earlier implementation the benchmarks measure against |
| `brand/`, `icons/` | Logos, icons and brand assets |

## Licence

Apache Licence 2.0. See [LICENSE](LICENSE).

## Trade marks

The Apache Licence 2.0 covering this repository does not grant permission to use the **naxp** name, logos or icons.

Saying that your software implements, supports or is compatible with **naxp** is always fine and needs no permission. So is a descriptive package name such as `rust-naxp`.

Please don't name a package or product plainly `naxp`, imply that your work is the official one, or alter the logo files in `brand/`.

If you want to do something not covered by these guidelines then please open an issue in this GitHub repo.
