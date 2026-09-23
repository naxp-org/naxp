// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The public comparison of two naxps: three set relationships and the first divergent value.
/// </summary>
public class NaxpComparisonTests
{
	const string Postcode = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
	const string PostcodeSpaceOptional = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
	const string PostcodeSpaceRequired = "\\A\\A?\\9\\X? \\s \\9\\A\\A";
	const string PostcodeFolded = "\\C(\\A\\A?\\9\\X? \\s!! \\9\\A\\A)";
	const string PostcodeWithGir = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA";
	const string PostcodeTight = "[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]";

	static NaxpComparison Compare(string a, string b) => Naxp.Compare(Naxp.Parse(a), Naxp.Parse(b));

	#region The three axes
	/// <summary>
	/// The four rows the site quotes, in the order it quotes them, with the figures the site
	/// gives for each.
	/// </summary>
	[Fact]
	public void TheSiteRows()
	{
		Assert.Equal(new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.SubsetOf, SetRelationship.Equal), Compare(PostcodeSpaceRequired, PostcodeSpaceOptional));
		Assert.Equal(new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.SubsetOf, SetRelationship.Equal), Compare(Postcode, PostcodeFolded));
		Assert.Equal(new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.Incomparable, SetRelationship.SubsetOf), Compare(Postcode, PostcodeWithGir));
		Assert.Equal(new NaxpComparison(SetRelationship.SupersetOf, SetRelationship.Incomparable, SetRelationship.SupersetOf), Compare(Postcode, PostcodeTight));
	}

	[Fact]
	public void ANaxpAgainstItself_IsEqualOnEveryAxis()
		=> Assert.Equal(new NaxpComparison(SetRelationship.Equal, SetRelationship.Equal, SetRelationship.Equal), Compare(Postcode, Postcode));

	/// <summary>
	/// The axes are independent. The same values with different printed text, and the same
	/// printed text with different values, are both possible, and each is a different kind
	/// of change to warn about. The last pair prints <c>a</c> and <c>c</c> either way and
	/// sends <c>b</c> to a different one of them.
	/// </summary>
	[Fact]
	public void TheAxesCanDisagree()
	{
		Assert.Equal(new NaxpComparison(SetRelationship.Equal, SetRelationship.Equal, SetRelationship.Incomparable), Compare("(A|B)!A", "(A|B)!B"));
		Assert.Equal(new NaxpComparison(SetRelationship.Equal, SetRelationship.Equal, SetRelationship.Incomparable), Compare("[\\s\\-]?!\\-", "[\\s\\-]!?"));
		Assert.Equal(new NaxpComparison(SetRelationship.Equal, SetRelationship.Incomparable, SetRelationship.SupersetOf), Compare("[ab]{2}", "([ab]{2})!(aa)"));
		Assert.Equal(new NaxpComparison(SetRelationship.Equal, SetRelationship.Incomparable, SetRelationship.Equal), Compare("(a|b)!a|c", "a|(b|c)!c"));
	}

	/// <summary>
	/// The relationships are from <c>a</c>'s point of view, so swapping the arguments swaps
	/// subset and superset on every axis and leaves the other two values alone.
	/// </summary>
	[Fact]
	public void SwappingTheArguments_SwapsSubsetAndSuperset()
	{
		NaxpComparison forward = Compare(Postcode, PostcodeWithGir);
		NaxpComparison backward = Compare(PostcodeWithGir, Postcode);

		Assert.Equal(SetRelationship.SubsetOf, forward.AcceptedText);
		Assert.Equal(SetRelationship.SupersetOf, backward.AcceptedText);
		Assert.Equal(SetRelationship.Incomparable, forward.Encoding);
		Assert.Equal(SetRelationship.Incomparable, backward.Encoding);
		Assert.Equal(SetRelationship.SubsetOf, forward.PrintedText);
		Assert.Equal(SetRelationship.SupersetOf, backward.PrintedText);
	}
	#endregion
	#region The struct
	[Fact]
	public void TheDefaultComparison_ClaimsNothing()
	{
		NaxpComparison comparison = default;

		Assert.Equal(SetRelationship.Incomparable, comparison.AcceptedText);
		Assert.Equal(SetRelationship.Incomparable, comparison.Encoding);
		Assert.Equal(SetRelationship.Incomparable, comparison.PrintedText);
	}

	[Fact]
	public void ComparisonsWithTheSameAxes_AreEqual()
	{
		var left = new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.SubsetOf, SetRelationship.Equal);
		var right = new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.SubsetOf, SetRelationship.Equal);
		var other = new NaxpComparison(SetRelationship.SubsetOf, SetRelationship.Incomparable, SetRelationship.Equal);

		Assert.Equal(left, right);
		Assert.True(left == right);
		Assert.NotEqual(left, other);
		Assert.True(left != other);
		Assert.Equal(left.GetHashCode(), right.GetHashCode());
	}
	#endregion
	#region Deciding, and not
	[Fact]
	public void TryCompare_DecidesWhatCompareDecides()
	{
		Assert.True(Naxp.TryCompare(Naxp.Parse(Postcode), Naxp.Parse(PostcodeWithGir), out NaxpComparison comparison));
		Assert.Equal(Compare(Postcode, PostcodeWithGir), comparison);
	}

	/// <summary>
	/// Only the encoding axis can go undecided, when its walk outgrows the budget, and no
	/// naxp anybody has reason to write gets near it. The undecided path is reached here by
	/// lowering the budget, which is what the overloads that do not take one stand on.
	/// </summary>
	[Fact]
	public void TheUndecidedChannel_IsFalseAndThrows()
	{
		Assert.True(Compiler.TryCompile(Postcode, out Compilation? a, out _));
		Assert.True(Compiler.TryCompile(PostcodeWithGir, out Compilation? b, out _));

		Assert.False(Relations.TryCompareEncodings(a!, b!, out _, budget: 1));

		InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => Naxp.Compare(Naxp.Parse(Postcode), Naxp.Parse(PostcodeWithGir), budget: 1));
		Assert.Contains("budget", thrown.Message, StringComparison.Ordinal);

		Assert.False(Naxp.TryCompare(Naxp.Parse(Postcode), Naxp.Parse(PostcodeWithGir), out NaxpComparison comparison, budget: 1));
		Assert.Equal(default, comparison);
	}

	/// <summary>
	/// The budget the overloads that do not take one use, which a caller scaling from it reads
	/// here rather than writing the number itself.
	/// </summary>
	[Fact]
	public void DefaultBudget_IsWhatTheOverloadsWithoutOneUse()
	{
		Assert.Equal(ValueAgreement.MaxTuples, Naxp.DefaultBudget);

		Assert.Equal(
			Naxp.Compare(Naxp.Parse(Postcode), Naxp.Parse(PostcodeWithGir)),
			Naxp.Compare(Naxp.Parse(Postcode), Naxp.Parse(PostcodeWithGir), Naxp.DefaultBudget));
	}

	[Fact]
	public void NullArguments_Throw()
	{
		Naxp naxp = Naxp.Parse("A");

		Assert.Throws<ArgumentNullException>(() => Naxp.Compare(null!, naxp));
		Assert.Throws<ArgumentNullException>(() => Naxp.Compare(naxp, null!));
		Assert.Throws<ArgumentNullException>(() => Naxp.TryCompare(null!, naxp, out _));
		Assert.Throws<ArgumentNullException>(() => Naxp.FirstDivergentValue(naxp, null!));
	}
	#endregion
	#region The first divergent value
	[Fact]
	public void AddingGir_DivergesWhereTheSiteSays()
	{
		Naxp without = Naxp.Parse(Postcode);
		Naxp with = Naxp.Parse(PostcodeWithGir);

		ulong value = Naxp.FirstDivergentValue(without, with);

		Assert.Equal(405_194_401UL, value);
		Assert.Equal("G0 0AA", without.Decode(value));
		Assert.Equal("H0 0AA", with.Decode(value));
	}

	/// <summary>
	/// Zero says every value both hold decodes alike. It says nothing about values only one
	/// holds, which the count of values covers.
	/// </summary>
	[Theory]
	[InlineData("[AB]", "[ABC]")]
	[InlineData(PostcodeSpaceRequired, PostcodeSpaceOptional)]
	[InlineData(Postcode, PostcodeFolded)]
	public void SafeEdits_HaveNoDivergentValue(string a, string b)
		=> Assert.Equal(0UL, Naxp.FirstDivergentValue(Naxp.Parse(a), Naxp.Parse(b)));

	/// <summary>
	/// Equal encodings and a divergent value are not a contradiction: the two questions are
	/// about text going in and text coming out.
	/// </summary>
	[Fact]
	public void EqualEncodings_CanStillDivergeOnDecoding()
	{
		Naxp a = Naxp.Parse("(A|B)!A");
		Naxp b = Naxp.Parse("(A|B)!B");

		Assert.Equal(SetRelationship.Equal, Naxp.Compare(a, b).Encoding);
		Assert.Equal(1UL, Naxp.FirstDivergentValue(a, b));
		Assert.Equal("A", a.Decode(1UL));
		Assert.Equal("B", b.Decode(1UL));
	}
	#endregion
}
