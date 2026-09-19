// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_COMPILER_HPP
#define NAXP_COMPILER_HPP

#include "ast.hpp"
#include "fault.hpp"
#include "state_map.hpp"
#include "tx_machine.hpp"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <string_view>

namespace logmu::detail
{
	/// A naxp that has been parsed, checked and turned into machines.
	class compilation
	{
	public:
		/// @param accepted The machine for L, or null where it is the machine for C.
		/// @param canonical_machine The machine that canonicalises, or null where rho is the
		///     identity.
		compilation(
			std::string pattern,
			ast_ptr tree,
			std::unique_ptr<state_map> accepted,
			std::unique_ptr<state_map> canonical,
			std::unique_ptr<tx_machine> canonical_machine);

		compilation(const compilation&) = delete;
		compilation& operator=(const compilation&) = delete;

		/// The pattern this naxp was parsed from.
		const std::string& pattern() const noexcept
		{
			return this->pattern_text;
		}

		const ast& tree() const noexcept
		{
			return *this->syntax_tree;
		}

		/// The machine for the accepted language L, which is the canonical machine where rho is
		/// the identity.
		const state_map& accepted() const noexcept
		{
			return this->accepted_map == nullptr ? *this->canonical_map : *this->accepted_map;
		}

		/// The machine for the canonical language C, which the encoding ranks over.
		const state_map& canonical() const noexcept
		{
			return *this->canonical_map;
		}

		/// The largest encoded value, which is the size of C.
		std::uint64_t max_encoded_value() const noexcept
		{
			return this->canonical_map->string_count();
		}

		/// The count of strings the naxp accepts, which is the size of L.
		std::uint64_t accepted_count() const noexcept
		{
			return this->accepted().string_count();
		}

		/// The length of the longest string in C, which bounds every buffer a decode needs.
		std::size_t max_length() const noexcept
		{
			return this->longest;
		}

		/// Whether rho is the identity, so that every accepted string is its own canonical form.
		///
		/// True exactly when the tree holds no unified element, since that is the only thing
		/// that makes the canonical form differ from the input. Then C and L are the same
		/// language and encoding is a walk of the machine, with no canonicalisation.
		bool canonical_is_identity() const noexcept
		{
			return this->rho_machine == nullptr;
		}

		/// The machine that canonicalises, or null where rho is the identity and there is
		/// nothing to canonicalise.
		const tx_machine* canonical_machine() const noexcept
		{
			return this->rho_machine.get();
		}

		/// Whether the naxp accepts the specified string.
		///
		/// This walks the machine for L, which is one transition per character. `encode`
		/// answers the same question, but where the naxp has a unified element it
		/// canonicalises first and then ranks, so it is two walks rather than one and the
		/// wrong way round to ask it.
		bool accepts(std::string_view text) const noexcept
		{
			return this->accepted().accepts(text);
		}

		/// The encoded value of a string, which is zero exactly when the string is invalid.
		///
		/// Encoding cannot fail. Every rule is decided when the naxp is compiled, W3 among
		/// them, so the string either has one value or is not in the language.
		std::uint64_t encode(std::string_view text) const;

		/// The string a value stands for, which is a canonical form.
		///
		/// @param value The value, from 1 to `max_encoded_value()`.
		/// @param text Where the string goes. Untouched where the value is out of range.
		/// @returns Whether the value is one this naxp can produce.
		bool try_decode(std::uint64_t value, std::string& text) const;

		/// The canonical form of a string, which is the string with the match of each unified
		/// element replaced by that element's rendering.
		///
		/// @param text The string.
		/// @param canonical Where the canonical form goes. Untouched where the string is
		///     invalid.
		/// @returns Whether the naxp accepts the string.
		bool try_get_canonical_form(std::string_view text, std::string& canonical) const;

	private:
		std::string pattern_text;
		ast_ptr syntax_tree;
		std::unique_ptr<state_map> accepted_map;
		std::unique_ptr<state_map> canonical_map;
		std::unique_ptr<tx_machine> rho_machine;
		std::size_t longest;
	};

	/// Parses a naxp, checks it and builds its machines.
	///
	/// Every rule is checked here or below: W4 in the parser, W2 and W1 in the well-formedness
	/// check, W3 in the W3 checker and W5 from the size of the canonical language. A
	/// compilation that succeeds is a well-formed naxp.
	namespace compiler
	{
		bool try_compile(std::string_view pattern, std::unique_ptr<compilation>& result, std::optional<fault>& error);
	}
}

#endif
