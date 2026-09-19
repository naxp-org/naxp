// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * Emits every conformance naxp in every output language through the JavaScript implementation
 * and writes the fragments to one file, so that another implementation's emitters can be held
 * byte-identical to them.
 *
 * This is differential testing, as the fuzz generator beside it is: the JavaScript emitters are
 * the standard here, having themselves been held byte-identical to the C# ones, and a difference
 * is a defect in one of the two. The output is not committed.
 *
 *     node conformance/emit/generate.js --out build/emit-expected.txt
 *
 * The file is a sequence of records, each a header line, the naxp on a line of its own, the
 * fragment as exactly the number of bytes the header states, and a newline:
 *
 *     === <language> <valueType> <prefix> <bytes>
 *     <naxp>
 *     <fragment>
 *
 * Every case is emitted with the prefix Case<i> and the widest value type. The first case is
 * emitted again with the bare names, each narrower type it fits, and a space indentation, so
 * that those paths are covered once each.
 */

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Naxp, NaxpValueType, OutputLanguage } from '../../src/js/lib/index.js';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

// The command line

const options = { out: 'emit-expected.txt', data: join(root, 'conformance', 'naxp-v0.10.json') };

for (let i = 2; i < process.argv.length; i += 2) {
	const name = process.argv[i].replace(/^--/, '');
	const value = process.argv[i + 1];

	if (!(name in options) || value === undefined) {
		console.error('Usage: node generate.js [--data FILE] [--out FILE]');
		process.exit(2);
	}

	options[name] = value;
}

// The records

const languages = Object.values(OutputLanguage);
const cases = JSON.parse(readFileSync(options.data, 'utf8')).cases;
const records = [];

function record(naxp, language, prefix, valueType, initialIndent = '', newLine = '\n', indent = '\t') {
	const fragment = Naxp.parse(naxp).emit(language, prefix, valueType, initialIndent, newLine, indent);
	const bytes = Buffer.byteLength(fragment, 'utf8');

	records.push(`=== ${language} ${valueType} ${prefix} ${bytes}\n${naxp}\n${fragment}\n`);
}

for (let i = 0; i < cases.length; i++) {
	for (const language of languages) {
		record(cases[i].naxp, language, `Case${i}`, NaxpValueType.UInt64);
	}
}

for (const language of languages) {
	record(cases[0].naxp, language, '', NaxpValueType.UInt64);
	record(cases[0].naxp, language, 'Narrow', NaxpValueType.Int8, '    ', '\r\n', '  ');
	record(cases[0].naxp, language, 'UKPostcode', NaxpValueType.UInt16);
	record(cases[0].naxp, language, 'Wide', NaxpValueType.Int64);
}

writeFileSync(options.out, records.join(''));

console.log(`Wrote ${records.length} fragments over ${cases.length} naxps to ${options.out}`);
