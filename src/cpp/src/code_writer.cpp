// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "code_writer.hpp"

#include <optional>
#include <string>
#include <string_view>
#include <utility>

namespace logmu::detail
{
	code_writer::code_writer(
		std::string& target,
		std::string_view initial_indent,
		std::string_view indent,
		std::optional<std::string> block_open,
		std::optional<std::string> block_close,
		std::string_view new_line)
		: target(target)
		, initial_indent(initial_indent)
		, indent(indent)
		, block_open(std::move(block_open))
		, block_close(std::move(block_close))
		, new_line(new_line)
	{
	}

	void code_writer::line()
	{
		this->target += this->new_line;
	}

	void code_writer::line(std::string_view text)
	{
		this->target += this->initial_indent;

		for (int i = 0; i < this->depth; ++i)
		{
			this->target += this->indent;
		}

		this->target += text;
		this->target += this->new_line;
	}

	void code_writer::open_block()
	{
		if (this->block_open.has_value())
		{
			this->line(*this->block_open);
		}

		++this->depth;
	}

	void code_writer::close_block()
	{
		--this->depth;

		if (this->block_close.has_value())
		{
			this->line(*this->block_close);
		}
	}
}
