// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LogMu;

/// <summary>
/// An immutable set of ASCII characters, that is of characters in the range U+0000 to U+007F.
/// </summary>
/// <remarks>
/// <para>
/// The set is held as 128 bits in two <see cref="ulong"/> fields rather than as a
/// <c>UInt128</c>, because <c>UInt128</c> does not exist on netstandard2.0 and because
/// two words are no slower: on x64 most <c>UInt128</c> operations are emulated anyway.
/// </para>
/// <para>
/// Every shift below masks its count explicitly. C# masks a <see cref="ulong"/> shift count
/// by 63, so the obvious two word shift is wrong at counts of 0 and 64, and silently so.
/// </para>
/// <para>
/// Internal rather than public. Nothing on <see cref="Naxp"/> exposes a character set, so making
/// this public would commit the package to thirty odd members that no caller can reach. If the
/// source generator turns out to need it in the code it emits, widening it then is not a breaking
/// change, whereas narrowing it later would be.
/// </para>
/// </remarks>
internal readonly struct AsciiCharSet : IEquatable<AsciiCharSet>, IComparable<AsciiCharSet>, IEnumerable<char>
{
	#region Private data
	/// <summary>Characters U+0000 to U+003F, one per bit, least significant bit first.</summary>
	readonly ulong bitsLow;
	/// <summary>Characters U+0040 to U+007F, one per bit, least significant bit first.</summary>
	readonly ulong bitsHigh;
	#endregion
	#region Private ctors
	/// <summary>Constructs an <see cref="AsciiCharSet"/> from its two words.</summary>
	/// <param name="bitsLow">Characters U+0000 to U+003F.</param>
	/// <param name="bitsHigh">Characters U+0040 to U+007F.</param>
	AsciiCharSet(ulong bitsLow, ulong bitsHigh)
	{
		this.bitsLow = bitsLow;
		this.bitsHigh = bitsHigh;
	}
	#endregion
	#region Public constants
	/// <summary>
	/// The number of characters that can be held, that is 128.
	/// </summary>
	public const int CharacterCount = 128;
	#endregion
	#region Public factory methods
	/// <summary>
	/// The empty set.
	/// </summary>
	public static AsciiCharSet Empty => default;
	/// <summary>
	/// Creates the set containing the single character <paramref name="c"/>.
	/// </summary>
	/// <param name="c">The single character in the set. Must be ASCII.</param>
	/// <returns>The set containing just <paramref name="c"/>.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="c"/> is not ASCII.</exception>
	public static AsciiCharSet FromSingleChar(char c)
	{
		if (c >= CharacterCount) { ThrowNotAscii(nameof(c)); }

		int index = c;
		return index < 64
			? new AsciiCharSet(1UL << index, 0UL)
			: new AsciiCharSet(0UL, 1UL << (index - 64))
			;
	}
	/// <summary>
	/// Creates the set containing the inclusive character range
	/// [<paramref name="cMin"/>,<paramref name="cMax"/>].
	/// </summary>
	/// <param name="cMin">The first character in the range. Must be ASCII.</param>
	/// <param name="cMax">The last character in the range. Must be ASCII and not less than <paramref name="cMin"/>.</param>
	/// <returns>The set containing the range.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// A bound is not ASCII, or <paramref name="cMin"/> is greater than <paramref name="cMax"/>.
	/// </exception>
	public static AsciiCharSet FromCharRange(char cMin, char cMax)
	{
		if (cMin >= CharacterCount) { ThrowNotAscii(nameof(cMin)); }
		if (cMax >= CharacterCount) { ThrowNotAscii(nameof(cMax)); }
		if (cMin > cMax)
		{
			throw new ArgumentOutOfRangeException(nameof(cMin), $"{nameof(cMin)} cannot be greater than {nameof(cMax)}.");
		}

		int min = cMin;
		int max = cMax;

		ulong bitsLow = min < 64 ? MaskRange(min, (max < 64) ? max : 63) : 0UL;
		ulong bitsHigh = max >= 64 ? MaskRange((min > 64) ? (min - 64) : 0, max - 64) : 0UL;

		return new AsciiCharSet(bitsLow, bitsHigh);
	}
	#endregion
	#region Public properties and methods
	/// <summary>
	/// Whether the set is empty.
	/// </summary>
	public bool IsEmpty => (this.bitsLow | this.bitsHigh) == 0UL;
	/// <summary>
	/// The number of characters in the set, in the range 0 to 128.
	/// </summary>
	public int Count => Bits.PopCount(this.bitsLow) + Bits.PopCount(this.bitsHigh);
	/// <summary>
	/// If the set holds exactly one character then that character, otherwise <see langword="null"/>.
	/// </summary>
	public char? SingleCharacter => this.Count == 1 ? (char)this.FirstCharacterCode() : (char?)null;
	/// <summary>
	/// Whether the set contains the specified character.
	/// </summary>
	/// <param name="c">The character to test for membership.</param>
	/// <returns>Whether the set contains <paramref name="c"/>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(char c)
	{
		if (c >= CharacterCount) { return false; }

		int index = c;
		ulong word = index < 64 ? this.bitsLow : this.bitsHigh;
		return ((word >> (index & 63)) & 1UL) != 0UL;
	}
	/// <summary>
	/// The zero based position of <paramref name="c"/> among the characters of the set taken
	/// in ascending order, or <c>-1</c> if the set does not contain it.
	/// </summary>
	/// <param name="c">The character whose position is wanted.</param>
	/// <returns>The position, or <c>-1</c>.</returns>
	public int IndexOf(char c)
	{
		if (!this.Contains(c)) { return -1; }

		int index = c;
		return index < 64
			? Bits.PopCount(this.bitsLow & MaskBelow(index))
			: Bits.PopCount(this.bitsLow) + Bits.PopCount(this.bitsHigh & MaskBelow(index - 64))
			;
	}
	/// <summary>
	/// The character at <paramref name="index"/> among the characters of the set taken in
	/// ascending order. The inverse of <see cref="IndexOf"/>.
	/// </summary>
	/// <param name="index">The position wanted, from zero to one less than <see cref="Count"/>.</param>
	/// <returns>The character at that position.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The set holds no character at that position.</exception>
	public char CharacterAt(int index)
	{
		if (index < 0) { throw new ArgumentOutOfRangeException(nameof(index), $"{nameof(index)} cannot be negative."); }

		int inLowWord = Bits.PopCount(this.bitsLow);

		if (index < inLowWord) { return (char)SetBitAt(this.bitsLow, index); }

		int remaining = index - inLowWord;

		if (remaining >= Bits.PopCount(this.bitsHigh))
		{
			throw new ArgumentOutOfRangeException(nameof(index), $"The set holds no character at position {index}.");
		}

		return (char)(64 + SetBitAt(this.bitsHigh, remaining));
	}
	/// <summary>
	/// Whether this set has any character in common with <paramref name="other"/>.
	/// </summary>
	/// <param name="other">The other set.</param>
	/// <returns>Whether the two sets intersect.</returns>
	public bool IntersectsWith(AsciiCharSet other)
		=> ((this.bitsLow & other.bitsLow) | (this.bitsHigh & other.bitsHigh)) != 0UL;
	/// <summary>
	/// The three disjoint combinations of this set and <paramref name="other"/>, in the order
	/// intersection, this less other, other less this. Any of them may be empty.
	/// </summary>
	/// <param name="other">The set to combine with this one.</param>
	/// <returns>The three disjoint combinations.</returns>
	public (AsciiCharSet intersection, AsciiCharSet thisLessOther, AsciiCharSet otherLessThis) GetDisjointCombinations(AsciiCharSet other)
		=> (this & other, this - other, other - this);
	/// <inheritdoc/>
	public bool Equals(AsciiCharSet other)
		=> this.bitsLow == other.bitsLow && this.bitsHigh == other.bitsHigh;
	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is AsciiCharSet other && this.Equals(other);
	/// <summary>
	/// Compares two sets in the order they would take if each were written out as the string of
	/// its characters in ascending order and the strings compared ordinally. So
	/// <c>[a]</c> &lt; <c>[ab]</c> &lt; <c>[abc]</c> &lt; <c>[ac]</c> &lt; <c>[b]</c>.
	/// </summary>
	/// <param name="other">The set to compare with this one.</param>
	/// <returns>A negative number, zero, or a positive number.</returns>
	public int CompareTo(AsciiCharSet other)
	{
		if (this.Equals(other)) { return 0; }

		// The lowest character at which the two sets differ. It exists, because they are not equal.
		int firstDifference = FirstSetBit(this.bitsLow ^ other.bitsLow, this.bitsHigh ^ other.bitsHigh);

		// Both sets agree below that character, so the comparison is settled by the next character
		// each of them holds at or above it. One of the two holds the differing character itself.
		int nextInThis = this.FirstCharacterCodeAtOrAbove(firstDifference);
		int nextInOther = other.FirstCharacterCodeAtOrAbove(firstDifference);

		// A set with nothing left is a prefix of the other, and a prefix sorts first.
		if (nextInThis == CharacterCount) { return -1; }
		if (nextInOther == CharacterCount) { return 1; }

		return nextInThis - nextInOther;
	}
	/// <inheritdoc/>
	public override int GetHashCode()
	{
		ulong mixed = this.bitsLow ^ (this.bitsHigh * 0x9E3779B97F4A7C15UL);
		return (int)(mixed ^ (mixed >> 32));
	}
	/// <summary>
	/// Gets an enumerator over the characters of the set in ascending order.
	/// </summary>
	/// <returns>The enumerator.</returns>
	public Enumerator GetEnumerator() => new Enumerator(this);
	/// <inheritdoc/>
	IEnumerator<char> IEnumerable<char>.GetEnumerator() => this.GetEnumerator();
	/// <inheritdoc/>
	IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
	#endregion
	#region Public operators
	/// <summary>Union.</summary>
	/// <param name="left">Left operand.</param>
	/// <param name="right">Right operand.</param>
	/// <returns>The characters in either set.</returns>
	public static AsciiCharSet operator |(AsciiCharSet left, AsciiCharSet right)
		=> new AsciiCharSet(left.bitsLow | right.bitsLow, left.bitsHigh | right.bitsHigh);
	/// <summary>Intersection.</summary>
	/// <param name="left">Left operand.</param>
	/// <param name="right">Right operand.</param>
	/// <returns>The characters in both sets.</returns>
	public static AsciiCharSet operator &(AsciiCharSet left, AsciiCharSet right)
		=> new AsciiCharSet(left.bitsLow & right.bitsLow, left.bitsHigh & right.bitsHigh);
	/// <summary>Set difference.</summary>
	/// <param name="left">Left operand.</param>
	/// <param name="right">Right operand.</param>
	/// <returns>The characters in <paramref name="left"/> but not in <paramref name="right"/>.</returns>
	public static AsciiCharSet operator -(AsciiCharSet left, AsciiCharSet right)
		=> new AsciiCharSet(left.bitsLow & ~right.bitsLow, left.bitsHigh & ~right.bitsHigh);
	/// <summary>Equality.</summary>
	/// <param name="left">Left operand.</param>
	/// <param name="right">Right operand.</param>
	/// <returns>Whether the two sets hold the same characters.</returns>
	public static bool operator ==(AsciiCharSet left, AsciiCharSet right) => left.Equals(right);
	/// <summary>Inequality.</summary>
	/// <param name="left">Left operand.</param>
	/// <param name="right">Right operand.</param>
	/// <returns>Whether the two sets differ.</returns>
	public static bool operator !=(AsciiCharSet left, AsciiCharSet right) => !left.Equals(right);
	#endregion
	#region Public static data
	/// <summary>The digits <c>0</c> to <c>9</c>, written <c>\9</c> in a naxp.</summary>
	public static readonly AsciiCharSet AllDigits = FromCharRange('0', '9');
	/// <summary>The letters <c>A</c> to <c>Z</c>, written <c>\A</c> in a naxp.</summary>
	public static readonly AsciiCharSet AllUpperCaseLetters = FromCharRange('A', 'Z');
	/// <summary>The letters <c>a</c> to <c>z</c>, written <c>\a</c> in a naxp.</summary>
	public static readonly AsciiCharSet AllLowerCaseLetters = FromCharRange('a', 'z');
	/// <summary>The digits and the upper case letters, written <c>\X</c> in a naxp.</summary>
	public static readonly AsciiCharSet AllDigitsAndUpperCaseLetters = AllDigits | AllUpperCaseLetters;
	#endregion
	#region Private helper methods
	/// <summary>
	/// The bits from <paramref name="from"/> to <paramref name="to"/> inclusive, within one word.
	/// </summary>
	/// <param name="from">The lowest bit to set, in the range 0 to 63.</param>
	/// <param name="to">The highest bit to set, in the range <paramref name="from"/> to 63.</param>
	/// <returns>The mask.</returns>
	static ulong MaskRange(int from, int to)
	{
		// A shift count of 64 would be masked to 0 and set every bit, so the top of the
		// range is special cased rather than written as ((1UL << (to + 1)) - 1UL).
		ulong maskFrom = ulong.MaxValue << from;
		ulong maskTo = to == 63 ? ulong.MaxValue : ((1UL << (to + 1)) - 1UL);
		return maskFrom & maskTo;
	}
	/// <summary>
	/// The bits below <paramref name="index"/>, within one word.
	/// </summary>
	/// <param name="index">The bit below which to set, in the range 0 to 63.</param>
	/// <returns>The mask.</returns>
	static ulong MaskBelow(int index)
		// A shift count of 64 would be masked to 0 and set every bit, so zero is special cased.
		=> index == 0 ? 0UL : (ulong.MaxValue >> (64 - index));
	/// <summary>
	/// The position of the <paramref name="index"/>th set bit of a word, counting from zero.
	/// </summary>
	/// <param name="word">The word, which must hold more than <paramref name="index"/> set bits.</param>
	/// <param name="index">How many set bits to skip.</param>
	/// <returns>The position, in the range 0 to 63.</returns>
	static int SetBitAt(ulong word, int index)
	{
		// Clearing the lowest set bit is one instruction, and the index is at most 63.
		for (int i = 0; i < index; ++i) { word &= word - 1UL; }

		return Bits.TrailingZeroCount(word);
	}
	/// <summary>
	/// The position of the lowest set bit across the two words, or 128 if both are zero.
	/// </summary>
	/// <param name="bitsLow">The low word.</param>
	/// <param name="bitsHigh">The high word.</param>
	/// <returns>The position, in the range 0 to 128.</returns>
	static int FirstSetBit(ulong bitsLow, ulong bitsHigh)
	{
		if (bitsLow != 0UL) { return Bits.TrailingZeroCount(bitsLow); }
		if (bitsHigh != 0UL) { return 64 + Bits.TrailingZeroCount(bitsHigh); }
		return CharacterCount;
	}
	/// <summary>
	/// The lowest character in the set, or 128 if it is empty.
	/// </summary>
	/// <returns>The character code, in the range 0 to 128.</returns>
	int FirstCharacterCode() => FirstSetBit(this.bitsLow, this.bitsHigh);
	/// <summary>
	/// The lowest character in the set that is not below <paramref name="index"/>,
	/// or 128 if there is none.
	/// </summary>
	/// <param name="index">The character code at or above which to look, in the range 0 to 127.</param>
	/// <returns>The character code, in the range 0 to 128.</returns>
	int FirstCharacterCodeAtOrAbove(int index)
	{
		ulong bitsLow = index < 64 ? (this.bitsLow & ~MaskBelow(index)) : 0UL;
		ulong bitsHigh = index < 64 ? this.bitsHigh : (this.bitsHigh & ~MaskBelow(index - 64));
		return FirstSetBit(bitsLow, bitsHigh);
	}
	static void ThrowNotAscii(string parameterName)
		=> throw new ArgumentOutOfRangeException(parameterName, $"{parameterName} must be an ASCII character, that is below U+0080.");
	#endregion
	#region Enumerator struct
	/// <summary>
	/// Enumerates the characters of an <see cref="AsciiCharSet"/> in ascending order.
	/// </summary>
	public struct Enumerator : IEnumerator<char>
	{
		AsciiCharSet remaining;
		char current;

		internal Enumerator(AsciiCharSet charSet)
		{
			this.remaining = charSet;
			this.current = default;
		}

		/// <inheritdoc/>
		public bool MoveNext()
		{
			int next = this.remaining.FirstCharacterCode();
			if (next == CharacterCount) { return false; }

			this.current = (char)next;
			this.remaining -= FromSingleChar(this.current);
			return true;
		}

		/// <inheritdoc/>
		public readonly char Current => this.current;

		/// <inheritdoc/>
		readonly object IEnumerator.Current => this.Current;

		/// <inheritdoc/>
		public void Reset() => throw new NotSupportedException();

		/// <inheritdoc/>
		public readonly void Dispose() { }
	}
	#endregion
}
