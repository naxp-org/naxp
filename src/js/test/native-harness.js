// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/**
 * What the C and C++ emitter tests share: writing a harness to a scratch directory, putting it
 * through a compiler with every warning fatal, running it, and reading the verdict. The compiler
 * is how those tests check anything, so its absence is a failure and not a pass.
 */

/** The flags every harness compiles under. A fragment that is not clean under these is not finished. */
const WARNINGS = ['-Wall', '-Wextra', '-Werror', '-pedantic', '-O1'];

/**
 * Compiles and runs a harness, and asserts that it compiled, that it exited zero, and that it
 * reported checks passed, so that a harness which checked nothing cannot pass quietly.
 *
 * @param {string} compiler The compiler, `gcc` or `g++`.
 * @param {string} standard The language standard flag.
 * @param {string} fileName What to call the source file.
 * @param {string} source The harness.
 */
export function compileAndRun(compiler, standard, fileName, source) {
	const directory = mkdtempSync(join(tmpdir(), 'naxp-native-'));

	try {
		const file = join(directory, fileName);
		const executable = join(directory, 'harness.exe');

		writeFileSync(file, source, 'utf8');

		const compiled = spawnSync(compiler, [standard, ...WARNINGS, file, '-o', executable], { encoding: 'utf8' });

		assert.equal(compiled.error, undefined, `${compiler} is not installed, so the emitted code cannot be compiled.`);
		assert.equal(compiled.status, 0, compiled.stdout + compiled.stderr);

		const ran = spawnSync(executable, [], { encoding: 'utf8' });

		assert.equal(ran.error, undefined, 'The harness did not start.');
		assert.equal(ran.status, 0, ran.stdout + ran.stderr);
		assert.ok(ran.stdout.includes('checks passed'), ran.stdout);
	} finally {
		rmSync(directory, { recursive: true, force: true });
	}
}

/**
 * Text as a C or C++ string literal. Unprintable characters go in octal, which stops at three
 * digits where a hexadecimal escape would swallow the digit after it; a question mark is escaped
 * because `-std=c99` honours trigraphs, and C++ does not mind.
 *
 * @param {string} text The text.
 * @returns {string} The literal.
 */
export function cString(text) {
	let literal = '"';

	for (let i = 0; i < text.length; ++i) {
		const c = text[i];
		const code = text.charCodeAt(i);

		if (c === '"') {
			literal += '\\"';
		} else if (c === '\\') {
			literal += '\\\\';
		} else if (c === '?') {
			literal += '\\?';
		} else if (code < 0x20 || code > 0x7e) {
			literal += '\\' + code.toString(8).padStart(3, '0');
		} else {
			literal += c;
		}
	}

	return literal + '"';
}
