// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "csharp_emitter.hpp"

#include "code_writer.hpp"
#include "emitter.hpp"
#include "tx.hpp"
#include "tx_machine.hpp"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace logmu::detail
{
	// The System types the fragment uses, fully qualified.
	namespace
	{
		const std::string read_only_span_of_char = "global::System.ReadOnlySpan<char>";
		const std::string read_only_span_of_byte = "global::System.ReadOnlySpan<byte>";
		const std::string span_of_char = "global::System.Span<char>";
		const std::string span_of_byte = "global::System.Span<byte>";
		const std::string argument_out_of_range_exception = "global::System.ArgumentOutOfRangeException";

	}

	/// One emission call's state: the context and the generated names built from the prefix.
	/// Per call so the shared instance stays stateless.
	class csharp_emitter::fragment
	{
	public:
		/// The name the generated code keeps the characters it has read under.
		static const std::string held_name;

		fragment(const csharp_emitter& emitter, emit_context& context);

		void emit();

	private:
		code_writer& writer()
		{
			return this->context.writer;
		}

		const std::vector<state_model>& accepted_states() const
		{
			return this->context.accepted_states;
		}

		const std::vector<state_model>& canonical_states() const
		{
			return this->context.canonical_states;
		}

		const std::vector<tx_state_model>& transducer_states() const
		{
			return this->context.transducer_states;
		}

		bool canonicalises() const
		{
			return this->context.canonicalises;
		}

		int max_length() const
		{
			return this->context.max_length;
		}

		int register_depth() const
		{
			return this->context.register_depth;
		}

		bool needs_register() const
		{
			return this->context.needs_register();
		}

		void emit_constants();
		void emit_accepts(bool bytes);
		void emit_encode(bool bytes);
		void emit_decode_publics();
		void emit_steppers();
		void emit_accept_case(int id);
		void emit_encode_case(int id);
		void emit_decode_case(int id);
		void emit_decode_arc(const arc_model& arc);
		void emit_canonical_case(int id);
		void emit_finish_case(int id);
		std::string step_arguments() const;
		std::string finish_arguments() const;
		std::vector<std::string> output_expressions(const std::string& output, bool for_finish) const;
		void emit_accepting_predicate(const std::string& name, const std::vector<state_model>& states);

		const csharp_emitter& emitter;
		emit_context& context;

		// The generated names, each the prefix plus the bare member name.
		std::string max_encoded_value_name;
		std::string max_length_name;
		std::string accepts_name;
		std::string encode_name;
		std::string decode_name;
		std::string decode_to_bytes_name;
		std::string try_decode_name;
		std::string rank_name;
		std::string decode_core_name;
		std::string accept_step_name;
		std::string is_accepting_name;
		std::string encode_step_name;
		std::string is_canonical_accepting_name;
		std::string decode_step_name;
		std::string canonical_step_name;
		std::string finish_canonical_name;

		// The C# spelling of the chosen value type. The steppers work in ulong throughout
		// whatever the choice, because C# promotes narrow operands to int anyway and casts
		// through the switch bodies would be pure noise; only the public boundary changes.
		std::string value_keyword;
		std::string max_encoded_value_value;
		std::string value_zero;
		std::string value_one;
		std::string decode_core_argument;
		bool value_is_widest;
	};

	const std::string csharp_emitter::fragment::held_name = "held";

	const csharp_emitter& csharp_emitter::instance()
	{
		static const csharp_emitter shared;

		return shared;
	}

	void csharp_emitter::emit_fragment(emit_context& context) const
	{
		fragment(*this, context).emit();
	}

	// The shape of C#

	void csharp_emitter::open_function(code_writer& writer, std::string_view name, std::string_view parameters) const
	{
		writer.line("static int " + std::string(name) + "(" + std::string(parameters) + ")");
		writer.open_block();
	}

	void csharp_emitter::close_function(code_writer& writer) const
	{
		writer.close_block();
	}

	void csharp_emitter::open_dispatch(code_writer& writer) const
	{
		writer.line("switch (state)");
		writer.open_block();
	}

	void csharp_emitter::close_dispatch(code_writer& writer, std::string_view result) const
	{
		writer.close_block();
		writer.line();
		writer.line("return " + std::string(result) + ";");
	}

	void csharp_emitter::write_return(code_writer& writer, std::string_view expression) const
	{
		writer.line("return " + std::string(expression) + ";");
	}

	void csharp_emitter::write_guarded_return(code_writer& writer, std::string_view condition, std::string_view expression) const
	{
		writer.line("if (" + std::string(condition) + ") { return " + std::string(expression) + "; }");
	}

	std::string csharp_emitter::equals_character(char c) const
	{
		return "c == " + char_literal(c);
	}

	std::string csharp_emitter::within_run(char first, char last) const
	{
		return "c >= " + char_literal(first) + " && c <= " + char_literal(last);
	}

	// The fragment

	csharp_emitter::fragment::fragment(const csharp_emitter& emitter, emit_context& context)
		: emitter(emitter)
		, context(context)
	{
		std::string value_suffix;

		switch (context.value_type)
		{
			case naxp_value_type::int8: this->value_keyword = "sbyte"; value_suffix = ""; break;
			case naxp_value_type::uint8: this->value_keyword = "byte"; value_suffix = ""; break;
			case naxp_value_type::int16: this->value_keyword = "short"; value_suffix = ""; break;
			case naxp_value_type::uint16: this->value_keyword = "ushort"; value_suffix = ""; break;
			case naxp_value_type::int32: this->value_keyword = "int"; value_suffix = ""; break;
			case naxp_value_type::uint32: this->value_keyword = "uint"; value_suffix = "U"; break;
			case naxp_value_type::int64: this->value_keyword = "long"; value_suffix = "L"; break;
			case naxp_value_type::uint64: this->value_keyword = "ulong"; value_suffix = "UL"; break;
			default: throw std::logic_error("Unhandled value type.");
		}

		this->value_is_widest = context.value_type == naxp_value_type::uint64;
		this->max_encoded_value_value = emitter.grouped(context.compiled.max_encoded_value()) + value_suffix;
		this->value_zero = this->value_is_widest ? "0UL" : "0";
		this->value_one = this->value_is_widest ? "1UL" : "1";

		// DecodeCore takes ulong. Only ulong reaches it unconverted; every other type needs
		// the cast, which the range check just made safe.
		this->decode_core_argument = this->value_is_widest ? "value" : "(ulong)value";

		const std::string& prefix = context.prefix;
		this->max_encoded_value_name = prefix + "MaxEncodedValue";
		this->max_length_name = prefix + "MaxLength";
		this->accepts_name = prefix + "Accepts";
		this->encode_name = prefix + "Encode";
		this->decode_name = prefix + "Decode";
		this->decode_to_bytes_name = prefix + "DecodeToBytes";
		this->try_decode_name = prefix + "TryDecode";
		this->rank_name = prefix + "Rank";
		this->decode_core_name = prefix + "DecodeCore";
		this->accept_step_name = prefix + "AcceptStep";
		this->is_accepting_name = prefix + "IsAccepting";
		this->encode_step_name = prefix + "EncodeStep";
		this->is_canonical_accepting_name = prefix + "IsCanonicalAccepting";
		this->decode_step_name = prefix + "DecodeStep";
		this->canonical_step_name = prefix + "CanonicalStep";
		this->finish_canonical_name = prefix + "FinishCanonical";
	}

	void csharp_emitter::fragment::emit()
	{
		this->emit_constants();
		this->emit_accepts(false);
		this->emit_accepts(true);
		this->emit_encode(false);
		this->emit_encode(true);
		this->emit_decode_publics();
		this->emit_steppers();
	}

	void csharp_emitter::fragment::emit_constants()
	{
		code_writer& out = this->writer();

		out.line("/// <summary>The largest encoded value this naxp produces, which is also how many it has.</summary>");
		out.line("public const " + this->value_keyword + " " + this->max_encoded_value_name + " = " + this->max_encoded_value_value + ";");
		out.line();
		out.line("/// <summary>The length of the longest string this naxp can decode a value to.</summary>");
		out.line("public const int " + this->max_length_name + " = " + decimal(this->max_length()) + ";");
		out.line();
	}

	// Public methods

	void csharp_emitter::fragment::emit_accepts(bool bytes)
	{
		code_writer& out = this->writer();

		if (bytes)
		{
			out.line("/// <summary>Whether this naxp accepts the specified ASCII text. A byte outside ASCII is never accepted.</summary>");
			out.line("/// <param name=\"text\">The ASCII text to test.</param>");
			out.line("/// <returns>Whether the naxp accepts it.</returns>");
			out.line("public static bool " + this->accepts_name + "(" + read_only_span_of_byte + " text)");
		}
		else
		{
			out.line("/// <summary>Whether this naxp accepts the specified string.</summary>");
			out.line("/// <param name=\"text\">The string to test.</param>");
			out.line("/// <returns>Whether the naxp accepts it.</returns>");
			out.line("public static bool " + this->accepts_name + "(" + read_only_span_of_char + " text)");
		}

		out.open_block();
		out.line("int state = 0;");
		out.line();
		out.line(bytes ? "foreach (byte b in text)" : "foreach (char c in text)");
		out.open_block();
		out.line(bytes
			? "state = " + this->accept_step_name + "(state, (char)b);"
			: "state = " + this->accept_step_name + "(state, c);");
		out.line();
		out.line("if (state < 0) { return false; }");
		out.close_block();
		out.line();
		out.line("return " + this->is_accepting_name + "(state);");
		out.close_block();
		out.line();
	}

	void csharp_emitter::fragment::emit_encode(bool bytes)
	{
		code_writer& out = this->writer();

		if (bytes)
		{
			out.line("/// <summary>The encoded value of ASCII text.</summary>");
			out.line("/// <param name=\"text\">The ASCII text to encode.</param>");
			out.line("/// <returns>The encoded value, from 1 to <see cref=\"" + this->max_encoded_value_name + "\"/>, or zero where the text is invalid.</returns>");
			out.line("public static " + this->value_keyword + " " + this->encode_name + "(" + read_only_span_of_byte + " text)");
		}
		else
		{
			out.line("/// <summary>The encoded value of a string.</summary>");
			out.line("/// <param name=\"text\">The string to encode.</param>");
			out.line("/// <returns>The encoded value, from 1 to <see cref=\"" + this->max_encoded_value_name + "\"/>, or zero where the string is invalid.</returns>");
			out.line("public static " + this->value_keyword + " " + this->encode_name + "(" + read_only_span_of_char + " text)");
		}

		out.open_block();

		if (!this->canonicalises())
		{
			out.line("int state = 0;");
			out.line("ulong total = 0UL;");
			out.line();
			out.line(bytes ? "foreach (byte b in text)" : "foreach (char c in text)");
			out.open_block();
			out.line(bytes
				? "state = " + this->encode_step_name + "(state, (char)b, ref total);"
				: "state = " + this->encode_step_name + "(state, c, ref total);");
			out.line();
			out.line("if (state < 0) { return " + this->value_zero + "; }");
			out.close_block();
			out.line();
			out.line(this->value_is_widest
				? "return " + this->is_accepting_name + "(state) ? total + 1UL : 0UL;"
				: "return " + this->is_accepting_name + "(state) ? (" + this->value_keyword + ")(total + 1UL) : (" + this->value_keyword + ")0;");
		}
		else
		{
			out.line(span_of_char + " canonical = stackalloc char[" + this->max_length_name + "];");

			if (this->needs_register())
			{
				// The characters read, oldest first, so a reference of depth d is the one at
				// register_depth - 1 - d. Shifting a buffer this small beats indexing a ring.
				out.line(span_of_char + " " + std::string(held_name) + " = stackalloc char[" + decimal(this->register_depth()) + "];");
			}

			out.line("int length = 0;");
			out.line("int state = 0;");
			out.line();
			out.line(bytes ? "foreach (byte b in text)" : "foreach (char c in text)");
			out.open_block();

			if (this->needs_register())
			{
				const std::string top = decimal(this->register_depth() - 1);
				const std::string held(held_name);

				if (this->register_depth() > 1)
				{
					out.line("for (int h = 0; h < " + top + "; ++h) { " + held + "[h] = " + held + "[h + 1]; }");
				}

				out.line(bytes ? held + "[" + top + "] = (char)b;" : held + "[" + top + "] = c;");
			}

			out.line(bytes
				? "state = " + this->canonical_step_name + "(state, (char)b, " + this->step_arguments() + ");"
				: "state = " + this->canonical_step_name + "(state, c, " + this->step_arguments() + ");");
			out.line();
			out.line("if (state < 0) { return " + this->value_zero + "; }");
			out.close_block();
			out.line();
			out.line("length = " + this->finish_canonical_name + "(state, " + this->finish_arguments() + ");");
			out.line();
			out.line(this->value_is_widest
				? "return length < 0 ? 0UL : " + this->rank_name + "(canonical.Slice(0, length));"
				: "return length < 0 ? (" + this->value_keyword + ")0 : (" + this->value_keyword + ")" + this->rank_name + "(canonical.Slice(0, length));");
		}

		out.close_block();
		out.line();
	}

	void csharp_emitter::fragment::emit_decode_publics()
	{
		code_writer& out = this->writer();
		const std::string count_digits = decimal(this->context.compiled.max_encoded_value());
		const std::string throw_statement =
			"throw new " + std::string(argument_out_of_range_exception) + "(nameof(value), value, \"This naxp encodes the values 1 to " + count_digits + ".\");";
		const std::string range_check = "if (value < " + this->value_one + " || value > " + this->max_encoded_value_name + ")";

		out.line("/// <summary>The string a value stands for.</summary>");
		out.line("/// <param name=\"value\">The encoded value, from 1 to <see cref=\"" + this->max_encoded_value_name + "\"/>.</param>");
		out.line("/// <returns>The string, which is in canonical form.</returns>");
		out.line("/// <exception cref=\"" + std::string(argument_out_of_range_exception) + "\">The value is not one this naxp produces.</exception>");
		out.line("public static string " + this->decode_name + "(" + this->value_keyword + " value)");
		out.open_block();
		out.line(range_check);
		out.open_block();
		out.line(throw_statement);
		out.close_block();
		out.line();
		out.line(span_of_char + " destination = stackalloc char[" + this->max_length_name + "];");
		out.line();
		out.line("return destination.Slice(0, " + this->decode_core_name + "(" + this->decode_core_argument + ", destination)).ToString();");
		out.close_block();
		out.line();

		out.line("/// <summary>The string a value stands for, as ASCII bytes.</summary>");
		out.line("/// <param name=\"value\">The encoded value, from 1 to <see cref=\"" + this->max_encoded_value_name + "\"/>.</param>");
		out.line("/// <returns>The bytes, which spell the string in canonical form.</returns>");
		out.line("/// <exception cref=\"" + std::string(argument_out_of_range_exception) + "\">The value is not one this naxp produces.</exception>");
		out.line("public static byte[] " + this->decode_to_bytes_name + "(" + this->value_keyword + " value)");
		out.open_block();
		out.line(range_check);
		out.open_block();
		out.line(throw_statement);
		out.close_block();
		out.line();
		out.line(span_of_char + " buffer = stackalloc char[" + this->max_length_name + "];");
		out.line("int length = " + this->decode_core_name + "(" + this->decode_core_argument + ", buffer);");
		out.line("var result = new byte[length];");
		out.line();
		out.line("for (int i = 0; i < length; ++i) { result[i] = (byte)buffer[i]; }");
		out.line();
		out.line("return result;");
		out.close_block();
		out.line();

		out.line("/// <summary>Tries to write the string a value stands for.</summary>");
		out.line("/// <param name=\"value\">The encoded value.</param>");
		out.line("/// <param name=\"destination\">Where the string is written.</param>");
		out.line("/// <param name=\"charsWritten\">How many characters were written, or zero where none were.</param>");
		out.line("/// <returns>False where the value is not one this naxp produces, or the destination is too short.</returns>");
		out.line("public static bool " + this->try_decode_name + "(" + this->value_keyword + " value, " + span_of_char + " destination, out int charsWritten)");
		out.open_block();
		out.line(range_check);
		out.open_block();
		out.line("charsWritten = 0;");
		out.line("return false;");
		out.close_block();
		out.line();
		out.line("if (destination.Length >= " + this->max_length_name + ")");
		out.open_block();
		out.line("charsWritten = " + this->decode_core_name + "(" + this->decode_core_argument + ", destination);");
		out.line("return true;");
		out.close_block();
		out.line();
		out.line(span_of_char + " buffer = stackalloc char[" + this->max_length_name + "];");
		out.line("int length = " + this->decode_core_name + "(" + this->decode_core_argument + ", buffer);");
		out.line();
		out.line("if (length > destination.Length)");
		out.open_block();
		out.line("charsWritten = 0;");
		out.line("return false;");
		out.close_block();
		out.line();
		out.line("buffer.Slice(0, length).CopyTo(destination);");
		out.line("charsWritten = length;");
		out.line("return true;");
		out.close_block();
		out.line();

		out.line("/// <summary>Tries to write the string a value stands for, as ASCII bytes.</summary>");
		out.line("/// <param name=\"value\">The encoded value.</param>");
		out.line("/// <param name=\"destination\">Where the bytes are written.</param>");
		out.line("/// <param name=\"bytesWritten\">How many bytes were written, or zero where none were.</param>");
		out.line("/// <returns>False where the value is not one this naxp produces, or the destination is too short.</returns>");
		out.line("public static bool " + this->try_decode_name + "(" + this->value_keyword + " value, " + span_of_byte + " destination, out int bytesWritten)");
		out.open_block();
		out.line(range_check);
		out.open_block();
		out.line("bytesWritten = 0;");
		out.line("return false;");
		out.close_block();
		out.line();
		out.line(span_of_char + " buffer = stackalloc char[" + this->max_length_name + "];");
		out.line("int length = " + this->decode_core_name + "(" + this->decode_core_argument + ", buffer);");
		out.line();
		out.line("if (length > destination.Length)");
		out.open_block();
		out.line("bytesWritten = 0;");
		out.line("return false;");
		out.close_block();
		out.line();
		out.line("for (int i = 0; i < length; ++i) { destination[i] = (byte)buffer[i]; }");
		out.line();
		out.line("bytesWritten = length;");
		out.line("return true;");
		out.close_block();
		out.line();
	}

	// Private methods

	void csharp_emitter::fragment::emit_steppers()
	{
		code_writer& out = this->writer();

		if (this->canonicalises())
		{
			out.line("/// <summary>The rank of a canonical string within the canonical language, or zero where it is not in it.</summary>");
			out.line("static ulong " + this->rank_name + "(" + read_only_span_of_char + " canonical)");
			out.open_block();
			out.line("int state = 0;");
			out.line("ulong total = 0UL;");
			out.line();
			out.line("foreach (char c in canonical)");
			out.open_block();
			out.line("state = " + this->encode_step_name + "(state, c, ref total);");
			out.line();
			out.line("if (state < 0) { return 0UL; }");
			out.close_block();
			out.line();
			out.line("return " + this->is_canonical_accepting_name + "(state) ? total + 1UL : 0UL;");
			out.close_block();
			out.line();
		}

		out.line("/// <summary>Writes the string of a value that was already checked against <see cref=\"" + this->max_encoded_value_name + "\"/>, and returns its length.</summary>");
		out.line("static int " + this->decode_core_name + "(ulong value, " + span_of_char + " destination)");
		out.open_block();
		out.line("ulong remaining = value;");
		out.line("int state = 0;");
		out.line("int length = 0;");
		out.line();
		out.line("while (state >= 0)");
		out.open_block();
		out.line("state = " + this->decode_step_name + "(state, ref remaining, destination, ref length);");
		out.close_block();
		out.line();
		out.line("return length;");
		out.close_block();
		out.line();

		out.line("/// <summary>The acceptor's transition: the next state, or -1 where the character fits nothing.</summary>");
		this->emitter.emit_step_functions(out,
			this->accept_step_name,
			"int state, char c",
			"state, c",
			static_cast<int>(this->accepted_states().size()),
			[this](int id) { this->emit_accept_case(id); });
		out.line();

		this->emit_accepting_predicate(this->is_accepting_name, this->accepted_states());
		out.line();

		out.line("/// <summary>The canonical machine's transition, accumulating the values skipped: the next state, or -1.</summary>");
		this->emitter.emit_step_functions(out,
			this->encode_step_name,
			"int state, char c, ref ulong total",
			"state, c, ref total",
			static_cast<int>(this->canonical_states().size()),
			[this](int id) { this->emit_encode_case(id); });
		out.line();

		if (this->canonicalises())
		{
			this->emit_accepting_predicate(this->is_canonical_accepting_name, this->canonical_states());
			out.line();
		}

		out.line("/// <summary>One step of decoding: appends at most one character and returns the next state, or -1 when the string is complete.</summary>");
		this->emitter.emit_step_functions(out,
			this->decode_step_name,
			"int state, ref ulong remaining, " + std::string(span_of_char) + " destination, ref int length",
			"state, ref remaining, destination, ref length",
			static_cast<int>(this->canonical_states().size()),
			[this](int id) { this->emit_decode_case(id); },
			"ulong index;",
			[this](int id) { return needs_index(this->canonical_states()[static_cast<std::size_t>(id)]); });

		if (this->canonicalises())
		{
			out.line();
			out.line("/// <summary>The canonicalising transition, appending what reading the character emits: the next state, or -1.</summary>");
			const std::string held_parameter = this->needs_register() ? std::string(span_of_char) + " " + std::string(held_name) + ", " : std::string();

			this->emitter.emit_step_functions(out,
				this->canonical_step_name,
				"int state, char c, " + held_parameter + std::string(span_of_char) + " canonical, ref int length",
				"state, c, " + this->step_arguments(),
				static_cast<int>(this->transducer_states().size()),
				[this](int id) { this->emit_canonical_case(id); });
			out.line();

			out.line("/// <summary>Appends what ending the input emits and returns the final length, or -1 where the input may not end here.</summary>");
			this->emitter.emit_step_functions(out,
				this->finish_canonical_name,
				"int state, " + held_parameter + std::string(span_of_char) + " canonical, int length",
				"state, " + this->finish_arguments(),
				static_cast<int>(this->transducer_states().size()),
				[this](int id) { this->emit_finish_case(id); },
				std::nullopt,
				nullptr,
				"-1",
				[this](int id) { return this->transducer_states()[static_cast<std::size_t>(id)].end_output.has_value(); });
		}
	}

	void csharp_emitter::fragment::emit_accept_case(int id)
	{
		code_writer& out = this->writer();
		const state_model& state = this->accepted_states()[static_cast<std::size_t>(id)];

		out.line("case " + decimal(id) + ":");
		out.indent_more();

		for (const auto& arc : state.arcs)
		{
			out.line("if (" + this->emitter.set_condition(arc.set) + ") { return " + decimal(arc.next) + "; }");
		}

		out.line("break;");
		out.indent_less();
	}

	void csharp_emitter::fragment::emit_encode_case(int id)
	{
		code_writer& out = this->writer();
		const state_model& state = this->canonical_states()[static_cast<std::size_t>(id)];

		out.line("case " + decimal(id) + ":");
		out.indent_more();

		for (const auto& arc : state.arcs)
		{
			std::uint64_t offset = 0;

			for (const char_run& run : get_runs(arc.set))
			{
				// Passing this run skips the values below it: those skipped before the whole
				// transition, and this transition's earlier runs. Within the run the character's
				// rank folds into (c - first).
				const std::uint64_t skipped = arc.skipped_before + (arc.next_count * offset);
				const std::optional<std::string> added = this->emitter.added_expression(skipped, arc.next_count, run.first, run.last);
				const std::string next = decimal(arc.next);

				out.line(!added.has_value()
					? "if (" + this->emitter.run_condition(run.first, run.last) + ") { return " + next + "; }"
					: "if (" + this->emitter.run_condition(run.first, run.last) + ") { total += " + *added + "; return " + next + "; }");

				offset += static_cast<std::uint64_t>(run.last - run.first + 1);
			}
		}

		out.line("break;");
		out.indent_less();
	}

	void csharp_emitter::fragment::emit_decode_case(int id)
	{
		code_writer& out = this->writer();
		const state_model& state = this->canonical_states()[static_cast<std::size_t>(id)];

		out.line("case " + decimal(id) + ":");
		out.open_block();

		if (state.arcs.empty())
		{
			// The terminal state. The remaining value is one here, because the caller checked the
			// value against the count of the start state and every step keeps it within the count
			// of the state it moves to.
			out.line("return -1;");
			out.close_block();
			return;
		}

		if (state.accepts_end)
		{
			out.line("if (remaining == 1UL) { return -1; }");
			out.line();
			out.line("remaining -= 1UL;");
		}

		for (std::size_t i = 0; i < state.arcs.size(); ++i)
		{
			const arc_model& arc = state.arcs[i];
			const std::uint64_t block = arc.next_count * static_cast<std::uint64_t>(arc.set.count());

			if (i < state.arcs.size() - 1)
			{
				if (i > 0 || state.accepts_end)
				{
					out.line();
				}

				out.line("if (remaining <= " + this->emitter.literal(block) + ")");
				out.open_block();
				this->emit_decode_arc(arc);
				out.close_block();
				out.line();
				out.line("remaining -= " + this->emitter.literal(block) + ";");
			}
			else
			{
				// The last transition takes whatever is left, by the same invariant as the
				// terminal state above.
				if (i > 0 || state.accepts_end)
				{
					out.line();
				}

				this->emit_decode_arc(arc);
			}
		}

		out.close_block();
	}

	void csharp_emitter::fragment::emit_decode_arc(const arc_model& arc)
	{
		code_writer& out = this->writer();
		const std::string next = decimal(arc.next);
		const std::vector<char_run> runs = get_runs(arc.set);

		if (arc.set.count() == 1)
		{
			// One character leaves the remaining value untouched: its rank is zero and the whole
			// block belongs to the next state.
			out.line("destination[length++] = " + char_literal(runs[0].first) + ";");
			out.line("return " + next + ";");
			return;
		}

		// index is declared once at the top of the method, because these arcs sit at differing
		// brace depths within one switch and sibling declarations there collide.
		out.line(arc.next_count == 1
			? "index = remaining - 1UL;"
			: "index = (remaining - 1UL) / " + this->emitter.literal(arc.next_count) + ";");
		out.line("destination[length++] = " + this->emitter.character_expression(runs) + ";");
		out.line(arc.next_count == 1
			? "remaining = 1UL;"
			: "remaining = ((remaining - 1UL) % " + this->emitter.literal(arc.next_count) + ") + 1UL;");
		out.line("return " + next + ";");
	}

	void csharp_emitter::fragment::emit_canonical_case(int id)
	{
		code_writer& out = this->writer();
		const tx_state_model& state = this->transducer_states()[static_cast<std::size_t>(id)];

		out.line("case " + decimal(id) + ":");
		out.indent_more();

		for (const auto& arc : state.arcs)
		{
			const std::string condition = this->emitter.set_condition(arc.set);
			const std::string next = decimal(arc.next);
			const std::vector<std::string> outputs = this->output_expressions(arc.output, false);

			if (outputs.empty())
			{
				out.line("if (" + condition + ") { return " + next + "; }");
			}
			else if (outputs.size() == 1)
			{
				out.line("if (" + condition + ") { canonical[length++] = " + outputs[0] + "; return " + next + "; }");
			}
			else
			{
				out.line("if (" + condition + ")");
				out.open_block();

				for (const std::string& expression : outputs)
				{
					out.line("canonical[length++] = " + expression + ";");
				}

				out.line("return " + next + ";");
				out.close_block();
			}
		}

		out.line("break;");
		out.indent_less();
	}

	void csharp_emitter::fragment::emit_finish_case(int id)
	{
		code_writer& out = this->writer();
		const tx_state_model& state = this->transducer_states()[static_cast<std::size_t>(id)];

		// A state where the input may not end has no case, so it falls to the default.
		if (!state.end_output.has_value())
		{
			return;
		}

		out.line("case " + decimal(id) + ":");
		out.indent_more();

		for (const std::string& expression : this->output_expressions(*state.end_output, true))
		{
			out.line("canonical[length++] = " + expression + ";");
		}

		out.line("return length;");
		out.indent_less();
	}

	std::string csharp_emitter::fragment::step_arguments() const
	{
		return this->needs_register() ? std::string(held_name) + ", canonical, ref length" : "canonical, ref length";
	}

	std::string csharp_emitter::fragment::finish_arguments() const
	{
		return this->needs_register() ? std::string(held_name) + ", canonical, length" : "canonical, length";
	}

	/// One expression per character an output emits, with each reference resolved against the
	/// characters kept.
	///
	/// @param output The output, over literals and references.
	/// @param for_finish Whether this is an end output, which has no character in hand.
	std::vector<std::string> csharp_emitter::fragment::output_expressions(const std::string& output, bool for_finish) const
	{
		std::vector<std::string> expressions;

		for (std::size_t i = 0; i < output.size(); ++i)
		{
			if (output[i] != copy_marker)
			{
				expressions.push_back(char_literal(output[i]));
				continue;
			}

			const int depth = static_cast<int>(static_cast<unsigned char>(output[i + 1])) - tx_reference::depth_base;

			++i;

			// A step already holds the character it is reading, so depth zero needs no buffer
			// there. The finish function has no character, so it reads even that one back.
			if (depth == 0 && !for_finish)
			{
				expressions.emplace_back("c");
				continue;
			}

			const int at = this->register_depth() - 1 - depth;

			expressions.push_back(std::string(held_name) + "[" + decimal(at) + "]");
		}

		return expressions;
	}

	void csharp_emitter::fragment::emit_accepting_predicate(const std::string& name, const std::vector<state_model>& states)
	{
		code_writer& out = this->writer();

		out.line("/// <summary>Whether the input may end in this state.</summary>");
		out.line("static bool " + name + "(int state)");
		out.open_block();
		out.line("switch (state)");
		out.open_block();

		for (std::size_t id = 0; id < states.size(); ++id)
		{
			if (states[id].accepts_end)
			{
				out.line("case " + decimal(static_cast<int>(id)) + ":");
			}
		}

		out.indent_more();
		out.line("return true;");
		out.indent_less();
		out.line("default:");
		out.indent_more();
		out.line("return false;");
		out.indent_less();
		out.close_block();
		out.close_block();
	}

	// Expression helpers

	std::optional<std::string> csharp_emitter::added_expression(std::uint64_t skipped, std::uint64_t count, char first, char last) const
	{
		if (first == last)
		{
			return skipped == 0 ? std::nullopt : std::optional<std::string>(this->literal(skipped));
		}

		// The accumulator is unsigned, and C# will not mix ulong with the int this subtraction
		// gives, so the rank is converted here rather than at every use.
		const std::string index = "(ulong)(c - " + char_literal(first) + ")";
		const std::string term = count == 1 ? index : this->literal(count) + " * " + index;

		return skipped == 0 ? term : this->literal(skipped) + " + " + term;
	}

	std::string csharp_emitter::character_expression(const std::vector<char_run>& runs) const
	{
		if (runs.size() == 1)
		{
			return this->run_character(runs[0], 0);
		}

		std::string result;
		std::uint64_t cumulative = 0;

		for (std::size_t i = 0; i < runs.size() - 1; ++i)
		{
			const std::uint64_t offset = cumulative;
			cumulative += static_cast<std::uint64_t>(runs[i].last - runs[i].first + 1);
			result += "index < " + this->literal(cumulative) + " ? " + this->run_character(runs[i], offset) + " : ";
		}

		result += this->run_character(runs[runs.size() - 1], cumulative);

		return result;
	}

	std::string csharp_emitter::run_character(char_run run, std::uint64_t offset) const
	{
		if (run.first == run.last)
		{
			return char_literal(run.first);
		}

		const std::string index = offset == 0 ? "(int)index" : "(int)(index - " + this->literal(offset) + ")";

		return "(char)(" + char_literal(run.first) + " + " + index + ")";
	}

	std::string csharp_emitter::char_literal(char c)
	{
		switch (c)
		{
			case '\'': return "'\\''";
			case '\\': return "'\\\\'";
			default:
				return c >= ' ' && c <= '~'
					? "'" + std::string(1, c) + "'"
					: "'\\u" + hex_upper(static_cast<unsigned char>(c), 4) + "'";
		}
	}

	std::string csharp_emitter::literal(std::uint64_t value) const
	{
		return this->grouped(value) + "UL";
	}
}
