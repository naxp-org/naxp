# naxp for C and C++

The C++ implementation of naxp, with a C face over it. One library serves both: the core is
C++17, `include/naxp/naxp.hpp` is its native surface, and `include/naxp/naxp.h` is a flat C API
over opaque handles for C callers and for every language that binds through C.

## Status

The library parses, checks every rule W1 to W6, compiles and runs a naxp: `accepts`, `encode`,
`decode` and the canonical form, compares two naxps on the three axes and by first divergent
value, and emits a naxp as a self-contained fragment of C#, JavaScript, C or C++, through both
the C++ class and the C face. It passes the conformance data for version 0.10 in full, its
emitters are held byte-identical to the JavaScript and C# implementations' on every conformance
naxp, and it builds on GCC 14 and MSVC 14.44 with warnings as errors.

## Layout

| Path | Contents |
| --- | --- |
| `include/naxp/naxp.hpp` | The C++ surface: `logmu::naxp`, `logmu::naxp_fault`, `logmu::naxp_error`, `logmu::naxp_comparison`, `logmu::set_relationship`, `logmu::output_language` and `logmu::naxp_value_type` |
| `include/naxp/naxp.h` | The C surface: `naxp_parse`, `naxp_accepts`, `naxp_encode`, `naxp_decode`, `naxp_emit` and their kin |
| `src/` | The library, in `logmu::detail`. Nothing here is reachable from outside it |
| `src/emitter.cpp` and `src/*_emitter.cpp` | The code emitters: the shared skeleton, then C#, JavaScript, and C and C++ over a shared C family |
| `tests/` | The tests, on a runner of their own in `check.hpp`, so the build downloads nothing |
| `tests/conformance_data.cpp` | A reader for `conformance/naxp-v0.10.json`, which the conformance tests run against |
| `cmake/` | The warning set and the package config template |
| `tools/amalgamate.js` | Writes the library as one `naxp.cpp` with `naxp.h` and `naxp.hpp` beside it |

The C++ namespace is `logmu` and the class is `naxp`, mirroring the C# `LogMu.Naxp`. A class
named the same as its namespace is a trap in C++, since `using naxp::naxp;` would make
`naxp::parse` ambiguous, which is why the namespace is not `naxp`.

The sources follow the C# file for file, so the two can be read side by side: `parser.cpp` is
`Parser.cs`, `tx_machine.cpp` is `TxMachine.cs`, and so on. Where the C# passes a fault out
through an `out` parameter the C++ passes a `std::optional<fault>&`; where the C# shares nodes by
reference the C++ hands out `const rx*` and `const tx*` owned by their factory, and
`std::shared_ptr` for the syntax tree, which the `x!!` form shares between two parents.

The pattern is bytes. A byte outside whitespace and U+0021 to U+007E is a fault, named by its
code point where the bytes are well-formed UTF-8 and by the byte where they are not; fault
offsets and lengths are in bytes.

## Building

Needs CMake 3.23 or newer and a C++17 compiler. The library stops at C++17 so that the toolchains R has shipped since 4.1 can build it. Two presets are defined.

With Visual Studio 2022, from any shell:

```bash
cmake --preset msvc
cmake --build --preset msvc
ctest --preset msvc
```

With GCC on this machine, the MSYS2 compiler and the Rtools make have to be on the path
together, because the Rtools Ninja runs its commands through `sh` and mangles Windows paths:

```bash
export PATH="/c/msys64/ucrt64/bin:/c/rtools45/usr/bin:$PATH"
cmake --preset gcc
cmake --build --preset gcc
ctest --preset gcc
```

On Linux or macOS, plain `cmake -S . -B build && cmake --build build && ctest --test-dir build`
does the same.

Every target builds with warnings as errors, at `/W4` on MSVC and `-Wall -Wextra -Wpedantic
-Wconversion -Wsign-conversion -Wshadow=local` elsewhere.

## Testing

`ctest` runs six tests. `naxp_tests` is the unit tests and the conformance data, and
`naxp_amalgamation_tests` is the same tests built over the amalgamation below in place of the
library. Where Node is on the machine, `naxp_fuzz_generate` writes a few thousand random naxps
and what the JavaScript implementation makes of them, and `naxp_fuzz` runs the conformance
tests over that instead; see `conformance/fuzz/README.md`. The seed and count are the CMake
cache variables `NAXP_FUZZ_SEED` and `NAXP_FUZZ_COUNT`. `naxp_emit_generate` emits every
conformance naxp in every language through the JavaScript implementation, and `naxp_emit`
checks that this library emits the same bytes; see `conformance/emit/README.md`.

The test binary takes an optional substring and runs only the tests whose names contain it, and
reads the `NAXP_CONFORMANCE_FILE` environment variable to run its conformance tests over any
file in the conformance data's shape:

```bash
NAXP_CONFORMANCE_FILE=build/fuzz-7.json build/gcc/tests/naxp_tests conformance
```

## Amalgamation

For anything that does not build with CMake, the library is also one file, in the manner of
sqlite3.c:

```bash
node tools/amalgamate.js --out build/amalgamation
```

writes `naxp.cpp`, `naxp.h` and `naxp.hpp`. Compile `naxp.cpp` as C++17 and include whichever
header suits; it needs no include path, because both headers are inlined into it, and it states
its own version. The build has a `naxp_amalgamation` target that does the same into
`build/<preset>/amalgamation/` whenever a source changes, and the tests run over its output.
The R package vendors these three files, and a release attaches them.

## Using it from another CMake project

```cmake
include(FetchContent)
FetchContent_Declare(naxp
	GIT_REPOSITORY https://github.com/naxp-org/naxp.git
	SOURCE_SUBDIR src/cpp)
FetchContent_MakeAvailable(naxp)

target_link_libraries(your_target PRIVATE naxp::naxp)
```

Or after `cmake --install`, `find_package(naxp CONFIG REQUIRED)` and the same link line.

## Calling it from C

`naxp.h` is plain C, but the library behind it is C++, so a C program that links it also needs
the C++ runtime. In CMake, enable both languages, `project(your_project LANGUAGES C CXX)`, and
CMake links the runtime for you; a project declaring `LANGUAGES C` alone fails at link time with
undefined references to `operator delete` and the exception machinery. Outside CMake, link with
the C++ driver (`g++` or `clang++` rather than `gcc` or `clang`), or add `-lstdc++` or `-lc++`
to the link line.
