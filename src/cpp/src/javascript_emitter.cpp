// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "javascript_emitter.hpp"

#include "code_writer.hpp"
#include "emitter.hpp"
#include "tx.hpp"
#include "tx_machine.hpp"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace logmu::detail
{
	namespace
	{
		/// The name of the flag saying that a public function was given bytes rather than a string.
		const std::string bytes_name = "bytes";

		/// A string as a single-quoted JavaScript literal. A pattern may hold whitespace other than
		/// the space, which is escaped along with the quote and the backslash.
		std::string javascript_string_literal(const std::string& text)
		{
			std::string literal = "'";

			for (const char c : text)
			{
				if (c == '\'' || c == '\\')
				{
					literal += '\\';
					literal += c;
				}
				else if (c == '\t')
				{
					literal += "\\t";
				}
				else if (c == '\n')
				{
					literal += "\\n";
				}
				else if (c == '\r')
				{
					literal += "\\r";
				}
				else if (c < ' ' || c > '~')
				{
					literal += "\\x" + hex_upper(static_cast<unsigned char>(c), 2);
				}
				else
				{
					literal += c;
				}
			}

			return literal + "'";
		}
	}

	/// One emission call's state: the context, the generated names, and the choice between
	/// numbers and BigInt. Per call so the shared instance stays stateless.
	class javascript_emitter::fragment
	{
	public:
		/// The name the generated code keeps the code points it has read under.
		static const std::string held_name;

		fragment(const javascript_emitter& emitter, emit_context& context);

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
		void emit_accepts();
		void emit_encode();
		void emit_read_loop(const std::string& step, const std::string& on_fault, const std::optional<std::string>& before = std::nullopt);
		void emit_decode_publics();
		void emit_get_canonical_form();
		void emit_range_check(const std::string& message);
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

		// Expression helpers

		/// How a JSDoc comment names the value type.
		std::string number_type() const
		{
			return this->big ? "bigint" : "number";
		}

		/// A value literal, BigInt or number as the naxp's size decided.
		std::string value(std::uint64_t value) const
		{
			return this->emitter.grouped(value) + (this->big ? "n" : "");
		}

		std::optional<std::string> added_expression(std::uint64_t skipped, std::uint64_t count, char first, char last) const;
		std::string character_expression(const std::vector<char_run>& runs) const;
		std::string run_character(char_run run, std::uint64_t offset) const;

		const javascript_emitter& emitter;
		emit_context& context;

		// The generated names, each the prefix plus the bare member name, camel cased.
		std::string pattern_name;
		std::string max_encoded_value_name;
		std::string max_length_name;
		std::string accepts_name;
		std::string encode_name;
		std::string decode_name;
		std::string decode_to_bytes_name;
		std::string try_decode_name;
		std::string get_canonical_form_name;
		std::string canonicalise_name;
		std::string rank_name;
		std::string decode_core_name;
		std::string accept_step_name;
		std::string is_accepting_name;
		std::string encode_step_name;
		std::string is_canonical_accepting_name;
		std::string decode_step_name;
		std::string canonical_step_name;
		std::string finish_canonical_name;

		/// Whether values are BigInt rather than number.
		bool big;

		std::string zero;
		std::string one;
	};

	const std::string javascript_emitter::fragment::held_name = "held";

	const javascript_emitter& javascript_emitter::instance()
	{
		static const javascript_emitter shared;

		return shared;
	}

	javascript_emitter::javascript_emitter()
		: emitter(std::nullopt, std::nullopt)
	{
	}

	void javascript_emitter::emit_fragment(emit_context& context) const
	{
		fragment(*this, context).emit();
	}

	// The shape of JavaScript

	void javascript_emitter::open_function(code_writer& writer, std::string_view name, std::string_view parameters) const
	{
		writer.line("function " + std::string(name) + "(" + std::string(parameters) + ") {");
		writer.indent_more();
	}

	void javascript_emitter::close_function(code_writer& writer) const
	{
		writer.indent_less();
		writer.line("}");
	}

	void javascript_emitter::open_dispatch(code_writer& writer) const
	{
		writer.line("switch (state) {");
		writer.indent_more();
	}

	void javascript_emitter::close_dispatch(code_writer& writer, std::string_view result) const
	{
		writer.indent_less();
		writer.line("}");
		writer.line();
		writer.line("return " + std::string(result) + ";");
	}

	void javascript_emitter::write_return(code_writer& writer, std::string_view expression) const
	{
		writer.line("return " + std::string(expression) + ";");
	}

	void javascript_emitter::write_guarded_return(code_writer& writer, std::string_view condition, std::string_view expression) const
	{
		writer.line("if (" + std::string(condition) + ") { return " + std::string(expression) + "; }");
	}

	std::string javascript_emitter::equals_character(char c) const
	{
		return "c === " + code_literal(c);
	}

	std::string javascript_emitter::within_run(char first, char last) const
	{
		return "c >= " + code_literal(first) + " && c <= " + code_literal(last);
	}

	// The fragment

	javascript_emitter::fragment::fragment(const javascript_emitter& emitter, emit_context& context)
		: emitter(emitter)
		, context(context)
		, big(context.compiled.max_encoded_value() > max_safe_integer)
	{
		this->zero = this->big ? "0n" : "0";
		this->one = this->big ? "1n" : "1";

		const std::string& prefix = context.prefix;
		this->pattern_name = camel(prefix, "Pattern");
		this->max_encoded_value_name = camel(prefix, "MaxEncodedValue");
		this->max_length_name = camel(prefix, "MaxLength");
		this->accepts_name = camel(prefix, "Accepts");
		this->encode_name = camel(prefix, "Encode");
		this->decode_name = camel(prefix, "Decode");
		this->decode_to_bytes_name = camel(prefix, "DecodeToBytes");
		this->try_decode_name = camel(prefix, "TryDecode");
		this->get_canonical_form_name = camel(prefix, "GetCanonicalForm");
		this->canonicalise_name = camel(prefix, "Canonicalise");
		this->rank_name = camel(prefix, "Rank");
		this->decode_core_name = camel(prefix, "DecodeCore");
		this->accept_step_name = camel(prefix, "AcceptStep");
		this->is_accepting_name = camel(prefix, "IsAccepting");
		this->encode_step_name = camel(prefix, "EncodeStep");
		this->is_canonical_accepting_name = camel(prefix, "IsCanonicalAccepting");
		this->decode_step_name = camel(prefix, "DecodeStep");
		this->canonical_step_name = camel(prefix, "CanonicalStep");
		this->finish_canonical_name = camel(prefix, "FinishCanonical");
	}

	void javascript_emitter::fragment::emit()
	{
		this->emit_constants();
		this->emit_accepts();
		this->emit_encode();
		this->emit_decode_publics();
		this->emit_get_canonical_form();
		this->emit_steppers();
	}

	void javascript_emitter::fragment::emit_constants()
	{
		code_writer& out = this->writer();

		out.line("/** The naxp this code was generated from. */");
		out.line("const " + this->pattern_name + " = " + javascript_string_literal(this->context.compiled.pattern()) + ";");
		out.line();
		out.line("/** The largest encoded value this naxp produces, which is also how many it has. */");
		out.line("const " + this->max_encoded_value_name + " = " + this->value(this->context.compiled.max_encoded_value()) + ";");
		out.line();
		out.line("/** The length of the longest string this naxp can decode a value to. */");
		out.line("const " + this->max_length_name + " = " + decimal(this->max_length()) + ";");
		out.line();
	}

	// Public functions

	void javascript_emitter::fragment::emit_accepts()
	{
		code_writer& out = this->writer();

		out.line("/** Whether this naxp accepts a string, or the ASCII text in a Uint8Array. A byte outside ASCII is never accepted.");
		out.line(" * @param {string | Uint8Array} text");
		out.line(" * @returns {boolean}");
		out.line(" */");
		out.line("function " + this->accepts_name + "(text) {");
		out.indent_more();
		out.line("const " + bytes_name + " = typeof text !== 'string';");
		out.line("let state = 0;");
		out.line();
		this->emit_read_loop("state = " + this->accept_step_name + "(state, c);", "return false;");
		out.line();
		out.line("return " + this->is_accepting_name + "(state);");
		out.indent_less();
		out.line("}");
		out.line();
	}

	void javascript_emitter::fragment::emit_encode()
	{
		code_writer& out = this->writer();

		out.line("/** The encoded value of a string, or of the ASCII text in a Uint8Array, from 1 to the largest encoded value, or zero where it is invalid.");
		out.line(" * @param {string | Uint8Array} text");
		out.line(" * @returns {" + this->number_type() + "}");
		out.line(" */");
		out.line("function " + this->encode_name + "(text) {");
		out.indent_more();

		if (this->canonicalises())
		{
			out.line("const canonical = " + this->canonicalise_name + "(text);");
			out.line();
			out.line("return canonical === null ? " + this->zero + " : " + this->rank_name + "(canonical);");
		}
		else
		{
			out.line("const " + bytes_name + " = typeof text !== 'string';");
			out.line("const acc = { total: " + this->zero + " };");
			out.line("let state = 0;");
			out.line();
			this->emit_read_loop("state = " + this->encode_step_name + "(state, c, acc);", "return " + this->zero + ";");
			out.line();
			out.line("return " + this->is_accepting_name + "(state) ? acc.total + " + this->one + " : " + this->zero + ";");
		}

		out.indent_less();
		out.line("}");
		out.line();
	}

	/// The loop every entry point reads its input with. A byte is already the code point, and
	/// anything above ASCII fits no transition, so one stepper serves strings and bytes alike; the
	/// function has already set the flag saying which it holds.
	///
	/// @param step The stepping statement, over the code point `c`.
	/// @param on_fault What a failed step does.
	/// @param before A statement between reading the code point and stepping, if any.
	void javascript_emitter::fragment::emit_read_loop(const std::string& step, const std::string& on_fault, const std::optional<std::string>& before)
	{
		code_writer& out = this->writer();

		out.line("for (let i = 0; i < text.length; i++) {");
		out.indent_more();
		out.line("const c = " + bytes_name + " ? text[i] : text.charCodeAt(i);");
		out.line();

		if (before.has_value())
		{
			out.line(*before);
		}

		out.line(step);
		out.line();
		out.line("if (state < 0) { " + on_fault + " }");
		out.indent_less();
		out.line("}");
	}

	void javascript_emitter::fragment::emit_decode_publics()
	{
		code_writer& out = this->writer();
		const std::string count_digits = decimal(this->context.compiled.max_encoded_value());
		const std::string message = "'This naxp encodes the values 1 to " + count_digits + ".'";

		out.line("/** The string a value stands for, which is in canonical form.");
		out.line(" * @param {" + this->number_type() + "} value");
		out.line(" * @returns {string}");
		out.line(" * @throws {RangeError} The value is not one this naxp produces.");
		out.line(" */");
		out.line("function " + this->decode_name + "(value) {");
		out.indent_more();
		this->emit_range_check(message);
		out.line("return String.fromCharCode.apply(null, " + this->decode_core_name + "(value));");
		out.indent_less();
		out.line("}");
		out.line();

		out.line("/** The string a value stands for, as ASCII bytes.");
		out.line(" * @param {" + this->number_type() + "} value");
		out.line(" * @returns {Uint8Array}");
		out.line(" * @throws {RangeError} The value is not one this naxp produces.");
		out.line(" */");
		out.line("function " + this->decode_to_bytes_name + "(value) {");
		out.indent_more();
		this->emit_range_check(message);
		out.line("return Uint8Array.from(" + this->decode_core_name + "(value));");
		out.indent_less();
		out.line("}");
		out.line();

		out.line("/** The string a value stands for, which is in canonical form, or null where the value is not one this naxp produces.");
		out.line(" * @param {" + this->number_type() + "} value");
		out.line(" * @returns {string | null}");
		out.line(" */");
		out.line("function " + this->try_decode_name + "(value) {");
		out.indent_more();
		out.line("if (value < " + this->one + " || value > " + this->max_encoded_value_name + ") { return null; }");
		out.line();
		out.line("return String.fromCharCode.apply(null, " + this->decode_core_name + "(value));");
		out.indent_less();
		out.line("}");
		out.line();
	}

	void javascript_emitter::fragment::emit_get_canonical_form()
	{
		code_writer& out = this->writer();

		out.line("/** The canonical form of a string, or of the ASCII text in a Uint8Array, or null where it is invalid.");
		out.line(" * @param {string | Uint8Array} text");
		out.line(" * @returns {string | null}");
		out.line(" */");
		out.line("function " + this->get_canonical_form_name + "(text) {");
		out.indent_more();

		if (this->canonicalises())
		{
			out.line("const canonical = " + this->canonicalise_name + "(text);");
			out.line();
			out.line("return canonical === null ? null : String.fromCharCode.apply(null, canonical);");
		}
		else
		{
			// Where nothing is unified an accepted string is its own canonical form, so only bytes
			// need anything done to them.
			out.line("if (!" + this->accepts_name + "(text)) { return null; }");
			out.line();
			out.line("return typeof text === 'string' ? text : String.fromCharCode.apply(null, text);");
		}

		out.indent_less();
		out.line("}");
		out.line();
	}

	void javascript_emitter::fragment::emit_range_check(const std::string& message)
	{
		code_writer& out = this->writer();

		out.line("if (value < " + this->one + " || value > " + this->max_encoded_value_name + ") {");
		out.indent_more();
		out.line("throw new RangeError(" + message + ");");
		out.indent_less();
		out.line("}");
		out.line();
	}

	// Private functions

	void javascript_emitter::fragment::emit_steppers()
	{
		code_writer& out = this->writer();

		if (this->canonicalises())
		{
			out.line("/** The code points of the canonical form of a string, or of the ASCII text in a Uint8Array, or null where it is invalid. */");
			out.line("function " + this->canonicalise_name + "(text) {");
			out.indent_more();
			out.line("const " + bytes_name + " = typeof text !== 'string';");
			out.line("const canonical = [];");
			out.line("let state = 0;");
			out.line();

			if (this->needs_register())
			{
				// The code points read, oldest first, so a reference of depth d is the one at
				// register_depth - 1 - d. Shifting a buffer this small beats indexing a ring.
				out.line("const " + held_name + " = new Array(" + decimal(this->register_depth()) + ").fill(0);");
				out.line();
			}

			this->emit_read_loop(
				"state = " + this->canonical_step_name + "(state, c, " + this->step_arguments() + ");",
				"return null;",
				this->needs_register() ? std::optional<std::string>(held_name + ".shift(); " + held_name + ".push(c);") : std::nullopt);
			out.line();
			out.line("return " + this->finish_canonical_name + "(state, " + this->finish_arguments() + ") ? canonical : null;");
			out.indent_less();
			out.line("}");
			out.line();

			out.line("/** The rank of a canonical string, as code points, within the canonical language, or zero where it is not in it. */");
			out.line("function " + this->rank_name + "(codes) {");
			out.indent_more();
			out.line("const acc = { total: " + this->zero + " };");
			out.line("let state = 0;");
			out.line();
			out.line("for (let i = 0; i < codes.length; i++) {");
			out.indent_more();
			out.line("state = " + this->encode_step_name + "(state, codes[i], acc);");
			out.line();
			out.line("if (state < 0) { return " + this->zero + "; }");
			out.indent_less();
			out.line("}");
			out.line();
			out.line("return " + this->is_canonical_accepting_name + "(state) ? acc.total + " + this->one + " : " + this->zero + ";");
			out.indent_less();
			out.line("}");
			out.line();
		}

		out.line("/** The code points of an encoded value already checked against the largest one. */");
		out.line("function " + this->decode_core_name + "(value) {");
		out.indent_more();
		out.line("const codes = [];");
		out.line("const box = { remaining: value };");
		out.line("let state = 0;");
		out.line();
		out.line("while (state >= 0) {");
		out.indent_more();
		out.line("state = " + this->decode_step_name + "(state, box, codes);");
		out.indent_less();
		out.line("}");
		out.line();
		out.line("return codes;");
		out.indent_less();
		out.line("}");
		out.line();

		out.line("/** The acceptor's transition: the next state, or -1 where the code point fits nothing. */");
		this->emitter.emit_step_functions(
			out,
			this->accept_step_name,
			"state, c",
			"state, c",
			static_cast<int>(this->accepted_states().size()),
			[this](int id) { this->emit_accept_case(id); });
		out.line();

		this->emit_accepting_predicate(this->is_accepting_name, this->accepted_states());
		out.line();

		out.line("/** The canonical machine's transition, accumulating the values skipped: the next state, or -1. */");
		this->emitter.emit_step_functions(
			out,
			this->encode_step_name,
			"state, c, acc",
			"state, c, acc",
			static_cast<int>(this->canonical_states().size()),
			[this](int id) { this->emit_encode_case(id); });
		out.line();

		if (this->canonicalises())
		{
			this->emit_accepting_predicate(this->is_canonical_accepting_name, this->canonical_states());
			out.line();
		}

		out.line("/** One step of decoding: appends at most one code point and returns the next state, or -1 when the string is complete. */");
		this->emitter.emit_step_functions(
			out,
			this->decode_step_name,
			"state, box, codes",
			"state, box, codes",
			static_cast<int>(this->canonical_states().size()),
			[this](int id) { this->emit_decode_case(id); },
			"let index;",
			[this](int id) { return needs_index(this->canonical_states()[static_cast<std::size_t>(id)]); });

		if (this->canonicalises())
		{
			out.line();
			out.line("/** The canonicalising transition, appending what reading the code point emits: the next state, or -1. */");
			this->emitter.emit_step_functions(
				out,
				this->canonical_step_name,
				"state, c, " + this->step_arguments(),
				"state, c, " + this->step_arguments(),
				static_cast<int>(this->transducer_states().size()),
				[this](int id) { this->emit_canonical_case(id); });
			out.line();

			out.line("/** Appends what ending the input emits, and returns whether the input may end here. */");
			this->emitter.emit_step_functions(
				out,
				this->finish_canonical_name,
				"state, " + this->finish_arguments(),
				"state, " + this->finish_arguments(),
				static_cast<int>(this->transducer_states().size()),
				[this](int id) { this->emit_finish_case(id); },
				std::nullopt,
				nullptr,
				"false",
				[this](int id) { return this->transducer_states()[static_cast<std::size_t>(id)].end_output.has_value(); });
		}
	}

	void javascript_emitter::fragment::emit_accept_case(int id)
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

	void javascript_emitter::fragment::emit_encode_case(int id)
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
				// transition, and this transition's earlier runs. Within the run the code point's
				// rank folds into (c - first).
				const std::uint64_t skipped = arc.skipped_before + (arc.next_count * offset);
				const std::optional<std::string> added = this->added_expression(skipped, arc.next_count, run.first, run.last);
				const std::string next = decimal(arc.next);

				out.line(!added.has_value()
					? "if (" + this->emitter.run_condition(run.first, run.last) + ") { return " + next + "; }"
					: "if (" + this->emitter.run_condition(run.first, run.last) + ") { acc.total += " + *added + "; return " + next + "; }");

				offset += static_cast<std::uint64_t>(run.last - run.first + 1);
			}
		}

		out.line("break;");
		out.indent_less();
	}

	void javascript_emitter::fragment::emit_decode_case(int id)
	{
		code_writer& out = this->writer();
		const state_model& state = this->canonical_states()[static_cast<std::size_t>(id)];

		out.line("case " + decimal(id) + ": {");
		out.indent_more();

		if (state.arcs.empty())
		{
			// The terminal state. The remaining value is one here, because the caller checked the
			// value against the count of the start state and every step keeps it within the count
			// of the state it moves to.
			out.line("return -1;");
			out.indent_less();
			out.line("}");
			return;
		}

		if (state.accepts_end)
		{
			out.line("if (box.remaining === " + this->one + ") { return -1; }");
			out.line();
			out.line("box.remaining -= " + this->one + ";");
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

				out.line("if (box.remaining <= " + this->value(block) + ") {");
				out.indent_more();
				this->emit_decode_arc(arc);
				out.indent_less();
				out.line("}");
				out.line();
				out.line("box.remaining -= " + this->value(block) + ";");
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

		out.indent_less();
		out.line("}");
	}

	void javascript_emitter::fragment::emit_decode_arc(const arc_model& arc)
	{
		code_writer& out = this->writer();
		const std::string next = decimal(arc.next);
		const std::vector<char_run> runs = get_runs(arc.set);

		if (arc.set.count() == 1)
		{
			// One code point leaves the remaining value untouched: its rank is zero and the whole
			// block belongs to the next state.
			out.line("codes.push(" + code_literal(runs[0].first) + ");");
			out.line("return " + next + ";");
			return;
		}

		// index is declared once at the top of the function, because these arcs sit at differing
		// brace depths within one switch and sibling declarations there collide.
		out.line(arc.next_count == 1
			? "index = box.remaining - " + this->one + ";"
			: this->big
				? "index = (box.remaining - 1n) / " + this->value(arc.next_count) + ";"
				: "index = Math.floor((box.remaining - 1) / " + this->value(arc.next_count) + ");");
		out.line("codes.push(" + this->character_expression(runs) + ");");
		out.line(arc.next_count == 1
			? "box.remaining = " + this->one + ";"
			: "box.remaining = ((box.remaining - " + this->one + ") % " + this->value(arc.next_count) + ") + " + this->one + ";");
		out.line("return " + next + ";");
	}

	void javascript_emitter::fragment::emit_canonical_case(int id)
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

			if (arc.output.empty())
			{
				out.line("if (" + condition + ") { return " + next + "; }");
			}
			else if (arc.output.size() == 1)
			{
				out.line("if (" + condition + ") { canonical.push(" + outputs[0] + "); return " + next + "; }");
			}
			else
			{
				out.line("if (" + condition + ") {");
				out.indent_more();

				for (const std::string& expression : outputs)
				{
					out.line("canonical.push(" + expression + ");");
				}

				out.line("return " + next + ";");
				out.indent_less();
				out.line("}");
			}
		}

		out.line("break;");
		out.indent_less();
	}

	void javascript_emitter::fragment::emit_finish_case(int id)
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
			out.line("canonical.push(" + expression + ");");
		}

		out.line("return true;");
		out.indent_less();
	}

	std::string javascript_emitter::fragment::step_arguments() const
	{
		return this->needs_register() ? held_name + ", canonical" : "canonical";
	}

	std::string javascript_emitter::fragment::finish_arguments() const
	{
		return this->needs_register() ? held_name + ", canonical" : "canonical";
	}

	/// One expression per code point an output emits, with each reference resolved against the
	/// code points kept.
	///
	/// @param output The output, over literals and references.
	/// @param for_finish Whether this is an end output, which has no code point in hand.
	std::vector<std::string> javascript_emitter::fragment::output_expressions(const std::string& output, bool for_finish) const
	{
		std::vector<std::string> expressions;

		for (std::size_t i = 0; i < output.size(); ++i)
		{
			if (output[i] != copy_marker)
			{
				expressions.push_back(code_literal(output[i]));
				continue;
			}

			const int depth = static_cast<int>(static_cast<unsigned char>(output[i + 1])) - tx_reference::depth_base;

			++i;

			// A step already holds the code point it is reading, so depth zero needs no buffer
			// there. The finish function has none, so it reads even that one back.
			if (depth == 0 && !for_finish)
			{
				expressions.emplace_back("c");
				continue;
			}

			const int at = this->register_depth() - 1 - depth;

			expressions.push_back(held_name + "[" + decimal(at) + "]");
		}

		return expressions;
	}

	void javascript_emitter::fragment::emit_accepting_predicate(const std::string& name, const std::vector<state_model>& states)
	{
		code_writer& out = this->writer();

		out.line("/** Whether the input may end in this state. */");
		out.line("function " + name + "(state) {");
		out.indent_more();
		out.line("switch (state) {");
		out.indent_more();

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
		out.indent_less();
		out.line("}");
		out.indent_less();
		out.line("}");
	}

	// Expression helpers

	/// What passing this run adds to the total, or nothing where it adds nothing.
	std::optional<std::string> javascript_emitter::fragment::added_expression(std::uint64_t skipped, std::uint64_t count, char first, char last) const
	{
		if (first == last)
		{
			return skipped == 0 ? std::nullopt : std::optional<std::string>(this->value(skipped));
		}

		// The code point's rank is a number either way, so BigInt arithmetic converts it.
		const std::string index = this->big ? "BigInt(c - " + code_literal(first) + ")" : "(c - " + code_literal(first) + ")";
		const std::string term = count == 1 ? index : this->value(count) + " * " + index;

		return skipped == 0 ? term : this->value(skipped) + " + " + term;
	}

	/// The code point at position `index` within a set, as an expression over its runs.
	std::string javascript_emitter::fragment::character_expression(const std::vector<char_run>& runs) const
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
			result += "index < " + this->value(cumulative) + " ? " + this->run_character(runs[i], offset) + " : ";
		}

		result += this->run_character(runs[runs.size() - 1], cumulative);

		return result;
	}

	std::string javascript_emitter::fragment::run_character(char_run run, std::uint64_t offset) const
	{
		if (run.first == run.last)
		{
			return code_literal(run.first);
		}

		// A code point is a number, so the BigInt index converts back on the way out.
		const std::string index = this->big
			? (offset == 0 ? std::string("Number(index)") : "Number(index - " + this->value(offset) + ")")
			: (offset == 0 ? std::string("index") : "(index - " + this->value(offset) + ")");

		return code_literal(run.first) + " + " + index;
	}

	// Text helpers

	std::string javascript_emitter::code_literal(char c)
	{
		return "0x" + hex_upper(static_cast<unsigned char>(c), 2);
	}

	std::string javascript_emitter::camel(const std::string& prefix, const std::string& member)
	{
		std::string name = prefix + member;

		if (name[0] >= 'A' && name[0] <= 'Z')
		{
			name[0] = static_cast<char>(name[0] + ('a' - 'A'));
		}

		return name;
	}
}
