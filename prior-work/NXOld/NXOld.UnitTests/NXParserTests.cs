// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using Naxp.NXComponents;
using Xunit;

namespace Naxp.UnitTests;

public class NXParserTests
{
	[Fact]
	public void TestNXParsing()
	{
		var testData = new[]
		{
			(text: @"", x: X.Empty),
			(text: @"    ", x: X.Empty),
			(text: @"A", x: X.Chars),
			(text: @"A B C", x: X.Seq(X.Chars, X.Chars, X.Chars)),
			(text: @"[AB C]", x: X.Chars),
			(text: @"[A B- Z12 3]", x: X.Chars),
			(text: @"A | BC", x:
				X.Or(
					X.Chars,
					X.Seq(
						X.Chars,
						X.Chars
						)
					)
				),
			(text: @"A[BC]|C", x:
				X.Or(
					X.Seq(
						X.Chars,
						X.Chars
						),
					X.Chars)
				),
			(text: @"A?", x: X.Opt(X.Chars)),
			(text: @"A?B", x: X.Seq(X.Opt(X.Chars), X.Chars)),
			(text: @"A?|B", x:
				X.Or(
					X.Opt(
						X.Chars
						),
					X.Chars)
				),
			(text: @"AB?|CD", x:
				X.Or(
					X.Seq(
						X.Chars,
						X.Opt(
							X.Chars)
						),
					X.Seq(
						X.Chars,
						X.Chars)
					)
				),
			(text: @"(AB?)?|(CD|E?)", x:
				X.Or(
					X.Opt(
						X.Seq(
							X.Chars,
							X.Opt(
								X.Chars
								)
							)
						),
					X.Seq(
						X.Chars,
						X.Chars
						),
					X.Opt(
						X.Chars)
					)
				),
		};

		foreach (var (text, x) in testData)
		{
			var success = Parser.TryParse(text, out Ast? ast, out _, out _);

			Assert.True(success);

			TestAstRecursively(ast!, x);
		}
	}

	[Fact]
	public void TestNXParsing_Errors()
	{
		var testData = new[]
		{
			(invalidText: @"A1D4|A1C3)", includedInErrorMessage: ")", errorOffset: -1),
			(invalidText: @"\A\9\X?|\A\A?\9|", includedInErrorMessage: "unexpected end of text", errorOffset: 16),
			// A character range written highest first. This used to throw out of TryParse.
			(invalidText: @"[E-A]", includedInErrorMessage: "character must be before", errorOffset: 1),
			(invalidText: @"[ E - A ]", includedInErrorMessage: "character must be before", errorOffset: 2),
			(invalidText: @"[0-9E-A]", includedInErrorMessage: "character must be before", errorOffset: 4),
			(invalidText: @"[\x45-\x41]", includedInErrorMessage: "character must be before", errorOffset: 1),
		};

		foreach (var (invalidText, includedInErrorMessage, errorOffset) in testData)
		{
			var success = Parser.TryParse(invalidText, out _, out var errorMessage, out int actual_errorOffset);

			Assert.False(success);
			Assert.True(errorMessage.Contains(includedInErrorMessage, StringComparison.InvariantCultureIgnoreCase));
			Assert.Equal(errorOffset >= 0 ? errorOffset : invalidText.Length + errorOffset, actual_errorOffset);
		}
	}

	#region Supporting stuff
	enum XType
	{
		Empty,
		Chars,
		Opt,
		Or,
		Seq,
	}

	sealed class X
	{
		public XType Type;
		public readonly X[] Children;
		private X(XType type) { this.Type = type; this.Children = []; }
		private X(XType type, params X[] children) { this.Type = type; this.Children = children; }

		public static readonly X Empty = new(XType.Empty);
		public static readonly X Chars = new(XType.Chars);
		public static X Seq(params X[] children) => new(XType.Seq, children);
		public static X Or(params X[] children) => new(XType.Or, children);
		public static X Opt(X child) => new(XType.Opt, child);
	}
	static void TestAstRecursively(Ast ast, X x)
	{
		switch (x.Type)
		{
			case XType.Empty: Assert.IsType<Empty>(ast); break;
			case XType.Chars: Assert.IsType<Chars>(ast); break;
			case XType.Opt:
				Assert.IsType<Opt>(ast);
				TestAstRecursively(((Opt)ast).Child, x.Children[0]);
				break;
			case XType.Or:
			case XType.Seq:
				switch (x.Type)
				{
					case XType.Or: Assert.IsType<Or>(ast); break;
					case XType.Seq: Assert.IsType<Seq>(ast); break;
					default: throw new NotImplementedException();
				}

				var multiChild = (MultiChild)ast;
				Assert.True(multiChild.Children.Length > 0);

				var children = multiChild.Children;
				for (int i = 0; i < children.Length; ++i)
				{
					var child = children[i];
					TestAstRecursively(child, x.Children[i]);
				}
				break;
			default: throw new NotImplementedException();
		}
	}
	#endregion
}

