// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "cpp_emitter.hpp"

#include "c_family_emitter.hpp"
#include "code_writer.hpp"
#include "emitter.hpp"

#include <optional>
#include <string>

namespace logmu::detail
{
	const cpp_emitter& cpp_emitter::instance()
	{
		static const cpp_emitter shared;

		return shared;
	}

	// The spelling of C++

	std::string cpp_emitter::step_linkage() const
	{
		return "inline";
	}

	std::string cpp_emitter::type_prefix() const
	{
		return "std::";
	}

	std::optional<std::string> cpp_emitter::digit_separator() const
	{
		return "'";
	}

	std::string cpp_emitter::pointer(const std::string& type, const std::string& name) const
	{
		return type + "* " + name;
	}

	std::string cpp_emitter::by_reference(const std::string& type, const std::string& name) const
	{
		return type + "& " + name;
	}

	std::string cpp_emitter::dereference(const std::string& name) const
	{
		return name;
	}

	std::string cpp_emitter::increment(const std::string& name) const
	{
		return name + "++";
	}

	std::string cpp_emitter::address_of(const std::string& name) const
	{
		return name;
	}

	std::string cpp_emitter::cast(const std::string& type, const std::string& expression) const
	{
		return "static_cast<" + type + ">(" + expression + ")";
	}

	void cpp_emitter::comment(code_writer& writer, const std::string& text) const
	{
		writer.line("/// " + text);
	}

	// The header

	void cpp_emitter::emit_header(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();

		writer.line("// Needs <cstdint>, <stdexcept>, <string> and <string_view>. Everything is inline, so the");
		writer.line("// fragment can sit in a header; give each naxp its own prefix where several share a program.");
		writer.line();
		this->comment(writer, "The largest encoded value this naxp produces, which is also how many it has.");
		writer.line("inline constexpr " + fragment.value_keyword + " " + fragment.max_encoded_value_name + " = " + fragment.max_encoded_value_literal + ";");
		writer.line();
		this->comment(writer, "The length of the longest string this naxp can decode a value to.");
		writer.line("inline constexpr int " + fragment.max_length_name + " = " + decimal(fragment.max_length()) + ";");
		writer.line();
	}

	// The public functions

	void cpp_emitter::emit_publics(fragment& fragment) const
	{
		this->emit_accepts(fragment);
		this->emit_encode(fragment);
		this->emit_decode(fragment);
	}

	void cpp_emitter::emit_accepts(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();

		writer.line("/// Whether this naxp accepts text. A byte outside ASCII is never accepted.");
		writer.line("///");
		writer.line("/// @param text The text to test.");
		writer.line("/// @returns Whether the naxp accepts it.");
		writer.line("inline bool " + fragment.accepts_name + "(std::string_view text)");
		writer.open_block();
		writer.line("int state = 0;");
		writer.line();
		writer.line("for (char c : text)");
		writer.open_block();
		writer.line("state = " + fragment.accept_step_name + "(state, static_cast<unsigned char>(c));");
		writer.line();
		writer.line("if (state < 0) { return false; }");
		writer.close_block();
		writer.line();
		writer.line("return " + fragment.is_accepting_name + "(state);");
		writer.close_block();
		writer.line();
	}

	void cpp_emitter::emit_encode(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();
		const std::string& held = fragment.held_name;

		writer.line("/// The encoded value of text.");
		writer.line("///");
		writer.line("/// @param text The text to encode.");
		writer.line("/// @returns The encoded value, from 1 to " + fragment.max_encoded_value_name + ", or zero where the text is invalid.");
		writer.line("inline " + fragment.value_keyword + " " + fragment.encode_name + "(std::string_view text)");
		writer.open_block();

		if (fragment.canonicalises())
		{
			writer.line("char canonical[" + fragment.buffer_size() + "] = {};");

			if (fragment.needs_register())
			{
				// The characters read, oldest first, so a reference of depth d is the one at
				// register_depth - 1 - d. Shifting a buffer this small beats indexing a ring, and
				// it starts zeroed so that an early shift reads nothing undefined.
				writer.line("char " + held + "[" + decimal(fragment.register_depth()) + "] = {};");
			}

			writer.line("int length = 0;");
			writer.line("int state = 0;");
			writer.line();
			writer.line("for (char c : text)");
			writer.open_block();

			if (fragment.needs_register())
			{
				const std::string top = decimal(fragment.register_depth() - 1);

				if (fragment.register_depth() > 1)
				{
					writer.line("for (int h = 0; h < " + top + "; ++h) { " + held + "[h] = " + held + "[h + 1]; }");
				}

				writer.line(held + "[" + top + "] = c;");
			}

			writer.line("state = " + fragment.canonical_step_name + "(state, static_cast<unsigned char>(c), " + fragment.step_arguments("length") + ");");
			writer.line();
			writer.line("if (state < 0) { return " + fragment.value_zero + "; }");
			writer.close_block();
			writer.line();
			writer.line("length = " + fragment.finish_canonical_name + "(state, " + fragment.finish_arguments() + ");");
			writer.line();
			writer.line(fragment.value_is_widest
				? "return length < 0 ? 0ULL : " + fragment.rank_name + "(canonical, length);"
				: "return length < 0 ? static_cast<" + fragment.value_keyword + ">(0) : static_cast<" + fragment.value_keyword + ">(" + fragment.rank_name + "(canonical, length));");
		}
		else
		{
			writer.line("int state = 0;");
			writer.line("std::uint64_t total = 0ULL;");
			writer.line();
			writer.line("for (char c : text)");
			writer.open_block();
			writer.line("state = " + fragment.encode_step_name + "(state, static_cast<unsigned char>(c), total);");
			writer.line();
			writer.line("if (state < 0) { return " + fragment.value_zero + "; }");
			writer.close_block();
			writer.line();
			writer.line(fragment.value_is_widest
				? "return " + fragment.is_accepting_name + "(state) ? total + 1ULL : 0ULL;"
				: "return " + fragment.is_accepting_name + "(state) ? static_cast<" + fragment.value_keyword + ">(total + 1ULL) : static_cast<" + fragment.value_keyword + ">(0);");
		}

		writer.close_block();
		writer.line();
	}

	void cpp_emitter::emit_decode(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();
		const std::string try_decode_name = fragment.name("TryDecode");

		writer.line("/// The string a value stands for.");
		writer.line("///");
		writer.line("/// @param value The encoded value, from 1 to " + fragment.max_encoded_value_name + ".");
		writer.line("/// @returns The string, which is in canonical form.");
		writer.line("/// @throws std::out_of_range The value is not one this naxp produces.");
		writer.line("inline std::string " + fragment.decode_name + "(" + fragment.value_keyword + " value)");
		writer.open_block();
		writer.line("if (value < " + fragment.value_one + " || value > " + fragment.max_encoded_value_name + ")");
		writer.open_block();
		writer.line("throw std::out_of_range(\"This naxp encodes the values 1 to " + fragment.max_encoded_value_digits() + ".\");");
		writer.close_block();
		writer.line();
		writer.line("char buffer[" + fragment.buffer_size() + "] = {};");
		writer.line("int length = " + fragment.decode_core_name + "(" + fragment.decode_core_argument + ", buffer);");
		writer.line();
		writer.line("return std::string(buffer, buffer + length);");
		writer.close_block();
		writer.line();

		writer.line("/// Tries to find the string a value stands for.");
		writer.line("///");
		writer.line("/// @param value The encoded value.");
		writer.line("/// @param text Where the string goes, which is in canonical form. Untouched where the value is not one this naxp produces.");
		writer.line("/// @returns Whether the value is one this naxp produces.");
		writer.line("inline bool " + try_decode_name + "(" + fragment.value_keyword + " value, std::string& text)");
		writer.open_block();
		writer.line("if (value < " + fragment.value_one + " || value > " + fragment.max_encoded_value_name + ") { return false; }");
		writer.line();
		writer.line("char buffer[" + fragment.buffer_size() + "] = {};");
		writer.line("int length = " + fragment.decode_core_name + "(" + fragment.decode_core_argument + ", buffer);");
		writer.line();
		writer.line("text.assign(buffer, buffer + length);");
		writer.line();
		writer.line("return true;");
		writer.close_block();
		writer.line();
	}
}
