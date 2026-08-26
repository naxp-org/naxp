// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * The entry point of the naxp package.
 *
 * Two names, mirroring the C# reference implementation, where `Naxp` is likewise the only public
 * type. Parse a naxp, then ask it whether it accepts a string, what value a string encodes to,
 * what string a value decodes to, and what a string's canonical form is.
 *
 * Everything else in `lib` is internal. JavaScript has no way to say so, but nothing else is
 * exported from here, and the tests reach the modules directly rather than through this file. That
 * matters most for `NaxpMessage`, whose members are spelled `NAXP1002_IntervalHyphen` as a note to
 * whoever is reading the library: the identifier a caller is given is `NAXP1002`, and the hint
 * would read as a promise about wording that has not been made.
 *
 * Widening this later is not a breaking change. Narrowing it is, which is why it starts here.
 */

export { Naxp, NaxpFormatError } from './naxp.js';
