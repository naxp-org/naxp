// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Linq;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The padding marks on a decimal range's lower bound, <c>0!</c> and <c>0?</c>: what they expand
/// to, which spellings they admit, and what they leave the encoding as.
/// </summary>
public class PaddingTests
{
	#region What a mark expands to
	/// <summary>
	/// <c>0!</c> is <c>0!!</c>, which is <c>0?!(0)</c>, and <c>0?</c> is <c>0!?</c>.
	/// </summary>
	[Fact]
	public void BangMark_ExpandsToAnOptionalZeroRenderedAsAZero()
	{
		AstUnified pad = FirstPadOf("#[0!0-99]");

		Assert.Equal(UnifiedForm.Reproduced, pad.Form);
		Assert.Equal(AsciiCharSet.FromSingleChar('0'), Assert.IsType<AstChars>(Assert.IsType<AstOptional>(pad.Subject).Child).CharSet);
		Assert.Equal(AsciiCharSet.FromSingleChar('0'), Assert.IsType<AstChars>(pad.Rendering).CharSet);
	}

	[Fact]
	public void QueryMark_ExpandsToAnOptionalZeroRenderedAsNothing()
	{
		AstUnified pad = FirstPadOf("#[0?0-99]");

		Assert.Equal(UnifiedForm.Dropped, pad.Form);
		Assert.Equal(AsciiCharSet.FromSingleChar('0'), Assert.IsType<AstChars>(Assert.IsType<AstOptional>(pad.Subject).Child).CharSet);
		Assert.IsType<AstEmpty>(pad.Rendering);
	}

	/// <summary>
	/// An unmarked range is left as the node it always was, so nothing pays for a feature it
	/// does not use.
	/// </summary>
	[Fact]
	public void UnmarkedRange_IsNotExpanded()
		=> Assert.IsType<AstDecimalRange>(Parse("#[00-105]"));

	/// <summary>
	/// The alternation runs one branch per width the value takes, widest last, and the padding in
	/// front of each branch is the positions that width leaves over.
	/// </summary>
	[Fact]
	public void Expansion_IsOneBranchPerWidthOfTheValue()
	{
		AstAlternation alternation = Assert.IsType<AstAlternation>(Parse("#[0?0!0-105]"));

		Assert.Equal(3, alternation.Children.Count);
		Assert.Equal(3, Assert.IsType<AstSequence>(alternation.Children[0]).Children.Count);
		Assert.Equal(2, Assert.IsType<AstSequence>(alternation.Children[1]).Children.Count);
		Assert.IsType<AstDecimalRange>(alternation.Children[2]);
	}
	#endregion
	#region Which spellings a marked range admits
	[Theory]
	[InlineData("#[0!0!0-105]", "7", true)]
	[InlineData("#[0!0!0-105]", "07", true)]
	[InlineData("#[0!0!0-105]", "007", true)]
	[InlineData("#[0!0!0-105]", "0007", false)]
	[InlineData("#[0!0!0-105]", "106", false)]
	[InlineData("#[0!0!0-105]", "0105", false)]
	// An unmarked padding position stays mandatory, so this one has a minimum width of two.
	[InlineData("#[00!0-999]", "7", false)]
	[InlineData("#[00!0-999]", "07", true)]
	[InlineData("#[00!0-999]", "007", true)]
	[InlineData("#[00!0-999]", "42", false)]
	[InlineData("#[00!0-999]", "042", true)]
	public void MarkedRange_AdmitsTheWidthsItsMarksAllow(string pattern, string text, bool expected)
		=> Assert.Equal(expected, Naxp.Parse(pattern).Accepts(text));
	#endregion
	#region What a mark does to the canonical form
	/// <summary>
	/// A <c>!</c> keeps its zero and a <c>?</c> drops it, and the marks are read outside in.
	/// </summary>
	[Theory]
	[InlineData("#[0!0!0-105]", "007", "042", "105")]
	[InlineData("#[0?0?0-105]", "7", "42", "105")]
	[InlineData("#[0?0!0-105]", "07", "42", "105")]
	[InlineData("#[0!0?0-105]", "07", "042", "105")]
	public void Marks_FixTheCanonicalWidth(string pattern, string seven, string fortyTwo, string oneOhFive)
	{
		var naxp = Naxp.Parse(pattern);

		foreach (string spelling in new[] { "7", "07", "007" })
		{
			Assert.Equal(seven, naxp.GetCanonicalForm(spelling));
		}

		Assert.Equal(fortyTwo, naxp.GetCanonicalForm("42"));
		Assert.Equal(fortyTwo, naxp.GetCanonicalForm("042"));
		Assert.Equal(oneOhFive, naxp.GetCanonicalForm("105"));
	}
	#endregion
	#region What a mark leaves the encoding as
	/// <summary>
	/// A mark changes which spellings are accepted and nothing else, so a marked range assigns the
	/// values of the range whose lower bound its canonical forms are written to.
	/// </summary>
	[Theory]
	[InlineData("#[0!0!0-105]", "#[000-105]")]
	[InlineData("#[0?0!0-105]", "#[00-105]")]
	[InlineData("#[0?0?0-105]", "#[0-105]")]
	public void MarkedRange_AssignsTheValuesOfTheRangeItCanonicalisesTo(string marked, string plain)
	{
		var withMarks = Naxp.Parse(marked);
		var without = Naxp.Parse(plain);

		Assert.Equal(without.MaxEncodedValue, withMarks.MaxEncodedValue);

		for (ulong value = 1UL; value <= without.MaxEncodedValue; ++value)
		{
			Assert.Equal(without.Decode(value), withMarks.Decode(value));
		}
	}

	/// <summary>
	/// Marking every padding position <c>!</c> makes the canonical language fixed width, which is
	/// the condition the ordering guarantee is stated on.
	/// </summary>
	[Fact]
	public void EveryPositionMarkedToKeepItsZero_OrdersNumerically()
	{
		var naxp = Naxp.Parse("#[0!0!0-105]");
		ulong previous = 0UL;

		for (int number = 0; number <= 105; ++number)
		{
			ulong value = naxp.Encode(number.ToString(System.Globalization.CultureInfo.InvariantCulture));

			Assert.True(value > previous, $"{number} did not encode above the number below it.");
			previous = value;
		}
	}

	/// <summary>
	/// The flight designator of the landing page: two characters of airline, then a number that
	/// may be written with or without its leading zeros and is stored with them.
	/// </summary>
	[Fact]
	public void FlightDesignator_TakesEitherSpellingAndOrdersByNumber()
	{
		var naxp = Naxp.Parse("(\\A\\A|\\A\\9|\\9\\A) #[0!0!0!1-9999]");

		Assert.Equal(11_958_804UL, naxp.MaxEncodedValue);
		Assert.Equal(naxp.Encode("AC861"), naxp.Encode("AC0861"));
		Assert.Equal("AC0861", naxp.Decode(naxp.Encode("AC861")));
		Assert.True(naxp.Encode("AC861") > naxp.Encode("AC860"));

		// The airline still leads, so a later designator outranks an earlier one whatever the
		// number, and a designator of a letter and a digit is admitted.
		Assert.True(naxp.Encode("BA1") > naxp.Encode("AC9999"));
		Assert.Equal("U20008", naxp.Decode(naxp.Encode("U28")));
	}

	/// <summary>
	/// A mark cannot emit until it knows how long the input is, so its machine holds what it has
	/// read. Holding it as a reference to how far back it was read rather than as its value costs
	/// a state per shape instead of a state per string, so the machine is no longer what binds.
	/// What binds is the W3 square, whose pair count is quartic in the width.
	/// </summary>
	[Fact]
	public void AFullyMarkedRange_ReachesElevenDigits_AndW3IsWhatStopsIt()
	{
		Naxp.Parse("#[" + Repeat("0!", 10) + "1-" + Repeat("9", 11) + "]");
		Naxp.Parse("#[" + Repeat("0?", 10) + "1-" + Repeat("9", 11) + "]");

		string wider = "#[" + Repeat("0!", 11) + "1-" + Repeat("9", 12) + "]";

		Assert.False(Compiler.TryCompile(wider, out _, out NaxpError? error));
		Assert.Equal(NaxpMessage.NAXP1050_TooManyPairStates, error!.Value.Message);
	}

	/// <summary>
	/// The square pairs residuals, and a marked range reaches one chain per width it accepts, so
	/// fewer marks means fewer widths and a smaller square. One mark reaches the cap on a bound.
	/// </summary>
	[Fact]
	public void ALightlyMarkedRange_GoesWider()
	{
		Naxp.Parse("#[0!" + Repeat("0", 13) + "-" + Repeat("9", 15) + "]");

		string wider = "#[0!0!" + Repeat("0", 13) + "-" + Repeat("9", 15) + "]";

		Assert.False(Compiler.TryCompile(wider, out _, out NaxpError? error));
		Assert.Equal(NaxpMessage.NAXP1050_TooManyPairStates, error!.Value.Message);
	}

	static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
	#endregion
	#region Faults
	[Theory]
	// The last digit is the number, not padding in front of it.
	[InlineData("#[0!-9]", "W4", "zeros at the front of a number")]
	[InlineData("#[10?5-999]", "W4", "zeros at the front of a number")]
	// Only a zero stands for padding.
	[InlineData("#[1!05-999]", "W4", "Only a zero may be marked")]
	// The upper bound does not fix the width, so it carries no marks.
	[InlineData("#[0-9!]", "W4", "Only the lower bound")]
	// A mark belongs to the digit before it.
	[InlineData("#[0 !0-9]", "syntax", "separating whitespace")]
	[InlineData("#[0! 0-9]", "syntax", "cannot be separated by whitespace")]
	public void MisplacedMark_IsRefused(string pattern, string rule, string fragment)
	{
		NaxpError error = FaultOf(pattern);

		Assert.Equal(rule, NaxpMessageRules.RuleOf(error.Message));
		Assert.Contains(fragment, error.Text, StringComparison.Ordinal);
	}

	/// <summary>
	/// A marked range holds unified elements once expanded, so W2 reaches it where the unmarked
	/// form was legal.
	/// </summary>
	[Fact]
	public void MarkedRangeInsideAUnifiedElement_BreaksW2()
	{
		Assert.Equal("W2", NaxpMessageRules.RuleOf(FaultOf("(#[0!0-105])!(07)").Message));

		// The same naxp without the mark is fine.
		Naxp.Parse("(#[00-105])!(07)");
	}

	/// <summary>
	/// A marked range accepts more than one width, so what follows it has to fix where it stops.
	/// A single digit does; an optional one does not.
	/// </summary>
	[Fact]
	public void MarkedRangeBesideAVariableWidthNeighbour_BreaksW3()
	{
		Naxp.Parse("#[0!0!0-105]\\9");

		Assert.False(Compiler.TryCompile("#[0!0!0-105]\\9?", out _, out NaxpError? error));
		Assert.Equal("W3", NaxpMessageRules.RuleOf(error!.Value.Message));
	}
	#endregion
	#region Helpers
	/// <summary>
	/// The padding element of the narrowest branch, which for a range with one padding position
	/// is the branch the alternation leads with.
	/// </summary>
	static AstUnified FirstPadOf(string pattern)
	{
		AstAlternation alternation = Assert.IsType<AstAlternation>(Parse(pattern));
		AstSequence shorter = Assert.IsType<AstSequence>(alternation.Children[0]);

		return Assert.IsType<AstUnified>(shorter.Children[0]);
	}

	static Ast Parse(string text)
	{
		Assert.True(Parser.TryParse(text, out Ast? ast, out NaxpError? error), $"{text} was invalid: {error}");
		Assert.True(WellFormedness.TryCheck(ast!, out error), $"{text} was invalid: {error}");

		return ast!;
	}

	static NaxpError FaultOf(string text)
	{
		if (!Parser.TryParse(text, out Ast? ast, out NaxpError? error)) { return error!.Value; }

		Assert.False(WellFormedness.TryCheck(ast!, out error), $"{text} was accepted.");

		return error!.Value;
	}
	#endregion
}
