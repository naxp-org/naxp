// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using Xunit;

namespace LogMu.UnitTests;

public class BitsTests
{
	#region Reference methods for comparison
	static int AltPopCount(ulong value)
	{
		int count = 0;
		for (int i = 0; i < 64; ++i)
		{
			if ((value & 1) != 0) { ++count; }
			value >>= 1;
		}
		return count;
	}
	static int AltTrailingZeroCount(ulong value)
	{
		for (int count = 0; count < 64; ++count)
		{
			if ((value & 1) != 0) { return count; }
			value >>= 1;
		}

		return 64;
	}
	#endregion

	/// <summary>
	/// Both primitives must agree with the plain loops above. Which implementation that checks
	/// depends on the target: net8.0 exercises the <c>BitOperations</c> path, and net472 loads the
	/// netstandard2.0 build of Naxp and so exercises the software path. Dropping net472 from this
	/// project would leave the software path untested.
	/// </summary>
	[Fact]
	public void Bits_Primitives()
	{
		foreach (var value in BitPatterns())
		{
			Assert.Equal(AltPopCount(value), Bits.PopCount(value));
			Assert.Equal(AltTrailingZeroCount(value), Bits.TrailingZeroCount(value));
		}
	}

	[Fact]
	public void TrailingZeroCount_OfZero_Is64()
	{
		Assert.Equal(64, Bits.TrailingZeroCount(0UL));
	}

	static IEnumerable<ulong> BitPatterns()
	{
		yield return 0UL;
		yield return ulong.MaxValue;

		// Every single bit, and every pair of adjacent bits, so that the word boundaries are hit.
		for (int i = 0; i < 64; ++i)
		{
			yield return 1UL << i;
			yield return ulong.MaxValue << i;
			yield return ulong.MaxValue >> i;
		}

		var random = new Random(20260810);
		for (int i = 0; i < 2000; ++i)
		{
			var bytes = new byte[8];
			random.NextBytes(bytes);
			yield return BitConverter.ToUInt64(bytes, 0);
		}
	}
}
