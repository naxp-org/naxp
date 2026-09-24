// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_C_FAMILY_EMITTER_HPP
#define NAXP_C_FAMILY_EMITTER_HPP

#include "code_writer.hpp"
#include "emitter.hpp"

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace logmu::detail
{
	/// What the C and C++ emitters share: the steppers, the prototypes C and C++ both need ahead
	/// of a function's first use, and the snake_case names.
	///
	/// The two fragments differ at their public surface, where C takes a pointer and a length and
	/// C++ a `string_view`, and in spelling: a pointer against a reference, a C cast against
	/// `static_cast`, a block comment against a line comment, `static` against `inline`. The
	/// steppers, which are most of a fragment, are otherwise the same text, so they are written
	/// once here over those spellings and each language supplies its own.
	///
	/// Both compilers warn of a parameter a function never reads, and with `-Wextra -Werror` the
	/// warning is fatal, so every stepper starts by voiding whatever its range of states leaves
	/// unused: a literal run skips nothing, so its encode step never touches the total, and a
	/// function holding only such states would otherwise not compile clean. The predicates
	/// deciding that sit beside the case emitters they mirror.
	class c_family_emitter : public emitter
	{
	protected:
		c_family_emitter() = default;

		class fragment;

		void emit_fragment(emit_context& context) const override;

		// The shape of the family

		void open_function(code_writer& writer, std::string_view name, std::string_view parameters) const override;
		void close_function(code_writer& writer) const override;
		void open_dispatch(code_writer& writer) const override;
		void close_dispatch(code_writer& writer, std::string_view result) const override;
		void write_return(code_writer& writer, std::string_view expression) const override;
		void write_guarded_return(code_writer& writer, std::string_view condition, std::string_view expression) const override;
		std::string equals_character(char c) const override;
		std::string within_run(char first, char last) const override;

		// What each language spells

		/// Writes the fragment's opening comment and its two constants.
		virtual void emit_header(fragment& fragment) const = 0;

		/// Writes the public functions, which are the whole difference between the two languages.
		virtual void emit_publics(fragment& fragment) const = 0;

		/// Writes the function that puts the canonical form of text into a buffer of the longest
		/// length, which every public function needing a canonical form calls. It reads its text as
		/// the public functions do, which is the language's own business.
		virtual void emit_canonicalise(fragment& fragment) const = 0;

		/// How text is taken in: a pointer and a length in C, a `string_view` in C++.
		virtual std::string text_parameters() const = 0;

		/// The linkage a stepper is declared with: `static` in C, `inline` in C++.
		virtual std::string step_linkage() const = 0;

		/// What the fixed-width integer types are qualified with: nothing in C, `std::` in C++.
		virtual std::string type_prefix() const = 0;

		/// A pointer parameter, which the two languages space differently.
		virtual std::string pointer(const std::string& type, const std::string& name) const = 0;

		/// A parameter the stepper writes through: a pointer in C, a reference in C++.
		virtual std::string by_reference(const std::string& type, const std::string& name) const = 0;

		/// Reading or writing a `by_reference` parameter.
		virtual std::string dereference(const std::string& name) const = 0;

		/// Post-incrementing a `by_reference` parameter, as an array index.
		virtual std::string increment(const std::string& name) const = 0;

		/// Passing a local on to a `by_reference` parameter.
		virtual std::string address_of(const std::string& name) const = 0;

		/// An explicit conversion of an expression, which the language brackets as it needs.
		virtual std::string cast(const std::string& type, const std::string& expression) const = 0;

		/// A one-line comment.
		virtual void comment(code_writer& writer, const std::string& text) const = 0;

		/// The 64-bit unsigned type the steppers work in.
		std::string uint64() const
		{
			return this->type_prefix() + "uint64_t";
		}

		// Text helpers

		/// The prefix and a member name, in snake_case. An underscore goes in wherever the case
		/// turns upward and the previous character was a letter or digit, and before an upper
		/// case letter that starts a new word after a run of them, so `UKPostcode` gives
		/// `uk_postcode`; a prefix already in snake_case comes through as it is.
		static std::string snake(const std::string& prefix, const std::string& member);

		/// An ASCII character as a literal, with the two that need it escaped and the unprintable
		/// ones in hexadecimal. A hexadecimal escape runs to the closing quote, so it is safe here
		/// where it would not be inside a string.
		static std::string char_literal(char c);

		/// A string as a literal. A pattern may hold whitespace other than the space, which is
		/// escaped along with the quote and the backslash, and a question mark after another is
		/// escaped too, so that no pair of them starts a trigraph where a compiler still reads
		/// trigraphs.
		static std::string string_literal(const std::string& text);

		/// An unsigned 64-bit literal, grouped where the language has a separator.
		std::string literal(std::uint64_t value) const
		{
			return this->grouped(value) + "ULL";
		}

		/// Whether an identifier needs no brackets under a C cast.
		static bool is_identifier(const std::string& expression) noexcept;
	};

	/// One emission call's state: the context, the generated names, the value type's spelling,
	/// and the steppers, which both languages write through this. Per call so the shared instance
	/// stays stateless.
	class c_family_emitter::fragment
	{
	public:
		/// The name the generated code keeps the characters it has read under.
		static const std::string held_name;

		fragment(const c_family_emitter& emitter, emit_context& context);

		fragment(const fragment&) = delete;
		fragment& operator=(const fragment&) = delete;

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

		int max_length() const
		{
			return this->context.max_length;
		}

		/// The naxp the fragment is generated from.
		const std::string& pattern() const
		{
			return this->context.compiled.pattern();
		}

		int register_depth() const
		{
			return this->context.register_depth;
		}

		bool needs_register() const
		{
			return this->context.needs_register();
		}

		bool canonicalises() const
		{
			return this->context.canonicalises;
		}

		/// The prefix in the language's own casing, for the public names each language adds.
		std::string name(const std::string& member) const
		{
			return snake(this->context.prefix, member);
		}

		// The generated names, each the prefix plus the bare member name in snake_case.
		std::string pattern_name;
		std::string max_encoded_value_name;
		std::string max_length_name;
		std::string accepts_name;
		std::string encode_name;
		std::string decode_name;
		std::string canonical_form_name;
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

		// The spelling of the chosen value type. The steppers work in the 64-bit unsigned type
		// throughout whatever the choice; only the public boundary changes.
		std::string value_keyword;
		std::string max_encoded_value_literal;
		std::string value_zero;
		std::string value_one;
		bool value_is_widest;
		std::string decode_core_argument;

		/// What sizes a character buffer. The longest string's length, except that a zero-length
		/// array is illegal in both languages, so a naxp whose only string is empty gets one byte
		/// that nothing writes.
		std::string buffer_size() const
		{
			return this->max_length() == 0 ? "1" : this->max_length_name;
		}

		/// The largest encoded value in plain digits, for a message.
		std::string max_encoded_value_digits() const
		{
			return decimal(this->context.compiled.max_encoded_value());
		}

		/// The arguments the canonicalising step takes after its character, as a public function
		/// passes them.
		std::string step_arguments(const std::string& length_argument) const
		{
			return this->needs_register() ? held_name + ", canonical, " + length_argument : "canonical, " + length_argument;
		}

		/// The arguments the finishing step takes after its state.
		std::string finish_arguments() const
		{
			return this->needs_register() ? held_name + ", canonical, length" : "canonical, length";
		}

		/// Declares every stepper ahead of the public functions that call it. Both languages need
		/// a function declared before its first use, and the public functions come first because
		/// they are what a reader is looking for.
		void emit_prototypes();

		/// Opens the function that canonicalises, with its comment, which both languages share.
		void open_canonicalise();

		/// Declares the register, where the naxp needs one, ahead of the loop that reads the text.
		///
		/// @param zeroed How an array is written zeroed: `{ 0 }` in C and `{}` in C++.
		void declare_register(const std::string& zeroed);

		/// Keeps the character just read, where the naxp needs a register.
		///
		/// @param character The character, as a `char`.
		void keep_character(const std::string& character);

		void emit_steppers();

	private:
		// The steppers' signatures. Each parameter list is written once here and read by the
		// prototype and the definition alike, so the two cannot drift.
		std::string canonicalise_parameters() const;
		std::string rank_parameters() const;
		std::string decode_core_parameters() const;
		std::string accept_step_parameters() const;
		std::string encode_step_parameters() const;
		std::string decode_step_parameters() const;
		std::string held_parameter() const;
		std::string canonical_step_parameters() const;
		std::string finish_canonical_parameters() const;

		void emit_step_prototypes(const std::string& name, const std::string& parameters, int state_count);
		void emit_accept_case(int id);
		void emit_encode_case(int id);
		void emit_decode_case(int id);
		void emit_decode_arc(const arc_model& arc);
		void emit_canonical_case(int id);
		void emit_finish_case(int id);
		bool finish_case_needed(int id) const;
		std::vector<std::string> output_expressions(const std::string& output, bool for_finish) const;
		void emit_accepting_predicate(const std::string& name, const std::vector<state_model>& states);

		// What a range of states leaves unused
		void accept_prologue(int first, int count);
		void encode_prologue(int first, int count);
		void decode_prologue(int first, int count);
		void canonical_prologue(int first, int count);
		void finish_prologue(int first, int count);
		void mark_unused(const std::vector<std::pair<std::string, bool>>& parameters);
		static bool any_arcs(const std::vector<state_model>& states, int first, int count) noexcept;
		bool any_addition(int first, int count) const noexcept;
		bool any_remaining(int first, int count) const noexcept;
		bool any_transducer_arcs(int first, int count) const noexcept;
		bool any_output(int first, int count) const noexcept;
		bool any_end_output(int first, int count) const noexcept;
		bool any_end_text(int first, int count) const noexcept;
		bool any_reach_back(int first, int count, bool for_finish) const noexcept;
		static bool reaches_back(const std::string& output, bool for_finish) noexcept;

		// Expression helpers
		std::optional<std::string> added_expression(std::uint64_t skipped, std::uint64_t count, char first, char last) const;
		std::string character_expression(const std::vector<char_run>& runs) const;
		std::string run_character(char_run run, std::uint64_t offset) const;

		const c_family_emitter& emitter;
		emit_context& context;
	};
}

#endif
