// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "array_view.hpp"
#include "state_map.hpp"

#include "ascii_char_set.hpp"
#include "fault.hpp"
#include "naxp_limits.hpp"
#include "naxp_message.hpp"
#include "rx.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <memory>
#include <optional>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace logmu::detail
{
	bool state_map::accepts(std::string_view text) const noexcept
	{
		const state* current = this->start;

		for (const char c : text)
		{
			const state* next = nullptr;

			for (const transition& arc : current->transitions)
			{
				if (arc.set.contains(c))
				{
					next = arc.next;
					break;
				}
			}

			if (next == nullptr)
			{
				return false;
			}

			current = next;
		}

		return current->accepts_end_of_text();
	}

	namespace state_map_builder
	{
		namespace
		{
			struct edge
			{
				ascii_char_set set;
				const rx* derivative;
			};

			/// The identity of a state, which is its transition list and nothing else. Two
			/// states are equal when their transitions are, which by induction means their
			/// languages are.
			struct state_key
			{
				std::vector<transition> transitions;

				bool operator==(const state_key& other) const noexcept
				{
					if (this->transitions.size() != other.transitions.size())
					{
						return false;
					}

					for (std::size_t i = 0; i < this->transitions.size(); ++i)
					{
						if (this->transitions[i].set != other.transitions[i].set || this->transitions[i].next != other.transitions[i].next)
						{
							return false;
						}
					}

					return true;
				}
			};

			struct state_key_hash
			{
				std::size_t operator()(const state_key& key) const noexcept
				{
					std::size_t accumulated = key.transitions.size();

					for (const transition& arc : key.transitions)
					{
						accumulated = (accumulated * 31) + arc.set.hash();
						accumulated = (accumulated * 31) + static_cast<std::size_t>(arc.next->id);
					}

					return accumulated;
				}
			};

			class builder
			{
			public:
				builder(rx_factory& factory, int max_states)
					: factory(factory)
					, max_states(max_states)
				{
				}

				bool try_build(const rx* start, std::unique_ptr<state_map>& map, std::optional<fault>& error)
				{
					map = nullptr;

					std::vector<const rx*> explored;
					std::unordered_map<const rx*, std::vector<edge>> edges;

					if (!this->try_explore(start, explored, edges, error))
					{
						return false;
					}

					// The successors of an expression all have a strictly shorter longest
					// string, so this ordering puts every state after the states it points at.
					// The terminal expressions, whose longest string is empty, come first.
					std::stable_sort(explored.begin(), explored.end(), [](const rx* left, const rx* right) { return left->max_length < right->max_length; });

					const state* terminal = this->intern({});
					std::unordered_map<const rx*, const state*> state_of;

					for (const rx* expression : explored)
					{
						const auto outgoing = edges.find(expression);

						if (outgoing == edges.end())
						{
							// No first sets, so the language is the empty string alone.
							state_of.emplace(expression, terminal);
							continue;
						}

						this->by_next.clear();

						for (const edge& arc : outgoing->second)
						{
							const state* next = state_of.at(arc.derivative);
							const auto already = this->by_next.find(next);

							if (already == this->by_next.end())
							{
								this->by_next.emplace(next, arc.set);
							}
							else
							{
								already->second = already->second | arc.set;
							}
						}

						this->transitions.clear();

						if (expression->is_nullable)
						{
							this->transitions.push_back(transition{ascii_char_set(), terminal});
						}

						for (const auto& [next, set] : this->by_next)
						{
							this->transitions.push_back(transition{set, next});
						}

						// After merging the sets are disjoint and non-empty apart from end of
						// text, so the sort has one outcome and its stability does not matter.
						std::sort(this->transitions.begin(), this->transitions.end(), [](const transition& left, const transition& right) { return left.set < right.set; });

						state_of.emplace(expression, this->intern(this->transitions));
					}

					map = std::make_unique<state_map>(state_of.at(start), std::move(this->states), this->saturated);
					error.reset();

					return true;
				}

			private:
				/// Walks the derivatives breadth first, collecting the distinct expressions
				/// and the edges between them.
				bool try_explore(const rx* start, std::vector<const rx*>& explored, std::unordered_map<const rx*, std::vector<edge>>& edges, std::optional<fault>& error)
				{
					explored.push_back(start);

					std::unordered_set<const rx*> seen{start};
					std::deque<const rx*> queue{start};

					while (!queue.empty())
					{
						const rx* expression = queue.front();
						queue.pop_front();

						const std::vector<ascii_char_set>& first_sets = expression->get_first_sets();

						if (first_sets.empty())
						{
							continue;
						}

						std::vector<edge> outgoing;

						minterms(first_sets, this->minterm_blocks);

						for (const ascii_char_set minterm : this->minterm_blocks)
						{
							const rx* derivative = this->factory.derivative(expression, minterm);

							if (derivative->kind == rx_kind::empty_set)
							{
								continue;
							}

							outgoing.push_back(edge{minterm, derivative});

							if (seen.insert(derivative).second)
							{
								explored.push_back(derivative);
								queue.push_back(derivative);

								if (explored.size() > static_cast<std::size_t>(this->max_states))
								{
									explored.clear();
									edges.clear();
									error = fault(naxp_message::too_many_states);

									return false;
								}
							}
						}

						edges.emplace(expression, std::move(outgoing));
					}

					error.reset();

					return true;
				}

				const state* intern(std::vector<transition> transitions)
				{
					state_key key{std::move(transitions)};

					if (const auto found = this->interned.find(key); found != this->interned.end())
					{
						return found->second;
					}

					const std::uint64_t string_count = this->count_values(key.transitions);
					auto created = std::make_unique<state>(static_cast<int>(this->states.size()), key.transitions, string_count);
					const state* result = created.get();

					this->states.push_back(std::move(created));
					this->interned.emplace(std::move(key), result);

					return result;
				}

				/// The count of strings a state's language holds, which is the sum over its
				/// transitions of max(1, size of the set) times the count of the next state.
				std::uint64_t count_values(const std::vector<transition>& transitions)
				{
					if (transitions.empty())
					{
						return 1;
					}

					std::uint64_t total = 0;

					for (const transition& arc : transitions)
					{
						const std::uint64_t width = arc.set.is_empty() ? 1 : static_cast<std::uint64_t>(arc.set.count());
						total = this->add(total, this->multiply(width, arc.next->string_count));
					}

					return total;
				}

				// The limit is the full width of the accumulator, so a single step can wrap from
				// operands that were both themselves legal, and a wrap cannot be recognised by
				// comparing the result against the limit afterwards. Every step is therefore
				// tested before it is taken.
				std::uint64_t multiply(std::uint64_t left, std::uint64_t right)
				{
					if (left == 0 || right == 0)
					{
						return 0;
					}

					if (left > limits::max_encoded_value / right)
					{
						this->saturated = true;

						return limits::max_encoded_value;
					}

					return left * right;
				}

				std::uint64_t add(std::uint64_t left, std::uint64_t right)
				{
					const std::uint64_t sum = left + right;

					// A sum below either operand is one that wrapped. Two alternatives that are
					// each legal on their own reach here, so this is not a theoretical case.
					if (sum < left)
					{
						this->saturated = true;

						return limits::max_encoded_value;
					}

					return sum;
				}

				rx_factory& factory;
				int max_states;
				std::unordered_map<state_key, const state*, state_key_hash> interned;
				std::vector<std::unique_ptr<state>> states;

				// Working space for one state at a time, cleared at the top of each state
				// rather than allocated per state.
				std::vector<ascii_char_set> minterm_blocks;
				std::unordered_map<const state*, ascii_char_set> by_next;
				std::vector<transition> transitions;

				bool saturated = false;
			};
		}

		bool try_build(const rx* start, rx_factory& factory, std::unique_ptr<state_map>& map, std::optional<fault>& error, int max_states)
		{
			return builder(factory, max_states).try_build(start, map, error);
		}

		void minterms(array_view<ascii_char_set> sets, std::vector<ascii_char_set>& blocks)
		{
			ascii_char_set universe;

			for (const ascii_char_set set : sets)
			{
				universe = universe | set;
			}

			blocks.clear();

			if (universe.is_empty())
			{
				return;
			}

			blocks.push_back(universe);

			for (const ascii_char_set set : sets)
			{
				// Once every block is a single character no further set can split anything.
				if (blocks.size() == static_cast<std::size_t>(universe.count()))
				{
					break;
				}

				// Only the blocks already present can be cut by this set. What gets appended
				// below is the part that fell outside it, which this set cannot cut again.
				const std::size_t count = blocks.size();

				for (std::size_t i = 0; i < count; ++i)
				{
					const ascii_char_set inside = blocks[i] & set;
					const ascii_char_set outside = blocks[i] - set;

					// The block lies wholly inside the set or wholly outside it, so it stands.
					if (inside.is_empty() || outside.is_empty())
					{
						continue;
					}

					blocks[i] = inside;
					blocks.push_back(outside);
				}
			}
		}

		std::vector<ascii_char_set> minterms(array_view<ascii_char_set> sets)
		{
			std::vector<ascii_char_set> blocks;
			minterms(sets, blocks);

			return blocks;
		}
	}
}
