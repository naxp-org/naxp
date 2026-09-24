# Implementation changelog

This changelog sets out changes in each release of **naxp** implementations.

The release workflow uses the version's section from here to construct the release notes.

Notes
- The specification is versioned separately -- *this changelog is for implementation changes only*.
- A level 2 heading must be a package version.
- The specification each release implements is stated in its section.
- Releases before 0.11.0 have their notes on
[the releases page](https://github.com/naxp-org/naxp/releases) and are not repeated here.

## 0.11.0

Implements **naxp 0.10.1**, which changes no encoded value. Encoding is identical to implementation v0.10.0.

### User-defined comparison budgets

The number of states required to compare two **naxp**s can get very large and so there is a default budget of 200 000 states.

Every implementation now allows the caller to set this budget and provides access to the default value:

- C#: `Naxp.Compare(a, b, budget)`, `Naxp.TryCompare(a, b, out comparison, budget)` and
  `Naxp.DefaultBudget`.
- C++: `naxp::compare(a, b, budget)`, `naxp::try_compare(a, b, comparison, budget)` and
  `naxp::default_budget`.
- C: `naxp_compare_within(a, b, comparison, budget)` and `naxp_default_budget()`, which the
  C face had no way of expressing before.
- JavaScript: the budget argument was already there; `Naxp.defaultBudget` is new.

### A single system of naxp error codes

The C# source generator previously reported invalid **naxp**s as `NAXP0101` and put the
language's error code inside the message. This meant confusing error display in IDEs and build logs, e.g. `error NAXP0101: NAXP1002: ...`.
It now reports the language's code as the diagnostic itself plus a message without the code:

```
error NAXP1002: The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'.
```

Rules can now be individually suppressed.

Every diagnostic carries a
help link, so Visual Studio turns the code in the Error List into a link to
[the code it names](https://naxp.org/codes/).

`NAXP0008` also stops suggesting syntax that does not compile.

### Other

- The website now documents every error code at [naxp.org/codes/](https://naxp.org/codes/), with entries derived direct from the implementations' tables.
- The C# package has a README of its own, and every package description now opens the same
  way.