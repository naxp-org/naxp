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

	std::string cpp_emitter::text_parameters() const
	{
		return "std::string_view text";
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

		writer.line("// Needs <cstdint>, <optional>, <stdexcept>, <string> and <string_view>. Everything is inline,");
		writer.line("// so the fragment can sit in a header; give each naxp its own prefix where several share a");
		writer.line("// program.");
		writer.line();
		this->comment(writer, "The naxp this code was generated from.");
		writer.line("inline constexpr std::string_view " + fragment.pattern_name + " = " + pattern_literal(fragment.pattern()) + ";");
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
		this->emit_canonical_form(fragment);
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

		writer.line("/// The encoded value of text.");
		writer.line("///");
		writer.line("/// @param text The text to encode.");
		writer.line("/// @returns The encoded value, from 1 to " + fragment.max_encoded_value_name + ", or zero where the text is invalid.");
		writer.line("inline " + fragment.value_keyword + " " + fragment.encode_name + "(std::string_view text)");
		writer.open_block();

		if (fragment.canonicalises())
		{
			writer.line("char canonical[" + fragment.buffer_size() + "] = {};");
			writer.line("int length = " + fragment.canonicalise_name + "(text, canonical);");
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

		writer.line("/// Tries to write the string a value stands for, which is in canonical form, with no terminator.");
		writer.line("///");
		writer.line("/// @param value The encoded value.");
		writer.line("/// @param destination Where the string is written. " + fragment.max_length_name + " characters always suffice.");
		writer.line("/// @param capacity How many characters there is room for.");
		writer.line("/// @param length How many characters were written, or zero where none were.");
		writer.line("/// @returns False where the value is not one this naxp produces, or the destination is too short, in which case nothing is written.");
		writer.line("inline bool " + try_decode_name + "(" + fragment.value_keyword + " value, char* destination, std::size_t capacity, std::size_t& length)");
		writer.open_block();
		writer.line("if (value < " + fragment.value_one + " || value > " + fragment.max_encoded_value_name + ")");
		writer.open_block();
		writer.line("length = 0;");
		writer.line("return false;");
		writer.close_block();
		writer.line();
		writer.line("char buffer[" + fragment.buffer_size() + "] = {};");
		writer.line("int written = " + fragment.decode_core_name + "(" + fragment.decode_core_argument + ", buffer);");
		writer.line();
		emit_copy_out(writer, false);
	}

	void cpp_emitter::emit_canonical_form(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();
		const std::string try_name = fragment.name("TryCanonicalForm");

		writer.line("/// The canonical form of text, which is the text decoding its encoded value gives back.");
		writer.line("///");
		writer.line("/// @param text The text.");
		writer.line("/// @returns The canonical form, or no value where the text is invalid.");
		writer.line("inline std::optional<std::string> " + fragment.canonical_form_name + "(std::string_view text)");
		writer.open_block();
		writer.line("char buffer[" + fragment.buffer_size() + "] = {};");
		writer.line("int length = " + fragment.canonicalise_name + "(text, buffer);");
		writer.line();
		writer.line("if (length < 0) { return std::nullopt; }");
		writer.line();
		writer.line("return std::string(buffer, buffer + length);");
		writer.close_block();
		writer.line();

		writer.line("/// Tries to find the canonical form of text.");
		writer.line("///");
		writer.line("/// @param text The text.");
		writer.line("/// @param canonical_form Where the canonical form goes. Untouched where the text is invalid.");
		writer.line("/// @returns Whether the naxp accepts the text.");
		writer.line("inline bool " + try_name + "(std::string_view text, std::string& canonical_form)");
		writer.open_block();
		writer.line("char buffer[" + fragment.buffer_size() + "] = {};");
		writer.line("int length = " + fragment.canonicalise_name + "(text, buffer);");
		writer.line();
		writer.line("if (length < 0) { return false; }");
		writer.line();
		writer.line("canonical_form.assign(buffer, buffer + length);");
		writer.line();
		writer.line("return true;");
		writer.close_block();
		writer.line();

		writer.line("/// Tries to write the canonical form of text, with no terminator.");
		writer.line("///");
		writer.line("/// @param text The text.");
		writer.line("/// @param destination Where the canonical form is written. " + fragment.max_length_name + " characters always suffice.");
		writer.line("/// @param capacity How many characters there is room for.");
		writer.line("/// @param length How many characters were written, or zero where none were.");
		writer.line("/// @returns False where the text is invalid, or the destination is too short, in which case nothing is written.");
		writer.line("inline bool " + try_name + "(std::string_view text, char* destination, std::size_t capacity, std::size_t& length)");
		writer.open_block();
		writer.line("char buffer[" + fragment.buffer_size() + "] = {};");
		writer.line("int written = " + fragment.canonicalise_name + "(text, buffer);");
		writer.line();
		emit_copy_out(writer, true);
	}

	void cpp_emitter::emit_copy_out(code_writer& writer, bool may_fail)
	{
		writer.line(may_fail
			? "if (written < 0 || static_cast<std::size_t>(written) > capacity)"
			: "if (static_cast<std::size_t>(written) > capacity)");
		writer.open_block();
		writer.line("length = 0;");
		writer.line("return false;");
		writer.close_block();
		writer.line();
		writer.line("std::char_traits<char>::copy(destination, buffer, static_cast<std::size_t>(written));");
		writer.line("length = static_cast<std::size_t>(written);");
		writer.line();
		writer.line("return true;");
		writer.close_block();
		writer.line();
	}

	void cpp_emitter::emit_canonicalise(fragment& fragment) const
	{
		code_writer& writer = fragment.writer();

		fragment.open_canonicalise();

		if (!fragment.canonicalises())
		{
			// Where nothing is unified an accepted string is its own canonical form, and being
			// accepted it fits the buffer.
			writer.line("if (!" + fragment.accepts_name + "(text)) { return -1; }");
			writer.line();
			writer.line("text.copy(canonical, text.size());");
			writer.line();
			writer.line("return static_cast<int>(text.size());");
			writer.close_block();
			writer.line();

			return;
		}

		fragment.declare_register("{}");
		writer.line("int length = 0;");
		writer.line("int state = 0;");
		writer.line();
		writer.line("for (char c : text)");
		writer.open_block();
		fragment.keep_character("c");
		writer.line("state = " + fragment.canonical_step_name + "(state, static_cast<unsigned char>(c), " + fragment.step_arguments("length") + ");");
		writer.line();
		writer.line("if (state < 0) { return -1; }");
		writer.close_block();
		writer.line();
		writer.line("return " + fragment.finish_canonical_name + "(state, " + fragment.finish_arguments() + ");");
		writer.close_block();
		writer.line();
	}

	std::string cpp_emitter::pattern_literal(const std::string& text)
	{
		for (const char c : text)
		{
			if (c < ' ' || c > '~')
			{
				return string_literal(text);
			}
		}

		return text.find(")\"") != std::string::npos ? string_literal(text) : "R\"(" + text + ")\"";
	}
}
