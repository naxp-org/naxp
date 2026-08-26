// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.IO;
using System.Text;
using Xunit;

namespace Naxp.UnitTests;

public sealed class AsciiCharSetTests
{
	[Fact]
	public void TestAsciiCharSet()
	{
		var testData = new[]
		{
			( text: "A", normalisedText: "A", charsIncluded: "A" ),
			( text: "[AB]", normalisedText: null, charsIncluded: "AB" ),
			( text: "[ABC]", normalisedText: null, charsIncluded: "ABC" ),
			( text: "[A-D]", normalisedText: null, charsIncluded: "ABCD" ),
			( text: "[A-DF]", normalisedText: null, charsIncluded: "ABCDF" ),
			( text: "[ABCEH-M]", normalisedText: null, charsIncluded: "ABCEHIJKLM"),
			( text: "~", normalisedText: null, charsIncluded: "~" ),
			( text: "[}~]", normalisedText: null, charsIncluded: "}~" ),
			( text: "[0-36-9]", normalisedText: null, charsIncluded: "01236789" ),
			( text: "[ABD-Z]", normalisedText: null, charsIncluded: "ABDEFGHIJKLMNOPQRSTUVWXYZ" ),
			( text: "[a-kopr-z]", normalisedText: null, charsIncluded: "abcdefghijkoprstuvwxyz" ),
			( text: "[0-9YZ]", normalisedText: null, charsIncluded: "0123456789YZ" ),
			( text: @"\9", normalisedText: null, charsIncluded: "0123456789" ),
			( text: @"\A", normalisedText: null, charsIncluded: "ABCDEFGHIJKLMNOPQRSTUVWXYZ" ),
			( text: @"\a", normalisedText: null, charsIncluded: "abcdefghijklmnopqrstuvwxyz" ),
			( text: @"\X", normalisedText: null, charsIncluded: "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" ),

			( text: @"[\x00\x7F]", normalisedText: null, charsIncluded: "\u0000\u007F" ),
			( text:@"[\x00-\x04\x7D-\x7F]", normalisedText: @"[\x00-\x04}~\x7F]", charsIncluded: "\u0000\u0001\u0002\u0003\u0004\u007D\u007E\u007F" ),

			( text: "[A-B]", normalisedText: "[AB]", charsIncluded: "AB" ),
			( text: "[ABC]", normalisedText: null, charsIncluded: "ABC" ),
			( text: "[ABD-Ka-f0123]", normalisedText: "[0-3ABD-Ka-f]", charsIncluded: "0123ABDEFGHIJKabcdef" ),
			( text: @"\9", normalisedText: null, charsIncluded: "0123456789" ),
			( text: @"\A", normalisedText: null, charsIncluded: "ABCDEFGHIJKLMNOPQRSTUVWXYZ" ),
			( text: @"\a", normalisedText: null, charsIncluded: "abcdefghijklmnopqrstuvwxyz" ),
			( text: @"\X", normalisedText: null, charsIncluded: "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" ),
			( text: @"\(", normalisedText: null, charsIncluded: "(" ),
			( text: "[00001111]", normalisedText: "[01]", charsIncluded: "01" ),
			( text: @"[\[\]\(\)\\]", normalisedText: @"[\(\)\[\\\]]", charsIncluded: @"[]()\" ),
			( text: @"[\[\]\(\9\)\\]", normalisedText: @"[\(\)0-9\[\\\]]", charsIncluded: @"()0123456789[\]" ),

			( text: @"[03-7\A]", normalisedText: @"[03-7A-Z]", charsIncluded: "034567ABCDEFGHIJKLMNOPQRSTUVWXYZ" ),
			( text: @"[\9A-F]", normalisedText: null, charsIncluded: "0123456789ABCDEF" ),

            // Examplar for drafting tests:
            //(text: "...", normalisedText: null, charsIncluded: "\u0020!\"#$%&\'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~\u007F"),
        };

		AsciiCharSet prev_charSet = default;
		foreach (var (text, normalisedText, charsIncluded) in testData)
		{
			AsciiCharSet charSet = AsciiCharSet.Parse(text);

			AsciiCharSet charSet_constructed = default;
			foreach (var c in charsIncluded) { charSet_constructed |= AsciiCharSet.FromSingleChar(c); }

			// Deliberately loop through whole of byte range, i.e. beyond ASCII and 128 bits:
			for (char c = (char)0; c <= 0x200; ++c)
			{
				bool isIncluded = charsIncluded.Contains(c);

				Assert.Equal(isIncluded, charSet.Contains(c));
			}

			// Check some addition exclusions way outside ASCII:
			Assert.False(charSet.Contains('\u03FF'));
			Assert.False(charSet.Contains('\u0400'));
			Assert.False(charSet.Contains('\u07FF'));
			Assert.False(charSet.Contains('\u0800'));
			Assert.False(charSet.Contains('\u0FFF'));
			Assert.False(charSet.Contains('\u1000'));
			Assert.False(charSet.Contains('\u3FFF'));
			Assert.False(charSet.Contains('\u4000'));
			Assert.False(charSet.Contains('\u7FFF'));
			Assert.False(charSet.Contains('\u8000'));

			Assert.Equal(charSet, charSet_constructed);
			Assert.True(charSet == charSet_constructed);

			// Just charSet from here

			Assert.Equal(charSet, charSet);
#pragma warning disable CS1718 // Comparison made to same variable
			Assert.True(charSet == charSet);
#pragma warning restore CS1718 // Comparison made to same variable

			Assert.Equal(charsIncluded.Length, charSet.Count);

			if (normalisedText is not null)
			{
				Assert.Equal(normalisedText, charSet.ToString());

				var sb = new StringBuilder();
				charSet.WriteTo(sb);
				Assert.Equal(normalisedText, sb.ToString());
			}

			Assert.True(charSet_constructed != prev_charSet);

			prev_charSet = charSet_constructed;
		}
	}

	[Fact]
	public void TestAsciiCharSet_IndexOf()
	{
		var testData = new[]
		{
			( charSetText: "",  charIndexPairs: new [] { ('A', -1), } ),
			( charSetText: "A",  charIndexPairs:[ ('A', 0), ('B', -1), ] ),
			( charSetText: "[BDFH]",  charIndexPairs:[ ('A', -1), ('B', 0), ('C', -1), ('D', 1), ('E', -1), ('F', 2), ('G', -1), ('H', 3), ('I', -1), ] ),
			( charSetText: @"[\x00\x7F]",  charIndexPairs:[ ('\0', 0), ('\u0001', -1), ('\u007E', -1), ('\u007F', 1), ] ),
			( charSetText: @"[\x00A\x7F]",  charIndexPairs:[ ('\0', 0), ('\u0001', -1), ('A', 1), ('\u007E', -1), ('\u007F', 2), ] ),
		};

		foreach (var (charSetText, charIndexPairs) in testData)
		{
			AsciiCharSet charSet = AsciiCharSet.Parse(charSetText);

			foreach (var (c, index) in charIndexPairs)
			{
				Assert.Equal(charSet.IndexOf(c), index);
			}
		}
	}

	[Fact]
	public void TestAsciiCharSet_Enumerator()
	{
		var testData = new[]
		{
			( charSetText: "",  chars: "" ),
			( charSetText: "A",  chars: "A" ),
			( charSetText: @"\x00",  chars: "\u0000" ),
			( charSetText: @"[\x00A]",  chars: "\u0000A" ),
			( charSetText: @"[A\x7F]",  chars: "A\u007F" ),
			( charSetText: @"\x7F",  chars: "\u007F" ),
			( charSetText: @"[\x00\x7F]",  chars: "\u0000\u007F" ),
			( charSetText: @"[\x00A1\x7F]",  chars: "\u00001A\u007F" ),
			( charSetText: @"[\x00135ABCxyz\x7F]",  chars: "\u0000135ABCxyz\u007F" ),
			( charSetText: @"[135ABCxyz]",  chars: "135ABCxyz" ),
		};

		foreach (var (charSetText, chars) in testData)
		{
			AsciiCharSet charSet = AsciiCharSet.Parse(charSetText);

			Assert.Equal(chars.Length, charSet.Count);

			int i = 0;
			foreach (var c in charSet)
			{
				Assert.Equal(chars[i], c);
				++i;
			}
			Assert.Equal(chars.Length, i);
		}
	}

	[Fact]
	public void TestAsciiCharSet_Constants()
	{
		for (char c = (char)0; c < (char)0x80; ++c)
		{
			Assert.Equal('0' <= c && c <= '9', AsciiCharSet.AllDigits.Contains(c));
			Assert.Equal('A' <= c && c <= 'Z', AsciiCharSet.AllUpperCaseLetters.Contains(c));
			Assert.Equal('a' <= c && c <= 'z', AsciiCharSet.AllLowerCaseLetters.Contains(c));
			Assert.Equal(('0' <= c && c <= '9') || ('A' <= c && c <= 'Z'), AsciiCharSet.AllDigitsAndUpperCaseLetters.Contains(c));
		}
	}

	[Fact]
	public void TestAsciiCharSet_FactoryMethods()
	{
		var testData = new[]
		{
			( cMin: (char)0, cMax: (char)0),
			( cMin: (char)0, cMax: (char)1),
			( cMin: (char)0, cMax: (char)127),
			( cMin: (char)1, cMax: (char)1),
			( cMin: (char)1, cMax: (char)127),
		};

		AsciiCharSet prev_charSet = default;
		foreach (var (cMin, cMax) in testData)
		{
			AsciiCharSet charSet = AsciiCharSet.FromCharRange(cMin, cMax);

			AsciiCharSet charSet2 = default;
			for (int i = cMin; i <= cMax; ++i)
			{
				charSet2 |= AsciiCharSet.FromSingleChar((char)i);
			}

			for (int i = 0; i <= 127; ++i)
			{
				bool included = cMin <= i && i <= cMax;
				Assert.Equal(included, charSet.Contains((char)i));
				Assert.Equal(included, charSet2.Contains((char)i));
			}

			Assert.True(charSet == charSet2);
			Assert.True(charSet != prev_charSet);

			prev_charSet = charSet;
		}
	}

	[Fact]
	public void TestAsciiCharSet_SetOperations()
	{
		var charSetTexts = new[]
		{
			"",
			"A",
			"AB",
			"ABC",
			"ABCD",
			"AC",
			"ACD",
			"AD",
			"B",
			"BC",
			"BCD",
			"C",
			"CD",
			"D",
		};

		var charSets = new AsciiCharSet[charSetTexts.Length];
		for (int i = 0; i < charSetTexts.Length; ++i)
		{
			var text = charSetTexts[i];
			charSets[i] = text == "" ? default : AsciiCharSet.Parse($"[{text}]");
		}

		for (int i = 0; i < charSets.Length; ++i)
		{
			var charSet_i = charSets[i];
			var charSetText_i = charSetTexts[i];
			for (int k = 0; k < charSets.Length; ++k)
			{
				var charSet_k = charSets[k];
				var charSetText_k = charSetTexts[k];

				Assert.Equal(Set_Equals(charSetText_i, charSetText_k), charSet_i == charSet_k);
				Assert.Equal(!Set_Equals(charSetText_i, charSetText_k), charSet_i != charSet_k);

				//Assert.Equal(Set_Contains(charSetText_i, charSetText_k), charSet_i.Contains(charSet_k));
				Assert.Equal(Set_IntersectsWith(charSetText_i, charSetText_k), charSet_i.IntersectsWith(charSet_k));
				Assert.Equal(!(charSet_i & charSet_k).IsEmpty, charSet_i.IntersectsWith(charSet_k));

				Assert.True(AreEquivalentCharSets(Set_Union(charSetText_i, charSetText_k), charSet_i | charSet_k));
				Assert.True(AreEquivalentCharSets(Set_Intersection(charSetText_i, charSetText_k), charSet_i & charSet_k));
				Assert.True(AreEquivalentCharSets(Set_Difference(charSetText_i, charSetText_k), charSet_i - charSet_k));

				var (intersection, i_less_k, k_less_i) = charSet_i.GetDisjointCombinations(charSet_k);
				Assert.Equal(charSet_i & charSet_k, intersection);
				Assert.Equal(charSet_i - charSet_k, i_less_k);
				Assert.Equal(charSet_k - charSet_i, k_less_i);
			}
		}
	}

	#region Set operation helpers
	static bool AreEquivalentCharSets(string chars, AsciiCharSet charSet)
	{
		if (chars.Length != charSet.Count) { return false; }

		foreach (var c in chars)
		{
			if (!charSet.Contains(c)) { return false; }
		}
		return true;
	}
	static bool Set_Equals(string left, string right) => left.Equals(right);
	static bool Set_Contains(string left, string right)
	{
		foreach (var r in right)
		{
			if (!left.Contains(r)) { return false; }
		}
		return true;
	}
	static bool Set_IntersectsWith(string left, string right)
	{
		foreach (var l in left)
		{
			if (right.Contains(l)) { return true; }
		}
		return false;
	}
	static string Set_Intersection(string left, string right)
	{
		if (Set_Contains(left, right)) { return right; }
		if (Set_Contains(right, left)) { return left; }
		if (!Set_IntersectsWith(left, right)) { return ""; }

		var sb = new StringBuilder();

		foreach (char c in left)
		{
			if (right.Contains(c) & !Contains(sb, c)) { sb.Append(c); }
		}
		foreach (char c in right)
		{
			if (left.Contains(c) & !Contains(sb, c)) { sb.Append(c); }
		}

		return sb.ToString();
	}
	static string Set_Union(string left, string right)
	{
		if (Set_Contains(left, right)) { return left; }
		if (Set_Contains(right, left)) { return right; }
		if (!Set_IntersectsWith(left, right)) { return left + right; }

		var sb = new StringBuilder();

		foreach (char c in left)
		{
			if (!Contains(sb, c)) { sb.Append(c); }
		}
		foreach (char c in right)
		{
			if (!Contains(sb, c)) { sb.Append(c); }
		}

		return sb.ToString();
	}
	static string Set_Difference(string left, string right)
	{
		if (!Set_IntersectsWith(left, right)) { return left; }

		var sb = new StringBuilder();

		foreach (char c in left)
		{
			if (!right.Contains(c) & !Contains(sb, c)) { sb.Append(c); }
		}

		return sb.ToString();
	}
	static bool Contains(StringBuilder sb, char c)
	{
		int n = sb.Length;
		for (int i = 0; i < n; ++i)
		{
			if (sb[i] == c) { return true; }
		}
		return false;
	}
	#endregion

	[Fact]
	public void TestAsciiCharSet_Ordering()
	{
		var sortedTexts = new[]
		{
			"",
			"[A]",
			"[AB]",
			"[ABC]",
			"[ABCD]",
			"[ABCDE]",
			"[ABCDEFGHIJKLMNOPQRSTUVWXY]",
			@"\A",
			"[ABCDEFGHIJKLMNOPQRSTUVWXZ]",
			"[ABCE]",
			"[ABCX]",
			"[ABCXY]",
			"[ABCXYZ]",
			"[ABCY]",
			"[ABCYZ]",
			"[ABCZ]",
			"[ABD]",
			"[ABDE]",
			"[ABE]",
			"[AC]",
			"[ACD]",
			"[ACDE]",
			"[ACE]",
			"[AD]",
			"[ADE]",
			"[AE]",
			"[B]",
			"[BC]",
			"[BCD]",
			"[BCDE]",
			"[BCE]",
			"[BD]",
			"[BDE]",
			"[BE]",
			"[C]",
			"[CD]",
			"[CDE]",
			"[CE]",
			"[D]",
			"[DE]",
			"[E]",
		};

		var sorted = new AsciiCharSet[sortedTexts.Length];
		for (int i = 0; i < sortedTexts.Length; ++i)
		{
			var text = sortedTexts[i];
			sorted[i] = text == "" ? default : AsciiCharSet.Parse(text);
		}

		for (int i = 0; i < sorted.Length; ++i)
		{
			var charSet_i = sorted[i];
			for (int k = 0; k < sorted.Length; ++k)
			{
				var charSet_k = sorted[k];

				Assert.Equal(Math.Sign(i.CompareTo(k)), Math.Sign(charSet_i.CompareTo(charSet_k)));

				Assert.Equal(i == k, charSet_i == charSet_k);
				Assert.Equal(i != k, charSet_i != charSet_k);
			}
		}
	}

	[Fact]
	public void TestAsciiCharSet_IO()
	{
		var c_empty = new AsciiCharSet();
		var c_0x00 = AsciiCharSet.FromSingleChar('\u0000');
		var c_0x7F = AsciiCharSet.FromSingleChar('\u007F');
		var c_all = AsciiCharSet.FromCharRange('\u0000', '\u007F');

		var c_0 = AsciiCharSet.FromSingleChar('0');
		var c_A = AsciiCharSet.FromSingleChar('A');
		var c_a = AsciiCharSet.FromSingleChar('a');

		var c_0_9 = AsciiCharSet.Parse("\\9");
		var c_A_Z = AsciiCharSet.FromCharRange('A', 'Z');
		var c_a_z = AsciiCharSet.FromCharRange('a', 'z');

		var c_complex = AsciiCharSet.Parse("[0135789ABDGHKMRSTacdghijkxz]");

		var charSets = new[]
		{
			c_empty,
			c_0x00,
			c_0x7F,
			c_all,

			c_0,
			c_A,
			c_a,

			c_0_9,
			c_A_Z,
			c_a_z,

			c_0_9 | c_A_Z,
			c_0_9 | c_A_Z | c_a_z,

			c_complex,

			c_0x00 | c_complex,
			c_complex | c_0x7F,
			c_0x00 | c_complex | c_0x7F,
		};

		using Stream stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		var reader = new BinaryReader(stream);

		foreach (var charSet in charSets)
		{
			stream.Position = 0;
			charSet.WriteTo(writer);
			var endPosition = stream.Position;

			stream.Position = 0;
			var charSet2 = reader.Read<AsciiCharSet>();

			Assert.Equal(charSet, charSet2);
			Assert.Equal(endPosition, stream.Position);
		}
	}
}