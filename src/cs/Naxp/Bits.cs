// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Numerics;
#endif

namespace LogMu;

/// <summary>
/// Bit primitives over <see cref="ulong"/>.
/// <para>
/// On net8.0 these map to <c>System.Numerics.BitOperations</c>, and so to hardware instructions.
/// netstandard2.0 has no <c>BitOperations</c>, so each method carries a software version in its
/// <c>#else</c> branch. Only one of the two is compiled for a given target, so the software
/// version is covered by the net472 test run, which loads the netstandard2.0 build.
/// </para>
/// </summary>
static class Bits
{
	/// <summary>
	/// The number of set bits in <paramref name="value"/>.
	/// </summary>
	/// <param name="value">The value whose set bits are to be counted.</param>
	/// <returns>The number of set bits, in the range 0 to 64.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int PopCount(ulong value)
	{
#if NET8_0_OR_GREATER
		return BitOperations.PopCount(value);
#else
		// The usual SWAR sum: pairs, then nibbles, then a multiply that sums the bytes into the top byte.
		value -= (value >> 1) & 0x5555555555555555UL;
		value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
		value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
		return (int)((value * 0x0101010101010101UL) >> 56);
#endif
	}
	/// <summary>
	/// The number of zero bits below the least significant set bit of <paramref name="value"/>,
	/// or 64 if <paramref name="value"/> is zero.
	/// </summary>
	/// <param name="value">The value to examine.</param>
	/// <returns>The trailing zero count, in the range 0 to 64.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int TrailingZeroCount(ulong value)
	{
#if NET8_0_OR_GREATER
		return BitOperations.TrailingZeroCount(value);
#else
		if (value == 0UL) { return 64; }

		// Isolating the lowest set bit leaves a single bit, and one less than that
		// is a run of exactly as many ones as there were trailing zeros.
		return PopCount((value & (0UL - value)) - 1UL);
#endif
	}
}
