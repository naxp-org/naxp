// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace NXOld;

/// <summary>
/// LogMu extensions, including extension methods.
/// </summary>
public static partial class LogMuExtensions
{
	// Do not delete!
	// Methods are typically defined in the same file as the type on which they act is defined.

	#region Public
	/// <summary>
	/// The LogMu number format info.
	/// <para>This is intended solely for output rendered to humans direct. It is not for data I/O.</para>
	/// </summary>
	public static NumberFormatInfo LogMuNumberFormatInfo
	=> cached_NumberFormatInfo ?? CreateLogMuNumberFormatInfo();
	#endregion

	#region Internal
	/// <summary>
	/// Serialises a UTC date-time.
	/// </summary>
	/// <param name="writer">The binary writer.</param>
	/// <param name="utcDateTime">The date-time. If this is not UTC then an exception will be thrown.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void WriteUtcDateTime(this BinaryWriter writer, DateTime utcDateTime)
	{
		if (utcDateTime.Kind != DateTimeKind.Utc) { ThrowNotUtcDateTime(); }
		writer.Write(utcDateTime.Ticks);
	}
	/// <summary>
	/// Deserialises a UTC date-time.
	/// </summary>
	/// <param name="reader">The binary reader.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static DateTime ReadUtcDateTime(this BinaryReader reader)
	=> new (reader.ReadInt64(), DateTimeKind.Utc);

	/// <summary>
	/// Short name of the day of the week, e.g. "Mon".
	/// </summary>
	/// <param name="day">The day of the week.</param>
	internal static string AsText(this DayOfWeek day) => day switch
	{
		DayOfWeek.Sunday => "Sun",
		DayOfWeek.Monday => "Mon",
		DayOfWeek.Tuesday => "Tue",
		DayOfWeek.Wednesday => "Wed",
		DayOfWeek.Thursday => "Thu",
		DayOfWeek.Friday => "Fri",
		DayOfWeek.Saturday => "Sat",
		_ => "unknown"
	};

	/// <summary>
	/// Writes 
	/// a 32-bit integer compressed using <see href="https://protobuf.dev/programming-guides/encoding/">'zig-zag' encoding</see>, 
	/// which is efficient for small positive <i>and negative</i> values. 
	/// <para>(<see cref="BinaryWriter.Write7BitEncodedInt(int)"/> does a very poor job for small negative integers.)</para>
	/// </summary>
	/// <param name="writer">The binary writer.</param>
	/// <param name="value">The 32-bit integer to serialise.</param>
	internal static void WriteZigZagEncodedInt(this BinaryWriter writer, int value)
	{
		writer.Write7BitEncodedInt((value << 1) ^ (value >> 31));
	}
	/// <summary>
	/// Reads
	/// a 32-bit integer compressed using <see href="https://protobuf.dev/programming-guides/encoding/">'zig-zag' encoding</see>, 
	/// which is efficient for small positive <i>and negative</i> values. 
	/// <para>(<see cref="BinaryReader.Read7BitEncodedInt()"/> does a very poor job for small negative integers.)</para>
	/// </summary>
	/// <param name="reader">The binary reader.</param>
	/// <returns>The deserialised 32-bit integer.</returns>
	internal static int ReadZigZagEncodedInt(this BinaryReader reader)
	{
		var value = reader.Read7BitEncodedInt();
		return (value >>> 1) ^ (-(value & 1));
	}
	#endregion

	#region Private
	[MethodImpl(MethodImplOptions.NoInlining)]
	[DoesNotReturn]
	static void ThrowNotUtcDateTime()
	{
		throw new ArgumentOutOfRangeException("utcDateTime", "Must be a UTC date-time.");
	}

	static NumberFormatInfo? cached_NumberFormatInfo;

	[MethodImpl(MethodImplOptions.NoInlining)]
	static NumberFormatInfo CreateLogMuNumberFormatInfo()
	{
		var nfi = new NumberFormatInfo()
		{
			//PositiveSign = "+", // This is the default.
			NegativeSign = "−", // The default is "-" (hyphen-dash).
								//NaNSymbol = "NaN", // The string used to represent NaN values. This is the default.
			PositiveInfinitySymbol = "∞", // The string used to represent positive infinities. The default is "Infinity".
			NegativeInfinitySymbol = "−∞", // The string used to represent positive infinities. The default is "-Infinity".

			//NumberDecimalSeparator = ".", // This is the default.
			//NumberGroupSizes = [3], // The number of digits in each group to the left of the decimal point. This is the default.
			NumberGroupSeparator = "\u202F", // Narrow no-break space (U+202F) per ISO. The default is ",".
											 //NumberDecimalDigits = 2, // The default number of decimal places. This is the default.
											 //NumberNegativePattern = 1, // This is the default.

			//CurrencyDecimalSeparator = ".", // The character used as the decimal separator. This is the default.
			//CurrencyGroupSizes = [3], // The number of digits in each group to the left of the decimal point. This is the default.
			CurrencyGroupSeparator = "\u202F", // The character used to separate groups of digits to the left of the decimal point. Narrow no-break space (U+202F) per ISO. The default is ",".
											   //CurrencyDecimalDigits = 2, // The default number of decimal places. This is the default.
											   //CurrencyPositivePattern = 0, // The format of positive values. This is the default.
											   //CurrencyNegativePattern = 0, // The format of negative values. This is the default.
											   //CurrencySymbol = "¤", // String used as local monetary symbol. This is the default, "¤" (U+00A4).

			//PercentDecimalSeparator = ".", // This is the default.
			//PercentGroupSizes = [3], // This is the default.
			PercentGroupSeparator = "\u202F", // Narrow no-break space (U+202F) per ISO. The default is ",".
											  //PercentDecimalDigits = 2, // This is the default.
											  //PercentPositivePattern = 0, // This is the default.
											  //PercentNegativePattern = 0, // This is the default.
											  //PercentSymbol = "%", // This is the default.

			//PerMilleSymbol = "‰", // This is the default (U+2030).
		};

		// I don't think we can avoid this copy if we want to ensure the result is read-only:
		return cached_NumberFormatInfo = NumberFormatInfo.ReadOnly(nfi);
	}
	#endregion
}