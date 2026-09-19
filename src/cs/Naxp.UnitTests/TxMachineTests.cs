// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Text;
using LogMu;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The canonicalisation machine, checked against <see cref="Canonicaliser"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Canonicaliser"/> is the reference here rather than a second opinion. It walks the
/// tree, which is what the specification describes, and it is already checked against the
/// conformance data through <see cref="Codec"/>. The machine exists because a tree walk has no
/// table an emitter could write out, so the thing to prove is that the two agree everywhere.
/// </para>
/// <para>
/// The naxps below are enumerated in full, not sampled, so a disagreement anywhere in the
/// accepted language fails the test. That is affordable only because each one is small; the
/// large ones are covered by the conformance data instead.
/// </para>
/// </remarks>
public class TxMachineTests
{
	#region Agreement with the tree walk
	/// <summary>
	/// Naxps holding a unified element, over which the machine must agree with the tree walk
	/// on every string of the accepted language.
	/// </summary>
	[Theory]
	// The conformance data's own unified cases.
	[InlineData("(A|a)!A")]
	[InlineData(@"\A!?")]
	[InlineData(@"\s!!X")]
	[InlineData(@"[\s\-]?!\-")]
	[InlineData(@"[\s\-]!?")]
	// The three counterexamples from encoding/canonicity.md that killed the single machine
	// design, where collapsing to the canonical character reorders the language.
	[InlineData("(A|b)!bX|BY")]
	[InlineData("(A|b)!AX|BY")]
	[InlineData("(a|A)!AX|AY")]
	// A unified element under each of the other operators.
	[InlineData("((A|a)!A){3}")]
	[InlineData("((A|a)!A|B){2}")]
	[InlineData("(AB|ab)!(AB)(C|c)!C")]
	[InlineData(@"X(\s|\-)!\-Y")]
	[InlineData("(A|a)!A(B|b)!B(C|c)!C")]
	[InlineData("((A|a)!A)?B")]
	[InlineData("(A|a)!A|(B|b)!B")]
	[InlineData("(ABC|abc|AbC)!(ABC)")]
	[InlineData(@"\9{2}(A|a)!A\9{2}")]
	[InlineData("((AA|aa)!(AA)){2}")]
	[InlineData("(A|a)!A(B|b)!B|(A|a)!A(C|c)!C")]
	[InlineData(@"(\A|\s)!Q")]
	// Renderings that are not the length of what was consumed. These are the cases the delay
	// exists for: the machine owes output it cannot emit until the input has told the branches
	// apart, and where the rendering is longer than the input it emits at end of text.
	[InlineData("(A|BB|CCC)!(BB)")]
	[InlineData("(A|BB|CCC)!(CCC)")]
	[InlineData("(A|BB|CCC)!A")]
	[InlineData("(()|A)!(A)")]
	[InlineData("(()|A)!()")]
	[InlineData("(()|AAAA)!(AAAA)")]
	[InlineData("((()|A)!(A)){3}")]
	[InlineData("(()|A)!(A)(()|B)!(B)")]
	[InlineData("X(()|AAA)!(AAA)X")]
	public void Canonicalise_OfEveryAcceptedString_AgreesWithTheTreeWalk(string pattern)
	{
		(Compilation compilation, TxMachine machine) = Build(pattern);

		int checkedStrings = 0;

		foreach (string input in Enumerate(compilation.Accepted))
		{
			bool byTree = Canonicaliser.TryCanonicalise(compilation.Ast, input.AsSpan(), out string? viaTree);
			bool byMachine = machine.TryCanonicalise(input.AsSpan(), out string? viaMachine);

			Assert.True(byTree, $"The tree walk invalid '{input}', which the accepted language holds.");
			Assert.True(byMachine, $"The machine invalid '{input}', which the accepted language holds.");
			Assert.Equal(viaTree, viaMachine);

			// A canonical form that the canonical language does not hold could not be encoded.
			Assert.True(
				compilation.Canonical.Accepts(viaMachine.AsSpan()),
				$"'{viaMachine}', the canonical form of '{input}', is not in the canonical language.");

			++checkedStrings;
		}

		Assert.True(checkedStrings > 0, "The naxp generated nothing, so nothing was compared.");
	}

	/// <summary>
	/// Invalid text has no canonical form, so the machine fails rather
	/// than producing one.
	/// </summary>
	[Theory]
	[InlineData("(A|a)!A", "B")]
	[InlineData("(A|a)!A", "")]
	[InlineData("(A|a)!A", "AA")]
	[InlineData("(A|BB|CCC)!(BB)", "CC")]
	[InlineData("(()|AAAA)!(AAAA)", "AA")]
	[InlineData(@"X(\s|\-)!\-Y", "XY")]
	public void Canonicalise_OfInvalidText_Fails(string pattern, string input)
	{
		(Compilation compilation, TxMachine machine) = Build(pattern);

		Assert.False(compilation.Accepted.Accepts(input.AsSpan()), "The test case is not invalid text.");
		Assert.False(machine.TryCanonicalise(input.AsSpan(), out string? canonical));
		Assert.Null(canonical);
	}
	#endregion

	#region Against the conformance data
	/// <summary>
	/// Every value of every conformance case that holds a unified element, checked against
	/// the canonical form the specification states rather than against another implementation.
	/// </summary>
	[Fact]
	public void Canonicalise_OfTheConformanceValues_MatchesTheStatedCanonicalForm()
	{
		ConformanceTestData data = ConformanceTestData.Load();

		int casesChecked = 0;
		int valuesChecked = 0;

		foreach (ConformanceCase testCase in data.Cases)
		{
			if (!Compiler.TryCompile(testCase.Naxp.AsSpan(), out Compilation? compilation, out _)) { continue; }

			// Without a unified element the canonicalisation is the identity and needs no
			// machine, which is what Compilation.CanonicalIsIdentity already reports.
			if (compilation!.CanonicalIsIdentity) { continue; }

			TxMachine machine = BuildMachine(compilation);
			++casesChecked;

			foreach (ConformanceValue value in testCase.Values)
			{
				Assert.True(
					machine.TryCanonicalise(value.In.AsSpan(), out string? canonical),
					$"'{testCase.Naxp}' invalid '{value.In}'.");

				Assert.Equal(value.Canon, canonical);
				++valuesChecked;
			}

			foreach (string invalid in testCase.Invalid)
			{
				Assert.False(
					machine.TryCanonicalise(invalid.AsSpan(), out _),
					$"'{testCase.Naxp}' accepted '{invalid}'.");
			}
		}

		// The data carried nine unified cases when this was written. The guard is against the
		// loop silently checking nothing, not against the count changing.
		Assert.True(casesChecked >= 9, $"Only {casesChecked} unified cases were checked.");
		Assert.True(valuesChecked >= 60, $"Only {valuesChecked} values were checked.");
	}
	#endregion

	#region Shape of the machine
	/// <summary>
	/// The transitions of a state are disjoint, which is what lets a walk stop at the first set
	/// that holds the character.
	/// </summary>
	[Theory]
	[InlineData("(A|a)!A")]
	[InlineData(@"\A\A?\9\X? \s!! \9\A\A")]
	[InlineData("(A|BB|CCC)!(BB)")]
	public void Transitions_OfAState_AreDisjoint(string pattern)
	{
		(_, TxMachine machine) = Build(pattern);

		foreach (TxState state in machine.States)
		{
			for (int i = 0; i < state.Transitions.Length; ++i)
			{
				for (int j = i + 1; j < state.Transitions.Length; ++j)
				{
					Assert.False(
						state.Transitions[i].Set.IntersectsWith(state.Transitions[j].Set),
						$"State {state.Id} has two transitions that share a character.");
				}
			}
		}
	}

	/// <summary>
	/// Building the same naxp twice gives the same machine, since nothing in the construction
	/// depends on anything but the expression.
	/// </summary>
	[Fact]
	public void Build_OfTheSameNaxpTwice_GivesTheSameShape()
	{
		const string Pattern = @"\A\A?\9\X? \s!! \9\A\A";

		(_, TxMachine first) = Build(Pattern);
		(_, TxMachine second) = Build(Pattern);

		Assert.Equal(first.States.Count, second.States.Count);

		for (int i = 0; i < first.States.Count; ++i)
		{
			TxState left = first.States[i];
			TxState right = second.States[i];

			Assert.Equal(left.EndOutput, right.EndOutput);
			Assert.Equal(left.Transitions.Length, right.Transitions.Length);

			for (int t = 0; t < left.Transitions.Length; ++t)
			{
				Assert.Equal(left.Transitions[t].Set, right.Transitions[t].Set);
				Assert.Equal(left.Transitions[t].Output, right.Transitions[t].Output);
				Assert.Equal(left.Transitions[t].Next.Id, right.Transitions[t].Next.Id);
			}
		}
	}

	/// <summary>
	/// The UK postcode naxp from the landing page, which is the largest unified example the
	/// project actually uses.
	/// </summary>
	[Theory]
	[InlineData("SW1A 1AA", "SW1A 1AA")]
	[InlineData("SW1A1AA", "SW1A 1AA")]
	[InlineData("M1 1AE", "M1 1AE")]
	[InlineData("M11AE", "M1 1AE")]
	[InlineData("CR2 6XH", "CR2 6XH")]
	[InlineData("DN55 1PT", "DN55 1PT")]
	public void Canonicalise_OfAPostcode_InsertsTheSeparator(string input, string expected)
	{
		(_, TxMachine machine) = Build(@"\A\A?\9\X? \s!! \9\A\A");

		Assert.True(machine.TryCanonicalise(input.AsSpan(), out string? canonical), $"'{input}' was invalid.");
		Assert.Equal(expected, canonical);
	}
	#endregion

	#region Size, and the naxps that have no machine
	/// <summary>
	/// The state count is exponential in the naxp's length, which is intrinsic rather than a
	/// weakness of this construction.
	/// </summary>
	/// <remarks>
	/// Nothing before the final character says which branch was taken, so the machine has to
	/// remember every character it has read in order to emit them later. The lower bound in
	/// <c>encoding/w3-functionality.md</c> holds for any finite-state machine emitting rho as it
	/// reads, so no cleverer determinisation escapes this. Both language machines stay small.
	/// </remarks>
	[Theory]
	[InlineData(2, 4, 3)]
	[InlineData(3, 5, 4)]
	[InlineData(4, 6, 5)]
	[InlineData(5, 7, 6)]
	[InlineData(6, 8, 7)]
	[InlineData(7, 9, 8)]
	[InlineData(8, 10, 9)]
	[InlineData(17, 19, 18)]
	public void Build_OfTheFormerlyExponentialFamily_IsLinear(int k, int states, int depth)
	{
		(Compilation _, TxMachine machine) = Build($"[ab]{{{k}}}c|([ab]!a){{{k}}}d");

		Assert.Equal(states, machine.States.Count);
		Assert.Equal(depth, machine.RegisterDepth);
	}

	/// <summary>
	/// The register changes the machine and not the language, which is the whole of its claim.
	/// </summary>
	[Fact]
	public void Build_OfTheFormerlyExponentialFamily_CanonicalisesAsBefore()
	{
		(Compilation _, TxMachine machine) = Build("[ab]{3}c|([ab]!a){3}d");

		Assert.True(machine.TryCanonicalise("abac".AsSpan(), out string? kept));
		Assert.Equal("abac", kept);

		Assert.True(machine.TryCanonicalise("abad".AsSpan(), out string? rendered));
		Assert.Equal("aaad", rendered);
	}

	/// <summary>
	/// A naxp can pass every rule, compile, and still have no machine. This is the one fault
	/// the builder makes that <see cref="W3Checker"/> does not already make.
	/// </summary>
	[Fact]
	public void Build_OfALegalNaxpBeyondTheStateCap_Fails()
	{
		// A lowered budget, so the path is exercised without building a machine of any size. The
		// family is linear now, so the budget has to be lowered further than it once did: eight
		// states and a register of seven are wanted, and four are allowed.
		Assert.True(Compiler.TryCompile("[ab]{6}c|([ab]!a){6}d".AsSpan(), out Compilation? compilation, out NaxpError? compileError));
		Assert.Null(compileError);

		var rxFactory = new RxFactory();
		var txFactory = new TxFactory(rxFactory);
		Tx root = TxConverter.Convert(compilation!.Ast, txFactory, rxFactory);

		Assert.False(TxMachineBuilder.TryBuild(root, txFactory, out TxMachine? machine, out NaxpError? error, maxStates: 4));
		Assert.Null(machine);
		// The code, not the prose. The message names the budget this implementation ships with,
		// which is not the lowered one a test builds against, and asserting on wording would
		// break every time a message is reworded.
		Assert.Equal(NaxpMessage.NAXP1049_TooManyCanonicalStates, error!.Value.Message);
	}

	/// <summary>
	/// The same thing at the real budget, which is what a caller would actually meet.
	/// </summary>
	/// <remarks>
	/// Kept separate and deliberately at the smallest k that exceeds the cap, because it builds a
	/// hundred thousand states before giving up and so is the slowest test in the file.
	/// </remarks>
	[Theory]
	// Linear now, so both of these fit where the second once did not. Kept as the regression:
	// the family that forced the register is the family that must stay cheap.
	[InlineData(9, true)]
	[InlineData(10, true)]
	public void Compile_OfANaxpWhoseCanonicalisationIsTooLarge_IsInvalid(int width, bool expected)
	{
		string pattern = $"[ab]{{{width}}}c|([ab]!a){{{width}}}d";

		bool compiled = Compiler.TryCompile(pattern.AsSpan(), out Compilation? compilation, out NaxpError? error);

		Assert.Equal(expected, compiled);

		if (expected)
		{
			Assert.NotNull(compilation!.CanonicalMachine);
		}
		else
		{
			Assert.Null(compilation);
			Assert.Equal("W6", NaxpMessageRules.RuleOf(error!.Value.Message));
		}
	}

	/// <summary>
	/// States that behave alike are merged, which the branch set keying alone does not do.
	/// </summary>
	[Fact]
	public void Build_OfTheMinimalityWitness_MergesStatesThatBehaveAlike()
	{
		// Eight states are built and five survive the merge.
		(_, TxMachine machine) = Build("A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)");

		Assert.Equal(5, machine.States.Count);
	}
	#endregion

	#region Defence in depth
	/// <summary>
	/// Naxps that break W3, built without running <see cref="W3Checker"/> first, so that the
	/// builder's own faults are exercised.
	/// </summary>
	/// <remarks>
	/// These paths are unreachable in the compiler, which checks before it builds. They are kept
	/// so that a machine built from an unchecked expression is invalid rather than silently
	/// wrong, and this is the only test that reaches them.
	/// </remarks>
	[Theory]
	[InlineData("AB!!B?C")]
	[InlineData("A!!A?")]
	[InlineData("A?A!!")]
	[InlineData("A!?A?")]
	[InlineData("A!!|()")]
	[InlineData("A!?|A!!")]
	[InlineData("[ab]|[ab]!a")]
	public void Build_WhenTheCheckerIsBypassed_StillFindsAW3Violation(string pattern)
	{
		Assert.True(Parser.TryParse(pattern.AsSpan(), out Ast? ast, out NaxpError? parseError), $"'{pattern}' did not parse: {parseError}");
		Assert.True(WellFormedness.TryCheck(ast!, out NaxpError? formError), $"'{pattern}' failed an earlier rule: {formError}");

		// The compiler would stop here. The builder is run directly instead.
		Assert.False(
			W3Checker.TryCheck(ast!, new RxFactory(), out _),
			$"'{pattern}' is not a W3 violation, so it does not belong in this theory.");

		var rxFactory = new RxFactory();
		var txFactory = new TxFactory(rxFactory);
		Tx root = TxConverter.Convert(ast!, txFactory, rxFactory);

		Assert.False(
			TxMachineBuilder.TryBuild(root, txFactory, out TxMachine? machine, out NaxpError? error),
			$"'{pattern}' built a machine even though it breaks W3.");

		Assert.Null(machine);
		Assert.Equal("W3", NaxpMessageRules.RuleOf(error!.Value.Message));
	}
	#endregion

	#region Helpers
	static (Compilation Compilation, TxMachine Machine) Build(string pattern)
	{
		Assert.True(
			Compiler.TryCompile(pattern.AsSpan(), out Compilation? compilation, out NaxpError? error),
			$"'{pattern}' did not compile: {error}");

		Assert.False(
			compilation!.CanonicalIsIdentity,
			$"'{pattern}' has no unified element, so it needs no machine.");

		return (compilation, BuildMachine(compilation));
	}

	static TxMachine BuildMachine(Compilation compilation)
	{
		var rxFactory = new RxFactory();
		var txFactory = new TxFactory(rxFactory);
		Tx root = TxConverter.Convert(compilation.Ast, txFactory, rxFactory);

		Assert.True(
			TxMachineBuilder.TryBuild(root, txFactory, out TxMachine? machine, out NaxpError? error),
			$"The machine for '{compilation.Pattern}' could not be built: {error}");

		return machine!;
	}

	/// <summary>Every string of a language, which is finite because a naxp has no unbounded repetition.</summary>
	static List<string> Enumerate(StateMap map)
	{
		var found = new List<string>();
		var builder = new StringBuilder();

		void Walk(State state)
		{
			if (state.AcceptsEndOfText) { found.Add(builder.ToString()); }

			foreach (Transition transition in state.Transitions)
			{
				if (transition.Set.IsEmpty) { continue; }

				foreach (char c in transition.Set)
				{
					builder.Append(c);
					Walk(transition.Next);
					builder.Length -= 1;
				}
			}
		}

		Walk(map.Start);

		return found;
	}
	#endregion
}
