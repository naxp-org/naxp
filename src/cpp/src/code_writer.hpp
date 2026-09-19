// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_CODE_WRITER_HPP
#define NAXP_CODE_WRITER_HPP

#include <optional>
#include <string>
#include <string_view>

namespace logmu::detail
{
	/// Writes pattern text a line at a time, indenting each line to the current block depth.
	///
	/// Written for the language emitters, which all build text the same way. Block syntax comes
	/// from the emitter and indentation from the caller, so one writer serves both brace
	/// languages and indentation languages: a Python emitter would pass no block lines, ending
	/// each block's header with a colon before `open_block`, and a caller wanting spaces would
	/// say so. The newline is the caller's too, so a fragment is the same text on every machine.
	///
	/// The C# has two sinks, a StringBuilder and a TextWriter. Everything here goes to a string
	/// the caller owns, which is the one sink C++ callers have asked for.
	class code_writer
	{
	public:
		/// @param target Where the text goes, appended to whatever it already holds.
		/// @param initial_indent What every non-empty line starts with, ahead of the depth
		///     indentation, so a fragment can sit inside an already-indented wrapper.
		/// @param indent What one level of indentation is written as.
		/// @param block_open The line `open_block` writes before indenting, or nothing where a
		///     language opens a block by indentation alone.
		/// @param block_close The line `close_block` writes after outdenting, or nothing.
		/// @param new_line What ends every line.
		code_writer(
			std::string& target,
			std::string_view initial_indent,
			std::string_view indent,
			std::optional<std::string> block_open,
			std::optional<std::string> block_close,
			std::string_view new_line);

		code_writer(const code_writer&) = delete;
		code_writer& operator=(const code_writer&) = delete;

		/// Writes an empty line, with no indentation.
		void line();

		/// Writes one line at the current indentation.
		///
		/// @param text The line, without a terminator.
		void line(std::string_view text);

		/// Opens a block: writes the block opening line, where the language has one, and indents.
		void open_block();

		/// Closes a block: outdents and writes the block closing line, where the language has one.
		void close_block();

		/// Indents by one level, for constructs that are not blocks.
		void indent_more()
		{
			++this->depth;
		}

		/// Undoes one `indent_more`.
		void indent_less()
		{
			--this->depth;
		}

	private:
		std::string& target;
		std::string initial_indent;
		std::string indent;
		std::optional<std::string> block_open;
		std::optional<std::string> block_close;
		std::string new_line;
		int depth = 0;
	};
}

#endif
