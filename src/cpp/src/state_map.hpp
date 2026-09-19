// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_STATE_MAP_HPP
#define NAXP_STATE_MAP_HPP

#include "array_view.hpp"
#include "ascii_char_set.hpp"
#include "fault.hpp"
#include "naxp_limits.hpp"
#include "rx.hpp"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <string_view>
#include <vector>

namespace logmu::detail
{
	class state;

	/// A character set paired with the state reached by consuming one of its characters.
	///
	/// An empty set is the end of text transition. The empty set is least in the set order, so
	/// it sorts first and needs no special handling.
	struct transition
	{
		ascii_char_set set;
		const state* next;
	};

	/// A state of the machine, which stands for a language.
	///
	/// States are shared: two languages that are equal give the same object. That is what makes
	/// the machine the minimal one and the encoding a property of the language rather than of
	/// the spelling.
	class state
	{
	public:
		state(int id, std::vector<transition> transitions, std::uint64_t string_count)
			: id(id)
			, transitions(std::move(transitions))
			, string_count(string_count)
		{
		}

		state(const state&) = delete;
		state& operator=(const state&) = delete;

		int id;

		/// The transitions, sorted by the set order, end of text first where present.
		std::vector<transition> transitions;

		/// The count of strings the state's language holds, saturated at 2^64 - 1.
		std::uint64_t string_count;

		/// Whether this is the terminal state, whose language is the empty string alone.
		bool is_terminal() const noexcept
		{
			return this->transitions.empty();
		}

		/// Whether the language holds the empty string.
		bool accepts_end_of_text() const noexcept
		{
			return this->is_terminal() || this->transitions[0].set.is_empty();
		}
	};

	/// The machine for one of a naxp's languages. It owns its states.
	class state_map
	{
	public:
		state_map(const state* start, std::vector<std::unique_ptr<state>> states, bool count_saturated)
			: start(start)
			, states(std::move(states))
			, count_saturated(count_saturated)
		{
		}

		state_map(const state_map&) = delete;
		state_map& operator=(const state_map&) = delete;

		const state* start;

		/// Every state, in creation order, which puts each state after the states it points at.
		std::vector<std::unique_ptr<state>> states;

		/// Whether the true count exceeds 2^64 - 1, in which case `string_count()` is that
		/// limit rather than the count.
		bool count_saturated;

		/// The size of the language, saturated at 2^64 - 1.
		std::uint64_t string_count() const noexcept
		{
			return this->start->string_count;
		}

		/// Whether this machine's language holds the specified string.
		///
		/// One transition per character and no allocation. A string longer than any the machine
		/// generates runs out of transitions and is invalid, so no length guard is needed.
		bool accepts(std::string_view text) const noexcept;
	};

	/// Builds the machine the specification defines, by symbolic derivatives.
	///
	/// The specification defines a state as a language, with one transition per first class.
	/// The minterms of the first sets refine those classes rather than equalling them, so the
	/// classes are recovered afterwards by merging transitions that reach the same state. Where
	/// `[AB]C|[BC]C` gives minterms `[A]`, `[B]` and `[C]`, all three have the derivative `C`,
	/// and the merge recombines them into the single class `[ABC]`.
	///
	/// Nothing here recurses over the machine. States are built in order of the longest string
	/// remaining, which strictly decreases along every derivative, so each state's successors
	/// are already built when it is reached. A long chain of states would otherwise want nine
	/// thousand stack frames.
	namespace state_map_builder
	{
		/// Builds the machine for an expression.
		///
		/// @param start The expression, as produced by the converter.
		/// @param factory The factory that made it, reused so derivatives stay interned.
		/// @param map The machine, or null if it was invalid.
		/// @param error The fault, or nothing.
		/// @param max_states The budget, lowered by tests so the cap can be reached cheaply.
		/// @returns Whether the machine was built.
		bool try_build(const rx* start, rx_factory& factory, std::unique_ptr<state_map>& map, std::optional<fault>& error, int max_states = limits::max_states);

		/// Splits the characters covered by `sets` into the coarsest blocks that each set is a
		/// union of.
		///
		/// @param sets The sets to separate.
		/// @param blocks The working list, cleared first and left holding the blocks. The
		///     caller owns it so that a builder can reuse one list across every state instead
		///     of allocating per state. It must not alias `sets`, which the clearing would
		///     destroy.
		void minterms(array_view<ascii_char_set> sets, std::vector<ascii_char_set>& blocks);

		/// Splits the characters covered by `sets` into the coarsest blocks that each set is a
		/// union of, in a list of its own.
		std::vector<ascii_char_set> minterms(array_view<ascii_char_set> sets);
	}
}

#endif
