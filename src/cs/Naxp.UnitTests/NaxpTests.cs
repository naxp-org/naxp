// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The public surface, and the two things it adds rather than forwards: the acceptance walk over
/// the machine for <i>L</i>, and the ASCII byte overloads.
/// </summary>
public class NaxpTests
{
	#region Parsing
	[Fact]
	public void Parse_KeepsTheSource()
	{
		Naxp naxp = Naxp.Parse("AB|B");

		Assert.Equal("AB|B", naxp.Source);
		Assert.Equal("AB|B", naxp.ToString());
	}

	/// <summary>
	/// Whitespace between tokens is ignored, so it survives in the source without reaching the
	/// language.
	/// </summary>
	[Fact]
	public void Source_IsWhatWasWrittenRatherThanWhatWasMeant()
	{
		Naxp naxp = Naxp.Parse("A | B");

		Assert.Equal("A | B", naxp.Source);
		Assert.Equal(2UL, naxp.ValueCount);
	}

	[Fact]
	public void Parse_TakesAStringWithoutCeremony()
	{
		// The point of the implicit conversion: a caller holding a string writes nothing extra.
		string source = "[A-Z]";

		Assert.Equal(26UL, Naxp.Parse(source).ValueCount);
	}

	[Fact]
	public void Parse_TakesASlice()
	{
		Naxp naxp = Naxp.Parse("xx[A-Z]yy".AsSpan(2, 5));

		Assert.Equal("[A-Z]", naxp.Source);
		Assert.Equal(26UL, naxp.ValueCount);
	}

	/// <summary>
	/// One per rule, so a refusal that arrives for the wrong reason is caught here rather than
	/// merely counted as a refusal. <c>A{2,5}</c> is a syntax error and not W4: the comma is the
	/// near miss the parser has an error production for, so it never reaches a count to check.
	/// </summary>
	[Theory]
	[InlineData("A|", "syntax")]
	[InlineData("A{2-5}", "syntax")]
	[InlineData("\\A!!", "W1")]
	[InlineData("(A|B)!(B!B)", "W2")]
	[InlineData("AB!!B?C", "W3")]
	[InlineData("A{5,2}", "W4")]
	[InlineData("\\9{20}", "W5")]
	public void Parse_RefusesAnIllFormedNaxp(string source, string expectedRule)
	{
		FormatException exception = Assert.Throws<FormatException>(() => Naxp.Parse(source));

		Assert.False(
			Naxp.TryParse(source, out Naxp? naxp, out _, out _, out _, out string? errorCode));
		Assert.Null(naxp);

		// The thrown message leads with the code, so this also pins that the two ways of asking
		// agree.
		Assert.StartsWith(errorCode, exception.Message, StringComparison.Ordinal);
		Assert.Equal(expectedRule, NaxpMessageRules.RuleOf(errorCode));
	}

	/// <summary>
	/// The message is now the reason alone, and where the fault is has its own two numbers, so a
	/// caller can underline it rather than read a position out of the prose.
	/// </summary>
	[Fact]
	public void TryParse_ReportsTheReasonTheSpanAndTheCode()
	{
		const string Source = "A{5,2}";

		Assert.False(Naxp.TryParse(
			Source,
			out Naxp? naxp,
			out string? errorMessage,
			out int errorTextOffset,
			out int errorTextLength,
			out string? errorCode));

		Assert.Null(naxp);
		Assert.Equal("NAXP1007", errorCode);
		Assert.Equal("The first count of an interval cannot exceed the second.", errorMessage);

		// The span covers '{5,2}', which is the interval and not the whole naxp.
		Assert.Equal("{5,2}", Source.Substring(errorTextOffset, errorTextLength));
	}

	/// <summary>
	/// A refusal that belongs to no one place in the naxp reports the whole of it, so that a
	/// caller underlining the span never points at an innocent first character.
	/// </summary>
	[Fact]
	public void TryParse_OfAFaultWithNoPosition_ReportsTheWholeNaxp()
	{
		const string Source = "\\9{20}";

		Assert.False(Naxp.TryParse(
			Source,
			out Naxp? naxp,
			out _,
			out int errorTextOffset,
			out int errorTextLength,
			out string? errorCode));

		Assert.Null(naxp);
		Assert.Equal("NAXP1047", errorCode);
		Assert.Equal(0, errorTextOffset);
		Assert.Equal(Source.Length, errorTextLength);
	}

	/// <summary>
	/// The short overload says the same thing about what is wrong, and nothing about where.
	/// </summary>
	[Fact]
	public void TryParse_OfTheShortOverload_GivesTheSameMessage()
	{
		Assert.False(Naxp.TryParse("A{5,2}", out Naxp? shortNaxp, out string? shortMessage));
		Assert.False(Naxp.TryParse("A{5,2}", out Naxp? longNaxp, out string? longMessage, out _, out _, out _));

		Assert.Null(shortNaxp);
		Assert.Null(longNaxp);
		Assert.Equal(longMessage, shortMessage);
	}

	/// <summary>
	/// Every reserved character has an escape that matches it, and nothing else does.
	/// </summary>
	/// <remarks>
	/// Driven by the list rather than by cases, because what this catches is a character joining
	/// the reserved set in the specification and in one implementation but not the other. That is
	/// exactly what happened when the comma became the interval separator: the C# reserved it, the
	/// JavaScript did not, and until this existed nothing noticed.
	/// </remarks>
	[Fact]
	public void EveryReservedCharacter_HasAnEscapeThatMatchesIt()
	{
		// The thirteen of the specification's table, in its order.
		const string Reserved = "!#(),-?[\\]{|}";

		Assert.Equal(13, Reserved.Length);

		foreach (char c in Reserved)
		{
			var naxp = Naxp.Parse("\\" + c);

			Assert.Equal(1UL, naxp.ValueCount);
			Assert.True(naxp.Accepts(c.ToString()), $"'\\{c}' does not match '{c}'.");
		}
	}

	/// <summary>
	/// And a character outside that set stands for itself, with no escape needed.
	/// </summary>
	[Fact]
	public void ACharacterOutsideTheReservedSet_StandsForItself()
	{
		foreach (char c in "\"$%&'*+./:;<=>@^_`~")
		{
			var naxp = Naxp.Parse(c.ToString());

			Assert.True(naxp.Accepts(c.ToString()), $"'{c}' does not match itself.");
		}
	}

	[Fact]
	public void TryParse_ClearsTheMessageOnSuccess()
	{
		Assert.True(Naxp.TryParse("A", out Naxp? naxp, out string? errorMessage));
		Assert.NotNull(naxp);
		Assert.Null(errorMessage);
	}

	/// <summary>
	/// The naxp is legal. It is this implementation's state budget that refuses it, and the
	/// refusal has to arrive through the same door as any other.
	/// </summary>
	[Fact]
	public void Parse_RefusesANaxpOverBudget()
		// Legal under the grammar since version 0.5 caps a count at two digits; 9 802 states.
		=> Assert.Throws<FormatException>(() => Naxp.Parse("(A{99}){99}"));
	#endregion
	#region Acceptance
	[Fact]
	public void Accepts_AgreesWithEncoding()
	{
		Naxp naxp = Naxp.Parse("#[0-10]");

		Assert.True(naxp.Accepts("0"));
		Assert.True(naxp.Accepts("10"));
		Assert.False(naxp.Accepts("11"));
		Assert.False(naxp.Accepts(string.Empty));
		Assert.False(naxp.Accepts("00"));
	}

	/// <summary>
	/// <see cref="Naxp.Accepts(ReadOnlySpan{char})"/> walks the machine for <i>L</i> while
	/// <see cref="Naxp.Encode(ReadOnlySpan{char})"/> canonicalises over the tree. They are
	/// separate routes to the same question, so every string in the test data checks both.
	/// </summary>
	[Fact]
	public void Accepts_AgreesWithEncodingAcrossTheTestData()
	{
		ConformanceTestData data = ConformanceTestData.Load();
		int checks = 0;

		foreach (ConformanceCase item in data.Cases)
		{
			if (!Naxp.TryParse(item.Naxp, out Naxp? naxp, out string? errorMessage))
			{
				Assert.Fail($"{item.Naxp} was refused: {errorMessage}");
				continue;
			}

			foreach (ConformanceValue value in item.Values)
			{
				Assert.True(naxp.Accepts(value.In), $"{item.Naxp} did not accept {value.In}.");
				++checks;
			}

			foreach (string text in item.NotAccepted)
			{
				Assert.False(naxp.Accepts(text), $"{item.Naxp} accepted {text}.");
				++checks;
			}
		}

		Assert.True(checks > 1400, $"Only {checks} strings were checked.");
	}
	#endregion
	#region Encoding and decoding
	[Fact]
	public void Encode_NumbersFromOne()
	{
		Naxp naxp = Naxp.Parse("AB|B");

		Assert.Equal(1UL, naxp.Encode("AB"));
		Assert.Equal(2UL, naxp.Encode("B"));
		Assert.Equal(0UL, naxp.Encode("A"));
	}

	[Fact]
	public void TryEncode_ReportsAcceptanceAndLeavesZeroBehind()
	{
		Naxp naxp = Naxp.Parse("AB|B");

		Assert.True(naxp.TryEncode("B", out ulong encoded));
		Assert.Equal(2UL, encoded);

		Assert.False(naxp.TryEncode("A", out encoded));
		Assert.Equal(0UL, encoded);
	}

	[Fact]
	public void Decode_InvertsEncode()
	{
		Naxp naxp = Naxp.Parse("[A-Z]{2}");

		for (ulong value = 1UL; value <= naxp.ValueCount; ++value)
		{
			Assert.Equal(value, naxp.Encode(naxp.Decode(value)));
		}
	}

	[Theory]
	[InlineData(0UL)]
	[InlineData(27UL)]
	[InlineData((ulong)long.MaxValue)]
	[InlineData(ulong.MaxValue)]
	public void TryDecode_RefusesAValueOutsideTheRange(ulong value)
	{
		Assert.False(Naxp.Parse("[A-Z]").TryDecode(value, out string? text));
		Assert.Null(text);
	}

	[Fact]
	public void Decode_ThrowsOutsideTheRange()
	{
		ArgumentOutOfRangeException exception
			= Assert.Throws<ArgumentOutOfRangeException>(() => Naxp.Parse("[A-Z]").Decode(0UL));

		Assert.Contains("1 to 26", exception.Message, StringComparison.Ordinal);
	}
	#endregion
	#region Canonical form
	/// <summary>
	/// The postcode example: both spellings encode alike and the value decodes to the one with
	/// the space, which is what the canonical form means.
	/// </summary>
	[Fact]
	public void CanonicalForm_IsWhatDecodingProduces()
	{
		Naxp naxp = Naxp.Parse(Postcode);

		Assert.Equal("M1 1AA", naxp.GetCanonicalForm("M1 1AA"));
		Assert.Equal("M1 1AA", naxp.GetCanonicalForm("M11AA"));
		Assert.Equal(naxp.Encode("M11AA"), naxp.Encode("M1 1AA"));
		Assert.Equal("M1 1AA", naxp.Decode(naxp.Encode("M11AA")));
	}

	[Fact]
	public void CanonicalForm_IsNullWhenTheNaxpDoesNotAccept()
	{
		Naxp naxp = Naxp.Parse("[A-Z]");

		Assert.Null(naxp.GetCanonicalForm("1"));
		Assert.False(naxp.TryGetCanonicalForm("1", out string? canonicalForm));
		Assert.Null(canonicalForm);
	}
	#endregion
	#region ASCII byte overloads
	[Fact]
	public void ByteOverloads_AgreeWithTheCharacterOnes()
	{
		Naxp naxp = Naxp.Parse(Postcode);

		Assert.True(naxp.Accepts(Ascii("M11AA")));
		Assert.False(naxp.Accepts(Ascii("M11A")));
		Assert.Equal(810639597UL, naxp.Encode(Ascii("M11AA")));

		Assert.True(naxp.TryEncode(Ascii("M1 1AA"), out ulong encoded));
		Assert.Equal(810639597UL, encoded);
	}

	/// <summary>
	/// A byte above 0x7E cannot be named by any naxp, so it is refused rather than folded onto
	/// some character that can.
	/// </summary>
	[Fact]
	public void ByteOverloads_RefuseAnythingOutsideAscii()
	{
		Naxp naxp = Naxp.Parse("[A-Z]{3}");

		Assert.False(naxp.Accepts(new byte[] { (byte)'A', 0xC3, 0x89 }));
		Assert.Equal(0UL, naxp.Encode(new byte[] { (byte)'A', 0xC3, 0x89 }));

		// 0xC1 is 'A' with the high bit set. Masking it off would accept this.
		Assert.False(naxp.Accepts(new byte[] { 0xC1, 0xC1, 0xC1 }));
	}

	/// <summary>
	/// Long text takes the heap path rather than the stack one, and has to give the same answer.
	/// </summary>
	[Fact]
	public void ByteOverloads_HandleTextLongerThanTheStackBuffer()
	{
		// Not [A-Z], which at this length has 26^300 values and is refused by W5. The count is
		// nested because version 0.5 caps an interval count at two digits.
		Naxp naxp = Naxp.Parse("(Q{50}){6}");
		var text = new string('Q', 300);

		Assert.True(naxp.Accepts(Ascii(text)));
		Assert.Equal(naxp.Encode(text), naxp.Encode(Ascii(text)));

		Assert.False(naxp.Accepts(Ascii(new string('Q', 301))));
	}

	[Fact]
	public void ByteOverloads_RefuseTextLongerThanAnyNaxpCanGenerate()
	{
		Naxp naxp = Naxp.Parse("[A-Z]");
		var text = new byte[NaxpLimits.MaxStringLength + 1];

		Assert.False(naxp.Accepts(text));
		Assert.Equal(0UL, naxp.Encode(text));
	}

	[Fact]
	public void ByteOverloads_AcceptEmptyText()
	{
		Naxp naxp = Naxp.Parse("()");

		Assert.True(naxp.Accepts(Array.Empty<byte>()));
		Assert.Equal(1UL, naxp.Encode(Array.Empty<byte>()));
	}
	#endregion
	#region Test data through the public surface
	/// <summary>
	/// The whole contract once more, but through <see cref="Naxp"/> rather than through
	/// <c>Compilation</c>, which is what catches a façade that forwards to the wrong place.
	/// </summary>
	[Fact]
	public void PublicSurface_MatchesTheTestData()
	{
		ConformanceTestData data = ConformanceTestData.Load();

		foreach (ConformanceCase item in data.Cases)
		{
			Assert.True(Naxp.TryParse(item.Naxp, out Naxp? naxp, out string? errorMessage), errorMessage);
			Assert.Equal((ulong)item.ValueCount, naxp.ValueCount);

			foreach (ConformanceValue value in item.Values)
			{
				Assert.Equal((ulong)value.Out, naxp.Encode(value.In));
				Assert.Equal(value.Canon ?? value.In, naxp.GetCanonicalForm(value.In));
				Assert.Equal(value.Canon ?? value.In, naxp.Decode((ulong)value.Out));
			}

			foreach (string text in item.NotAccepted)
			{
				Assert.Equal(0UL, naxp.Encode(text));
			}
		}
	}

	[Fact]
	public void PublicSurface_RefusesEveryRejection()
	{
		ConformanceTestData data = ConformanceTestData.Load();

		foreach (ConformanceRejection item in data.Rejected)
		{
			Assert.False(
				Naxp.TryParse(item.Naxp, out _, out _),
				$"{item.Naxp} should have been refused for {item.Rule}.");
		}
	}
	#endregion
	#region Private helpers
	/// <summary>
	/// UK postcodes, as <see cref="CodecTests"/> uses them. The <c>\s!!</c> is what makes the
	/// spelling without the space encode alike and canonicalise to the spelling with it.
	/// </summary>
	const string Postcode = "\\A \\A? \\9 \\X? \\s!! \\9 \\A \\A";

	static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
	#endregion
}
