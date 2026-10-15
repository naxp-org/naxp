// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LogMu.Generator;

/// <summary>
/// Every diagnostic the generator can report, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The table below is the whole set. Keep <see cref="Rule"/> and <see cref="Descriptors"/> in the
/// same order, and keep this comment in step with both - adding a rule should be three lines in
/// this one file.
/// </para>
/// <para>
/// NAXP2xxx is the attribute and its surroundings, which the generator can see for itself. The
/// leading digit names the family: NAXP1xxx is an error in a naxp, which every implementation
/// reports alike, and NAXP2xxx is one only this generator reports.
/// </para>
/// <para>
/// An error in the naxp itself is not in this table: it is reported under the library's own code,
/// NAXP1002 and its like, by <see cref="Rules.NaxpError"/>. That code names the rule broken, so
/// putting it in the identifier rather than inside the message leaves one code where a reader
/// expects one, and lets a build suppress a single rule. Every one of these is an error: each
/// stops one naxp being generated, and generated code that silently went missing would fail later
/// and further away.
/// </para>
/// <code>
/// Id        Rule                  Severity  Title
/// NAXP2001  NotPartial            Error     A type holding a naxp must be partial
/// NAXP2002  ContainerNotPartial   Error     A type holding a naxp must be nested in partial types
/// NAXP2003  GenericType           Error     A type holding a naxp must not be generic
/// NAXP2004  NaxpMissing           Error     The naxp is missing
/// NAXP2005  PrefixNotIdentifier   Error     The prefix is not an ASCII identifier
/// NAXP2006  PrefixNotUnique       Error     Two naxps in one type share a prefix
/// NAXP2007  ValueTypeUnknown      Error     The encoded value type is not an integer type
/// NAXP2008  ValueTypeTooNarrow    Error     The naxp does not fit the encoded value type
/// NAXP2009  GeneratorFailed       Error     The naxp generator failed
/// NAXP2010  FileLocalType         Error     A file-local type cannot hold a naxp
/// </code>
/// </remarks>
enum Rule
{
	NotPartial,
	ContainerNotPartial,
	GenericType,
	NaxpMissing,
	PrefixNotIdentifier,
	PrefixNotUnique,
	ValueTypeUnknown,
	ValueTypeTooNarrow,
	GeneratorFailed,
	FileLocalType,
}

/// <summary>The descriptors behind <see cref="Rule"/>, and the shorthand for reporting one.</summary>
static class Rules
{
	const string Category = "Usage";

	/// <summary>
	/// One descriptor per <see cref="Rule"/>, in the same order.
	/// </summary>
	/// <remarks>
	/// The help link is built from the id by <see cref="Help"/>, so Visual Studio turns the code
	/// in the Error List into a link to the page documenting it.
	/// </remarks>
	static readonly ImmutableArray<DiagnosticDescriptor> Descriptors = ImmutableArray.Create(
		Error(
			"NAXP2001",
			"A type holding a naxp must be partial",
			"'{0}' is annotated with [Naxp] but is not a partial class or struct. Add the 'partial' modifier to '{0}'."),
		Error(
			"NAXP2002",
			"A type holding a naxp must be nested in partial types",
			"'{0}' is annotated with [Naxp] but its containing type, '{1}', is not a partial class or struct. Add the 'partial' modifier to '{1}' as well."),
		Error(
			"NAXP2003",
			"A type holding a naxp must not be generic",
			"The [Naxp] attribute cannot be applied to a generic type such as '{0}'."),
		Error(
			"NAXP2004",
			"The naxp is missing",
			"Missing or null naxp argument. It should look something like '[Naxp(@\"\\A\\9\\X \\s!! \\9\\A\\A\", typeof(int))]'."),
		Error(
			"NAXP2005",
			"The prefix is not an ASCII identifier",
			"Prefix = \"{0}\" cannot start the generated member names: {1} Use ASCII letters, digits and underscores, starting with a letter or an underscore."),
		Error(
			"NAXP2006",
			"Two naxps in one type share a prefix",
			"'{0}' has two naxps with Prefix = \"{1}\", which would generate two members called '{1}Accepts'. Give each naxp in the same class or struct a different Prefix."),
		Error(
			"NAXP2007",
			"The encoded value type is not an integer type",
			"{0} is not a valid encoded value type. Pass typeof of an integer type, e.g. 'typeof(int)'."),
		Error(
			"NAXP2008",
			"The naxp does not fit the encoded value type",
			"This naxp encodes {0} values, which does not fit into {1}. Pass 'typeof({2})' or wider as the encoded value type (or change the naxp to produce a smaller number of encoded values)."),
		Error(
			"NAXP2009",
			"The naxp generator failed",
			"The naxp generator failed on this naxp. This is likely a bug in naxp rather than in your code: {0} Please report it at https://github.com/naxp-org/naxp/issues."),
		Error(
			"NAXP2010",
			"A file-local type cannot hold a naxp",
			"'{0}' is a file-local type, which will not work with the source generator. Drop the 'file' modifier."));

	/// <summary>The descriptor for a rule.</summary>
	public static DiagnosticDescriptor Descriptor(Rule rule) => Descriptors[(int)rule];

	/// <summary>The identifier a build log shows for a rule, such as <c>NAXP2008</c>.</summary>
	public static string Id(Rule rule) => Descriptor(rule).Id;

	/// <summary>Reports a rule at a location, with the arguments its message takes.</summary>
	public static Diagnostic Create(Rule rule, Location? location, params object?[] arguments)
		=> Diagnostic.Create(Descriptor(rule), location ?? Location.None, arguments);

	/// <summary>Reports an error in the naxp itself, under the library's own code for it.</summary>
	/// <remarks>
	/// The descriptor is built here rather than taken from the table above, because the set of
	/// codes is the language's rather than the generator's: NAXP1002, NAXP1031 and the rest. A
	/// generator may do this, having no fixed set to declare as an analyzer does.
	/// <para>
	/// The message goes in as an argument against a format of <c>{0}</c> rather than as the format
	/// itself. A message can name a naxp - 'Write 'A{2,5}'.' is one - and braces in a format
	/// string are substitutions, so passing one as the format throws where it does not corrupt.
	/// </para>
	/// </remarks>
	/// <param name="code">The library's code, such as <c>NAXP1031</c>.</param>
	/// <param name="message">What is wrong, and where practical what to write instead.</param>
	/// <param name="location">Where in the naxp the error is.</param>
	public static Diagnostic NaxpError(string code, string message, Location? location)
		=> Diagnostic.Create(
			new DiagnosticDescriptor(code, "The naxp is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help(code)),
			location ?? Location.None,
			message);

	static DiagnosticDescriptor Error(string id, string title, string messageFormat)
		=> new(id, title, messageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help(id));

	/// <summary>Where a code is documented, which every row of that page is an anchor of.</summary>
	static string Help(string id) => "https://naxp.org/error-codes/#" + id.ToLowerInvariant();
}
