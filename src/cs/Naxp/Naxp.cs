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
/// reserved for text it finds invalid. The numbering is a property of the language rather
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
	/// <param name="pattern">The pattern of the naxp.</param>
	/// <returns>The naxp.</returns>
	/// <exception cref="FormatException">
	/// <paramref name="pattern"/> is not a well-formed naxp.
	/// </exception>
	public static Naxp Parse(ReadOnlySpan<char> pattern)
	{
		if (TryParse(
			pattern,
			out Naxp? naxp,
			out string? errorMessage,
			out int errorOffset,
			out int errorLength,
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
			errorOffset,
			errorOffset + errorLength,
			errorMessage));
	}

	/// <summary>
	/// Tries to parse a naxp, or says what is wrong.
	/// </summary>
	/// <param name="pattern">The pattern of the naxp.</param>
	/// <param name="naxp">The naxp, if this returns <see langword="true"/>.</param>
	/// <param name="errorMessage">
	/// What is wrong, and where practical what to write instead, if this returns
	/// <see langword="false"/>.
	/// </param>
	/// <returns>Whether the pattern is a well-formed naxp.</returns>
	public static bool TryParse(
		ReadOnlySpan<char> pattern,
		[NotNullWhen(true)] out Naxp? naxp,
		[NotNullWhen(false)] out string? errorMessage)
		=> TryParse(pattern, out naxp, out errorMessage, out _, out _, out _);

	/// <summary>
	/// Tries to parse a naxp, or says what is wrong, where, and why it is invalid.
	/// </summary>
	/// <param name="pattern">The pattern of the naxp.</param>
	/// <param name="naxp">The naxp, if this returns <see langword="true"/>.</param>
	/// <param name="errorMessage">
	/// What is wrong, and where practical what to write instead.
	/// </param>
	/// <param name="errorOffset">
	/// Where the fault starts, in characters from the start of <paramref name="pattern"/>.
	/// </param>
	/// <param name="errorLength">
	/// How much of <paramref name="pattern"/> is at fault, which is the whole of it where the fault
	/// belongs to the naxp rather than to any one place in it.
	/// </param>
	/// <param name="errorCode">
	/// A stable identifier for this fault, such as <c>NAXP1002</c>. Diagnostics
	/// only: it is here so that a log or a bug report names the fault without quoting the prose.
	/// </param>
	/// <returns>Whether the pattern is a well-formed naxp.</returns>
	public static bool TryParse(
		ReadOnlySpan<char> pattern,
		[NotNullWhen(true)] out Naxp? naxp,
		[NotNullWhen(false)] out string? errorMessage,
		out int errorOffset,
		out int errorLength,
		[NotNullWhen(false)] out string? errorCode)
	{
		if (Compiler.TryCompile(pattern, out Compilation? compilation, out NaxpError? error))
		{
			naxp = new Naxp(compilation!);
			errorMessage = null;
			errorCode = null;
			errorOffset = 0;
			errorLength = 0;

			return true;
		}

		NaxpError fault = error!.Value;

		naxp = null;
		errorMessage = fault.Text;
		errorCode = fault.Code;
		errorOffset = fault.Offset;

		// Only here is the length of the pattern known, so this is where a fault that named no
		// place in the naxp is given the whole of it.
		errorLength = fault.IsWholeNaxp ? pattern.Length : fault.Length;

		return false;
	}
	#endregion
	#region Public properties
	/// <summary>The pattern this naxp was parsed from.</summary>
	public string Pattern => this.compilation.Pattern;

	/// <summary>
	/// The largest encoded value this naxp produces, which is also how many it has.
	/// </summary>
	/// <remarks>
	/// W5 caps this at 2^64 - 1, so a naxp with more encoded values than a <see cref="ulong"/>
	/// can hold is invalid rather than reported here.
	/// </remarks>
	public ulong MaxEncodedValue => this.compilation.MaxEncodedValue;
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
	/// A byte outside ASCII makes the text invalid, since no naxp can name a character above U+007E.
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
	/// The encoded value of a string.
	/// </summary>
	/// <remarks>
	/// Encoding cannot fail. Every rule was decided when the naxp was parsed, so the string
	/// either has exactly one value or is not one this naxp accepts.
	/// </remarks>
	/// <param name="text">The string to encode.</param>
	/// <returns>
	/// The encoded value, from 1 to <see cref="MaxEncodedValue"/>, or zero if the string is
	/// invalid.
	/// </returns>
	public ulong Encode(ReadOnlySpan<char> text) => this.compilation.Encode(text);

	/// <summary>
	/// The encoded value of ASCII text.
	/// </summary>
	/// <remarks>
	/// A byte outside ASCII gives zero, since no naxp can name a character above U+007E.
	/// </remarks>
	/// <param name="text">The ASCII text to encode.</param>
	/// <returns>
	/// The encoded value, from 1 to <see cref="MaxEncodedValue"/>, or zero if the text is
	/// invalid.
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
	/// <param name="encoded">The value, or zero if the string is invalid.</param>
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
	/// <param name="encoded">The value, or zero if the text is invalid.</param>
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
	/// <param name="value">The encoded value, from 1 to <see cref="MaxEncodedValue"/>.</param>
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
					this.MaxEncodedValue))
			;

	/// <summary>
	/// Tries to find the string a value stands for.
	/// </summary>
	/// <param name="value">The encoded value, from 1 to <see cref="MaxEncodedValue"/>.</param>
	/// <param name="text">The string, if this returns <see langword="true"/>.</param>
	/// <returns>Whether the value is one this naxp produces.</returns>
	public bool TryDecode(ulong value, [NotNullWhen(true)] out string? text)
		=> this.compilation.TryDecode(value, out text);
	#endregion
	#region Public canonical form
	/// <summary>
	/// The canonical form of a string, which is the string with the match of each unified
	/// element replaced by that element's rendering.
	/// </summary>
	/// <remarks>
	/// A string and its canonical form encode to the same value, and decoding produces the
	/// canonical form.
	/// </remarks>
	/// <param name="text">The string.</param>
	/// <returns>
	/// The canonical form, or <see langword="null"/> if the string is invalid.
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
	#region Public code generation
	/// <summary>
	/// Emits this naxp as source in the specified language.
	/// </summary>
	/// <remarks>
	/// <para>
	/// What comes back is a fragment: the declarations answering the same questions this class
	/// does, for this one naxp, calling back into nothing. The paraphernalia around it, a header
	/// comment, imports, a namespace or a wrapping class, is the caller's, which is what lets one
	/// fragment land in a source generator's partial class and on a web page alike.
	/// </para>
	/// <para>
	/// Everything a naxp decides is decided when it is compiled, so generated code carries no
	/// dependency on this library at all.
	/// </para>
	/// </remarks>
	/// <param name="language">The language to emit.</param>
	/// <param name="prefix">
	/// The prefix every generated name starts with, so several naxps can share one scope. Empty
	/// gives the bare names. Each language applies its own casing convention to it.
	/// </param>
	/// <param name="valueType">
	/// The integer type the generated code uses for encoded values.
	/// <see cref="MaxEncodedValue"/> must fit it.
	/// </param>
	/// <param name="initialIndent">
	/// What every line is indented with ahead of its own depth, so the fragment can sit inside an
	/// already-indented wrapper.
	/// </param>
	/// <param name="newLine">
	/// What ends every line. The caller's choice rather than the platform's, so that one naxp
	/// gives the same fragment on every machine.
	/// </param>
	/// <param name="indent">What one level of indentation is written as.</param>
	/// <returns>The fragment.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="language"/> is not an output language, the prefix is neither empty nor an
	/// ASCII identifier, or the largest encoded value does not fit <paramref name="valueType"/>.
	/// </exception>
	/// <exception cref="ArgumentNullException">Any of the three formatting strings is <see langword="null"/>.</exception>
	public string Emit(
		OutputLanguage language,
		string prefix = "",
		NaxpValueType valueType = NaxpValueType.UInt64,
		string initialIndent = "",
		string newLine = "\n",
		string indent = "\t")
	{
		Emitter emitter = language switch
		{
			OutputLanguage.CSharp => CSharpEmitter.Instance,
			OutputLanguage.JavaScript => JavaScriptEmitter.Instance,
			OutputLanguage.C => CEmitter.Instance,
			OutputLanguage.Cpp => CppEmitter.Instance,
			_ => throw new ArgumentException($"'{language}' is not an output language.", nameof(language)),
		};

		return emitter.Emit(this.compilation, prefix, valueType, initialIndent, newLine, indent);
	}
	#endregion
	#region Public comparison
	/// <summary>
	/// How <paramref name="b"/> stands to <paramref name="a"/>: what happens to text and values
	/// held under <paramref name="a"/> if <paramref name="b"/> replaces it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three set relationships, each from <paramref name="a"/>'s point of view: the strings
	/// each accepts, the (text, value) pairs each defines, and the strings each prints. A change
	/// is safe for stored values exactly when <see cref="NaxpComparison.Encoding"/> is
	/// <see cref="SetRelationship.Equal"/> or <see cref="SetRelationship.SubsetOf"/>. See
	/// <see cref="NaxpComparison"/> for how the three relate.
	/// </para>
	/// <para>
	/// The encoding relationship is decided by walking the two naxps together, which has a
	/// budget. No naxp anybody has reason to write comes near it, but a pair that does cannot be
	/// decided, and this throws rather than guess; <see cref="TryCompare(Naxp, Naxp, out NaxpComparison)"/> returns
	/// <see langword="false"/> instead. The other two relationships are always decidable.
	/// </para>
	/// </remarks>
	/// <param name="a">The naxp the data was encoded with.</param>
	/// <param name="b">The naxp proposed to replace it.</param>
	/// <returns>The comparison.</returns>
	/// <exception cref="ArgumentNullException">Either naxp is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The encoding relationship could not be decided within the budget.
	/// </exception>
	public static NaxpComparison Compare(Naxp a, Naxp b) => Compare(a, b, ValueAgreement.MaxTuples);

	/// <summary>
	/// Tries to find how <paramref name="b"/> stands to <paramref name="a"/>.
	/// </summary>
	/// <param name="a">The naxp the data was encoded with.</param>
	/// <param name="b">The naxp proposed to replace it.</param>
	/// <param name="comparison">
	/// The comparison, if this returns <see langword="true"/>; otherwise the default, which is
	/// <see cref="SetRelationship.Incomparable"/> on every axis and claims nothing.
	/// </param>
	/// <returns>
	/// Whether the comparison was decided. Only the encoding relationship can fail to be, when
	/// the walk that decides it outgrows its budget.
	/// </returns>
	/// <exception cref="ArgumentNullException">Either naxp is <see langword="null"/>.</exception>
	public static bool TryCompare(Naxp a, Naxp b, out NaxpComparison comparison)
		=> TryCompare(a, b, out comparison, ValueAgreement.MaxTuples);

	/// <summary>
	/// The lowest value both naxps hold that they decode to different strings, or zero where
	/// every value both hold decodes alike.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a different question from <see cref="Compare(Naxp, Naxp)"/>, which is about the text going
	/// in; this is about the text coming out. <c>(A|B)!A</c> and <c>(A|B)!B</c> have equal
	/// encodings and diverge at 1, decoding it to <c>A</c> and to <c>B</c>. Decode the value
	/// under each naxp to see the two forms.
	/// </para>
	/// <para>
	/// Values only one naxp holds do not count, so a naxp extended by values that sort after all
	/// of its own gives zero however many were added; <see cref="MaxEncodedValue"/> says the
	/// rest.
	/// </para>
	/// </remarks>
	/// <param name="a">The first naxp.</param>
	/// <param name="b">The second naxp.</param>
	/// <returns>The value, or zero for none.</returns>
	/// <exception cref="ArgumentNullException">Either naxp is <see langword="null"/>.</exception>
	public static ulong FirstDivergentValue(Naxp a, Naxp b)
	{
		if (a is null) { throw new ArgumentNullException(nameof(a)); }
		if (b is null) { throw new ArgumentNullException(nameof(b)); }

		return Relations.FirstDivergentValue(a.compilation.Canonical, b.compilation.Canonical);
	}

	/// <summary>
	/// <see cref="Compare(Naxp, Naxp)"/> with the budget exposed, so that a test can reach the
	/// undecided path without a naxp large enough to exhaust the real one.
	/// </summary>
	internal static NaxpComparison Compare(Naxp a, Naxp b, int budget)
		=> TryCompare(a, b, out NaxpComparison comparison, budget)
			? comparison
			: throw new InvalidOperationException(
				"The relationship between the encodings of these two naxps could not be decided within the budget. Their accepted text and printed text can still be compared.")
			;

	/// <summary>
	/// <see cref="TryCompare(Naxp, Naxp, out NaxpComparison)"/> with the budget exposed.
	/// </summary>
	internal static bool TryCompare(Naxp a, Naxp b, out NaxpComparison comparison, int budget)
	{
		if (a is null) { throw new ArgumentNullException(nameof(a)); }
		if (b is null) { throw new ArgumentNullException(nameof(b)); }

		comparison = default;

		Compilation left = a.compilation;
		Compilation right = b.compilation;

		// First, because it is the one that can fail, and nothing is worth computing if it does.
		if (!Relations.TryCompareEncodings(left, right, out SetRelationship encoding, budget)) { return false; }

		comparison = new NaxpComparison(
			Relations.CompareLanguages(left.Accepted, right.Accepted),
			encoding,
			Relations.CompareLanguages(left.Canonical, right.Canonical));

		return true;
	}
	#endregion
	#region Public overrides
	/// <inheritdoc/>
	public override string ToString() => this.Pattern;
	#endregion
	#region Private methods
	/// <summary>
	/// Copies ASCII bytes into characters.
	/// </summary>
	/// <remarks>
	/// A byte of 0x80 or above becomes a character no naxp can name, so it is invalid further
	/// down rather than needing a check here.
	/// </remarks>
	static void Widen(ReadOnlySpan<byte> pattern, Span<char> destination)
	{
		for (int i = 0; i < pattern.Length; ++i) { destination[i] = (char)pattern[i]; }
	}
	#endregion
}
