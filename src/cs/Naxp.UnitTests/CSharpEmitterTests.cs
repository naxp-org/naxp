// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.IO;
using System.Text;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The shape of what <see cref="CSharpEmitter"/> writes. Whether the generated code is *correct*
/// is decided by <c>GeneratedCodeTests</c>, which compiles it and runs the conformance data
/// through it; these tests only pin the text.
/// </summary>
public class CSharpEmitterTests
{
	static Compilation Compile(string naxp)
	{
		Assert.True(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error), error?.ToString());

		return compilation!;
	}

	static string Emit(string naxp, string prefix = "")
		=> CSharpEmitter.Instance.Emit(Compile(naxp), prefix);

	[Fact]
	public void Emit_CarriesTheConstants()
	{
		string source = Emit("#[1-12]");

		Assert.Contains("public const ulong ValueCount = 12UL;", source, StringComparison.Ordinal);
		Assert.Contains("public const int MaxLength = 2;", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_IsDeterministic()
	{
		Assert.Equal(Emit("K9\\9 9K9"), Emit("K9\\9 9K9"));
	}

	[Fact]
	public void Emit_IsAFragmentWithoutParaphernalia()
	{
		string source = Emit("A|B");

		Assert.DoesNotContain("namespace", source, StringComparison.Ordinal);
		Assert.DoesNotContain("class", source, StringComparison.Ordinal);
		Assert.DoesNotContain("using", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_PrefixesEveryNameWithTheStem()
	{
		string source = Emit("A|B", "Postcode");

		Assert.Contains("public const ulong PostcodeValueCount = 2UL;", source, StringComparison.Ordinal);
		Assert.Contains("public static bool PostcodeAccepts(global::System.ReadOnlySpan<char> text)", source, StringComparison.Ordinal);
		Assert.Contains("public static string PostcodeDecode(ulong value)", source, StringComparison.Ordinal);
		Assert.Contains("static int PostcodeAcceptStep(int state, char c)", source, StringComparison.Ordinal);

		// No name escapes the prefix: the bare names never appear followed by their own syntax.
		Assert.DoesNotContain(" ValueCount", source, StringComparison.Ordinal);
		Assert.DoesNotContain(" Accepts(", source, StringComparison.Ordinal);
		Assert.DoesNotContain("\"ValueCount\"", source, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("1Bad")]
	[InlineData("Bad.Name")]
	[InlineData("Bad Name")]
	public void Emit_RefusesABadPrefix(string prefix)
	{
		Compilation compilation = Compile("A");

		Assert.Throws<ArgumentException>(() => CSharpEmitter.Instance.Emit(compilation, prefix));
	}

	/// <summary>
	/// A name of Unicode letters passes <see cref="char.IsLetter(char)"/> and must still be
	/// refused, because the identifier rule is ASCII across every target language.
	/// </summary>
	[Fact]
	public void Emit_RefusesAUnicodePrefix()
	{
		Compilation compilation = Compile("A");
		string aUmlaut = "N" + (char)0xE4 + "xp";

		Assert.Throws<ArgumentException>(() => CSharpEmitter.Instance.Emit(compilation, aUmlaut));
	}

	[Fact]
	public void Emit_AllowsABlankPrefix()
	{
		string source = Emit("A", "");

		Assert.Contains("public static bool Accepts(global::System.ReadOnlySpan<char> text)", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_TargetsAgree()
	{
		Compilation compilation = Compile("A|B");
		string direct = CSharpEmitter.Instance.Emit(compilation, "X");

		var builder = new StringBuilder();
		CSharpEmitter.Instance.Emit(compilation, "X", builder);
		Assert.Equal(direct, builder.ToString());

		using var writer = new StringWriter();
		CSharpEmitter.Instance.Emit(compilation, "X", writer);
		Assert.Equal(direct, writer.ToString());
	}

	[Fact]
	public void Emit_AppliesTheInitialIndent()
	{
		string source = CSharpEmitter.Instance.Emit(Compile("A|B"), "", initialIndent: "\t\t");

		foreach (string line in source.Split('\n'))
		{
			string text = line.TrimEnd('\r');

			if (text.Length == 0) { continue; }

			Assert.StartsWith("\t\t", text, StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// A narrower value type changes only the public boundary: the consts, the signatures and
	/// the casts at the returns. The steppers keep their ulong arithmetic.
	/// </summary>
	[Fact]
	public void Emit_HonoursTheValueType()
	{
		string source = CSharpEmitter.Instance.Emit(Compile("#[1-12]"), "", NaxpValueType.UInt8);

		Assert.Contains("public const byte ValueCount = 12;", source, StringComparison.Ordinal);
		Assert.Contains("public static byte Encode(global::System.ReadOnlySpan<char> text)", source, StringComparison.Ordinal);
		Assert.Contains("public static string Decode(byte value)", source, StringComparison.Ordinal);
		Assert.Contains("public static bool TryDecode(byte value, global::System.Span<char> destination, out int charsWritten)", source, StringComparison.Ordinal);
		Assert.Contains("static int EncodeStep(int state, char c, ref ulong total)", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_RefusesAValueTypeTheCountDoesNotFit()
	{
		// 128 values: one more than sbyte holds, exactly what byte holds and more.
		Compilation compilation = Compile("#[1-128]");

		Assert.Throws<ArgumentException>(() => CSharpEmitter.Instance.Emit(compilation, "", NaxpValueType.Int8));
		Assert.Contains("public const byte ValueCount = 128;", CSharpEmitter.Instance.Emit(compilation, "", NaxpValueType.UInt8), StringComparison.Ordinal);
	}

	/// <summary>
	/// A machine over <see cref="Emitter.ChunkSize"/> states is split into a dispatcher and
	/// chunk methods. Only long literal runs get here, so the naxp is three of them.
	/// </summary>
	[Fact]
	public void Emit_ChunksALargeMachine()
	{
		string source = Emit("A{99}B{99}C{99}");

		Assert.Contains("if (state < 250) { return AcceptStep0(state, c); }", source, StringComparison.Ordinal);
		Assert.Contains("static int AcceptStep1(int state, char c)", source, StringComparison.Ordinal);
		Assert.Contains("public const int MaxLength = 297;", source, StringComparison.Ordinal);

		// The canonicalising machine chunks by the same rule.
		string replaceable = Emit("(B|b)!BA{99}C{99}D{99}");

		Assert.Contains("static int CanonicalStep1(", replaceable, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_OmitsTheCanonicaliserWhereNothingIsReplaceable()
	{
		string source = Emit("AB|C");

		Assert.DoesNotContain("CanonicalStep", source, StringComparison.Ordinal);
		Assert.DoesNotContain("FinishCanonical", source, StringComparison.Ordinal);
		Assert.DoesNotContain("Rank", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_CanonicalisesWhereTheNaxpReplaces()
	{
		string source = Emit("(B|b)!B");

		Assert.Contains("static int CanonicalStep(", source, StringComparison.Ordinal);
		Assert.Contains("static int FinishCanonical(", source, StringComparison.Ordinal);
		Assert.Contains("static ulong Rank(", source, StringComparison.Ordinal);

		// The copy marker is internal to the transducer and must never survive into source.
		Assert.DoesNotContain(Tx.CopyMarker.ToString(), source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_EveryConformanceCaseEmits()
	{
		foreach (ConformanceCase item in ConformanceTestData.Load().Cases)
		{
			Assert.True(Compiler.TryCompile(item.Naxp, out Compilation? compilation, out NaxpError? error), $"{item.Naxp}: {error}");

			string source = CSharpEmitter.Instance.Emit(compilation!, "");

			Assert.Contains("public static ulong Encode(global::System.ReadOnlySpan<char> text)", source, StringComparison.Ordinal);
			Assert.Contains("public static ulong Encode(global::System.ReadOnlySpan<byte> text)", source, StringComparison.Ordinal);
			Assert.Contains("public static string Decode(ulong value)", source, StringComparison.Ordinal);
			Assert.DoesNotContain(Tx.CopyMarker.ToString(), source, StringComparison.Ordinal);
		}
	}
}
