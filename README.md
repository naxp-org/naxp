# naxp


A **naxp** ('e**n**coded **A**SCII e**xp**ression')
uses a regex-like syntax to define how ASCII strings should be mapped to a numerical index.

With one simple expression you can standardise conversion of alphanumeric codes to integer indexes unambiguously and consistently across multiple coding languages and hardware platforms.

**naxp** is currently experimental at **version 0.10**.Comments are welcome -- please raise a GitHub issue in this repo.

For more information see [the **naxp** website](https://naxp.org/).

## This repo

| Path | Contents |
| --- | --- |
| `conformance/` | Test data generated from the specification, and the generator that produces it |
| `src/cpp/` | The C++ library with its C face, and the amalgamation tool |
| `src/cs/` | The reference implementation, in C#, with the source generator |
| `src/js/` | The JavaScript implementation, published to npm as `@naxp/naxp` |
| `encoding/` | The reasoning behind the hardest decisions, cited from the code |
| `samples/` | `try-naxp`, which consumes the packed NuGet package as a stranger would |
| `site/` | The source of [naxp.org](https://naxp.org), including the specification under `site/src/spec/` |
| `prior-work/` | `NXOld`, an earlier, more limited implementation used for testing and benchmarking |
| `brand/`, `icons/` | Logos, icons and brand assets |

## Licence

Apache Licence 2.0. See [LICENSE](LICENSE).

## Trade marks

The Apache Licence 2.0 covering this repository does not grant permission to use the **naxp** name, logos or icons.

These are always fine and need no permission:
- Saying that your software implements, supports or is compatible with **naxp**.
- Descriptive package names such as `rust-naxp`.
- Re-using the **naxp** logo (<img src="brand/naxp-logo-inline-12pt.svg" alt="the naxp logo" align="middle">) in connection with the above.

Please do not:
- Name a package or product plainly `naxp`.
- Imply that your work is the official one.
- Alter the logo files in `brand/`.

If you want to do something not covered by these guidelines then please open an issue in this GitHub repo.
