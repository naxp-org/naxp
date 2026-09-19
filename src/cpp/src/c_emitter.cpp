// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "c_emitter.hpp"

#include "c_family_emitter.hpp"
#include "code_writer.hpp"
#include "emitter.hpp"

#include <optional>
#include <string>

namespace logmu::detail
{
	const c_emitter& c_emitter::instance()
	{
		static const c_emitter shared;

		return shared;
	}

	// The spelling of C

	std::string c_emitter::step_linkage() const
	{
		return "static";
	}

	std::string c_emitter::type_prefix() const
	{
		return "";
	}

	std::optional<std::string> c_emitter::digit_separator() const
	{
		return std::nullopt;
	}

	std::string c_emitter::pointer(const std::string& type, const std::string& name) const
	{
		return type + " *" + name;
	}

	std::string c_emitter::by_reference(const std::string& type, const std::string& name) const
	{
		return type + " *" + name;
	}

	std::string c_emitter::dereference(const std::string& name) const
	{
		return "*" + name;
	}

	std::string c_emitter::increment(const std::string& name) const
	{
		return "(*" + name + ")++";
	}

	std::string c_emitter::address_of(const std::string& name) const
	{
		return "&" + name;
	}

	std::string c_emitter::cast(const std::string& type, const std::string& expression) const
	{
		return is_identifier(expression) ? "(" + type + ")" + expression : "(" + type + ")(" + expression + ")";
	}

	void c_emitter::comment(code_writer& writer, const std::string& text) const
	{
		writer.line("/* " + text + " */");
	}

	// The header

	void c_emitter::emit_header(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();

		this->comment(writer, "Needs <stdbool.h>, <stddef.h>, <stdint.h> and <string.h>.");
		writer.line();
		this->comment(writer, "The largest encoded value this naxp produces, which is also how many it has.");
		writer.line("static const " + fragment.value_keyword + " " + fragment.max_encoded_value_name + " = " + fragment.max_encoded_value_literal + ";");
		writer.line();
		this->comment(writer, "The length of the longest string this naxp can decode a value to.");
		writer.line("enum { " + fragment.max_length_name + " = " + decimal(fragment.max_length()) + " };");
		writer.line();
	}

	// The public functions

	void c_emitter::emit_publics(fragment& fragment) const
	{
		this->emit_accepts(fragment);
		this->emit_encode(fragment);
		this->emit_decode(fragment);
	}

	void c_emitter::emit_accepts(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();
		const std::string cstr_name = fragment.name("AcceptsCstr");

		this->comment(writer, "Whether this naxp accepts text. A byte outside ASCII is never accepted.");
		writer.line("static inline bool " + fragment.accepts_name + "(const char *text, size_t text_length)");
		writer.open_block();
		writer.line("int state = 0;");
		writer.line();
		writer.line("for (size_t i = 0; i < text_length; ++i)");
		writer.open_block();
		writer.line("state = " + fragment.accept_step_name + "(state, (unsigned char)text[i]);");
		writer.line();
		writer.line("if (state < 0) { return false; }");
		writer.close_block();
		writer.line();
		writer.line("return " + fragment.is_accepting_name + "(state);");
		writer.close_block();
		writer.line();

		this->comment(writer, "Whether this naxp accepts a NUL-terminated string.");
		writer.line("static inline bool " + cstr_name + "(const char *text)");
		writer.open_block();
		writer.line("return " + fragment.accepts_name + "(text, strlen(text));");
		writer.close_block();
		writer.line();
	}

	void c_emitter::emit_encode(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();
		const std::string cstr_name = fragment.name("EncodeCstr");
		const std::string& held = fragment.held_name;

		this->comment(writer, "The encoded value of text: from 1 to " + fragment.max_encoded_value_name + ", or zero where the text is invalid.");
		writer.line("static inline " + fragment.value_keyword + " " + fragment.encode_name + "(const char *text, size_t text_length)");
		writer.open_block();

		if (fragment.canonicalises())
		{
			writer.line("char canonical[" + fragment.buffer_size() + "] = { 0 };");

			if (fragment.needs_register())
			{
				// The characters read, oldest first, so a reference of depth d is the one at
				// register_depth - 1 - d. Shifting a buffer this small beats indexing a ring, and
				// it starts zeroed so that an early shift reads nothing undefined.
				writer.line("char " + held + "[" + decimal(fragment.register_depth()) + "] = { 0 };");
			}

			writer.line("int length = 0;");
			writer.line("int state = 0;");
			writer.line();
			writer.line("for (size_t i = 0; i < text_length; ++i)");
			writer.open_block();

			if (fragment.needs_register())
			{
				const std::string top = decimal(fragment.register_depth() - 1);

				if (fragment.register_depth() > 1)
				{
					writer.line("for (int h = 0; h < " + top + "; ++h) { " + held + "[h] = " + held + "[h + 1]; }");
				}

				writer.line(held + "[" + top + "] = text[i];");
			}

			writer.line("state = " + fragment.canonical_step_name + "(state, (unsigned char)text[i], " + fragment.step_arguments("&length") + ");");
			writer.line();
			writer.line("if (state < 0) { return " + fragment.value_zero + "; }");
			writer.close_block();
			writer.line();
			writer.line("length = " + fragment.finish_canonical_name + "(state, " + fragment.finish_arguments() + ");");
			writer.line();
			writer.line(fragment.value_is_widest
				? "return length < 0 ? 0ULL : " + fragment.rank_name + "(canonical, length);"
				: "return length < 0 ? (" + fragment.value_keyword + ")0 : (" + fragment.value_keyword + ")" + fragment.rank_name + "(canonical, length);");
		}
		else
		{
			writer.line("int state = 0;");
			writer.line("uint64_t total = 0ULL;");
			writer.line();
			writer.line("for (size_t i = 0; i < text_length; ++i)");
			writer.open_block();
			writer.line("state = " + fragment.encode_step_name + "(state, (unsigned char)text[i], &total);");
			writer.line();
			writer.line("if (state < 0) { return " + fragment.value_zero + "; }");
			writer.close_block();
			writer.line();
			writer.line(fragment.value_is_widest
				? "return " + fragment.is_accepting_name + "(state) ? total + 1ULL : 0ULL;"
				: "return " + fragment.is_accepting_name + "(state) ? (" + fragment.value_keyword + ")(total + 1ULL) : (" + fragment.value_keyword + ")0;");
		}

		writer.close_block();
		writer.line();

		this->comment(writer, "The encoded value of a NUL-terminated string: from 1 to " + fragment.max_encoded_value_name + ", or zero where the string is invalid.");
		writer.line("static inline " + fragment.value_keyword + " " + cstr_name + "(const char *text)");
		writer.open_block();
		writer.line("return " + fragment.encode_name + "(text, strlen(text));");
		writer.close_block();
		writer.line();
	}

	void c_emitter::emit_decode(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();
		const std::string cstr_name = fragment.name("DecodeCstr");

		writer.line("/*");
		writer.line("   Writes the string a value stands for, which is in canonical form, with no terminator.");
		writer.line("   " + fragment.max_length_name + " bytes always suffice. length, which may be NULL, receives how many");
		writer.line("   were written, or zero where none were.");
		writer.line();
		writer.line("   Returns false where the value is not one this naxp produces, or the destination is too");
		writer.line("   short, in which case nothing is written.");
		writer.line("*/");
		writer.line("static inline bool " + fragment.decode_name + "(" + fragment.value_keyword + " value, char *destination, size_t capacity, size_t *length)");
		writer.open_block();
		writer.line("char buffer[" + fragment.buffer_size() + "] = { 0 };");
		writer.line("int written;");
		writer.line();
		writer.line("if (value < " + fragment.value_one + " || value > " + fragment.max_encoded_value_name + ")");
		writer.open_block();
		writer.line("if (length != NULL) { *length = 0; }");
		writer.line();
		writer.line("return false;");
		writer.close_block();
		writer.line();
		writer.line("written = " + fragment.decode_core_name + "(" + fragment.decode_core_argument + ", buffer);");
		writer.line();
		writer.line("if ((size_t)written > capacity)");
		writer.open_block();
		writer.line("if (length != NULL) { *length = 0; }");
		writer.line();
		writer.line("return false;");
		writer.close_block();
		writer.line();
		writer.line("memcpy(destination, buffer, (size_t)written);");
		writer.line();
		writer.line("if (length != NULL) { *length = (size_t)written; }");
		writer.line();
		writer.line("return true;");
		writer.close_block();
		writer.line();

		writer.line("/*");
		writer.line("   Writes the string a value stands for, which is in canonical form, as a NUL-terminated");
		writer.line("   string. " + fragment.max_length_name + " + 1 bytes always suffice.");
		writer.line();
		writer.line("   Returns false where the value is not one this naxp produces, or the destination is too");
		writer.line("   short, in which case nothing is written.");
		writer.line("*/");
		writer.line("static inline bool " + cstr_name + "(" + fragment.value_keyword + " value, char *destination, size_t capacity)");
		writer.open_block();
		writer.line("size_t length;");
		writer.line();
		writer.line("if (capacity == 0 || !" + fragment.decode_name + "(value, destination, capacity - 1, &length)) { return false; }");
		writer.line();
		writer.line("destination[length] = '\\0';");
		writer.line();
		writer.line("return true;");
		writer.close_block();
		writer.line();
	}
}
