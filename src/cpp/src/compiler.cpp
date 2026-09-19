// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "compiler.hpp"

#include "ast.hpp"
#include "codec.hpp"
#include "fault.hpp"
#include "naxp_limits.hpp"
#include "naxp_message.hpp"
#include "parser.hpp"
#include "rx.hpp"
#include "rx_converter.hpp"
#include "state_map.hpp"
#include "tx.hpp"
#include "tx_machine.hpp"
#include "w3_checker.hpp"
#include "well_formedness.hpp"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace logmu::detail
{
	namespace
	{
		/// The length of the longest string a machine generates.
		///
		/// The states are listed in creation order and every transition points at an earlier
		/// state, because the builder interns each state's successors before the state itself,
		/// so a single pass has every target's length ready when it is read.
		std::size_t longest_path(const state_map& map)
		{
			std::vector<std::size_t> lengths(map.states.size(), 0);

			for (std::size_t id = 0; id < map.states.size(); ++id)
			{
				std::size_t longest = 0;

				for (const transition& arc : map.states[id]->transitions)
				{
					if (arc.set.is_empty())
					{
						continue;
					}

					const std::size_t via_transition = lengths[static_cast<std::size_t>(arc.next->id)] + 1;

					if (via_transition > longest)
					{
						longest = via_transition;
					}
				}

				lengths[id] = longest;
			}

			return lengths[static_cast<std::size_t>(map.start->id)];
		}
	}

	compilation::compilation(
		std::string pattern,
		ast_ptr tree,
		std::unique_ptr<state_map> accepted,
		std::unique_ptr<state_map> canonical,
		std::unique_ptr<tx_machine> canonical_machine)
		: pattern_text(std::move(pattern))
		, syntax_tree(std::move(tree))
		, accepted_map(std::move(accepted))
		, canonical_map(std::move(canonical))
		, rho_machine(std::move(canonical_machine))
		, longest(longest_path(*this->canonical_map))
	{
	}

	std::uint64_t compilation::encode(std::string_view text) const
	{
		if (this->canonical_is_identity())
		{
			return codec::encode(*this->canonical_map, text);
		}

		std::string canonical;

		return this->try_get_canonical_form(text, canonical)
			? codec::encode(*this->canonical_map, canonical)
			: 0;
	}

	bool compilation::try_decode(std::uint64_t value, std::string& text) const
	{
		return codec::try_decode(*this->canonical_map, value, text);
	}

	bool compilation::try_get_canonical_form(std::string_view text, std::string& canonical) const
	{
		// Where rho is the identity an accepted string is its own canonical form, so the answer
		// is the machine's, and walking the tree for it would only rebuild what was passed in.
		if (this->canonical_is_identity())
		{
			if (!this->accepts(text))
			{
				return false;
			}

			canonical = std::string(text);

			return true;
		}

		// The machine is linear in the length of the input where a tree walk is not, and it is
		// the form the emitters need, so it is the one the runtime uses.
		return this->rho_machine->try_canonicalise(text, canonical);
	}

	namespace compiler
	{
		bool try_compile(std::string_view pattern, std::unique_ptr<compilation>& result, std::optional<fault>& error)
		{
			result = nullptr;

			ast_ptr tree;

			if (!try_parse(pattern, tree, error))
			{
				return false;
			}

			if (!well_formedness::try_check(*tree, error))
			{
				return false;
			}

			// One factory across both languages and the W3 check, so the shared sub-expressions
			// and their derivatives are computed once.
			rx_factory factory;

			// Everything below turns on this, so the tree is walked for it once.
			const bool has_unified = ast::contains_unified(*tree);

			// The transduction is wanted twice, by the W3 check and then by the machine that
			// canonicalises, so it is converted once and both are given it.
			std::unique_ptr<tx_factory> transductions;
			const tx* root = nullptr;

			if (has_unified)
			{
				transductions = std::make_unique<tx_factory>(factory);
				root = tx_converter::convert(*tree, *transductions, factory);

				// Before the machines, because a naxp that breaks W3 has no well defined
				// encoding and building its machines would say nothing about that.
				if (!w3_checker::try_check(root, *transductions, error))
				{
					return false;
				}
			}

			const rx* canonical_expression = rx_converter::convert(*tree, factory, true);
			std::unique_ptr<state_map> canonical;

			if (!state_map_builder::try_build(canonical_expression, factory, canonical, error))
			{
				return false;
			}

			if (canonical->count_saturated)
			{
				error = fault(naxp_message::too_many_values);

				return false;
			}

			// The accepted language can legitimately be larger than the canonical one, and W5
			// says nothing about it, so its count is allowed to saturate. A unified element is
			// the only node the converter reads the language at, so without one the two
			// conversions would give the same machine and the canonical one serves as both.
			std::unique_ptr<state_map> accepted;

			if (has_unified)
			{
				const rx* accepted_expression = rx_converter::convert(*tree, factory, false);

				if (!state_map_builder::try_build(accepted_expression, factory, accepted, error))
				{
					return false;
				}
			}

			// Last, because it is the part of W6 a naxp can fail after passing every other rule,
			// and the cheaper checks should come first; see limits::max_canonical_states.
			std::unique_ptr<tx_machine> canonical_machine;

			if (has_unified && !tx_machine_builder::try_build(root, *transductions, canonical_machine, error, limits::max_canonical_states))
			{
				return false;
			}

			result = std::make_unique<compilation>(
				std::string(pattern),
				std::move(tree),
				std::move(accepted),
				std::move(canonical),
				std::move(canonical_machine));

			error.reset();

			return true;
		}
	}
}
