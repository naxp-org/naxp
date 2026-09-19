// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "emitter.hpp"

#include "ascii_char_set.hpp"
#include "code_writer.hpp"
#include "compiler.hpp"
#include "state_map.hpp"
#include "tx.hpp"
#include "tx_machine.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>
#include <vector>

namespace logmu::detail
{
	namespace
	{
		bool is_ascii_letter(char c) noexcept
		{
			return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
		}

		/// Renumbers a machine breadth first from its start.
		///
		/// @param map The machine.
		/// @param with_counts Whether to carry each transition's string counts, which encode
		///     and decode fold into their constants. The accepted machine's counts may be
		///     saturated and nothing generated reads them, so it does not carry them.
		std::vector<state_model> build_machine(const state_map& map, bool with_counts)
		{
			std::unordered_map<const state*, int> id_of;
			std::vector<const state*> ordered{ map.start };
			std::deque<const state*> queue{ map.start };

			id_of.emplace(map.start, 0);

			while (!queue.empty())
			{
				const state* current = queue.front();
				queue.pop_front();

				for (const auto& transition : current->transitions)
				{
					if (transition.set.is_empty() || id_of.count(transition.next) != 0)
					{
						continue;
					}

					id_of.emplace(transition.next, static_cast<int>(ordered.size()));
					ordered.push_back(transition.next);
					queue.push_back(transition.next);
				}
			}

			std::vector<state_model> states;
			states.reserve(ordered.size());

			for (const state* current : ordered)
			{
				std::vector<arc_model> arcs;
				std::uint64_t skipped = 0;

				for (const auto& transition : current->transitions)
				{
					// The end of text transition sorts first, so where it exists every arc's
					// skipped count starts from the one value it stands for.
					if (transition.set.is_empty())
					{
						skipped = 1;
						continue;
					}

					const std::uint64_t count = with_counts ? transition.next->string_count : 0;

					arcs.push_back(arc_model{ transition.set, id_of[transition.next], count, skipped });
					skipped += count * static_cast<std::uint64_t>(transition.set.count());
				}

				states.push_back(state_model{ current->accepts_end_of_text(), std::move(arcs) });
			}

			return states;
		}

		std::vector<tx_state_model> build_transducer(const tx_machine& machine)
		{
			std::unordered_map<const tx_state*, int> id_of;
			std::vector<const tx_state*> ordered{ machine.start };
			std::deque<const tx_state*> queue{ machine.start };

			id_of.emplace(machine.start, 0);

			while (!queue.empty())
			{
				const tx_state* current = queue.front();
				queue.pop_front();

				for (const auto& transition : current->transitions)
				{
					if (id_of.count(transition.next) != 0)
					{
						continue;
					}

					id_of.emplace(transition.next, static_cast<int>(ordered.size()));
					ordered.push_back(transition.next);
					queue.push_back(transition.next);
				}
			}

			std::vector<tx_state_model> states;
			states.reserve(ordered.size());

			for (const tx_state* current : ordered)
			{
				std::vector<tx_arc_model> arcs;

				for (const auto& transition : current->transitions)
				{
					arcs.push_back(tx_arc_model{ transition.set, transition.output, id_of[transition.next] });
				}

				states.push_back(tx_state_model{ current->end_output, std::move(arcs) });
			}

			return states;
		}

		/// Whether any state in a function's range writes a case.
		bool any_case(int first_state, int state_count, const std::function<bool(int)>& case_needed)
		{
			if (case_needed == nullptr)
			{
				return true;
			}

			for (int id = first_state; id < first_state + state_count; ++id)
			{
				if (case_needed(id))
				{
					return true;
				}
			}

			return false;
		}

		bool needs_preamble(int first_state, int state_count, const std::function<bool(int)>& preamble_needed)
		{
			if (preamble_needed == nullptr)
			{
				return true;
			}

			for (int id = first_state; id < first_state + state_count; ++id)
			{
				if (preamble_needed(id))
				{
					return true;
				}
			}

			return false;
		}
	}

	std::string decimal(std::uint64_t value)
	{
		return std::to_string(value);
	}

	std::string decimal(int value)
	{
		return std::to_string(value);
	}

	std::string hex_upper(unsigned value, int width)
	{
		static const char digits[] = "0123456789ABCDEF";
		std::string result;

		for (; value != 0 || width > 0; value >>= 4, --width)
		{
			result.insert(result.begin(), digits[value & 0xF]);
		}

		return result;
	}

	// The per call context

	emit_context::emit_context(const compilation& compiled, std::string prefix, naxp_value_type value_type, code_writer& writer)
		: compiled(compiled)
		, prefix(std::move(prefix))
		, value_type(value_type)
		, writer(writer)
		, accepted_states()
		, canonical_states(build_machine(compiled.canonical(), true))
		, transducer_states()
		, canonicalises(compiled.canonical_machine() != nullptr)
		, max_length(static_cast<int>(compiled.max_length()))
		, register_depth(compiled.canonical_machine() == nullptr ? 0 : compiled.canonical_machine()->register_depth)
	{
		this->accepted_states = compiled.canonical_is_identity()
			? this->canonical_states
			: build_machine(compiled.accepted(), false);

		if (this->canonicalises)
		{
			this->transducer_states = build_transducer(*compiled.canonical_machine());
		}
	}

	bool emit_context::end_output_reaches_back() const noexcept
	{
		if (!this->canonicalises)
		{
			return false;
		}

		for (const auto& state : this->transducer_states)
		{
			if (state.end_output.has_value() && state.end_output->find(copy_marker) != std::string::npos)
			{
				return true;
			}
		}

		return false;
	}

	// Emitting

	emitter::emitter(std::optional<std::string> block_open, std::optional<std::string> block_close)
		: block_open(std::move(block_open))
		, block_close(std::move(block_close))
	{
	}

	std::string emitter::emit(
		const compilation& compiled,
		std::string_view prefix,
		naxp_value_type value_type,
		std::string_view initial_indent,
		std::string_view new_line,
		std::string_view indent) const
	{
		if (!prefix.empty())
		{
			std::string reason;

			if (!try_validate_identifier(prefix, reason))
			{
				throw std::invalid_argument(reason);
			}
		}

		if (compiled.max_encoded_value() > capacity(value_type))
		{
			throw std::invalid_argument(
				"This naxp encodes " + decimal(compiled.max_encoded_value()) + " values, which does not fit the value type.");
		}

		std::string fragment;
		code_writer writer(fragment, initial_indent, indent, this->block_open, this->block_close, new_line);
		emit_context context(compiled, std::string(prefix), value_type, writer);

		this->emit_fragment(context);

		return fragment;
	}

	std::uint64_t emitter::capacity(naxp_value_type value_type)
	{
		switch (value_type)
		{
			case naxp_value_type::int8: return 127;
			case naxp_value_type::uint8: return 255;
			case naxp_value_type::int16: return 32767;
			case naxp_value_type::uint16: return 65535;
			case naxp_value_type::int32: return 2147483647;
			case naxp_value_type::uint32: return 4294967295ULL;
			case naxp_value_type::int64: return 9223372036854775807ULL;
			case naxp_value_type::uint64: return 18446744073709551615ULL;
		}

		throw std::invalid_argument("The value type is not one of the naxp value types.");
	}

	// Shared helpers

	std::vector<char_run> emitter::get_runs(const ascii_char_set& set)
	{
		std::vector<char_run> runs;
		char first = 0;
		char previous = 0;
		bool open = false;

		for (const char c : set)
		{
			if (!open)
			{
				first = c;
				open = true;
			}
			else if (c != previous + 1)
			{
				runs.push_back(char_run{ first, previous });
				first = c;
			}

			previous = c;
		}

		if (open)
		{
			runs.push_back(char_run{ first, previous });
		}

		return runs;
	}

	std::string emitter::comment_text(std::string_view text)
	{
		std::string result;
		result.reserve(text.size());

		for (const char c : text)
		{
			result += static_cast<unsigned char>(c) < ' ' ? ' ' : c;
		}

		return result;
	}

	bool emitter::try_validate_identifier(std::string_view name, std::string& reason)
	{
		if (name.empty())
		{
			reason = "The name must not be empty.";

			return false;
		}

		if (!is_ascii_letter(name[0]) && name[0] != '_')
		{
			reason = "'" + std::string(name) + "' is not an ASCII identifier: it starts with '" + name[0] + "'.";

			return false;
		}

		for (const char c : name)
		{
			if (!is_ascii_letter(c) && (c < '0' || c > '9') && c != '_')
			{
				reason = "'" + std::string(name) + "' is not an ASCII identifier: it contains '" + c + "'.";

				return false;
			}
		}

		reason.clear();

		return true;
	}

	std::string emitter::grouped(std::uint64_t value) const
	{
		const std::string digits = decimal(value);
		const std::optional<std::string> separator = this->digit_separator();

		if (digits.size() <= 4 || !separator.has_value())
		{
			return digits;
		}

		std::string result;
		std::size_t leading = digits.size() % 3;

		if (leading == 0)
		{
			leading = 3;
		}

		result.append(digits, 0, leading);

		for (std::size_t i = leading; i < digits.size(); i += 3)
		{
			result += *separator;
			result.append(digits, i, 3);
		}

		return result;
	}

	bool emitter::needs_index(const state_model& state) noexcept
	{
		for (const auto& arc : state.arcs)
		{
			if (arc.set.count() > 1)
			{
				return true;
			}
		}

		return false;
	}

	// The shape of one language

	std::string emitter::run_condition(char first, char last) const
	{
		return first == last ? this->equals_character(first) : this->within_run(first, last);
	}

	std::string emitter::set_condition(const ascii_char_set& set) const
	{
		const std::vector<char_run> runs = get_runs(set);

		if (runs.size() == 1)
		{
			return this->run_condition(runs[0].first, runs[0].last);
		}

		std::string result;

		for (std::size_t i = 0; i < runs.size(); ++i)
		{
			if (i > 0)
			{
				result += ' ';
				result += this->or_operator();
				result += ' ';
			}

			// A run of more than one character is a conjunction in most languages, so it is
			// bracketed where it sits beside another test.
			result += runs[i].first == runs[i].last
				? this->equals_character(runs[i].first)
				: "(" + this->within_run(runs[i].first, runs[i].last) + ")";
		}

		return result;
	}

	// The stepper skeleton

	void emitter::emit_step_functions(
		code_writer& writer,
		std::string_view name,
		std::string_view parameters,
		std::string_view arguments,
		int state_count,
		const std::function<void(int)>& emit_case,
		std::optional<std::string> preamble,
		const std::function<bool(int)>& preamble_needed,
		std::string_view default_result,
		const std::function<bool(int)>& case_needed,
		const std::function<void(int, int)>& prologue) const
	{
		const int chunks = chunk_count(state_count);

		if (chunks == 1)
		{
			this->emit_step_function(
				writer, name, parameters, 0, state_count, emit_case, preamble, preamble_needed, default_result, case_needed, prologue);

			return;
		}

		const std::string call_suffix = "(" + std::string(arguments) + ")";

		this->open_function(writer, name, parameters);

		for (int chunk = 0; chunk < chunks - 1; ++chunk)
		{
			const std::string bound = decimal((chunk + 1) * chunk_size);

			this->write_guarded_return(writer, "state < " + bound, std::string(name) + decimal(chunk) + call_suffix);
		}

		writer.line();
		this->write_return(writer, std::string(name) + decimal(chunks - 1) + call_suffix);
		this->close_function(writer);

		for (int chunk = 0; chunk < chunks; ++chunk)
		{
			const int first = chunk * chunk_size;
			const int count = std::min(chunk_size, state_count - first);

			writer.line();
			this->emit_step_function(
				writer,
				std::string(name) + decimal(chunk),
				parameters,
				first,
				count,
				emit_case,
				preamble,
				preamble_needed,
				default_result,
				case_needed,
				prologue);
		}
	}

	void emitter::emit_step_function(
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
		const std::function<void(int, int)>& prologue) const
	{
		this->open_function(writer, name, parameters);

		if (prologue != nullptr)
		{
			prologue(first_state, state_count);
		}

		// A chunk of a machine where no state writes a case has nothing to dispatch on, and an
		// empty switch is both noise and, in C#, a warning. Only the finishing step can be in
		// that position, and only when chunked: its cases belong to the states where the input
		// may end, and those can all fall outside one chunk.
		if (!any_case(first_state, state_count, case_needed))
		{
			this->write_return(writer, default_result);
			this->close_function(writer);

			return;
		}

		if (preamble.has_value() && needs_preamble(first_state, state_count, preamble_needed))
		{
			writer.line(*preamble);
			writer.line();
		}

		this->open_dispatch(writer);

		for (int id = first_state; id < first_state + state_count; ++id)
		{
			emit_case(id);
		}

		this->close_dispatch(writer, default_result);
		this->close_function(writer);
	}
}
