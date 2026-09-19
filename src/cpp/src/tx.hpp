// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_TX_HPP
#define NAXP_TX_HPP

#include "array_view.hpp"
#include "ascii_char_set.hpp"
#include "ast.hpp"
#include "rx.hpp"

#include <cstddef>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace logmu::detail
{
	/// What a `tx` node is.
	enum class tx_kind
	{
		/// The empty relation, which arises only as a derivative.
		empty_set,

		/// Reads the empty string and emits nothing.
		epsilon,

		/// Reads one character of a set and emits that same character.
		chars,

		/// Reads any string of a subject and emits a fixed rendering when it completes.
		repl,

		/// Two or more in sequence.
		concat,

		/// Two or more in alternation.
		union_,

		/// One repeated between `min_count` and `max_count` times.
		interval,
	};

	/// Whether an expression has exactly one way of emitting at end of text.
	enum class eot_kind
	{
		/// There is no epsilon-parse, so the expression cannot accept end of text.
		none,

		/// Every epsilon-parse emits the same string.
		single,

		/// Two epsilon-parses emit different strings, which is a W3 violation wherever it is
		/// reached.
		multiple,

		/// Deciding would build a string longer than this implementation will materialise.
		too_long,
	};

	/// What an expression emits when it accepts the empty string.
	struct eot
	{
		eot_kind kind = eot_kind::none;

		/// The emitted string, for `single` only.
		std::string text;

		static eot none() noexcept
		{
			return eot{};
		}

		static eot multiple() noexcept
		{
			return eot{eot_kind::multiple, {}};
		}

		static eot too_long() noexcept
		{
			return eot{eot_kind::too_long, {}};
		}

		static eot empty()
		{
			return eot{eot_kind::single, {}};
		}

		static eot single(std::string text);

		/// The end of text behaviour of two expressions in sequence, which is the product of
		/// theirs.
		static eot concat(const eot& left, const eot& right);

		/// The end of text behaviour of two expressions in alternation, which is the union of
		/// theirs.
		static eot union_(const eot& left, const eot& right);
	};

	class tx;

	/// One way of consuming a block of characters: what was emitted, and what is left to do.
	struct tx_move
	{
		/// What this step emits. A copied character appears as `copy_marker` where the block
		/// holds more than one character, since which character was read is not yet decided.
		std::string emitted;

		const tx* residual;
	};

	/// The result of differentiating, cached whole because the ambiguity flag belongs to the
	/// step rather than to any one move.
	struct tx_derivative
	{
		std::vector<tx_move> moves;

		/// Whether a nullable element was skipped over that emits two different strings at end
		/// of text, with a live continuation beyond it.
		///
		/// That is a W3 violation on its own. The two skips give one input two outputs, and the
		/// continuation is non-empty because empty residuals are dropped, so both parses reach
		/// an accepting string.
		bool skips_ambiguously = false;

		/// Whether the step was abandoned as too large to compute.
		bool too_long = false;
	};

	/// Stands for a copied character whose identity the block has not yet fixed.
	///
	/// Outside ASCII, so it cannot collide with anything a naxp emits. Where one of these
	/// survives into a delay the comparison it takes part in is undecided, and the W3 checker
	/// retries that step one character at a time.
	inline constexpr char copy_marker = static_cast<char>(0xFF);

	/// The transduction rho as an expression, so that derivatives of it can be taken.
	///
	/// This is what the rx converter throws away. There a unified element becomes either its
	/// subject or its rendering, depending on which language is being built, and W3 is exactly
	/// the question of how the two behave together. `tx_kind::repl` is the node that keeps them
	/// paired.
	///
	/// Emission is deferred to the end of the element. A unified consumes its subject one
	/// character at a time emitting nothing, then emits the whole rendering when it completes,
	/// which is why the difference between two branches' outputs has to be carried as a delay
	/// rather than compared character by character.
	///
	/// Nodes are interned by their factory, so pointer equality is structural equality and a
	/// node can be a map key.
	class tx
	{
	public:
		tx(int id, tx_kind kind, ascii_char_set char_set, const rx* subject, std::string rendering, std::vector<const tx*> children, int min_count, int max_count, bool is_nullable)
			: id(id)
			, kind(kind)
			, char_set(char_set)
			, subject(subject)
			, rendering(std::move(rendering))
			, children(std::move(children))
			, min_count(min_count)
			, max_count(max_count)
			, is_nullable(is_nullable)
		{
		}

		tx(const tx&) = delete;
		tx& operator=(const tx&) = delete;

		/// A number unique within the factory that made this node.
		int id;

		tx_kind kind;

		/// The characters, for `chars`.
		ascii_char_set char_set;

		/// What is consumed, for `repl`.
		const rx* subject;

		/// What is emitted, for `repl`. One string, by W1.
		std::string rendering;

		std::vector<const tx*> children;

		int min_count;

		int max_count;

		/// Whether the empty string can be consumed. This is about input alone.
		bool is_nullable;

		/// What is emitted where the empty string is consumed.
		const eot& get_eot() const;

		/// The character sets that can match the first character consumed.
		const std::vector<ascii_char_set>& get_first_sets() const;

	private:
		eot compute_eot() const;

		void collect_first_sets(std::vector<ascii_char_set>& sets) const;

		mutable std::optional<eot> end_of_text;
		mutable std::vector<ascii_char_set> first_sets;
		mutable bool first_sets_known = false;
	};

	/// Makes `tx` nodes, normalising and interning as it goes, and differentiates them.
	class tx_factory
	{
	public:
		explicit tx_factory(rx_factory& rx_factory);

		tx_factory(const tx_factory&) = delete;
		tx_factory& operator=(const tx_factory&) = delete;

		const tx* empty_set() const noexcept
		{
			return this->empty_set_node;
		}

		const tx* epsilon() const noexcept
		{
			return this->epsilon_node;
		}

		/// How many distinct expressions this factory has made.
		std::size_t count() const noexcept
		{
			return this->interned.size();
		}

		/// Every character that appears in some rendering.
		///
		/// Splitting these out as singleton blocks is what makes emission uniform over a block.
		/// A character set emits the character read and a unified element emits a fixed string,
		/// so whether the two agree depends on which character of the block was read: in
		/// `[ab]|[ab]!a` they agree on `a` and disagree on `b`. Refining costs transitions,
		/// never states, and cannot change what is accepted, since the input side is already
		/// uniform over the coarser blocks.
		const std::vector<char>& rendering_characters() const noexcept
		{
			return this->rendering_chars;
		}

		const tx* chars(ascii_char_set set);

		/// A unified element: consume any string of `subject`, emit `rendering`.
		const tx* repl(const rx* subject, std::string_view rendering);

		const tx* concat(array_view<const tx*> parts);

		const tx* concat(const tx* first, const tx* second);

		/// Duplicates are removed by identity, which is safe: two identical alternatives are one
		/// parse repeated, not two parses, so removing one removes no output.
		const tx* union_(array_view<const tx*> alternatives);

		const tx* union_(const tx* first, const tx* second);

		const tx* interval(const tx* child, int min_count, int max_count);

		/// Every way of consuming one character of `block`.
		///
		/// @param expression The expression to differentiate.
		/// @param block A block of characters that behave alike on the input side, and on the
		///     output side too once `rendering_characters()` have been split out.
		/// @returns The moves, with the emitted string of each.
		const tx_derivative& derivative(const tx* expression, ascii_char_set block);

	private:
		/// The most skipped copies of an interval this implementation will follow separately.
		///
		/// Only reached where skipping a copy emits, which needs a unified element with a
		/// nullable subject inside an interval whose count can vary. Nothing a naxp is for goes
		/// near it, and a naxp that does breaks W6 rather than being judged either way.
		static constexpr int max_skipped_copies = 64;

		struct tx_key
		{
			tx_kind kind;
			ascii_char_set char_set;
			const rx* subject;
			std::string rendering;
			std::vector<const tx*> children;
			int min_count;
			int max_count;

			bool operator==(const tx_key& other) const noexcept
			{
				return this->kind == other.kind
					&& this->char_set == other.char_set
					&& this->subject == other.subject
					&& this->rendering == other.rendering
					&& this->children == other.children
					&& this->min_count == other.min_count
					&& this->max_count == other.max_count;
			}
		};

		struct tx_key_hash
		{
			std::size_t operator()(const tx_key& key) const noexcept;
		};

		struct derivative_key
		{
			int expression_id;
			ascii_char_set block;

			bool operator==(const derivative_key& other) const noexcept
			{
				return this->expression_id == other.expression_id && this->block == other.block;
			}
		};

		struct derivative_key_hash
		{
			std::size_t operator()(const derivative_key& key) const noexcept;
		};

		tx_derivative compute_derivative(const tx* expression, ascii_char_set block);

		const tx* intern(tx_kind kind, ascii_char_set char_set, const rx* subject, std::string rendering, std::vector<const tx*> children, int min_count, int max_count, bool is_nullable);

		rx_factory& rxs;
		std::vector<std::unique_ptr<tx>> nodes;
		std::unordered_map<tx_key, const tx*, tx_key_hash> interned;
		std::unordered_map<derivative_key, std::unique_ptr<tx_derivative>, derivative_key_hash> derivatives;
		std::vector<char> rendering_chars;
		int next_id = 0;
		const tx* empty_set_node = nullptr;
		const tx* epsilon_node = nullptr;
	};

	/// Turns a parsed naxp into the transducer algebra.
	namespace tx_converter
	{
		const tx* convert(const ast& node, tx_factory& factory, rx_factory& rxs);
	}
}

#endif
