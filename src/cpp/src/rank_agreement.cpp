// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "rank_agreement.hpp"

#include "ascii_char_set.hpp"
#include "rank_difference.hpp"
#include "state_map.hpp"

#include <cstddef>
#include <cstdint>
#include <unordered_map>
#include <utility>
#include <vector>

namespace logmu::detail::rank_agreement
{
	namespace
	{
		struct pair_hash
		{
			std::size_t operator()(const std::pair<int, int>& key) const noexcept
			{
				return (static_cast<std::size_t>(key.first) * 31) ^ static_cast<std::size_t>(key.second);
			}
		};

		class walk
		{
		public:
			explicit walk(int max_pairs) noexcept
				: max_pairs(max_pairs)
			{
			}

			agreement run(const state_map& a, const state_map& b)
			{
				this->visit(a.start, b.start, rank_difference());

				if (this->outgrew)
				{
					return agreement::undecided;
				}

				// A conflict only matters where the two machines still share a string from that
				// pair. Where the shared language dies out below it, nothing reaches an
				// accepting pair and the differing totals are never compared.
				for (const auto& [p, q] : this->conflicts)
				{
					if (this->shares_an_ending(p, q))
					{
						return agreement::differs;
					}
				}

				return agreement::agrees;
			}

		private:
			void visit(const state* p, const state* q, rank_difference diff)
			{
				if (this->outgrew)
				{
					return;
				}

				const std::pair<int, int> key(p->id, q->id);

				if (const auto seen = this->difference.find(key); seen != this->difference.end())
				{
					if (seen->second != diff)
					{
						this->conflicts.emplace_back(p, q);
					}

					return;
				}

				if (this->difference.size() >= static_cast<std::size_t>(this->max_pairs))
				{
					this->outgrew = true;

					return;
				}

				this->difference.emplace(key, diff);

				// Both ending here is a string they share, and the value each gives it is the
				// running total plus one, so the difference decides it.
				if (p->accepts_end_of_text() && q->accepts_end_of_text() && !diff.is_zero())
				{
					this->conflicts.emplace_back(p, q);
				}

				for (const transition& left : p->transitions)
				{
					if (left.set.is_empty())
					{
						continue;
					}

					for (const transition& right : q->transitions)
					{
						if (right.set.is_empty())
						{
							continue;
						}

						const ascii_char_set shared = left.set & right.set;

						if (shared.is_empty())
						{
							continue;
						}

						// The rank a character contributes depends on where it sits in its own
						// set, so each shared character is its own step. They agree on the pair
						// they move to, which is what the memo above collapses them onto.
						for (const char c : shared)
						{
							this->visit(
								left.next,
								right.next,
								diff.plus(contribution(*p, left, c)).minus(contribution(*q, right, c)));
						}
					}
				}
			}

			// Whether the two states still hold a string in common, which is what makes a
			// difference between their totals something anybody can observe.
			bool shares_an_ending(const state* p, const state* q)
			{
				const std::pair<int, int> key(p->id, q->id);

				if (const auto known = this->reaches.find(key); known != this->reaches.end())
				{
					return known->second;
				}

				// False while the answer is being worked out, so a pair cannot depend on itself.
				this->reaches.emplace(key, false);

				bool found = p->accepts_end_of_text() && q->accepts_end_of_text();

				if (!found)
				{
					for (const transition& left : p->transitions)
					{
						if (left.set.is_empty() || found)
						{
							continue;
						}

						for (const transition& right : q->transitions)
						{
							if (right.set.is_empty())
							{
								continue;
							}

							if (left.set.intersects_with(right.set) && this->shares_an_ending(left.next, right.next))
							{
								found = true;
								break;
							}
						}
					}
				}

				this->reaches[key] = found;

				return found;
			}

			int max_pairs;
			std::unordered_map<std::pair<int, int>, rank_difference, pair_hash> difference;
			std::vector<std::pair<const state*, const state*>> conflicts;
			std::unordered_map<std::pair<int, int>, bool, pair_hash> reaches;
			bool outgrew = false;
		};
	}

	agreement compare(const state_map& a, const state_map& b, int budget)
	{
		// A saturated count is a stand-in for the real one, so the arithmetic below would be
		// comparing two approximations rather than two ranks.
		if (a.count_saturated || b.count_saturated)
		{
			return agreement::undecided;
		}

		return walk(budget).run(a, b);
	}

	std::uint64_t contribution(const state& from, const transition& taken, char c) noexcept
	{
		std::uint64_t skipped = 0;

		for (const transition& arc : from.transitions)
		{
			const std::uint64_t count = arc.next->string_count;

			if (arc.set == taken.set)
			{
				return skipped + (count * static_cast<std::uint64_t>(taken.set.index_of(c)));
			}

			// An empty set is the end of text transition, which stands for one string.
			skipped += count * (arc.set.is_empty() ? 1 : static_cast<std::uint64_t>(arc.set.count()));
		}

		return skipped;
	}
}
