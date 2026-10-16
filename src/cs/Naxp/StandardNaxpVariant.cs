// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

namespace LogMu;

/// <summary>
/// One variant of a standard naxp, as <see cref="Naxp.AllStandardNaxps"/> lists them.
/// </summary>
public sealed class StandardNaxpVariant
{
	internal StandardNaxpVariant(string id, string name, string variant, string pattern, string notes)
	{
		this.Id = id;
		this.Name = name;
		this.Variant = variant;
		this.Pattern = pattern;
		this.Notes = notes;
	}

	/// <summary>
	/// The standard's id, such as <c>uk-postcode</c>: lower case letters, digits and single
	/// hyphens.
	/// </summary>
	public string Id { get; }

	/// <summary>The standard's name, for display, such as <c>UK postcode</c>.</summary>
	public string Name { get; }

	/// <summary>
	/// The variant's id, which is <c>default</c> for the variant a standard is used with unless
	/// another is asked for.
	/// </summary>
	public string Variant { get; }

	/// <summary>The variant's naxp.</summary>
	public string Pattern { get; }

	/// <summary>
	/// What the variant accepts and what it prints, in Markdown that uses backticks, <c>*</c> and
	/// <c>**</c> alone. A newline is a line break.
	/// </summary>
	public string Notes { get; }
}
