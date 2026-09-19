// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// How the encoding of one naxp stands to another's: the graph of encode, the set of
/// (text, value) pairs, as a set relationship.
/// </summary>
public class EncodingRelationTests
{
	static SetRelationship Encoding(string a, string b)
	{
		Assert.True(Relations.TryCompareEncodings(Compiled(a), Compiled(b), out SetRelationship relationship));

		return relationship;
	}

	static SetRelationship Accepted(string a, string b)
		=> Relations.CompareLanguages(Compiled(a).Accepted, Compiled(b).Accepted);

	static SetRelationship Canonical(string a, string b)
		=> Relations.CompareLanguages(Compiled(a).Canonical, Compiled(b).Canonical);

	static Compilation Compiled(string pattern)
	{
		Assert.True(Compiler.TryCompile(pattern, out Compilation? compilation, out NaxpError? error), $"{pattern}: {error?.Message}");

		return compilation!;
	}

	#region Without unified elements it is the language relation, refined
	/// <summary>
	/// Where every shared string keeps its value, the encoding stands as the accepted language
	/// does, because the graph of a function is contained in another's exactly when the domain
	/// is and the two agree on it.
	/// </summary>
	[Theory]
	[InlineData("AB", "AB", SetRelationship.Equal)]
	[InlineData("[AB]", "A|B", SetRelationship.Equal)]
	[InlineData("[AB]", "[ABC]", SetRelationship.SubsetOf)]
	[InlineData("[ABC]", "[AB]", SetRelationship.SupersetOf)]
	[InlineData("AB", "AB|AC", SetRelationship.SubsetOf)]
	public void ValuesKept_FollowsTheLanguage(string a, string b, SetRelationship expected)
		=> Assert.Equal(expected, Encoding(a, b));

	/// <summary>
	/// Where a shared string changes value the graphs are incomparable whatever the languages
	/// do, since each holds a pair the other lacks.
	/// </summary>
	[Theory]
	[InlineData("[AC]", "[ABC]")]
	[InlineData("[ABC]", "[AC]")]
	[InlineData("AC", "AB|AC")]
	public void ValuesMoved_IsIncomparable(string a, string b)
	{
		Assert.NotEqual(SetRelationship.Incomparable, Accepted(a, b));
		Assert.Equal(SetRelationship.Incomparable, Encoding(a, b));
	}

	/// <summary>
	/// Two naxps sharing no string at all, or each holding strings the other lacks, are
	/// incomparable before any value is looked at.
	/// </summary>
	[Theory]
	[InlineData("AB", "CD")]
	[InlineData("[AB]", "[BC]")]
	public void IncomparableLanguages_AreIncomparableEncodings(string a, string b)
		=> Assert.Equal(SetRelationship.Incomparable, Encoding(a, b));
	#endregion
	#region Canonical forms are not values
	/// <summary>
	/// The encoding is about values, and two naxps can print a string differently while giving
	/// it the same value. The first pair is the specification's own example under "Values", the
	/// last is a naxp folded the wrong way. On all of them the encoding is <c>Equal</c> and the
	/// printed text is <c>Incomparable</c>, and a comparison that confused the two axes would
	/// tell somebody their stored values were broken when they were not.
	/// </summary>
	[Theory]
	[InlineData("[\\s\\-]?!\\-", "[\\s\\-]!?")]
	[InlineData("(A|B)!A", "(A|B)!B")]
	[InlineData("(A|B)!A|C", "(A|B)!B|C")]
	[InlineData("\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)")]
	public void SameValuesDifferentText_IsEqual(string a, string b)
	{
		Assert.Equal(SetRelationship.Equal, Encoding(a, b));
		Assert.Equal(SetRelationship.Incomparable, Canonical(a, b));
	}

	/// <summary>
	/// And the reverse: the same canonical strings at the same ranks, with a unified element
	/// sending the other strings elsewhere.
	/// </summary>
	[Fact]
	public void SameCanonicalRanksDifferentValues_IsIncomparable()
	{
		Assert.Equal(SetRelationship.Equal, Accepted("[ab]{2}", "([ab]{2})!(aa)"));
		Assert.Equal(SetRelationship.Incomparable, Encoding("[ab]{2}", "([ab]{2})!(aa)"));
	}
	#endregion
	#region The edits the site quotes
	/// <summary>
	/// A safe edit accepts more and keeps every value: a space made optional, a case fold
	/// added, a unified element introduced.
	/// </summary>
	[Theory]
	[InlineData("A", "(A|a)!A")]
	[InlineData("A\\sB", "A\\s!!B")]
	[InlineData("\\A\\A?\\9\\X? \\s \\9\\A\\A", "\\A\\A?\\9\\X? \\s!! \\9\\A\\A")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "\\C(\\A\\A?\\9\\X? \\s!! \\9\\A\\A)")]
	public void SafeEdits_AreSubsetOf(string a, string b)
		=> Assert.Equal(SetRelationship.SubsetOf, Encoding(a, b));

	/// <summary>
	/// The two breaking rows: <c>GIR 0AA</c> added, and the letters tightened to the Royal Mail
	/// guide. Both are decided from the canonical machines alone, as the site's figures were.
	/// </summary>
	[Fact]
	public void BreakingEdits_AreIncomparable()
	{
		const string Postcode = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string WithGir = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA";
		const string Tight = "[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]";

		Assert.Equal(SetRelationship.Incomparable, Encoding(Postcode, WithGir));
		Assert.Equal(SetRelationship.Incomparable, Encoding(Postcode, Tight));
	}

	/// <summary>
	/// Dropping the space rather than reproducing it keeps the accepted language and changes
	/// the value of every string with a space in it, which only the walk over parses can see.
	/// </summary>
	[Fact]
	public void DroppingTheSpace_IsIncomparable()
	{
		const string Reproduced = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string Dropped = "\\A\\A?\\9\\X? \\s!? \\9\\A\\A";

		Assert.Equal(SetRelationship.Equal, Accepted(Reproduced, Dropped));
		Assert.Equal(Agreement.Agrees, RankAgreement.Compare(Compiled(Reproduced).Canonical, Compiled(Dropped).Canonical));
		Assert.Equal(SetRelationship.Incomparable, Encoding(Reproduced, Dropped));
	}
	#endregion
	#region The encoding is the strongest axis
	/// <summary>
	/// <c>Equal</c> or <c>SubsetOf</c> on the encoding forces the same on the accepted text,
	/// because the graph's domain is the accepted language. The converse does not hold, which
	/// the rows above show.
	/// </summary>
	[Theory]
	[InlineData("AB", "AB")]
	[InlineData("[AB]", "[ABC]")]
	[InlineData("[AC]", "[ABC]")]
	[InlineData("(A|B)!A", "(A|B)!B")]
	[InlineData("[ab]{2}", "([ab]{2})!(aa)")]
	[InlineData("A\\sB", "A\\s!!B")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "\\A\\A?\\9\\X? \\s!? \\9\\A\\A")]
	public void EncodingImpliesAcceptedText(string a, string b)
	{
		SetRelationship encoding = Encoding(a, b);

		if (encoding != SetRelationship.Incomparable) { Assert.Equal(encoding, Accepted(a, b)); }
	}
	#endregion
	#region The budget
	/// <summary>
	/// A comparison that outgrows its budget says so rather than guessing, and says nothing
	/// about the relationship.
	/// </summary>
	[Theory]
	[InlineData("[AB]", "[ABC]")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "\\A\\A?\\9\\X? \\s!? \\9\\A\\A")]
	public void ABudgetTooSmall_IsUndecided(string a, string b)
	{
		Assert.False(Relations.TryCompareEncodings(Compiled(a), Compiled(b), out SetRelationship relationship, budget: 1));
		Assert.Equal(SetRelationship.Incomparable, relationship);
	}

	/// <summary>
	/// Where the languages are incomparable no walk is needed, so no budget can be too small.
	/// </summary>
	[Fact]
	public void IncomparableLanguages_NeedNoBudget()
	{
		Assert.True(Relations.TryCompareEncodings(Compiled("AB"), Compiled("CD"), out SetRelationship relationship, budget: 1));
		Assert.Equal(SetRelationship.Incomparable, relationship);
	}
	#endregion
}
