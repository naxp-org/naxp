// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Naxp.NXComponents;
using Xunit;

namespace Naxp.UnitTests;

public sealed class NXStateGeneratorTests
{
	[Fact]
	public void TestStateGenerator_CreateStateMap()
	{
		var testData = new[]
		{
			(text: "A"
				, validPaths: new [] { "A", }
				, invalidPaths: new[] { "", "B", "AA", }
				),
			(text: "AB"
				, validPaths: [ "AB", ]
				, invalidPaths: [ "", "B", "A", "BA", "AA", "BB", "ABA", "CCC", "000", ]
				),
			(text: "A?B"
				, validPaths: [ "AB", "B", ]
				, invalidPaths: [ "", "A", "BA", "AA", "BB", "ABA", "CCC", "000", ]
				),
			(text: "(A|B)"
				, validPaths: [ "A", "B", ]
				, invalidPaths: [ "", "AB", "BA", "AA", "BB", "C", "000", ]
				),
			(text: "(A|B)(C|D)"
				, validPaths: [ "AC", "AD", "BC", "BD", ]
				, invalidPaths: [ "", "A", "B", "C", "D", "AA", "CC", "DB", "AAA", "ACB", ]
				),
			(text: "(A1|B2)(C3|D4)"
				, validPaths: [ "A1C3", "A1D4", "B2C3", "B2D4", ]
				, invalidPaths: [ "", "A", "B", "C", "D", "AA", "CC", "DB", "A1C3A", "00000", ]
				),
			(text: @"\A?\A\9\X? \s \9\A\A"
				, validPaths: [ "A1 0AA", "A11 0AA", "A1A 0AA", "AA1 0AA", "AA11 0AA", "AA1A 0AA", ]
				, invalidPaths: [ "", "A10AA", "AA 0AA", "A10  AA", "AA1A0AA", ]
				),
		};

		foreach (var (text, validPaths, invalidPaths) in testData)
		{
			var ast = Parser.Parse(text);

			var stateMap = StateMapGenerator.CreateStateMap(ast);

			var startState = stateMap[0];

			foreach (var path in validPaths)
			{
				Assert.True(startState.Accepts(path));
			}

			foreach (var path in invalidPaths)
			{
				Assert.False(startState.Accepts(path));
			}

			Ast.Simplify(ref ast);

			var stateMapSimplified = StateMapGenerator.CreateStateMap(ast);

			Assert.Equal(stateMap.Length, stateMapSimplified.Length);
			for (int i = 0; i < stateMap.Length; ++i)
			{
				Assert.Equal(stateMap[0], stateMapSimplified[0]);
			}
		}
	}

	[Fact]
	public void TestStateGenerator_IsStandardised()
	{
		var testData = new[]
		{
			(text_0: @"\A?\A\9\X?", text_1: @"\A\9\X|\A\9|\A\A\9|\A\A\9\X"),
			(text_0: @"\A?\A\9\X? \s", text_1: @"\A\9\X \s|(\A\9|\A\A\9) \s|\A\A\9\X \s"),
			(text_0: "A1|B2|C3|D1|E2|F3", text_1: @"D1|E2|F3|C3|B2|A1"),
		};

		foreach (var (text_0, text_1) in testData)
		{
			var ast_0 = Parser.Parse(text_0);
			var stateMap_0 = StateMapGenerator.CreateStateMap(ast_0);

			var ast_1 = Parser.Parse(text_1);
			var stateMap_1 = StateMapGenerator.CreateStateMap(ast_1);

			Assert.Equal(stateMap_0.Length, stateMap_1.Length);
			for (int i = 0; i < stateMap_0.Length; ++i)
			{
				Assert.Equal(stateMap_0[0], stateMap_1[0]);
			}
		}
	}

	[Fact]
	public void TestStateGenerator_Unroll()
	{
		var testData = new[]
		{
			(text: "A", unrolled: (string[][])[["A", ], ]),
			(text: "AB", unrolled: [["A","B", ], ]),
			(text: "A?B", unrolled: [["A","B", ], ["B", ], ]),
			(text: "EF|CD|AB", unrolled: [["E","F", ], ["C","D", ], ["A","B"]]),
			(text: "(AB|CD)(EF|GH)", unrolled: [["A","B", "E","F", ], ["A","B", "G","H", ],["C","D", "E","F", ], ["C","D", "G","H", ]]),
			(text: "A?B(EF|GH)", unrolled: [["A","B", "E","F", ], ["A","B", "G","H", ],["B", "E","F", ], ["B", "G","H", ]]),
		};

		foreach (var (text, expected_unrolled) in testData)
		{
			var ast = Parser.Parse(text);

			var actual_unrolled = StateMapGenerator.Unroll(ast);

			Assert.Equal(expected_unrolled.Length, actual_unrolled.Count);

			for (int i = 0; i < expected_unrolled.Length; ++i)
			{
				var expected_items = expected_unrolled[i];
				var actual_items = actual_unrolled[i];

				Assert.Equal(expected_items.Length, actual_items.Count);

				for (int k = 0; k < expected_items.Length; ++k)
				{
					var expected_item = AsciiCharSet.Parse(expected_items[k]);
					var actual_item = actual_items[k];

					Assert.Equal(expected_item, actual_item);
				}
			}
		}
	}

	[Fact]
	public void TestStateGenerator_MergeTransitionsToSameState()
	{
		var A = AsciiCharSet.Parse("A");
		var B = AsciiCharSet.Parse("B");
		var C = AsciiCharSet.Parse("C");
		var D = AsciiCharSet.Parse("D");
		var a = AsciiCharSet.Parse("a");
		var b = AsciiCharSet.Parse("b");

		static State St(ImmutableArray<Transition> transitions) => new(transitions);

		var s_x = State.DefinitiveEndOfText;

		var s_a_a = St([a + St([a + s_x])]);
		var s_a_b = St([a + St([b + s_x])]);
		var s_b_b = St([b + St([b + s_x])]);

		var testData = new[]
		{
			(originalTransitions: Array.Empty<Transition>(), mergedTransitions: Array.Empty<Transition>()),
			(originalTransitions: [A + s_a_a, B + s_b_b], mergedTransitions: [A + s_a_a, B + s_b_b]),
			(originalTransitions: [A + s_a_a, B + s_a_a], mergedTransitions: [(A|B) + s_a_a]),
			(originalTransitions: [A + s_a_a, B + s_a_b, C + s_a_b, D + s_b_b], mergedTransitions: [A + s_a_a, (B|C) + s_a_b, D + s_b_b]),
			(originalTransitions: [A + s_a_a, B + s_a_b, C + s_a_b, D + s_a_a], mergedTransitions: [(A|D) + s_a_a, (B|C) + s_a_b]),
		};

		foreach (var (originalTransitions, mergedTransitions) in testData)
		{
			var actualTransitions = (Transition[])originalTransitions.Clone();

			StateMapGenerator.MergeTransitionsToSameState(ref actualTransitions);

			Assert.Equal(mergedTransitions.Length, actualTransitions.Length);

			for (int i = 0; i < mergedTransitions.Length; ++i)
			{
				var expected = mergedTransitions[i];
				var actual = actualTransitions[i];
				Assert.Equal(expected.CharSet, actual.CharSet);
				Assert.Equal(expected.NextState, actual.NextState);
			}
		}
	}

	[Fact]
	public void TestStateGenerator_UpdateDisjointCharSets()
	{
		var Empty = new AsciiCharSet();

		var A = AsciiCharSet.Parse("A");
		var B = AsciiCharSet.Parse("B");
		var C = AsciiCharSet.Parse("C");
		var D = AsciiCharSet.Parse("D");
		var E = AsciiCharSet.Parse("E");
		var F = AsciiCharSet.Parse("F");

		var testData = new[]
		{
			(disjointCharSets: new List<AsciiCharSet>(), newCharSet: Empty, disjointCharSetsPost: Array.Empty<AsciiCharSet>()),
			(disjointCharSets: [A, B, C], newCharSet: Empty, disjointCharSetsPost: [A, B, C]),
			(disjointCharSets: [A, B, C], newCharSet: B, disjointCharSetsPost: [A, B, C]),
			(disjointCharSets: [A, B, C], newCharSet: D, disjointCharSetsPost: [A, B, C, D]),
			(disjointCharSets: [A|B, C], newCharSet: B|D, disjointCharSetsPost: [B, C, A, D]),
			(disjointCharSets: [A|B, C], newCharSet: C|D, disjointCharSetsPost: [A|B, C, D]),
			(disjointCharSets: [A|B, C|D], newCharSet: B|D, disjointCharSetsPost: [B, D, A, C]),
		};

		foreach (var (disjointCharSets, newCharSet, expected) in testData)
		{
			StateMapGenerator.UpdateDisjointCharSets(disjointCharSets, newCharSet);
			Assert.Equal(expected.Length, disjointCharSets.Count);
			for (int i = 0; i < expected.Length; ++i)
			{
				Assert.Equal(expected[i], disjointCharSets[i]);
			}
		}
	}
}