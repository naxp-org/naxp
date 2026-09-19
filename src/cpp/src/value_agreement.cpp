// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "value_agreement.hpp"

#include "ascii_char_set.hpp"
#include "compiler.hpp"
#include "rank_agreement.hpp"
#include "rank_difference.hpp"
#include "rx.hpp"
#include "state_map.hpp"
#include "tx.hpp"

#include <cstddef>
#include <cstdint>
#include <deque>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <tuple>
#include <unordered_map>
#include <utility>
#include <vector>

namespace logmu::detail::value_agreement
{
	namespace
	{
		/// A product state: where each parse has got to, and where each canonical machine has
		/// got to on what that parse has emitted.
		///
		/// The two transductions come from different factories, so identity is by side and id
		/// rather than by pointer alone; the same holds of the two machines.
		struct key
		{
			const tx* left;
			const tx* right;
			const state* left_state;
			const state* right_state;

			bool operator==(const key& other) const noexcept
			{
				return this->left->id == other.left->id
					&& this->right->id == other.right->id
					&& this->left_state->id == other.left_state->id
					&& this->right_state->id == other.right_state->id;
			}
		};

		struct key_hash
		{
			std::size_t operator()(const key& item) const noexcept
			{
				return (((((static_cast<std::size_t>(item.left->id) * 31) ^ static_cast<std::size_t>(item.right->id)) * 31) ^ static_cast<std::size_t>(item.left_state->id)) * 31) ^ static_cast<std::size_t>(item.right_state->id);
			}
		};

		struct pair_hash
		{
			std::size_t operator()(const std::pair<int, int>& item) const noexcept
			{
				return (static_cast<std::size_t>(item.first) * 31) ^ static_cast<std::size_t>(item.second);
			}
		};

		struct conflict
		{
			std::size_t existing;
			std::size_t parent;
			char arrival;
		};

		bool copies(const tx_derivative& derivative) noexcept
		{
			for (const tx_move& move : derivative.moves)
			{
				if (move.emitted.find(copy_marker) != std::string::npos)
				{
					return true;
				}
			}

			return false;
		}

		/// Walks a concrete emission through a canonical machine, returning what it adds to the
		/// running total.
		std::uint64_t walk(const state* from, std::string_view emitted, const state*& to)
		{
			std::uint64_t added = 0;
			const state* current = from;

			for (const char c : emitted)
			{
				if (c == copy_marker)
				{
					throw std::logic_error("An undecided copy reached the walk.");
				}

				const transition* taken = nullptr;

				for (const transition& arc : current->transitions)
				{
					if (!arc.set.is_empty() && arc.set.contains(c))
					{
						taken = &arc;
						break;
					}
				}

				// A partial parse's output is a prefix of a canonical string, so it cannot fall
				// off the machine of the canonical language.
				if (taken == nullptr)
				{
					throw std::logic_error("A canonical form is not accepted by its own machine.");
				}

				added += rank_agreement::contribution(*current, *taken, c);
				current = taken->next;
			}

			to = current;

			return added;
		}

		/// Explores the product states reachable on a common input, reporting the first shared
		/// string the two value differently.
		class product
		{
		public:
			product(const compilation& a, const compilation& b, tx_factory& left_factory, tx_factory& right_factory, int max_tuples)
				: a(a)
				, b(b)
				, left_factory(left_factory)
				, right_factory(right_factory)
				, max_tuples(max_tuples)
			{
			}

			agreement run(const tx* left, const tx* right, std::optional<std::string>& witness)
			{
				witness.reset();

				std::deque<std::size_t> queue;
				queue.push_back(this->add(key{left, right, this->a.canonical().start, this->b.canonical().start}, rank_difference(), std::nullopt, '\0'));

				while (!queue.empty())
				{
					const std::size_t index = queue.front();
					queue.pop_front();

					const agreement ending = this->at_end_of_text(index);

					if (ending == agreement::differs)
					{
						witness = this->path(index);

						return agreement::differs;
					}

					if (ending == agreement::undecided)
					{
						return agreement::undecided;
					}

					const key current = this->keys[index];
					this->first_sets.clear();
					const std::vector<ascii_char_set>& left_sets = current.left->get_first_sets();
					const std::vector<ascii_char_set>& right_sets = current.right->get_first_sets();
					this->first_sets.insert(this->first_sets.end(), left_sets.begin(), left_sets.end());
					this->first_sets.insert(this->first_sets.end(), right_sets.begin(), right_sets.end());

					if (this->first_sets.empty())
					{
						continue;
					}

					state_map_builder::minterms(this->first_sets, this->blocks);

					// Copied so that narrowing, which re-enters the step, cannot disturb the
					// list being iterated.
					const std::vector<ascii_char_set> snapshot = this->blocks;

					for (const ascii_char_set block : snapshot)
					{
						if (!this->try_step(index, block, queue))
						{
							return agreement::undecided;
						}
					}
				}

				// A differing arrival matters only where the two residuals still share a string,
				// so that the differing totals are ever compared. Where they do, the two paths
				// through the state finish at different differences, so at least one of them is
				// a shared string the two value differently; but only one need be, and which is
				// not known without the rest of the walk, so each is asked the direct way.
				for (const conflict& item : this->conflicts)
				{
					const key current = this->keys[item.existing];
					const std::optional<std::string> completion = this->completion(current.left, current.right);

					if (!completion.has_value())
					{
						continue;
					}

					std::string first = this->path(item.existing) + *completion;
					std::string second = this->path(item.parent) + item.arrival + *completion;

					if (second.size() < first.size())
					{
						std::swap(first, second);
					}

					if (this->a.encode(first) != this->b.encode(first))
					{
						witness = first;
					}
					else if (this->a.encode(second) != this->b.encode(second))
					{
						witness = second;
					}
					else
					{
						throw std::logic_error("Two arrivals differed and neither is a witness.");
					}

					return agreement::differs;
				}

				return agreement::agrees;
			}

		private:
			/// Whether both sides can end the input here with different values.
			agreement at_end_of_text(std::size_t index) const
			{
				const key current = this->keys[index];

				if (!current.left->is_nullable || !current.right->is_nullable)
				{
					return agreement::agrees;
				}

				const eot& left = current.left->get_eot();
				const eot& right = current.right->get_eot();

				if (left.kind == eot_kind::too_long || right.kind == eot_kind::too_long)
				{
					return agreement::undecided;
				}

				if (left.kind != eot_kind::single || right.kind != eot_kind::single)
				{
					throw std::logic_error("A compiled naxp emits more than one thing at end of text.");
				}

				// Ending emits whatever the residual still owes, which the canonical machine has
				// to be walked over. The end of text transition itself contributes nothing on
				// either side, and the one that turns a total into a value cancels.
				const state* left_end = nullptr;
				const state* right_end = nullptr;
				const std::uint64_t left_added = walk(current.left_state, left.text, left_end);
				const std::uint64_t right_added = walk(current.right_state, right.text, right_end);

				if (!left_end->accepts_end_of_text() || !right_end->accepts_end_of_text())
				{
					throw std::logic_error("A canonical form is not accepted by its own machine.");
				}

				return this->differences[index].plus(left_added).minus(right_added).is_zero()
					? agreement::agrees
					: agreement::differs;
			}

			/// Takes one step of the input, narrowing the block to single characters where
			/// either side would copy one.
			///
			/// A copied character has to be known before it can be walked through a canonical
			/// machine, since the rank it contributes depends on which character it is. That is
			/// the only reason to narrow. The W3 checker can let two identical copies cancel
			/// because it compares them with each other; here they are walked through two
			/// different machines, so they cannot.
			bool try_step(std::size_t index, ascii_char_set block, std::deque<std::size_t>& queue)
			{
				const key current = this->keys[index];
				const tx_derivative& left = this->left_factory.derivative(current.left, block);
				const tx_derivative& right = this->right_factory.derivative(current.right, block);

				if (left.too_long || right.too_long)
				{
					return false;
				}

				if (left.skips_ambiguously || right.skips_ambiguously)
				{
					throw std::logic_error("A compiled naxp skips ambiguously.");
				}

				// One side cannot consume this block, so no shared string passes through it.
				if (left.moves.empty() || right.moves.empty())
				{
					return true;
				}

				if (!block.single_character().has_value() && (copies(left) || copies(right)))
				{
					for (const char c : block)
					{
						if (!this->try_step(index, ascii_char_set::single_character(c), queue))
						{
							return false;
						}
					}

					return true;
				}

				const char arrival = block.single_character().value_or(block.character_at(0));
				const rank_difference difference = this->differences[index];

				for (const tx_move& left_move : left.moves)
				{
					for (const tx_move& right_move : right.moves)
					{
						const state* left_state = nullptr;
						const state* right_state = nullptr;
						const std::uint64_t left_added = walk(current.left_state, left_move.emitted, left_state);
						const std::uint64_t right_added = walk(current.right_state, right_move.emitted, right_state);
						const rank_difference next = difference.plus(left_added).minus(right_added);
						const key next_key{left_move.residual, right_move.residual, left_state, right_state};

						if (const auto existing = this->index_of.find(next_key); existing != this->index_of.end())
						{
							if (this->differences[existing->second] != next)
							{
								this->conflicts.push_back(conflict{existing->second, index, arrival});
							}

							continue;
						}

						if (this->keys.size() >= static_cast<std::size_t>(this->max_tuples))
						{
							return false;
						}

						queue.push_back(this->add(next_key, next, index, arrival));
					}
				}

				return true;
			}

			std::size_t add(const key& item, rank_difference difference, std::optional<std::size_t> parent, char arrival)
			{
				const std::size_t index = this->keys.size();

				this->index_of.emplace(item, index);
				this->keys.push_back(item);
				this->differences.push_back(difference);
				this->parents.push_back(parent);
				this->arrivals.push_back(arrival);

				return index;
			}

			/// The input that reaches a state, read back along the path that found it.
			std::string path(std::size_t index) const
			{
				std::string builder;

				for (std::size_t at = index; this->parents[at].has_value(); at = *this->parents[at])
				{
					builder.insert(builder.begin(), this->arrivals[at]);
				}

				return builder;
			}

			/// A string both residuals accept, or nothing where there is none.
			///
			/// Existence needs no narrowing: what a block emits varies by character, but whether
			/// it is consumed does not.
			std::optional<std::string> completion(const tx* left, const tx* right)
			{
				const std::pair<int, int> pair(left->id, right->id);

				if (const auto known = this->completions.find(pair); known != this->completions.end())
				{
					return known->second;
				}

				// Nothing while the answer is being worked out, so a pair cannot depend on
				// itself.
				this->completions.emplace(pair, std::nullopt);

				std::optional<std::string> found;

				if (left->is_nullable && right->is_nullable)
				{
					found = std::string();
				}
				else
				{
					std::vector<ascii_char_set> sets;
					const std::vector<ascii_char_set>& left_sets = left->get_first_sets();
					const std::vector<ascii_char_set>& right_sets = right->get_first_sets();
					sets.insert(sets.end(), left_sets.begin(), left_sets.end());
					sets.insert(sets.end(), right_sets.begin(), right_sets.end());

					for (const ascii_char_set block : state_map_builder::minterms(sets))
					{
						const tx_derivative& left_derivative = this->left_factory.derivative(left, block);
						const tx_derivative& right_derivative = this->right_factory.derivative(right, block);

						if (left_derivative.moves.empty() || right_derivative.moves.empty())
						{
							continue;
						}

						for (const tx_move& left_move : left_derivative.moves)
						{
							for (const tx_move& right_move : right_derivative.moves)
							{
								const std::optional<std::string> rest = this->completion(left_move.residual, right_move.residual);

								if (rest.has_value())
								{
									found = block.character_at(0) + *rest;
									break;
								}
							}

							if (found.has_value())
							{
								break;
							}
						}

						if (found.has_value())
						{
							break;
						}
					}
				}

				this->completions[pair] = found;

				return found;
			}

			const compilation& a;
			const compilation& b;
			tx_factory& left_factory;
			tx_factory& right_factory;
			int max_tuples;
			std::unordered_map<key, std::size_t, key_hash> index_of;
			std::vector<key> keys;
			std::vector<rank_difference> differences;
			std::vector<std::optional<std::size_t>> parents;
			std::vector<char> arrivals;
			std::vector<conflict> conflicts;
			std::unordered_map<std::pair<int, int>, std::optional<std::string>, pair_hash> completions;
			std::vector<ascii_char_set> blocks;
			std::vector<ascii_char_set> first_sets;
		};
	}

	agreement compare(const compilation& a, const compilation& b, std::optional<std::string>& witness, int budget)
	{
		// Each side has its own factories. The sides are ordered and never compared for
		// identity, so there is nothing to gain from sharing and nothing to get wrong.
		rx_factory left_rxs;
		tx_factory left_factory(left_rxs);
		const tx* left = tx_converter::convert(a.tree(), left_factory, left_rxs);

		rx_factory right_rxs;
		tx_factory right_factory(right_rxs);
		const tx* right = tx_converter::convert(b.tree(), right_factory, right_rxs);

		return product(a, b, left_factory, right_factory, budget).run(left, right, witness);
	}
}
