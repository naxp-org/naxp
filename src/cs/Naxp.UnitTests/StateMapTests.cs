// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The machine construction: that it matches the specification's worked example, that it
/// depends on the language rather than the spelling, and that both budgets bite.
/// </summary>
public class StateMapTests
{
	#region Specification's worked example
	/// <summary>
	/// <c>#[0-10]</c> expands to <c>[0-9] | 10</c>. The continuation after each of <c>0</c> and
	/// <c>2</c> to <c>9</c> is the empty string alone; the continuation after <c>1</c> is the
	/// empty string or <c>0</c>. So the first classes are <c>[02-9]</c> and <c>[1]</c>.
	/// </summary>
	[Fact]
	public void WorkedExample_MatchesTheSpecification()
	{
		StateMap map = Canonical("#[0-10]");

		Assert.Equal(11UL, map.ValueCount);

		State start = map.Start;
		Assert.Equal(2, start.Transitions.Length);
		Assert.False(start.AcceptsEndOfText);

		Assert.Equal(Set("023456789"), start.Transitions[0].Set);
		Assert.True(start.Transitions[0].Next.IsTerminal);

		Assert.Equal(Set("1"), start.Transitions[1].Set);

		State afterOne = start.Transitions[1].Next;
		Assert.Equal(2UL, afterOne.ValueCount);
		Assert.Equal(2, afterOne.Transitions.Length);

		Assert.True(afterOne.Transitions[0].Set.IsEmpty);
		Assert.True(afterOne.Transitions[0].Next.IsTerminal);
		Assert.Equal(Set("0"), afterOne.Transitions[1].Set);
		Assert.True(afterOne.Transitions[1].Next.IsTerminal);
	}

	/// <summary>
	/// Padding the lower bound fixes one width, so every match takes the same route and numeric
	/// order is preserved.
	/// </summary>
	[Fact]
	public void PaddedDigitsRange_HasOneWidth()
	{
		StateMap map = Canonical("#[00-10]");

		Assert.Equal(11UL, map.ValueCount);
		Assert.False(map.Start.AcceptsEndOfText);
		Assert.Equal(Set("0"), map.Start.Transitions[0].Set);
		Assert.Equal(Set("1"), map.Start.Transitions[1].Set);
	}
	#endregion
	#region Machine depends on the language, not the spelling
	/// <summary>
	/// Hash-consing on transition lists is what does this. The minterms of <c>[AB]C|[BC]C</c> at
	/// the first position are <c>[A]</c>, <c>[B]</c> and <c>[C]</c>, all with derivative
	/// <c>C</c>, and merging recombines them into the single class <c>[ABC]</c>.
	/// </summary>
	[Theory]
	[InlineData("AB|AC", "A(B|C)")]
	[InlineData("A?A?", "(AA)?|A")]
	[InlineData("[AB]C|[BC]C", "[ABC]C")]
	[InlineData("A{2,4}", "AAA?A?")]
	[InlineData("A|A", "A")]
	[InlineData("#[0-9]", "[0-9]")]
	[InlineData("A{0}", "()")]
	public void EquivalentNaxps_GiveTheSameMachine(string left, string right)
		=> Assert.Equal(Describe(Canonical(left)), Describe(Canonical(right)));

	/// <summary>
	/// A rendering is not cosmetic: it determines the canonical language, so changing it changes
	/// the machine and with it the values.
	/// </summary>
	[Fact]
	public void DifferentRenderings_GiveDifferentMachines()
	{
		Assert.NotEqual(Describe(Canonical("(A|b)!bX|BY")), Describe(Canonical("(A|b)!AX|BY")));

		// Both accept the same three strings, so only the canonical machines differ.
		Assert.Equal(Describe(Accepted("(A|b)!bX|BY")), Describe(Accepted("(A|b)!AX|BY")));
	}

	/// <summary>
	/// Equal values do not mean equal text. Both of these give every string they accept the
	/// value 1, and they print nothing and a hyphen respectively.
	/// </summary>
	[Fact]
	public void SameValuesDifferentText()
	{
		Assert.Equal(1UL, Canonical("[\\s\\-]!?").ValueCount);
		Assert.Equal(1UL, Canonical("[\\s\\-]?!\\-").ValueCount);
		Assert.NotEqual(Describe(Canonical("[\\s\\-]!?")), Describe(Canonical("[\\s\\-]?!\\-")));
	}
	#endregion
	#region Counting
	[Theory]
	[InlineData("\\9{18}", 1000000000000000000UL)]
	[InlineData("\\9{3}", 1000UL)]
	[InlineData("[0-5]\\9", 60UL)]
	[InlineData("#[0-105]", 106UL)]
	public void ValueCounts(string naxp, ulong expected)
		=> Assert.Equal(expected, Canonical(naxp).ValueCount);

	/// <summary>
	/// Nineteen digits fit inside W5's limit and twenty do not.
	/// </summary>
	[Fact]
	public void W5_RefusesTwentyDigits()
	{
		Assert.True(Compiler.TryCompile("\\9{19}", out _, out _));

		Assert.False(Compiler.TryCompile("\\9{20}", out _, out NaxpError? error));
		Assert.Equal("W5", NaxpMessageRules.RuleOf(error!.Value.Message));
	}

	/// <summary>
	/// The count must be tested after every step. The limit is the full width of the accumulator,
	/// so one multiplication can wrap from operands that were both legal.
	/// </summary>
	[Fact]
	public void W5_RefusesAProductOfTwoLegalHalves()
	{
		Assert.True(Compiler.TryCompile("\\9{19}", out _, out _));

		Assert.False(Compiler.TryCompile("\\9{19}\\9{19}", out _, out NaxpError? error));
		Assert.Equal("W5", NaxpMessageRules.RuleOf(error!.Value.Message));
	}
	/// <summary>
	/// Two alternatives, each legal on its own, whose counts sum past the limit. The sum has to
	/// be tested for the wrap itself, since the limit leaves no headroom above it to compare
	/// against.
	/// </summary>
	[Fact]
	public void W5_RefusesASumOfTwoLegalAlternatives()
	{
		Assert.True(Compiler.TryCompile("\\9{19}", out _, out _));
		Assert.True(Compiler.TryCompile("[A-J]\\9{17}[A-J]", out _, out _));

		Assert.False(Compiler.TryCompile("\\9{19}|[A-J]\\9{17}[A-J]", out _, out NaxpError? error));
		Assert.Equal("W5", NaxpMessageRules.RuleOf(error!.Value.Message));
	}
	#endregion
	#region State budget
	[Fact]
	public void StateBudget_RefusesAMachineTooLargeToBuild()
	{
		var factory = new RxFactory();
		// (A{50}){10} is 500 copies, the same machine A{500} used to give before version 0.5
		// cut interval counts to two digits.
		Rx expression = RxConverter.Convert(ParseOnly("(A{50}){10}"), factory, NaxpLanguage.Canonical);

		Assert.False(StateMapBuilder.TryBuild(expression, factory, out _, out NaxpError? error, maxStates: 100));
		Assert.Equal("ImplementationLimit", NaxpMessageRules.RuleOf(error!.Value.Message));

		Assert.True(StateMapBuilder.TryBuild(expression, factory, out StateMap? map, out _, maxStates: 1000));
		Assert.Equal(1UL, map!.ValueCount);
	}
	#endregion
	#region Minterms
	[Fact]
	public void Minterms_SplitOverlappingSets()
	{
		List<AsciiCharSet> blocks = StateMapBuilder.Minterms(new[] { Set("AB"), Set("BC") });

		Assert.Equal(3, blocks.Count);
		Assert.Contains(Set("A"), blocks);
		Assert.Contains(Set("B"), blocks);
		Assert.Contains(Set("C"), blocks);
	}

	[Fact]
	public void Minterms_OfOneSet_AreThatSet()
		=> Assert.Equal(new[] { Set("ABC") }, StateMapBuilder.Minterms(new[] { Set("ABC") }));

	/// <summary>
	/// A list satisfies both parameters, so the overload that writes into a caller's list has to
	/// say no rather than clear the sets it was about to read and hand back nothing.
	/// </summary>
	[Fact]
	public void Minterms_WhenBlocksAliasSets_Throws()
	{
		List<AsciiCharSet> sets = [Set("AB"), Set("BC")];

		Assert.Throws<ArgumentException>(() => StateMapBuilder.Minterms(sets, sets));
	}
	#endregion
	#region Helpers
	static StateMap Canonical(string naxp) => Compile(naxp).Canonical;

	static StateMap Accepted(string naxp) => Compile(naxp).Accepted;

	static Compilation Compile(string naxp)
	{
		Assert.True(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error), $"{naxp} was refused: {error}");

		return compilation!;
	}

	static Ast ParseOnly(string naxp)
	{
		Assert.True(Parser.TryParse(naxp, out Ast? ast, out NaxpError? error), $"{naxp} was refused: {error}");

		return ast!;
	}

	static AsciiCharSet Set(string characters)
	{
		AsciiCharSet set = AsciiCharSet.Empty;
		foreach (char c in characters) { set |= AsciiCharSet.FromSingleChar(c); }

		return set;
	}

	/// <summary>
	/// A machine written out with its states numbered in the order a walk from the start reaches
	/// them. Two machines describe alike exactly when they have the same shape.
	/// </summary>
	static string Describe(StateMap map)
	{
		var numbers = new Dictionary<State, int>();
		var order = new List<State>();

		Number(map.Start, numbers, order);

		var builder = new StringBuilder();

		foreach (State state in order)
		{
			builder.Append(numbers[state].ToString(CultureInfo.InvariantCulture));
			builder.Append(" count=").Append(state.ValueCount.ToString(CultureInfo.InvariantCulture));

			foreach (Transition transition in state.Transitions)
			{
				builder.Append(" <");
				foreach (char c in transition.Set) { builder.Append(c); }
				builder.Append(">->").Append(numbers[transition.Next].ToString(CultureInfo.InvariantCulture));
			}

			builder.AppendLine();
		}

		return builder.ToString();
	}

	static void Number(State state, Dictionary<State, int> numbers, List<State> order)
	{
		if (numbers.ContainsKey(state)) { return; }

		numbers.Add(state, order.Count);
		order.Add(state);

		foreach (Transition transition in state.Transitions) { Number(transition.Next, numbers, order); }
	}
	#endregion
}
