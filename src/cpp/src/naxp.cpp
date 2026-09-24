// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "naxp/naxp.hpp"

#include "c_emitter.hpp"
#include "compiler.hpp"
#include "cpp_emitter.hpp"
#include "csharp_emitter.hpp"
#include "emitter.hpp"
#include "fault.hpp"
#include "javascript_emitter.hpp"
#include "naxp_limits.hpp"
#include "relations.hpp"
#include "value_agreement.hpp"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>

namespace logmu
{
	namespace
	{
		/// The public fault for an internal one, which is where a fault that named no place in
		/// the naxp is given the whole of it, since only here is the length of the pattern known.
		naxp_fault to_public(const detail::fault& found, std::size_t pattern_length)
		{
			return naxp_fault{
				std::string(found.code()),
				found.text(),
				found.offset,
				found.is_whole_naxp() ? pattern_length : found.length};
		}

		std::string describe(const naxp_fault& parts)
		{
			// The code and the span are in the message because a thrown exception is all
			// anybody gets.
			return parts.code + " at " + std::to_string(parts.offset) + ".." + std::to_string(parts.offset + parts.length) + ": " + parts.message;
		}

		/// Copies the first `written` characters of a buffer into the caller's where they fit,
		/// and nothing at all where they do not or where `written` is -1 for a failure.
		bool copy_out(const char* buffer, int written, char* destination, std::size_t capacity, std::size_t& length) noexcept
		{
			if (written < 0 || static_cast<std::size_t>(written) > capacity)
			{
				length = 0;

				return false;
			}

			std::char_traits<char>::copy(destination, buffer, static_cast<std::size_t>(written));
			length = static_cast<std::size_t>(written);

			return true;
		}
	}

	naxp_error::naxp_error(naxp_fault parts)
		: std::invalid_argument(describe(parts))
		, parts(std::move(parts))
	{
	}

	naxp::naxp(std::shared_ptr<const detail::compilation> compiled)
		: compilation(std::move(compiled))
	{
	}

	naxp naxp::parse(std::string_view pattern)
	{
		naxp_fault found;
		std::optional<naxp> parsed = try_parse(pattern, &found);

		if (!parsed.has_value())
		{
			throw naxp_error(std::move(found));
		}

		return std::move(*parsed);
	}

	std::optional<naxp> naxp::try_parse(std::string_view pattern, naxp_fault* fault)
	{
		std::unique_ptr<detail::compilation> compiled;
		std::optional<detail::fault> error;

		if (detail::compiler::try_compile(pattern, compiled, error))
		{
			return naxp(std::shared_ptr<const detail::compilation>(std::move(compiled)));
		}

		if (fault != nullptr)
		{
			*fault = to_public(*error, pattern.size());
		}

		return std::nullopt;
	}

	std::string_view naxp::pattern() const noexcept
	{
		return this->compilation->pattern();
	}

	std::uint64_t naxp::max_encoded_value() const noexcept
	{
		return this->compilation->max_encoded_value();
	}

	std::size_t naxp::max_length() const noexcept
	{
		return this->compilation->max_length();
	}

	bool naxp::accepts(std::string_view text) const noexcept
	{
		return this->compilation->accepts(text);
	}

	std::uint64_t naxp::encode(std::string_view text) const
	{
		return this->compilation->encode(text);
	}

	std::string naxp::decode(std::uint64_t encoded_value) const
	{
		std::string text;

		if (!this->try_decode(encoded_value, text))
		{
			throw std::out_of_range("This naxp encodes the values 1 to " + std::to_string(this->max_encoded_value()) + ".");
		}

		return text;
	}

	bool naxp::try_decode(std::uint64_t encoded_value, std::string& text) const
	{
		return this->compilation->try_decode(encoded_value, text);
	}

	bool naxp::try_decode(std::uint64_t encoded_value, char* destination, std::size_t capacity, std::size_t& length) const
	{
		char buffer[detail::limits::max_string_length];

		return copy_out(buffer, this->compilation->decode(encoded_value, buffer), destination, capacity, length);
	}

	std::optional<std::string> naxp::canonical_form(std::string_view text) const
	{
		std::string canonical;

		if (!this->compilation->try_get_canonical_form(text, canonical))
		{
			return std::nullopt;
		}

		return canonical;
	}

	bool naxp::try_canonical_form(std::string_view text, std::string& canonical_form) const
	{
		return this->compilation->try_get_canonical_form(text, canonical_form);
	}

	bool naxp::try_canonical_form(std::string_view text, char* destination, std::size_t capacity, std::size_t& length) const
	{
		char buffer[detail::limits::max_string_length];

		return copy_out(buffer, this->compilation->canonicalise(text, buffer), destination, capacity, length);
	}

	std::string naxp::emit(
		output_language language,
		std::string_view prefix,
		naxp_value_type value_type,
		std::string_view initial_indent,
		std::string_view new_line,
		std::string_view indent) const
	{
		const detail::emitter* emitter = nullptr;

		switch (language)
		{
			case output_language::csharp: emitter = &detail::csharp_emitter::instance(); break;
			case output_language::javascript: emitter = &detail::javascript_emitter::instance(); break;
			case output_language::c: emitter = &detail::c_emitter::instance(); break;
			case output_language::cpp: emitter = &detail::cpp_emitter::instance(); break;
		}

		if (emitter == nullptr)
		{
			throw std::invalid_argument("The language is not an output language.");
		}

		return emitter->emit(*this->compilation, prefix, value_type, initial_indent, new_line, indent);
	}

	// The header carries the budget so that a caller can read it without reaching into the
	// library's private headers, which leaves two copies of one number. This is the guard.
	static_assert(naxp::default_budget == detail::value_agreement::max_tuples,
		"naxp::default_budget and detail::value_agreement::max_tuples have drifted apart.");

	naxp_comparison naxp::compare(const naxp& a, const naxp& b)
	{
		return compare(a, b, default_budget);
	}

	naxp_comparison naxp::compare(const naxp& a, const naxp& b, int budget)
	{
		naxp_comparison comparison;

		if (!try_compare(a, b, comparison, budget))
		{
			throw std::runtime_error("The relationship between the encodings of these two naxps could not be decided within the budget. Their accepted text and printed text can still be compared.");
		}

		return comparison;
	}

	bool naxp::try_compare(const naxp& a, const naxp& b, naxp_comparison& comparison)
	{
		return try_compare(a, b, comparison, default_budget);
	}

	bool naxp::try_compare(const naxp& a, const naxp& b, naxp_comparison& comparison, int budget)
	{
		comparison = naxp_comparison();

		const detail::compilation& left = *a.compilation;
		const detail::compilation& right = *b.compilation;

		// First, because it is the one that can fail, and nothing is worth computing if it does.
		set_relationship encoding = set_relationship::incomparable;

		if (!detail::relations::try_compare_encodings(left, right, encoding, budget))
		{
			return false;
		}

		comparison = naxp_comparison{
			detail::relations::compare_languages(left.accepted(), right.accepted()),
			encoding,
			detail::relations::compare_languages(left.canonical(), right.canonical())};

		return true;
	}

	std::uint64_t naxp::first_divergent_value(const naxp& a, const naxp& b)
	{
		return detail::relations::first_divergent_value(a.compilation->canonical(), b.compilation->canonical());
	}
}
