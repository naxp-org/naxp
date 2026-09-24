// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_JAVASCRIPT_EMITTER_HPP
#define NAXP_JAVASCRIPT_EMITTER_HPP

#include "code_writer.hpp"
#include "emitter.hpp"

#include <cstdint>
#include <string>
#include <string_view>

namespace logmu::detail
{
	/// The JavaScript emitter: one naxp as a fragment of declarations, in the shape a module or a
	/// script tag can hold.
	///
	/// The fragment is a set of const and function declarations, three consts, the public
	/// functions and their private steppers, every name prefixed with the caller's prefix and
	/// camel cased, which is what JavaScript readers expect. Nothing is exported: the module
	/// wrapper, the export list and any header comment are the caller's job.
	///
	/// Characters are handled as ASCII code points rather than one-character strings, so the
	/// steppers compare numbers and bytes feed straight in. That is why each public function
	/// takes a string or a `Uint8Array` alike, as the library's `Naxp` does, where the C# emitter
	/// needs an overload and a cast.
	///
	/// JavaScript has one number type, exact to 2^53 - 1, so the emitter reads the naxp's largest
	/// encoded value and picks: ordinary numbers where every value and every intermediate rank
	/// fits, BigInt above that. Ranks are bounded by that, so in the number case the arithmetic
	/// is exact. The BigInt case needs ES2020; everything else needs ES2015.
	class javascript_emitter final : public emitter
	{
	public:
		/// The largest integer a JavaScript number holds exactly, `Number.MAX_SAFE_INTEGER`.
		static constexpr std::uint64_t max_safe_integer = 9007199254740991ULL;

		/// The shared instance, which is stateless and serves every call concurrently.
		static const javascript_emitter& instance();

	protected:
		void emit_fragment(emit_context& context) const override;

		void open_function(code_writer& writer, std::string_view name, std::string_view parameters) const override;
		void close_function(code_writer& writer) const override;
		void open_dispatch(code_writer& writer) const override;
		void close_dispatch(code_writer& writer, std::string_view result) const override;
		void write_return(code_writer& writer, std::string_view expression) const override;
		void write_guarded_return(code_writer& writer, std::string_view condition, std::string_view expression) const override;
		std::string equals_character(char c) const override;
		std::string within_run(char first, char last) const override;

	private:
		/// JavaScript puts an opening brace at the end of the line it belongs to, so the fragment
		/// writes its own braces and takes only the indenting from `code_writer`.
		javascript_emitter();

		class fragment;

		/// An ASCII code point, in hexadecimal. Bare, with no comment naming the character: the
		/// comparisons sit two or three to a line, and the annotations cost more in noise than
		/// they return in clarity when `charCodeAt` is in plain view above.
		static std::string code_literal(char c);

		/// The prefix and a member name, camel cased as JavaScript writes function names.
		static std::string camel(const std::string& prefix, const std::string& member);
	};
}

#endif
