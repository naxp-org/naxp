// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.Collections.Generic;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// Whether two naxps give the same encoded value to every string they both accept, whatever
/// each does to it on the way.
/// </summary>
public class ValueAgreementTests
{
	static Agreement Values(string a, string b)
		=> ValueAgreement.Compare(Compiled(a), Compiled(b), out _);

	static Compilation Compiled(string pattern)
	{
		Assert.True(Compiler.TryCompile(pattern, out Compilation? compilation, out NaxpError? error), $"{pattern}: {error?.Message}");

		return compilation!;
	}

	/// <summary>
	/// Every string the two share, one at a time through the accepted machines and the public
	/// surface. The slow and obvious way, so that the walk has something independent to answer
	/// to. Unlike the enumeration in <see cref="RankAgreementTests"/> this starts from the
	/// accepted language rather than the canonical one, because the strings a unified element
	/// rewrites are exactly the ones in question.
	/// </summary>
	static Agreement ValuesByEnumeration(string a, string b)
	{
		Compilation left = Compiled(a);
		Compilation right = Compiled(b);

		Assert.True(left.AcceptedCount <= 200_000UL && right.AcceptedCount <= 200_000UL, "Too large to enumerate.");

		foreach (Compilation side in new[] { left, right })
		{
			foreach (string text in Strings(side.Accepted))
			{
				if (!left.Accepts(text) || !right.Accepts(text)) { continue; }

				if (left.Encode(text) != right.Encode(text)) { return Agreement.Differs; }
			}
		}

		return Agreement.Agrees;
	}

	/// <summary>Every string of a machine's language.</summary>
	static IEnumerable<string> Strings(StateMap map)
	{
		var pending = new Stack<(State state, string prefix)>();
		pending.Push((map.Start, string.Empty));

		while (pending.Count > 0)
		{
			(State state, string prefix) = pending.Pop();

			if (state.IsTerminal) { yield return prefix; continue; }

			foreach (Transition transition in state.Transitions)
			{
				if (transition.Set.IsEmpty) { yield return prefix; continue; }

				foreach (char c in transition.Set) { pending.Push((transition.Next, prefix + c)); }
			}
		}
	}

	/// <summary>A witness has to be a string both accept and value differently, or it is no witness.</summary>
	static void AssertWitness(string a, string b, string? witness)
	{
		Assert.NotNull(witness);

		Naxp left = Naxp.Parse(a);
		Naxp right = Naxp.Parse(b);

		Assert.True(left.Accepts(witness!), $"'{witness}' is not accepted by a.");
		Assert.True(right.Accepts(witness!), $"'{witness}' is not accepted by b.");
		Assert.NotEqual(left.Encode(witness!), right.Encode(witness!));
	}

	#region Canonical forms are not values
	/// <summary>
	/// Two naxps can canonicalise a string differently and still value it alike, because each
	/// rank is taken in its own canonical language. These pairs disagree about what they print
	/// and agree about every value, which is the distinction the specification draws under
	/// "Values". The string given is one they print differently, so that the case is not
	/// vacuous. Any implementation that decides values by comparing canonical forms fails here.
	/// </summary>
	[Theory]
	[InlineData("(A|B)!A", "(A|B)!B", "A")]
	[InlineData("(A|B)!A|C", "(A|B)!B|C", "A")]
	[InlineData("[\\s\\-]?!\\-", "[\\s\\-]!?", "")]
	[InlineData("\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)", "A0A")]
	public void DifferentCanonicalForms_CanStillAgree(string a, string b, string printedDifferently)
	{
		Assert.NotEqual(Naxp.Parse(a).GetCanonicalForm(printedDifferently), Naxp.Parse(b).GetCanonicalForm(printedDifferently));

		Assert.Equal(Agreement.Agrees, Values(a, b));
		Assert.Equal(Agreement.Agrees, ValuesByEnumeration(a, b));
	}

	/// <summary>
	/// The other direction. Where the shared canonical strings keep their ranks but a unified
	/// element sends other strings somewhere else, rank agreement on the canonical languages
	/// says nothing, and only the values decide. Here the one shared canonical string is
	/// <c>aa</c> at value 1 under both, and <c>ab</c> is 2 under one and 1 under the other.
	/// </summary>
	[Theory]
	[InlineData("[ab]{2}", "([ab]{2})!(aa)")]
	[InlineData("[ab]{17}", "([ab]{17})!(a{17})")]
	public void RankAgreementOnCanonicalStrings_IsNotEnough(string a, string b)
	{
		Assert.Equal(Agreement.Agrees, RankAgreement.Compare(Compiled(a).Canonical, Compiled(b).Canonical));

		Assert.Equal(Agreement.Differs, ValueAgreement.Compare(Compiled(a), Compiled(b), out string? witness));
		AssertWitness(a, b, witness);
	}
	#endregion
	#region Agreement
	/// <summary>
	/// The safe edits: a space made optional, a case fold added, an alternative added after
	/// everything else. Each accepts more and values what it shared exactly as before.
	/// </summary>
	[Theory]
	[InlineData("\\A\\9\\s\\A", "\\A\\9\\s!!\\A")]
	[InlineData("\\A\\9\\A", "\\C(\\A\\9\\A)")]
	[InlineData("A!?|B", "\\C(A!?|B)")]
	[InlineData("[ab]{16}c|([ab]!a){16}d", "[ab]{16}c|([ab]!a){16}d|e")]
	[InlineData("\\A\\A?\\9\\X? \\s \\9\\A\\A", "\\A\\A?\\9\\X? \\s!! \\9\\A\\A")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "\\C(\\A\\A?\\9\\X? \\s!! \\9\\A\\A)")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "[A-Y]\\A?\\9\\X? \\s!! \\9\\A\\A")]
	public void SafeEdits_Agree(string a, string b)
		=> Assert.Equal(Agreement.Agrees, Values(a, b));

	/// <summary>
	/// A naxp against itself reaches the same product state by two inputs with different running
	/// totals many times over, through cross pairs of parses such as <c>\X?</c> taken against
	/// <c>\X?</c> skipped. None of those pairs shares a completion, so none is a witness, and an
	/// implementation that takes a differing arrival as a witness without asking fails on the
	/// naxp the site leads with.
	/// </summary>
	[Fact]
	public void ThePostcodeAgainstItself_Agrees()
	{
		const string Postcode = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";

		Assert.Equal(Agreement.Agrees, Values(Postcode, Postcode));
	}
	#endregion
	#region Disagreement
	/// <summary>
	/// The breaking edits, each with a witness that both accept and value differently. Adding
	/// <c>GIR 0AA</c> and removing a middle letter move the codes around them; dropping the
	/// space rather than reproducing it, and marking the padding of a decimal range, change
	/// what the shared strings canonicalise to.
	/// </summary>
	[Theory]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "[A-PR-Z]\\A?\\9\\X? \\s!! \\9\\A\\A")]
	[InlineData("\\A\\A?\\9\\X? \\s!! \\9\\A\\A", "\\A\\A?\\9\\X? \\s!? \\9\\A\\A")]
	[InlineData("#[0-105]", "#[0?0!0-105]")]
	[InlineData("#[0!0-105]", "#[0?0-105]")]
	public void BreakingEdits_Differ(string a, string b)
	{
		Assert.Equal(Agreement.Differs, ValueAgreement.Compare(Compiled(a), Compiled(b), out string? witness));
		AssertWitness(a, b, witness);
	}

	/// <summary>
	/// The site's first divergent value for the <c>GIR 0AA</c> comparison, which the walk
	/// finds as the shortest witness.
	/// </summary>
	[Fact]
	public void AddingGirToThePostcode_FindsTheFirstDivergentValue()
	{
		const string Without = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A";
		const string With = "\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA";

		Assert.Equal(Agreement.Differs, ValueAgreement.Compare(Compiled(Without), Compiled(With), out string? witness));
		Assert.Equal(405_194_401UL, Naxp.Parse(Without).Encode(witness!));
		Assert.Equal(1_688_310_001UL, Naxp.Parse(With).Encode(witness!));
	}
	#endregion
	#region Against the slow way
	/// <summary>
	/// The walk and an enumeration of every shared string must reach the same verdict, over
	/// naxps with and without unified elements. Those without are the pairs
	/// <see cref="RankAgreementTests"/> checks the same way, so the two walks are held to one
	/// answer on them.
	/// </summary>
	[Theory]
	[InlineData("[AB]", "[ABC]")]
	[InlineData("[AC]", "[ABC]")]
	[InlineData("AB|AC", "AB|AC|AD")]
	[InlineData("AB|AD", "AB|AC|AD")]
	[InlineData("\\A\\9", "\\A\\X")]
	[InlineData("A|BB", "A|AB|BB")]
	[InlineData("\\A{2}", "\\A{2,3}")]
	[InlineData("#[00-99]", "#[00-99]|AA")]
	[InlineData("(A|B)!A", "(A|B)!B")]
	[InlineData("(A|B)!A|C", "(A|B)!B|C")]
	[InlineData("[\\s\\-]?!\\-", "[\\s\\-]!?")]
	[InlineData("[ab]{2}", "([ab]{2})!(aa)")]
	[InlineData("[ab]{3}", "([ab]{3})!(aab)")]
	[InlineData("\\A\\9\\s\\A", "\\A\\9\\s!!\\A")]
	[InlineData("\\A\\9\\A", "\\C(\\A\\9\\A)")]
	[InlineData("\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)")]
	[InlineData("A!?|B", "\\C(A!?|B)")]
	[InlineData("A B!! C", "A B? C")]
	[InlineData("A B!? C", "A B? C")]
	[InlineData("[ab]{8}c|([ab]!a){8}d", "[ab]{8}c|([ab]!a){8}d|e")]
	[InlineData("[ab]{8}c|([ab]!a){8}d", "[ab]{8}(c|e)|([ab]!a){8}d")]
	[InlineData("#[0-105]", "#[0?0!0-105]")]
	[InlineData("#[0!0-105]", "#[0?0-105]")]
	[InlineData("#[00!0-999]", "#[0!00-999]")]
	public void TheWalkAgreesWithEnumeration(string a, string b)
		=> Assert.Equal(ValuesByEnumeration(a, b), Values(a, b));

	/// <summary>
	/// Where neither naxp holds a unified element the two walks decide the same question by
	/// different routes, one over the canonical machines and one over parses, and must agree.
	/// </summary>
	[Theory]
	[InlineData("[AB]", "[ABC]")]
	[InlineData("[AC]", "[ABC]")]
	[InlineData("AC", "AB|AC")]
	[InlineData("\\9\\A", "\\X\\A")]
	[InlineData("\\A{2,3}", "\\A{2}")]
	public void WithoutUnifiedElements_AgreesWithRankAgreement(string a, string b)
	{
		Assert.True(Compiled(a).CanonicalIsIdentity && Compiled(b).CanonicalIsIdentity);

		Assert.Equal(RankAgreement.Compare(Compiled(a).Canonical, Compiled(b).Canonical), Values(a, b));
	}
	#endregion
	#region Direction
	/// <summary>
	/// The question is symmetric. It asks about the strings both hold, and neither side is
	/// privileged.
	/// </summary>
	[Theory]
	[InlineData("[ab]{2}", "([ab]{2})!(aa)")]
	[InlineData("(A|B)!A", "(A|B)!B")]
	[InlineData("\\A\\9\\s\\A", "\\A\\9\\s!!\\A")]
	[InlineData("#[0-105]", "#[0?0!0-105]")]
	public void SwappingTheArguments_KeepsTheVerdict(string a, string b)
		=> Assert.Equal(Values(a, b), Values(b, a));
	#endregion
	#region The budget
	/// <summary>
	/// A walk given no room says so rather than guessing, and offers no witness.
	/// </summary>
	[Fact]
	public void ABudgetTooSmall_IsUndecided()
	{
		Compilation a = Compiled("\\A\\A?\\9\\X? \\s!! \\9\\A\\A");
		Compilation b = Compiled("\\A\\A?\\9\\X? \\s!! \\9\\A\\A | GIR \\s!! 0AA");

		Assert.Equal(Agreement.Undecided, ValueAgreement.Compare(a, b, out string? witness, maxTuples: 2));
		Assert.Null(witness);
	}

	/// <summary>
	/// The pair on which a square seeded with two roots would hold a distinct delay for every
	/// distinct prefix, 2^17 of them, and exhaust any budget. Values need nothing held, so this
	/// is decided in a handful of product states.
	/// </summary>
	[Fact]
	public void AHeldRenderingAgainstACopy_IsCheap()
	{
		Compilation a = Compiled("[ab]{17}");
		Compilation b = Compiled("([ab]{17})!(a{17})");

		Assert.Equal(Agreement.Differs, ValueAgreement.Compare(a, b, out _, maxTuples: 64));
	}
	#endregion
}
