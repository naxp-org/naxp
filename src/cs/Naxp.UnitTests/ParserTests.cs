// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The parser beyond what the conformance test data covers: the shape of the tree, and the error
/// productions for syntax that is plausibly wrong rather than merely invalid.
/// </summary>
public class ParserTests
{
	#region Tree shape
	[Fact]
	public void Group_DoesNotSurviveParsing()
	{
		Ast bare = Parse("A");
		Ast grouped = Parse("(A)");

		Assert.IsType<AstChars>(bare);
		Assert.IsType<AstChars>(grouped);
		Assert.Equal(((AstChars)bare).CharSet, ((AstChars)grouped).CharSet);
	}

	[Fact]
	public void EmptyGroup_IsTheEmptyString()
		=> Assert.IsType<AstEmpty>(Parse("()"));

	/// <summary>
	/// <c>x!!</c> is <c>x?!(x)</c>, and the expansion is structural rather than textual.
	/// </summary>
	[Fact]
	public void DoubleBang_ExpandsToAnOptionalSubjectRenderedAsItself()
	{
		AstReplaceable replaceable = Assert.IsType<AstReplaceable>(Parse("\\s!!"));

		Assert.Equal(ReplaceableForm.Reproduced, replaceable.Form);
		AstOptional subject = Assert.IsType<AstOptional>(replaceable.Subject);
		Assert.Same(subject.Child, replaceable.Rendering);
	}

	/// <summary>
	/// <c>x!?</c> is <c>x?!()</c>.
	/// </summary>
	[Fact]
	public void BangQuery_ExpandsToAnOptionalSubjectRenderedAsNothing()
	{
		AstReplaceable replaceable = Assert.IsType<AstReplaceable>(Parse("\\A!?"));

		Assert.Equal(ReplaceableForm.Dropped, replaceable.Form);
		Assert.IsType<AstOptional>(replaceable.Subject);
		Assert.IsType<AstEmpty>(replaceable.Rendering);
	}

	/// <summary>
	/// A quantifier binds to the base before it and does not reach back over the sequence.
	/// </summary>
	[Fact]
	public void Quantifier_BindsToTheBaseBeforeIt()
	{
		AstSequence sequence = Assert.IsType<AstSequence>(Parse("AB?"));

		Assert.Equal(2, sequence.Children.Count);
		Assert.IsType<AstChars>(sequence.Children[0]);
		Assert.IsType<AstOptional>(sequence.Children[1]);
	}

	[Fact]
	public void Interval_KeepsBothCounts()
	{
		AstInterval interval = Assert.IsType<AstInterval>(Parse("A{2,4}"));

		Assert.Equal(2, interval.MinCount);
		Assert.Equal(4, interval.MaxCount);
	}

	[Fact]
	public void Interval_WithOneCount_UsesItForBoth()
	{
		AstInterval interval = Assert.IsType<AstInterval>(Parse("A{3}"));

		Assert.Equal(3, interval.MinCount);
		Assert.Equal(3, interval.MaxCount);
	}

	[Fact]
	public void DigitsRange_KeepsTheWidthsAsWritten()
	{
		AstDigitsRange padded = Assert.IsType<AstDigitsRange>(Parse("#[00-105]"));

		Assert.Equal(0UL, padded.Low);
		Assert.Equal(2, padded.LowDigitCount);
		Assert.Equal(105UL, padded.High);
		Assert.Equal(3, padded.HighDigitCount);
	}
	#endregion
	#region Whitespace
	/// <summary>
	/// In each of these the separator is a token in its own right, so whitespace around it is
	/// whitespace between tokens.
	/// </summary>
	[Theory]
	[InlineData("[A - E]", "[A-E]")]
	[InlineData("A{2 , 5}", "A{2,5}")]
	[InlineData("#[0 - 10]", "#[0-10]")]
	[InlineData(" A | B ", "A|B")]
	[InlineData("\\s !!", "\\s!!")]
	public void Whitespace_BetweenTokens_IsIgnored(string spaced, string tight)
	{
		Ast withSpaces = Parse(spaced);
		Ast withoutSpaces = Parse(tight);

		Assert.Equal(withoutSpaces.GetType(), withSpaces.GetType());

		foreach (string text in new[] { "A", "C", "E", "2", "10", "AAAAA", " ", "" })
		{
			Assert.Equal(
				Matcher.Generates(withoutSpaces, text, out _),
				Matcher.Generates(withSpaces, text, out _));
		}
	}
	#endregion
	#region Error productions for near misses
	/// <summary>
	/// Every regular expression dialect separates interval counts with a comma, so this is the
	/// commonest mistake a regex-literate user will make.
	/// </summary>
	[Fact]
	public void Interval_WithAHyphen_NamesTheSeparator()
	{
		NaxpError error = Refuse("A{2-5}");

		Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
		Assert.Equal(3, error.Offset);
		Assert.Contains("',', not by a hyphen", error.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void Interval_Unbounded_SaysThereIsNone()
	{
		NaxpError error = Refuse("A{2,}");

		Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
		Assert.Contains("no unbounded interval", error.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void BareBang_NamesTheThreeForms()
	{
		NaxpError error = Refuse("A!");

		Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
		Assert.Equal(1, error.Offset);
		Assert.Contains("'x!y', 'x!!' or 'x!?'", error.Text, StringComparison.Ordinal);
	}

	/// <summary>
	/// Version 0.3 removed the hex escape, so anyone who knows regex or an earlier draft will
	/// write this and deserves to be told why it has gone.
	/// </summary>
	[Fact]
	public void HexEscape_SaysItWasRemoved()
	{
		NaxpError error = Refuse("\\x41");

		Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
		Assert.Equal(0, error.Offset);
		Assert.Contains("removed in version 0.3", error.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void UndefinedEscape_ListsTheEscapeLetters()
	{
		NaxpError error = Refuse("\\d");

		Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
		Assert.Contains("'s', '9', 'A', 'a' and 'X'", error.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void RangeWrittenBackwards_SaysLowestFirst()
	{
		NaxpError error = Refuse("[E-A]");

		Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
		Assert.Contains("lowest first", error.Text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("\\ s", 1, "cannot be followed by whitespace")]
	[InlineData("A! !", 2, "is one token")]
	[InlineData("A{2 5}", 3, "cannot be separated by whitespace")]
	[InlineData("# [0-10]", 1, "no whitespace between '#' and '['")]
	[InlineData("#[1 0-20]", 3, "cannot be separated by whitespace")]
	public void WhitespaceSplittingAToken_PointsAtTheWhitespace(string text, int offset, string fragment)
	{
		NaxpError error = Refuse(text);

		Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
		Assert.Equal(offset, error.Offset);
		Assert.Contains(fragment, error.Text, StringComparison.Ordinal);
	}
	/// <summary>
	/// Refusals the test data does not cover, kept here so the parser cannot quietly grow lax.
	/// </summary>
	[Theory]
	[InlineData("A)", "syntax")]
	[InlineData("A-B", "syntax")]
	[InlineData("[\\9-A]", "syntax")]
	[InlineData("[A-]", "syntax")]
	[InlineData("A{}", "syntax")]
	[InlineData("A{2,1}", "W4")]
	[InlineData("#[5-4]", "W4")]
	[InlineData("(A|B)!(A|B)", "W1")]
	public void FurtherRefusals(string text, string rule)
		=> Assert.Equal(rule, NaxpMessageRules.RuleOf(Refuse(text).Message));
	#endregion
	#region Source repertoire
	/// <summary>
	/// The source may hold whitespace and the printable ASCII characters U+0021 to U+007E.
	/// </summary>
	[Fact]
	public void SourceOutsideTheRepertoire_IsRefused()
	{
		foreach (char c in new[] { '\u00E9', '\u0001', '\u007F' })
		{
			NaxpError error = Refuse("A" + c);

			Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
			Assert.Equal(1, error.Offset);
			Assert.Contains("cannot appear in the source", error.Text, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void EmptySource_IsNotANaxp()
	{
		NaxpError error = Refuse(string.Empty);

		Assert.Equal("syntax", NaxpMessageRules.RuleOf(error.Message));
	}
	#endregion
	#region Matching
	[Theory]
	[InlineData("#[0-10]", "0", true)]
	[InlineData("#[0-10]", "9", true)]
	[InlineData("#[0-10]", "10", true)]
	[InlineData("#[0-10]", "00", false)]
	[InlineData("#[0-10]", "11", false)]
	[InlineData("#[00-10]", "00", true)]
	[InlineData("#[00-10]", "7", false)]
	[InlineData("#[00-105]", "07", true)]
	[InlineData("#[00-105]", "007", false)]
	[InlineData("#[0-105]", "07", false)]
	[InlineData("#[0-105]", "105", true)]
	[InlineData("#[0-105]", "106", false)]
	public void DigitsRange_MatchesTheWidthsItsBoundsFix(string naxp, string text, bool expected)
		=> Assert.Equal(expected, Matcher.Generates(Parse(naxp), text, out _));

	[Theory]
	[InlineData("A{0,3}", "", true)]
	[InlineData("A{0,3}", "AAA", true)]
	[InlineData("A{0,3}", "AAAA", false)]
	[InlineData("A{0}", "", true)]
	[InlineData("A{0}", "A", false)]
	[InlineData("(A?){9}", "AAA", true)]
	public void Interval_MatchesItsCounts(string naxp, string text, bool expected)
		=> Assert.Equal(expected, Matcher.Generates(Parse(naxp), text, out _));
	#endregion
	#region Helpers
	static Ast Parse(string text)
	{
		Assert.True(Parser.TryParse(text, out Ast? ast, out NaxpError? error), $"{text} was refused: {error}");
		Assert.True(WellFormedness.TryCheck(ast!, out error), $"{text} was refused: {error}");

		return ast!;
	}

	static NaxpError Refuse(string text)
	{
		if (!Parser.TryParse(text, out Ast? ast, out NaxpError? error)) { return error!.Value; }

		Assert.False(WellFormedness.TryCheck(ast!, out error), $"{text} was accepted.");

		return error!.Value;
	}
	#endregion
}
