// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The encoding, its inverse, and the canonical form the two hinge on.
/// </summary>
public class CodecTests
{
	#region Specification's worked values
	/// <summary>
	/// The case the Ordering section of version 0.4 warns about: <c>1</c> sorts
	/// above <c>9</c> because it is the only leading digit that can carry a second.
	/// </summary>
	[Theory]
	[InlineData("0", 1UL)]
	[InlineData("9", 9UL)]
	[InlineData("1", 10UL)]
	[InlineData("10", 11UL)]
	public void UnpaddedDigitsRange_PutsTheWiderMatchesLast(string text, ulong expected)
		=> Assert.Equal(expected, Encode("#[0-10]", text));

	/// <summary>
	/// Padding the lower bound gives every match the same width, so numeric order survives.
	/// </summary>
	[Theory]
	[InlineData("00", 1UL)]
	[InlineData("09", 10UL)]
	[InlineData("10", 11UL)]
	public void PaddedDigitsRange_KeepsNumericOrder(string text, ulong expected)
		=> Assert.Equal(expected, Encode("#[00-10]", text));

	/// <summary>
	/// In <c>AB|B</c> the first classes are <c>[A]</c> and <c>[B]</c>, so <c>AB</c> precedes
	/// <c>B</c> although it is longer. The order is neither lexicographic nor shortlex.
	/// </summary>
	[Fact]
	public void FirstClassOrder_IsNeitherLexicographicNorShortlex()
	{
		Assert.Equal(1UL, Encode("AB|B", "AB"));
		Assert.Equal(2UL, Encode("AB|B", "B"));
	}

	[Fact]
	public void NotAccepted_EncodesToZero()
	{
		Assert.Equal(0UL, Encode("#[0-10]", "11"));
		Assert.Equal(0UL, Encode("#[0-10]", string.Empty));
		Assert.Equal(0UL, Encode("A", "B"));
	}
	#endregion
	#region Worked example
	/// <summary>
	/// UK postcodes. Both spellings of a postcode encode alike, and the value decodes to the
	/// spelling with the space.
	/// </summary>
	[Theory]
	[InlineData("M1 1AA", "M11AA", 810639597UL)]
	[InlineData("CR2 6XH", "CR26XH", 180591302UL)]
	[InlineData("DN55 1PT", "DN551PT", 238906246UL)]
	[InlineData("W1A 1AA", "W1A1AA", 1486037957UL)]
	[InlineData("EC1A 1BB", "EC1A1BB", 277958384UL)]
	public void Postcodes_EncodeAlikeWithAndWithoutTheSpace(string spaced, string tight, ulong expected)
	{
		Compilation postcode = Compile(Postcode);

		Assert.Equal(expected, Encode(postcode, spaced));
		Assert.Equal(expected, Encode(postcode, tight));

		Assert.True(postcode.TryDecode(expected, out string? decoded));
		Assert.Equal(spaced, decoded);
	}

	[Fact]
	public void Postcodes_RunFromOneToTheCount()
	{
		Compilation postcode = Compile(Postcode);

		Assert.Equal(1755842400UL, postcode.ValueCount);
		Assert.Equal(1UL, Encode(postcode, "A0 0AA"));
		Assert.Equal(1755842400UL, Encode(postcode, "ZZ9Z 9ZZ"));

		Assert.False(postcode.TryDecode(0UL, out _));
		Assert.False(postcode.TryDecode(1755842401UL, out _));
	}

	const string Postcode = "\\A \\A? \\9 \\X? \\s!! \\9 \\A \\A";
	#endregion
	#region Canonical form
	[Theory]
	[InlineData("(A|a)!A", "a", "A")]
	[InlineData("(A|a)!A", "A", "A")]
	[InlineData("\\s!!X", "X", " X")]
	[InlineData("\\s!!X", " X", " X")]
	[InlineData("[\\s\\-]?!\\-", "", "-")]
	[InlineData("[\\s\\-]?!\\-", " ", "-")]
	[InlineData("[\\s\\-]!?", " ", "")]
	[InlineData("\\A!?", "Q", "")]
	[InlineData("A", "A", "A")]
	public void CanonicalForm(string naxp, string text, string expected)
	{
		Compilation compilation = Compile(naxp);

		Assert.True(compilation.TryGetCanonicalForm(text, out string? canonical));
		Assert.Equal(expected, canonical);
	}

	/// <summary>
	/// Every string a replaceable element accepts takes the same value, since those strings
	/// share a canonical form.
	/// </summary>
	[Fact]
	public void ReplaceableElement_GivesEveryMatchTheSameValue()
	{
		Compilation compilation = Compile("(A|a)!A");

		Assert.Equal(1UL, compilation.ValueCount);
		Assert.Equal(2UL, compilation.AcceptedCount);
		Assert.Equal(1UL, Encode(compilation, "A"));
		Assert.Equal(1UL, Encode(compilation, "a"));
	}

	/// <summary>
	/// A rendering determines the canonical language, so changing it changes which value a
	/// string takes.
	/// </summary>
	[Fact]
	public void Rendering_DecidesTheValues()
	{
		Assert.Equal(1UL, Encode("(A|b)!bX|BY", "BY"));
		Assert.Equal(2UL, Encode("(A|b)!AX|BY", "BY"));
	}

	[Fact]
	public void NotAccepted_HasNoCanonicalForm()
	{
		Compilation compilation = Compile("(A|a)!A");

		Assert.False(compilation.TryGetCanonicalForm("B", out string? canonical));
		Assert.Null(canonical);
	}
	#endregion
	#region W3
	/// <summary>
	/// A naxp whose replacement is not single valued is refused when it is compiled, so encoding
	/// never meets the case. Read the <c>B</c> of <c>AB!!B?C</c> as the replaceable element with
	/// the optional one absent and <c>ABC</c> canonicalises to itself; read it the other way round
	/// and it canonicalises to <c>ABBC</c>.
	/// </summary>
	[Fact]
	public void AmbiguousReplacement_IsRefusedAtCompileTime()
	{
		Assert.False(Compiler.TryCompile("AB!!B?C", out Compilation? compilation, out NaxpError? error));

		Assert.Null(compilation);
		Assert.Equal("W3", NaxpMessageRules.RuleOf(error!.Value.Message));
		Assert.Contains("more than one canonical form", error.Value.Text, StringComparison.Ordinal);
	}

	/// <summary>
	/// The postcode naxp satisfies W3 because a space appears in neither <c>\X</c> nor <c>\9</c>,
	/// so a string that omits the space can never be confused with one that includes it.
	/// </summary>
	[Fact]
	public void DisjointCharacterSets_LeaveNoAmbiguity()
	{
		Compilation postcode = Compile(Postcode);

		Assert.True(postcode.TryGetCanonicalForm("EC1A1BB", out string? canonical));
		Assert.Equal("EC1A 1BB", canonical);
	}

	/// <summary>
	/// Two branches can be alive at every position with quite different outputs, so long as only
	/// one of them survives to the end. Carrying one output per position rather than every output
	/// is what keeps this affordable: this naxp has 2^17 strings on each side.
	/// </summary>
	[Fact]
	public void CompetingBranches_CostOneOutputPerPosition()
	{
		const int Width = 17;
		string source = $"[ab]{{{Width}}}c|([ab]!a){{{Width}}}d";

		// This naxp no longer compiles, though it breaks no rule of the language. Canonicalising
		// it as a machine wants 2^18 states, over NaxpLimits.MaxCanonicalStates, because nothing
		// before the last character says which alternative was taken; see
		// encoding/transducer-determinisation.md.
		Assert.False(Compiler.TryCompile(source, out _, out NaxpError? refusal));
		Assert.Equal("ImplementationLimit", NaxpMessageRules.RuleOf(refusal!.Value.Message));

		// The property this test exists for belongs to the tree walk, which is unaffected by that
		// budget, so it is exercised directly.
		Assert.True(Parser.TryParse(source.AsSpan(), out Ast? ast, out _));
		Assert.True(WellFormedness.TryCheck(ast!, out _));

		var letters = new string('b', Width - 1) + "a";

		Assert.True(Canonicaliser.TryCanonicalise(ast!, (letters + "c").AsSpan(), out string? copied));
		Assert.Equal(letters + "c", copied);

		// The whole of the second alternative replaces each letter, so every string it accepts
		// canonicalises to the same one.
		Assert.True(Canonicaliser.TryCanonicalise(ast!, (letters + "d").AsSpan(), out string? replaced));
		Assert.Equal(new string('a', Width) + "d", replaced);
	}
	#endregion
	#region Helpers
	static Compilation Compile(string naxp)
	{
		Assert.True(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error), $"{naxp} was refused: {error}");

		return compilation!;
	}

	static ulong Encode(string naxp, string text) => Compile(naxp).Encode(text);

	static ulong Encode(Compilation compilation, string text) => compilation.Encode(text);
	#endregion
}
