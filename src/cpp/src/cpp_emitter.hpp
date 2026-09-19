// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_CPP_EMITTER_HPP
#define NAXP_CPP_EMITTER_HPP

#include "c_family_emitter.hpp"
#include "code_writer.hpp"

#include <optional>
#include <string>

namespace logmu::detail
{
	/// Emits a compiled naxp as a C++ fragment, in C++17.
	///
	/// The fragment is two constants, four public functions and their steppers, every name in
	/// snake_case under the caller's prefix, and its surface is the library's own `logmu::naxp`:
	/// a `string_view` in, a `std::string` out, `decode` throwing `std::out_of_range` and
	/// `try_decode` reporting instead. It needs `cstdint`, `stdexcept`, `string` and
	/// `string_view`, which the caller includes, and its first line says so.
	///
	/// Every function is `inline` and both constants `inline constexpr`, so the fragment can sit
	/// in a header that several translation units include. That is also why the caller gives each
	/// naxp its own prefix where several share a program: two inline functions of one name and
	/// different bodies in different translation units break the one definition rule, and no
	/// compiler is obliged to notice.
	class cpp_emitter final : public c_family_emitter
	{
	public:
		/// The shared instance, which is stateless and serves every call concurrently.
		static const cpp_emitter& instance();

	protected:
		std::string step_linkage() const override;
		std::string type_prefix() const override;

		/// C++14's digit separator.
		std::optional<std::string> digit_separator() const override;

		std::string pointer(const std::string& type, const std::string& name) const override;
		std::string by_reference(const std::string& type, const std::string& name) const override;
		std::string dereference(const std::string& name) const override;
		std::string increment(const std::string& name) const override;
		std::string address_of(const std::string& name) const override;
		std::string cast(const std::string& type, const std::string& expression) const override;
		void comment(code_writer& writer, const std::string& text) const override;

		void emit_header(fragment& fragment) const override;
		void emit_publics(fragment& fragment) const override;

	private:
		cpp_emitter() = default;

		void emit_accepts(fragment& fragment) const;
		void emit_encode(fragment& fragment) const;
		void emit_decode(fragment& fragment) const;
	};
}

#endif
