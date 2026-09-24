# Library changelog

This changelog sets out the changes in each release of the **naxp** libraries and packages. (From here on 'library' covers both terms.)

For avoidance of doubt, **this changelog is for *library* versioning**. The **naxp** specification is versioned separately [here](https://naxp.org/spec/).

Rules for this changelog:
- Each level 2 heading must be a distinct library version, later releases first.
- Library versioning follows [Semantic Versioning 2.0.0](https://semver.org/#semantic-versioning-200).
- This changelog follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) (apart from the initial version, 0.10.0, and unless otherwise stated).
- Changes not yet released go under `## [Unreleased]`. Before a release is tagged, that heading becomes the version and date, such as `## [0.12.0] - 2026-09-30`, and the link definitions at the foot of the file are updated to match.
- The date in the heading for each release may not be the same date for each language target &ndash; it is indicative only.
- Each library version entry must state the **naxp** specification it implements.
- Text for one library only goes between `<!-- START name -->` and `<!-- END -->`, each on a line of its own, where the name is `C`, `C#` or `JavaScript` (case insensitive). `C` covers C and C++. Text outside such a block is for every library.

> **Note** The release workflow uses the version's section from here to construct the release notes, keeping only the text for C and C++.

## [Unreleased]

## [0.12.0] - 2026-09-24

Implements [**naxp specification 0.10.1**](https://naxp.org/spec/v0.10.1/).

Encoding is identical to the previous library version, 0.10.0. (There is no version 0.11.0.)

### Added

- The API of generated code now matches the library API for a single **naxp**.
- Generated C# is annotated for nullable reference types where the target framework supports them, and still compiles as C# 7.3.

<!-- START C# -->

- `Naxp.MaxLength` and `Naxp.DecodeToBytes`.
- `Naxp.GetCanonicalForm` and `Naxp.TryGetCanonicalForm` overloads accepting ASCII text as a `ReadOnlySpan<byte>`.
- `TryDecode` and `TryGetCanonicalForm` overloads that write to a `Span<char>` or a `Span<byte>` provided by the caller, as an alternative to instantiating strings.
- The number of states required to compare two **naxp**s can get very large and so there is a default budget of 200 000 states. New overloads `Naxp.Compare(a, b, budget)` and `Naxp.TryCompare(a, b, out comparison, budget)` allow the caller to set this budget. The default value itself is available as `Naxp.DefaultBudget`.
- Source generator rules can be individually suppressed.
- Every source generator diagnostic carries a help link, so Visual Studio turns the diagnostic code in the Error List into a link to the [list of error codes on naxp.org](https://naxp.org/codes/).
- The package has a README of its own.

<!-- END -->

<!-- START C -->

- Decoding an encoded value and finding the canonical form can write to a buffer provided by the caller, as an alternative to allocating strings.
    - C++: `try_decode` and `try_canonical_form` overloads that write to a `char*` with a capacity and a length.
    - C: `naxp_decode` and `naxp_canonical_form` already did; each now has a `_cstr` version that takes and writes NUL-terminated strings, as do `naxp_accepts` and `naxp_encode`.
- The number of states required to compare two **naxp**s can get very large and so there is a default budget of 200 000 states. `budget` arguments have been added to the compare functions and the default value itself made available.
    - C++: `naxp::compare(a, b, budget)`, `naxp::try_compare(a, b, comparison, budget)` and `naxp::default_budget`.
    - C: `naxp_compare_within(a, b, comparison, budget)` and `naxp_default_budget()`.

<!-- END -->

<!-- START JavaScript -->

- `Naxp.maxLength` and `Naxp.decodeToBytes`.
- The number of states required to compare two **naxp**s can get very large and so there is a default budget of 200 000 states. So that users can scale relative to this default value it is available as `Naxp.defaultBudget`.

<!-- END -->

### Changed

- Generated JavaScript: `accepts` and `encode` take a string or a `Uint8Array`, consistent with the library API.
- Every package description begins the same way.

<!-- START C# -->

- Decoding and finding the canonical form no longer allocate beyond the string returned.
- The source generator previously reported invalid **naxp**s as `NAXP0101` and put the language's error code inside the message. This meant confusing error display in IDEs and build logs, e.g. `error NAXP0101: NAXP1002: ...`. It now reports the language's code as the diagnostic itself plus a message without the code:
    ```
    error NAXP1002: The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'.
    ```

<!-- END -->

<!-- START C -->

- Decoding and finding the canonical form no longer allocate, except in the C++ overloads that return a `std::string`.
- **Breaking:** `naxp_pattern` no longer takes a `length`. The pattern is NUL-terminated and holds no NUL, so the out parameter was superfluous.

<!-- END -->

### Removed

- **Breaking:** generated JavaScript no longer has `acceptsBytes` and `encodeBytes`, because `accepts` and `encode` now take bytes too.

<!-- START C# -->

### Fixed

- The source generator diagnostic `NAXP0008` no longer suggests syntax that does not compile.

<!-- END -->

## [0.10.0] - 2026-09-21

First public release. Implements [**naxp specification 0.10**](https://naxp.org/spec/v0.10/).

### Libraries

There are three libraries, each passing the conformance data for v0.10, `conformance/naxp-v0.10.json`:

- C#, the reference library, with a source generator (`src/cs`).
- JavaScript (`src/js`), published to npm as `@naxp/naxp`.
- C++17, with a plain C interface (`src/cpp`).

Each library can emit a self-contained encoder / decoder for a given **naxp** in C, C++, C# or JavaScript, with the code for each target being the same whichever library emits it.

### C and C++ as single files

The release attaches the C++ library as one file, `naxp.cpp`, which needs no include path, with `naxp.hpp` for C++ callers and `naxp.h` for C. `SHA256SUMS` holds their digests. The same library builds from source with CMake under `src/cpp`.

[Unreleased]: https://github.com/naxp-org/naxp/compare/v0.12.0...HEAD
[0.12.0]: https://github.com/naxp-org/naxp/compare/v0.10.0...v0.12.0
[0.10.0]: https://github.com/naxp-org/naxp/releases/tag/v0.10.0
