// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_AST_HPP
#define NAXP_AST_HPP

#include "ascii_char_set.hpp"

#include <cassert>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace logmu::detail
{
	/// Which node an `ast` is, so that a walk can switch on it. The order is that of the
	/// specification's grammar and nothing depends on it.
	enum class ast_kind
	{
		empty,
		chars,
		decimal_range,
		sequence,
		alternation,
		optional,
		interval,
		unified,
	};

	/// A node in the abstract syntax tree of a naxp.
	///
	/// The tree keeps the structure the pattern was written in. Intervals and decimal ranges are
	/// deliberately not expanded here. The cap on an interval count exists so that an
	/// implementation can find a naxp invalid before expanding it, and expanding at parse time
	/// would throw that away: `(A{99}){99}` is eleven characters of pattern and nearly ten
	/// thousand characters of expansion.
	///
	/// Groups do not survive parsing. `(A)` and `A` give the same tree, and the two abbreviated
	/// unified forms are expanded into the general one, since the specification defines them
	/// structurally rather than textually.
	///
	/// Nodes are shared through `ast_ptr` because the `x!!` form shares one subtree between its
	/// subject and its rendering. Nothing in the tree is mutated after parsing, apart from the
	/// pattern offset the parser sets while it builds, so that is safe.
	class ast
	{
	public:
		virtual ~ast() = default;

		ast(const ast&) = delete;
		ast& operator=(const ast&) = delete;

		ast_kind kind() const noexcept
		{
			return this->node_kind;
		}

		/// The offset in the pattern at which this node starts. Diagnostics only.
		std::size_t pattern_offset = 0;

		/// Whether the tree holds a unified element anywhere.
		///
		/// This decides two things at once, which is why it lives here rather than with either
		/// of them. Without a unified element rho is the identity, so W3 holds for nothing and
		/// the canonical language is the accepted one.
		static bool contains_unified(const ast& node);

	protected:
		explicit ast(ast_kind kind, std::size_t offset) noexcept
			: pattern_offset(offset)
			, node_kind(kind)
		{
		}

	private:
		ast_kind node_kind;
	};

	using ast_ptr = std::shared_ptr<ast>;

	/// The empty string, written `()`.
	class ast_empty final : public ast
	{
	public:
		explicit ast_empty(std::size_t offset) noexcept
			: ast(ast_kind::empty, offset)
		{
		}
	};

	/// A set of characters matching one position, such as `A`, `\9` or `[A-F]`.
	class ast_chars final : public ast
	{
	public:
		ast_chars(ascii_char_set char_set, std::size_t offset) noexcept
			: ast(ast_kind::chars, offset)
			, char_set(char_set)
		{
		}

		ascii_char_set char_set;
	};

	/// A decimal range, written `#[lo-hi]`.
	///
	/// The digit counts are the counts as written, which is what fixes the widths generated:
	/// `#[00-105]` does not match `7` while `#[0-105]` does.
	class ast_decimal_range final : public ast
	{
	public:
		ast_decimal_range(std::uint64_t low, int low_digit_count, std::uint64_t high, int high_digit_count, std::size_t offset) noexcept
			: ast(ast_kind::decimal_range, offset)
			, low(low)
			, low_digit_count(low_digit_count)
			, high(high)
			, high_digit_count(high_digit_count)
		{
		}

		std::uint64_t low;
		int low_digit_count;
		std::uint64_t high;
		int high_digit_count;
	};

	/// Two or more elements in sequence.
	class ast_sequence final : public ast
	{
	public:
		ast_sequence(std::vector<ast_ptr> children, std::size_t offset) noexcept
			: ast(ast_kind::sequence, offset)
			, children(std::move(children))
		{
		}

		std::vector<ast_ptr> children;
	};

	/// Two or more alternatives separated by `|`.
	class ast_alternation final : public ast
	{
	public:
		ast_alternation(std::vector<ast_ptr> children, std::size_t offset) noexcept
			: ast(ast_kind::alternation, offset)
			, children(std::move(children))
		{
		}

		std::vector<ast_ptr> children;
	};

	/// An optional element, written `x?`.
	class ast_optional final : public ast
	{
	public:
		ast_optional(ast_ptr child, std::size_t offset) noexcept
			: ast(ast_kind::optional, offset)
			, child(std::move(child))
		{
		}

		ast_ptr child;
	};

	/// A bounded interval, written `x{n}` or `x{m,n}`.
	class ast_interval final : public ast
	{
	public:
		ast_interval(ast_ptr child, int min_count, int max_count, std::size_t offset) noexcept
			: ast(ast_kind::interval, offset)
			, child(std::move(child))
			, min_count(min_count)
			, max_count(max_count)
		{
		}

		ast_ptr child;
		int min_count;
		int max_count;
	};

	/// How a unified element was written, which is needed only so that a well-formedness
	/// message can name the form the author used rather than the form it expands to.
	enum class unified_form
	{
		/// `x!y`, written in full.
		written,

		/// `x!!`, which expands to `x?!(x)`.
		reproduced,

		/// `x!?`, which expands to `x?!()`.
		dropped,

		/// One branch of a case fold, such as the `[Aa]!A` that `\CA` expands to.
		///
		/// The form records where the branch came from and changes no rule. An outer fold reaches
		/// a fold branch like any other unified element, so where two folds meet the outer
		/// governs and `\C(AB\cC)` prints `ABC`.
		fold,
	};

	/// A unified element, written `x!y`. Which of the strings the subject accepts was matched
	/// is not part of the encoding, and the rendering is printed in its place.
	///
	/// For the two abbreviated forms the subject is the `ast_optional` wrapping what was
	/// written, so `subject` is always the expression whose choice goes unencoded. The `x!!`
	/// form shares one subtree between `subject` and `rendering`.
	class ast_unified final : public ast
	{
	public:
		ast_unified(ast_ptr subject, ast_ptr rendering, unified_form form, std::size_t offset) noexcept
			: ast(ast_kind::unified, offset)
			, subject(std::move(subject))
			, rendering(std::move(rendering))
			, form(form)
		{
		}

		ast_ptr subject;
		ast_ptr rendering;
		unified_form form;
	};

	/// A node as one particular kind, which the caller has already established it is.
	template <typename Node>
	const Node& as(const ast& node) noexcept
	{
		assert(dynamic_cast<const Node*>(&node) != nullptr);

		return static_cast<const Node&>(node);
	}

	inline bool ast::contains_unified(const ast& node)
	{
		switch (node.kind())
		{
			case ast_kind::unified:
				return true;

			case ast_kind::sequence:
				for (const auto& child : as<ast_sequence>(node).children)
				{
					if (contains_unified(*child))
					{
						return true;
					}
				}

				return false;

			case ast_kind::alternation:
				for (const auto& child : as<ast_alternation>(node).children)
				{
					if (contains_unified(*child))
					{
						return true;
					}
				}

				return false;

			case ast_kind::optional:
				return contains_unified(*as<ast_optional>(node).child);

			case ast_kind::interval:
				return contains_unified(*as<ast_interval>(node).child);

			default:
				return false;
		}
	}
}

#endif
