// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_NAXP_HPP
#define NAXP_NAXP_HPP

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>

namespace logmu
{
	namespace detail
	{
		class compilation;
	}

	/// A fault found in a pattern: what is wrong, where, and a stable code naming it.
	///
	/// An offset and a length of zero together mean the naxp as a whole rather than any one
	/// place in it, which is what most faults outside the parser want. Every fault that does
	/// name a place has a length of at least one.
	struct naxp_fault
	{
		/// The stable identifier for this fault, such as `NAXP1002`. Diagnostics only: it is
		/// here so that a log or a bug report names the fault without quoting the prose.
		std::string code;

		/// What is wrong, and where practical what to write instead.
		std::string message;

		/// Where the fault starts, in characters from the start of the pattern.
		std::size_t offset = 0;

		/// How much of the pattern is at fault, which is the whole of it where the fault
		/// belongs to the naxp rather than to any one place in it.
		std::size_t length = 0;
	};

	/// How one set stands to another, for two sets called `a` and `b`.
	enum class set_relationship : std::uint8_t
	{
		/// Neither set contains the other. They may be wholly disjoint or overlap in part.
		///
		/// This is the default so that a comparison which was never made says nothing rather
		/// than claiming the two sets are alike.
		incomparable = 0,

		/// The two sets have exactly the same members.
		equal = 1,

		/// Every member of `a` is a member of `b`, and `b` has at least one more.
		subset_of = 2,

		/// Every member of `b` is a member of `a`, and `a` has at least one more.
		superset_of = 3,
	};

	/// How one naxp stands to another, as three set relationships, each from the first naxp's
	/// point of view.
	///
	/// The three are independent and each answers a different question about replacing the
	/// first naxp with the second. `accepted_text` says whether text that was valid stays
	/// valid. `encoding` says whether values already stored still mean what they meant.
	/// `printed_text` says whether what is printed on decoding can change.
	///
	/// `encoding` is the strongest of the three: `equal` or `subset_of` there forces the same
	/// on `accepted_text`, since the pairs' texts are the accepted strings. Nothing else
	/// follows. Two naxps can give every string the same value and print it differently, as
	/// `(A|B)!A` and `(A|B)!B` do, and can print the same strings and value a shared one
	/// differently.
	///
	/// The default is `incomparable` on every axis, which is what a comparison that was never
	/// made should say.
	struct naxp_comparison
	{
		/// How the set of strings the first naxp accepts stands to the second's.
		set_relationship accepted_text = set_relationship::incomparable;

		/// How the set of (text, value) pairs the first naxp defines stands to the second's.
		set_relationship encoding = set_relationship::incomparable;

		/// How the set of strings the first naxp prints, its canonical forms, stands to the
		/// second's. These are compared as sets of strings, so two naxps that print every
		/// value in different forms are incomparable here however closely the forms
		/// correspond.
		set_relationship printed_text = set_relationship::incomparable;

		friend bool operator==(const naxp_comparison& left, const naxp_comparison& right) noexcept
		{
			return left.accepted_text == right.accepted_text
				&& left.encoding == right.encoding
				&& left.printed_text == right.printed_text;
		}

		friend bool operator!=(const naxp_comparison& left, const naxp_comparison& right) noexcept
		{
			return !(left == right);
		}
	};

	/// The language a naxp is emitted as source in.
	///
	/// Named for what it is rather than called a language on its own, because in a naxp's own
	/// vocabulary a language is a set of strings: the accepted language L and the canonical
	/// language C. This is the other sense.
	enum class output_language : std::uint8_t
	{
		/// C#, as a fragment of static members.
		csharp = 0,

		/// JavaScript, as a fragment of const and function declarations.
		javascript = 1,

		/// C, C99, as a fragment of static functions.
		c = 2,

		/// C++, C++17, as a fragment of inline functions that can sit in a header.
		cpp = 3,
	};

	/// The integer type generated code uses for encoded values. Each holds every encoded value
	/// up to its own largest, which `naxp::emit` checks the naxp against.
	enum class naxp_value_type : std::uint8_t
	{
		/// A signed 8-bit integer, holding encoded values to 127.
		int8 = 0,

		/// An unsigned 8-bit integer, holding encoded values to 255.
		uint8 = 1,

		/// A signed 16-bit integer, holding encoded values to 32 767.
		int16 = 2,

		/// An unsigned 16-bit integer, holding encoded values to 65 535.
		uint16 = 3,

		/// A signed 32-bit integer, holding encoded values to 2 147 483 647.
		int32 = 4,

		/// An unsigned 32-bit integer, holding encoded values to 4 294 967 295.
		uint32 = 5,

		/// A signed 64-bit integer, holding encoded values to 9 223 372 036 854 775 807.
		int64 = 6,

		/// An unsigned 64-bit integer, holding every encoded value any naxp produces. The
		/// default.
		uint64 = 7,
	};

	/// Thrown by `naxp::parse` where the pattern is not a well-formed naxp.
	///
	/// The message is the fault's code, offset, length and prose in one line, because a thrown
	/// exception is all a caller of `parse` gets; the fault itself is kept for callers that want
	/// the parts.
	class naxp_error : public std::invalid_argument
	{
	public:
		explicit naxp_error(naxp_fault parts);

		/// The fault, in its parts.
		const naxp_fault& fault() const noexcept
		{
			return this->parts;
		}

	private:
		naxp_fault parts;
	};

	/// A parsed naxp: a recogniser for its text and a codec between that text and the unsigned
	/// integers from 1 to `max_encoded_value()`.
	///
	/// A `naxp` is a handle over an immutable compilation, so copying one is cheap and every
	/// copy is the same naxp. It is safe to use from several threads at once.
	class naxp
	{
	public:
		/// The budget the `compare` and `try_compare` overloads that do not take one use, which
		/// is 200 000 product states.
		///
		/// Here so that a caller passing a budget of its own can scale from this one rather
		/// than write the number itself. The library checks this against the figure it actually
		/// walks to, so the two cannot drift apart.
		static constexpr int default_budget = 200'000;

		/// Parses a naxp.
		///
		/// @param pattern The pattern of the naxp.
		/// @returns The naxp.
		/// @throws naxp_error The pattern is not a well-formed naxp.
		static naxp parse(std::string_view pattern);

		/// Tries to parse a naxp, or says what is wrong, where, and why it is invalid.
		///
		/// @param pattern The pattern of the naxp.
		/// @param fault Where to put the fault where the pattern is invalid, or null where the
		///     caller only wants to know whether it parsed. Untouched where it did.
		/// @returns The naxp, or nothing where the pattern is not a well-formed naxp.
		static std::optional<naxp> try_parse(std::string_view pattern, naxp_fault* fault = nullptr);

		/// The pattern this naxp was parsed from.
		std::string_view pattern() const noexcept;

		/// The largest encoded value this naxp produces. Encoded values run from 1 with no
		/// gaps, so this is also how many there are.
		std::uint64_t max_encoded_value() const noexcept;

		/// The length of the longest text this naxp decodes a value to. A buffer this long
		/// takes anything `decode` or `canonical_form` writes.
		std::size_t max_length() const noexcept;

		/// Whether this naxp accepts the specified text. A byte outside ASCII is never
		/// accepted.
		///
		/// @param text The text to test.
		/// @returns Whether the naxp accepts it.
		bool accepts(std::string_view text) const noexcept;

		/// The encoded value of text.
		///
		/// @param text The text to encode.
		/// @returns The encoded value, from 1 to `max_encoded_value()`, or zero where the text
		///     is invalid for this naxp.
		std::uint64_t encode(std::string_view text) const;

		/// The text an encoded value stands for.
		///
		/// @param encoded_value The encoded value, from 1 to `max_encoded_value()`.
		/// @returns The text, which is in canonical form.
		/// @throws std::out_of_range The value is not one this naxp produces.
		std::string decode(std::uint64_t encoded_value) const;

		/// Tries to find the text an encoded value stands for.
		///
		/// @param encoded_value The encoded value.
		/// @param text Where the text goes, which is in canonical form. Untouched where the
		///     value is not one this naxp produces.
		/// @returns Whether the value is one this naxp produces.
		bool try_decode(std::uint64_t encoded_value, std::string& text) const;

		/// The canonical form of text, which is the text `decode` gives back for its encoded
		/// value.
		///
		/// @param text The text.
		/// @returns The canonical form, or nothing where the text is invalid for this naxp.
		std::optional<std::string> canonical_form(std::string_view text) const;

		/// Emits this naxp as source in the specified language.
		///
		/// What comes back is a fragment: the declarations answering the same questions this
		/// class does, for this one naxp, calling back into nothing. The paraphernalia around
		/// it, a header comment, includes, a namespace or a wrapping class, is the caller's,
		/// which is what lets one fragment land in a header and on a web page alike.
		///
		/// Everything a naxp decides is decided when it is compiled, so generated code carries
		/// no dependency on this library at all.
		///
		/// @param language The language to emit.
		/// @param prefix The prefix every generated name starts with, so several naxps can
		///     share one scope. Empty gives the bare names. Each language applies its own casing
		///     convention to it.
		/// @param value_type The integer type the generated code uses for encoded values.
		///     `max_encoded_value()` must fit it.
		/// @param initial_indent What every line is indented with ahead of its own depth, so
		///     the fragment can sit inside an already-indented wrapper.
		/// @param new_line What ends every line. The caller's choice rather than the
		///     platform's, so that one naxp gives the same fragment on every machine.
		/// @param indent What one level of indentation is written as.
		/// @returns The fragment.
		/// @throws std::invalid_argument The language is not an output language, the prefix is
		///     neither empty nor an ASCII identifier, or the largest encoded value does not fit
		///     the value type.
		std::string emit(
			output_language language,
			std::string_view prefix = "",
			naxp_value_type value_type = naxp_value_type::uint64,
			std::string_view initial_indent = "",
			std::string_view new_line = "\n",
			std::string_view indent = "\t") const;

		/// How `b` stands to `a`: what happens to text and values held under `a` if `b`
		/// replaces it.
		///
		/// Three set relationships, each from `a`'s point of view: the strings each accepts,
		/// the (text, value) pairs each defines, and the strings each prints. A change is safe
		/// for stored values exactly when `naxp_comparison::encoding` is `equal` or
		/// `subset_of`. See `naxp_comparison` for how the three relate.
		///
		/// The encoding relationship is decided by walking the two naxps together, which has a
		/// budget. No naxp anybody has reason to write comes near it, but a pair that does
		/// cannot be decided, and this throws rather than guess; `try_compare` returns false
		/// instead. The other two relationships are always decidable.
		///
		/// @param a The naxp the data was encoded with.
		/// @param b The naxp proposed to replace it.
		/// @returns The comparison.
		/// @throws std::runtime_error The encoding relationship could not be decided within
		///     the budget.
		static naxp_comparison compare(const naxp& a, const naxp& b);

		/// `compare` with the budget given rather than left at its default.
		///
		/// The budget caps how many product states the walk deciding the encoding relationship
		/// may build, and defaults to 200 000 in the overload that does not take it. Raising it
		/// buys nothing on any naxp anybody has reason to write, since those settle in a few
		/// hundred.
		///
		/// @param a The naxp the data was encoded with.
		/// @param b The naxp proposed to replace it.
		/// @param budget How many product states the walk may build.
		/// @returns The comparison.
		/// @throws std::runtime_error The encoding relationship could not be decided within
		///     `budget`.
		static naxp_comparison compare(const naxp& a, const naxp& b, int budget);

		/// Tries to find how `b` stands to `a`.
		///
		/// @param a The naxp the data was encoded with.
		/// @param b The naxp proposed to replace it.
		/// @param comparison The comparison, if this returns true; otherwise the default,
		///     which is `incomparable` on every axis and claims nothing.
		/// @returns Whether the comparison was decided. Only the encoding relationship can fail
		///     to be, when the walk that decides it outgrows its budget.
		static bool try_compare(const naxp& a, const naxp& b, naxp_comparison& comparison);

		/// `try_compare` with the budget given rather than left at its default.
		///
		/// A budget too small for the pair leaves the comparison undecided rather than wrong.
		///
		/// @param a The naxp the data was encoded with.
		/// @param b The naxp proposed to replace it.
		/// @param comparison The comparison, if this returns true; otherwise the default,
		///     which is `incomparable` on every axis and claims nothing.
		/// @param budget How many product states the walk may build.
		/// @returns Whether the comparison was decided. Only the encoding relationship can fail
		///     to be, when the walk that decides it outgrows `budget`.
		static bool try_compare(const naxp& a, const naxp& b, naxp_comparison& comparison, int budget);

		/// The lowest value both naxps hold that they decode to different strings, or zero
		/// where every value both hold decodes alike.
		///
		/// This is a different question from `compare`, which is about the text going in; this
		/// is about the text coming out. `(A|B)!A` and `(A|B)!B` have equal encodings and
		/// diverge at 1, decoding it to `A` and to `B`. Decode the value under each naxp to see
		/// the two forms.
		///
		/// Values only one naxp holds do not count, so a naxp extended by values that sort
		/// after all of its own gives zero however many were added; `max_encoded_value()` says
		/// the rest.
		static std::uint64_t first_divergent_value(const naxp& a, const naxp& b);

	private:
		explicit naxp(std::shared_ptr<const detail::compilation> compilation);

		std::shared_ptr<const detail::compilation> compilation;
	};
}

#endif
