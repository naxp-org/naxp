// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using NXOld.NXComponents;

namespace NXOld;

/// <summary>
/// ASCII character map.
/// </summary>
public readonly struct AsciiCharSet : IEquatable<AsciiCharSet>, IComparable<AsciiCharSet>, IEnumerable<char>, IBinarySerializable<AsciiCharSet>
{
	#region Private data
	readonly UInt128 bits;
	#endregion
	#region Private ctors / d-ctors
	/// <summary>Constructs a <see cref="AsciiCharSet"/>.</summary>
	/// <param name="bits">The underlying 128 bit representation.</param>
	AsciiCharSet(UInt128 bits)
	{
		this.bits = bits;
	}
	#endregion
	#region Public factory methods
	/// <summary>
	/// Create the char set for the single character <paramref name="c"/>.
	/// </summary>
	/// <param name="c">The single character in the char set.</param>
	/// <returns>The char set comprising the single char <paramref name="c"/>.</returns>
	public static AsciiCharSet FromSingleChar(char c)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(c, 0x80, nameof(c));

		return new AsciiCharSet(UInt128.One << c);
	}
	/// <summary>
	/// Create the char set for the character range [<paramref name="cMin"/>,<paramref name="cMax"/>].
	/// </summary>
	/// <param name="cMin">The first character in the character range.</param>
	/// <param name="cMax">The last character in the character range.</param>
	/// <returns>The char set comprising the character range [<paramref name="cMin"/>,<paramref name="cMax"/>].</returns>
	public static AsciiCharSet FromCharRange(char cMin, char cMax)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cMin, 0x80, nameof(cMin));
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cMax, 0x80, nameof(cMax));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(cMin, cMax, $"{nameof(cMin)} cannot be greater than {nameof(cMax)}.");

		int k = 127 - cMax;

		UInt128 bitsLeft = UInt128.MaxValue << k + cMin;

		return new AsciiCharSet(bitsLeft >> k);
	}
	/// <summary>
	/// Creates an <see cref="AsciiCharSet"/> by parsing the specfied text in NX syntax, e.g. "A", "[ABU-Z]", "\X" etc.
	/// </summary>
	/// <param name="text">The text specifying the char set in NX syntax.</param>
	/// <returns>The <see cref="AsciiCharSet"/>.</returns>
	public static AsciiCharSet Parse(ReadOnlySpan<char> text)
		=> TryParse(text, out var charSet, out string? errorMessage, out int errorOffset)
			? charSet
			: throw new ArgumentOutOfRangeException(nameof(text), $"Error at offset {errorOffset}: {errorMessage}")
			;
	/// <summary>
	/// Tries to parse the specfied text as an <see cref="AsciiCharSet"/> 
	/// in NX syntax, e.g. "A", "[ABU-Z]", "\X" etc, 
	/// or reports the error if the text is invalid.
	/// </summary>
	/// <param name="text">The text specifying the char set in NX syntax.</param>
	/// <param name="charSet">The created <see cref="AsciiCharSet"/> (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <param name="errorOffset">
	/// The (zero-based) offset to the position of the error in <paramref name="text"/>
	/// (if the methods returns <see langword="false"/>).
	/// </param>
	/// <returns>Whether the parse succeeeded.</returns>
	public static bool TryParse(ReadOnlySpan<char> text
		, out AsciiCharSet charSet
		, [NotNullWhen(false)] out string? errorMessage
		, out int errorOffset
		)
		=> Parser.TryParseChars(text, out charSet, out errorMessage, out errorOffset);
	#endregion
	#region Public properties and methods
	/// <summary>
	/// Whether this char set is empty.
	/// </summary>
	public bool IsEmpty => this.bits == 0;
	/// <summary>
	/// The number of characters in this char set.
	/// <para>By definition, this is in the range 0 to 128 inclusive.</para>
	/// </summary>
	public int Count => (int)UInt128.PopCount(this.bits);
	/// <summary>
	/// Whether <see langword="this"/> contains the specified character.
	/// </summary>
	/// <param name="c">The character to test for membership.</param>
	/// <returns>Whether <see langword="this"/> contains <paramref name="c"/>.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Contains(char c)
	{
		// C# operator left shift masks the shift count by 1 − number of bits.
		// For built-in types, see https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators#shift-count-of-the-shift-operators.
		// By design, similar behaviour applies to UInt128. Specifically, the left shift count is masked using &= 0x7F.
		return c < '\u0080' && (this.bits & UInt128.One << c) != 0;
	}
	/// <summary>
	/// If <see langword="this"/> contains the specified character
	/// then this method returns the zero-based sequential index 
	/// of the character out of all characters included in the character set.
	/// If the character is not included then the method returns <c>-1</c>.
	/// </summary>
	/// <param name="c">The character for the sequential index is required..</param>
	/// <returns>The index of this character in the character set if it is present, otherwise <c>-1</c>.</returns>
	public int IndexOf(char c)
	{
		var mask = UInt128.One << c;
		if (c >= '\u0080' || (this.bits & mask) == 0) { return -1; }
		--mask;
		return (int)UInt128.PopCount(this.bits & mask);
	}
	/// <summary>
	/// If this char set contains a single character then this property returns that character.
	/// Otherwise <see langword="null"/> is returned.
	/// </summary>
	public char? SingleCharacter
	{
		get
		{
			if (this.Count != 1) { return null; }
			return (char)UInt128.TrailingZeroCount(this.bits);
		}
	}
	/// <summary>
	/// Whether <see langword="this"/> has characters in common with <paramref name="other"/>.
	/// </summary>
	/// <param name="other">The other char set.</param>
	/// <returns>Whether <see langword="this"/> intersects with <paramref name="other"/>.</returns>
	public bool IntersectsWith(AsciiCharSet other) => (this.bits & other.bits) != 0;
	/// <summary>
	/// Gets the three possible disjoint combinations of <see langword="this"/> and <paramref name="other"/> in a tuple ordered as follows:
	/// <list type="bullet">
	/// <item><see langword="this"/>&#x00A0;∩&#x00A0;<paramref name="other"/></item>
	/// <item><see langword="this"/>&#x00A0;\&#x00A0;<paramref name="other"/></item>
	/// <item><paramref name="other"/>&#x00A0;\&#x00A0;<see langword="this"/></item>
	/// </list>
	/// <para>Some of these may be empty.</para>
	/// </summary>
	/// <param name="other">The <see cref="AsciiCharSet"/> to compare with <see langword="this"/>.</param>
	/// <returns>
	/// The three disjoint combinations of <see langword="this"/> and <paramref name="other"/>.
	/// </returns>
	public (AsciiCharSet intersection, AsciiCharSet thisLessOther, AsciiCharSet otherLessThis) GetDisjointCombinations(AsciiCharSet other)
	{
		var intersection = new AsciiCharSet(this.bits & other.bits);
		var thisLessOther = new AsciiCharSet(this.bits & ~other.bits);
		var otherLessThis = new AsciiCharSet(other.bits & ~this.bits);

		return (intersection, thisLessOther, otherLessThis);
	}
	/// <inheritdoc/>
	public bool Equals(AsciiCharSet other) => this.bits == other.bits;
	/// <inheritdoc/>
	public override bool Equals([NotNullWhen(true)] object? obj)
		=> obj is AsciiCharSet other && this.bits == other.bits;
	/// <summary>
	/// <inheritdoc/>
	/// <para>
	///The sort order is the same as ASCII ordinal if the chars sets 
	/// were written as strings, with included chars in sequence from low to high.
	/// So <c>[a]</c> &lt; <c>[ab]</c> &lt; <c>[abc]</c> &lt; <c>[ac]</c> &lt; <c>b</c> &lt; <c>c</c> &lt; <c>[cd]</c>.
	/// </para>
	/// </summary>
	/// <inheritdoc/>
	public int CompareTo(AsciiCharSet other)
	{
		var bitsA = this.bits;
		var bitsB = other.bits;

		if (bitsA == bitsB) { return 0; }

		// Skip the leftmost bits that A and B have in common.
		var xor = bitsA ^ bitsB;
		// Given A != B and definition of xor, some bits must be different,
		// and hence the following is in the range [0,127].
		var shiftRight = (int)UInt128.TrailingZeroCount(xor);
		bitsA >>= shiftRight;
		bitsB >>= shiftRight;

		// If bitsA or bitsB is zero then the trailing zero count is 128.
		// Casting to sbyte makes this into -128.
		var pos1stA = (sbyte)UInt128.TrailingZeroCount(bitsA);
		var pos1stB = (sbyte)UInt128.TrailingZeroCount(bitsB);
		// We know that the leftmost bits are different, 
		// hence this is non-zero.
		return pos1stA - pos1stB;
	}
	/// <summary>
	/// A hash code for the char set.
	/// </summary>
	/// <returns>A hash code for the char set.</returns>
	public override int GetHashCode() => this.bits.GetHashCode();
	/// <summary>
	/// The char set in NX notation (provided it is legal).
	/// </summary>
	/// <returns>The char set in NX notation (provided it is legal).</returns>
	public override string ToString()
	{
		if ((uint)UInt128.PopCount(this.bits) == 1)
		{
			return NXFormattedChar((char)UInt128.TrailingZeroCount(this.bits));
		}
		else if (this == AllDigits) { return "\\9"; }
		else if (this == AllUpperCaseLetters) { return "\\A"; }
		else if (this == AllLowerCaseLetters) { return "\\a"; }
		else if (this == AllDigitsAndUpperCaseLetters) { return "\\X"; }
		else
		{
			var sb = new StringBuilder();

			this.WriteTo(sb);

			return sb.ToString();
		}
	}
	/// <summary>Gets a (performant) enumerator of all characters in the set.</summary>
	/// <returns>The enumerator.</returns>
	public IEnumerator<char> GetEnumerator() => new Enumerator(this);
	/// <summary>Not implemented.</summary>
	IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
	/// <summary>
	/// The writes the char set in NX notation (provided it is legal) to <paramref name="sb"/>.
	/// </summary>
	/// <returns>Writes the char set in NX notation (provided it is legal).</returns>
	public void WriteTo(StringBuilder sb)
	{
		if ((uint)UInt128.PopCount(this.bits) == 1)
		{
			AddNXFormattedChar((char)UInt128.TrailingZeroCount(this.bits), sb);
			return;
		}

		#region Shortcuts
		if (this == AllDigits)
		{
			sb.Append("\\9");
			return;
		}
		if (this == AllUpperCaseLetters)
		{
			sb.Append("\\A");
			return;
		}
		if (this == AllLowerCaseLetters)
		{
			sb.Append("\\a");
			return;
		}
		if (this == AllDigitsAndUpperCaseLetters)
		{
			sb.Append("\\X");
			return;
		}
		#endregion

		sb.Append('[');

		var remainingBits = this.bits;

		bool current = (remainingBits & 1) != 0;
		remainingBits >>= 1;
		bool la1 = (remainingBits & 1) != 0;
		remainingBits >>= 1;
		bool la2 = (remainingBits & 1) != 0;
		remainingBits >>= 1;
		bool la3 = (remainingBits & 1) != 0;

		bool inARange = false;

		for (int c = 0; c < 0x80; ++c)
		{
			if (inARange)
			{
				if (!la1)
				{
					// Next char is not included so we're at the end of the range.
					inARange = false;
					AddNXFormattedChar((char)c, sb);
				}
			}
			else if (current && la1 && la2 && la3)
			{
				// "123" is *not* written as a range, i.e. we don't write "1-3" (because this is the same number of chars).
				// "1234" *is* written as range, i.e. "1-4".
				inARange = true;
				AddNXFormattedChar((char)c, sb);
				sb.Append('-');
			}
			else if (current)
			{
				AddNXFormattedChar((char)c, sb);
			}

			current = la1;
			la1 = la2;
			la2 = la3;
			remainingBits >>= 1;
			la3 = ((uint)remainingBits & 1) != 0;
		}

		sb.Append(']');
	}
	#endregion
	#region Public operators and conversions
	/// <summary>
	/// Union operator.
	/// </summary>
	/// <param name="left">Left arg.</param>
	/// <param name="right">Right arg.</param>
	/// <returns>The union of the two char sets.</returns>
	public static AsciiCharSet operator |(AsciiCharSet left, AsciiCharSet right) => new(left.bits | right.bits);
	/// <summary>
	/// Set difference operator, i.e. elements in <paramref name="left"/> that are not present in <paramref name="right"/>.
	/// </summary>
	/// <param name="left">Left arg.</param>
	/// <param name="right">Right arg.</param>
	/// <returns>The set difference between the two char sets.</returns>
	public static AsciiCharSet operator -(AsciiCharSet left, AsciiCharSet right) => new(left.bits & ~right.bits);
	/// <summary>
	/// Intersection operator.
	/// </summary>
	/// <param name="left">Left arg.</param>
	/// <param name="right">Right arg.</param>
	/// <returns>The intersection of the two char sets.</returns>
	public static AsciiCharSet operator &(AsciiCharSet left, AsciiCharSet right) => new(left.bits & right.bits);
	/// <summary>
	/// Equality operator.
	/// </summary>
	/// <param name="left">Left arg.</param>
	/// <param name="right">Right arg.</param>
	/// <returns>Whether the sets are the same.</returns>
	public static bool operator ==(AsciiCharSet left, AsciiCharSet right) => left.bits == right.bits;
	/// <summary>
	/// Inequality operator.
	/// </summary>
	/// <param name="left">Left arg.</param>
	/// <param name="right">Right arg.</param>
	/// <returns>Whether the sets are different.</returns>
	public static bool operator !=(AsciiCharSet left, AsciiCharSet right) => left.bits != right.bits;
	#endregion
	#region Public constants and static data
	/// <summary>
	/// The NX <c>[0-9]</c>, which is also denoted <c>\9</c>.
	/// </summary>
	public static readonly AsciiCharSet AllDigits = new(new UInt128(
		upper: 0,
		lower: 0b0000001111111111000000000000000000000000000000000000000000000000u
		));
	/// <summary>
	/// The NX <c>[A-Z]</c>, which is also denoted <c>\A</c>.
	/// </summary>
	public static readonly AsciiCharSet AllUpperCaseLetters = new(new UInt128(
		upper: 0b0000000000000000000000000000000000000111111111111111111111111110u,
		lower: 0
		));
	/// <summary>
	/// The NX <c>[a-z]</c>, which is also denoted <c>\a</c>.
	/// </summary>
	public static readonly AsciiCharSet AllLowerCaseLetters = new(new UInt128(
		upper: 0b0000011111111111111111111111111000000000000000000000000000000000u,
		lower: 0
		));
	/// <summary>
	/// The NX <c>[0-9A-Z]</c>, which is also denoted <c>\X</c>.
	/// </summary>
	public static readonly AsciiCharSet AllDigitsAndUpperCaseLetters = new(new UInt128(
		upper: 0b0000000000000000000000000000000000000111111111111111111111111110u,
		lower: 0b0000001111111111000000000000000000000000000000000000000000000000u
		));
	#endregion
	#region Private static properties and methods
	/// <summary>
	/// Returns an NX version of the char (or a C# version if the char is control).
	/// </summary>
	/// <returns>An NX version of the char set.</returns>
	static string NXFormattedChar(char c)
	{
		if (c > '\u0020' && c < '\u007F')
		{
			if (RequiresStandardEscape(c))
			{
				return $"\\{c}";
			}
			return $"{c}";
		}
		else if (c == '\u0020')
		{
			return "\\s";
		}
		else
		{
			return $"\\x{((uint)c).ToString("X2", CultureInfo.InvariantCulture)}";
		}
	}
	/// <summary>
	/// Returns an NX version of the char (or a C# version if the char is control).
	/// </summary>
	/// <returns>An NX version of the char set.</returns>
	static void AddNXFormattedChar(char c, StringBuilder sb)
	{
		if (c > '\u0020' && c < '\u007F')
		{
			if (RequiresStandardEscape(c))
			{
				sb.Append('\\');
			}
			sb.Append(c);
		}
		else if (c == '\u0020')
		{
			// Space
			sb.Append("\\s");
		}
		else
		{
			sb
				.Append("\\x")
				.Append(((uint)c).ToString("X2", CultureInfo.InvariantCulture))
				;
		}
	}
	static bool RequiresStandardEscape(char c)
		=> c == '!' // 0x21
			|| c == '"' // 0x22
			|| c == '#' // 0x23
			|| c == '(' // 0x28
			|| c == ')' // 0x29
			|| c == '-' // 0x2D
			|| c == '[' // 0x5B
			|| c == '\\' // 0x5C
			|| c == ']' // 0x5D
			|| c == '|' // 0x7C
		;
	#endregion
	#region IO
	/// <inheritdoc/>
	public void WriteTo(BinaryWriter writer)
	{
		var bits = this.bits;

		// Writing the whole set as a 128 integer would take 16 bytes.
		// In typical applications, only a single bit or a few ranges of
		// consecutive bits are set so we optimise for this scenario.

		int bitsRemaining = 128;

		for (; ; )
		{
			int trailingZeroCount = Math.Min(bitsRemaining, (int)UInt128.TrailingZeroCount(bits));

			writer.Write((byte)trailingZeroCount);

			bitsRemaining -= trailingZeroCount;
			if (bitsRemaining <= 0) { break; }
			bits >>= trailingZeroCount;
			bits = ~bits;
		}
	}
	/// <inheritdoc/>
	public static bool TryReadFrom(BinaryReader reader, out AsciiCharSet instance, [NotNullWhen(false)] out string? errorMessage)
	{
		const string ErrorMessageStart = $"Binary deserialisation error when expecting an {nameof(AsciiCharSet)}. ";

		try
		{
			UInt128 bits = 0;
			bool isReadingSetBits = false;
			int bitsRemaining = 128;
			int oldBitsRemaining = 128;

			for (; ; )
			{
				int trailingZeroCount = reader.ReadByte();
				bitsRemaining -= trailingZeroCount;
				if (trailingZeroCount > 128 || bitsRemaining < 0)
				{
					instance = default;
					errorMessage = ErrorMessageStart + "Unexpected bit pattern A.";
					return false;
				}
				// By design, the right shift argument is masked by 0x7F.
				// So we need the following to ensure it is saturating.
				bits = trailingZeroCount >= 128 ? 0 : bits >>= trailingZeroCount;
				if (bitsRemaining <= 0) { break; }
				bits = ~bits;
				if (isReadingSetBits)
				{
					if (oldBitsRemaining == bitsRemaining)
					{
						instance = default;
						errorMessage = ErrorMessageStart + "Unexpected bit pattern B.";
						return false;
					}
					oldBitsRemaining = bitsRemaining;
				}
				isReadingSetBits = !isReadingSetBits;
			}

			if (isReadingSetBits) { bits = ~bits; }

			instance = new AsciiCharSet(bits);
			errorMessage = default;
			return true;
		}
		catch (Exception e)
		{
			instance = default;
			errorMessage = ErrorMessageStart + e.Message;
			return false;
		}
	}
	#endregion
	#region Enumerator struct
	/// <inheritdoc/>
	public struct Enumerator : IEnumerator<char>, System.Collections.IEnumerator
	{
		UInt128 bitsRemaining;
		uint shiftCount;
		char current;

		internal Enumerator(AsciiCharSet charSet)
		{
			this.bitsRemaining = charSet.bits;
			this.shiftCount = 0;
			this.current = default;
		}

		/// <inheritdoc/>
		public bool MoveNext()
		{
			if (this.bitsRemaining == UInt128.Zero) { return false; }
			var n = (uint)UInt128.TrailingZeroCount(this.bitsRemaining);
			this.current = (char)(n + this.shiftCount);
			++n;
			if (n > 127)
			{
				this.bitsRemaining = 0;
			}
			else
			{
				this.bitsRemaining >>= (int)n;
				this.shiftCount += n;
			}
			return true;
		}

		/// <inheritdoc/>
		public readonly char Current => this.current;

		/// <inheritdoc/>
		public void Dispose() { }

		/// <summary>
		/// Not implemented.
		/// </summary>
		/// <exception cref="NotImplementedException"></exception>
		readonly Object System.Collections.IEnumerator.Current => this.Current;

		/// <summary>
		/// Not implemented.
		/// </summary>
		/// <exception cref="NotImplementedException"></exception>
		void System.Collections.IEnumerator.Reset() => throw new NotImplementedException();
	}
	#endregion
}