// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace LogMu.Generator;

/// <summary>
/// A place in the user's source, in the form a model may hold.
/// </summary>
/// <remarks>
/// A <see cref="Location"/> holds its syntax tree, and a tree changes with every keystroke, so a
/// model holding one would compare unequal on every edit and cache nothing. These three fields
/// rebuild the same location without that.
/// </remarks>
readonly record struct LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
	/// <summary>The location this describes.</summary>
	public Location ToLocation() => Location.Create(this.FilePath, this.Span, this.LineSpan);

	/// <summary>The description of a syntax node's location, or null where it has none in source.</summary>
	public static LocationInfo? From(SyntaxNode? node) => From(node?.GetLocation());

	/// <summary>
	/// The description of a single token's location. A type declaration's own span runs from its
	/// first attribute to its closing brace, so a message about the type points at the name.
	/// </summary>
	public static LocationInfo? From(SyntaxToken token) => From(token.GetLocation());

	/// <summary>The description of a location, or null where it is not in source.</summary>
	public static LocationInfo? From(Location? location)
	{
		if (location is null || location.SourceTree is null) { return null; }

		FileLinePositionSpan lineSpan = location.GetLineSpan();

		return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, lineSpan.Span);
	}
}

/// <summary>A diagnostic found while building the model, in the form a model may hold.</summary>
sealed record DiagnosticInfo(Rule Rule, LocationInfo? Location, EquatableArray<string> Arguments)
{
	/// <summary>Builds a diagnostic with its arguments already formatted.</summary>
	public static DiagnosticInfo Create(Rule rule, LocationInfo? location, params string[] arguments)
		=> new(rule, location, arguments.ToEquatableArray());

	/// <summary>The diagnostic this describes.</summary>
	public Diagnostic ToDiagnostic()
		=> Rules.Create(this.Rule, this.Location?.ToLocation(), this.Arguments.Items.Cast<object?>().ToArray());
}

/// <summary>
/// Where in the user's source one naxp's text sits, character by character.
/// </summary>
/// <remarks>
/// <see cref="Offsets"/> holds, for each character of the naxp, its offset from the start of the
/// literal token, with one extra entry for the position just past the last character. A naxp
/// written as an ordinary literal is escaped - <c>"\\A\\9"</c> for <c>\A\9</c> - so a refusal at
/// naxp offset 2 has to be moved to source offset 5 before it can be pointed at. Where the
/// literal cannot be mapped, which is anything other than a single-line string literal, the array
/// is empty and the whole literal is pointed at instead.
/// </remarks>
sealed record NaxpText(LocationInfo Location, EquatableArray<int> Offsets)
{
	/// <summary>
	/// The location to point a refusal at, given the span it names within the naxp.
	/// </summary>
	/// <remarks>
	/// A zero offset with a zero length is the library's way of saying the fault belongs to the
	/// naxp as a whole, so the whole literal is pointed at rather than its first character.
	/// </remarks>
	/// <param name="offset">Where the fault starts within the naxp.</param>
	/// <param name="length">How many of the naxp's characters are at fault.</param>
	public Location At(int offset, int length)
	{
		if (this.Offsets.Count == 0 || (offset == 0 && length == 0))
		{
			return this.Location.ToLocation();
		}

		int index = Math.Max(0, Math.Min(offset, this.Offsets.Count - 1));
		int last = Math.Max(index, Math.Min(offset + Math.Max(1, length), this.Offsets.Count - 1));
		int start = this.Offsets[index];
		int end = last > index
			? this.Offsets[last]
			: (index + 1 < this.Offsets.Count ? this.Offsets[index + 1] : start + 1)
			;

		var span = new TextSpan(this.Location.Span.Start + start, Math.Max(1, end - start));

		// The mapping is only built for a literal on one line, so the line is the literal's own
		// and the column is simply shifted.
		LinePosition first = this.Location.LineSpan.Start;
		var lineSpan = new LinePositionSpan(
			new LinePosition(first.Line, first.Character + start),
			new LinePosition(first.Line, first.Character + start + span.Length));

		return Microsoft.CodeAnalysis.Location.Create(this.Location.FilePath, span, lineSpan);
	}
}

/// <summary>One <c>[Naxp]</c> attribute, as much of it as survives validation.</summary>
sealed record NaxpSpec(
	string Naxp,
	string Prefix,
	NaxpValueType ValueType,
	NaxpText Text,
	LocationInfo AttributeLocation,
	LocationInfo ValueTypeLocation);

/// <summary>
/// One partial type declaration carrying naxps, and everything needed to write the generated
/// half of it.
/// </summary>
/// <remarks>
/// <see cref="TypeHeaders"/> are the declarations to reopen, outermost first, copied from the
/// user's own source - <c>internal static partial class Codes</c> and the like - so the generated
/// part agrees with theirs whatever they wrote.
/// </remarks>
sealed record TypeModel(
	string? Namespace,
	EquatableArray<string> TypeHeaders,
	string DisplayName,
	EquatableArray<NaxpSpec> Specs,
	EquatableArray<DiagnosticInfo> Diagnostics,
	string HintName,
	bool CanGenerate);
