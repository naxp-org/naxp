// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// Whether two naxps give the same encoded value to the strings they share.
/// </summary>
public class RankAgreementTests
{
	static Agreement Ranks(string a, string b)
		=> RankAgreement.Compare(Compiled(a).Canonical, Compiled(b).Canonical);

	static Compilation Compiled(string pattern)
	{
		Assert.True(Compiler.TryCompile(pattern, out Compilation? compilation, out NaxpError? error), $"{pattern}: {error?.Message}");

		return compilation!;
	}

	/// <summary>
	/// Every string the two share, checked one at a time through the public surface. The slow
	/// and obvious way, so that the walk has something independent to answer to.
	/// </summary>
	static Agreement RanksByEnumeration(string a, string b)
	{
		Naxp left = Naxp.Parse(a);
		Naxp right = Naxp.Parse(b);

		for (ulong value = 1UL; value <= left.MaxEncodedValue; value++)
		{
			string text = left.Decode(value);

			if (right.Accepts(text) && right.Encode(text) != value) { return Agreement.Differs; }
		}

		return Agreement.Agrees;
	}

	#region Agreement
	[Theory]
	[InlineData("AB", "AB")]
	[InlineData("[AB]", "A|B")]
	[InlineData("A\\sB", "A\\s!!B")]
	[InlineData("\\A\\A", "\\C(\\A\\A)")]
	public void SameCanonicalLanguage_Agrees(string a, string b)
		=> Assert.Equal(Agreement.Agrees, Ranks(a, b));

	/// <summary>
	/// A shorter language whose strings keep their places agrees, because agreement is only
	/// ever asked of the strings the two share.
	/// </summary>
	[Theory]
	[InlineData("[AB]", "[ABC]")]
	[InlineData("AB", "AB|AC")]
	public void AddingAfterEverythingElse_Agrees(string a, string b)
		=> Assert.Equal(Agreement.Agrees, Ranks(a, b));
	#endregion
	#region Disagreement
	/// <summary>
	/// A string inserted among the others pushes everything after it along by one.
	/// </summary>
	[Theory]
	[InlineData("[AC]", "[ABC]")]
	[InlineData("AC", "AB|AC")]
	public void AddingInTheMiddle_Differs(string a, string b)
		=> Assert.Equal(Agreement.Differs, Ranks(a, b));

	/// <summary>
	/// The site's own case. <c>GIR 0AA</c> takes the highest value of all and still moves the
	/// codes beneath it, because it splits <c>G</c> out of its first class.
	/// </summary>
	[Fact]
	public void AddingGirToThePostcode_Differs()
	{
		const string Without = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string With = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA";

		Assert.Equal(Agreement.Differs, Ranks(Without, With));
	}

	/// <summary>
	/// Restricting the letters to those the Royal Mail guide lists disagrees almost at once.
	/// </summary>
	[Fact]
	public void TighteningThePostcodeLetters_Differs()
	{
		const string Loose = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string Tight = "[A-PR-UWYZ][A-HJ-Y]?\\9[\\9A-HJKPSTUW]? \\s!! \\9[ABD-HJLNP-UW-Z][ABD-HJLNP-UW-Z]";

		Assert.Equal(Agreement.Differs, Ranks(Loose, Tight));
	}
	#endregion
	#region Against the slow way
	/// <summary>
	/// The walk and an enumeration of every shared string must reach the same verdict. The
	/// naxps here are small enough to enumerate, which is the only reason this can be asked.
	/// </summary>
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
	public void TheWalkAgreesWithEnumeration(string a, string b)
		=> Assert.Equal(RanksByEnumeration(a, b), Ranks(a, b));
	#endregion
	#region Direction
	/// <summary>
	/// The question is symmetric: it asks about the strings both hold, and neither side is
	/// privileged.
	/// </summary>
	[Theory]
	[InlineData("[AC]", "[ABC]")]
	[InlineData("[AB]", "[ABC]")]
	public void SwappingTheArguments_KeepsTheVerdict(string a, string b)
		=> Assert.Equal(Ranks(a, b), Ranks(b, a));
	#endregion
	#region The budget
	/// <summary>
	/// A walk given no room says so rather than guessing.
	/// </summary>
	[Fact]
	public void ABudgetTooSmall_IsUndecided()
	{
		StateMap a = Compiled("\\A\\A?\\9\\X? \\s!! \\9\\A\\A").Canonical;
		StateMap b = Compiled("\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA").Canonical;

		Assert.Equal(Agreement.Undecided, RankAgreement.Compare(a, b, maxPairs: 2));
	}

	/// <summary>
	/// The guard against a saturated count cannot be reached through a compiled naxp, because W5
	/// refuses any naxp holding more than 2^64 - 1 values in the first place. The largest one
	/// there is comes nowhere near saturating, and this says so, so the guard is read as
	/// defensive rather than as something a caller can trip.
	/// </summary>
	[Fact]
	public void AValidNaxpNeverHasASaturatedCount()
	{
		Compilation biggest = Compiled("\\9{19}");

		Assert.False(biggest.Canonical.CountSaturated);
		Assert.Equal(10_000_000_000_000_000_000UL, biggest.MaxEncodedValue);
		Assert.Equal(Agreement.Agrees, RankAgreement.Compare(biggest.Canonical, biggest.Canonical));
	}
	#endregion
}
