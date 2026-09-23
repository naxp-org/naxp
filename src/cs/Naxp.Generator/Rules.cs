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
/// NAXP00xx is the attribute and its surroundings, which the generator can see for itself. A
/// fault in the naxp itself is not in this table: it is reported under the library's own code,
/// NAXP1002 and its like, by <see cref="Rules.NaxpFault"/>. That code names the rule broken, so
/// putting it in the identifier rather than inside the message leaves one code where a reader
/// expects one, and lets a build suppress a single rule. Every one of these is an error: each
/// stops one naxp being generated, and generated code that silently went missing would fail later
/// and further away.
/// </para>
/// <code>
/// Id        Rule                  Severity  Title
/// NAXP0001  NotPartial            Error     A type holding a naxp must be partial
/// NAXP0002  ContainerNotPartial   Error     A type holding a naxp must be nested in partial types
/// NAXP0003  GenericType           Error     A type holding a naxp must not be generic
/// NAXP0004  NaxpMissing           Error     The naxp is missing
/// NAXP0005  PrefixNotIdentifier   Error     The prefix is not an ASCII identifier
/// NAXP0006  PrefixNotUnique       Error     Two naxps in one type share a prefix
/// NAXP0007  ValueTypeUnknown      Error     The value type is not an integer type
/// NAXP0008  ValueTypeTooNarrow    Error     The naxp does not fit the value type
/// NAXP0009  GeneratorFailed       Error     The naxp generator failed
/// NAXP0010  FileLocalType         Error     A file-local type cannot hold a naxp
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
			"NAXP0001",
			"A type holding a naxp must be partial",
			"'{0}' carries [Naxp] but is not partial, so there is nowhere to put the generated code. Add the 'partial' modifier to '{0}'."),
		Error(
			"NAXP0002",
			"A type holding a naxp must be nested in partial types",
			"'{0}' carries [Naxp] but the type it sits in, '{1}', is not partial. Add the 'partial' modifier to '{1}' as well."),
		Error(
			"NAXP0003",
			"A type holding a naxp must not be generic",
			"'{0}' is generic, and every constructed type would get its own copy of the generated tables and constants. Move the naxp to a type without type parameters."),
		Error(
			"NAXP0004",
			"The naxp is missing",
			"[Naxp] takes the naxp itself as its first argument, and this one is null. Write the naxp as a string, such as [Naxp(@\"\\A\\9\\X \\s! \\9\\A\\A\", typeof(int))]."),
		Error(
			"NAXP0005",
			"The prefix is not an ASCII identifier",
			"Prefix = \"{0}\" cannot start the generated member names: {1} Use ASCII letters, digits and underscores, starting with a letter or an underscore."),
		Error(
			"NAXP0006",
			"Two naxps in one type share a prefix",
			"'{0}' has two naxps with Prefix = \"{1}\", which would generate two members called '{1}Accepts'. Give each naxp in a type a different Prefix."),
		Error(
			"NAXP0007",
			"The value type is not an integer type",
			"{0} is not a type a naxp can encode to. Write typeof of an integer type: " + ValueTypes.Choices + "."),
		Error(
			"NAXP0008",
			"The naxp does not fit the value type",
			"This naxp encodes {0} values, which does not fit {1}. Pass typeof({2}) or wider as the value type, or narrow the naxp."),
		Error(
			"NAXP0009",
			"The naxp generator failed",
			"The naxp generator failed on this naxp, which is a fault in naxp rather than in your code: {0} Please report it at https://github.com/naxp-org/naxp/issues."),
		Error(
			"NAXP0010",
			"A file-local type cannot hold a naxp",
			"'{0}' is a file-local type, and the generated code goes in a file of its own, which cannot be part of it. Drop the 'file' modifier."));

	/// <summary>The descriptor for a rule.</summary>
	public static DiagnosticDescriptor Descriptor(Rule rule) => Descriptors[(int)rule];

	/// <summary>The identifier a build log shows for a rule, such as <c>NAXP0008</c>.</summary>
	public static string Id(Rule rule) => Descriptor(rule).Id;

	/// <summary>Reports a rule at a location, with the arguments its message takes.</summary>
	public static Diagnostic Create(Rule rule, Location? location, params object?[] arguments)
		=> Diagnostic.Create(Descriptor(rule), location ?? Location.None, arguments);

	/// <summary>Reports a fault in the naxp itself, under the library's own code for it.</summary>
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
	/// <param name="location">Where in the naxp the fault is.</param>
	public static Diagnostic NaxpFault(string code, string message, Location? location)
		=> Diagnostic.Create(
			new DiagnosticDescriptor(code, "The naxp is invalid", "{0}", Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help(code)),
			location ?? Location.None,
			message);

	static DiagnosticDescriptor Error(string id, string title, string messageFormat)
		=> new(id, title, messageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help(id));

	/// <summary>Where a code is documented, which every row of that page is an anchor of.</summary>
	static string Help(string id) => "https://naxp.org/codes/#" + id.ToLowerInvariant();
}
