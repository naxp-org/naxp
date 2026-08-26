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
/// NAXP00xx is the attribute and its surroundings, which the generator can see for itself.
/// NAXP0101 is the naxp itself. There is one identifier rather than one per rule of the language,
/// because the library's own code - NAXP1002_IntervalComma and its like - is carried in the
/// message and says more than a rule name would. Every one of these is an error: each stops one
/// naxp being generated, and generated code that silently went missing would fail later and
/// further away.
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
/// NAXP0101  NaxpRefused           Error     The naxp was refused
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
	NaxpRefused,
}

/// <summary>The descriptors behind <see cref="Rule"/>, and the shorthand for reporting one.</summary>
static class Rules
{
	const string Category = "Usage";

	/// <summary>
	/// One descriptor per <see cref="Rule"/>, in the same order.
	/// </summary>
	/// <remarks>
	/// No help link: naxp.org has no page for these yet. Add helpLinkUri here, not in a dozen
	/// places, once it does.
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
			"This naxp encodes {0} values, which does not fit {1}. Use ValueType = typeof({2}) or wider, or narrow the naxp."),
		Error(
			"NAXP0009",
			"The naxp generator failed",
			"The naxp generator failed on this naxp, which is a fault in naxp rather than in your code: {0} Please report it at https://github.com/naxp-org/naxp/issues."),
		Error(
			"NAXP0010",
			"A file-local type cannot hold a naxp",
			"'{0}' is a file-local type, and the generated code goes in a file of its own, which cannot be part of it. Drop the 'file' modifier."),
		Error(
			"NAXP0101",
			"The naxp was refused",
			"{0}: {1}"));

	/// <summary>The descriptor for a rule.</summary>
	public static DiagnosticDescriptor Descriptor(Rule rule) => Descriptors[(int)rule];

	/// <summary>The identifier a build log shows for a rule, such as <c>NAXP0104</c>.</summary>
	public static string Id(Rule rule) => Descriptor(rule).Id;

	/// <summary>Reports a rule at a location, with the arguments its message takes.</summary>
	public static Diagnostic Create(Rule rule, Location? location, params object?[] arguments)
		=> Diagnostic.Create(Descriptor(rule), location ?? Location.None, arguments);

	static DiagnosticDescriptor Error(string id, string title, string messageFormat)
		=> new(id, title, messageFormat, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
}
