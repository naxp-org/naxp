// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The lowest value two naxps both hold and decode differently.
/// </summary>
public class FirstDivergentValueTests
{
	static ulong FirstDivergent(string a, string b)
		=> Relations.FirstDivergentValue(Compiled(a).Canonical, Compiled(b).Canonical);

	static Compilation Compiled(string pattern)
	{
		Assert.True(Compiler.TryCompile(pattern, out Compilation? compilation, out NaxpError? error), $"{pattern}: {error?.Message}");

		return compilation!;
	}

	/// <summary>
	/// Every value both hold, decoded one at a time through the public surface. The slow and
	/// obvious way, so that the walk has something independent to answer to.
	/// </summary>
	static ulong FirstDivergentByEnumeration(string a, string b)
	{
		Naxp left = Naxp.Parse(a);
		Naxp right = Naxp.Parse(b);
		ulong shared = Math.Min(left.MaxEncodedValue, right.MaxEncodedValue);

		Assert.True(shared <= 200_000UL, "Too many values to enumerate.");

		for (ulong value = 1UL; value <= shared; value++)
		{
			if (!string.Equals(left.Decode(value), right.Decode(value), StringComparison.Ordinal)) { return value; }
		}

		return 0UL;
	}

	#region No divergence
	[Theory]
	[InlineData("AB", "AB")]
	[InlineData("[AB]", "A|B")]
	[InlineData("\\A\\A", "\\C(\\A\\A)")]
	[InlineData("A\\sB", "A\\s!!B")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "\\A\\A?\\9\\X? \\s!! \\9\\A\\A")]
	public void SameCanonicalLanguage_IsZero(string a, string b)
		=> Assert.Equal(0UL, FirstDivergent(a, b));

	/// <summary>
	/// Values added after every existing one leave the existing ones decoding as before, so
	/// there is no divergence to report, however many were added.
	/// </summary>
	[Theory]
	[InlineData("[AB]", "[ABC]")]
	[InlineData("AB", "AB|AC")]
	[InlineData("AB", "AB|ABC")]
	[InlineData("A", "A|B{2,9}")]
	public void ValuesAddedAfterEverythingElse_IsZero(string a, string b)
	{
		Assert.Equal(0UL, FirstDivergent(a, b));
		Assert.Equal(0UL, FirstDivergent(b, a));
	}
	#endregion
	#region Divergence
	/// <summary>
	/// A string inserted among the others pushes everything after it along by one, so the
	/// first value at or after the insertion decodes differently.
	/// </summary>
	[Theory]
	[InlineData("[AC]", "[ABC]", 2UL)]
	[InlineData("AC", "AB|AC", 1UL)]
	[InlineData("A|AB", "A|AC", 2UL)]
	public void ValuesMoved_IsTheFirstMoved(string a, string b, ulong expected)
	{
		Assert.Equal(expected, FirstDivergent(a, b));
		Assert.Equal(expected, FirstDivergent(b, a));
	}

	/// <summary>
	/// The empty string comes first in the order, so a naxp that accepts it and one that does
	/// not diverge at value 1.
	/// </summary>
	[Fact]
	public void OneAcceptsTheEmptyString_IsOne()
	{
		Assert.Equal(1UL, FirstDivergent("A?", "A"));
		Assert.Equal(1UL, FirstDivergent("A", "A?"));
	}

	/// <summary>
	/// Where one side's continuation runs out inside a chunk the other side's continues, the
	/// next value on the longer side is compared against the next chunk on the shorter one.
	/// </summary>
	[Fact]
	public void OneContinuationRunsOutFirst_IsTheValueAfterIt()
	{
		Assert.Equal(2UL, FirstDivergent("A[BC]|D", "AB|D"));
		Assert.Equal(2UL, FirstDivergent("AB|D", "A[BC]|D"));
	}

	/// <summary>
	/// Two naxps that value every string alike can still decode every value differently, which
	/// is the reason this is a separate question from the encoding relation.
	/// </summary>
	[Theory]
	[InlineData("(A|B)!A", "(A|B)!B")]
	[InlineData("[\\s\\-]?!\\-", "[\\s\\-]!?")]
	[InlineData("\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)")]
	public void SameValuesDifferentText_IsOne(string a, string b)
	{
		Assert.True(Relations.TryCompareEncodings(Compiled(a), Compiled(b), out SetRelationship encoding));
		Assert.Equal(SetRelationship.Equal, encoding);

		Assert.Equal(1UL, FirstDivergent(a, b));
	}
	#endregion
	#region The postcode cases the site quotes
	/// <summary>
	/// The site's figure: <c>FZ9Z 9ZZ</c> agrees at 405 194 400, and the next value is
	/// <c>G0 0AA</c> under one and <c>H0 0AA</c> under the other.
	/// </summary>
	[Fact]
	public void AddingGirToThePostcode_DivergesWhereTheSiteSays()
	{
		const string Without = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string With = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA";

		ulong value = FirstDivergent(Without, With);

		Assert.Equal(405_194_401UL, value);
		Assert.Equal("G0 0AA", Naxp.Parse(Without).Decode(value));
		Assert.Equal("H0 0AA", Naxp.Parse(With).Decode(value));
		Assert.Equal("FZ9Z 9ZZ", Naxp.Parse(Without).Decode(value - 1UL));
		Assert.Equal("FZ9Z 9ZZ", Naxp.Parse(With).Decode(value - 1UL));
	}

	/// <summary>
	/// Restricting the letters to those the Royal Mail guide lists diverges almost at once.
	/// </summary>
	[Fact]
	public void TighteningThePostcodeLetters_DivergesAtThree()
	{
		const string Loose = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string Tight = "[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]";

		ulong value = FirstDivergent(Loose, Tight);

		Assert.Equal(3UL, value);
		Assert.NotEqual(Naxp.Parse(Loose).Decode(value), Naxp.Parse(Tight).Decode(value));
		Assert.Equal(Naxp.Parse(Loose).Decode(value - 1UL), Naxp.Parse(Tight).Decode(value - 1UL));
	}
	#endregion
	#region Against the slow way
	[Theory]
	[InlineData("[AB]", "[ABC]")]
	[InlineData("[AC]", "[ABC]")]
	[InlineData("[BC]", "[ABC]")]
	[InlineData("AB|AC", "AB|AC|AD")]
	[InlineData("AB|AD", "AB|AC|AD")]
	[InlineData("\\A\\9", "\\A\\X")]
	[InlineData("\\9\\A", "\\X\\A")]
	[InlineData("A|BB", "A|BB|C")]
	[InlineData("A|BB", "A|AB|BB")]
	[InlineData("\\A{2}", "\\A{2,3}")]
	[InlineData("\\A{2,3}", "\\A{2}")]
	[InlineData("#[00-99]", "#[00-99]|AA")]
	[InlineData("#[0-105]", "#[0?0!0-105]")]
	[InlineData("A[BC]|D", "AB|D")]
	[InlineData("A?", "A")]
	[InlineData("(A|B)!A", "(A|B)!B")]
	[InlineData("\\A\\9\\s!!\\A", "\\A\\9\\s!?\\A")]
	[InlineData("\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)")]
	public void TheWalkAgreesWithEnumeration(string a, string b)
		=> Assert.Equal(FirstDivergentByEnumeration(a, b), FirstDivergent(a, b));
	#endregion
	#region Direction
	/// <summary>
	/// The question is symmetric. It asks about the values both hold, and neither side is
	/// privileged.
	/// </summary>
	[Theory]
	[InlineData("[AC]", "[ABC]")]
	[InlineData("A[BC]|D", "AB|D")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA")]
	public void SwappingTheArguments_KeepsTheValue(string a, string b)
		=> Assert.Equal(FirstDivergent(a, b), FirstDivergent(b, a));
	#endregion
}
