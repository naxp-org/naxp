// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using Microsoft.CodeAnalysis;

namespace LogMu.Generator;

/// <summary>
/// The eight integer types a naxp can encode to, in the three forms this generator needs them:
/// the type the user writes in <c>typeof(...)</c>, the library's own choice, and the C# spelling
/// a message shows back.
/// </summary>
/// <remarks>
/// The rows run narrowest first, which is what lets <see cref="Narrowest"/> walk them in order.
/// </remarks>
static class ValueTypes
{
	static readonly (SpecialType Written, NaxpValueType Value, string Keyword)[] Table =
	[
		(SpecialType.System_SByte, NaxpValueType.Int8, "sbyte"),
		(SpecialType.System_Byte, NaxpValueType.UInt8, "byte"),
		(SpecialType.System_Int16, NaxpValueType.Int16, "short"),
		(SpecialType.System_UInt16, NaxpValueType.UInt16, "ushort"),
		(SpecialType.System_Int32, NaxpValueType.Int32, "int"),
		(SpecialType.System_UInt32, NaxpValueType.UInt32, "uint"),
		(SpecialType.System_Int64, NaxpValueType.Int64, "long"),
		(SpecialType.System_UInt64, NaxpValueType.UInt64, "ulong"),
	];

	/// <summary>The types a message offers when it has just refused one, in the same order.</summary>
	public const string Choices = "sbyte, byte, short, ushort, int, uint, long or ulong";

	/// <summary>The type <c>typeof(...)</c> named, where it is one a naxp can encode to.</summary>
	public static bool TryFrom(ITypeSymbol symbol, out NaxpValueType valueType)
	{
		foreach ((SpecialType written, NaxpValueType value, string _) in Table)
		{
			if (symbol.SpecialType == written)
			{
				valueType = value;

				return true;
			}
		}

		valueType = NaxpValueType.UInt64;

		return false;
	}

	/// <summary>The C# spelling of a value type, which is how a message should name it.</summary>
	public static string Keyword(NaxpValueType valueType)
	{
		foreach ((SpecialType _, NaxpValueType value, string keyword) in Table)
		{
			if (value == valueType) { return keyword; }
		}

		return valueType.ToString();
	}

	/// <summary>The narrowest type a naxp of this many values fits, for saying so.</summary>
	public static NaxpValueType Narrowest(ulong valueCount)
	{
		foreach ((SpecialType _, NaxpValueType value, string _2) in Table)
		{
			if (valueCount <= Emitter.Capacity(value)) { return value; }
		}

		// W5 caps a legal naxp at ulong.MaxValue values, which is the last row, so the loop
		// always returns and this is unreachable.
		return NaxpValueType.UInt64;
	}
}
