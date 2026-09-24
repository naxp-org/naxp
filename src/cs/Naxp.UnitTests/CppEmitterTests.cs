// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Globalization;
using System.Text;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The C++ emitter: what its text says, and what its output does when g++ compiles it against the
/// conformance data with every warning fatal.
/// </summary>
public class CppEmitterTests
{
	static readonly ConformanceTestData TestData = ConformanceTestData.Load();

	static Compilation Compile(string naxp)
	{
		Assert.True(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error), error?.ToString());

		return compilation!;
	}

	static string Emit(string naxp, string prefix = "", NaxpValueType valueType = NaxpValueType.UInt64)
		=> CppEmitter.Instance.Emit(Compile(naxp), prefix, valueType);

	#region The text
	[Fact]
	public void Emit_SnakeCasesThePrefixOntoEveryName()
	{
		string source = Emit(@"\A\9", "Postcode");

		Assert.Contains("inline constexpr std::uint64_t postcode_max_encoded_value = 260ULL;", source, StringComparison.Ordinal);
		Assert.Contains("inline constexpr int postcode_max_length = 2;", source, StringComparison.Ordinal);
		Assert.Contains("inline bool postcode_accepts(std::string_view text)", source, StringComparison.Ordinal);
		Assert.Contains("inline std::uint64_t postcode_encode(std::string_view text)", source, StringComparison.Ordinal);
		Assert.Contains("inline std::string postcode_decode(std::uint64_t value)", source, StringComparison.Ordinal);
		Assert.Contains("inline bool postcode_try_decode(std::uint64_t value, std::string& text)", source, StringComparison.Ordinal);
		Assert.Contains("inline bool postcode_try_decode(std::uint64_t value, char* destination, std::size_t capacity, std::size_t& length)", source, StringComparison.Ordinal);
		Assert.Contains(@"inline constexpr std::string_view postcode_pattern = R""(\A\9)"";", source, StringComparison.Ordinal);
		Assert.Contains("inline std::optional<std::string> postcode_canonical_form(std::string_view text)", source, StringComparison.Ordinal);
		Assert.Contains("inline bool postcode_try_canonical_form(std::string_view text, std::string& canonical_form)", source, StringComparison.Ordinal);
		Assert.Contains("inline bool postcode_try_canonical_form(std::string_view text, char* destination, std::size_t capacity, std::size_t& length)", source, StringComparison.Ordinal);
	}

	/// <summary>A pattern that would close a raw string early is escaped instead.</summary>
	[Fact]
	public void Emit_EscapesAPatternARawStringCannotHold()
		=> Assert.Contains(@"inline constexpr std::string_view pattern = ""\\)\"""";", Emit(@"\)"""), StringComparison.Ordinal);

	[Fact]
	public void Emit_AllowsABlankPrefix()
	{
		string source = Emit(@"\A\9");

		Assert.Contains("inline bool accepts(std::string_view text)", source, StringComparison.Ordinal);
		Assert.Contains("inline constexpr std::uint64_t max_encoded_value = 260ULL;", source, StringComparison.Ordinal);
	}

	/// <summary>C++ needs a function declared before its first use, and the public functions come first.</summary>
	[Fact]
	public void Emit_DeclaresTheSteppersAheadOfThePublicFunctions()
	{
		string source = Emit(@"\A\9");

		int prototype = source.IndexOf("inline int accept_step(int state, int c);", StringComparison.Ordinal);
		int use = source.IndexOf("state = accept_step(state, static_cast<unsigned char>(c));", StringComparison.Ordinal);
		int definition = source.IndexOf("inline int accept_step(int state, int c)\n", StringComparison.Ordinal);

		Assert.True(prototype >= 0 && prototype < use && use < definition);
	}

	[Fact]
	public void Emit_NamesTheHeadersItNeeds()
	{
		string source = Emit(@"\A\9");

		Assert.StartsWith("// Needs <cstdint>, <optional>, <stdexcept>, <string> and <string_view>.", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_GroupsDigitsWithApostrophes()
	{
		string source = Emit(@"\A{12}");

		Assert.Contains("= 95'428'956'661'682'176ULL;", source, StringComparison.Ordinal);
	}

	/// <summary>The steppers take references where C takes pointers, and cast the C++ way.</summary>
	[Fact]
	public void Emit_WritesReferencesAndStaticCasts()
	{
		string source = Emit(@"\A\9");

		Assert.Contains("inline int encode_step(int state, int c, std::uint64_t& total)", source, StringComparison.Ordinal);
		Assert.Contains("inline int decode_step(int state, std::uint64_t& remaining, char* destination, int& length)", source, StringComparison.Ordinal);
		Assert.Contains("total += 10ULL * static_cast<std::uint64_t>(c - 'A');", source, StringComparison.Ordinal);
		Assert.Contains("destination[length++] = static_cast<char>('A' + static_cast<int>(index));", source, StringComparison.Ordinal);
		Assert.DoesNotContain("(*", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_MapsTheValueTypeOntoCstdint()
	{
		string source = Emit(@"\A\9", "Postcode", NaxpValueType.Int32);

		Assert.Contains("inline constexpr std::int32_t postcode_max_encoded_value = 260;", source, StringComparison.Ordinal);
		Assert.Contains("inline std::int32_t postcode_encode(std::string_view text)", source, StringComparison.Ordinal);
		Assert.Contains("return postcode_is_accepting(state) ? static_cast<std::int32_t>(total + 1ULL) : static_cast<std::int32_t>(0);", source, StringComparison.Ordinal);
		Assert.Contains("inline std::string postcode_decode(std::int32_t value)", source, StringComparison.Ordinal);
		Assert.Contains("postcode_decode_core(static_cast<std::uint64_t>(value), buffer)", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_RefusesAValueTypeTheNaxpOutgrows()
	{
		Compilation compilation = Compile(@"\A\9");

		Assert.Throws<ArgumentException>(() => CppEmitter.Instance.Emit(compilation, "", NaxpValueType.Int8));
	}

	[Fact]
	public void Emit_VoidsTheParametersARangeLeavesUnused()
	{
		string source = Emit("ABC");

		Assert.Contains("inline int encode_step(int state, int c, std::uint64_t& total)\n{\n\t(void)total;\n", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_CanonicalisesWhereTheNaxpReplaces()
	{
		string source = Emit(@"(B|b)!B");

		Assert.Contains("canonical_step(state, static_cast<unsigned char>(c), canonical, length);", source, StringComparison.Ordinal);
		Assert.Contains("return finish_canonical(state, canonical, length);", source, StringComparison.Ordinal);
		Assert.Contains("int length = canonicalise(text, canonical);", source, StringComparison.Ordinal);
		Assert.Contains("rank(canonical, length)", source, StringComparison.Ordinal);
		Assert.DoesNotContain(Tx.CopyMarker.ToString(), source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_ThrowsOutOfRangeFromDecode()
	{
		string source = Emit(@"\A\9");

		Assert.Contains("throw std::out_of_range(\"This naxp encodes the values 1 to 260.\");", source, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("1Bad")]
	[InlineData("Bad.Name")]
	[InlineData("Bad Name")]
	public void Emit_ThrowsOnABadPrefix(string prefix)
	{
		Compilation compilation = Compile("A");

		Assert.Throws<ArgumentException>(() => CppEmitter.Instance.Emit(compilation, prefix));
	}
	#endregion
	#region What it does when it runs
	/// <summary>
	/// Every naxp of the conformance data emitted into one file, compiled by g++ as C++17 with
	/// every warning fatal, and the whole of the test data checked by the result.
	/// </summary>
	[Fact]
	public void Generated_MatchesTheConformanceDataUnderGpp()
		=> NativeHarness.CompileAndRun("g++", "-std=c++17", "conformance.cpp", BuildHarness());

	/// <summary>The fragments for every conformance naxp, a table over them, and the checks.</summary>
	static string BuildHarness()
	{
		var builder = new StringBuilder();

		builder.Append("// Generated by CppEmitterTests. One fragment per conformance case.\n\n");
		builder.Append("#include <cstdint>\n#include <cstdio>\n#include <cstring>\n#include <optional>\n#include <stdexcept>\n#include <string>\n#include <string_view>\n\n");

		for (int i = 0; i < TestData.Cases.Count; ++i)
		{
			ConformanceCase item = TestData.Cases[i];

			builder.Append($"// {i.ToString(CultureInfo.InvariantCulture)}: {Emitter.CommentText(item.Naxp)}\n");
			CppEmitter.Instance.Emit(Compile(item.Naxp), Prefix(i), builder);
			builder.Append('\n');
		}

		builder.Append(Types);

		for (int i = 0; i < TestData.Cases.Count; ++i)
		{
			ConformanceCase item = TestData.Cases[i];

			builder.Append($"static const value_row {Snake(i)}_values[] =\n{{\n");

			foreach (ConformanceValue value in item.Values)
			{
				builder.Append($"\t{{ {NativeHarness.CString(value.In)}, {NativeHarness.Length(value.In)}, {value.Out.ToString(CultureInfo.InvariantCulture)}ULL, {NativeHarness.CString(value.Canon ?? string.Empty)}, {NativeHarness.Length(value.Canon ?? string.Empty)} }},\n");
			}

			foreach (string invalid in item.Invalid)
			{
				builder.Append($"\t{{ {NativeHarness.CString(invalid)}, {NativeHarness.Length(invalid)}, 0ULL, \"\", 0 }},\n");
			}

			// A trailing row, so the array is never empty and the count can be read off it.
			builder.Append("\t{ nullptr, 0, 0ULL, nullptr, 0 },\n};\n\n");
		}

		builder.Append("static const naxp_case cases[] =\n{\n");

		for (int i = 0; i < TestData.Cases.Count; ++i)
		{
			ConformanceCase item = TestData.Cases[i];
			string snake = Snake(i);

			builder.Append($"\t{{ {NativeHarness.CString(item.Naxp)}, {NativeHarness.Length(item.Naxp)}, {item.MaxEncodedValue.ToString(CultureInfo.InvariantCulture)}ULL, {snake}_max_encoded_value, {snake}_pattern, {snake}_accepts, {snake}_encode, {snake}_decode, {snake}_try_decode, {snake}_try_decode, {snake}_canonical_form, {snake}_try_canonical_form, {snake}_try_canonical_form, {snake}_values }},\n");
		}

		builder.Append("};\n\n");
		builder.Append(Driver);

		return builder.ToString();
	}

	static string Prefix(int index) => "Case" + index.ToString(CultureInfo.InvariantCulture);

	/// <summary>The prefix as the fragment spells it.</summary>
	static string Snake(int index) => "case" + index.ToString(CultureInfo.InvariantCulture);

	const string Types = """
		struct value_row
		{
			const char* in;
			std::size_t in_length;
			std::uint64_t out;
			const char* canon;
			std::size_t canon_length;
		};

		struct naxp_case
		{
			const char* naxp;
			std::size_t naxp_length;
			std::uint64_t max_encoded_value;
			std::uint64_t fragment_max_encoded_value;
			std::string_view pattern;
			bool (*accepts)(std::string_view);
			std::uint64_t (*encode)(std::string_view);
			std::string (*decode)(std::uint64_t);
			bool (*try_decode)(std::uint64_t, std::string&);
			bool (*try_decode_to)(std::uint64_t, char*, std::size_t, std::size_t&);
			std::optional<std::string> (*canonical_form)(std::string_view);
			bool (*try_canonical_form)(std::string_view, std::string&);
			bool (*try_canonical_form_to)(std::string_view, char*, std::size_t, std::size_t&);
			const value_row* values;
		};


		""";

	/// <summary>The checks, which are the same questions ConformanceTests asks the library.</summary>
	const string Driver = """
		static int checks = 0;
		static int failures = 0;

		static void check(bool condition, const char* naxp, const char* what, const char* text)
		{
			++checks;

			if (condition) { return; }

			if (++failures <= 40) { std::printf("  %s: %s '%s'\n", naxp, what, text); }
		}

		static bool throws_out_of_range(const naxp_case& c, std::uint64_t value)
		{
			try { c.decode(value); }
			catch (const std::out_of_range&) { return true; }
			catch (...) { return false; }

			return false;
		}

		int main()
		{
			std::size_t case_count = sizeof cases / sizeof cases[0];

			for (const naxp_case& c : cases)
			{
				check(c.fragment_max_encoded_value == c.max_encoded_value, c.naxp, "max_encoded_value", "");
				check(c.pattern == std::string_view(c.naxp, c.naxp_length), c.naxp, "pattern", "");

				for (const value_row* row = c.values; row->in != nullptr; ++row)
				{
					bool valid = row->out != 0ULL;
					std::string_view in(row->in, row->in_length);
					std::string canon(row->canon, row->canon_length);
					std::uint64_t encoded = c.encode(in);
					std::string text = "untouched";

					char out[2048];
					std::size_t length = 99;
					std::optional<std::string> canonical_form = c.canonical_form(in);

					check(encoded == row->out, c.naxp, "encode", row->in);
					check(c.accepts(in) == valid, c.naxp, "accepts", row->in);
					check(valid ? canonical_form == canon : !canonical_form.has_value(), c.naxp, "canonical_form", row->in);
					check(c.try_canonical_form(in, text) == valid && text == (valid ? canon : std::string("untouched")), c.naxp, "try_canonical_form", row->in);
					check(c.try_canonical_form_to(in, out, sizeof out, length) == valid
						&& (valid ? std::string_view(out, length) == canon : length == 0),
						c.naxp, "try_canonical_form into a buffer", row->in);

					if (!valid) { continue; }

					check(c.decode(encoded) == canon, c.naxp, "decode", row->in);
					check(c.try_decode(encoded, text) && text == canon, c.naxp, "try_decode", row->in);
					check(c.try_decode_to(encoded, out, sizeof out, length) && std::string_view(out, length) == canon, c.naxp, "try_decode into a buffer", row->in);
					check(c.encode(canon) == row->out, c.naxp, "round trip", row->in);

					// Too short by one, then exactly long enough.
					if (!canon.empty())
					{
						check(!c.try_decode_to(encoded, out, canon.size() - 1, length) && length == 0, c.naxp, "try_decode short", row->in);
						check(!c.try_canonical_form_to(in, out, canon.size() - 1, length) && length == 0, c.naxp, "try_canonical_form short", row->in);
					}

					check(c.try_decode_to(encoded, out, canon.size(), length) && length == canon.size(), c.naxp, "try_decode exact", row->in);
					check(c.try_canonical_form_to(in, out, canon.size(), length) && length == canon.size(), c.naxp, "try_canonical_form exact", row->in);
				}

				std::string text = "untouched";

				check(throws_out_of_range(c, 0ULL), c.naxp, "decode zero", "");
				check(!c.try_decode(0ULL, text) && text == "untouched", c.naxp, "try_decode zero", "");

				char out[16];
				std::size_t length = 99;

				check(!c.try_decode_to(0ULL, out, sizeof out, length) && length == 0, c.naxp, "try_decode into a buffer zero", "");

				if (c.max_encoded_value != UINT64_MAX)
				{
					check(throws_out_of_range(c, c.max_encoded_value + 1ULL), c.naxp, "decode past the end", "");
				}
			}

			if (failures != 0)
			{
				std::printf("%d of %d checks failed.\n", failures, checks);

				return 1;
			}

			std::printf("%d checks passed over %d naxps.\n", checks, static_cast<int>(case_count));

			return 0;
		}
		""";
	#endregion
}
