// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

namespace LogMu;

/// <summary>
/// How one naxp stands to another, as three set relationships, each from the first naxp's
/// point of view.
/// </summary>
/// <remarks>
/// <para>
/// The three are independent and each answers a different question about replacing the
/// first naxp with the second. <see cref="AcceptedText"/> says whether text that was valid
/// stays valid. <see cref="Encoding"/> says whether values already stored still mean what they
/// meant. <see cref="PrintedText"/> says whether what is printed on decoding can change.
/// </para>
/// <para>
/// <see cref="Encoding"/> is the strongest of the three: <see cref="SetRelationship.Equal"/>
/// or <see cref="SetRelationship.SubsetOf"/> there forces the same on
/// <see cref="AcceptedText"/>, since the pairs' texts are the accepted strings. Nothing else
/// follows. Two naxps can give every string the same value and print it differently, as
/// <c>(A|B)!A</c> and <c>(A|B)!B</c> do, and can print the same strings and value a shared one
/// differently.
/// </para>
/// <para>
/// The default value is <see cref="SetRelationship.Incomparable"/> on every axis, which is
/// what a comparison that was never made should say.
/// </para>
/// </remarks>
/// <param name="AcceptedText">
/// How the set of strings the first naxp accepts stands to the second's.
/// </param>
/// <param name="Encoding">
/// How the set of (text, value) pairs the first naxp defines stands to the second's.
/// </param>
/// <param name="PrintedText">
/// How the set of strings the first naxp prints, its canonical forms, stands to the second's.
/// These are compared as sets of strings, so two naxps that print every value in different
/// forms are incomparable here however closely the forms correspond.
/// </param>
public readonly record struct NaxpComparison(
	SetRelationship AcceptedText,
	SetRelationship Encoding,
	SetRelationship PrintedText);
