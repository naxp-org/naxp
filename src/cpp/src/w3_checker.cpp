// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "w3_checker.hpp"

#include "ascii_char_set.hpp"
#include "ast.hpp"
#include "fault.hpp"
#include "naxp_message.hpp"
#include "rx.hpp"
#include "state_map.hpp"
#include "tx.hpp"

#include <cstddef>
#include <deque>
#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>
#include <vector>

namespace logmu::detail::w3_checker
{
	namespace
	{
		/// How far one branch's output runs ahead of the other's.
		///
		/// At most one side is non-empty, because a common prefix is committed at every step.
		/// Where both would be non-empty the outputs disagree at a position that is already
		/// fixed, so the delay collapses to the mismatch mark and the strings stop mattering.
		struct delay
		{
			/// The two outputs already differ and can never agree again.
			bool is_mismatch = false;

			/// What the first branch has emitted beyond the second.
			std::string left;

			/// What the second branch has emitted beyond the first.
			std::string right;

			static delay mismatch()
			{
				return delay{true, {}, {}};
			}

			static delay none()
			{
				return delay{};
			}

			/// The delay after both branches have emitted, with their common prefix committed.
			static delay after(const delay& current, std::string_view left_emitted, std::string_view right_emitted)
			{
				if (current.is_mismatch)
				{
					return mismatch();
				}

				const std::string left = current.left + std::string(left_emitted);
				const std::string right = current.right + std::string(right_emitted);

				std::size_t common = 0;

				while (common < left.size() && common < right.size() && left[common] == right[common])
				{
					++common;
				}

				// One of them is exhausted, or they differ here and will differ forever.
				if (common < left.size() && common < right.size())
				{
					return mismatch();
				}

				return delay{false, left.substr(common), right.substr(common)};
			}

			delay swapped() const
			{
				return this->is_mismatch ? mismatch() : delay{false, this->right, this->left};
			}

			bool operator==(const delay& other) const noexcept
			{
				return this->is_mismatch == other.is_mismatch && this->left == other.left && this->right == other.right;
			}

			std::size_t hash() const noexcept
			{
				return this->is_mismatch
					? 0
					: (std::hash<std::string>()(this->left) * 31) ^ std::hash<std::string>()(this->right);
			}
		};

		/// A pair of live branches and the delay between them, as an unordered pair.
		struct pair_key
		{
			const tx* left;
			const tx* right;
			delay lag;

			pair_key(const tx* first, const tx* second, delay between)
			{
				// The pair is unordered, so one orientation is chosen and the delay follows it.
				if (first->id <= second->id)
				{
					this->left = first;
					this->right = second;
					this->lag = std::move(between);
				}
				else
				{
					this->left = second;
					this->right = first;
					this->lag = between.swapped();
				}
			}

			bool operator==(const pair_key& other) const noexcept
			{
				return this->left == other.left && this->right == other.right && this->lag == other.lag;
			}
		};

		struct pair_key_hash
		{
			std::size_t operator()(const pair_key& key) const noexcept
			{
				return (((static_cast<std::size_t>(key.left->id) * 31) ^ static_cast<std::size_t>(key.right->id)) * 31) ^ key.lag.hash();
			}
		};

		/// The decision was abandoned because an intermediate result grew too large, which is a
		/// different thing from running out of pair states and must not claim to be that.
		fault abandoned()
		{
			return fault(naxp_message::pair_output_abandoned);
		}

		fault too_large()
		{
			return fault(naxp_message::too_many_pair_states);
		}

		fault violation(std::string witness)
		{
			return fault(naxp_message::unification_not_single_valued_witness, std::move(witness));
		}

		/// Explores the pairs reachable on a common input, reporting the first that can accept
		/// with two different outputs.
		class square
		{
		public:
			square(tx_factory& factory, int max_states)
				: factory(factory)
				, max_states(max_states)
			{
			}

			bool try_run(const tx* root, std::optional<fault>& error)
			{
				const std::size_t start = this->add(pair_key(root, root, delay::none()), std::nullopt, '\0');
				std::deque<std::size_t> queue{start};

				while (!queue.empty())
				{
					const std::size_t index = queue.front();
					queue.pop_front();

					// Copied, because stepping adds states and would move the vector's storage.
					const pair_key state = this->states[index];
					bool eot_too_long = false;

					if (this->accepts(state, eot_too_long))
					{
						error = violation(this->witness(index));

						return false;
					}

					if (eot_too_long)
					{
						error = abandoned();

						return false;
					}

					for (const ascii_char_set block : this->blocks(state))
					{
						if (!this->try_step(state, index, block, queue, error))
						{
							return false;
						}
					}
				}

				error.reset();

				return true;
			}

		private:
			/// Takes one step of the input, narrowing the block to single characters where what
			/// is emitted would otherwise stay undecided.
			bool try_step(const pair_key& state, std::size_t index, ascii_char_set block, std::deque<std::size_t>& queue, std::optional<fault>& error)
			{
				const tx_derivative& left = this->factory.derivative(state.left, block);
				const tx_derivative& right = this->factory.derivative(state.right, block);

				if (left.too_long || right.too_long)
				{
					error = abandoned();

					return false;
				}

				// Before the test for no moves, which an ambiguous skip can itself cause: the
				// moves past it are dropped because the verdict no longer depends on them.
				if (left.skips_ambiguously || right.skips_ambiguously)
				{
					error = violation(this->witness(index) + block.character_at(0));

					return false;
				}

				if (left.moves.empty() || right.moves.empty())
				{
					// One side cannot consume this block, so there is no pair to follow. The
					// other side's own future is covered by its diagonal pair.
					error.reset();

					return true;
				}

				if (needs_narrowing(state, left, right))
				{
					for (const char c : block)
					{
						if (!this->try_step(state, index, ascii_char_set::single_character(c), queue, error))
						{
							return false;
						}
					}

					error.reset();

					return true;
				}

				const char arrival = block.single_character().value_or(block.character_at(0));

				for (const tx_move& left_move : left.moves)
				{
					for (const tx_move& right_move : right.moves)
					{
						pair_key next(
							left_move.residual,
							right_move.residual,
							delay::after(state.lag, left_move.emitted, right_move.emitted));

						if (this->index_of.find(next) != this->index_of.end())
						{
							continue;
						}

						if (this->states.size() >= static_cast<std::size_t>(this->max_states))
						{
							error = too_large();

							return false;
						}

						queue.push_back(this->add(std::move(next), index, arrival));
					}
				}

				error.reset();

				return true;
			}

			/// Whether this step has to be retried one character at a time.
			///
			/// A character set emits the character read. Where the block holds more than one
			/// character that emission is not yet a known string, and comparing it against a
			/// rendering, or against a character copied at some other position, has no answer
			/// until the character is fixed. The one case that needs no narrowing is the common
			/// one: both branches emit the very same thing at the same step from equal delays,
			/// which cancels whatever the character turns out to be.
			static bool needs_narrowing(const pair_key& state, const tx_derivative& left, const tx_derivative& right)
			{
				const bool no_delay = state.lag == delay::none();

				for (const tx_move& left_move : left.moves)
				{
					for (const tx_move& right_move : right.moves)
					{
						const bool undecided = left_move.emitted.find(copy_marker) != std::string::npos
							|| right_move.emitted.find(copy_marker) != std::string::npos;

						if (!undecided)
						{
							continue;
						}

						// Identical emissions from one step cancel exactly, whatever was read,
						// but only where there is no earlier delay to shift one against the
						// other.
						if (no_delay && left_move.emitted == right_move.emitted)
						{
							continue;
						}

						return true;
					}
				}

				return false;
			}

			/// Whether both branches can accept here, with different outputs.
			static bool accepts(const pair_key& state, bool& eot_too_long)
			{
				eot_too_long = false;

				if (!state.left->is_nullable || !state.right->is_nullable)
				{
					return false;
				}

				const eot& left = state.left->get_eot();
				const eot& right = state.right->get_eot();

				if (left.kind == eot_kind::too_long || right.kind == eot_kind::too_long)
				{
					eot_too_long = true;

					return false;
				}

				// One residual with two end of text outputs is a violation on its own, which is
				// how a naxp such as 'A!?|A!!' is caught before any character is read.
				if (left.kind == eot_kind::multiple || right.kind == eot_kind::multiple)
				{
					return true;
				}

				// Both can accept, and their outputs already differ.
				if (state.lag.is_mismatch)
				{
					return true;
				}

				return state.lag.left + left.text != state.lag.right + right.text;
			}

			/// The blocks to step by: the minterms of both branches' first sets, refined so that
			/// every character appearing in a rendering stands alone.
			std::vector<ascii_char_set> blocks(const pair_key& state) const
			{
				std::vector<ascii_char_set> sets;
				const std::vector<ascii_char_set>& left_sets = state.left->get_first_sets();
				const std::vector<ascii_char_set>& right_sets = state.right->get_first_sets();
				sets.insert(sets.end(), left_sets.begin(), left_sets.end());
				sets.insert(sets.end(), right_sets.begin(), right_sets.end());

				if (sets.empty())
				{
					return {};
				}

				ascii_char_set universe;

				for (const ascii_char_set set : sets)
				{
					universe = universe | set;
				}

				for (const char c : this->factory.rendering_characters())
				{
					if (universe.contains(c))
					{
						sets.push_back(ascii_char_set::single_character(c));
					}
				}

				return state_map_builder::minterms(sets);
			}

			std::size_t add(pair_key key, std::optional<std::size_t> parent, char arrival)
			{
				const std::size_t index = this->states.size();

				this->index_of.emplace(key, index);
				this->states.push_back(std::move(key));
				this->parents.push_back(parent);
				this->arrivals.push_back(arrival);

				return index;
			}

			/// The input that reaches a state, read back along the path that found it.
			std::string witness(std::size_t index) const
			{
				std::string builder;

				for (std::size_t at = index; this->parents[at].has_value(); at = *this->parents[at])
				{
					builder.insert(builder.begin(), this->arrivals[at]);
				}

				return builder;
			}

			tx_factory& factory;
			int max_states;
			std::unordered_map<pair_key, std::size_t, pair_key_hash> index_of;
			std::vector<pair_key> states;
			std::vector<std::optional<std::size_t>> parents;
			std::vector<char> arrivals;
		};
	}

	bool try_check(const ast& tree, rx_factory& rxs, std::optional<fault>& error, int max_states)
	{
		// Without a '!' the transduction is the identity, which is single valued for nothing.
		if (!ast::contains_unified(tree))
		{
			error.reset();

			return true;
		}

		tx_factory factory(rxs);
		const tx* root = tx_converter::convert(tree, factory, rxs);

		return try_check(root, factory, error, max_states);
	}

	bool try_check(const tx* root, tx_factory& factory, std::optional<fault>& error, int max_states)
	{
		return square(factory, max_states).try_run(root, error);
	}
}
