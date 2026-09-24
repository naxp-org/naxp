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

		Assert.Equal("AB|B", naxp.Pattern);
		Assert.Equal("AB|B", naxp.ToString());
	}

	/// <summary>
	/// Whitespace between tokens is ignored, so it survives in the pattern without reaching the
	/// language.
	/// </summary>
	[Fact]
	public void Source_IsWhatWasWrittenRatherThanWhatWasMeant()
	{
		Naxp naxp = Naxp.Parse("A | B");

		Assert.Equal("A | B", naxp.Pattern);
		Assert.Equal(2UL, naxp.MaxEncodedValue);
	}

	[Fact]
	public void Parse_TakesAStringWithoutCeremony()
	{
		// The point of the implicit conversion: a caller holding a string writes nothing extra.
		string pattern = "[A-Z]";

		Assert.Equal(26UL, Naxp.Parse(pattern).MaxEncodedValue);
	}

	[Fact]
	public void Parse_TakesASlice()
	{
		Naxp naxp = Naxp.Parse("xx[A-Z]yy".AsSpan(2, 5));

		Assert.Equal("[A-Z]", naxp.Pattern);
		Assert.Equal(26UL, naxp.MaxEncodedValue);
	}

	/// <summary>
	/// One per rule, so a fault that arrives for the wrong reason is caught here rather than
	/// merely counted as a fault. <c>A{2,5}</c> is a syntax error and not W4: the comma is the
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
	public void Parse_ThrowsOnAnIllFormedNaxp(string pattern, string expectedRule)
	{
		FormatException exception = Assert.Throws<FormatException>(() => Naxp.Parse(pattern));

		Assert.False(
			Naxp.TryParse(pattern, out Naxp? naxp, out _, out _, out _, out string? errorCode));
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
		const string Pattern = "A{5,2}";

		Assert.False(Naxp.TryParse(
			Pattern,
			out Naxp? naxp,
			out string? errorMessage,
			out int errorOffset,
			out int errorLength,
			out string? errorCode));

		Assert.Null(naxp);
		Assert.Equal("NAXP1007", errorCode);
		Assert.Equal("The first count of an interval cannot exceed the second.", errorMessage);

		// The span covers '{5,2}', which is the interval and not the whole naxp.
		Assert.Equal("{5,2}", Pattern.Substring(errorOffset, errorLength));
	}

	/// <summary>
	/// A fault that belongs to no one place in the naxp reports the whole of it, so that a
	/// caller underlining the span never points at an innocent first character.
	/// </summary>
	[Fact]
	public void TryParse_OfAFaultWithNoPosition_ReportsTheWholeNaxp()
	{
		const string Pattern = "\\9{20}";

		Assert.False(Naxp.TryParse(
			Pattern,
			out Naxp? naxp,
			out _,
			out int errorOffset,
			out int errorLength,
			out string? errorCode));

		Assert.Null(naxp);
		Assert.Equal("NAXP1046", errorCode);
		Assert.Equal(0, errorOffset);
		Assert.Equal(Pattern.Length, errorLength);
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
		// The eighteen of the specification's table, in its order.
		const string Reserved = "!#(),-?[\\]{|}*+.^$";

		Assert.Equal(18, Reserved.Length);

		foreach (char c in Reserved)
		{
			var naxp = Naxp.Parse("\\" + c);

			Assert.Equal(1UL, naxp.MaxEncodedValue);
			Assert.True(naxp.Accepts(c.ToString()), $"'\\{c}' does not match '{c}'.");
		}
	}

	/// <summary>
	/// And a character outside that set stands for itself, with no escape needed.
	/// </summary>
	[Fact]
	public void ACharacterOutsideTheReservedSet_StandsForItself()
	{
		foreach (char c in "\"%&'/:;<=>@_`~")
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
	/// The naxp breaks no other rule. It is W6 that rules it out, and the
	/// fault has to arrive through the same door as any other.
	/// </summary>
	[Fact]
	public void Parse_ThrowsOnANaxpOverBudget()
		// Legal under the grammar, which caps a count at two digits; 9 802 states.
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
				Assert.Fail($"{item.Naxp} was invalid: {errorMessage}");
				continue;
			}

			foreach (ConformanceValue value in item.Values)
			{
				Assert.True(naxp.Accepts(value.In), $"{item.Naxp} did not accept {value.In}.");
				++checks;
			}

			foreach (string text in item.Invalid)
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

		for (ulong value = 1UL; value <= naxp.MaxEncodedValue; ++value)
		{
			Assert.Equal(value, naxp.Encode(naxp.Decode(value)));
		}
	}

	[Theory]
	[InlineData(0UL)]
	[InlineData(27UL)]
	[InlineData((ulong)long.MaxValue)]
	[InlineData(ulong.MaxValue)]
	public void TryDecode_FailsForAValueOutsideTheRange(ulong value)
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
	/// A byte above 0x7E cannot be named by any naxp, so it is invalid rather than folded onto
	/// some character that can.
	/// </summary>
	[Fact]
	public void ByteOverloads_TreatAnythingOutsideAsciiAsInvalid()
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
		// Not [A-Z], which at this length has 26^300 values and is found invalid by W5. The count is
		// nested because an interval count is capped at two digits.
		Naxp naxp = Naxp.Parse("(Q{50}){6}");
		var text = new string('Q', 300);

		Assert.True(naxp.Accepts(Ascii(text)));
		Assert.Equal(naxp.Encode(text), naxp.Encode(Ascii(text)));

		Assert.False(naxp.Accepts(Ascii(new string('Q', 301))));
	}

	[Fact]
	public void ByteOverloads_TreatTextTooLongAsInvalid()
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
	[Fact]
	public void ByteOverloads_GiveTheCanonicalForm()
	{
		Naxp naxp = Naxp.Parse(Postcode);

		Assert.Equal("M1 1AA", naxp.GetCanonicalForm(Ascii("M11AA")));
		Assert.Null(naxp.GetCanonicalForm(Ascii("M11A")));
		Assert.True(naxp.TryGetCanonicalForm(Ascii("M11AA"), out string? canonicalForm));
		Assert.Equal("M1 1AA", canonicalForm);
		Assert.Null(naxp.GetCanonicalForm(new byte[NaxpLimits.MaxStringLength + 1]));
	}
	#endregion
	#region Longest length and writing into buffers
	[Theory]
	[InlineData(Postcode, 8)]
	[InlineData("A|BCD|EF", 3)]
	[InlineData("()", 0)]
	[InlineData("(AB|A)!A", 1)]
	public void MaxLength_IsTheLongestCanonicalString(string pattern, int expected)
		=> Assert.Equal(expected, Naxp.Parse(pattern).MaxLength);

	[Fact]
	public void DecodeToBytes_SpellsWhatDecodeGives()
	{
		Naxp naxp = Naxp.Parse(Postcode);

		Assert.Equal(Ascii("M1 1AA"), naxp.DecodeToBytes(810639597UL));
		Assert.Throws<ArgumentOutOfRangeException>(() => naxp.DecodeToBytes(0UL));
	}

	/// <summary>
	/// Every overload that writes into a buffer, against the test data: a buffer of
	/// <see cref="Naxp.MaxLength"/> always takes the answer, one of exactly its length does too, and
	/// one shorter fails with nothing written.
	/// </summary>
	[Fact]
	public void BufferOverloads_MatchTheTestData()
	{
		ConformanceTestData data = ConformanceTestData.Load();

		foreach (ConformanceCase item in data.Cases)
		{
			Naxp naxp = Naxp.Parse(item.Naxp);
			var chars = new char[naxp.MaxLength];
			var bytes = new byte[naxp.MaxLength];
			int written;

			foreach (ConformanceValue value in item.Values)
			{
				string canon = value.Canon ?? value.In;

				Assert.True(canon.Length <= naxp.MaxLength, $"{item.Naxp}: '{canon}' is longer than MaxLength.");

				Assert.True(naxp.TryDecode((ulong)value.Out, chars, out written));
				Assert.Equal(canon, new string(chars, 0, written));
				Assert.True(naxp.TryDecode((ulong)value.Out, bytes, out written));
				Assert.Equal(Ascii(canon), bytes.AsSpan(0, written).ToArray());
				Assert.True(naxp.TryDecode((ulong)value.Out, new char[canon.Length], out written));

				Assert.True(naxp.TryGetCanonicalForm(value.In, chars, out written));
				Assert.Equal(canon, new string(chars, 0, written));
				Assert.True(naxp.TryGetCanonicalForm(Ascii(value.In), bytes, out written));
				Assert.Equal(Ascii(canon), bytes.AsSpan(0, written).ToArray());
				Assert.Equal(canon, naxp.GetCanonicalForm(Ascii(value.In)));

				if (canon.Length == 0) { continue; }

				Assert.False(naxp.TryDecode((ulong)value.Out, new char[canon.Length - 1], out written));
				Assert.Equal(0, written);
				Assert.False(naxp.TryGetCanonicalForm(value.In, new char[canon.Length - 1], out written));
				Assert.Equal(0, written);
			}

			foreach (string text in item.Invalid)
			{
				Assert.False(naxp.TryGetCanonicalForm(text, chars, out written));
				Assert.Equal(0, written);
				Assert.False(naxp.TryGetCanonicalForm(Ascii(text), bytes, out written));
				Assert.Null(naxp.GetCanonicalForm(Ascii(text)));
			}

			Assert.False(naxp.TryDecode(0UL, chars, out written));
			Assert.False(naxp.TryDecode(0UL, bytes, out written));
		}
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
			Assert.Equal((ulong)item.MaxEncodedValue, naxp.MaxEncodedValue);

			foreach (ConformanceValue value in item.Values)
			{
				Assert.Equal((ulong)value.Out, naxp.Encode(value.In));
				Assert.Equal(value.Canon ?? value.In, naxp.GetCanonicalForm(value.In));
				Assert.Equal(value.Canon ?? value.In, naxp.Decode((ulong)value.Out));
			}

			foreach (string text in item.Invalid)
			{
				Assert.Equal(0UL, naxp.Encode(text));
			}
		}
	}

	[Fact]
	public void PublicSurface_ReportsEveryInvalidNaxp()
	{
		ConformanceTestData data = ConformanceTestData.Load();

		foreach (ConformanceInvalidNaxp item in data.InvalidNaxps)
		{
			Assert.False(
				Naxp.TryParse(item.Naxp, out _, out _),
				$"{item.Naxp} should have been invalid for {item.Rule}.");
		}
	}
	#endregion
	#region Code generation
	/// <summary>
	/// The one entry point reaches every emitter, and the language decides nothing else: the same
	/// naxp, the same prefix, two fragments in two languages.
	/// </summary>
	[Fact]
	public void Emit_ReachesEachLanguage()
	{
		Naxp naxp = Naxp.Parse("\\A\\9");

		Assert.Contains(
			"public const ulong PostcodeMaxEncodedValue = 260UL;",
			naxp.Emit(OutputLanguage.CSharp, "Postcode"),
			StringComparison.Ordinal);
		Assert.Contains(
			"const postcodeMaxEncodedValue = 260;",
			naxp.Emit(OutputLanguage.JavaScript, "Postcode"),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_RejectsALanguageThatIsNotOne()
	{
		Naxp naxp = Naxp.Parse("A");

		Assert.Throws<ArgumentException>(() => naxp.Emit((OutputLanguage)99));
	}

	/// <summary>
	/// The line ending is the caller's, not the platform's, so one naxp gives the same fragment
	/// on every machine.
	/// </summary>
	[Fact]
	public void Emit_EndsLinesTheWayItIsAsked()
	{
		Naxp naxp = Naxp.Parse("A|B");

		Assert.DoesNotContain("\r", naxp.Emit(OutputLanguage.CSharp), StringComparison.Ordinal);
		Assert.DoesNotContain("\r", naxp.Emit(OutputLanguage.JavaScript), StringComparison.Ordinal);

		string crlf = naxp.Emit(OutputLanguage.CSharp, newLine: "\r\n");

		Assert.DoesNotContain("\n", crlf.Replace("\r\n", string.Empty), StringComparison.Ordinal);
		Assert.Equal(
			naxp.Emit(OutputLanguage.CSharp),
			crlf.Replace("\r\n", "\n"));
	}

	[Fact]
	public void Emit_IndentsTheWayItIsAsked()
	{
		string source = Naxp.Parse("A|B").Emit(OutputLanguage.CSharp, indent: "    ");

		Assert.Contains("\n    int state = 0;", source, StringComparison.Ordinal);
		Assert.DoesNotContain("\t", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_ThrowsOnANullFormattingString()
	{
		Naxp naxp = Naxp.Parse("A");

		Assert.Throws<ArgumentNullException>(() => naxp.Emit(OutputLanguage.CSharp, newLine: null!));
		Assert.Throws<ArgumentNullException>(() => naxp.Emit(OutputLanguage.CSharp, indent: null!));
		Assert.Throws<ArgumentNullException>(() => naxp.Emit(OutputLanguage.CSharp, initialIndent: null!));
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
