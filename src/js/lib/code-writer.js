// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

/**
 * Writes generated source a line at a time, indenting each line to the current block depth.
 *
 * Written for the language emitters, which all build text the same way. Block syntax comes from
 * the emitter and indentation from the caller, so one writer serves both brace languages and
 * indentation languages: a Python emitter would pass no block lines, ending each block's header
 * with a colon before {@link CodeWriter#openBlock}, and a caller wanting spaces would say so.
 *
 * The C# reference implementation splits this into a `StringBuilder` writer and a `TextWriter`
 * one, because .NET has two sinks worth serving. Here there is one: the lines are collected and
 * joined on {@link CodeWriter#toString}, and a caller wanting a stream writes the result to it.
 * What ends a line is the caller's, never the host's, so one naxp is the same text everywhere.
 */
export class CodeWriter {
	/**
	 * @param {string} initialIndent What every non-empty line starts with, ahead of the depth
	 * indentation, so a fragment can sit inside an already-indented wrapper.
	 * @param {string} indent What one level of indentation is written as.
	 * @param {string | null} blockOpen The line {@link CodeWriter#openBlock} writes before
	 * indenting, or null for none, where a language opens a block by indentation alone.
	 * @param {string | null} blockClose The line {@link CodeWriter#closeBlock} writes after
	 * outdenting, or null for none.
	 * @param {string} newLine What ends every line.
	 */
	constructor(initialIndent = '', indent = '\t', blockOpen = '{', blockClose = '}', newLine = '\n') {
		this.initialIndent = initialIndent;
		this.indent = indent;
		this.blockOpen = blockOpen;
		this.blockClose = blockClose;
		this.newLine = newLine;

		/** @type {string[]} The lines written so far, each without its terminator. */
		this.lines = [];

		/** The current block depth. */
		this.depth = 0;
	}

	/**
	 * Writes one line at the current indentation, or an empty line with no indentation at all
	 * where there is no text.
	 *
	 * @param {string} [text] The line, without a terminator.
	 */
	line(text) {
		if (text === undefined) {
			this.lines.push('');

			return;
		}

		this.lines.push(this.initialIndent + this.indent.repeat(this.depth) + text);
	}

	/** Opens a block: writes the block opening line, where the language has one, and indents. */
	openBlock() {
		if (this.blockOpen !== null) { this.line(this.blockOpen); }

		++this.depth;
	}

	/** Closes a block: outdents and writes the block closing line, where the language has one. */
	closeBlock() {
		--this.depth;

		if (this.blockClose !== null) { this.line(this.blockClose); }
	}

	/** Indents by one level, for constructs that are not blocks. */
	indentBy() {
		++this.depth;
	}

	/** Undoes one {@link CodeWriter#indentBy}. */
	outdent() {
		--this.depth;
	}

	/**
	 * Everything written so far, each line terminated.
	 *
	 * @returns {string} The fragment.
	 */
	toString() {
		return this.lines.length === 0 ? '' : this.lines.join(this.newLine) + this.newLine;
	}
}
