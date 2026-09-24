// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Globalization;
using System.Text;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The C emitter: what its text says, and what its output does when gcc compiles it against the
/// conformance data with every warning fatal.
/// </summary>
public class CEmitterTests
{
	static readonly ConformanceTestData TestData = ConformanceTestData.Load();

	static Compilation Compile(string naxp)
	{
		Assert.True(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error), error?.ToString());

		return compilation!;
	}

	static string Emit(string naxp, string prefix = "", NaxpValueType valueType = NaxpValueType.UInt64)
		=> CEmitter.Instance.Emit(Compile(naxp), prefix, valueType);

	#region The text
	[Fact]
	public void Emit_SnakeCasesThePrefixOntoEveryName()
	{
		string source = Emit(@"\A\9", "Postcode");

		Assert.Contains("static const uint64_t postcode_max_encoded_value = 260ULL;", source, StringComparison.Ordinal);
		Assert.Contains("enum { postcode_max_length = 2 };", source, StringComparison.Ordinal);
		Assert.Contains("static inline bool postcode_accepts(const char *text, size_t text_length)", source, StringComparison.Ordinal);
		Assert.Contains("static inline bool postcode_accepts_cstr(const char *text)", source, StringComparison.Ordinal);
		Assert.Contains("static inline uint64_t postcode_encode(const char *text, size_t text_length)", source, StringComparison.Ordinal);
		Assert.Contains("static inline uint64_t postcode_encode_cstr(const char *text)", source, StringComparison.Ordinal);
		Assert.Contains("static inline bool postcode_decode(uint64_t value, char *destination, size_t capacity, size_t *length)", source, StringComparison.Ordinal);
		Assert.Contains("static inline bool postcode_decode_cstr(uint64_t value, char *destination, size_t capacity)", source, StringComparison.Ordinal);
		Assert.Contains("static inline const char *postcode_pattern(void)", source, StringComparison.Ordinal);
		Assert.Contains("static inline bool postcode_canonical_form(const char *text, size_t text_length, char *destination, size_t capacity, size_t *length)", source, StringComparison.Ordinal);
		Assert.Contains("static inline bool postcode_canonical_form_cstr(const char *text, char *destination, size_t capacity)", source, StringComparison.Ordinal);
	}

	/// <summary>A question mark after another is escaped, so that no trigraph can form.</summary>
	[Fact]
	public void Emit_EscapesTheSecondOfTwoQuestionMarks()
		=> Assert.Contains(@"return ""A\\?\?"";", Emit(@"A\??"), StringComparison.Ordinal);

	/// <summary>A run of capitals is one word until the letter that starts the next word.</summary>
	[Theory]
	[InlineData("UKPostcode", "uk_postcode_accepts")]
	[InlineData("Postcode2", "postcode2_accepts")]
	[InlineData("uk_postcode", "uk_postcode_accepts")]
	[InlineData("ABC", "abc_accepts")]
	[InlineData("_", "_accepts")]
	public void Emit_TurnsCaseBoundariesIntoUnderscores(string prefix, string accepts)
	{
		string source = Emit(@"\A\9", prefix);

		Assert.Contains($"static inline bool {accepts}(const char *text, size_t text_length)", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_AllowsABlankPrefix()
	{
		string source = Emit(@"\A\9");

		Assert.Contains("static inline bool accepts(const char *text, size_t text_length)", source, StringComparison.Ordinal);
		Assert.Contains("static const uint64_t max_encoded_value = 260ULL;", source, StringComparison.Ordinal);
	}

	/// <summary>C needs a function declared before its first use, and the public functions come first.</summary>
	[Fact]
	public void Emit_DeclaresTheSteppersAheadOfThePublicFunctions()
	{
		string source = Emit(@"\A\9");

		int prototype = source.IndexOf("static int accept_step(int state, int c);", StringComparison.Ordinal);
		int use = source.IndexOf("state = accept_step(state, (unsigned char)text[i]);", StringComparison.Ordinal);
		int definition = source.IndexOf("static int accept_step(int state, int c)\n", StringComparison.Ordinal);

		Assert.True(prototype >= 0 && prototype < use && use < definition);
	}

	[Fact]
	public void Emit_NamesTheHeadersItNeeds()
	{
		string source = Emit(@"\A\9");

		Assert.StartsWith("/* Needs <stdbool.h>, <stddef.h>, <stdint.h> and <string.h>. */", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_WritesNoDigitSeparators()
	{
		string source = Emit(@"\A{12}");

		Assert.Contains("= 95428956661682176ULL;", source, StringComparison.Ordinal);
		Assert.DoesNotContain("'", source.Substring(0, source.IndexOf("static int", StringComparison.Ordinal)));
	}

	[Fact]
	public void Emit_MapsTheValueTypeOntoStdint()
	{
		string source = Emit(@"\A\9", "Postcode", NaxpValueType.UInt16);

		Assert.Contains("static const uint16_t postcode_max_encoded_value = 260;", source, StringComparison.Ordinal);
		Assert.Contains("static inline uint16_t postcode_encode(const char *text, size_t text_length)", source, StringComparison.Ordinal);
		Assert.Contains("return postcode_is_accepting(state) ? (uint16_t)(total + 1ULL) : (uint16_t)0;", source, StringComparison.Ordinal);
		Assert.Contains("static inline bool postcode_decode(uint16_t value, char *destination, size_t capacity, size_t *length)", source, StringComparison.Ordinal);
		Assert.Contains("postcode_decode_core((uint64_t)value, buffer)", source, StringComparison.Ordinal);

		// The steppers keep the widest type whatever the choice.
		Assert.Contains("static int postcode_encode_step(int state, int c, uint64_t *total)", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_RefusesAValueTypeTheNaxpOutgrows()
	{
		Compilation compilation = Compile(@"\A\9");

		Assert.Throws<ArgumentException>(() => CEmitter.Instance.Emit(compilation, "", NaxpValueType.UInt8));
	}

	/// <summary>A literal run adds nothing to the total, and the compiler would say so.</summary>
	[Fact]
	public void Emit_VoidsTheParametersARangeLeavesUnused()
	{
		string source = Emit("ABC");

		Assert.Contains("static int encode_step(int state, int c, uint64_t *total)\n{\n\t(void)total;\n", source, StringComparison.Ordinal);
		Assert.Contains("static int decode_step(int state, uint64_t *remaining, char *destination, int *length)\n{\n\t(void)remaining;\n", source, StringComparison.Ordinal);
		Assert.DoesNotContain("(void)c;", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_CanonicalisesWhereTheNaxpReplaces()
	{
		string source = Emit(@"(B|b)!B");

		Assert.Contains("canonical_step(state, (unsigned char)text[i], canonical, &length);", source, StringComparison.Ordinal);
		Assert.Contains("return finish_canonical(state, canonical, length);", source, StringComparison.Ordinal);
		Assert.Contains("int length = canonicalise(text, text_length, canonical);", source, StringComparison.Ordinal);
		Assert.Contains("rank(canonical, length)", source, StringComparison.Ordinal);
		Assert.DoesNotContain(Tx.CopyMarker.ToString(), source, StringComparison.Ordinal);
	}

	/// <summary>A zero-length array is illegal, so the empty naxp gets a byte nothing writes.</summary>
	[Fact]
	public void Emit_GivesTheEmptyNaxpAOneByteBuffer()
	{
		string source = Emit("()");

		Assert.Contains("enum { max_length = 0 };", source, StringComparison.Ordinal);
		Assert.Contains("char buffer[1] = { 0 };", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_SplitsAMachineAboveTheChunkSizeAndDeclaresTheChunks()
	{
		string source = Emit("A{99}B{99}C{99}");

		Assert.Contains("static int accept_step0(int state, int c);", source, StringComparison.Ordinal);
		Assert.Contains("static int accept_step1(int state, int c);", source, StringComparison.Ordinal);
		Assert.Contains("if (state < 250) { return accept_step0(state, c); }", source, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("1Bad")]
	[InlineData("Bad.Name")]
	[InlineData("Bad Name")]
	public void Emit_ThrowsOnABadPrefix(string prefix)
	{
		Compilation compilation = Compile("A");

		Assert.Throws<ArgumentException>(() => CEmitter.Instance.Emit(compilation, prefix));
	}
	#endregion
	#region What it does when it runs
	/// <summary>
	/// Every naxp of the conformance data emitted into one file, compiled by gcc as C99 with every
	/// warning fatal, and the whole of the test data checked by the result.
	/// </summary>
	[Fact]
	public void Generated_MatchesTheConformanceDataUnderGcc()
		=> NativeHarness.CompileAndRun("gcc", "-std=c99", "conformance.c", BuildHarness());

	/// <summary>The fragments for every conformance naxp, a table over them, and the checks.</summary>
	static string BuildHarness()
	{
		var builder = new StringBuilder();

		builder.Append("/* Generated by CEmitterTests. One fragment per conformance case. */\n\n");
		builder.Append("#include <stdbool.h>\n#include <stddef.h>\n#include <stdint.h>\n#include <string.h>\n#include <stdio.h>\n\n");

		for (int i = 0; i < TestData.Cases.Count; ++i)
		{
			ConformanceCase item = TestData.Cases[i];

			builder.Append($"/* {i.ToString(CultureInfo.InvariantCulture)}: {Emitter.CommentText(item.Naxp)} */\n");
			CEmitter.Instance.Emit(Compile(item.Naxp), Prefix(i), builder);
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
			builder.Append("\t{ NULL, 0, 0ULL, NULL, 0 },\n};\n\n");
		}

		builder.Append("static const naxp_case cases[] =\n{\n");

		for (int i = 0; i < TestData.Cases.Count; ++i)
		{
			ConformanceCase item = TestData.Cases[i];
			string snake = Snake(i);

			builder.Append($"\t{{ {NativeHarness.CString(item.Naxp)}, {NativeHarness.Length(item.Naxp)}, {item.MaxEncodedValue.ToString(CultureInfo.InvariantCulture)}ULL, {snake}_max_encoded_value, {snake}_pattern, {snake}_accepts, {snake}_accepts_cstr, {snake}_encode, {snake}_encode_cstr, {snake}_decode, {snake}_decode_cstr, {snake}_canonical_form, {snake}_canonical_form_cstr, {snake}_values }},\n");
		}

		builder.Append("};\n\n");
		builder.Append(Driver);

		return builder.ToString();
	}

	static string Prefix(int index) => "Case" + index.ToString(CultureInfo.InvariantCulture);

	/// <summary>The prefix as the fragment spells it.</summary>
	static string Snake(int index) => "case" + index.ToString(CultureInfo.InvariantCulture);

	const string Types = """
		typedef struct
		{
			const char *in;
			size_t in_length;
			uint64_t out;
			const char *canon;
			size_t canon_length;
		} value_row;

		typedef struct
		{
			const char *naxp;
			size_t naxp_length;
			uint64_t max_encoded_value;
			uint64_t fragment_max_encoded_value;
			const char *(*pattern)(void);
			bool (*accepts)(const char *, size_t);
			bool (*accepts_cstr)(const char *);
			uint64_t (*encode)(const char *, size_t);
			uint64_t (*encode_cstr)(const char *);
			bool (*decode)(uint64_t, char *, size_t, size_t *);
			bool (*decode_cstr)(uint64_t, char *, size_t);
			bool (*canonical_form)(const char *, size_t, char *, size_t, size_t *);
			bool (*canonical_form_cstr)(const char *, char *, size_t);
			const value_row *values;
		} naxp_case;


		""";

	/// <summary>The checks, which are the same questions ConformanceTests asks the library.</summary>
	const string Driver = """
		static int checks = 0;
		static int failures = 0;

		static void check(bool condition, const char *naxp, const char *what, const char *text)
		{
			++checks;

			if (condition) { return; }

			if (++failures <= 40) { printf("  %s: %s '%s'\n", naxp, what, text); }
		}

		int main(void)
		{
			char out[2048];
			size_t case_count = sizeof cases / sizeof cases[0];

			for (size_t i = 0; i < case_count; ++i)
			{
				const naxp_case *c = &cases[i];

				const char *pattern = c->pattern();

				check(c->fragment_max_encoded_value == c->max_encoded_value, c->naxp, "max_encoded_value", "");
				check(strlen(pattern) == c->naxp_length && memcmp(pattern, c->naxp, c->naxp_length) == 0, c->naxp, "pattern", "");

				for (const value_row *row = c->values; row->in != NULL; ++row)
				{
					bool valid = row->out != 0ULL;
					bool plain = strlen(row->in) == row->in_length;
					bool plain_canon = strlen(row->canon) == row->canon_length;
					uint64_t encoded = c->encode(row->in, row->in_length);
					size_t length;

					check(encoded == row->out, c->naxp, "encode", row->in);
					check(c->accepts(row->in, row->in_length) == valid, c->naxp, "accepts", row->in);

					if (plain)
					{
						check(c->encode_cstr(row->in) == row->out, c->naxp, "encode_cstr", row->in);
						check(c->accepts_cstr(row->in) == valid, c->naxp, "accepts_cstr", row->in);
					}

					check(c->canonical_form(row->in, row->in_length, out, sizeof out, &length) == valid
						&& (valid ? length == row->canon_length && memcmp(out, row->canon, length) == 0 : length == 0),
						c->naxp, "canonical_form", row->in);

					if (plain)
					{
						check(c->canonical_form_cstr(row->in, out, sizeof out) == valid
							&& (!valid || (strlen(out) == row->canon_length && memcmp(out, row->canon, row->canon_length) == 0)),
							c->naxp, "canonical_form_cstr", row->in);
					}

					if (!valid) { continue; }

					/* Too short by one, then exactly long enough. */
					if (row->canon_length > 0)
					{
						check(!c->canonical_form(row->in, row->in_length, out, row->canon_length - 1, &length) && length == 0, c->naxp, "canonical_form short", row->in);
					}

					check(c->canonical_form(row->in, row->in_length, out, row->canon_length, NULL), c->naxp, "canonical_form exact", row->in);

					check(c->decode(encoded, out, sizeof out, &length) && length == row->canon_length && memcmp(out, row->canon, length) == 0,
						c->naxp, "decode", row->in);
					check(c->encode(row->canon, row->canon_length) == row->out, c->naxp, "round trip", row->in);

					if (plain_canon)
					{
						check(c->decode_cstr(encoded, out, sizeof out) && strlen(out) == row->canon_length && memcmp(out, row->canon, row->canon_length) == 0,
							c->naxp, "decode_cstr", row->in);
					}

					/* Too short by one, then exactly long enough. */
					if (row->canon_length > 0)
					{
						check(!c->decode(encoded, out, row->canon_length - 1, &length) && length == 0, c->naxp, "decode short", row->in);
					}

					check(c->decode(encoded, out, row->canon_length, NULL), c->naxp, "decode exact", row->in);
					check(!c->decode_cstr(encoded, out, row->canon_length), c->naxp, "decode_cstr short", row->in);
					check(c->decode_cstr(encoded, out, row->canon_length + 1), c->naxp, "decode_cstr exact", row->in);
				}

				check(!c->decode(0ULL, out, sizeof out, NULL), c->naxp, "decode zero", "");
				check(!c->decode_cstr(0ULL, out, sizeof out), c->naxp, "decode_cstr zero", "");

				if (c->max_encoded_value != UINT64_MAX)
				{
					check(!c->decode(c->max_encoded_value + 1ULL, out, sizeof out, NULL), c->naxp, "decode past the end", "");
				}
			}

			if (failures != 0)
			{
				printf("%d of %d checks failed.\n", failures, checks);

				return 1;
			}

			printf("%d checks passed over %d naxps.\n", checks, (int)case_count);

			return 0;
		}
		""";
	#endregion
}
