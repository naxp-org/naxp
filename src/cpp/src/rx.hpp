// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_RX_HPP
#define NAXP_RX_HPP

#include "array_view.hpp"
#include "ascii_char_set.hpp"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <unordered_map>
#include <vector>

namespace logmu::detail
{
	/// What an `rx` node is.
	enum class rx_kind
	{
		/// The empty language, which arises only as a derivative.
		empty_set,

		/// The language holding only the empty string.
		epsilon,

		/// A non-empty set of characters matching one position.
		chars,

		/// Two or more expressions in sequence.
		concat,

		/// Two or more expressions in alternation.
		union_,

		/// An expression repeated between `min_count` and `max_count` times.
		interval,
	};

	/// An expression in the algebra the state map is built over.
	///
	/// This is deliberately not `ast`. The tree records what was written, whereas these nodes
	/// are what derivatives are taken of: they carry an empty language, they are normalised by
	/// their factory, and they are interned, so equal expressions are the same object and
	/// pointer equality can be relied on as a map key.
	///
	/// Normalisation does not have to reduce every expression denoting the same language to one
	/// form, and it does not. Making the machine canonical is the job of hash-consing on
	/// transition lists in the state map builder; normalisation here only keeps derivatives from
	/// growing and makes memoisation bite.
	///
	/// Intervals stay symbolic. Expanding `(A{99}){99}` into nearly ten thousand nodes would
	/// throw away the reason the count cap exists.
	class rx
	{
	public:
		rx(int id, rx_kind kind, ascii_char_set char_set, std::vector<const rx*> children, int min_count, int max_count, bool is_nullable, std::int64_t max_length)
			: id(id)
			, kind(kind)
			, char_set(char_set)
			, children(std::move(children))
			, min_count(min_count)
			, max_count(max_count)
			, is_nullable(is_nullable)
			, max_length(max_length)
		{
		}

		rx(const rx&) = delete;
		rx& operator=(const rx&) = delete;

		/// A number unique within the factory that made this node.
		int id;

		rx_kind kind;

		/// The characters, for `rx_kind::chars`.
		ascii_char_set char_set;

		/// The operands, for `concat`, `union_` and `interval`.
		std::vector<const rx*> children;

		int min_count;

		int max_count;

		/// Whether the language holds the empty string.
		bool is_nullable;

		/// The length of the longest string in the language, or zero where the language is
		/// empty.
		///
		/// Exact rather than an upper bound, and it strictly decreases along every derivative,
		/// which is what lets the builder order the states without a topological sort.
		std::int64_t max_length;

		/// The character sets that can match the first character of a string in this language.
		///
		/// These overlap in general. The minterms of them refine the first classes the
		/// specification defines, and the builder recovers the classes themselves by merging
		/// afterwards.
		const std::vector<ascii_char_set>& get_first_sets() const;

	private:
		void collect_first_sets(std::vector<ascii_char_set>& sets) const;

		mutable std::vector<ascii_char_set> first_sets;
		mutable bool first_sets_known = false;
	};

	/// Makes `rx` nodes, normalising and interning as it goes.
	///
	/// One factory per build, which owns every node it makes. Interning is not shared between
	/// naxps, so nothing accumulates and nothing needs locking.
	class rx_factory
	{
	public:
		rx_factory();

		rx_factory(const rx_factory&) = delete;
		rx_factory& operator=(const rx_factory&) = delete;

		/// The empty language.
		const rx* empty_set() const noexcept
		{
			return this->empty_set_node;
		}

		/// The language holding only the empty string.
		const rx* epsilon() const noexcept
		{
			return this->epsilon_node;
		}

		/// How many distinct expressions this factory has made.
		std::size_t count() const noexcept
		{
			return this->interned.size();
		}

		const rx* chars(ascii_char_set set);

		/// Concatenation, flattened, with the empty string dropped and the empty language
		/// absorbing.
		const rx* concat(array_view<const rx*> parts);

		const rx* concat(const rx* first, const rx* second);

		/// Alternation, flattened, with the empty language dropped and duplicates removed.
		///
		/// The operands are sorted by id. Ids differ between runs, but within one run two
		/// unions over the same operands sort the same way, which is all interning needs.
		const rx* union_(array_view<const rx*> alternatives);

		const rx* union_(const rx* first, const rx* second);

		/// Between `min_count` and `max_count` copies in sequence.
		const rx* interval(const rx* child, int min_count, int max_count);

		/// The derivative of `expression` after any character of `minterm`.
		///
		/// @param expression The expression to differentiate.
		/// @param minterm A minterm of the expression's first sets. Every character in it must
		///     behave alike, which is what makes one derivative stand for the whole set.
		/// @returns The derivative, which is `empty_set()` where nothing follows.
		const rx* derivative(const rx* expression, ascii_char_set minterm);

	private:
		/// The identity of an expression: its shape and its operands, which are already
		/// interned and so are compared by pointer.
		struct rx_key
		{
			rx_kind kind;
			ascii_char_set char_set;
			std::vector<const rx*> children;
			int min_count;
			int max_count;

			bool operator==(const rx_key& other) const noexcept
			{
				return this->kind == other.kind
					&& this->char_set == other.char_set
					&& this->children == other.children
					&& this->min_count == other.min_count
					&& this->max_count == other.max_count;
			}
		};

		struct rx_key_hash
		{
			std::size_t operator()(const rx_key& key) const noexcept;
		};

		struct derivative_key
		{
			int expression_id;
			ascii_char_set minterm;

			bool operator==(const derivative_key& other) const noexcept
			{
				return this->expression_id == other.expression_id && this->minterm == other.minterm;
			}
		};

		struct derivative_key_hash
		{
			std::size_t operator()(const derivative_key& key) const noexcept;
		};

		const rx* compute_derivative(const rx* expression, ascii_char_set minterm);

		const rx* intern(rx_kind kind, ascii_char_set char_set, std::vector<const rx*> children, int min_count, int max_count, bool is_nullable, std::int64_t max_length);

		std::vector<std::unique_ptr<rx>> nodes;
		std::unordered_map<rx_key, const rx*, rx_key_hash> interned;
		std::unordered_map<derivative_key, const rx*, derivative_key_hash> derivatives;
		int next_id = 0;
		const rx* empty_set_node = nullptr;
		const rx* epsilon_node = nullptr;
	};
}

#endif
