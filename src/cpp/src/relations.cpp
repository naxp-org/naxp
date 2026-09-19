// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "relations.hpp"

#include "naxp/naxp.hpp"

#include "ascii_char_set.hpp"
#include "compiler.hpp"
#include "rank_agreement.hpp"
#include "state_map.hpp"
#include "value_agreement.hpp"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace logmu::detail::relations
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

		/// The characters a state can consume, the end of text transition excepted.
		ascii_char_set taken(const state& from) noexcept
		{
			ascii_char_set result;

			for (const transition& arc : from.transitions)
			{
				result = result | arc.set;
			}

			return result;
		}

		class language_walk
		{
		public:
			set_relationship run(const state_map& a, const state_map& b)
			{
				this->visit(a.start, b.start);

				if (!this->a_has_extra && !this->b_has_extra)
				{
					return set_relationship::equal;
				}

				if (this->a_has_extra && !this->b_has_extra)
				{
					return set_relationship::superset_of;
				}

				if (!this->a_has_extra && this->b_has_extra)
				{
					return set_relationship::subset_of;
				}

				return set_relationship::incomparable;
			}

		private:
			void visit(const state* p, const state* q)
			{
				// Nothing further can change the answer once both are known.
				if (this->a_has_extra && this->b_has_extra)
				{
					return;
				}

				if (!this->visited.insert(std::pair<int, int>(p->id, q->id)).second)
				{
					return;
				}

				if (p->accepts_end_of_text() != q->accepts_end_of_text())
				{
					if (p->accepts_end_of_text())
					{
						this->a_has_extra = true;
					}
					else
					{
						this->b_has_extra = true;
					}
				}

				const ascii_char_set taken_by_p = taken(*p);
				const ascii_char_set taken_by_q = taken(*q);

				// A character one side can take and the other cannot leads somewhere, because
				// every state of a trimmed machine holds at least one string.
				for (const transition& arc : p->transitions)
				{
					if (!arc.set.is_empty() && !(arc.set - taken_by_q).is_empty())
					{
						this->a_has_extra = true;
					}
				}

				for (const transition& arc : q->transitions)
				{
					if (!arc.set.is_empty() && !(arc.set - taken_by_p).is_empty())
					{
						this->b_has_extra = true;
					}
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

						if (left.set.intersects_with(right.set))
						{
							this->visit(left.next, right.next);
						}
					}
				}
			}

			bool a_has_extra = false;
			bool b_has_extra = false;
			std::unordered_set<std::pair<int, int>, pair_hash> visited;
		};

		/// A state's enumeration as chunks in order: the empty string, then one chunk per
		/// character in class order and ASCII order within a class. A null state marks the end
		/// of text.
		struct chunk
		{
			char c;
			const state* next;
		};

		std::vector<chunk> chunks(const state& from)
		{
			std::vector<chunk> result;

			for (const transition& arc : from.transitions)
			{
				if (arc.set.is_empty())
				{
					result.push_back(chunk{'\0', nullptr});
					continue;
				}

				for (const char c : arc.set)
				{
					result.push_back(chunk{c, arc.next});
				}
			}

			if (from.is_terminal())
			{
				result.push_back(chunk{'\0', nullptr});
			}

			return result;
		}

		class divergence_walk
		{
		public:
			std::uint64_t run(const state_map& a, const state_map& b)
			{
				std::uint64_t index = 0;

				return this->diverge(a.start, b.start, index) ? index + 1 : 0;
			}

		private:
			struct outcome
			{
				bool found;
				std::uint64_t index;
			};

			// Whether the two enumerations differ within the length of the shorter, and where.
			bool diverge(const state* p, const state* q, std::uint64_t& index)
			{
				const std::pair<int, int> key(p->id, q->id);

				if (const auto known = this->memo.find(key); known != this->memo.end())
				{
					index = known->second.index;

					return known->second.found;
				}

				const bool found = this->compare(p, q, index);
				this->memo[key] = outcome{found, index};

				return found;
			}

			bool compare(const state* p, const state* q, std::uint64_t& index)
			{
				const std::vector<chunk> left = chunks(*p);
				const std::vector<chunk> right = chunks(*q);
				std::size_t i = 0;
				std::size_t j = 0;
				std::uint64_t offset = 0;

				while (i < left.size() && j < right.size())
				{
					const chunk x = left[i];
					const chunk y = right[j];

					// Both end here: one value each, the same string.
					if (x.next == nullptr && y.next == nullptr)
					{
						++offset;
						++i;
						++j;
						continue;
					}

					// One ends where the other reads on, or they read different characters, so
					// the strings at this position differ in this very character.
					if (x.next == nullptr || y.next == nullptr || x.c != y.c)
					{
						index = offset;

						return true;
					}

					std::uint64_t inner = 0;

					if (this->diverge(x.next, y.next, inner))
					{
						index = offset + inner;

						return true;
					}

					const std::uint64_t left_count = x.next->string_count;
					const std::uint64_t right_count = y.next->string_count;

					if (left_count == right_count)
					{
						offset += left_count;
						++i;
						++j;
						continue;
					}

					// The shorter continuation ran out inside the chunk. The longer side's next
					// string still begins with this character; the shorter side's next chunk
					// begins with another, so they differ there, unless the shorter side has no
					// next chunk and its whole enumeration has ended.
					const bool shorter_has_more = left_count < right_count ? i + 1 < left.size() : j + 1 < right.size();

					if (shorter_has_more)
					{
						index = offset + std::min(left_count, right_count);

						return true;
					}

					index = 0;

					return false;
				}

				// One enumeration ended without a difference, so there is none within the
				// shorter.
				index = 0;

				return false;
			}

			std::unordered_map<std::pair<int, int>, outcome, pair_hash> memo;
		};
	}

	set_relationship compare_languages(const state_map& a, const state_map& b)
	{
		return language_walk().run(a, b);
	}

	bool try_compare_encodings(const compilation& a, const compilation& b, set_relationship& relationship, int budget)
	{
		relationship = set_relationship::incomparable;

		// With no string held in common, or with each holding strings the other lacks, the
		// graphs are incomparable before any value is looked at.
		const set_relationship languages = compare_languages(a.accepted(), b.accepted());

		if (languages == set_relationship::incomparable)
		{
			return true;
		}

		const agreement ranks = rank_agreement::compare(a.canonical(), b.canonical(), budget);

		if (ranks == agreement::differs)
		{
			return true;
		}

		agreement values = agreement::undecided;

		if (a.canonical_is_identity() && b.canonical_is_identity())
		{
			// Every accepted string is canonical, so the rank walk has already seen them all.
			values = ranks;
		}
		else
		{
			std::optional<std::string> witness;
			values = value_agreement::compare(a, b, witness, budget);
		}

		if (values == agreement::undecided)
		{
			return false;
		}

		relationship = values == agreement::agrees ? languages : set_relationship::incomparable;

		return true;
	}

	std::uint64_t first_divergent_value(const state_map& a, const state_map& b)
	{
		return divergence_walk().run(a, b);
	}
}
