// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The case folds <c>\C</c> and <c>\c</c>: what they expand to, what they bind to, and what a
/// fold does to the encoding of the naxp it wraps.
/// </summary>
public class FoldingTests
{
	#region What a fold expands to
	/// <summary>
	/// A fold over one letter is the unified element the specification says it is.
	/// </summary>
	[Fact]
	public void FoldOverOneLetter_IsAPairUnifiedToTheCanonicalCase()
	{
		AstUnified upper = Assert.IsType<AstUnified>(Parse("\\CA"));

		Assert.Equal(UnifiedForm.Fold, upper.Form);
		Assert.Equal(SetOf("Aa"), Assert.IsType<AstChars>(upper.Subject).CharSet);
		Assert.Equal(SetOf("A"), Assert.IsType<AstChars>(upper.Rendering).CharSet);

		AstUnified lower = Assert.IsType<AstUnified>(Parse("\\cA"));

		Assert.Equal(SetOf("Aa"), Assert.IsType<AstChars>(lower.Subject).CharSet);
		Assert.Equal(SetOf("a"), Assert.IsType<AstChars>(lower.Rendering).CharSet);
	}

	/// <summary>
	/// The case a set is written in does not decide the canonical case; the operator does.
	/// </summary>
	[Fact]
	public void FoldOverASet_IgnoresTheCaseItWasWrittenIn()
	{
		Assert.Equal(RenderingsOf("\\C[A-F]"), RenderingsOf("\\C[a-f]"));
		Assert.Equal(RenderingsOf("\\C[A-F]"), RenderingsOf("\\C[a-fA-F]"));
		Assert.Equal("ABCDEF", RenderingsOf("\\C[a-f]"));
		Assert.Equal("abcdef", RenderingsOf("\\c[A-F]"));
	}

	/// <summary>
	/// Characters with no case pass through untouched, in a set of their own.
	/// </summary>
	[Fact]
	public void FoldOverAMixedSet_LeavesTheUncasedCharactersAlone()
	{
		AstAlternation alternation = Assert.IsType<AstAlternation>(Parse("\\C[\\9A-F]"));

		Assert.Equal(7, alternation.Children.Count);
		Assert.Equal(AsciiCharSet.AllDigits, Assert.IsType<AstChars>(alternation.Children[0]).CharSet);
		Assert.Equal("ABCDEF", RenderingsOf("\\C[\\9A-F]"));
	}

	/// <summary>
	/// A fold over characters with no case is not an error, it just has nothing to do.
	/// </summary>
	[Fact]
	public void FoldOverUncasedCharacters_DoesNothing()
	{
		Assert.Equal(AsciiCharSet.AllDigits, Assert.IsType<AstChars>(Parse("\\C\\9")).CharSet);
		Assert.IsType<AstDecimalRange>(Parse("\\C#[0-10]"));
		Assert.False(Ast.ContainsUnified(Parse("\\C\\9{4}")));
	}
	#endregion
	#region What a fold binds to
	/// <summary>
	/// A case fold runs to the end of the enclosing group, across any <c>|</c>; grouping the
	/// fold is how it is stopped short.
	/// </summary>
	[Fact]
	public void CaseFold_RunsToTheEndOfTheEnclosingGroup()
	{
		var naxp = Naxp.Parse("\\CAB");

		Assert.True(naxp.Accepts("aB"));
		Assert.True(naxp.Accepts("Ab"));
		Assert.Equal("AB", naxp.GetCanonicalForm("ab"));
		Assert.False(Naxp.Parse("(\\CA)B").Accepts("Ab"));

		var alternation = Naxp.Parse("\\CA|b");

		Assert.Equal("B", alternation.GetCanonicalForm("b"));
		Assert.Equal(2UL, alternation.MaxEncodedValue);
	}

	/// <summary>
	/// A case fold written after another sits inside its extent, and the outer governs.
	/// </summary>
	[Fact]
	public void CaseFold_WrittenLaterSitsInsideTheEarlierOne()
	{
		Assert.Equal("AB", Naxp.Parse("\\Ca\\cb").GetCanonicalForm("ab"));
	}

	/// <summary>
	/// A case fold cannot begin a rendering: it would run to the end of the group, and a fold
	/// with anything to do is a <c>!</c> once expanded, which W2 refuses there.
	/// </summary>
	[Fact]
	public void CaseFold_CannotBeginARendering()
	{
		NaxpError error = FaultOf("A!\\CA");

		Assert.Equal(NaxpMessage.NAXP1058_FoldBeginsRendering, error.Message);
		Assert.Equal(2, error.Offset);
	}

	/// <summary>
	/// A fold binds looser than <c>!</c>, so it widens the subject and canonicalises the
	/// rendering rather than putting a <c>!</c> inside the subject of one.
	/// </summary>
	/// <remarks>
	/// Were it the other way round <c>\CA!A</c> would break W2, which is the whole reason for
	/// the choice.
	/// </remarks>
	[Fact]
	public void Fold_BindsLooserThanUnification()
	{
		AstUnified unified = Assert.IsType<AstUnified>(Parse("\\CA!A"));

		Assert.Equal(UnifiedForm.Explicit, unified.Form);
		Assert.Equal(SetOf("Aa"), Assert.IsType<AstChars>(unified.Subject).CharSet);
		Assert.Equal(SetOf("A"), Assert.IsType<AstChars>(unified.Rendering).CharSet);
	}

	/// <summary>
	/// A fold binds looser than a quantifier too, so it reaches inside one.
	/// </summary>
	[Fact]
	public void Fold_BindsLooserThanAQuantifier()
	{
		Assert.IsType<AstOptional>(Parse("\\CA?"));
		Assert.IsType<AstInterval>(Parse("\\CA{2}"));
		Assert.True(Naxp.Parse("\\CA{2}").Accepts("aA"));
	}

	/// <summary>
	/// <c>x!!</c> keeps its rendering while its subject widens, which is what makes a fold over
	/// an optional separator work.
	/// </summary>
	[Fact]
	public void FoldOverAReproducedUnified_WidensTheSubjectAndKeepsTheRendering()
	{
		var naxp = Naxp.Parse("\\C((AB)!!)");

		Assert.True(naxp.Accepts("ab"));
		Assert.True(naxp.Accepts("aB"));
		Assert.True(naxp.Accepts(""));
		Assert.Equal("AB", naxp.GetCanonicalForm("ab"));
		Assert.Equal(1UL, naxp.MaxEncodedValue);
	}

	/// <summary>
	/// Where two folds meet the outer governs, so a fold within its extent takes its case.
	/// </summary>
	[Fact]
	public void OuterFold_GovernsAFoldWithinItsExtent()
	{
		var naxp = Naxp.Parse("\\C(AB\\cC)");

		Assert.Equal("ABC", naxp.GetCanonicalForm("abc"));
		Assert.Equal("ABC", naxp.GetCanonicalForm("ABC"));
	}

	/// <summary>
	/// A fold written directly on a fold is the outer one; it is never an error.
	/// </summary>
	[Fact]
	public void FoldOnAFold_IsTheOuterOne()
	{
		var naxp = Naxp.Parse("\\C\\cA");

		Assert.Equal("A", naxp.GetCanonicalForm("a"));
		Assert.Equal(1UL, naxp.MaxEncodedValue);
	}
	#endregion
	#region Wrapping an existing naxp
	/// <summary>
	/// The worked example of the specification, wrapped: more spellings accepted, and every
	/// encoded value exactly where it was.
	/// </summary>
	[Fact]
	public void WrappingThePostcode_AddsSpellingsAndMovesNoValue()
	{
		const string Postcode = "\\A \\A? \\9 \\X? \\s!! \\9 \\A \\A";

		var plain = Naxp.Parse(Postcode);
		var folded = Naxp.Parse("\\C(" + Postcode + ")");

		Assert.Equal(1_755_842_400UL, plain.MaxEncodedValue);
		Assert.Equal(plain.MaxEncodedValue, folded.MaxEncodedValue);

		foreach (string spelling in new[] { "EC1A 1BB", "ec1a 1bb", "Ec1A 1bB", "ec1a1bb" })
		{
			Assert.True(folded.Accepts(spelling), spelling);
			Assert.Equal("EC1A 1BB", folded.GetCanonicalForm(spelling));
			Assert.Equal(plain.Encode("EC1A 1BB"), folded.Encode(spelling));
		}

		Assert.False(plain.Accepts("ec1a 1bb"));
		Assert.Equal(plain.Decode(plain.Encode("EC1A 1BB")), folded.Decode(folded.Encode("ec1a 1bb")));
	}

	/// <summary>
	/// The wrong fold keeps the count of encoded values and their order, and decodes every one of
	/// them into the other case. This is why the fold has to match the case the naxp prints.
	/// </summary>
	[Fact]
	public void TheWrongFold_KeepsTheValuesAndChangesWhatTheyDecodeTo()
	{
		var plain = Naxp.Parse("[a-f]{2}");
		var wrapped = Naxp.Parse("\\C([a-f]{2})");

		Assert.Equal(36UL, plain.MaxEncodedValue);
		Assert.Equal(36UL, wrapped.MaxEncodedValue);
		Assert.Equal(plain.Encode("cd"), wrapped.Encode("cd"));
		Assert.Equal("cd", plain.Decode(plain.Encode("cd")));
		Assert.Equal("CD", wrapped.Decode(wrapped.Encode("cd")));

		// \c is the fold that preserves this naxp, since its canonical strings are lower case.
		var right = Naxp.Parse("\\c([a-f]{2})");

		Assert.Equal("cd", right.Decode(right.Encode("CD")));
	}

	/// <summary>
	/// A fold can make a well-formed naxp invalid, by destroying a case distinction that was
	/// keeping two branches apart.
	/// </summary>
	[Fact]
	public void FoldingCanBreakW3()
	{
		Assert.True(Compiler.TryCompile("A!?|a", out _, out _));
		Assert.False(Compiler.TryCompile("\\C(A!?|a)", out _, out NaxpError? error));
		Assert.Equal("W3", NaxpMessageRules.RuleOf(error!.Value.Message));
	}
	#endregion
	#region Where a fold may not go
	/// <summary>
	/// A fold applies to a whole element, so it is not one of the things a character set holds.
	/// </summary>
	[Fact]
	public void FoldInsideACharacterSet_IsASyntaxError()
	{
		NaxpError error = FaultOf("[\\CA]");

		Assert.Equal(NaxpMessage.NAXP1052_FoldInCharacterSet, error.Message);
		Assert.Contains("before the set", error.Text, StringComparison.Ordinal);
	}

	/// <summary>
	/// A fold is a <c>!</c> once expanded, so W2 refuses one inside either operand of a <c>!</c>.
	/// Writing the fold outside is the naxp that was meant, and is what <c>\CA!A</c> parses as.
	/// </summary>
	[Fact]
	public void FoldInsideAUnifiedOperand_BreaksW2()
	{
		Assert.Equal(NaxpMessage.NAXP1039_UnifiedNested, FaultOf("(\\CA)!A").Message);
		Assert.Equal(NaxpMessage.NAXP1039_UnifiedNested, FaultOf("A!(\\CA)").Message);
	}
	#endregion
	#region Helpers
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

	/// <summary>The canonical characters a folded set prints, in the order the branches hold them.</summary>
	static string RenderingsOf(string pattern)
	{
		var renderings = new List<char>();

		Collect(Parse(pattern));

		return new string(renderings.ToArray());

		void Collect(Ast node)
		{
			switch (node)
			{
				case AstUnified unified when unified.Form == UnifiedForm.Fold:
					renderings.Add(Assert.IsType<AstChars>(unified.Rendering).CharSet.SingleCharacter!.Value);
					break;

				case AstAlternation alternation:
					foreach (Ast child in alternation.Children) { Collect(child); }

					break;
			}
		}
	}

	static AsciiCharSet SetOf(string characters)
	{
		AsciiCharSet set = AsciiCharSet.Empty;

		foreach (char c in characters) { set |= AsciiCharSet.FromSingleChar(c); }

		return set;
	}
	#endregion
}
