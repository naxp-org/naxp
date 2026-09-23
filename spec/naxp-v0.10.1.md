# naxp spec v0.10.1

<!--
MAKE SURE YOU'RE WORKING FROM THE
VERSION OF THIS DOC IN THE
`spec/` FOLDER!!

THE VERSION IN `site/src/spec`
**WILL** BE OVERWRITTEN
-->

- [1. Introduction](#1-introduction)
    - [1.1 Specification versioning](#11-specification-versioning)
    - [1.2 Implementation versioning](#12-implementation-versioning)
- [2. Glossary](#2-glossary)
- [3. Notation](#3-notation)
- [4. Character repertoire](#4-character-repertoire)
    - [4.1 Reserved characters](#41-reserved-characters)
    - [4.2 Whitespace](#42-whitespace)
- [5. Grammar](#5-grammar)
- [6. Character sets](#6-character-sets)
- [7. Decimal ranges](#7-decimal-ranges)
    - [7.1 Marking the padding](#71-marking-the-padding)
    - [7.2 Ordering](#72-ordering)
- [8. Postfix operators](#8-postfix-operators)
    - [8.1 `?` optional](#81--optional)
    - [8.2 `{}` interval](#82--interval)
    - [8.3 `!` text unification](#83--text-unification)
- [9. Case folding](#9-case-folding)
    - [9.1 Case fold binding](#91-case-fold-binding)
    - [9.2 Case fold operation](#92-case-fold-operation)
    - [9.3 Wrapping an existing naxp](#93-wrapping-an-existing-naxp)
- [10. Well-formedness](#10-well-formedness)
- [11. Building the machines](#11-building-the-machines)
    - [11.1 The machine that computes canonical forms](#111-the-machine-that-computes-canonical-forms)
    - [11.2 The machine that decides W3](#112-the-machine-that-decides-w3)
- [12. The encoding](#12-the-encoding)
    - [12.1 Three languages](#121-three-languages)
    - [12.2 The order](#122-the-order)
    - [12.3 Values](#123-values)
- [13. Computing the encoding](#13-computing-the-encoding)
    - [13.1 A worked value](#131-a-worked-value)
- [14. Worked example](#14-worked-example)
- [15. Relations between naxps](#15-relations-between-naxps)
    - [15.1 Value agreement](#151-value-agreement)
    - [15.2 Why the relation has to be computed](#152-why-the-relation-has-to-be-computed)
    - [15.3 The first divergent value](#153-the-first-divergent-value)
    - [15.4 Deciding the relations](#154-deciding-the-relations)
- [16. Data portability concerns](#16-data-portability-concerns)
- [17. Conformance](#17-conformance)
    - [17.1 Test data](#171-test-data)
- [18. What this document does not cover](#18-what-this-document-does-not-cover)

<a id="introduction"></a>
## 1. Introduction

A **naxp** maps
a finite set of ASCII strings to positive integers. Invalid text is mapped to the reserved value 0.
These integers are the **encoded value**s.
Because 0 is reserved to mean invalid text, a naxp with a maximum encoded value of 255 fits a byte, but one of 256 does not.

The reverse mapping takes an encoded value and maps it
to the **canonical form**, which is the standardised version of the accepted text,
allowing for variations the naxp tolerates, such as whether or not an optional space is included in the canonical form.
The encoded value 0 and values greater than the maximum encoded value are invalid and do not map to text.

A naxp is defined using text written in a syntax similar in some respects to a regular expression (regex). 
Unlike regexes, a naxp matches the whole text or none of it: there is no searching within a string and there are no anchors.

This specification defines naxp syntax, validity and encoding.

**Specifications with versions before v1 are *draft*. In particular, until v1 is reached, the mapping of text to encoded values should be regarded as unstable – it may change in a future draft.**

The remainder of this specification is set out as follows:

- Section 2 defines common terms.
- Sections 3 to 9 define the syntax and meaning of each construct.
- Section 10 sets out the well-formedness rules, W1 to W6, that a syntactically valid naxp must also satisfy.
- Section 11 fixes two machine constructions
  whose state counts W6 would otherwise leave to the implementation.
- Section 12 defines the encoding of a string.
- Section 13 sets out
   the full procedure to compute an encoded value, with a worked value.
- Section 14 works through the
  postcode example.
- Section 15 defines how one naxp relates to another, which matters when migrating from one naxp to another.
- Section 16 covers data portability, given that the bits available vary between languages.
- Section 17 describes the conformance testing required of an implementation.
- Section 18 lists what is left undefined.

<a id="specification-versioning"></a>
### 1.1 Specification versioning

The major and minor components of a version govern text encoding, namely
- valid **naxp** patterns,
- text accepted by a **naxp**, and
- the encoded value of accepted text.

Versions of this specification are written `MAJOR.MINOR.PATCH`, following the principles
of semantic versioning, amended as follows:
- A **patch** change corrects typos, resolves ambiguity and improves the prose.
A patch change must *not* change text encoding.
- A **minor** version change adds to the language. For major version v0 (the development version), a minor change *may* change encoded values. For major versions v1 and later, a minor change must *not* change encoded values.
- A **major** version change *may* change encoded values.

If the patch component is 0 then it may be omitted from the version:
versions 0.10 and 0.10.0 are the same document.

Test data is published per minor version, because a patch changes nothing there is to test.
The data for 0.10.0 is the data for every 0.10.x. See [17.1 Test data](#171-test-data).

<a id="implementation-versioning"></a>
### 1.2 Implementation versioning

**naxp** implementations are versioned separately from this document.
An implementation must state which version of this specification it implements.

<a id="glossary"></a>
## 2. Glossary

The section that introduces each term defines it, and that definition governs.
The entries here are for reference.

- **Accepted language**, *L*: the set of strings a naxp matches. It is finite
  and non-empty for every naxp.
- **Atom**: the grammar's name for what a quantifier applies to: a character
  set, a decimal range or a parenthesised group.
- **Block**: in the machines of section 11, a set of characters that every
  parse of a state treats alike. The blocks are the coarsest partition of the
  characters the state can read with that property.
- **Case fold**: `\C` or `\c`, which makes everything to the end of its
  enclosing group insensitive to case, printing upper or lower case
  respectively; section 9.
- **Canonical form**, ρ(*w*): an accepted string *w* with the match of each
  text unification replaced by its rendering. Where a naxp contains no
  text unification once its case folds and marked decimal ranges are expanded,
  the canonical form of a string is the string itself.
- **Canonical language**, *C*: the set of canonical forms, ρ(*L*). It is the
  accepted language of the canonical naxp, which is the naxp with every text
  unification rewritten in place as the table in section 12.1 says.
- **Continuation**: within a language *M*, the continuation after a character
  *c* is the set of strings *w* for which *cw* is in *M*.
- **Delay**: in the machine that decides W3, what one parse of a pair has
  emitted beyond the other.
- **Element**: an atom with its quantifier and text unification, where it has
  them; the unit a sequence is made of.
- **Encoded value**: the positive integer a naxp assigns to an accepted string,
  one plus the count of strings of *C* that precede its canonical form in the
  order of section 12.2. Zero is reserved for invalid text.
- **First class**: a set of characters of a language *M* whose continuations
  are equal and non-empty. The first classes partition the characters that can
  begin a string of *M*.
- **Machine**: a deterministic finite automaton, or in section 11 the
  transducer that computes canonical forms. The minimal machine for a language
  is the smallest that accepts it, and is unique.
- **Mark**: the `!` or `?` on a leading zero of a decimal range's lower bound,
  which makes that zero optional in the text; section 7.1.
- **Padding position**: a leading zero of a decimal range's lower bound, which
  stands in front of the digits of the value and fixes the width; section 7.
- **Parse**: one way of matching the text read so far against the naxp. A
  state of either machine in section 11 holds the parses still possible.
- **Pending output**: in section 11, the output a parse has emitted that the
  machine has not yet emitted.
- **Pattern**: the characters a naxp is written in.
- **Quantifier**: `?` or an interval `{m,n}`, which says how many times its
  atom occurs; section 8.
- **Reconstitution**: decoding, which recovers from an encoded value the
  canonical form it stands for.
- **Reference**: in the machine that computes canonical forms, a character
  copied from the input and written as how far back it was read rather than as
  its value.
- **Register**: the memory in which that machine holds its references. Its
  depth is how far back its deepest reference reaches, and W6 counts the depth
  together with the states.
- **Rendering**: the `y` of a text unification `x!y`, the one string printed in
  place of whatever the subject matched. `x!!` renders `x` and `x!?` renders
  nothing.
- **Single valued**: of a transducer, giving at most one output for each input.
  W3 requires the map from an accepted string to its canonical form to be single
  valued.
- **Subject**: the `x` of a text unification `x!y`.
- **Text**: the string a naxp is applied to.
- **Text unification**: an element `x!y`, formed by the binary text
  unification operator `!`, or one of its unary abbreviations `x!!` and `x!?`.
  Every string its subject accepts encodes to the same value, and its rendering
  is printed in that position on reconstitution.
- **Transducer**: a finite state machine that emits output as it reads. The map
  from an accepted string to its canonical form is a finite state transduction.
- **W**: in W1 to W6, W stands for well-formedness.
- **Well formed**: satisfying the six rules W1 to W6. A naxp that parses may
  still fail one of them, and is then invalid.

<a id="notation"></a>
## 3. Notation

Productions are written as `name ::= definition`. Within a definition:

- Text in double quotes is literal, so `"|"` is the vertical bar character.
- `"x"-"y"` is the inclusive range of characters from `x` to `y` in ASCII order.
- `A | B` means either `A` or `B`.
- `A?` means nil or one occurrence of `A`, `A*` nil or more, and `A+` one or more.
- Parentheses group.

<a id="character-repertoire"></a>
## 4. Character repertoire

**The pattern of a naxp** may contain whitespace (see below) and the printable
ASCII characters `0x21` to `0x7E`. A naxp containing a byte outside that set is
not a naxp.

**The characters a naxp matches** are `0x20` to `0x7E`, the printable characters
and the space. Control characters cannot be matched. Whitespace in a pattern is
ignored, so a space cannot appear bare there and is written `\s`.

<a id="reserved-characters"></a>
### 4.1 Reserved characters

Eighteen printable characters require escaping with `\` 
if the literal character is required.

Five of these, `*`, `+`, `.`, `^` and `$`, require escaping purely for safety, because they are operators in regular expressions. Requiring the escape saves anyone used to regular expressions from a silent failure when they expect these characters to keep their regular expression meaning.

| Character | Meaning | Literal |
| --- | --- | --- |
| `!` | The binary text unification operator: the left operand is unified to the right | `\!` |
| `#` | Introduces a decimal range | `\#` |
| `(` | Opens a group | `\(` |
| `)` | Closes a group | `\)` |
| `,` | Separates the counts of an interval | `\,` |
| `-` | Separates the bounds of a range | `\-` |
| `?` | Optional postfix | `\?` |
| `[` | Opens a character set | `\[` |
| `\` | Introduces an escape | `\\` |
| `]` | Closes a character set | `\]` |
| `{` | Opens an interval | `\{` |
| `\|` | Separates alternatives | `\\|` |
| `}` | Closes an interval | `\}` |
| `*` `+` | Reserved: naxp has no unbounded repetition, so an interval is used instead, e.g. `\A{1,4}` | `\*` `\+` |
| `.` | Reserved: the character set meant is written instead, e.g. `\X` | `\.` |
| `^` `$` | Reserved: a naxp matches the whole text, so there are no anchors | `\^` `\$` |

Every other printable character stands for itself: the digits, the upper and
lower case letters, and these:

```
" % & ' / : ; < = > @ _ ` ~
```

In the grammar below,
- the eighteen reserved characters are `reserved_char`, and
- the characters that represent themselves are `bare_char`.

<a id="whitespace"></a>
### 4.2 Whitespace

The whitespace characters are tab (`0x09`), line feed (`0x0A`), carriage return
(`0x0D`) and space (`0x20`).

Whitespace in a naxp has no meaning. It is ignored between
tokens and may not split a multi-character token. The multi-character tokens are:
- the reserved character escapes defined in [4.1](#reserved-characters),
- the case folds `\C` and `\c`,
- the forms `#[`, `!!` and `!?`, 
- a run of digits, and
- a marked digit of a decimal range bound.

Every other token is a single character.

For example,
- `[A - E]`, `{2 , 5}` and `#[0 - 10]` are legal, because in each of them the
separator is a token in its own right, but
- `\ s`, `! !`, `A{2 5}`, `# [0-10]`,
`#[1 0-20]` and `#[0 !0-20]` each split a token and are syntax errors.

<a id="grammar"></a>
## 5. Grammar

```
naxp          ::= expr

expr          ::= seq ( "|" seq )*
seq           ::= element+ | element* case_fold expr
element       ::= atom ( "?" ( "!" element )? | interval? text_unification? )
case_fold     ::= "\C" | "\c"
atom          ::= char_set | digits_range | "(" expr? ")"
interval      ::= "{" digits ( "," digits )? "}"
text_unification ::= "!" element | "!!" | "!?"

char_set      ::= literal_char | block_escape | "[" set_item+ "]"
set_item      ::= block_escape | literal_char ( "-" literal_char )?
block_escape  ::= "\" ( "9" | "A" | "a" | "X" | "x" )

digits_range  ::= "#[" low_bound "-" digits "]"
low_bound     ::= ( digit mark? )+
mark          ::= "!" | "?"
digits        ::= digit+
digit         ::= "0"-"9"

literal_char  ::= bare_char | escape
escape        ::= "\" ( "s" | reserved_char )

reserved_char ::= "!" | "#" | "(" | ")" | "," | "-" | "?" | "[" | "\"
                | "]" | "{" | "|" | "}" | "*" | "+" | "." | "^" | "$"
```

A `bare_char` is any printable ASCII character, `0x21` to `0x7E`, that is not a
`reserved_char`. It denotes itself.

A **quantifier** is `?` or an interval. `element` writes the two separately
because `!!` and `!?` carry their own `?` and so may not follow one: `A{2}!!` is
a naxp and `A?!!` is a syntax error. After a `?` only the general form `!y` is
available, which is why `A?!()` parses. See
[8. Postfix operators](#postfix-operators).

`\s` denotes a space (`0x20`), not the letter `s`; every other `escape` denotes
the `reserved_char` after its backslash. A backslash must be followed either by
a `reserved_char` or by one of the eight escape letters `s`, `9`, `A`, `a`, `X`, `x`,
`C` and `c`. Every other sequence beginning with a backslash is a syntax error, so
`\d`, `\w` and `\Z` are not naxps.

`()` is an atom denoting the empty string. As the right hand operand of `!` it
means print nothing; as an alternative it admits the empty string, so `A|()`
denotes the same strings as `A?`; and on its own it is a legal naxp matching
only the empty string.

The text unification operator `!` is binary and requires a right hand operand,
so `A!` is a syntax error. Apart from the two unary operators `!!` and `!?`, the
right operand is a single element, which needs parentheses only when the
rendering runs to more than one element: `[AB]!A` and
`\s?!()` need none, whereas replacing with `AB` must be written `(AB|CD)!(AB)`.
The right operand cannot begin with a case fold, so `A!\CA` is a syntax error: a
rendering is one string, written in the case wanted.

A case fold is the loosest operator. It runs from where it is written to the end
of the enclosing group, or of the naxp, across any `|`, the way a flag does in a
regular expression; so `\CAB` case folds both letters, `\CA|b` is `\C(A|b)`,
and a fold is stopped short by grouping it, as in `(\CA)B`. A case fold written
after another in the same group sits inside the first one's extent, so `\Ca\Cb`
is `\C(a(\Cb))`.

Operator precedence is as follows (with a lower value indicating a looser binding):

|Precedence | Operator | Type | Meaning | Example |
| ---: | :---: | --- | --- | --- |
| 100 | `\C` `\c` | unary prefix | Case folding, to the end of the enclosing group | `\CA\|b` is `\C(A\|b)` |
| 200 | `\|` | binary | Alternatives | `AB\|CD` is `(AB)\|(CD)` |
| 300 | concatenation | binary | Sequence | `A?B` is `(A?)B` |
| 400 | `!` | binary | Text unification: the left operand is the subject, the right its rendering | `A?!()` is `(A?)!()` |
| 400 | `!!` `!?` | unary postfix | Optional text unification: `x!!` reproduces `x`, `x!?` drops it | `A{2}!!` is `(A{2})!!` |
| 500 | `?` `{}` | unary postfix | Optional repeats (including none) | `AB?` is `A(B?)` |

Parentheses are used to override the order.


<a id="character-sets"></a>
## 6. Character sets

A `char_set` denotes a set of one or more characters that may appear at a single
position. A bare literal or an escape denotes a set of one character: `A`
matches `A`, and `\[` matches `[`. A block escape denotes a common set:

| Escape | Set |
| --- | --- |
| `\9` | `0` to `9` |
| `\A` | `A` to `Z` |
| `\a` | `a` to `z` |
| `\X` | `0` to `9` and `A` to `Z` |
| `\x` | `0` to `9` and `a` to `z` |

Square brackets denote the union of their contents, where each item is a block
escape, a single character, or an inclusive range of two characters. So
`[A-EG-Z]` matches any upper case letter other than `F`, and `[\9A-F]` matches a
hexadecimal digit.

A range must be written lowest first: the character before the hyphen may not
follow the character after it in ASCII order. The two may be equal, so `[A-A]`
is the set holding `A` alone. A range written the other way round is refused
under W4 rather than read as an empty set or a set read backwards, so `[E-A]`
is invalid.

A character set must contain at least one character. `[]` is not legal.

<a id="decimal-ranges"></a>
## 7. Decimal ranges

This section uses the text unification operators `!!` and `!?` of
[8.3](#text-unification), the canonical form of [12](#the-encoding) and the rules W3 and
W5 of [10](#well-formedness), because a decimal range is defined by expanding it
into those. A first reading can take the expansions on trust.

`#[`*lo*`-`*hi*`]` is shorthand for the set of decimal representations of the
integers from *lo* to *hi* inclusive. It expands to an ordinary expression and
adds no expressive power.

The number of digits written in each bound determines the widths generated:

| Written | Expands to |
| --- | --- |
| `#[0-9]` | `[0-9]` |
| `#[0-10]` | `[0-9] \| 10` |
| `#[0-105]` | `[0-9] \| [1-9][0-9] \| 10[0-5]` |
| `#[00-105]` | `[0-9][0-9] \| 10[0-5]` |

Leading zeros in *lo* therefore set a minimum width, unless they are marked as
described under [Marking the padding](#marking-the-padding). `#[00-105]` does not
match `7`, while `#[0-105]` does.

Six constraints apply:

1. Each bound has at least one and at most fifteen digits, so that every
   bound is below 2<sup>53</sup> and a language that reads a number into a
   double reads it exactly.
2. *lo* may not have more digits than *hi*.
3. If *hi* has more digits than *lo*, then *hi* may not have leading zeros.
4. The value of *lo* may not exceed the value of *hi*. The two may be equal.
5. A mark may attach only to a padding position of *lo*, which is a leading zero
   standing in front of the digits of the value. `#[0!-9]` marks the units digit
   and `#[10?5-999]` marks a zero inside the number, and both are invalid under W4.
6. Only *lo* may carry marks. Marks are not digits, so they do not count towards
   the fifteen of constraint 1.

The cap applies to the bound as written. It does not limit how many values a naxp
may hold, which W5 governs: `\9{18}` is a field of eighteen digits and is
legal.

<a id="marking-the-padding"></a>
### 7.1 Marking the padding

A leading zero of *lo* may carry a mark, `!` or `?`, which makes that zero
optional on input and fixes what it contributes to the canonical form. `0!` and
`0?` are the only marked forms a range admits. Between the brackets a mark is a
note on a zero rather than an operator applied to one, so `0!!` and `0!?` cannot
be written there; they appear in the expansion below, which is where the meaning
of a mark comes from.

Write *d* for the digits of *lo* and take a value whose shortest representation
is *n* digits. That value is written in the last *n* positions of *lo*, so the
padding positions that apply to it are the first *d* − *n*; where *n* is *d* or
more there are none. An unmarked padding position is a mandatory zero. A marked
one is optional.

- **Accepted forms** run from *n* plus the count of unmarked padding
  positions, up to *d*.
- **The canonical form** is the value in *n* digits, preceded by one zero for
  each padding position marked `!`.

With no marks, the first rule gives the minimum width the table above describes
and the second leaves the width of the value alone.

| Written | Accepts | Canonical form of 7 | Canonical language |
| --- | --- | --- | --- |
| `#[000-105]` | `007` | `007` | fixed width 3 |
| `#[0!0!0-105]` | `7`, `07`, `007` | `007` | as `#[000-105]` |
| `#[0?0!0-105]` | `7`, `07`, `007` | `07` | as `#[00-105]` |
| `#[0?0?0-105]` | `7`, `07`, `007` | `7` | as `#[0-105]` |
| `#[0!0?0-105]` | `7`, `07`, `007` | `07` | widths 2, 3, 3 |

The marks may be mixed, and are read outside in. `#[0?0!0-105]` is a field of
minimum canonical width two which accepts one, two or three digits, and the
unmarked shorthand has no way to say that. Interleaving the other way gives the last
row, where the canonical width runs two, three, three as the value grows. A width
can never decrease, since raising *n* by one drops at most one padding position,
so there is no incoherent case to exclude.

The expansion is one alternative per *n*, with the padding written out in front
of each:

```
#[0?0!0-105]   →   0!? 0!! #[0-9] | 0!? #[10-99] | #[100-105]
```

A text unification contributes its rendering whether or not its subject matched
anything, as `\s!!` in the postcode example below relies on. So a padding chain
contributes a fixed string whichever of two identical zeros the input supplied,
and `07` has one canonical form under `0!! 0!!` rather than two.

A marked range accepts strings of more than one width, so what follows it has to
fix where it stops. `#[0!0!0-105]\9` is fine, since the single digit at the end
takes one character and the range takes the rest. `#[0!0!0-105]\9?` is not: `00`
reads either as the value 0 with one padding zero and nothing after it, or as
the value 0 with none and a zero after it. Those give `000` and `0000`, and W3
rules the naxp out on that witness.

<a id="ordering"></a>
### 7.2 Ordering

The encoding preserves numeric order where the canonical language is fixed width,
which is where *lo* and *hi* are written with the same number of digits and every
mark, if there are any, is `!`. Of two matches denoting different numbers, the
smaller number takes the smaller encoded value. In `#[00-10]`, `00` encodes below
`09`, which encodes below `10`, and `#[0!0!0-105]` puts `7` below `42` while
accepting both of them unpadded.

The same holds for a sequence of digit sets of fixed width whose contents do not
depend on one another, such as `\9{4}` or `[0-5]\9`.

The comparison is unsigned. Where a naxp has more than 2<sup>63</sup>&#xA0;−&#xA0;1
encoded values, everything above that point has its top bit set, and a consumer
holding such a value in a signed 64 bit integer reads it as negative. C#'s
`ulong` and Java's `Long.compareUnsigned` compare correctly. R has no unsigned
integer type at all, so an R consumer cannot see this order. See
[W5](#well-formedness).

No other promise about order is made. A naxp defines equality and the reserved
zero; the guarantee above is a by-product of the order on canonical forms, and
because a naxp's encoding is fixed permanently it can be relied on where it
holds.

It does not extend to bounds of different widths. `#[0-10]` accepts both `1` and
`10`, and encodes `1` above `9`. The cause is structural: `1` is the only leading
digit that can carry a second digit, so it has to be separated from the rest, and
it ends up in a later position than the digits it was separated from. Both cases
are worked through under [A worked value](#a-worked-value).

Nor does it survive a `?` mark, which takes padding out of the canonical form and
so lets the widths differ again. `#[0?0-10]` assigns the values of `#[0-10]`.
Marking every padding position `!` is what buys the order, and it is the reason to
reach for `!` where either mark would do.

<a id="postfix-operators"></a>
## 8. Postfix operators

A quantifier – `?` or an interval – binds to the single `atom` immediately
preceding it, and `!` binds to that atom together with its quantifier.
Neither reaches back over the sequence it sits in, so `AB?` is an `A` followed by
an optional `B`, and to make the pair optional you group it as `(AB)?`.

An atom may take one quantifier and then be the left operand of `!`, in
that order. `A??`, `A{2}?` and `A?{2}` are all syntax errors, as are `A?!!` and
`A?!?`, because those two forms carry their own `?`. So in `A?!()` it is `A?`
that is unified, and what goes unencoded is the choice between `A` and nothing.

<a id="optional"></a>
### 8.1 `?` optional

`x?` matches `x` or nothing. The choice is part of the encoding: under `AB?`,
the strings `AB` and `A` encode to different values.

<a id="interval"></a>
### 8.2 `{}` interval

`x{n}` is `n` copies of `x` in sequence. `x{m,n}` is anything from `m` to `n`
copies inclusive. Like a decimal range, an interval expands to an ordinary
expression and adds no expressive power: `A{2,4}` denotes the same strings as
`AAA?A?`.

There is no unbounded form. `A{2,}` is a syntax error, since the grammar has
no place for it: a naxp must have a finite number of encoded values.

Three constraints apply:

1. Each count has at least one and at most two digits, which is what keeps
   the check of W6 affordable: `(A{99}){99}` is already 9 802 states, and with
   four-digit counts a hostile naxp costs seconds to refuse.
2. If both counts are given, the first may not exceed the second. The two may be
   equal.
3. The second count, or the only one, is at least one. `A{0}` and `A{0,0}`
   would denote the empty string, which is written `()`.

`A{0,3}` is legal and is up to three `A`s.

<a id="text-unification"></a>
### 8.3 `!` text unification

`!` is the binary text unification operator, and `x!y` is a **text
unification**: it matches whatever its left operand `x`, the subject, matches,
and which of those strings was matched is **not** part of the encoding. Every string `x` accepts encodes to the same value,
and `y` is printed in that position on reconstitution.

`y` must be one of the strings `x` generates, since reconstituted text has to be
text the naxp can encode again; W1 states this precisely.

Two unary text unification operators cover the common cases, where the subject
is made optional and the rendering is either the subject itself or nothing:

| Written | Means |
| --- | --- |
| `x!!` | `x?!(x)` – optional, reproduced on reconstitution |
| `x!?` | `x?!()` – optional, dropped on reconstitution |

`x!(x)` without the `?` replaces a single string with itself and does nothing,
so the useful reading of `!!` is the one that makes the subject optional as
well.

The expansions are structural rather than textual. When the subject carries an
interval, making it optional needs a group: `A{2}!!` expands to
`(A{2})?!(A{2})`, since `A{2}?` itself does not parse.

So `\s!?` matches an optional space that is dropped when text is reconstituted,
`\s!!` matches an optional space that is reproduced, and `[\s\-]?!\-` matches a
space, a hyphen or nothing and prints a hyphen.

Case folding falls out of the same operator. `(A|a)!A` matches either case,
encodes both alike and prints the capital. `\C` and `\c` are shorthand for it,
under [Case folding](#case-folding).

<a id="case-folding"></a>
## 9. Case folding

`\C` and `\c` make the element that follows them insensitive to case. `\C` takes
upper case as canonical and `\c` takes lower, so `\C[A-F]` matches a hexadecimal
letter in either case and prints the capital, while `\c[A-F]` matches the same
characters and prints the small letter.

A case fold adds no expressive power. It is shorthand for the text unifications that
would otherwise be written a letter at a time. `\CA` is `[Aa]!A`, and `\C[A-F]` is

```
( [Aa]!A | [Bb]!B | [Cc]!C | [Dd]!D | [Ee]!E | [Ff]!F )
```

<a id="what-a-fold-binds-to"></a>
### 9.1 Case fold binding

A case fold is the loosest operator, so it binds to everything from where it is
written to the end of the enclosing group, or of the naxp, across any `|`; see
the operator precedence table under [5. Grammar](#grammar). `\CAB` case folds
both letters, and `\CA|b` is `\C(A|b)`. To case fold less than the rest of the
group, group the fold: `(\CA)B` case folds the `A` and leaves the `B` alone.

Binding looser than `!` is what keeps the ordinary ways of writing legal. `\CA!A`
is `\C(A!A)`, which widens the subject and prints the capital.

Where one case fold falls inside another, the outer one governs: every character
set within its extent takes its case, whether or not an inner case fold had
already chosen the other, so `\C(AB\cC)` prints `ABC`. A case fold within a case
fold is never an error. That holds for a case fold written directly on another,
so `\C\cA` is `\CA`, and for one written later in the same group, which sits
inside the first one's extent: `\Ca\cb` is `\C(a(\cb))` and prints `AB`. To
give two parts of a naxp different cases, close the first fold before opening the
second: `(\Ca)(\cb)` prints `Ab`.

<a id="what-a-fold-does"></a>
### 9.2 Case fold operation

A case fold applies to every character set within the element it binds to.

- On a character set it adds the other case of each cased character, and the set
  prints the canonical case of whichever character was matched. Characters with
  no case pass through untouched, so `\C[\9A-F]` accepts `0` to `9`, `A` to `F`
  and `a` to `f`, and prints a digit as itself and a letter as a capital.
- On a text unification `x!y` it widens the sets within `x` and puts `y` into
  canonical case, adding no `!` of its own. The choice within a text unification
  is already unencoded, so there is nothing there for a case fold to unify.
- On a sequence, an alternation, a group or an interval it distributes to the
  parts.
- On a decimal range it does nothing, decimal digits having no case.

Every rule of the language reads the expansion rather than the case fold, which
is what saves a case fold from needing rules of its own. W1 in particular holds of
the result without being checked: the rendering of every text unification a case fold
reaches is a case variant of a string its subject already generated, and the
widened subject generates every case variant of everything it generated before.

A case fold widens character sets rather than adding states, so neither the machine
for the accepted language nor the machine for the canonical language grows under
W6. What grows is the transition table of the machine that computes canonical
forms, which carries one output per character of a case-folded set.

<a id="wrapping-an-existing-naxp"></a>
### 9.3 Wrapping an existing naxp

The common use is to make a naxp that already works tolerant of case, by
writing a case fold in front of it; since the fold runs to the end of the naxp,
no parentheses are needed. The postcode under [Worked example](#worked-example)
becomes

```
\C \A \A? \9 \X? \s!! \9 \A \A
```

which accepts `ec1a 1bb` and `Ec1A 1bB` as well as `EC1A 1BB`, canonicalises all
three to `EC1A 1BB`, and gives every one of them the encoded value the unwrapped
naxp gives to `EC1A 1BB`. The count of encoded values is unchanged at
1 755 842 400.

Choose the case fold to match the case the naxp already prints. `\C` preserves the
encoding of a naxp whose canonical strings are upper case, and `\c` of one whose
canonical strings are lower. The wrong choice keeps the count of encoded values
and their order, and decodes every one of them into the other case. A naxp whose
canonical form is mixed cannot be wrapped in either without changing what its
stored values decode to.

Wrapping can also make a well-formed naxp invalid, under W3.
`A!?|a` is well formed: `A` matches the left branch and canonicalises to nothing,
and `a` matches only the right. Case fold it and `a` matches both, so it has two
canonical forms.

<a id="well-formedness"></a>
## 10. Well-formedness

A naxp that parses may still be invalid. Six rules apply, W1 to W6, the W
standing for well-formedness.

**W1. A rendering must be one of the strings it replaces.** In `x!y`, `y` must
generate exactly one string, and `x` must generate it.

`\s!(B|C)` is invalid, because there would be no basis on which to choose
between `B` and `C` when reconstituting. So is `\s!\-`, because the subject never
accepts a hyphen and the reconstituted text would not re-encode. `[\s\-]?!\-` is
fine, a hyphen being one of the things the subject accepts.

Deletion is the case where the rendering is empty. `\s?!()` is legal because `\s?`
generates the empty string; `\s!()` is not, because `\s` does not. So no rule
against deleting a mandatory element is needed.

The same rule constrains `!!`. Since `x!!` expands to `x?!(x)`, the subject of a
`!!` must itself generate exactly one string: `\s!!` and `(ABC)!!` are legal;
`\A!!`, `[AB]!!` and `(B|C)!!` are not. `x!?` can never fail it, since `x?`
always generates the empty string. So `\A!?` is legal where `\A!!` is not: it
accepts a capital or nothing, discards which, and prints nothing.

**W2. `!` may not nest.** Neither operand of a `!` may contain another `!`.

`(\s!?)!?` is invalid, and so are `(A|B)!(B!B)`, whose rendering hides one, and
`(\CA)!A`, a case fold being a `!` once expanded. The rule reads the expansion
rather than the case fold as written, so a case fold with nothing to do is
invisible to it and `(\C\9)!5` is legal, being `\9!5`. Moving the case fold
outside the `!` gives the naxp
that was meant in either case, and `\CA!A` parses that way already.

**W3. Text unification must be single valued.** For every string a naxp accepts,
replacing each text unification by its rendering must give exactly one result.

`A B!! B? C` is invalid. It satisfies W1, the subject of the `!!` being a single
string, but the input `ABC` reads two ways. Take the `B` as the subject of the
`!!`, with the optional one absent, and the canonical form is `ABC`; take it as
the optional one and the text unification contributes its rendering as well, giving
`ABBC`. One input, two canonical forms, two values.

Ambiguity between alternatives needs no rule. The encoded value of a string
depends only on its canonical form, not on which parse produced it, and outside
text unifications the canonical form is the string itself, so `A|A` and `A`
produce the same encoding. A text unification is the one construct that rewrites
what was matched, so it is the only place where two parses of one string can
disagree.

A sufficient condition that is easy to check by eye: no character that can
begin a text unification's subject can also be the next character of a string
in which that subject is omitted. Then at every position it is settled by the
character read whether the subject is present. The postcode example below
relies on this, since a space is neither a digit nor an upper case letter. The
general test is decidable, since the map from an accepted string to its canonical
form is a finite state transduction, and whether a finite state transducer is
single valued can be decided.

**W4. Character ranges, decimal ranges and intervals must satisfy the
constraints stated for them above.** Those are the one under
[6. Character sets](#character-sets), the six under
[7. Decimal ranges](#decimal-ranges) and the three under
[8.2 `{}` interval](#interval). Each of them is decided from the tokens alone,
so an implementation may report a W4 fault while parsing; the rule is named
separately from syntax because each of the constructs parses and only then
fails a bound.

**W5. A naxp may not have more than 2<sup>64</sup>&#xA0;−&#xA0;1 encoded values.** The
count of encoded values is the size of the canonical language, defined under
[The encoding](#the-encoding). The limit is 18 446 744 073 709 551 615, the
largest unsigned 64 bit integer, and since values run from 1 with no gaps it is
also the largest integer the encoding can produce. `\9{19}` is legal and `\9{20}`
is not.

The limit is what an unsigned 64 bit integer holds once zero is reserved, and
every narrower width stops at the same point: a naxp of 255 values fits a byte
and one of 256 does not. An implementation should hold a value in an unsigned
64 bit type wherever the language has one, a naxp value being a count. A signed
64 bit type holds every value, since the bit patterns round trip and equality on
them is equality on values, but past 2<sup>63</sup>&#xA0;−&#xA0;1 the values read
as negative, so the guarantee under [Ordering](#ordering) is visible only where
the comparison is unsigned. Not every consumer can hold a 64 bit value at all;
[Data portability concerns](#data-portability-concerns) sets out what each
representation costs.

**W6. A naxp's machines may not exceed 2000 states.** Four machines are built from
a naxp: the minimal machine for its canonical language *C*, the minimal machine
for the language it accepts *L*, the machine that computes canonical forms, and
the product machine that decides W3. None of them may have more than 2000 states.

The machine that computes canonical forms keeps a **register**: a character it has
read but cannot yet place is held as a reference to how far back it was read
rather than as its value. Its budget counts its states **and its register depth**
together, because a register is memory the state count does not see. The depth is
small beside the states: eleven for the widest marked decimal range the
language admits, since a wider one is refused by the machine that decides W3
before this one is built, and two for a postcode.

The machines for *L* and *C* are minimal and so unique, and counting their
states needs no further definition. The other two are constructions, which
[Building the machines](#building-the-machines) fixes; its figures are the ones
W6 caps.

W5 bounds the encoded values and not the work. `(A{99}){99}` is eleven characters
of pattern denoting a single string of 9 801 characters, whose minimal machine has
9 802 states and exactly one encoded value. A string of *n* characters forces a
machine of at least *n* + 1 states, so no naxp satisfying W6 generates a string
longer than 1999 characters; that bound is a consequence of W6 rather than a rule
beside it.

The figure is part of the language, since an implementation choosing its own
would disagree with another about which naxps are valid.

<a id="building-the-machines"></a>
## 11. Building the machines

This section is for implementers. It assumes the vocabulary of finite automata
and transducers, and a reader who wants to write naxps rather than implement
them can skip it: nothing after it depends on it except the counts W6 caps.

W6 caps a count, so the count has to be the same everywhere, and a count that
decides validity cannot be left to whoever writes the implementation. The
machine that computes canonical forms and the product that decides W3 are
constructions, and two reasonable constructions give different numbers. This
section fixes them. It is the only place in this document that prescribes an
algorithm, and it does so because W6 decides which naxps are naxps.

<a id="the-machine-that-computes-canonical-forms"></a>
### 11.1 The machine that computes canonical forms

A state is a set of parses, each with its pending output: what it has emitted
that the machine has not.

1. The start state is the whole transduction with nothing pending.
2. The characters a state can read are partitioned into **blocks**: the coarsest
   partition in which each block lies wholly inside or wholly outside the
   **first set** of every parse, the set of characters that parse can read
   next, so that every parse treats the whole block alike.
3. Reading a block, each parse's pending output is what was pending before,
   followed by what the step emits. A character copied from the input is written
   as a **reference** to how far back it was read, except where the block holds
   one character only, in which case that character is written instead.
   Everything already pending grows one step older.
4. The transition emits the longest common prefix of the parses' pending outputs,
   which may not stop between the two parts of a reference. What is left over is
   pending in the state reached.
5. Where two parses reach the same remainder of the naxp with different pending
   output, one string would have two canonical forms and the naxp breaks W3 –
   unless one of them has the character being read pending, in which case the
   block is split into single characters and the step retaken, since fixing the
   character may make the two agree.
6. The machine is then minimised: transitions agreeing on what they emit and where
   they go are merged over the union of their blocks, and states are shared
   bottom up, two being one state when they emit the same thing at the end of
   the input and have the same transitions. The machine is acyclic, so this is
   minimisation.

The count W6 caps is the states of the minimised machine plus its register depth,
which is how far back its deepest reference reaches.

<a id="the-machine-that-decides-w3"></a>
### 11.2 The machine that decides W3

A state is an unordered pair of parses with the **delay** between them: the
output one parse has emitted beyond the other. Since a common prefix is
committed at every step, at most one of the two has emitted anything beyond
the other, so the delay is one string and a note of whose it is. For example,
after reading `A` in `A\s!!B|AB`, the first parse has emitted `A ` and the
second `A`, and the delay is a space on the first side. Both then read `B` and
end with the delay still there, which is how the machine finds that `AB` has
two canonical forms and the naxp breaks W3. Where both would be non-empty the two outputs
already differ and the delay collapses to a flag that records the disagreement.

The pair is stepped over blocks as in 11.1, but with the partition refined so
that every character appearing in a rendering stands alone, because whether a
copied character agrees with a rendered one depends on which character was
read; so the blocks here are not the coarsest partition. Where a pair can end the
input on both sides and the delay says their outputs differ, the naxp breaks W3.
The count W6 caps is the number of pairs reached.

<a id="the-encoding"></a>
## 12. The encoding

A naxp maps each string it accepts to a positive integer, and maps that integer
back to a string. The mapping is defined on the strings alone, with no reference
to any machine, so two naxps that accept the same strings and canonicalise them
the same way assign the same integers however differently they are written.
[Computing the encoding](#computing-the-encoding) gives a procedure that
implements the definition; where the two appear to differ, the definition
governs.

Write Σ for the matchable characters, `0x20` to `0x7E`.

<a id="three-languages"></a>
### 12.1 Three languages

**The accepted language** *L* is the set of strings the naxp matches. It is
finite and non-empty for every naxp: there is no unbounded repetition, and every
atom matches at least one string.

**The canonical form** ρ(*w*) of an accepted string *w* is *w* with the match of
each text unification replaced by its rendering. W3 is exactly the
requirement that ρ is a function, so that no accepted string has two canonical
forms. Where a naxp contains no text unification once its case folds and
marked decimal ranges are expanded, ρ is the identity.

**The canonical language** *C* is ρ(*L*). It is the accepted language of the
canonical naxp, which is the naxp with every text unification rewritten in place:

| Written | Rewrites to |
| --- | --- |
| `x!y` | `y` |
| `x!!` | `x` |
| `x!?` | `()` |
| `\Cx` | `x` with every letter in upper case |
| `\cx` | `x` with every letter in lower case |

The last two rows follow from the first three, a case fold being shorthand for
text unifications whose renderings are in canonical case. A marked decimal range
needs no row of its own for the same reason: expanding it puts `0!!` and `0!?`
in the tree, and the first three rows take it from there.

W1 puts every rendering among the strings its subject accepts, so *C* is a subset
of *L*, and ρ leaves every member of *C* alone.

<a id="the-order"></a>
### 12.2 The order

Within a language *M*, the continuation of *M* after a character *c* is the set
of strings *w* for which *cw* is in *M*. Two characters belong to the same
**first class** of *M* when their continuations are equal and non-empty. The
first classes partition the characters that can begin a string of *M*.

Character sets are ordered by writing each as the string of its members in
ascending ASCII order and comparing those strings by code point. The empty set comes
first, and `[02-9]` comes before `[1]`.

The order on *M* is then defined by recursion. The empty string precedes every
other string of *M*. For two other strings *au* and *bv*:

- if *a* and *b* lie in different first classes, order the strings by the set
  order of those classes;
- if *a* and *b* lie in the same first class and differ, order the strings by the
  ASCII order of *a* and *b*;
- if *a* and *b* are the same character, order *u* and *v* within the
  continuation after *a*.

This is a strict total order on *M*. It is neither lexicographic nor shortlex. In
`AB|B` the first classes are `[A]` and `[B]`, so `AB` precedes `B`.

<a id="values"></a>
### 12.3 Values

The **encoded value** of an accepted string *w* is one plus the count of strings
of *C* that precede ρ(*w*) in the order on *C*. Encoded values run from 1 to the
size of *C* with no gaps.

**Zero is reserved.** It is the result for invalid text, and it is never the
encoded value of a string the naxp accepts.

Decoding inverts the rank: the string of encoded value *n* is the *n*th string of
*C* in that order. So decoding the encoded value of an accepted string yields its
canonical form, and encoding the string of a value yields that value back.
Decoding is what the earlier sections call reconstitution.

What follows from this:

- Only the meaning of a naxp matters. Both *L* and ρ are properties of what a
  naxp accepts and prints rather than of how it is spelt, so `A(B|C)` and `AB|AC`
  assign the same values, and so do `A|A` and `A`.
- Every string a text unification accepts takes the same value, since those
  strings share a canonical form. `M1 1AA` and `M11AA` encode alike under the
  postcode naxp below, and the value decodes to the form with the space.
- The ordering guarantee for equal width decimal ranges under
  [Ordering](#ordering) follows from the order defined here, and applies to
  canonical forms.

A rendering is not merely cosmetic. It determines *C*, so changing one can change
which value a string takes: `(A|b)!bX|BY` gives `BY` the value 1, while
`(A|b)!AX|BY` gives it 2. But two naxps can agree on every value and still decode
differently. `[\s\-]!?` and `[\s\-]?!\-` both accept a space, a hyphen or
nothing, both give every string they accept the value 1, and they print nothing
and a hyphen respectively. Equivalence of values and equivalence of text are
different relations, and the first is the one that matters for storage.

<a id="computing-the-encoding"></a>
## 13. Computing the encoding

This section is normative. An implementation need not follow it step by step, but
must agree with it on every naxp and every string.

The construction is proved to compute the definition above, and to be independent
of how a naxp is written, in the analysis published with the reference
implementation.

**Step 1. Canonicalise.** Expand the decimal ranges and intervals into ordinary
expressions, then rewrite each text unification as in the table above to get the
canonical naxp. The result denotes *C*. The order of the two matters, since a
marked decimal range yields text unifications only once it is expanded.

**Step 2. Build the machine.** There are two kinds of state. The *terminal* state
has no transitions. An ordinary state carries a list of transitions, each a
character set paired with a next state, sorted by the set order.

For a language *M*, the state of *M* has these transitions:

- the pair (∅, terminal), present exactly when *M* contains the empty string;
- for each first class *D* of *M*, the pair (*D*, *s*), where *s* is the terminal
  state if the continuation after *D* is the empty string alone, and otherwise
  the state of that continuation.

The empty set is least in the set order, so the end of text transition, when
present, comes first. The start state is the state of *C*.

The recursion terminates because a continuation strictly shortens the longest
string. Two states are equal when their transition lists are equal, so building
bottom up and sharing equal states gives the minimal machine directly, with no
separate minimisation pass. That sharing is what makes the values depend on the
language rather than on how the naxp is written.

**Step 3. Count.** The string count of the terminal state is 1. The string count of
an ordinary state is the sum over its transitions of

> max(1, size of *D*) × string count of the next state

The string count of the start state is the size of *C*. The naxp is invalid under
W5 if it exceeds 2<sup>64</sup>&#xA0;−&#xA0;1. **Compute the count so that it cannot overflow
silently.** A single multiplication can wrap from operands that were both
themselves legal, and the limit is the full width of a 64 bit accumulator, so a
wrap cannot be detected afterwards by comparing against it. Test each
multiplication and each sum before performing it and carry a flag once one would
overflow, or count in a wider type. Either way, test at every step rather than
once at the end.

**Step 4. Encode.** Write rank(*c*, *D*) for the 0 based position of *c* within
*D* in ascending ASCII order. To encode a string, canonicalise it and walk from
the start state.

```
encode(terminal, w) = 1 if w is empty, else 0

encode(state, w):
    if w is empty:
        return 1 if state has an end of text transition, else 0
    offset := 0
    for each transition (D, next) of state, in order:
        n := string count of next
        if the first character c of w is in D:
            e := encode(next, w less its first character)
            return 0 if e = 0, else offset + n × rank(c, D) + e
        offset := offset + n × max(1, size of D)
    return 0
```

The value is a mixed radix positional number, most significant digit first.
Passing a transition skips every value below it, the rank of the character within
its set is the leading digit, and the value of the remainder of the string is the
rest.

**Step 5. Decode.** The inverse walk, with integer division:

```
decode(terminal, n) = the empty string

decode(state, n):                    -- 1 ≤ n ≤ string count of state
    for each transition (D, next) of state, in order:
        if D is empty:
            return the empty string if n = 1
            n := n - 1
        else:
            m := string count of next
            if n ≤ size of D × m:
                return the character of D at rank (n - 1) / m
                       followed by decode(next, ((n - 1) mod m) + 1)
            n := n - size of D × m
```

Decoding uses the canonical machine only. The accepted language plays no part in
it.

**Computing ρ.** An implementation may derive the canonical form however it
likes, whether by a separate pass over a machine for *L* or by a single pass over
a product of a canonicalising transducer with the canonical machine. Nothing in
the definition depends on the choice.

<a id="a-worked-value"></a>
### 13.1 A worked value

`#[0-10]` contains no text unification, so it canonicalises to itself, and it expands
to `[0-9] | 10`. The continuation after each of `0`, `2`, `3` … `9` is the empty
string alone; the continuation after `1` is the empty string or `0`. So the first
classes are `[02-9]` and `[1]`, in that order, and the machine is

```
start   ([02-9], terminal)  ([1], A)         count  9 × 1 + 1 × 2  = 11
A       (∅, terminal)  ([0], terminal)       count  1     + 1      =  2
```

`0` takes the value 1 and `9` takes 9, both from the first transition. `1` takes
9 + 2 × 0 + 1 = 10 and `10` takes 9 + 2 × 0 + 2 = 11. This is the case
[Ordering](#ordering) warns about: `1` sorts above `9` because it is the only
leading digit that can carry a second.

`#[00-10]` expands to `0[0-9] | 10`, giving every match the same width. The first
classes at the leading position are then `[0]` and `[1]`, in numeric order, and
nothing has to be split, so `00` … `09` take 1 … 10 and `10` takes 11.

<a id="worked-example"></a>
## 14. Worked example

UK postcodes, in which the separating space is optional on input and present in
the canonical form:

```
\A \A? \9 \X? \s!! \9 \A \A
```

The whitespace between elements is ignored and is there only for legibility. This
matches `M1 1AA`, `CR2 6XH`, `DN55 1PT`, `W1A 1AA` and `EC1A 1BB`, and matches
each of them equally with the space omitted. Both forms of a given postcode
encode to the same value, and reconstituting that value yields the form with the
space.

The naxp satisfies W3 because a space appears in neither `\X` nor `\9`, so a
string that omits the space can never be confused with one that includes it.

The canonical naxp is `\A \A? \9 \X? \s \9 \A \A`, since `\s!!` rewrites to `\s`.
Its positions are independent, so the count of encoded values is

> 26 × 27 × 10 × 37 × 1 × 10 × 26 × 26 = 1 755 842 400

comfortably inside W5, and inside 31 bits.

<a id="relations-between-naxps"></a>
## 15. Relations between naxps

A naxp travels with its data. Text is encoded under one naxp and the value is
stored, so the question that matters when a naxp is changed is what the change
does to values already written. This
section defines three relations between two naxps that answer it.

Write *a* and *b* for the two naxps, with accepted languages *L*, canonical
languages *C* and canonicalisations ρ as [The encoding](#the-encoding) defines
them. The **encoding** of *a* is the function e_a(*w*) = the encoded value of
*w*, defined on *L_a*.

Each relation is one of **equal**, **subset**, **superset** or
**incomparable**, taken from *a*'s point of view, where subset means every member
of *a*'s set is a member of *b*'s and *b* has at least one more. Incomparable is
the answer whenever neither contains the other, whether the two are disjoint or
overlap in part.

| Relation | Compares | Answers |
| --- | --- | --- |
| Accepted text | *L_a* to *L_b* | Does text that was valid stay valid? |
| Encoding | the graph of e_a to the graph of e_b | Do stored values still mean what they meant? |
| Printed text | *C_a* to *C_b* | Can what is printed on decoding change? |

The graph of an encoding is its set of (text, value) pairs, not its set of
values. The values of any naxp are 1 to the size of its canonical language with
no gaps, so comparing those sets would compare nothing but the counts.

The three are independent, with one exception: equal, subset or superset on
the encoding forces the same on the accepted text, since the texts of the pairs
are the accepted strings, and incomparable on the accepted text forces
incomparable on the encoding. Nothing else follows in either direction.

<a id="value-agreement"></a>
### 15.1 Value agreement

Graph inclusion holds exactly when *L_a* is contained in *L_b* and the two
encodings agree everywhere *a* is defined. So the encoding relation is the
accepted text relation refined by one condition:

> **Value agreement.** e_a(*w*) = e_b(*w*) for every *w* in *L_a* ∩ *L_b*.

Where value agreement holds, the encoding relation is the accepted text
relation. Where it fails, the encoding relation is incomparable, whatever the
accepted texts do.

Two facts about it are easy to get wrong.

**Agreeing on values does not mean canonicalising alike.** `(A|B)!A` and
`(A|B)!B` accept the same two strings, have one encoded value each, and give
every string they accept the value 1. Their encodings are equal. Their printed
text is incomparable, one printing `A` and the other `B`. The specification's own
pair `[\s\-]?!\-` and `[\s\-]!?` behaves the same way, as does a naxp wrapped in
`\C` against the same naxp wrapped in `\c`. So agreement of ρ is sufficient for
value agreement, given that the ranks agree, and it is not necessary. A test
that gated the encoding relation on ρ would report incomparable on all three of
those pairs, which is a wrong answer about stored data.

**Canonicalising alike does not mean agreeing on values.** Where a canonical
string is held by both, its value under each is its rank, and two ranks that
differ are a witness on their own. This is what makes a breaking change exact to
detect: no knowledge of ρ is needed to establish it.

<a id="why-the-relation-has-to-be-computed"></a>
### 15.2 Why the relation has to be computed

The order on a canonical language is built from first classes, and adding a
single string can repartition them. Nothing about where the new strings sort
establishes that the old values survive.

Adding `| (GIR \s!! 0AA)` to the postcode naxp of [Worked
example](#worked-example) gives `GIR 0AA` the largest encoded value there is, so
everything added sorts after everything already present. It still moves most of
the existing values. `G` acquires a continuation the other letters do not have,
so it splits out of its class; the class string `ABCDEF…` sorts before the
class string `G`, and every postcode not beginning with `G` drops below every
one that does. `ZZ9Z 9ZZ` falls from 1 755 842 400 to 1 688 310 000 while
`GI9Z 9ZZ` climbs from 430 206 400 to 1 755 842 400.

<a id="the-first-divergent-value"></a>
### 15.3 The first divergent value

Where two naxps disagree, the useful witness is a value rather than a string.
The **first divergent value** of *a* and *b* is the least *n* held by both, that
is *n* no greater than either canonical language's size, whose decoding under the
two differs; it is 0 where no such *n* exists.

Values held by only one of the two do not count. An extension that adds its
strings after all the existing ones therefore gives 0, and the difference in the
counts says the rest.

Adding `GIR 0AA` to the postcode naxp gives 405 194 401, where *a* decodes
`G0 0AA` and *b* decodes `H0 0AA`, the two having agreed on `FZ9Z 9ZZ` at
405 194 400.

<a id="deciding-the-relations"></a>
### 15.4 Deciding the relations

All three relations are decidable, the languages being finite.

Accepted text and printed text are comparisons of the minimal machines for *L*
and for *C*, whose sizes W6 bounds, so both are settled within the budget W6
already provides.

The encoding relation needs value agreement, and W6 bounds no machine that
decides it. Where neither naxp contains a text unification once expanded, ρ is
the identity on both and
value agreement is a comparison of ranks over the two canonical machines, again
within W6's bound. Where either contains a `!`, an implementation may fail to
decide it within whatever budget it sets.

An implementation that cannot decide the encoding relation must say so. It may
not report incomparable, which is a claim, nor any other relation. The other two
relations remain available in that case, and a rank disagreement remains exact,
so a change that breaks stored values can always be reported as breaking.

<a id="data-portability-concerns"></a>
## 16. Data portability concerns

W5 caps a naxp at 64&#xA0;bits of information (less the reserved value 0). How an
encoded value is represented needs consideration when systems or languages have
to interoperate, or when encoded values are exchanged. There are two issues:

1. **Availability**. Does the language or system offer this representation?
2. **Portability**. Can encoded values be moved safely between systems using this representation?

The table below summarises common representations. Portability is graded by
what can go wrong in transit: *excellent* where every system reads the value
back exactly; *moderate* where a system reads it exactly but may show a large
value as negative, since a value above 2<sup>63</sup> − 1 has the sign bit set;
*poor* where a common system cannot hold the type at all; *nil* where the
representation itself differs between languages.

|Representation|Maximum bits |Availability|Portability|
|:---|:--:|:---|:---|
|Signed 32-bit integer|31|Widely available|Excellent|
|Unsigned 32-bit integer|32|Limited, e.g. present in C, C++, C# and Rust, but not in R or JavaScript|Poor|
|Double precision floating point|53|Widely available|Excellent|
|Signed 64-bit integer|63|Widely available, natively in e.g. C, C++, C#, Java, Rust and SQL `BIGINT`, and by other means in R (`bit64::integer64`, which reinterprets the bits of a double) and Python (`numpy.int64`)|Moderate|
|Unsigned 64-bit integer|64|Available in e.g. C, C++, C# and Rust, plus unsigned helpers on `Long` in Java and `numpy.uint64` in Python, but in R only by carrying the bits in `bit64::integer64`, which reserves the single value 2<sup>63</sup> for `NA`|Poor|
|'Big int'|Unlimited, though a naxp needs at most 64|Language specific (`BigInt` in JavaScript, `int` in Python)|Nil|

The largest portable naxp is therefore 53&#xA0;bits, represented as a double
precision floating point number.

Data can also be exchanged as text in decimal format. In theory this has
arbitrary precision; in practice it has to be parsed to and from one of the
representations above. JSON warrants a special mention: its grammar sets no limit
on a number, but most parsers read one into a double, so values above
53&#xA0;bits have to be written as and parsed from strings.

<a id="conformance"></a>
## 17. Conformance

An implementation conforms to this version of naxp when, for every naxp and
every string, it agrees with this document on four things:

1. **Validity.** Which patterns are naxps, and which naxps are well formed. A
   pattern this document refuses must be refused, and one it admits must be
   admitted.
2. **Acceptance.** Which strings a naxp accepts.
3. **The canonical form** of every accepted string.
4. **The encoded value** of every accepted string, and the string every value
   from 1 to the size of the canonical language decodes to.

Invalid text encodes to 0, and 0 is never the encoded value of an accepted
string. An implementation that cannot represent a value the naxp defines must
fail rather than truncate it; [16. Data portability
concerns](#data-portability-concerns) sets out what each representation holds.

No more is required. How an implementation builds its machines, words its
faults and shapes its interface is its own affair. Where it reports which rule an invalid naxp breaks, the
names to use are `syntax` for a pattern that does not parse and W1 to W6 for a
naxp that parses and is not well formed.

The two constructions this document does prescribe are in [11. Building the
machines](#building-the-machines), and they are prescribed because W6 caps counts
that two reasonable constructions would compute differently.

<a id="test-data"></a>
### 17.1 Test data

Language-neutral test data is published with the reference implementation,
one file per minor version. It was generated from this document rather than from any
implementation, so an implementation cannot define its own truth by passing it.

**The data checks conformance and does not define it.** Where the two disagree,
this document governs and the data is wrong. Passing it is necessary and not
sufficient, for two reasons.

- **The ceiling on a marked decimal range is not tested and cannot be.** The
  generator builds a range by listing the strings between its bounds, so an
  eleven digit range is 10<sup>11</sup> of them and is refused long before it
  reaches any machine. What stops such a range in the language is the machine
  that decides W3; what stops it in the generator is arithmetic. The two agree on
  the answer for different reasons.
- **Four cases have one witness rather than two.** Every case the generator can
  enumerate has its values produced twice by independent routes, once by ranking
  the canonical language and once by walking the minimal machine. The four
  holding between 1.7 billion and 10<sup>19</sup> encoded values are beyond any
  enumeration, so only the machine has spoken for them.

<a id="what-this-document-does-not-cover"></a>
## 18. What this document does not cover

A unique minimal form. The values a naxp assigns are canonical, and two naxps
that accept the same strings and canonicalise them the same way can be shown to
be the same mechanically by comparing the machines built from them. The text you would print
to display such a machine back as a naxp is a separate question, and this version
does not fix it.

A procedure an implementation must follow to decide W3. [The machine that
decides W3](#the-machine-that-decides-w3) fixes one construction, because W6
caps the count of pairs it reaches and two constructions would disagree on that
count. How an implementation reaches the verdict is its own business, so long as
it agrees on both the verdict and the count.
