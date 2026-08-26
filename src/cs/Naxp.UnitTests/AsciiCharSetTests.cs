// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Text;
using LogMu;
using Xunit;

namespace LogMu.UnitTests;

public class AsciiCharSetTests
{
	const int CharacterCount = AsciiCharSet.CharacterCount;

	#region Construction
	/// <summary>
	/// Every range, checked character by character. This is what catches a shift count that
	/// C# has masked, because it covers 0, 63, 64 and 127 as both bounds.
	/// </summary>
	[Fact]
	public void FromCharRange_MatchesReference_ForEveryRange()
	{
		for (int min = 0; min < CharacterCount; ++min)
		{
			for (int max = min; max < CharacterCount; ++max)
			{
				var charSet = AsciiCharSet.FromCharRange((char)min, (char)max);

				Assert.Equal((max - min) + 1, charSet.Count);

				for (int c = 0; c < CharacterCount; ++c)
				{
					Assert.Equal(c >= min && c <= max, charSet.Contains((char)c));
				}
			}
		}
	}

	[Fact]
	public void FromSingleChar_MatchesReference_ForEveryCharacter()
	{
		for (int i = 0; i < CharacterCount; ++i)
		{
			var charSet = AsciiCharSet.FromSingleChar((char)i);

			Assert.Equal(1, charSet.Count);
			Assert.Equal((char)i, charSet.SingleCharacter);

			for (int c = 0; c < CharacterCount; ++c)
			{
				Assert.Equal(c == i, charSet.Contains((char)c));
			}
		}
	}

	[Fact]
	public void Construction_RejectsNonAscii()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => AsciiCharSet.FromSingleChar((char)128));
		Assert.Throws<ArgumentOutOfRangeException>(() => AsciiCharSet.FromCharRange((char)128, (char)129));
		Assert.Throws<ArgumentOutOfRangeException>(() => AsciiCharSet.FromCharRange('A', (char)200));
	}

	[Fact]
	public void FromCharRange_RejectsAHighestFirstRange()
	{
		// The bug fixed in NXOld: the parser must check this before calling in, and the
		// contract here is that it throws, so that a missing check cannot pass unnoticed.
		Assert.Throws<ArgumentOutOfRangeException>(() => AsciiCharSet.FromCharRange('E', 'A'));
	}

	[Fact]
	public void Empty_HasNoCharacters()
	{
		Assert.True(AsciiCharSet.Empty.IsEmpty);
		Assert.Equal(0, AsciiCharSet.Empty.Count);
		Assert.Null(AsciiCharSet.Empty.SingleCharacter);
		Assert.Equal(default, AsciiCharSet.Empty);

		for (int c = 0; c < CharacterCount; ++c)
		{
			Assert.False(AsciiCharSet.Empty.Contains((char)c));
			Assert.Equal(-1, AsciiCharSet.Empty.IndexOf((char)c));
		}
	}

	[Fact]
	public void NonAsciiCharacterIsNeverContained()
	{
		var all = AsciiCharSet.FromCharRange((char)0, (char)127);

		Assert.False(all.Contains((char)128));
		Assert.False(all.Contains('£'));
		Assert.Equal(-1, all.IndexOf((char)128));
	}
	#endregion

	#region Behaviour against a reference implementation
	[Fact]
	public void Membership_MatchesReference()
	{
		foreach (var (charSet, reference) in SampleSets())
		{
			Assert.Equal(reference.Count, charSet.Count);
			Assert.Equal(reference.Count == 0, charSet.IsEmpty);
			Assert.Equal(reference.Count == 1 ? reference[0] : (char?)null, charSet.SingleCharacter);

			for (int c = 0; c < CharacterCount; ++c)
			{
				Assert.Equal(reference.Contains((char)c), charSet.Contains((char)c));
				Assert.Equal(reference.IndexOf((char)c), charSet.IndexOf((char)c));
			}
		}
	}

	/// <summary>
	/// Decoding needs the inverse of <see cref="AsciiCharSet.IndexOf"/>, so the two must agree
	/// both ways round.
	/// </summary>
	[Fact]
	public void CharacterAt_InvertsIndexOf()
	{
		foreach (var (charSet, reference) in SampleSets())
		{
			for (int i = 0; i < reference.Count; ++i)
			{
				Assert.Equal(reference[i], charSet.CharacterAt(i));
				Assert.Equal(i, charSet.IndexOf(charSet.CharacterAt(i)));
			}

			Assert.Throws<ArgumentOutOfRangeException>(() => charSet.CharacterAt(reference.Count));
			Assert.Throws<ArgumentOutOfRangeException>(() => charSet.CharacterAt(-1));
		}
	}

	[Fact]
	public void Enumerator_YieldsCharactersInAscendingOrder()
	{
		foreach (var (charSet, reference) in SampleSets())
		{
			var yielded = new List<char>();
			foreach (var c in charSet) { yielded.Add(c); }

			Assert.Equal(reference, yielded);
		}
	}

	[Fact]
	public void Operators_MatchReference()
	{
		var samples = new List<(AsciiCharSet charSet, List<char> reference)>(SampleSets());

		for (int i = 0; i < samples.Count; ++i)
		{
			for (int j = 0; j < samples.Count; ++j)
			{
				var (left, leftReference) = samples[i];
				var (right, rightReference) = samples[j];

				AssertSameCharacters(Union(leftReference, rightReference), left | right);
				AssertSameCharacters(Intersection(leftReference, rightReference), left & right);
				AssertSameCharacters(Difference(leftReference, rightReference), left - right);

				Assert.Equal(Intersection(leftReference, rightReference).Count != 0, left.IntersectsWith(right));

				var (intersection, leftLessRight, rightLessLeft) = left.GetDisjointCombinations(right);
				Assert.Equal(left & right, intersection);
				Assert.Equal(left - right, leftLessRight);
				Assert.Equal(right - left, rightLessLeft);
			}
		}
	}

	/// <summary>
	/// The documented order is that of the sets written out as strings and compared ordinally.
	/// </summary>
	[Fact]
	public void CompareTo_MatchesOrdinalStringOrder()
	{
		var samples = new List<(AsciiCharSet charSet, List<char> reference)>(SampleSets());

		for (int i = 0; i < samples.Count; ++i)
		{
			for (int j = 0; j < samples.Count; ++j)
			{
				var (left, leftReference) = samples[i];
				var (right, rightReference) = samples[j];

				var expected = Math.Sign(string.CompareOrdinal(AsString(leftReference), AsString(rightReference)));
				var actual = Math.Sign(left.CompareTo(right));

				Assert.Equal(expected, actual);
			}
		}
	}

	[Fact]
	public void CompareTo_OrdersTheDocumentedExamples()
	{
		// [a] < [ab] < [abc] < [ac] < [b] < [c] < [cd]
		var ordered = new[]
		{
			Set("a"),
			Set("ab"),
			Set("abc"),
			Set("ac"),
			Set("b"),
			Set("c"),
			Set("cd"),
		};

		for (int i = 0; i < ordered.Length - 1; ++i)
		{
			Assert.True(ordered[i].CompareTo(ordered[i + 1]) < 0, $"Expected item {i} to sort before item {i + 1}.");
			Assert.True(ordered[i + 1].CompareTo(ordered[i]) > 0, $"Expected item {i + 1} to sort after item {i}.");
			Assert.Equal(0, ordered[i].CompareTo(ordered[i]));
		}
	}

	[Fact]
	public void EqualitySurvivesADifferentRouteToTheSameSet()
	{
		var byRange = AsciiCharSet.FromCharRange('0', '9');
		var byUnion = AsciiCharSet.Empty;
		for (char c = '0'; c <= '9'; ++c) { byUnion |= AsciiCharSet.FromSingleChar(c); }

		Assert.True(byRange == byUnion);
		Assert.False(byRange != byUnion);
		Assert.True(byRange.Equals(byUnion));
		Assert.True(byRange.Equals((object)byUnion));
		Assert.False(byRange.Equals("not a char set"));
		Assert.Equal(byRange.GetHashCode(), byUnion.GetHashCode());
		Assert.Equal(0, byRange.CompareTo(byUnion));
	}
	#endregion

	#region Named sets
	[Fact]
	public void NamedSetsHoldTheRightCharacters()
	{
		AssertSameCharacters(Range('0', '9'), AsciiCharSet.AllDigits);
		AssertSameCharacters(Range('A', 'Z'), AsciiCharSet.AllUpperCaseLetters);
		AssertSameCharacters(Range('a', 'z'), AsciiCharSet.AllLowerCaseLetters);
		AssertSameCharacters(Union(Range('0', '9'), Range('A', 'Z')), AsciiCharSet.AllDigitsAndUpperCaseLetters);

		Assert.Equal(10, AsciiCharSet.AllDigits.Count);
		Assert.Equal(26, AsciiCharSet.AllUpperCaseLetters.Count);
		Assert.Equal(26, AsciiCharSet.AllLowerCaseLetters.Count);
		Assert.Equal(36, AsciiCharSet.AllDigitsAndUpperCaseLetters.Count);
	}
	#endregion

	#region Supporting stuff
	/// <summary>
	/// Sets to test against, each paired with its characters in ascending order.
	/// </summary>
	static IEnumerable<(AsciiCharSet charSet, List<char> reference)> SampleSets()
	{
		yield return Pair([]);
		yield return Pair([(char)0]);
		yield return Pair([(char)63]);
		yield return Pair([(char)64]);
		yield return Pair([(char)127]);
		yield return Pair([(char)63, (char)64]);
		yield return Pair([(char)0, (char)127]);
		yield return Pair(RangeChars(0, 127));
		yield return Pair(RangeChars('0', '9'));
		yield return Pair(RangeChars('A', 'Z'));
		yield return Pair(RangeChars('a', 'z'));
		yield return Pair(['a']);
		yield return Pair(['a', 'b']);
		yield return Pair(['a', 'b', 'c']);
		yield return Pair(['a', 'c']);
		yield return Pair(['b']);

		var random = new Random(20260810);
		for (int i = 0; i < 200; ++i)
		{
			var characters = new List<char>();
			for (int c = 0; c < CharacterCount; ++c)
			{
				if (random.Next(4) == 0) { characters.Add((char)c); }
			}
			yield return Pair(characters);
		}
	}

	static (AsciiCharSet, List<char>) Pair(IEnumerable<char> characters)
	{
		var reference = new List<char>(characters);
		reference.Sort();

		var charSet = AsciiCharSet.Empty;
		foreach (var c in reference) { charSet |= AsciiCharSet.FromSingleChar(c); }

		return (charSet, reference);
	}

	static AsciiCharSet Set(string characters)
	{
		var charSet = AsciiCharSet.Empty;
		foreach (var c in characters) { charSet |= AsciiCharSet.FromSingleChar(c); }
		return charSet;
	}

	static List<char> RangeChars(int min, int max)
	{
		var characters = new List<char>();
		for (int c = min; c <= max; ++c) { characters.Add((char)c); }
		return characters;
	}

	static List<char> Range(char min, char max) => RangeChars(min, max);

	static List<char> Union(List<char> left, List<char> right)
	{
		var result = new List<char>(left);
		foreach (var c in right) { if (!result.Contains(c)) { result.Add(c); } }
		result.Sort();
		return result;
	}

	static List<char> Intersection(List<char> left, List<char> right)
	{
		var result = new List<char>();
		foreach (var c in left) { if (right.Contains(c)) { result.Add(c); } }
		result.Sort();
		return result;
	}

	static List<char> Difference(List<char> left, List<char> right)
	{
		var result = new List<char>();
		foreach (var c in left) { if (!right.Contains(c)) { result.Add(c); } }
		result.Sort();
		return result;
	}

	static string AsString(List<char> characters)
	{
		var sb = new StringBuilder();
		foreach (var c in characters) { sb.Append(c); }
		return sb.ToString();
	}

	static void AssertSameCharacters(List<char> expected, AsciiCharSet actual)
	{
		Assert.Equal(expected.Count, actual.Count);

		for (int c = 0; c < CharacterCount; ++c)
		{
			Assert.Equal(expected.Contains((char)c), actual.Contains((char)c));
		}
	}
	#endregion
}
