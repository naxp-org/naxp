// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * Fails if a published standard naxp has changed.
 *
 *     node standards/freeze.js PREVIOUS CURRENT
 *
 * PREVIOUS is the standards file as the last release published it, and CURRENT the one about to
 * be published. Every variant of every standard in PREVIOUS must be in CURRENT with its naxp
 * unchanged to the byte and the same largest encoded value, because values stored under a
 * standard naxp have to keep their meaning for good. New standards and new variants are free, as
 * are names, notes and examples.
 *
 * The release workflow runs this against the newest earlier tag, and copy-to-public.ps1 against
 * the public repository's newest tag before anything is copied.
 */

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Each way a published standard naxp has changed, as a sentence; empty where none has.
 *
 * @param {{standards: {id: string, variants: {id: string, naxp: string,
 * maxEncodedValue: string}[]}[]}} previous The standards as published.
 * @param {typeof previous} current The standards about to be published.
 * @returns {string[]}
 */
export function findChanges(previous, current) {
	const changes = [];
	const variants = new Map();

	for (const standard of current.standards) {
		for (const variant of standard.variants) { variants.set(`${standard.id}/${variant.id}`, variant); }
	}

	for (const standard of previous.standards) {
		for (const published of standard.variants) {
			const where = `${standard.id}/${published.id}`;
			const variant = variants.get(where);

			if (variant === undefined) {
				changes.push(`${where} has been removed.`);
			} else if (variant.naxp !== published.naxp) {
				changes.push(`${where} was ${JSON.stringify(published.naxp)} and is now ${JSON.stringify(variant.naxp)}.`);
			} else if (variant.maxEncodedValue !== published.maxEncodedValue) {
				changes.push(`${where} declared ${published.maxEncodedValue} values and now declares ${variant.maxEncodedValue}.`);
			}
		}
	}

	return changes;
}

if (process.argv[1] !== undefined && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
	if (process.argv.length !== 4) {
		console.error('Usage: node standards/freeze.js PREVIOUS CURRENT');
		process.exit(2);
	}

	const [previous, current] = process.argv.slice(2).map((file) => JSON.parse(readFileSync(file, 'utf8')));
	const changes = findChanges(previous, current);

	if (changes.length > 0) {
		console.error('A published standard naxp has changed, which would change the meaning of values stored under it:');
		for (const change of changes) { console.error(`  ${change}`); }
		process.exit(1);
	}

	console.log('Every published standard naxp is unchanged.');
}
