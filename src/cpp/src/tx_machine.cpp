// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "tx_machine.hpp"

#include "ascii_char_set.hpp"
#include "fault.hpp"
#include "naxp_message.hpp"
#include "state_map.hpp"
#include "tx.hpp"

#include <algorithm>
#include <cstddef>
#include <deque>
#include <functional>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace logmu::detail
{
	// tx_machine

	namespace
	{
		/// Appends an output, resolving each reference to the character it stands for.
		void append_output(std::string& builder, std::string_view output, std::string_view text, std::size_t at)
		{
			for (std::size_t i = 0; i < output.size(); ++i)
			{
				if (output[i] == copy_marker)
				{
					builder.push_back(text[at - static_cast<std::size_t>(output[i + 1] - tx_reference::depth_base)]);
					++i;
				}
				else
				{
					builder.push_back(output[i]);
				}
			}
		}
	}

	bool tx_machine::try_canonicalise(std::string_view text, std::string& canonical) const
	{
		std::string builder;
		const tx_state* current = this->start;

		for (std::size_t i = 0; i < text.size(); ++i)
		{
			const char c = text[i];
			const tx_state* next = nullptr;

			for (const tx_transition& arc : current->transitions)
			{
				if (!arc.set.contains(c))
				{
					continue;
				}

				append_output(builder, arc.output, text, i);
				next = arc.next;
				break;
			}

			if (next == nullptr)
			{
				return false;
			}

			current = next;
		}

		if (!current->end_output.has_value())
		{
			return false;
		}

		// The end output reaches back from the last character read, as a transition's does
		// from the character that took it.
		append_output(builder, *current->end_output, text, text.size() - 1);
		canonical = std::move(builder);

		return true;
	}

	// tx_reference

	namespace tx_reference
	{
		std::string symbolise(std::string_view emitted)
		{
			if (emitted.find(copy_marker) == std::string_view::npos)
			{
				return std::string(emitted);
			}

			std::string builder;
			builder.reserve(emitted.size() * 2);

			for (const char c : emitted)
			{
				if (c == copy_marker)
				{
					builder.push_back(copy_marker);
					builder.push_back(static_cast<char>(depth_base));
				}
				else
				{
					builder.push_back(c);
				}
			}

			return builder;
		}

		std::string age(std::string_view pending)
		{
			if (pending.find(copy_marker) == std::string_view::npos)
			{
				return std::string(pending);
			}

			std::string builder;
			builder.reserve(pending.size());

			for (std::size_t i = 0; i < pending.size(); ++i)
			{
				if (pending[i] == copy_marker)
				{
					builder.push_back(copy_marker);
					builder.push_back(static_cast<char>(pending[i + 1] + 1));
					++i;
				}
				else
				{
					builder.push_back(pending[i]);
				}
			}

			return builder;
		}

		int depth_of(std::string_view pending) noexcept
		{
			int depth = 0;

			for (std::size_t i = pending.find(copy_marker); i != std::string_view::npos; i = pending.find(copy_marker, i + 2))
			{
				depth = std::max(depth, pending[i + 1] - depth_base + 1);
			}

			return depth;
		}

		std::size_t unit_length(std::string_view pending, std::size_t at) noexcept
		{
			return pending[at] == copy_marker ? 2 : 1;
		}

		bool could_reconcile(std::string_view left, std::string_view right) noexcept
		{
			std::size_t at = left.find(copy_marker);

			if (at != std::string_view::npos && left[at + 1] == static_cast<char>(depth_base))
			{
				return true;
			}

			at = right.find(copy_marker);

			return at != std::string_view::npos && right[at + 1] == static_cast<char>(depth_base);
		}
	}

	// tx_machine_builder

	namespace tx_machine_builder
	{
		namespace
		{
			/// One live parse: what is left to consume, and what it owes beyond the others.
			struct branch
			{
				const tx* residual;

				/// What this parse has emitted that the machine has not. Never holds a
				/// `copy_marker`: the builder narrows a block to single characters rather than
				/// carry one past the step that read it, since nothing downstream could resolve
				/// it.
				std::string pending;

				bool operator==(const branch& other) const noexcept
				{
					return this->residual == other.residual && this->pending == other.pending;
				}
			};

			/// A set of live parses, which is what a state of the machine is. Sorted and
			/// deduplicated by whoever builds it.
			struct branch_set_key
			{
				std::vector<branch> branches;

				bool operator==(const branch_set_key& other) const noexcept
				{
					return this->branches == other.branches;
				}
			};

			struct branch_set_key_hash
			{
				std::size_t operator()(const branch_set_key& key) const noexcept
				{
					std::size_t accumulated = key.branches.size();

					for (const branch& item : key.branches)
					{
						const std::size_t branch_hash = (static_cast<std::size_t>(item.residual->id) * 31) ^ std::hash<std::string>()(item.pending);
						accumulated = (accumulated * 31) + branch_hash;
					}

					return accumulated;
				}
			};

			/// A transition recorded before its target state object exists.
			struct pending_transition
			{
				ascii_char_set set;
				std::string output;
				std::size_t next;
			};

			fault violation()
			{
				return fault(naxp_message::unification_not_single_valued);
			}

			fault too_large()
			{
				return fault(naxp_message::too_many_canonical_states);
			}

			std::string longest_common_prefix(const std::unordered_map<const tx*, std::string>& pendings)
			{
				const std::string* shortest = nullptr;

				for (const auto& [residual, pending] : pendings)
				{
					if (shortest == nullptr || pending.size() < shortest->size())
					{
						shortest = &pending;
					}
				}

				std::size_t common = shortest->size();

				for (const auto& [residual, pending] : pendings)
				{
					std::size_t at = 0;

					while (at < common && pending[at] == (*shortest)[at])
					{
						++at;
					}

					common = at;

					if (common == 0)
					{
						break;
					}
				}

				// A reference is two characters, so a prefix must not stop between them.
				// Walking the units of the shortest pending cannot land inside one.
				std::size_t unit = 0;

				while (unit < common)
				{
					const std::size_t next = unit + tx_reference::unit_length(*shortest, unit);

					if (next > common)
					{
						break;
					}

					unit = next;
				}

				return shortest->substr(0, unit);
			}

			class builder
			{
			public:
				builder(tx_factory& factory, int max_states)
					: factory(factory)
					, max_states(max_states)
				{
				}

				bool try_run(const tx* root, std::unique_ptr<tx_machine>& machine, std::optional<fault>& error)
				{
					machine = nullptr;

					std::deque<std::size_t> queue;
					std::size_t start = 0;

					if (!this->try_add(branch_set_key{{branch{root, std::string()}}}, queue, start, error))
					{
						return false;
					}

					while (!queue.empty())
					{
						const std::size_t index = queue.front();
						queue.pop_front();

						if (!this->try_set_end_output(index, error))
						{
							return false;
						}

						if (!this->try_explore(index, queue, error))
						{
							return false;
						}
					}

					// W6 counts what the machine holds as well as what it is, because a register
					// is memory the state count does not see. The depth is small next to the
					// states: fifteen for the widest decimal range the language admits.
					if (this->keys.size() + static_cast<std::size_t>(this->register_depth) > static_cast<std::size_t>(this->max_states))
					{
						error = too_large();

						return false;
					}

					const std::unique_ptr<tx_machine> built = this->materialise(start);
					machine = tx_machine_merger::merge(*built);
					error.reset();

					return true;
				}

			private:
				/// Records how far back an output reaches.
				void note_depth(std::string_view pending) noexcept
				{
					const int depth = tx_reference::depth_of(pending);

					if (depth > this->register_depth)
					{
						this->register_depth = depth;
					}
				}

				/// Records what the state emits where the input ends, failing where the parses
				/// disagree.
				bool try_set_end_output(std::size_t index, std::optional<fault>& error)
				{
					std::optional<std::string> end_output;

					for (const branch& item : this->keys[index].branches)
					{
						const eot& end = item.residual->get_eot();

						switch (end.kind)
						{
							case eot_kind::none:
								continue;

							case eot_kind::too_long:
								error = too_large();
								return false;

							case eot_kind::multiple:
								error = violation();
								return false;

							case eot_kind::single:
							{
								std::string candidate = item.pending + end.text;

								this->note_depth(candidate);

								if (!end_output.has_value())
								{
									end_output = std::move(candidate);
								}
								else if (*end_output != candidate)
								{
									error = violation();

									return false;
								}

								break;
							}
						}
					}

					this->end_output_of[index] = std::move(end_output);
					error.reset();

					return true;
				}

				/// Follows every block of characters the state can read.
				bool try_explore(std::size_t index, std::deque<std::size_t>& queue, std::optional<fault>& error)
				{
					this->first_sets.clear();

					for (const branch& item : this->keys[index].branches)
					{
						const std::vector<ascii_char_set>& sets = item.residual->get_first_sets();
						this->first_sets.insert(this->first_sets.end(), sets.begin(), sets.end());
					}

					if (this->first_sets.empty())
					{
						error.reset();

						return true;
					}

					state_map_builder::minterms(this->first_sets, this->blocks);

					// Copied because narrowing a block re-enters the step, and the shared list
					// must not be the thing being iterated.
					const std::vector<ascii_char_set> snapshot = this->blocks;

					for (const ascii_char_set block : snapshot)
					{
						if (!this->try_step(index, block, queue, error))
						{
							return false;
						}
					}

					error.reset();

					return true;
				}

				/// Takes one step, narrowing the block to single characters where what is
				/// emitted would otherwise stay undecided past this step.
				bool try_step(std::size_t index, ascii_char_set block, std::deque<std::size_t>& queue, std::optional<fault>& error)
				{
					std::unordered_map<const tx*, std::string> pending_of;

					for (const branch& item : this->keys[index].branches)
					{
						const tx_derivative& derivative = this->factory.derivative(item.residual, block);

						if (derivative.too_long)
						{
							error = too_large();

							return false;
						}

						if (derivative.skips_ambiguously)
						{
							error = violation();

							return false;
						}

						// What was already owed is one character older now, and what this step
						// emits owes the character being read.
						const std::string carried = tx_reference::age(item.pending);

						for (const tx_move& move : derivative.moves)
						{
							std::string pending = carried + tx_reference::symbolise(move.emitted);
							const auto existing = pending_of.find(move.residual);

							if (existing == pending_of.end())
							{
								pending_of.emplace(move.residual, std::move(pending));
							}
							else if (existing->second != pending)
							{
								// Same continuation, two outputs, which would give every string
								// the continuation accepts two canonical forms. Unless one of
								// them owes the character being read: fixing that character may
								// make the two the same output rather than two different ones,
								// so the block is narrowed before the verdict.
								if (!block.single_character().has_value() && tx_reference::could_reconcile(existing->second, pending))
								{
									return this->try_narrow(index, block, queue, error);
								}

								error = violation();

								return false;
							}
						}
					}

					if (pending_of.empty())
					{
						error.reset();

						return true;
					}

					const std::string common = longest_common_prefix(pending_of);

					this->note_depth(common);

					std::vector<branch> branches;
					branches.reserve(pending_of.size());

					for (const auto& [residual, pending] : pending_of)
					{
						std::string trimmed = pending.substr(common.size());

						this->note_depth(trimmed);
						branches.push_back(branch{residual, std::move(trimmed)});
					}

					std::sort(branches.begin(), branches.end(), [](const branch& left, const branch& right)
					{
						return left.residual->id != right.residual->id
							? left.residual->id < right.residual->id
							: left.pending < right.pending;
					});

					std::size_t next = 0;

					if (!this->try_add(branch_set_key{std::move(branches)}, queue, next, error))
					{
						return false;
					}

					this->transitions_of[index].push_back(pending_transition{block, common, next});
					error.reset();

					return true;
				}

				/// Retakes a step one character at a time, so that a reference to the character
				/// being read becomes the character itself and two parses can be compared.
				bool try_narrow(std::size_t index, ascii_char_set block, std::deque<std::size_t>& queue, std::optional<fault>& error)
				{
					for (const char c : block)
					{
						if (!this->try_step(index, ascii_char_set::single_character(c), queue, error))
						{
							return false;
						}
					}

					error.reset();

					return true;
				}

				/// Finds a state, adding it and queueing it where it is new.
				bool try_add(branch_set_key key, std::deque<std::size_t>& queue, std::size_t& index, std::optional<fault>& error)
				{
					if (const auto found = this->index_of.find(key); found != this->index_of.end())
					{
						index = found->second;
						error.reset();

						return true;
					}

					if (this->keys.size() >= static_cast<std::size_t>(this->max_states))
					{
						index = 0;
						error = too_large();

						return false;
					}

					index = this->keys.size();

					this->keys.push_back(key);
					this->transitions_of.emplace_back();
					this->end_output_of.emplace_back();
					this->index_of.emplace(std::move(key), index);

					queue.push_back(index);
					error.reset();

					return true;
				}

				/// Turns the recorded indices into linked state objects.
				std::unique_ptr<tx_machine> materialise(std::size_t start)
				{
					std::vector<std::unique_ptr<tx_state>> states;
					states.reserve(this->keys.size());

					for (std::size_t i = 0; i < this->keys.size(); ++i)
					{
						states.push_back(std::make_unique<tx_state>(static_cast<int>(i)));
						states.back()->end_output = this->end_output_of[i];
					}

					for (std::size_t i = 0; i < states.size(); ++i)
					{
						std::vector<tx_transition> transitions;
						transitions.reserve(this->transitions_of[i].size());

						for (const pending_transition& pending : this->transitions_of[i])
						{
							transitions.push_back(tx_transition{pending.set, pending.output, states[pending.next].get()});
						}

						std::sort(transitions.begin(), transitions.end(), [](const tx_transition& left, const tx_transition& right) { return left.set < right.set; });

						states[i]->transitions = std::move(transitions);
					}

					const tx_state* start_state = states[start].get();

					return std::make_unique<tx_machine>(start_state, std::move(states), this->register_depth);
				}

				tx_factory& factory;
				int max_states;
				std::unordered_map<branch_set_key, std::size_t, branch_set_key_hash> index_of;
				std::vector<branch_set_key> keys;
				std::vector<std::vector<pending_transition>> transitions_of;
				std::vector<std::optional<std::string>> end_output_of;
				std::vector<ascii_char_set> blocks;
				std::vector<ascii_char_set> first_sets;

				/// How far back the deepest reference reaches, which is what a run has to keep.
				int register_depth = 0;
			};
		}

		bool try_build(const tx* root, tx_factory& factory, std::unique_ptr<tx_machine>& machine, std::optional<fault>& error, int max_states)
		{
			return builder(factory, max_states).try_run(root, machine, error);
		}
	}

	// tx_machine_merger

	namespace tx_machine_merger
	{
		namespace
		{
			/// How far through one state's transitions the walk has got.
			struct step
			{
				const tx_state* state;
				std::size_t index;
			};

			/// Post-order, so that every successor is ordered before the state that reaches it.
			///
			/// Iterative rather than recursive. A naxp is allowed to be a long chain,
			/// `(\A!A){99}` is legal, linear and ten thousand states, and recursing over that
			/// overflows the stack, which cannot be caught.
			std::vector<const tx_state*> post_order(const tx_state* start)
			{
				std::vector<const tx_state*> order;
				std::unordered_set<const tx_state*> seen{start};
				std::vector<step> pending{step{start, 0}};

				while (!pending.empty())
				{
					const step current = pending.back();
					pending.pop_back();

					if (current.index == current.state->transitions.size())
					{
						// Every successor has been finished, so this state may be finished too.
						order.push_back(current.state);
						continue;
					}

					pending.push_back(step{current.state, current.index + 1});

					const tx_state* next = current.state->transitions[current.index].next;

					if (seen.insert(next).second)
					{
						pending.push_back(step{next, 0});
					}
				}

				return order;
			}

			std::optional<std::size_t> index_of_mergeable(const std::vector<tx_transition>& rebuilt, const tx_state* target, std::string_view output) noexcept
			{
				for (std::size_t i = 0; i < rebuilt.size(); ++i)
				{
					if (rebuilt[i].next == target && rebuilt[i].output == output)
					{
						return i;
					}
				}

				return std::nullopt;
			}

			/// What makes two states the same once their successors have been merged.
			struct merged_key
			{
				std::optional<std::string> end_output;
				std::vector<tx_transition> transitions;

				bool operator==(const merged_key& other) const noexcept
				{
					if (this->end_output != other.end_output || this->transitions.size() != other.transitions.size())
					{
						return false;
					}

					for (std::size_t i = 0; i < this->transitions.size(); ++i)
					{
						if (this->transitions[i].set != other.transitions[i].set
							|| this->transitions[i].output != other.transitions[i].output
							|| this->transitions[i].next != other.transitions[i].next)
						{
							return false;
						}
					}

					return true;
				}
			};

			struct merged_key_hash
			{
				std::size_t operator()(const merged_key& key) const noexcept
				{
					std::size_t accumulated = key.end_output.has_value() ? std::hash<std::string>()(*key.end_output) : 0;

					for (const tx_transition& arc : key.transitions)
					{
						accumulated = (accumulated * 31) + arc.set.hash();
						accumulated = (accumulated * 31) + std::hash<std::string>()(arc.output);
						accumulated = (accumulated * 31) + static_cast<std::size_t>(arc.next->id);
					}

					return accumulated;
				}
			};
		}

		std::unique_ptr<tx_machine> merge(const tx_machine& machine)
		{
			const std::vector<const tx_state*> order = post_order(machine.start);

			std::unordered_map<const tx_state*, tx_state*> representative;
			std::unordered_map<merged_key, tx_state*, merged_key_hash> canonical;
			std::vector<std::unique_ptr<tx_state>> merged;
			std::vector<tx_transition> rebuilt;

			for (const tx_state* current : order)
			{
				rebuilt.clear();

				for (const tx_transition& arc : current->transitions)
				{
					tx_state* target = representative.at(arc.next);
					const std::optional<std::size_t> at = index_of_mergeable(rebuilt, target, arc.output);

					if (at.has_value())
					{
						rebuilt[*at].set = rebuilt[*at].set | arc.set;
					}
					else
					{
						rebuilt.push_back(tx_transition{arc.set, arc.output, target});
					}
				}

				std::sort(rebuilt.begin(), rebuilt.end(), [](const tx_transition& left, const tx_transition& right) { return left.set < right.set; });

				merged_key key{current->end_output, rebuilt};

				if (const auto existing = canonical.find(key); existing != canonical.end())
				{
					representative.emplace(current, existing->second);
					continue;
				}

				auto created = std::make_unique<tx_state>(static_cast<int>(merged.size()));
				created->end_output = current->end_output;
				created->transitions = rebuilt;

				tx_state* created_state = created.get();
				merged.push_back(std::move(created));
				canonical.emplace(std::move(key), created_state);
				representative.emplace(current, created_state);
			}

			const tx_state* start = representative.at(machine.start);

			return std::make_unique<tx_machine>(start, std::move(merged), machine.register_depth);
		}
	}
}
