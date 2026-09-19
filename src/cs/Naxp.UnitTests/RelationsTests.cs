// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// How the language of one naxp stands to another's.
/// </summary>
public class RelationsTests
{
	static SetRelationship Accepted(string a, string b)
		=> Relations.CompareLanguages(Compiled(a).Accepted, Compiled(b).Accepted);

	static SetRelationship Canonical(string a, string b)
		=> Relations.CompareLanguages(Compiled(a).Canonical, Compiled(b).Canonical);

	static Compilation Compiled(string pattern)
	{
		Assert.True(Compiler.TryCompile(pattern, out Compilation? compilation, out NaxpError? error), $"{pattern}: {error?.Message}");

		return compilation!;
	}

	#region The four relationships
	[Theory]
	[InlineData("AB", "AB")]
	[InlineData("[AB]", "A|B")]
	[InlineData("A(B|C)", "AB|AC")]
	[InlineData("\\9{2}", "#[00-99]")]
	public void SameLanguage_IsEqual(string a, string b)
		=> Assert.Equal(SetRelationship.Equal, Accepted(a, b));

	[Theory]
	[InlineData("[AB]", "[ABC]")]
	[InlineData("A", "A?")]
	[InlineData("AB", "AB|CD")]
	[InlineData("\\A\\9", "\\A\\X")]
	public void FewerStrings_IsSubsetOf(string a, string b)
		=> Assert.Equal(SetRelationship.SubsetOf, Accepted(a, b));

	[Theory]
	[InlineData("[ABC]", "[AB]")]
	[InlineData("A?", "A")]
	[InlineData("AB|CD", "AB")]
	public void MoreStrings_IsSupersetOf(string a, string b)
		=> Assert.Equal(SetRelationship.SupersetOf, Accepted(a, b));

	[Theory]
	[InlineData("[AB]", "[BC]")]
	[InlineData("AB", "CD")]
	[InlineData("A|BB", "A|CC")]
	public void NeitherContainsTheOther_IsIncomparable(string a, string b)
		=> Assert.Equal(SetRelationship.Incomparable, Accepted(a, b));
	#endregion
	#region The relationship is directional
	[Fact]
	public void SwappingTheArguments_SwapsSubsetAndSuperset()
	{
		Assert.Equal(SetRelationship.SubsetOf, Accepted("[AB]", "[ABC]"));
		Assert.Equal(SetRelationship.SupersetOf, Accepted("[ABC]", "[AB]"));
	}
	#endregion
	#region Strings of differing length
	/// <summary>
	/// A string one machine takes and the other has no transition for, at a point where the
	/// other has already finished, is still a difference.
	/// </summary>
	[Fact]
	public void OneLanguageRunsOnPastTheOther()
	{
		Assert.Equal(SetRelationship.SubsetOf, Accepted("AB", "AB|ABC"));
		Assert.Equal(SetRelationship.SupersetOf, Accepted("AB|ABC", "AB"));
		Assert.Equal(SetRelationship.Incomparable, Accepted("AB|ABC", "AB|ABD"));
	}
	#endregion
	#region The accepted language and the canonical language differ
	/// <summary>
	/// A '!' widens what is accepted and leaves the canonical language alone, which is the
	/// property the whole comparison rests on. The two axes must therefore be able to disagree.
	/// </summary>
	[Fact]
	public void UnifiedWidensTheAcceptedLanguageOnly()
	{
		Assert.Equal(SetRelationship.SubsetOf, Accepted("A\\sB", "A\\s!!B"));
		Assert.Equal(SetRelationship.Equal, Canonical("A\\sB", "A\\s!!B"));
	}

	/// <summary>
	/// A case fold does the same, with the upper case form canonical either way.
	/// </summary>
	[Fact]
	public void FoldWidensTheAcceptedLanguageOnly()
	{
		Assert.Equal(SetRelationship.SubsetOf, Accepted("\\A\\A", "\\C(\\A\\A)"));
		Assert.Equal(SetRelationship.Equal, Canonical("\\A\\A", "\\C(\\A\\A)"));
	}
	#endregion
	#region The postcode cases the site quotes
	/// <summary>
	/// Adding <c>GIR 0AA</c> widens both languages by exactly one string.
	/// </summary>
	[Fact]
	public void AddingGirWidensBothLanguages()
	{
		const string Without = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string With = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA";

		Assert.Equal(SetRelationship.SubsetOf, Accepted(Without, With));
		Assert.Equal(SetRelationship.SubsetOf, Canonical(Without, With));
		Assert.Equal(1UL, Compiled(With).MaxEncodedValue - Compiled(Without).MaxEncodedValue);
	}

	/// <summary>
	/// Restricting the letters to those the Royal Mail guide lists refuses text the looser naxp
	/// accepts, which is the first column saying so.
	/// </summary>
	[Fact]
	public void TighteningTheLettersNarrowsBothLanguages()
	{
		const string Loose = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string Tight = "[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]";

		Assert.Equal(SetRelationship.SupersetOf, Accepted(Loose, Tight));
		Assert.Equal(SetRelationship.SupersetOf, Canonical(Loose, Tight));
	}
	#endregion
}
