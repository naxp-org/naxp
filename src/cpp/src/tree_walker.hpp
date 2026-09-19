// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_TREE_WALKER_HPP
#define NAXP_TREE_WALKER_HPP

#include "ast.hpp"
#include "naxp_limits.hpp"

#include <cstddef>
#include <string>
#include <string_view>
#include <vector>

/// Matches a string against a parsed naxp, without building a machine.
///
/// W1 and the canonical form both need an answer before any machine exists, W1 because it runs
/// before the build and canonicalisation because it works over the tree, where the unified
/// elements are still visible. Working with sets of positions rather than by backtracking keeps
/// the cost polynomial.
namespace logmu::detail::tree_walker
{
	/// The most characters this implementation will materialise for a single generated string.
	///
	/// Not a rule of the language. A naxp generating a longer string than this would be invalid
	/// by `limits::max_states` a moment later in any case; see `limits::max_string_length` for
	/// why the two are tied together.
	inline constexpr int max_generated_length = limits::max_string_length;

	/// Whether an expression generates exactly one string.
	enum class single_string_outcome
	{
		/// The expression generates exactly one string.
		single,

		/// It generates none, or more than one.
		multiple,

		/// It generates one string, but longer than `max_generated_length`.
		too_long,
	};

	/// The one string an expression generates, where it generates exactly one.
	///
	/// @param node The expression.
	/// @param result The string, meaningful only where the outcome is `single`.
	single_string_outcome try_get_single_string(const ast& node, std::string& result);

	/// A set of positions in a text, from zero to its length inclusive.
	///
	/// A bitmap rather than a hash set: there are at most one more positions than characters,
	/// so it is small, and union, equality and emptiness are then a pass over it.
	class position_set
	{
	public:
		/// The empty set over a text of the specified length.
		explicit position_set(std::size_t text_length)
			: members(text_length + 1, false)
		{
		}

		bool contains(std::size_t position) const noexcept
		{
			return this->members[position];
		}

		void add(std::size_t position)
		{
			this->members[position] = true;
		}

		void add_all(const position_set& other)
		{
			for (std::size_t i = 0; i < this->members.size(); ++i)
			{
				if (other.members[i])
				{
					this->members[i] = true;
				}
			}
		}

		bool is_empty() const noexcept
		{
			for (const bool member : this->members)
			{
				if (member)
				{
					return false;
				}
			}

			return true;
		}

		/// One past the last position, which is the length of the text plus one.
		std::size_t extent() const noexcept
		{
			return this->members.size();
		}

		friend bool operator==(const position_set& left, const position_set& right) noexcept
		{
			return left.members == right.members;
		}

	private:
		std::vector<bool> members;
	};

	/// Whether `node` generates `text` exactly.
	///
	/// @param node The expression.
	/// @param text The string it must generate in full.
	/// @param too_long Whether the answer was abandoned as too large to compute.
	bool generates(const ast& node, std::string_view text, bool& too_long);

	/// The set of positions reachable by matching `node` from each of `starts`.
	///
	/// Working with sets of positions rather than backtracking keeps the cost polynomial. There
	/// are at most one more positions than there are characters, so an alternation cannot
	/// multiply the work.
	position_set advance(const ast& node, std::string_view text, const position_set& starts);
}

#endif
