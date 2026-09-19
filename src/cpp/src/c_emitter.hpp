// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_C_EMITTER_HPP
#define NAXP_C_EMITTER_HPP

#include "c_family_emitter.hpp"
#include "code_writer.hpp"

#include <optional>
#include <string>

namespace logmu::detail
{
	/// Emits a compiled naxp as a C fragment, in C99.
	///
	/// The fragment is two constants, six public functions and their steppers, every name in
	/// snake_case under the caller's prefix. It needs `stdbool.h`, `stddef.h`, `stdint.h` and
	/// `string.h`, which the caller includes, and its first line says so. Everything has internal
	/// linkage, the public functions `static inline`, the steppers `static`, so the fragment can
	/// sit in a header or a source file alike and two translation units holding it never collide.
	///
	/// Text comes in two ways, as a pointer and a length and as a NUL-terminated string, each
	/// public function having a `_cstr` twin for the second. Decoding writes into the caller's
	/// buffer and reports its length rather than returning anything, which is the only shape C
	/// has, and mirrors the library's own C façade. The largest encoded value is a `static const`
	/// rather than a macro, which stays out of the caller's namespace; the longest length is an
	/// enum constant, because it sizes arrays and a `static const` cannot.
	class c_emitter final : public c_family_emitter
	{
	public:
		/// The shared instance, which is stateless and serves every call concurrently.
		static const c_emitter& instance();

	protected:
		std::string step_linkage() const override;
		std::string type_prefix() const override;

		/// C never adopted a digit separator.
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
		c_emitter() = default;

		void emit_accepts(fragment& fragment) const;
		void emit_encode(fragment& fragment) const;
		void emit_decode(fragment& fragment) const;
	};
}

#endif
