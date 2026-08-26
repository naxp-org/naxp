// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Globalization;
using LogMu;

namespace TryNaxp
{
	/// <summary>
	/// Two naxps in one type, kept apart by their prefixes, each encoding to the integer type it
	/// was given.
	/// </summary>
	[Naxp(@"\A\A?\9\X? \s \9\A\A", typeof(int), Prefix = "Postcode")]
	[Naxp(@"\A\9", typeof(short), Prefix = "Letter")]
	// Four hexadecimal digits. Every position draws from the same set, so the strings are ordered
	// by ASCII, and ASCII puts 0-9 immediately before A-F: the encoding is the number itself, plus
	// one because zero is reserved. Lower case would break that, since 'a' sorts above 'F' while
	// meaning the same digit. The 65536 values are one too many for ushort, whose largest is
	// 65535, so this is int.
	[Naxp(@"[0-9A-F]{4}", typeof(int), Prefix = "Hex")]
	internal static partial class Codes
	{
	}

	/// <summary>
	/// One naxp and no prefix, so the members are bare: the class supplies the noun.
	/// </summary>
	// The hyphens are escaped: '-' is reserved, since it separates the bounds of a range.
	[Naxp(@"\9{4}\-\9{2}\-\9{2}", typeof(int))]
	internal static partial class IsoDate
	{
	}

	internal static class Program
	{
		private static int Main()
		{
			const string Postcode = "SW1A 1AA";
			const string Date = "2026-08-20";

			int encoded = Codes.PostcodeEncode(Postcode);
			string decoded = Codes.PostcodeDecode(encoded);

			Console.WriteLine("Postcodes, from the naxp \\A\\A?\\9\\X? \\s \\9\\A\\A");
			Console.WriteLine($"  values         1 to {Codes.PostcodeValueCount}");
			Console.WriteLine($"  longest        {Codes.PostcodeMaxLength} characters");
			Console.WriteLine($"  accepts        '{Postcode}' {Codes.PostcodeAccepts(Postcode)}, 'ZZ99 9ZZ' {Codes.PostcodeAccepts("ZZ99 9ZZ")}, 'nonsense' {Codes.PostcodeAccepts("nonsense")}");
			Console.WriteLine($"  encode         '{Postcode}' to {encoded}");
			Console.WriteLine($"  decode         {encoded} back to '{decoded}'");
			Console.WriteLine($"  rejected text  encodes to {Codes.PostcodeEncode("nonsense")}, the value no string has");
			Console.WriteLine();

			Console.WriteLine("A letter and a digit, over short rather than int");
			Console.WriteLine($"  values         1 to {Codes.LetterValueCount}");
			Console.WriteLine($"  encode         'A7' to {Codes.LetterEncode("A7")}, 'Z9' to {Codes.LetterEncode("Z9")}");
			Console.WriteLine();

			bool hexIsItsOwnNumber = HexMatchesItsNumber();

			Console.WriteLine("Four hexadecimal digits, from [0-9A-F]{4}");
			Console.WriteLine($"  values         1 to {Codes.HexValueCount}");
			Console.WriteLine($"  encode         '0000' to {Codes.HexEncode("0000")}, '00FF' to {Codes.HexEncode("00FF")}, 'FFFF' to {Codes.HexEncode("FFFF")}");
			Console.WriteLine($"  the number     plus one, over every one of the {Codes.HexValueCount}: {hexIsItsOwnNumber}");
			Console.WriteLine();

			// The encoding is ordered, so for a naxp of nothing but digits the value is the digits
			// read as a number, plus one: zero is reserved for text the naxp does not accept.
			Console.WriteLine("An ISO date, with no prefix, so the names are bare");
			Console.WriteLine($"  encode         '{Date}' to {IsoDate.Encode(Date)}");
			Console.WriteLine($"  decode         back to '{IsoDate.Decode(IsoDate.Encode(Date))}'");
			Console.WriteLine();

			// The generated code answers on its own, with no reference to the library at run time.
			// The library, given the same naxp, has to agree.
			var library = Naxp.Parse(@"\A?\A\9\X? \s \9\A\A");
			bool agrees = library.Encode(Postcode) == encoded && library.ValueCount == Codes.PostcodeValueCount;

			Console.WriteLine($"The library agrees: {agrees}");

			return decoded == Postcode && agrees && hexIsItsOwnNumber ? 0 : 1;
		}

		/// <summary>
		/// Whether every string of the hexadecimal naxp encodes to its own value plus one, which
		/// is what it means for the encoding's order to be hexadecimal order.
		/// </summary>
		private static bool HexMatchesItsNumber()
		{
			for (int number = 0; number <= 0xFFFF; number++)
			{
				if (Codes.HexEncode(number.ToString("X4", CultureInfo.InvariantCulture)) != number + 1)
				{
					return false;
				}
			}

			return true;
		}
	}
}
