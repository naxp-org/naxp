// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "c_family_emitter.hpp"

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
#include <utility>
#include <vector>

namespace logmu::detail
{
	namespace
	{
		bool is_ascii_upper(char c) noexcept
		{
			return c >= 'A' && c <= 'Z';
		}

		bool is_ascii_lower(char c) noexcept
		{
			return c >= 'a' && c <= 'z';
		}

		bool is_ascii_digit(char c) noexcept
		{
			return c >= '0' && c <= '9';
		}

		std::size_t at(int id) noexcept
		{
			return static_cast<std::size_t>(id);
		}
	}

	const std::string c_family_emitter::fragment::held_name = "held";

	void c_family_emitter::emit_fragment(emit_context& context) const
	{
		fragment fragment(*this, context);

		this->emit_header(fragment);
		fragment.emit_prototypes();
		this->emit_publics(fragment);
		fragment.emit_steppers();
	}

	// The shape of the family

	void c_family_emitter::open_function(code_writer& writer, std::string_view name, std::string_view parameters) const
	{
		writer.line(this->step_linkage() + " int " + std::string(name) + "(" + std::string(parameters) + ")");
		writer.open_block();
	}

	void c_family_emitter::close_function(code_writer& writer) const
	{
		writer.close_block();
	}

	void c_family_emitter::open_dispatch(code_writer& writer) const
	{
		writer.line("switch (state)");
		writer.open_block();
	}

	void c_family_emitter::close_dispatch(code_writer& writer, std::string_view result) const
	{
		writer.close_block();
		writer.line();
		writer.line("return " + std::string(result) + ";");
	}

	void c_family_emitter::write_return(code_writer& writer, std::string_view expression) const
	{
		writer.line("return " + std::string(expression) + ";");
	}

	void c_family_emitter::write_guarded_return(code_writer& writer, std::string_view condition, std::string_view expression) const
	{
		writer.line("if (" + std::string(condition) + ") { return " + std::string(expression) + "; }");
	}

	std::string c_family_emitter::equals_character(char c) const
	{
		return "c == " + char_literal(c);
	}

	std::string c_family_emitter::within_run(char first, char last) const
	{
		return "c >= " + char_literal(first) + " && c <= " + char_literal(last);
	}

	// Text helpers

	std::string c_family_emitter::snake(const std::string& prefix, const std::string& member)
	{
		const std::string name = prefix + member;
		std::string result;
		result.reserve(name.size() + 4);

		for (std::size_t i = 0; i < name.size(); ++i)
		{
			const char c = name[i];

			if (i > 0 && is_ascii_upper(c))
			{
				const char previous = name[i - 1];
				const bool boundary = is_ascii_lower(previous)
					|| is_ascii_digit(previous)
					|| (is_ascii_upper(previous) && i + 1 < name.size() && is_ascii_lower(name[i + 1]));

				if (boundary)
				{
					result += '_';
				}
			}

			result += is_ascii_upper(c) ? static_cast<char>(c + ('a' - 'A')) : c;
		}

		return result;
	}

	std::string c_family_emitter::char_literal(char c)
	{
		switch (c)
		{
			case '\'': return "'\\''";
			case '\\': return "'\\\\'";
			default:
				return c >= ' ' && c <= '~'
					? "'" + std::string(1, c) + "'"
					: "'\\x" + hex_upper(static_cast<unsigned char>(c), 2) + "'";
		}
	}

	bool c_family_emitter::is_identifier(const std::string& expression) noexcept
	{
		for (const char c : expression)
		{
			if (!is_ascii_upper(c) && !is_ascii_lower(c) && !is_ascii_digit(c) && c != '_')
			{
				return false;
			}
		}

		return true;
	}

	// The fragment

	c_family_emitter::fragment::fragment(const c_family_emitter& emitter, emit_context& context)
		: emitter(emitter)
		, context(context)
	{
		std::string bare;
		std::string suffix;

		switch (context.value_type)
		{
			case naxp_value_type::int8: bare = "int8_t"; suffix = ""; break;
			case naxp_value_type::uint8: bare = "uint8_t"; suffix = ""; break;
			case naxp_value_type::int16: bare = "int16_t"; suffix = ""; break;
			case naxp_value_type::uint16: bare = "uint16_t"; suffix = ""; break;
			case naxp_value_type::int32: bare = "int32_t"; suffix = ""; break;
			case naxp_value_type::uint32: bare = "uint32_t"; suffix = "U"; break;
			case naxp_value_type::int64: bare = "int64_t"; suffix = "LL"; break;
			case naxp_value_type::uint64: bare = "uint64_t"; suffix = "ULL"; break;
			default: throw std::logic_error("Unhandled value type.");
		}

		this->value_keyword = emitter.type_prefix() + bare;
		this->value_is_widest = context.value_type == naxp_value_type::uint64;
		this->max_encoded_value_literal = emitter.grouped(context.compiled.max_encoded_value()) + suffix;
		this->value_zero = this->value_is_widest ? "0ULL" : "0";
		this->value_one = this->value_is_widest ? "1ULL" : "1";

		// decode_core takes the widest type. Only that reaches it unconverted; every other type
		// needs the cast, which the range check just made safe.
		this->decode_core_argument = this->value_is_widest ? "value" : emitter.cast(emitter.uint64(), "value");

		const std::string& prefix = context.prefix;
		this->max_encoded_value_name = snake(prefix, "MaxEncodedValue");
		this->max_length_name = snake(prefix, "MaxLength");
		this->accepts_name = snake(prefix, "Accepts");
		this->encode_name = snake(prefix, "Encode");
		this->decode_name = snake(prefix, "Decode");
		this->rank_name = snake(prefix, "Rank");
		this->decode_core_name = snake(prefix, "DecodeCore");
		this->accept_step_name = snake(prefix, "AcceptStep");
		this->is_accepting_name = snake(prefix, "IsAccepting");
		this->encode_step_name = snake(prefix, "EncodeStep");
		this->is_canonical_accepting_name = snake(prefix, "IsCanonicalAccepting");
		this->decode_step_name = snake(prefix, "DecodeStep");
		this->canonical_step_name = snake(prefix, "CanonicalStep");
		this->finish_canonical_name = snake(prefix, "FinishCanonical");
	}

	// The steppers' signatures

	std::string c_family_emitter::fragment::rank_parameters() const
	{
		return this->emitter.pointer("const char", "canonical") + ", int length";
	}

	std::string c_family_emitter::fragment::decode_core_parameters() const
	{
		return this->emitter.uint64() + " value, " + this->emitter.pointer("char", "destination");
	}

	std::string c_family_emitter::fragment::accept_step_parameters() const
	{
		return "int state, int c";
	}

	std::string c_family_emitter::fragment::encode_step_parameters() const
	{
		return "int state, int c, " + this->emitter.by_reference(this->emitter.uint64(), "total");
	}

	std::string c_family_emitter::fragment::decode_step_parameters() const
	{
		return "int state, " + this->emitter.by_reference(this->emitter.uint64(), "remaining") + ", "
			+ this->emitter.pointer("char", "destination") + ", " + this->emitter.by_reference("int", "length");
	}

	std::string c_family_emitter::fragment::held_parameter() const
	{
		return this->needs_register() ? this->emitter.pointer("const char", held_name) + ", " : std::string();
	}

	std::string c_family_emitter::fragment::canonical_step_parameters() const
	{
		return "int state, int c, " + this->held_parameter() + this->emitter.pointer("char", "canonical") + ", "
			+ this->emitter.by_reference("int", "length");
	}

	std::string c_family_emitter::fragment::finish_canonical_parameters() const
	{
		return "int state, " + this->held_parameter() + this->emitter.pointer("char", "canonical") + ", int length";
	}

	// Prototypes

	void c_family_emitter::fragment::emit_prototypes()
	{
		code_writer& out = this->writer();
		const std::string linkage = this->emitter.step_linkage();

		this->emitter.comment(out, "The steppers, which are defined below the public functions.");

		if (this->canonicalises())
		{
			out.line(linkage + " " + this->emitter.uint64() + " " + this->rank_name + "(" + this->rank_parameters() + ");");
		}

		out.line(linkage + " int " + this->decode_core_name + "(" + this->decode_core_parameters() + ");");
		this->emit_step_prototypes(this->accept_step_name, this->accept_step_parameters(), static_cast<int>(this->accepted_states().size()));
		out.line(linkage + " bool " + this->is_accepting_name + "(int state);");
		this->emit_step_prototypes(this->encode_step_name, this->encode_step_parameters(), static_cast<int>(this->canonical_states().size()));

		if (this->canonicalises())
		{
			out.line(linkage + " bool " + this->is_canonical_accepting_name + "(int state);");
		}

		this->emit_step_prototypes(this->decode_step_name, this->decode_step_parameters(), static_cast<int>(this->canonical_states().size()));

		if (this->canonicalises())
		{
			this->emit_step_prototypes(this->canonical_step_name, this->canonical_step_parameters(), static_cast<int>(this->transducer_states().size()));
			this->emit_step_prototypes(this->finish_canonical_name, this->finish_canonical_parameters(), static_cast<int>(this->transducer_states().size()));
		}

		out.line();
	}

	/// A stepper's prototype, and its chunks' where the machine is split.
	void c_family_emitter::fragment::emit_step_prototypes(const std::string& name, const std::string& parameters, int state_count)
	{
		code_writer& out = this->writer();

		out.line(this->emitter.step_linkage() + " int " + name + "(" + parameters + ");");

		const int chunks = chunk_count(state_count);

		if (chunks == 1)
		{
			return;
		}

		for (int chunk = 0; chunk < chunks; ++chunk)
		{
			out.line(this->emitter.step_linkage() + " int " + name + decimal(chunk) + "(" + parameters + ");");
		}
	}

	// The steppers

	void c_family_emitter::fragment::emit_steppers()
	{
		code_writer& out = this->writer();
		const std::string linkage = this->emitter.step_linkage();
		const std::string uint64 = this->emitter.uint64();

		if (this->canonicalises())
		{
			this->emitter.comment(out, "The rank of a canonical string within the canonical language, or zero where it is not in it.");
			out.line(linkage + " " + uint64 + " " + this->rank_name + "(" + this->rank_parameters() + ")");
			out.open_block();
			out.line("int state = 0;");
			out.line(uint64 + " total = 0ULL;");
			out.line();
			out.line("for (int i = 0; i < length; ++i)");
			out.open_block();
			out.line("state = " + this->encode_step_name + "(state, canonical[i], " + this->emitter.address_of("total") + ");");
			out.line();
			out.line("if (state < 0) { return 0ULL; }");
			out.close_block();
			out.line();
			out.line("return " + this->is_canonical_accepting_name + "(state) ? total + 1ULL : 0ULL;");
			out.close_block();
			out.line();
		}

		this->emitter.comment(out, "Writes the string of a value that was already checked against " + this->max_encoded_value_name + ", and returns its length.");
		out.line(linkage + " int " + this->decode_core_name + "(" + this->decode_core_parameters() + ")");
		out.open_block();
		out.line(uint64 + " remaining = value;");
		out.line("int state = 0;");
		out.line("int length = 0;");
		out.line();
		out.line("while (state >= 0)");
		out.open_block();
		out.line("state = " + this->decode_step_name + "(state, " + this->emitter.address_of("remaining") + ", destination, " + this->emitter.address_of("length") + ");");
		out.close_block();
		out.line();
		out.line("return length;");
		out.close_block();
		out.line();

		this->emitter.comment(out, "The acceptor's transition: the next state, or -1 where the character fits nothing.");
		this->emitter.emit_step_functions(out,
			this->accept_step_name,
			this->accept_step_parameters(),
			"state, c",
			static_cast<int>(this->accepted_states().size()),
			[this](int id) { this->emit_accept_case(id); },
			std::nullopt,
			nullptr,
			"-1",
			nullptr,
			[this](int first, int count) { this->accept_prologue(first, count); });
		out.line();

		this->emit_accepting_predicate(this->is_accepting_name, this->accepted_states());
		out.line();

		this->emitter.comment(out, "The canonical machine's transition, accumulating the values skipped: the next state, or -1.");
		this->emitter.emit_step_functions(out,
			this->encode_step_name,
			this->encode_step_parameters(),
			"state, c, total",
			static_cast<int>(this->canonical_states().size()),
			[this](int id) { this->emit_encode_case(id); },
			std::nullopt,
			nullptr,
			"-1",
			nullptr,
			[this](int first, int count) { this->encode_prologue(first, count); });
		out.line();

		if (this->canonicalises())
		{
			this->emit_accepting_predicate(this->is_canonical_accepting_name, this->canonical_states());
			out.line();
		}

		this->emitter.comment(out, "One step of decoding: appends at most one character and returns the next state, or -1 when the string is complete.");
		this->emitter.emit_step_functions(out,
			this->decode_step_name,
			this->decode_step_parameters(),
			"state, remaining, destination, length",
			static_cast<int>(this->canonical_states().size()),
			[this](int id) { this->emit_decode_case(id); },
			uint64 + " index;",
			[this](int id) { return needs_index(this->canonical_states()[at(id)]); },
			"-1",
			nullptr,
			[this](int first, int count) { this->decode_prologue(first, count); });

		if (this->canonicalises())
		{
			out.line();
			this->emitter.comment(out, "The canonicalising transition, appending what reading the character emits: the next state, or -1.");
			this->emitter.emit_step_functions(out,
				this->canonical_step_name,
				this->canonical_step_parameters(),
				"state, c, " + this->step_arguments("length"),
				static_cast<int>(this->transducer_states().size()),
				[this](int id) { this->emit_canonical_case(id); },
				std::nullopt,
				nullptr,
				"-1",
				nullptr,
				[this](int first, int count) { this->canonical_prologue(first, count); });
			out.line();

			this->emitter.comment(out, "Appends what ending the input emits and returns the final length, or -1 where the input may not end here.");
			this->emitter.emit_step_functions(out,
				this->finish_canonical_name,
				this->finish_canonical_parameters(),
				"state, " + this->finish_arguments(),
				static_cast<int>(this->transducer_states().size()),
				[this](int id) { this->emit_finish_case(id); },
				std::nullopt,
				nullptr,
				"-1",
				[this](int id) { return this->finish_case_needed(id); },
				[this](int first, int count) { this->finish_prologue(first, count); });
		}
	}

	void c_family_emitter::fragment::emit_accept_case(int id)
	{
		code_writer& out = this->writer();
		const state_model& state = this->accepted_states()[at(id)];

		out.line("case " + decimal(id) + ":");
		out.indent_more();

		for (const auto& arc : state.arcs)
		{
			out.line("if (" + this->emitter.set_condition(arc.set) + ") { return " + decimal(arc.next) + "; }");
		}

		out.line("break;");
		out.indent_less();
	}

	void c_family_emitter::fragment::emit_encode_case(int id)
	{
		code_writer& out = this->writer();
		const state_model& state = this->canonical_states()[at(id)];
		const std::string total = this->emitter.dereference("total");

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
				const std::optional<std::string> added = this->added_expression(skipped, arc.next_count, run.first, run.last);
				const std::string next = decimal(arc.next);

				out.line(!added.has_value()
					? "if (" + this->emitter.run_condition(run.first, run.last) + ") { return " + next + "; }"
					: "if (" + this->emitter.run_condition(run.first, run.last) + ") { " + total + " += " + *added + "; return " + next + "; }");

				offset += static_cast<std::uint64_t>(run.last - run.first + 1);
			}
		}

		out.line("break;");
		out.indent_less();
	}

	void c_family_emitter::fragment::emit_decode_case(int id)
	{
		code_writer& out = this->writer();
		const state_model& state = this->canonical_states()[at(id)];
		const std::string remaining = this->emitter.dereference("remaining");

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
			out.line("if (" + remaining + " == 1ULL) { return -1; }");
			out.line();
			out.line(remaining + " -= 1ULL;");
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

				out.line("if (" + remaining + " <= " + this->emitter.literal(block) + ")");
				out.open_block();
				this->emit_decode_arc(arc);
				out.close_block();
				out.line();
				out.line(remaining + " -= " + this->emitter.literal(block) + ";");
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

	void c_family_emitter::fragment::emit_decode_arc(const arc_model& arc)
	{
		code_writer& out = this->writer();
		const std::string next = decimal(arc.next);
		const std::string remaining = this->emitter.dereference("remaining");
		const std::string append = "destination[" + this->emitter.increment("length") + "]";
		const std::vector<char_run> runs = get_runs(arc.set);

		if (arc.set.count() == 1)
		{
			// One character leaves the remaining value untouched: its rank is zero and the whole
			// block belongs to the next state.
			out.line(append + " = " + char_literal(runs[0].first) + ";");
			out.line("return " + next + ";");
			return;
		}

		// index is declared once at the top of the function, because these arcs sit at differing
		// brace depths within one switch and sibling declarations there collide.
		out.line(arc.next_count == 1
			? "index = " + remaining + " - 1ULL;"
			: "index = (" + remaining + " - 1ULL) / " + this->emitter.literal(arc.next_count) + ";");
		out.line(append + " = " + this->character_expression(runs) + ";");
		out.line(arc.next_count == 1
			? remaining + " = 1ULL;"
			: remaining + " = ((" + remaining + " - 1ULL) % " + this->emitter.literal(arc.next_count) + ") + 1ULL;");
		out.line("return " + next + ";");
	}

	void c_family_emitter::fragment::emit_canonical_case(int id)
	{
		code_writer& out = this->writer();
		const tx_state_model& state = this->transducer_states()[at(id)];
		const std::string append = "canonical[" + this->emitter.increment("length") + "]";

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
				out.line("if (" + condition + ") { " + append + " = " + outputs[0] + "; return " + next + "; }");
			}
			else
			{
				out.line("if (" + condition + ")");
				out.open_block();

				for (const std::string& expression : outputs)
				{
					out.line(append + " = " + expression + ";");
				}

				out.line("return " + next + ";");
				out.close_block();
			}
		}

		out.line("break;");
		out.indent_less();
	}

	void c_family_emitter::fragment::emit_finish_case(int id)
	{
		code_writer& out = this->writer();
		const tx_state_model& state = this->transducer_states()[at(id)];

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

	bool c_family_emitter::fragment::finish_case_needed(int id) const
	{
		return this->transducer_states()[at(id)].end_output.has_value();
	}

	/// One expression per character an output emits, with each reference resolved against the
	/// characters kept.
	///
	/// @param output The output, over literals and references.
	/// @param for_finish Whether this is an end output, which has no character in hand.
	std::vector<std::string> c_family_emitter::fragment::output_expressions(const std::string& output, bool for_finish) const
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
			// there. The finish function has no character, so it reads even that one back. The
			// character in hand is an int, so it is converted going in.
			if (depth == 0 && !for_finish)
			{
				expressions.push_back(this->emitter.cast("char", "c"));
				continue;
			}

			const int at_index = this->register_depth() - 1 - depth;

			expressions.push_back(held_name + "[" + decimal(at_index) + "]");
		}

		return expressions;
	}

	void c_family_emitter::fragment::emit_accepting_predicate(const std::string& name, const std::vector<state_model>& states)
	{
		code_writer& out = this->writer();

		this->emitter.comment(out, "Whether the input may end in this state.");
		out.line(this->emitter.step_linkage() + " bool " + name + "(int state)");
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

	// What a range of states leaves unused. Each predicate mirrors the case emitter above it: a
	// parameter is used exactly when some state in the range writes a line that names it. The
	// compiler harness in the tests is what keeps them honest.

	void c_family_emitter::fragment::accept_prologue(int first, int count)
	{
		this->mark_unused({ { "c", any_arcs(this->accepted_states(), first, count) } });
	}

	void c_family_emitter::fragment::encode_prologue(int first, int count)
	{
		this->mark_unused({
			{ "c", any_arcs(this->canonical_states(), first, count) },
			{ "total", this->any_addition(first, count) } });
	}

	void c_family_emitter::fragment::decode_prologue(int first, int count)
	{
		const bool appends = any_arcs(this->canonical_states(), first, count);

		this->mark_unused({
			{ "remaining", this->any_remaining(first, count) },
			{ "destination", appends },
			{ "length", appends } });
	}

	void c_family_emitter::fragment::canonical_prologue(int first, int count)
	{
		const bool appends = this->any_output(first, count);

		this->mark_unused({
			{ "c", this->any_transducer_arcs(first, count) },
			{ held_name, !this->needs_register() || this->any_reach_back(first, count, false) },
			{ "canonical", appends },
			{ "length", appends } });
	}

	void c_family_emitter::fragment::finish_prologue(int first, int count)
	{
		const bool any = this->any_end_output(first, count);

		this->mark_unused({
			{ "state", any },
			{ held_name, !this->needs_register() || this->any_reach_back(first, count, true) },
			{ "canonical", this->any_end_text(first, count) },
			{ "length", any } });
	}

	/// Voids each parameter the range leaves unused, so the compiler does not warn of it. A
	/// parameter the function does not have counts as used, which is how the register is passed
	/// where there is none.
	void c_family_emitter::fragment::mark_unused(const std::vector<std::pair<std::string, bool>>& parameters)
	{
		code_writer& out = this->writer();
		bool any = false;

		for (const auto& parameter : parameters)
		{
			if (parameter.second)
			{
				continue;
			}

			out.line("(void)" + parameter.first + ";");
			any = true;
		}

		if (any)
		{
			out.line();
		}
	}

	bool c_family_emitter::fragment::any_arcs(const std::vector<state_model>& states, int first, int count) noexcept
	{
		for (int id = first; id < first + count; ++id)
		{
			if (!states[at(id)].arcs.empty())
			{
				return true;
			}
		}

		return false;
	}

	/// Whether any run in the range adds to the total, which mirrors `added_expression`.
	bool c_family_emitter::fragment::any_addition(int first, int count) const noexcept
	{
		for (int id = first; id < first + count; ++id)
		{
			for (const auto& arc : this->canonical_states()[at(id)].arcs)
			{
				if (arc.set.count() > 1 || arc.skipped_before != 0)
				{
					return true;
				}
			}
		}

		return false;
	}

	/// Whether any state in the range reads the remaining value, which mirrors `emit_decode_case`.
	bool c_family_emitter::fragment::any_remaining(int first, int count) const noexcept
	{
		for (int id = first; id < first + count; ++id)
		{
			const state_model& state = this->canonical_states()[at(id)];

			// The terminal state returns before it reads anything.
			if (state.arcs.empty())
			{
				continue;
			}

			if (state.accepts_end || state.arcs.size() > 1 || needs_index(state))
			{
				return true;
			}
		}

		return false;
	}

	bool c_family_emitter::fragment::any_transducer_arcs(int first, int count) const noexcept
	{
		for (int id = first; id < first + count; ++id)
		{
			if (!this->transducer_states()[at(id)].arcs.empty())
			{
				return true;
			}
		}

		return false;
	}

	bool c_family_emitter::fragment::any_output(int first, int count) const noexcept
	{
		for (int id = first; id < first + count; ++id)
		{
			for (const auto& arc : this->transducer_states()[at(id)].arcs)
			{
				if (!arc.output.empty())
				{
					return true;
				}
			}
		}

		return false;
	}

	bool c_family_emitter::fragment::any_end_output(int first, int count) const noexcept
	{
		for (int id = first; id < first + count; ++id)
		{
			if (this->transducer_states()[at(id)].end_output.has_value())
			{
				return true;
			}
		}

		return false;
	}

	bool c_family_emitter::fragment::any_end_text(int first, int count) const noexcept
	{
		for (int id = first; id < first + count; ++id)
		{
			const auto& end_output = this->transducer_states()[at(id)].end_output;

			if (end_output.has_value() && !end_output->empty())
			{
				return true;
			}
		}

		return false;
	}

	/// Whether any output in the range reads the register, which mirrors `output_expressions`: a
	/// step reads it for any reference past the character in hand, and the finish function for
	/// any reference at all.
	bool c_family_emitter::fragment::any_reach_back(int first, int count, bool for_finish) const noexcept
	{
		for (int id = first; id < first + count; ++id)
		{
			const tx_state_model& state = this->transducer_states()[at(id)];

			if (for_finish)
			{
				if (state.end_output.has_value() && reaches_back(*state.end_output, true))
				{
					return true;
				}

				continue;
			}

			for (const auto& arc : state.arcs)
			{
				if (reaches_back(arc.output, false))
				{
					return true;
				}
			}
		}

		return false;
	}

	bool c_family_emitter::fragment::reaches_back(const std::string& output, bool for_finish) noexcept
	{
		for (std::size_t i = 0; i < output.size(); ++i)
		{
			if (output[i] != copy_marker)
			{
				continue;
			}

			const int depth = static_cast<int>(static_cast<unsigned char>(output[i + 1])) - tx_reference::depth_base;

			++i;

			if (depth > 0 || for_finish)
			{
				return true;
			}
		}

		return false;
	}

	// Expression helpers

	/// What passing this run adds to the total, or nothing where it adds nothing.
	std::optional<std::string> c_family_emitter::fragment::added_expression(std::uint64_t skipped, std::uint64_t count, char first, char last) const
	{
		if (first == last)
		{
			return skipped == 0 ? std::nullopt : std::optional<std::string>(this->emitter.literal(skipped));
		}

		// The accumulator is unsigned and the subtraction is an int, so the rank is converted
		// here rather than at every use.
		const std::string index = this->emitter.cast(this->emitter.uint64(), "c - " + char_literal(first));
		const std::string term = count == 1 ? index : this->emitter.literal(count) + " * " + index;

		return skipped == 0 ? term : this->emitter.literal(skipped) + " + " + term;
	}

	/// The character at position `index` within a set, as an expression over its runs.
	std::string c_family_emitter::fragment::character_expression(const std::vector<char_run>& runs) const
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
			result += "index < " + this->emitter.literal(cumulative) + " ? " + this->run_character(runs[i], offset) + " : ";
		}

		result += this->run_character(runs[runs.size() - 1], cumulative);

		return result;
	}

	std::string c_family_emitter::fragment::run_character(char_run run, std::uint64_t offset) const
	{
		if (run.first == run.last)
		{
			return char_literal(run.first);
		}

		const std::string index = offset == 0
			? this->emitter.cast("int", "index")
			: this->emitter.cast("int", "index - " + this->emitter.literal(offset));

		return this->emitter.cast("char", char_literal(run.first) + " + " + index);
	}
}
