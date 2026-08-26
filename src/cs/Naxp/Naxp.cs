// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace LogMu;

/// <summary>
/// A naxp: an expression over ASCII strings that numbers the strings it accepts.
/// </summary>
/// <remarks>
/// <para>
/// A naxp accepts a set of strings and gives each one a number from 1 upwards, with zero
/// reserved for a string it does not accept. The numbering is a property of the language rather
/// than of how it was written, so two naxps accepting the same strings number them alike.
/// </para>
/// <para>
/// Every rule of the language is decided when the naxp is parsed. An instance of this type is
/// therefore a well-formed naxp, and no operation on it can fail for a reason of the naxp's own:
/// <see cref="Encode(ReadOnlySpan{char})"/> returns zero only because the string is not one this
/// naxp accepts.
/// </para>
/// <para>
/// Instances are immutable and safe to share between threads.
/// </para>
/// </remarks>
public sealed class Naxp
{
	#region Private data
	/// <summary>
	/// The longest string widened from bytes on the stack rather than the heap. Strings a naxp
	/// is written for are far shorter than this, so the heap path is close to unreachable.
	/// </summary>
	const int MaxStackAllocLength = 256;

	readonly Compilation compilation;
	#endregion
	#region Private ctors
	Naxp(Compilation compilation)
	{
		this.compilation = compilation;
	}
	#endregion
	#region Public factory methods
	/// <summary>
	/// Parses a naxp.
	/// </summary>
	/// <param name="text">The source of the naxp.</param>
	/// <returns>The naxp.</returns>
	/// <exception cref="FormatException">
	/// <paramref name="text"/> is not a well-formed naxp, or is one this implementation will not
	/// compile because of its size.
	/// </exception>
	public static Naxp Parse(ReadOnlySpan<char> text)
	{
		if (TryParse(
			text,
			out Naxp? naxp,
			out string? errorMessage,
			out int errorTextOffset,
			out int errorTextLength,
			out string? errorCode))
		{
			return naxp;
		}

		// The code and the span are in the message because a thrown exception is all anybody
		// gets: there is no out parameter to read them from.
		throw new FormatException(string.Format(
			CultureInfo.InvariantCulture,
			"{0} at {1}..{2}: {3}",
			errorCode,
			errorTextOffset,
			errorTextOffset + errorTextLength,
			errorMessage));
	}

	/// <summary>
	/// Tries to parse a naxp, or says what is wrong.
	/// </summary>
	/// <param name="text">The source of the naxp.</param>
	/// <param name="naxp">The naxp, if this returns <see langword="true"/>.</param>
	/// <param name="errorMessage">
	/// What is wrong, and where practical what to write instead, if this returns
	/// <see langword="false"/>.
	/// </param>
	/// <returns>Whether the source is a well-formed naxp this implementation can compile.</returns>
	public static bool TryParse(
		ReadOnlySpan<char> text,
		[NotNullWhen(true)] out Naxp? naxp,
		[NotNullWhen(false)] out string? errorMessage)
		=> TryParse(text, out naxp, out errorMessage, out _, out _, out _);

	/// <summary>
	/// Tries to parse a naxp, or says what is wrong, where, and which refusal it is.
	/// </summary>
	/// <param name="text">The source of the naxp.</param>
	/// <param name="naxp">The naxp, if this returns <see langword="true"/>.</param>
	/// <param name="errorMessage">
	/// What is wrong, and where practical what to write instead.
	/// </param>
	/// <param name="errorTextOffset">
	/// Where the fault starts, in characters from the start of <paramref name="text"/>.
	/// </param>
	/// <param name="errorTextLength">
	/// How much of <paramref name="text"/> is at fault, which is the whole of it where the fault
	/// belongs to the naxp rather than to any one place in it.
	/// </param>
	/// <param name="errorCode">
	/// A stable identifier for this refusal, such as <c>NAXP1002</c>. Diagnostics
	/// only: it is here so that a log or a bug report names the fault without quoting the prose.
	/// </param>
	/// <returns>Whether the source is a well-formed naxp this implementation can compile.</returns>
	public static bool TryParse(
		ReadOnlySpan<char> text,
		[NotNullWhen(true)] out Naxp? naxp,
		[NotNullWhen(false)] out string? errorMessage,
		out int errorTextOffset,
		out int errorTextLength,
		[NotNullWhen(false)] out string? errorCode)
	{
		if (Compiler.TryCompile(text, out Compilation? compilation, out NaxpError? error))
		{
			naxp = new Naxp(compilation!);
			errorMessage = null;
			errorCode = null;
			errorTextOffset = 0;
			errorTextLength = 0;

			return true;
		}

		NaxpError refusal = error!.Value;

		naxp = null;
		errorMessage = refusal.Text;
		errorCode = refusal.Code;
		errorTextOffset = refusal.Offset;

		// Only here is the length of the source known, so this is where a refusal that named no
		// place in the naxp is given the whole of it.
		errorTextLength = refusal.IsWholeNaxp ? text.Length : refusal.Length;

		return false;
	}
	#endregion
	#region Public properties
	/// <summary>The source this naxp was parsed from.</summary>
	public string Source => this.compilation.Source;

	/// <summary>
	/// The count of values this naxp encodes, which is the largest value it can produce.
	/// </summary>
	/// <remarks>
	/// W5 caps this at 2^64 - 1, so a naxp with more values than a <see cref="ulong"/> can hold
	/// is refused rather than reported here.
	/// </remarks>
	public ulong ValueCount => this.compilation.ValueCount;
	#endregion
	#region Public acceptance
	/// <summary>
	/// Whether this naxp accepts the specified string.
	/// </summary>
	/// <param name="text">The string to test.</param>
	/// <returns>Whether the naxp accepts it.</returns>
	public bool Accepts(ReadOnlySpan<char> text) => this.compilation.Accepts(text);

	/// <summary>
	/// Whether this naxp accepts the specified ASCII text.
	/// </summary>
	/// <remarks>
	/// A byte outside ASCII is not accepted, since no naxp can name a character above U+007E.
	/// </remarks>
	/// <param name="text">The ASCII text to test.</param>
	/// <returns>Whether the naxp accepts it.</returns>
	public bool Accepts(ReadOnlySpan<byte> text)
	{
		if (text.Length > NaxpLimits.MaxStringLength) { return false; }

		if (text.Length <= MaxStackAllocLength)
		{
			Span<char> buffer = stackalloc char[text.Length];
			Widen(text, buffer);

			return this.Accepts(buffer);
		}

		var chars = new char[text.Length];
		Widen(text, chars);

		return this.Accepts(chars.AsSpan());
	}
	#endregion
	#region Public encoding
	/// <summary>
	/// The value of a string.
	/// </summary>
	/// <remarks>
	/// Encoding cannot fail. Every rule was decided when the naxp was parsed, so the string
	/// either has exactly one value or is not one this naxp accepts.
	/// </remarks>
	/// <param name="text">The string to encode.</param>
	/// <returns>
	/// The value, from 1 to <see cref="ValueCount"/>, or zero if the naxp does not accept the
	/// string.
	/// </returns>
	public ulong Encode(ReadOnlySpan<char> text) => this.compilation.Encode(text);

	/// <summary>
	/// The value of ASCII text.
	/// </summary>
	/// <remarks>
	/// A byte outside ASCII gives zero, since no naxp can name a character above U+007E.
	/// </remarks>
	/// <param name="text">The ASCII text to encode.</param>
	/// <returns>
	/// The value, from 1 to <see cref="ValueCount"/>, or zero if the naxp does not accept the
	/// text.
	/// </returns>
	public ulong Encode(ReadOnlySpan<byte> text)
	{
		if (text.Length > NaxpLimits.MaxStringLength) { return 0UL; }

		if (text.Length <= MaxStackAllocLength)
		{
			Span<char> buffer = stackalloc char[text.Length];
			Widen(text, buffer);

			return this.Encode(buffer);
		}

		var chars = new char[text.Length];
		Widen(text, chars);

		return this.Encode(chars.AsSpan());
	}

	/// <summary>
	/// Tries to encode a string.
	/// </summary>
	/// <param name="text">The string to encode.</param>
	/// <param name="encoded">The value, or zero if the naxp does not accept the string.</param>
	/// <returns>Whether the naxp accepts the string.</returns>
	public bool TryEncode(ReadOnlySpan<char> text, out ulong encoded)
	{
		encoded = this.Encode(text);

		return encoded != 0UL;
	}

	/// <summary>
	/// Tries to encode ASCII text.
	/// </summary>
	/// <param name="text">The ASCII text to encode.</param>
	/// <param name="encoded">The value, or zero if the naxp does not accept the text.</param>
	/// <returns>Whether the naxp accepts the text.</returns>
	public bool TryEncode(ReadOnlySpan<byte> text, out ulong encoded)
	{
		encoded = this.Encode(text);

		return encoded != 0UL;
	}
	#endregion
	#region Public decoding
	/// <summary>
	/// The string a value stands for, which is in canonical form.
	/// </summary>
	/// <param name="value">The value, from 1 to <see cref="ValueCount"/>.</param>
	/// <returns>The string.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="value"/> is not one this naxp produces.
	/// </exception>
	public string Decode(ulong value)
		=> this.TryDecode(value, out string? text)
			? text
			: throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				string.Format(
					CultureInfo.InvariantCulture,
					"This naxp encodes the values 1 to {0}.",
					this.ValueCount))
			;

	/// <summary>
	/// Tries to find the string a value stands for.
	/// </summary>
	/// <param name="value">The value, from 1 to <see cref="ValueCount"/>.</param>
	/// <param name="text">The string, if this returns <see langword="true"/>.</param>
	/// <returns>Whether the value is one this naxp produces.</returns>
	public bool TryDecode(ulong value, [NotNullWhen(true)] out string? text)
		=> this.compilation.TryDecode(value, out text);
	#endregion
	#region Public canonical form
	/// <summary>
	/// The canonical form of a string, which is the string with the match of each replaceable
	/// element replaced by that element's rendering.
	/// </summary>
	/// <remarks>
	/// A string and its canonical form encode to the same value, and decoding produces the
	/// canonical form.
	/// </remarks>
	/// <param name="text">The string.</param>
	/// <returns>
	/// The canonical form, or <see langword="null"/> if the naxp does not accept the string.
	/// </returns>
	public string? GetCanonicalForm(ReadOnlySpan<char> text)
		=> this.TryGetCanonicalForm(text, out string? canonicalForm) ? canonicalForm : null;

	/// <summary>
	/// Tries to find the canonical form of a string.
	/// </summary>
	/// <param name="text">The string.</param>
	/// <param name="canonicalForm">
	/// The canonical form, if this returns <see langword="true"/>.
	/// </param>
	/// <returns>Whether the naxp accepts the string.</returns>
	public bool TryGetCanonicalForm(ReadOnlySpan<char> text, [NotNullWhen(true)] out string? canonicalForm)
		=> this.compilation.TryGetCanonicalForm(text, out canonicalForm);
	#endregion
	#region Public overrides
	/// <inheritdoc/>
	public override string ToString() => this.Source;
	#endregion
	#region Private methods
	/// <summary>
	/// Copies ASCII bytes into characters.
	/// </summary>
	/// <remarks>
	/// A byte of 0x80 or above becomes a character no naxp can name, so it is refused further
	/// down rather than needing a check here.
	/// </remarks>
	static void Widen(ReadOnlySpan<byte> source, Span<char> destination)
	{
		for (int i = 0; i < source.Length; ++i) { destination[i] = (char)source[i]; }
	}
	#endregion
}
