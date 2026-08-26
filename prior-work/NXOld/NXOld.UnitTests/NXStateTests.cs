// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using Naxp.NXComponents;
using Xunit;

namespace Naxp.UnitTests;

public sealed class NXStateTests
{
	[Fact]
	public void TestNXState_UKPostcode()
	{
		// \A\A?\9\X? \s \9\A\A
		//
		// s0 :
		//     \A → s1
		// s1 :
		//     \9 → s3
		//     \A → s2
		// s2 :
		//     \9 → s3
		// s3 :
		//     \s → s5
		//     \X → s4
		// s4 :
		//     \s → s5
		// s5 :
		//     \9 → s6
		// s6 :
		//     \A → s7
		// s7 :
		//     \A → s8
		// s8 :
		//     EOT → null
		var s8 = State.DefinitiveEndOfText;
		var count8 = 1ul;

		var t7_8 = new Transition(AsciiCharSet.Parse("\\A"), s8);
		var s7 = new State([t7_8]);
		var count7 = 26 * count8;

		var t6_7 = new Transition(AsciiCharSet.Parse("\\A"), s7);
		var s6 = new State([t6_7]);
		var count6 = 26 * count7;

		var t5_6 = new Transition(AsciiCharSet.Parse("\\9"), s6);
		var s5 = new State([t5_6]);
		var count5 = 10 * count6;

		var t4_5 = new Transition(AsciiCharSet.Parse("\\s"), s5);
		var s4 = new State([t4_5]);
		var count4 = 1 * count5;

		var t3_5 = new Transition(AsciiCharSet.Parse("\\s"), s5);
		var t3_4 = new Transition(AsciiCharSet.Parse("\\X"), s4);
		var s3 = new State([t3_5, t3_4]);
		var count3 = 1 * count5 + 36 * count4;

		var t2_3 = new Transition(AsciiCharSet.Parse("\\9"), s3);
		var s2 = new State([t2_3]);
		var count2 = 10 * count3;

		var t1_3 = new Transition(AsciiCharSet.Parse("\\9"), s3);
		var t1_2 = new Transition(AsciiCharSet.Parse("\\A"), s2);
		var s1 = new State([t1_3, t1_2]);
		var count1 = 10 * count3 + 26 * count2;

		var t0_1 = new Transition(AsciiCharSet.Parse("\\A"), s1);
		var s0 = new State([t0_1]);
		var count0 = 26 * count1;

		var stateData = new[]
		{
			(state: s8, characterCombinationCount: count8, minLength: 0, maxLength: 0
                // (terminal)
                , accepts: new string[] { "", }
				, rejects: new string[] { " ", "A", "1", "\u0000", "\u007F", }
			),
			(state: s7, characterCombinationCount: count7, minLength: 1, maxLength: 1
                //\A
                , accepts: new string[] { "A", "M", "Z", }
				, rejects: new string[] { "", " ", "a", "1", "_", }
			),
			(state: s6, characterCombinationCount: count6, minLength: 2, maxLength: 2
                // \A\A
                , accepts: new string[] { "BC", "EF", "YZ", }
				, rejects: new string[] { "", " ", "A", "1C", "E5", "hI", "34", }
			),
			(state: s5, characterCombinationCount: count5, minLength: 3, maxLength: 3
                // \9\A\A
                , accepts: new string[] { "1BC", "3EF", "6HI", "9LM", "1QR", "3YZ", }
				, rejects: new string[] { "", " ", "A", "01C", "3E5", "6hI", " 9L1", "1Q", "34Z", }
			),
			(state: s4, characterCombinationCount: count4, minLength: 4, maxLength: 4
                // \s \9\A\A
                , accepts: new string[] { " 1BC", " 3EF", " 6HI", " 9LM", " 1QR", " 3YZ", }
				, rejects: new string[] { "", " ", "A", "01BC", "2D 3EF", " 6hI", "z 9L1", " 1Q", " 34Z", }
			),
			(state: s3, characterCombinationCount: count3, minLength: 4, maxLength: 5
                //\X? \s \9\A\A
                , accepts: new string[] { " 1BC", " 3EF", "5 6HI", "8 9LM", "P 1QR", "U 3YZ", }
				, rejects: new string[] { "", " ", "A", "01BC", "2D 3EF", "G 6hI", "z 9L1", "P 1Q", "UU 3YZ", }
			),
			(state: s2, characterCombinationCount: count2, minLength: 5, maxLength: 6
                //\9\X? \s \9\A\A
                , accepts: new string[] { "0 1BC", "2 3EF", "45 6HI", "78 9LM", "0P 1QR", "2U 3YZ", }
				, rejects: new string[] { "", " ", "A", "01BC", "2D2 3EF", "G5 6hI", "K8 9L1", "P 1Q", "T2UU 3YZ", }
			),
			(state: s1, characterCombinationCount: count1, minLength: 5, maxLength: 7
                //\A?\9\X? \s \9\A\A
                , accepts: new string[] { "0 1BC", "D2 3EF", "45 6HI", "K78 9LM", "0P 1QR", "T2U 3YZ", }
				, rejects: new string[] { "", " ", "A", "01BC", "2D2 3EF", "G5 6hI", "K78 9L1", "0P 1Q", "T2UU 3YZ", }
			),
			(state: s0, characterCombinationCount: count0, minLength: 6, maxLength: 8
                // \A\A?\9\X? \s \9\A\A
                , accepts: new string[] { "A0 1BC", "CD2 3EF", "G45 6HI", "JK78 9LM", "N0P 1QR", "ST2U 3YZ", }
				, rejects: new string[] { "", " ", "A", "A01BC", "2D2 3EF", "G45 6hI", "JK78 9L1", "N0P 1Q", "ST2UU 3YZ", }
			),
		};

		foreach (var (state, characterCombinationCount, minLength, maxLength, accepts, rejects) in stateData)
		{
			Assert.Equal(state.CharacterCombinationCount, characterCombinationCount);
			Assert.Equal(state.MinLength, minLength);
			Assert.Equal(state.MaxLength, maxLength);

			foreach (var text in accepts)
			{
				Assert.True(state.Accepts(text));
				Assert.True(state.Accepts(ConvertToBytes(text)));
			}
			foreach (var text in rejects)
			{
				Assert.False(state.Accepts(text));
				Assert.False(state.Accepts(ConvertToBytes(text)));
			}
		}
	}

	static byte[] ConvertToBytes(ReadOnlySpan<char> chars)
	{
		var bytes = new byte[chars.Length];
		for (int i = 0; i < bytes.Length; ++i)
		{
			bytes[i] = (byte)Math.Min((uint)chars[i], 0xFF);
		}
		return bytes;
	}
}