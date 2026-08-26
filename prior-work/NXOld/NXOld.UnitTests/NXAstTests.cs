// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using Naxp.NXComponents;
using Xunit;

namespace Naxp.UnitTests;

public class NXAstTests
{
	[Fact]
	public void TestNXAstSimplification()
	{
		var testData = new[]
		{
			(raw: "(A)B", parsed: "AB", simplified: "AB"),
			(raw: "(AB)(CD)", parsed: "ABCD", simplified: "ABCD"),
			(raw: "A|B|CD", parsed: "A|B|CD", simplified: "[AB]|CD"),
			(raw: "(A|BC)|D", parsed: "A|BC|D", simplified: "[AD]|BC"),
			(raw: "AB|(C|D)", parsed: "AB|C|D", simplified: "[CD]|AB"),
			(raw: "(A|B)|(C|D)", parsed: "A|B|C|D", simplified: "[A-D]"),
			(raw: "(AB)|AB", parsed: "AB|AB", simplified: "AB"),
			(raw: "(BC)|A|BC", parsed: "BC|A|BC", simplified: "A|BC"),
			(raw: "(A)?B", parsed: "A?B", simplified: "A?B"),
			(raw: "(A)?B|A?C", parsed: "A?B|A?C", simplified: "A?[BC]"),
			(raw: "(A)?B|A?CD", parsed: "A?B|A?CD", simplified: "A?(B|CD)"),
			(raw: "AB|(CD)?|EF", parsed: "AB|(CD)?|EF", simplified: "(AB|CD|EF)?"),
			(raw: "EF|CD|AB", parsed: "EF|CD|AB", simplified: "AB|CD|EF"),

			(raw: "XA|X", parsed: "XA|X", simplified: "XA?"),
			(raw: "XAA|X|BB|CC", parsed: "XAA|X|BB|CC", simplified: "BB|CC|X(AA)?"),
			(raw: "XAA|BB|XCC|DD", parsed: "XAA|BB|XCC|DD", simplified: "BB|DD|X(AA|CC)"),
			(raw: "XAA|BB|X|XCC|DD", parsed: "XAA|BB|X|XCC|DD", simplified: "BB|DD|X(AA|CC)?"),
			(raw: "XAA|XBB|XCC", parsed: "XAA|XBB|XCC", simplified: "X(AA|BB|CC)"),
			(raw: "XAA|X|XBB|XCC", parsed: "XAA|X|XBB|XCC", simplified: "X(AA|BB|CC)?"),

			(raw: "X|AX", parsed: "X|AX", simplified: "A?X"),
			(raw: "AA|X|BBX|CC", parsed: "AA|X|BBX|CC", simplified: "AA|CC|(BB)?X"),
			(raw: "AAX|BB|CCX|DD", parsed: "AAX|BB|CCX|DD", simplified: "BB|DD|(AA|CC)X"),
			(raw: "AAX|BB|X|CCX|DD", parsed: "AAX|BB|X|CCX|DD", simplified: "BB|DD|(AA|CC)?X"),
			(raw: "AAX|BB|CCX|DD", parsed: "AAX|BB|CCX|DD", simplified: "BB|DD|(AA|CC)X"),
			(raw: "AAX|X|BBX|CCX", parsed: "AAX|X|BBX|CCX", simplified: "(AA|BB|CC)?X"),

			(raw:  @"\A?\A\9\X? \s \9\A\A", parsed: @"\A?\A\9\X?\s\9\A\A", simplified: @"\A\A?\9\X?\s\9\A\A"),
			(raw:  @"\A\A?\9[\A0-9]? \s \9\A\A", parsed: @"\A\A?\9\X?\s\9\A\A", simplified: @"\A\A?\9\X?\s\9\A\A"),
			(raw: @"(\A\9 | \A\9\9 | \A\A\9 | \A\A\9\9 | \A\9\A | \A\A\9\A) \s \9\A\A", parsed: @"(\A\9|\A\9\9|\A\A\9|\A\A\9\9|\A\9\A|\A\A\9\A)\s\9\A\A", simplified: @"\A\A?\9\X?\s\9\A\A"),
			(raw: @"\A\9|\A\9\9|\A\A\9|\A\A\9\9|\A\9\A|\A\A\9\A", parsed: @"\A\9|\A\9\9|\A\A\9|\A\A\9\9|\A\9\A|\A\A\9\A", simplified: @"\A\A?\9\X?"),
			(raw: @"AB|ABB|AAB|AABB|ABA|AABA", parsed: @"AB|ABB|AAB|AABB|ABA|AABA", simplified: @"AA?B[AB]?"),
			(raw: @"B|BB|AB|ABB|BA|ABA", parsed: @"B|BB|AB|ABB|BA|ABA", simplified: @"A?B[AB]?"),
		};

		foreach (var (raw, parsed, simplified) in testData)
		{
			if (!Parser.TryParse(raw, out Ast? ast, out _, out _)) { throw new Exception(); }

			Assert.Equal(parsed, ast.ToString());

			Ast.Simplify(ref ast);

			Assert.Equal(simplified, ast.ToString());
		}
	}
	[Fact]
	public void TestNXAstUnrolling()
	{
		var testData = new[]
		{
			(text: "AB",  unrolled: new List<List<string>>() { new() { "A", "B" } }),
			(text: "ABCD", unrolled: [ ["A", "B", "C", "D"] ]),
			(text: "A|B|C|D", unrolled: [ ["A"], ["B"], ["C"], ["D"] ]),
			(text: "A?", unrolled: [ ["A"], [] ]),
			(text: "A?B", unrolled: [ ["A", "B"], ["B"] ]),
			(text: "(A|B|C)1", unrolled: [ ["A", "1"], ["B", "1"], ["C", "1"] ]),
			(text: "(A|B|C)(1|2)", unrolled: [ ["A", "1"], ["A", "2"], ["B", "1"], ["B", "2"], ["C", "1"], ["C", "2"], ]),
			(text: "A(1|2|3)", unrolled: [ ["A", "1"], ["A", "2"], ["A", "3"], ]),
			(text:  @"\A?\A\9\X? \s \9\A\A", unrolled: [
				[@"\A", @"\A", @"\9", @"\X", @"\s", @"\9", @"\A", @"\A"],
				[@"\A", @"\A", @"\9", @"\s", @"\9", @"\A", @"\A"],
				[@"\A", @"\9", @"\X", @"\s", @"\9", @"\A", @"\A"],
				[ @"\A", @"\9", @"\s", @"\9", @"\A", @"\A"],
				]),
		};

		foreach (var (text, unrolled) in testData)
		{
			if (!Parser.TryParse(text, out Ast? ast, out _, out _)) { throw new Exception(); }

			// We deliberately do not call Ast.Simplify(...).

			var actual = StateMapGenerator.Unroll(ast);

			Assert.Equal(unrolled.Count, actual.Count);
			for (int i = 0; i < unrolled.Count; ++i)
			{
				var unrolledChild = unrolled[i];
				var actualChild = actual[i];
				Assert.Equal(unrolledChild.Count, actualChild.Count);
				for (int k = 0; k < unrolledChild.Count; ++k)
				{
					var unrolledCharSetText = unrolledChild[k];
					var actualCharSet = actualChild[k];

					Assert.Equal(unrolledCharSetText, actualCharSet.ToString());
				}
			}
		}
	}
}