// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_CSHARP_EMITTER_HPP
#define NAXP_CSHARP_EMITTER_HPP

#include "code_writer.hpp"
#include "emitter.hpp"

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace logmu::detail
{
	/// Emits a compiled naxp as a C# fragment.
	///
	/// The fragment is a set of static members, three consts, the public methods and their private
	/// steppers, answering the same questions as `LogMu.Naxp` for its one naxp, without calling
	/// back into that library. Self-containment is a requirement rather than a taste: the code
	/// lands in the caller's assembly, where nothing internal to the library is visible, and the
	/// source generator that carries it cannot resolve package references of its own. The
	/// wrapping class, any namespace and any header comment are the caller's job. System types
	/// are spelt `global::System.…`, the source generator convention, so no using directive or
	/// shadowing type in the wrapper can disturb the fragment.
	///
	/// Every machine is emitted as a switch over states, never as a table. A table walk pays a
	/// popcount over two ulongs per character to rank the character within its set; in a switch
	/// the rank constant-folds into the arithmetic of its case.
	///
	/// A switch is split into methods of at most `emitter::chunk_size` states, dispatched by
	/// state number; that constant carries the per-method limits behind the split.
	///
	/// The fragment compiles as C# 7.3, which is what a .NET Framework project gets by default.
	/// The members that can give back null are therefore written twice under `#if`: annotated,
	/// with `NotNullWhen`, where the target framework has that attribute and so the language has
	/// nullable reference types, and plain elsewhere. C# has no symbol for its own version, so the
	/// framework is the test; a project on a modern framework that pins the language below C# 8
	/// is the one case it gets wrong.
	class csharp_emitter final : public emitter
	{
	public:
		/// The shared instance, which is stateless and serves every call concurrently.
		static const csharp_emitter& instance();

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
		csharp_emitter() = default;

		class fragment;

		// Expression helpers

		/// What passing this run adds to the total, or nothing where it adds nothing.
		std::optional<std::string> added_expression(std::uint64_t skipped, std::uint64_t count, char first, char last) const;

		/// The character at position `index` within a set, as an expression over its runs.
		std::string character_expression(const std::vector<char_run>& runs) const;

		std::string run_character(char_run run, std::uint64_t offset) const;

		static std::string char_literal(char c);

		/// An unsigned literal, grouped with underscores the way the C# codebase writes big
		/// numbers.
		std::string literal(std::uint64_t value) const;
	};
}

#endif
