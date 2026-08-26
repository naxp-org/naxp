# NX grammar

**This needs updating!!!**

- We are **not** using `!` to indicate optional chars -- does it need escaping?
- Should `{` and `}` be escaped?
- Check range of chars, e.g. `0x00` and `0xFF` are allowed.

```
// The only valid whitespace is space (0x21).
// Literal spaces are ignored everywhere in an NX. Use "\s" to represent a space to be recognised.

expr ::= seq ( "|" seq)*

seq ::= element+

element ::= atom | integer_range | expr ("?" | "!" | "!!")? | "(" expr ")"

atom ::= literal_char | "[" chars_in_range+ "]" | builtin_range

integer_range ::= "#[" integer_literal "," integer_literal "]"

integer_literal := ["0","9"]+

chars_in_range ::= literal_char ("-" literal_char)?

builtin_range ::= "\" ("9" | "A" | "a" | "X" )

// The only valid (non-space) chars are ASCII from 0x21 to 0x7E.

literal_char ::= 
        // " " ( 20 ) // Use "\s"
        ["0","9"] ( [30,39] )
        ["A","Z"] ( [41-5A] )
        ["a","z"] ( [61-7A] )
        //"!" ( 21 )
        //""" ( 22 )
        //"#" ( 23 )
        "$" ( 24 )
        "%" ( 25 )
        "&" ( 26 )
        "'" ( 27 )
        //"(" ( 28 )
        //")" ( 29 )
        "*" ( 2A )
        "+" ( 2B )
        "," ( 2C )
        //"-" ( 2D ) // Valid outside [ ]
        "." ( 2E )
        "/" ( 2F )
        ":" ( 3A )
        ";" ( 3B )
        "<" ( 3C )
        "=" ( 3D )
        ">" ( 3E )
        //"?" ( 3F )
        "@" ( 40 )
        //"[" ( 5B )
        "\" ( 5C )
        //"]" ( 5D )
        "^" ( 5E )
        "_" ( 5F )
        "`" ( 60 )
        "{" ( 7B )
        //"|" ( 7C )
        "}" ( 7D )
        "~" ( 7E )
    | escaped_literal_char

escaped_literal_char ::= "\" plus
        "s" // Space
        "!" ( 21 )
        """ ( 22 )
        "#" ( 23 )
        "(" ( 28 )
        ")" ( 29 )
        "-" ( 2D )
        "?" ( 3F )
        "[" ( 5B )
        "]" ( 5D )
        "|" ( 7C )
```