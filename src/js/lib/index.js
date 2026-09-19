// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * The entry point of the naxp package.
 *
 * Six names, mirroring the C# reference implementation, which makes the same six public. Parse a
 * naxp, then ask it whether it accepts a string, what value a string encodes to, what string a
 * value decodes to, and what a string's canonical form is. `Naxp.compare` says how one naxp stands
 * to another, as a {@link NaxpComparison} of three {@link SetRelationship} values. `naxp.emit`
 * writes the same questions out as source in an {@link OutputLanguage}, over a
 * {@link NaxpValueType}, for a caller who wants no dependency on this library at run time.
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
export { NaxpComparison } from './naxp-comparison.js';
export { NaxpValueType } from './emitter.js';
export { OutputLanguage } from './output-language.js';
export { SetRelationship } from './set-relationship.js';
