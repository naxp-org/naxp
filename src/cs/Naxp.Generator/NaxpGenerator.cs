// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using NaxpCompilation = LogMu.Compilation;

namespace LogMu.Generator;

/// <summary>
/// Turns <c>[Naxp]</c> on a partial type into the recogniser and codec for that naxp,
/// written as members of the type.
/// </summary>
/// <remarks>
/// <para>
/// The work splits the way an incremental generator's work has to. The transform reads syntax and
/// symbols and produces a <see cref="TypeModel"/> holding nothing but values, so an edit
/// elsewhere in the file compares equal and regenerates nothing. Compiling the naxp itself, which
/// is the expensive half, happens in the output stage against that model.
/// </para>
/// <para>
/// One generated file holds one partial declaration and every naxp on it. A type declared in two
/// files, each with naxps, gets one generated file for each.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class NaxpGenerator : IIncrementalGenerator
{
	/// <summary>
	/// The name of the pipeline step that produces the models, which is how a test asks whether an
	/// edit elsewhere regenerated anything.
	/// </summary>
	public const string ModelStepName = "NaxpTypes";

	/// <inheritdoc/>
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterPostInitializationOutput(
			static ctx => ctx.AddSource(AttributeSource.HintName, SourceText.From(AttributeSource.Text, Encoding.UTF8)));

		IncrementalValuesProvider<TypeModel> models = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				AttributeSource.AttributeMetadataName,
				predicate: static (node, _) => node is TypeDeclarationSyntax,
				transform: static (ctx, token) => Transform(ctx, token))
			.Where(static model => model is not null)
			.Select(static (model, _) => model!)
			.WithTrackingName(ModelStepName);

		context.RegisterSourceOutput(models, static (ctx, model) => Emit(ctx, model));
	}

	#region Reading the source
	static TypeModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
	{
		if (context.TargetNode is not TypeDeclarationSyntax declaration
			|| context.TargetSymbol is not INamedTypeSymbol symbol)
		{
			return null;
		}

		var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
		bool canGenerate = ReadTypeShape(declaration, symbol, diagnostics, out ImmutableArray<string> headers);
		ImmutableArray<NaxpSpec> specs = ReadSpecs(context, declaration, symbol, diagnostics, cancellationToken);

		if (specs.Length == 0 && diagnostics.Count == 0) { return null; }

		return new TypeModel(
			symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
			headers.AsEquatable(),
			symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
			specs.AsEquatable(),
			diagnostics.ToImmutable().AsEquatable(),
			HintName(symbol, declaration),
			canGenerate && specs.Length != 0);
	}

	/// <summary>
	/// Checks that the generated half of this type can be written at all, and collects the
	/// declarations to reopen, outermost first.
	/// </summary>
	static bool ReadTypeShape(
		TypeDeclarationSyntax declaration,
		INamedTypeSymbol symbol,
		ImmutableArray<DiagnosticInfo>.Builder diagnostics,
		out ImmutableArray<string> headers)
	{
		string name = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
		var reversed = new List<string>();
		bool canGenerate = true;

		for (TypeDeclarationSyntax? current = declaration;
			current is not null;
			current = current.Parent as TypeDeclarationSyntax)
		{
			bool isTarget = ReferenceEquals(current, declaration);
			LocationInfo? location = LocationInfo.From(current.Identifier);

			if (!current.Modifiers.Any(SyntaxKind.PartialKeyword))
			{
				diagnostics.Add(isTarget
					? DiagnosticInfo.Create(Rule.NotPartial, location, name)
					: DiagnosticInfo.Create(Rule.ContainerNotPartial, location, name, current.Identifier.ValueText));
				canGenerate = false;
			}

			if (current.Modifiers.Any(SyntaxKind.FileKeyword))
			{
				diagnostics.Add(DiagnosticInfo.Create(Rule.FileLocalType, location, current.Identifier.ValueText));
				canGenerate = false;
			}

			if (current.TypeParameterList is { Parameters.Count: > 0 })
			{
				diagnostics.Add(DiagnosticInfo.Create(Rule.GenericType, location, current.Identifier.ValueText));
				canGenerate = false;
			}

			reversed.Add(Header(current));
		}

		reversed.Reverse();
		headers = ImmutableArray.CreateRange(reversed);

		return canGenerate;
	}

	/// <summary>The declaration as it has to be reopened: the user's own modifiers and keywords.</summary>
	static string Header(TypeDeclarationSyntax declaration)
	{
		var builder = new StringBuilder();

		foreach (SyntaxToken modifier in declaration.Modifiers)
		{
			builder.Append(modifier.ValueText).Append(' ');
		}

		builder.Append(declaration.Keyword.ValueText);

		if (declaration is RecordDeclarationSyntax record
			&& !record.ClassOrStructKeyword.IsKind(SyntaxKind.None))
		{
			builder.Append(' ').Append(record.ClassOrStructKeyword.ValueText);
		}

		return builder.Append(' ').Append(declaration.Identifier.ValueText).ToString();
	}

	/// <summary>Reads the attributes written on this one declaration.</summary>
	static ImmutableArray<NaxpSpec> ReadSpecs(
		GeneratorAttributeSyntaxContext context,
		TypeDeclarationSyntax declaration,
		INamedTypeSymbol symbol,
		ImmutableArray<DiagnosticInfo>.Builder diagnostics,
		CancellationToken cancellationToken)
	{
		var specs = ImmutableArray.CreateBuilder<NaxpSpec>();
		var prefixes = new HashSet<string>(StringComparer.Ordinal);

		foreach (AttributeData attribute in context.Attributes)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// A partial type declared in two files fires this transform once per declaration, both
			// times carrying every attribute on the symbol. Each firing takes only its own.
			if (attribute.ApplicationSyntaxReference is not { } reference
				|| reference.SyntaxTree != declaration.SyntaxTree
				|| !declaration.Span.Contains(reference.Span)
				|| reference.GetSyntax(cancellationToken) is not AttributeSyntax syntax
				|| LocationInfo.From(syntax) is not { } attributeLocation)
			{
				continue;
			}

			ImmutableArray<ExpressionSyntax?> positional = PositionalArguments(syntax);

			string? naxp = attribute.ConstructorArguments.Length == 2
				? attribute.ConstructorArguments[0].Value as string
				: null;

			if (naxp is null)
			{
				diagnostics.Add(DiagnosticInfo.Create(Rule.NaxpMissing, attributeLocation));
				continue;
			}

			// The second constructor argument is required, so the compiler has already refused an
			// attribute without one. Only its type is ours to check.
			TypedConstant written = attribute.ConstructorArguments[1];
			LocationInfo valueTypeLocation =
				LocationInfo.From(positional.Length > 1 ? positional[1] : null) ?? attributeLocation;

			if (written.Value is not ITypeSymbol writtenType
				|| !ValueTypes.TryFrom(writtenType, out NaxpValueType valueType))
			{
				diagnostics.Add(DiagnosticInfo.Create(
					Rule.ValueTypeUnknown,
					valueTypeLocation,
					written.Value is ITypeSymbol unusable
						? $"typeof({unusable.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)})"
						: "null"));
				continue;
			}

			string prefix = "";
			LocationInfo? prefixLocation = null;

			foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
			{
				if (argument.Key == "Prefix")
				{
					prefix = argument.Value.Value as string ?? "";
					prefixLocation = ArgumentLocation(syntax, "Prefix");
				}
			}

			if (prefix.Length != 0 && !Emitter.TryValidateIdentifier(prefix, out string? reason))
			{
				diagnostics.Add(DiagnosticInfo.Create(
					Rule.PrefixNotIdentifier,
					prefixLocation ?? attributeLocation,
					prefix,
					reason!));
				continue;
			}

			if (!prefixes.Add(prefix))
			{
				diagnostics.Add(DiagnosticInfo.Create(
					Rule.PrefixNotUnique,
					prefixLocation ?? attributeLocation,
					symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
					prefix));
				continue;
			}

			ExpressionSyntax? text = positional.Length > 0 ? positional[0] : null;

			ImmutableArray<int> offsets = text is null
				? ImmutableArray<int>.Empty
				: Literals.MapOffsets(text, naxp);

			specs.Add(new NaxpSpec(
				naxp,
				prefix,
				valueType,
				new NaxpText(LocationInfo.From(text) ?? attributeLocation, offsets.AsEquatable()),
				attributeLocation,
				valueTypeLocation));
		}

		return specs.ToImmutable();
	}

	/// <summary>
	/// The attribute's positional arguments in order, so that a message can point at the naxp or
	/// at the type beside it. An argument written by name, <c>naxp:</c> and the like, keeps its
	/// place in the constructor rather than in the source, so it is left out.
	/// </summary>
	static ImmutableArray<ExpressionSyntax?> PositionalArguments(AttributeSyntax syntax)
	{
		if (syntax.ArgumentList is null) { return ImmutableArray<ExpressionSyntax?>.Empty; }

		ImmutableArray<ExpressionSyntax?>.Builder arguments = ImmutableArray.CreateBuilder<ExpressionSyntax?>();

		foreach (AttributeArgumentSyntax argument in syntax.ArgumentList.Arguments)
		{
			if (argument.NameEquals is null && argument.NameColon is null)
			{
				arguments.Add(argument.Expression);
			}
		}

		return arguments.ToImmutable();
	}

	/// <summary>The location of one named argument of an attribute, for pointing a message at.</summary>
	static LocationInfo? ArgumentLocation(AttributeSyntax syntax, string name)
		=> LocationInfo.From(syntax.ArgumentList?.Arguments
			.FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == name));

	/// <summary>
	/// The name of the generated file, which has to be unique across the compilation. A type
	/// declared in more than one file gets its part's index, because each part generates a file.
	/// </summary>
	static string HintName(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration)
	{
		var builder = new StringBuilder();

		foreach (char c in symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
		{
			builder.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' ? c : '_');
		}

		ImmutableArray<SyntaxReference> parts = symbol.DeclaringSyntaxReferences;

		if (parts.Length > 1)
		{
			int index = 0;

			for (int i = 0; i < parts.Length; i++)
			{
				if (parts[i].SyntaxTree == declaration.SyntaxTree && parts[i].Span == declaration.Span)
				{
					index = i;
					break;
				}
			}

			builder.Append('.').Append(index.ToString(CultureInfo.InvariantCulture));
		}

		return builder.Append(".Naxp.g.cs").ToString();
	}
	#endregion
	#region Writing the code
	static void Emit(SourceProductionContext context, TypeModel model)
	{
		foreach (DiagnosticInfo info in model.Diagnostics)
		{
			context.ReportDiagnostic(info.ToDiagnostic());
		}

		if (!model.CanGenerate) { return; }

		var builder = new StringBuilder();

		builder.AppendLine("// <auto-generated/>");
		builder.AppendLine(
			$"// Written by the naxp source generator, version {Emitter.PackageVersion()}. Do not edit.");
		builder.AppendLine();

		int depth = 0;

		if (model.Namespace is not null)
		{
			builder.Append("namespace ").AppendLine(model.Namespace);
			builder.AppendLine("{");
			depth++;
		}

		foreach (string header in model.TypeHeaders)
		{
			builder.Append(Indent(depth)).AppendLine(header);
			builder.Append(Indent(depth)).AppendLine("{");
			depth++;
		}

		bool wroteAny = false;

		foreach (NaxpSpec spec in model.Specs)
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			if (wroteAny) { builder.AppendLine(); }

			wroteAny |= TryEmit(context, spec, builder, depth);
		}

		while (depth > 0)
		{
			depth--;
			builder.Append(Indent(depth)).AppendLine("}");
		}

		if (wroteAny)
		{
			context.AddSource(model.HintName, SourceText.From(builder.ToString(), Encoding.UTF8));
		}
	}

	/// <summary>Compiles one naxp and writes its fragment, or reports why it cannot be.</summary>
	static bool TryEmit(SourceProductionContext context, NaxpSpec spec, StringBuilder builder, int depth)
	{
		try
		{
			if (!Compiler.TryCompile(spec.Naxp.AsSpan(), out NaxpCompilation? compilation, out NaxpError? error))
			{
				NaxpError refusal = error!.Value;

				context.ReportDiagnostic(Rules.Create(
					Rule.NaxpRefused,
					spec.Text.At(refusal.Offset, refusal.Length),
					refusal.Code,
					refusal.Text));

				return false;
			}

			if (compilation!.ValueCount > Emitter.Capacity(spec.ValueType))
			{
				context.ReportDiagnostic(Rules.Create(
					Rule.ValueTypeTooNarrow,
					spec.ValueTypeLocation.ToLocation(),
					// Invariant and unseparated, as everywhere else a count reaches a message.
					compilation.ValueCount.ToString(CultureInfo.InvariantCulture),
					ValueTypes.Keyword(spec.ValueType),
					ValueTypes.Keyword(ValueTypes.Narrowest(compilation.ValueCount))));

				return false;
			}

			string indent = Indent(depth);

			builder.Append(indent).Append("// The naxp ").Append(Emitter.CommentText(spec.Naxp));
			builder.AppendLine(spec.Prefix.Length == 0 ? "." : $", as {spec.Prefix}.");

			CSharpEmitter.Instance.Emit(compilation, spec.Prefix, builder, spec.ValueType, indent);

			return true;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			context.ReportDiagnostic(Rules.Create(
				Rule.GeneratorFailed,
				spec.AttributeLocation.ToLocation(),
				exception.Message));

			return false;
		}
	}

	static string Indent(int depth) => new('\t', depth);
	#endregion
}
