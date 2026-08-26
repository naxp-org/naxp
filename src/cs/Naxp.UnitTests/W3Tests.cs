// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Text;
using LogMu;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// W3: replacement must be single valued.
/// </summary>
/// <remarks>
/// The cases come from <c>encoding/w3-functionality.md</c>, which reviewed the procedure before
/// it was written and supplied the naxps that break the obvious wrong versions of it. Several
/// look interchangeable and are not: <c>A!!A?</c> breaks W3 and <c>A!!A</c> does not.
/// </remarks>
public class W3Tests
{
	#region Violations
	/// <summary>
	/// Naxps whose replacement is not single valued. Each is refused when it is compiled.
	/// </summary>
	[Theory]
	// The case the conformance data already carried.
	[InlineData("AB!!B?C")]
	// Five characters, and smaller than the above. The first two are caught only by comparing
	// what a branch emits at end of text, not by comparing what it has emitted so far.
	[InlineData("A!!A?")]
	[InlineData("A?A!!")]
	[InlineData("A!?A?")]
	// Witnessed by the empty string alone, so no character is ever read.
	[InlineData("A!!|()")]
	// The same point with both canonical forms produced at end of text from one residual.
	[InlineData("A!?|A!!")]
	// Emissions are not uniform over a first-set minterm: 'a' agrees and 'b' does not.
	[InlineData("[ab]|[ab]!a")]
	// Skipping a nullable copy of an interval emits, so how many are skipped is a choice.
	[InlineData("(A!!){0,3}")]
	public void Violations_AreRefused(string naxp)
	{
		Assert.False(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error));

		Assert.Null(compilation);
		Assert.Equal("W3", NaxpMessageRules.RuleOf(error!.Value.Message));
	}

	/// <summary>
	/// The witness named in the message really does have two canonical forms.
	/// </summary>
	[Theory]
	[InlineData("AB!!B?C")]
	[InlineData("A!!A?")]
	[InlineData("A?A!!")]
	[InlineData("A!?A?")]
	[InlineData("[ab]|[ab]!a")]
	public void Violations_NameAWitnessThatIsGenuinelyAmbiguous(string naxp)
	{
		Assert.False(Compiler.TryCompile(naxp, out _, out NaxpError? error));

		string witness = WitnessOf(error!.Value.Text);

		Assert.True(Parser.TryParse(naxp, out Ast? ast, out _));
		Assert.Equal(ReferenceOutcome.Ambiguous, ReferenceCanonicaliser.TryCanonicalise(ast!, witness, out _));
	}

	/// <summary>
	/// The empty string is a witness like any other, and the message says so.
	/// </summary>
	[Fact]
	public void Violation_OnTheEmptyString_IsFoundBeforeAnyCharacterIsRead()
	{
		Assert.False(Compiler.TryCompile("A!!|()", out _, out NaxpError? error));

		Assert.Equal("W3", NaxpMessageRules.RuleOf(error!.Value.Message));
		Assert.Equal(string.Empty, WitnessOf(error.Value.Text));
	}
	#endregion
	#region Well formed
	/// <summary>
	/// Naxps that pass, including the near misses that a checker comparing the wrong thing
	/// refuses.
	/// </summary>
	[Theory]
	// Both alternatives map B and BA to BA, so two branches with pendings that differ still agree
	// once what they emit at end of text is counted. A checker comparing pendings refuses this.
	[InlineData("(B|BA)!(BA)|BA!!")]
	// The same shape with a tail, so the disagreement survives past a consumed character.
	[InlineData("(B|BA)!(BA)X|BA!!X")]
	// Four-character near misses bracketing the five-character violations above.
	[InlineData("A!!B")]
	[InlineData("BA!!")]
	[InlineData("A!?A")]
	[InlineData("A!!A")]
	// A '?' three tokens away is the whole difference between this and AB!!B?C.
	[InlineData("AB!!BC")]
	// No '!' at all, so the transduction is the identity.
	[InlineData("\\A\\A?\\9\\X?\\s\\9\\A\\A")]
	// The postcode: the space appears in neither \X nor \9, so nothing can be confused.
	[InlineData("\\A\\A?\\9\\X?\\s!!\\9\\A\\A")]
	public void WellFormed_AreAccepted(string naxp)
	{
		Assert.True(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error), $"{naxp}: {error}");

		Assert.NotNull(compilation);
	}
	#endregion
	#region Cost
	/// <summary>
	/// The ill-formed blow-up family is diagnosed rather than abandoned. A subset construction
	/// over sets of branches passes 100 000 configurations here before it can even reach its
	/// first acceptance check; the square settles it in a few dozen pair states.
	/// </summary>
	[Fact]
	public void IllFormedBlowUpFamily_IsDiagnosedWithinASmallBudget()
	{
		Assert.True(Parser.TryParse("([ab]|[ab]!a){17}", out Ast? ast, out _));
		Assert.True(WellFormedness.TryCheck(ast!, out _));

		Assert.False(W3Checker.TryCheck(ast!, new RxFactory(), out NaxpError? error, maxStates: 2000));
		Assert.Equal("W3", NaxpMessageRules.RuleOf(error!.Value.Message));
	}

	/// <summary>
	/// The well-formed blow-up family is accepted rather than refused. This is the case that
	/// killed the subset construction: both its machines have fewer than forty states, yet a
	/// determinisation needs 2^17, so a legal naxp would have been rejected.
	/// </summary>
	[Fact]
	public void WellFormedBlowUpFamily_IsAcceptedWithinASmallBudget()
	{
		Assert.True(Parser.TryParse("[ab]{17}c|([ab]!a){17}d", out Ast? ast, out _));
		Assert.True(WellFormedness.TryCheck(ast!, out _));

		Assert.True(W3Checker.TryCheck(ast!, new RxFactory(), out NaxpError? error, maxStates: 2000), $"{error}");
	}

	/// <summary>
	/// A naxp beyond the budget is refused as an implementation limit rather than judged either
	/// way, which is the same answer the machine builder gives.
	/// </summary>
	[Fact]
	public void BeyondTheBudget_IsAnImplementationLimit()
	{
		Assert.True(Parser.TryParse("[ab]{17}c|([ab]!a){17}d", out Ast? ast, out _));

		Assert.False(W3Checker.TryCheck(ast!, new RxFactory(), out NaxpError? error, maxStates: 8));
		Assert.Equal("ImplementationLimit", NaxpMessageRules.RuleOf(error!.Value.Message));
	}
	#endregion
	#region Differential test against the per-string canonicaliser
	/// <summary>
	/// The static rule and the per-string one must agree everywhere.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="ReferenceCanonicaliser"/> decides ambiguity for one string by walking the tree
	/// and carrying every output, which shares no reasoning with the square. A naxp's language is
	/// finite and can be enumerated from its accepted machine, so the two can be compared
	/// exhaustively: the square must refuse a naxp exactly when some string of its language has
	/// more than one canonical form.
	/// </para>
	/// <para>
	/// The naxps are generated rather than listed, because the interesting cases are the ones
	/// nobody thinks to write down.
	/// </para>
	/// </remarks>
	[Fact]
	public void StaticRule_AgreesWithThePerStringRule_OverGeneratedNaxps()
	{
		var failures = new List<string>();
		int compared = 0;

		foreach (string naxp in GeneratedNaxps())
		{
			if (!Parser.TryParse(naxp, out Ast? ast, out _)) { continue; }
			if (!WellFormedness.TryCheck(ast!, out _)) { continue; }

			if (!TryEnumerate(ast!, out List<string>? language)) { continue; }

			string? ambiguous = null;
			foreach (string text in language!)
			{
				if (ReferenceCanonicaliser.TryCanonicalise(ast!, text, out _) == ReferenceOutcome.Ambiguous)
				{
					ambiguous = text;
					break;
				}
			}

			bool passes = W3Checker.TryCheck(ast!, new RxFactory(), out NaxpError? error);

			if (error is { } limit && NaxpMessageRules.IsImplementationLimit(limit.Message)) { continue; }

			++compared;

			if (passes && ambiguous is not null)
			{
				failures.Add($"{naxp} was accepted, but '{ambiguous}' has more than one canonical form.");
			}
			else if (!passes && ambiguous is null)
			{
				failures.Add($"{naxp} was refused ({error}), but no string of its language is ambiguous.");
			}
		}

		Assert.True(compared > 2000, $"Only {compared} naxps were compared, which is too few to mean anything.");
		Assert.True(
			failures.Count == 0,
			$"{failures.Count} disagreements:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
	}

	/// <summary>
	/// Sequences and alternations over a pool of elements chosen to put replaceable and
	/// non-replaceable ways of matching the same characters next to one another.
	/// </summary>
	static IEnumerable<string> GeneratedNaxps()
	{
		string[] units =
		{
			"A", "B", "A?", "B?", "A!!", "B!!", "A!?", "B!?", "(A|B)!A", "(A|B)!B", "(A|B)?",
		};

		foreach (string first in units)
		{
			yield return first;

			foreach (string second in units)
			{
				yield return first + second;
				yield return first + "|" + second;

				foreach (string third in units) { yield return first + second + third; }
			}
		}

		// Intervals, where skipping a copy is itself an emission and a fixed count is the
		// difference between one output and several.
		string[] counts = { "{2}", "{3}", "{0,2}", "{1,2}", "{1,3}", "{0,3}" };

		foreach (string unit in units)
		{
			foreach (string count in counts)
			{
				yield return "(" + unit + ")" + count;
				yield return "(" + unit + ")" + count + "A";
				yield return "A(" + unit + ")" + count;

				foreach (string tail in units) { yield return "(" + unit + ")" + count + tail; }
			}
		}
	}

	/// <summary>
	/// Every string the naxp accepts, or nothing where there are too many to be worth checking.
	/// </summary>
	static bool TryEnumerate(Ast ast, out List<string>? language)
	{
		language = null;

		var factory = new RxFactory();
		Rx expression = RxConverter.Convert(ast, factory, NaxpLanguage.Accepted);

		if (!StateMapBuilder.TryBuild(expression, factory, out StateMap? map, out _)) { return false; }
		if (map!.CountSaturated || map.ValueCount > 4096UL) { return false; }

		language = new List<string>((int)map.ValueCount);

		for (ulong value = 1UL; value <= map.ValueCount; ++value)
		{
			if (!Codec.TryDecode(map, value, out string? text)) { return false; }

			language.Add(text!);
		}

		return true;
	}
	#endregion
	#region Abandoned decisions
	/// <summary>
	/// Where the decision is abandoned because an intermediate result grew too large, the message
	/// must not blame the pair state budget, which is a different limit and was not the one hit.
	/// </summary>
	/// <remarks>
	/// <c>(A!!){66}</c> passes more than <c>TxFactory.MaxSkippedCopies</c> skipped copies of an
	/// interval, so the derivative gives up. The naxp is legal and the message says so.
	/// </remarks>
	[Fact]
	public void TryCheck_WhenTheDerivativeIsAbandoned_DoesNotBlameThePairStateBudget()
	{
		Assert.True(Parser.TryParse("(A!!){66}".AsSpan(), out Ast? ast, out _));
		Assert.True(WellFormedness.TryCheck(ast!, out _));

		Assert.False(W3Checker.TryCheck(ast!, new RxFactory(), out NaxpError? error));

		Assert.Equal("ImplementationLimit", NaxpMessageRules.RuleOf(error!.Value.Message));
		Assert.DoesNotContain("pair states", error.Value.Text, StringComparison.Ordinal);
		Assert.Contains("may well be legal", error.Value.Text, StringComparison.Ordinal);
	}

	/// <summary>
	/// The pair state budget keeps its own message, so the two limits stay distinguishable.
	/// </summary>
	[Fact]
	public void TryCheck_WhenThePairStateBudgetIsSpent_SaysSo()
	{
		Assert.True(Parser.TryParse("[ab]{6}c|([ab]!a){6}d".AsSpan(), out Ast? ast, out _));
		Assert.True(WellFormedness.TryCheck(ast!, out _));

		Assert.False(W3Checker.TryCheck(ast!, new RxFactory(), out NaxpError? error, maxStates: 8));

		Assert.Equal("ImplementationLimit", NaxpMessageRules.RuleOf(error!.Value.Message));
		Assert.Contains("pair states", error.Value.Text, StringComparison.Ordinal);
	}
	#endregion

	#region Helpers
	/// <summary>
	/// The witness out of a W3 message, which quotes it between the first pair of apostrophes.
	/// </summary>
	static string WitnessOf(string message)
	{
		int open = message.IndexOf('\'');
		int close = message.IndexOf('\'', open + 1);

		return message.Substring(open + 1, close - open - 1);
	}
	#endregion
}
