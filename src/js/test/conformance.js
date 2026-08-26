// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import { readFileSync } from 'node:fs';

/**
 * `conformance/naxp-v0.5.json`, which was generated from the specification rather than from any
 * implementation. It is the oracle: the parser is not allowed to define its own truth.
 *
 * The counts and encoded values are carried as decimal strings, because a naxp may hold up to
 * 2^64 - 1 values and a JSON number is not safe above 2^53. They are left as strings here and
 * turned into BigInts where they are used, so that nothing passes through a number on the way.
 *
 * @returns {{
 *   naxpVersion: string,
 *   testDataVersion: number,
 *   cases: Array<{naxp: string, note?: string, valueCount: string, acceptedCount: string,
 *     complete: boolean, values: Array<{in: string, out: string, canon?: string}>,
 *     notAccepted: string[]}>,
 *   rejected: Array<{naxp: string, rule: string, note?: string}>,
 * }} The test data.
 */
export function loadConformanceData() {
	const path = new URL('../../../conformance/naxp-v0.5.json', import.meta.url);

	return JSON.parse(readFileSync(path, 'utf8'));
}
