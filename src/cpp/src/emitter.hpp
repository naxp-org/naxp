// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_EMITTER_HPP
#define NAXP_EMITTER_HPP

#include "naxp/naxp.hpp"

#include "ascii_char_set.hpp"
#include "code_writer.hpp"
#include "compiler.hpp"

#include <cstdint>
#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace logmu::detail
{
	/// An inclusive run of consecutive character codes.
	struct char_run
	{
		char first;
		char last;
	};

	/// A transition of a machine renumbered for generated code.
	struct arc_model
	{
		ascii_char_set set;
		int next;

		/// The string count of the state this arc reaches.
		std::uint64_t next_count;

		/// The count of strings sitting below this arc in its state's order.
		std::uint64_t skipped_before;
	};

	struct state_model
	{
		bool accepts_end;
		std::vector<arc_model> arcs;
	};

	struct tx_arc_model
	{
		ascii_char_set set;
		std::string output;
		int next;
	};

	struct tx_state_model
	{
		std::optional<std::string> end_output;
		std::vector<tx_arc_model> arcs;
	};

	/// What one emission call works from: the naxp's machines in the form generated code takes,
	/// the prefix, and the writer the fragment goes through.
	class emit_context
	{
	public:
		emit_context(const compilation& compiled, std::string prefix, naxp_value_type value_type, code_writer& writer);

		emit_context(const emit_context&) = delete;
		emit_context& operator=(const emit_context&) = delete;

		const compilation& compiled;

		/// The prefix every generated name starts with, possibly empty.
		std::string prefix;

		/// The integer type for encoded values, already validated against the largest one.
		naxp_value_type value_type;

		/// The writer the fragment goes through, configured for the language.
		code_writer& writer;

		/// The machine for the accepted language L.
		std::vector<state_model> accepted_states;

		/// The machine for the canonical language C, which encode and decode rank over.
		std::vector<state_model> canonical_states;

		/// The canonicalisation machine, empty where rho is the identity.
		std::vector<tx_state_model> transducer_states;

		/// Whether there is a canonicalisation machine, which is when rho is not the identity.
		bool canonicalises;

		/// The length of the longest canonical string, which bounds every buffer the generated
		/// code needs.
		int max_length;

		/// How many characters read the generated code has to keep, so that an output can reach
		/// back to one it has not yet placed.
		///
		/// Zero where there is no canonicalisation machine and one where every reference is to
		/// the character being read, which is the character the step already has. Only two or
		/// more needs a buffer, which is what `needs_register` reports.
		int register_depth;

		/// Whether the generated code has to keep characters beyond the one being read.
		///
		/// A depth of one is every reference standing for the character being read, which a step
		/// already holds. An end output is different: the finish function has no character, so a
		/// reference there needs the buffer whatever the depth.
		bool needs_register() const noexcept
		{
			return this->register_depth > 1 || this->end_output_reaches_back();
		}

	private:
		bool end_output_reaches_back() const noexcept;
	};

	/// The base of the language emitters, which turn a compiled naxp into recogniser and codec
	/// source in one language.
	///
	/// An emitter writes a fragment: function definitions and the constants they share, every
	/// name prefixed with the caller's prefix. The language paraphernalia around the fragment, a
	/// header comment, imports, a namespace or wrapping class, is the caller's job, which is what
	/// lets the same fragment land in a source generator's partial class and on a web page alike.
	///
	/// An instance holds only its language. Everything the naxp decides, the renumbered machines,
	/// the constants encode and decode fold their arithmetic into, the buffer bound, is computed
	/// per call into an `emit_context`, so one instance serves every naxp, concurrently. The
	/// derived emitters each keep such an instance.
	///
	/// States are renumbered breadth first from the start, so the start is state zero and an
	/// ordinary naxp's whole machine lands in the first chunk.
	class emitter
	{
	public:
		/// The most states one generated function may hold. A machine above this is emitted as
		/// functions of this many states behind a dispatcher.
		///
		/// One function holding 2 000 states would meet per-function limits: the .NET JIT stops
		/// optimising very large methods, and Java has a hard 64 KB of bytecode per method.
		/// Machines above this size only arise from long literal runs, whose cases are trivial,
		/// so the split costs an ordinary naxp nothing.
		static constexpr int chunk_size = 250;

		virtual ~emitter() = default;

		emitter(const emitter&) = delete;
		emitter& operator=(const emitter&) = delete;

		/// Emits a compiled naxp as a source fragment. The parameters are those of `naxp::emit`.
		///
		/// @throws std::invalid_argument The prefix is neither empty nor an ASCII identifier, or
		///     the largest encoded value does not fit the value type.
		std::string emit(
			const compilation& compiled,
			std::string_view prefix,
			naxp_value_type value_type,
			std::string_view initial_indent,
			std::string_view new_line,
			std::string_view indent) const;

		/// The largest encoded value a type can hold, remembering that zero is reserved.
		///
		/// @throws std::invalid_argument The type is not a value type.
		static std::uint64_t capacity(naxp_value_type value_type);

		/// Whether a name is an ASCII identifier: an ASCII letter or underscore, then ASCII
		/// letters, digits and underscores. ASCII rather than the target language's own rule,
		/// because one name feeds emitters in several languages and this is what they all accept.
		///
		/// @param reason Why not, where it is not.
		static bool try_validate_identifier(std::string_view name, std::string& reason);

		/// The naxp's pattern, made safe for a line comment.
		static std::string comment_text(std::string_view text);

	protected:
		/// Constructs an emitter over its language's block syntax, with the meanings
		/// `code_writer` gives the arguments. The defaults suit the brace languages.
		///
		/// Indentation and the newline are not here. A caller may reasonably want either, and no
		/// language is broken by the choice, so they are parameters of `emit` instead. Block
		/// syntax is not a taste: a caller cannot want C# with different braces.
		explicit emitter(std::optional<std::string> block_open = "{", std::optional<std::string> block_close = "}");

		/// Writes one naxp's fragment in the derived emitter's language.
		virtual void emit_fragment(emit_context& context) const = 0;

		// Shared helpers

		/// The set's characters as inclusive runs of consecutive codes, in ascending order.
		static std::vector<char_run> get_runs(const ascii_char_set& set);

		/// What separates groups of three digits in a numeric literal, or nothing where the
		/// language has no such thing.
		///
		/// An underscore in C#, JavaScript, Java and Python; an apostrophe in C++14; nothing at
		/// all in C, which never adopted a separator.
		virtual std::optional<std::string> digit_separator() const
		{
			return "_";
		}

		/// The digits of a value, in groups of three where the language has a separator for
		/// them, and without a type suffix.
		std::string grouped(std::uint64_t value) const;

		/// Whether any of a state's decode arcs picks a character by rank, wanting a local for it.
		static bool needs_index(const state_model& state) noexcept;

		// The shape of one language

		/// Writes a function's header and opens its body.
		///
		/// A language's whole function syntax sits behind this and `close_function`: C# writes a
		/// return type and a brace on a line of its own, JavaScript a keyword and a brace at the
		/// end of the line, and a Python emitter would write `def` and a colon and let the
		/// indenting do the rest. Every function these skeletons write returns the same thing, a
		/// state or a flag, so the return type belongs to the language rather than to the call.
		virtual void open_function(code_writer& writer, std::string_view name, std::string_view parameters) const = 0;

		/// Closes a body opened by `open_function`.
		virtual void close_function(code_writer& writer) const = 0;

		/// Opens the dispatch on the state, by whatever construct the language dispatches with.
		virtual void open_dispatch(code_writer& writer) const = 0;

		/// Writes the result for a state the dispatch does not name, and closes it.
		virtual void close_dispatch(code_writer& writer, std::string_view result) const = 0;

		/// Returns an expression, as one statement.
		virtual void write_return(code_writer& writer, std::string_view expression) const = 0;

		/// Returns an expression where a condition holds, on one line.
		virtual void write_guarded_return(code_writer& writer, std::string_view condition, std::string_view expression) const = 0;

		/// The test that the character in hand is one particular character.
		virtual std::string equals_character(char c) const = 0;

		/// The test that the character in hand lies within an inclusive run.
		virtual std::string within_run(char first, char last) const = 0;

		/// How the language spells 'or', which joins the tests of a set's runs.
		virtual std::string or_operator() const
		{
			return "||";
		}

		/// The test for one inclusive run, which is an equality where the run holds one character.
		std::string run_condition(char first, char last) const;

		/// Membership of a whole set, as a test over its runs.
		std::string set_condition(const ascii_char_set& set) const;

		// The stepper skeleton

		/// Emits a dispatch over states as one function, or over `chunk_size` states at a time as
		/// a dispatcher and one function per chunk.
		///
		/// @param writer Where the fragment is going.
		/// @param name The function name, which chunks suffix with their number.
		/// @param parameters The parameter list. The first parameter must be the state.
		/// @param arguments The same list as arguments, for the dispatcher to pass on.
		/// @param state_count The count of states.
		/// @param emit_case Writes one state's whole case, label included, or nothing to leave it
		///     to the default.
		/// @param preamble A declaration each function needs ahead of its dispatch, or nothing.
		/// @param preamble_needed Whether a state's case uses the preamble. A function holding no
		///     such state leaves the declaration out, since an unused local is a warning in some
		///     of the target languages. Null means every state does.
		/// @param default_result What a state the dispatch does not name returns.
		/// @param case_needed Whether a state writes a case at all. A function holding no such
		///     state has nothing to dispatch on, and writes only the default. Null means every
		///     state does.
		/// @param prologue Writes whatever the language needs at the top of a function's body,
		///     given the first state the function holds and how many, or null for nothing. C and
		///     C++ use it to say which parameters the range leaves unused, which their compilers
		///     otherwise warn of.
		void emit_step_functions(
			code_writer& writer,
			std::string_view name,
			std::string_view parameters,
			std::string_view arguments,
			int state_count,
			const std::function<void(int)>& emit_case,
			std::optional<std::string> preamble = std::nullopt,
			const std::function<bool(int)>& preamble_needed = nullptr,
			std::string_view default_result = "-1",
			const std::function<bool(int)>& case_needed = nullptr,
			const std::function<void(int, int)>& prologue = nullptr) const;

		/// How many functions a machine of this many states is emitted as: one up to
		/// `chunk_size`, and above that one per chunk, which the dispatcher then fronts.
		///
		/// Protected because C and C++ have to declare every function before its first use, so
		/// their prototypes need the chunk names ahead of the skeleton writing them.
		static int chunk_count(int state_count) noexcept
		{
			return state_count <= chunk_size ? 1 : ((state_count - 1) / chunk_size) + 1;
		}

	private:
		void emit_step_function(
			code_writer& writer,
			std::string_view name,
			std::string_view parameters,
			int first_state,
			int state_count,
			const std::function<void(int)>& emit_case,
			const std::optional<std::string>& preamble,
			const std::function<bool(int)>& preamble_needed,
			std::string_view default_result,
			const std::function<bool(int)>& case_needed,
			const std::function<void(int, int)>& prologue) const;

		std::optional<std::string> block_open;
		std::optional<std::string> block_close;
	};

	/// The decimal digits of a value, with no grouping.
	std::string decimal(std::uint64_t value);

	/// The decimal digits of a state number or a length.
	std::string decimal(int value);

	/// A value in upper case hexadecimal, zero padded to a width.
	std::string hex_upper(unsigned value, int width);
}

#endif
