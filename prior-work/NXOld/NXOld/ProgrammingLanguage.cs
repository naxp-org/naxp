// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.Globalization;

namespace NXOld;

/// <summary>
/// Represents a supported programming language.
/// </summary>
public enum ProgrammingLanguage
{
	/// <summary>
	/// <see href="https://en.wikipedia.org/wiki/C_Sharp_(programming_language)">The C# programming language</see>.
	/// </summary>
	CSharp,
}

partial class LogMuExtensions
{
	/// <summary>
	/// Name of the specified programming language.
	/// </summary>
	/// <param name="language">The programming language.</param>
	/// <returns>Name of <paramref name="language"/>.</returns>
	public static string AsText(this ProgrammingLanguage language) => language switch
	{
		ProgrammingLanguage.CSharp => "C#",
		_ => string.Create(CultureInfo.InvariantCulture, $"unknown {nameof(ProgrammingLanguage)} (x{(uint)language:X8})"),
	};
}