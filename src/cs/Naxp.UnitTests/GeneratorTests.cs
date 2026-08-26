// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// Driving a source generator needs Roslyn, which the test project only references on net8.0.
#if NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The source generator driven the way the compiler drives it: a compilation in, generated files
/// and diagnostics out.
/// </summary>
public class GeneratorTests
{
	/// <summary>A UK postcode, 1 755 842 400 values, which needs Int32 or wider.</summary>
	const string Postcode = @"\A?\A\9\X? \s \9\A\A";

	/// <summary>A letter and a digit, 260 values, which fits Int16 but not a byte.</summary>
	const string LetterDigit = @"\A\9";

	#region What it generates
	[Fact]
	public void TheAttribute_IsAddedToTheCompilation()
	{
		Run("class Empty { }", out GeneratorResult result);

		Assert.Contains("NaxpAttribute.g.cs", result.FileNames);
		Assert.Contains("internal sealed class NaxpAttribute", result.Source("NaxpAttribute.g.cs"));
	}

	[Fact]
	public void APartialClass_GetsItsNaxpAndCompilesCleanly()
	{
		Run(Source($"[LogMu.Naxp(@\"{Postcode}\", typeof(long), Prefix = \"Postcode\")]", "internal partial class Codes"), out GeneratorResult result);

		result.AssertNoDiagnostics();
		result.AssertCompiles();

		string generated = result.SourceContaining("PostcodeAccepts");

		Assert.Contains("internal partial class Codes", generated);
		Assert.Contains("public const long PostcodeValueCount = 1_755_842_400L;", generated);
		Assert.Contains("public static bool PostcodeAccepts(global::System.ReadOnlySpan<char> text)", generated);
		Assert.Contains("public static string PostcodeDecode(long value)", generated);
	}

	[Fact]
	public void AStaticClass_IsReopenedAsStatic()
	{
		Run(Source($"[LogMu.Naxp(@\"{LetterDigit}\", typeof(short))]", "internal static partial class Codes"), out GeneratorResult result);

		result.AssertNoDiagnostics();
		result.AssertCompiles();
		Assert.Contains("internal static partial class Codes", result.SourceContaining("Accepts"));
	}

	[Fact]
	public void ANestedClass_ReopensEveryContainingType()
	{
		string source = "namespace Example" + Environment.NewLine
			+ "{" + Environment.NewLine
			+ "\tinternal partial class Outer" + Environment.NewLine
			+ "\t{" + Environment.NewLine
			+ $"\t\t[LogMu.Naxp(@\"{LetterDigit}\", typeof(short))]" + Environment.NewLine
			+ "\t\tprivate static partial class Inner" + Environment.NewLine
			+ "\t\t{" + Environment.NewLine
			+ "\t\t}" + Environment.NewLine
			+ "\t}" + Environment.NewLine
			+ "}" + Environment.NewLine;

		Run(source, out GeneratorResult result);

		result.AssertNoDiagnostics();
		result.AssertCompiles();

		string generated = result.SourceContaining("Accepts");

		Assert.Contains("namespace Example", generated);
		Assert.Contains("internal partial class Outer", generated);
		Assert.Contains("private static partial class Inner", generated);
	}

	[Fact]
	public void TwoNaxpsInOneType_AreKeptApartByTheirPrefixes()
	{
		string source = Source(
			$"[LogMu.Naxp(@\"{Postcode}\", typeof(long), Prefix = \"Postcode\")]" + Environment.NewLine
			+ $"[LogMu.Naxp(@\"{LetterDigit}\", typeof(short), Prefix = \"Short\")]",
			"internal partial class Codes");

		Run(source, out GeneratorResult result);

		result.AssertNoDiagnostics();
		result.AssertCompiles();

		string generated = result.SourceContaining("PostcodeAccepts");

		Assert.Contains("public static bool ShortAccepts(global::System.ReadOnlySpan<char> text)", generated);
		Assert.Contains("public const short ShortValueCount = 260;", generated);
	}

	[Fact]
	public void AnEditElsewhere_RegeneratesNothing()
	{
		string naxpSource = Source($"[LogMu.Naxp(@\"{Postcode}\", typeof(long), Prefix = \"Postcode\")]", "internal partial class Codes");
		const string OtherFirst = "internal class Other { }";
		const string OtherSecond = "internal class Other { int field; }";

		CSharpCompilation compilation = Compile(naxpSource, OtherFirst);
		GeneratorDriver driver = Driver().RunGenerators(compilation);

		driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(
			compilation.SyntaxTrees.Last(),
			CSharpSyntaxTree.ParseText(OtherSecond)));

		IncrementalGeneratorRunStep[] steps = driver.GetRunResult().Results
			.SelectMany(result => result.TrackedSteps.TryGetValue(Generator.NaxpGenerator.ModelStepName, out var tracked)
				? tracked
				: [])
			.ToArray();

		Assert.NotEmpty(steps);
		Assert.All(
			steps.SelectMany(step => step.Outputs),
			output => Assert.True(
				output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
				$"The model was recomputed: {output.Reason}."));
	}
	#endregion
	#region What it refuses
	[Fact]
	public void AClassThatIsNotPartial_IsNAXP0001()
	{
		Run(Source($"[LogMu.Naxp(@\"{LetterDigit}\", typeof(short))]", "internal class Codes"), out GeneratorResult result);

		Diagnostic diagnostic = result.Only("NAXP0001");

		Assert.Contains("Add the 'partial' modifier", diagnostic.GetMessage());
		Assert.Empty(result.GeneratedFileNames);
	}

	[Fact]
	public void AContainerThatIsNotPartial_IsNAXP0002()
	{
		string source = "internal class Outer" + Environment.NewLine
			+ "{" + Environment.NewLine
			+ $"\t[LogMu.Naxp(@\"{LetterDigit}\", typeof(short))]" + Environment.NewLine
			+ "\tinternal partial class Codes" + Environment.NewLine
			+ "\t{" + Environment.NewLine
			+ "\t}" + Environment.NewLine
			+ "}" + Environment.NewLine;

		Run(source, out GeneratorResult result);

		Assert.Contains("'Outer'", result.Only("NAXP0002").GetMessage());
	}

	[Fact]
	public void AGenericClass_IsNAXP0003()
	{
		Run(Source($"[LogMu.Naxp(@\"{LetterDigit}\", typeof(short))]", "internal partial class Codes<T>"), out GeneratorResult result);

		result.Only("NAXP0003");
	}

	[Fact]
	public void AFileLocalClass_IsNAXP0010()
	{
		Run(Source($"[LogMu.Naxp(@\"{LetterDigit}\", typeof(short))]", "file partial class Codes"), out GeneratorResult result);

		result.Only("NAXP0010");
	}

	[Fact]
	public void APrefixThatIsNotAnIdentifier_IsNAXP0005()
	{
		Run(
			Source($"[LogMu.Naxp(@\"{LetterDigit}\", typeof(short), Prefix = \"Post code\")]", "internal partial class Codes"),
			out GeneratorResult result);

		Assert.Contains("' '", result.Only("NAXP0005").GetMessage());
	}

	[Fact]
	public void TwoNaxpsWithOnePrefix_IsNAXP0006()
	{
		string source = Source(
			$"[LogMu.Naxp(@\"{Postcode}\", typeof(long), Prefix = \"Code\")]" + Environment.NewLine
			+ $"[LogMu.Naxp(@\"{LetterDigit}\", typeof(short), Prefix = \"Code\")]",
			"internal partial class Codes");

		Run(source, out GeneratorResult result);

		Assert.Contains("CodeAccepts", result.Only("NAXP0006").GetMessage());
	}

	/// <summary>
	/// The count is written invariant and unseparated, as every other count in a message is.
	/// </summary>
	[Fact]
	public void AValueTypeTooNarrow_IsNAXP0008_AndNamesTheNarrowestThatFits()
	{
		Run(
			Source(
				$"[LogMu.Naxp(@\"{Postcode}\", typeof(ushort))]",
				"internal partial class Codes"),
			out GeneratorResult result);

		string message = result.Only("NAXP0008").GetMessage();

		Assert.Contains("1755842400 values", message);
		Assert.Contains("does not fit ushort", message);
		Assert.Contains("typeof(int)", message);
	}

	[Fact]
	public void AValueTypeThatIsNotAnInteger_IsNAXP0007()
	{
		Run(
			Source(
				$"[LogMu.Naxp(@\"{LetterDigit}\", typeof(string))]",
				"internal partial class Codes"),
			out GeneratorResult result);

		Assert.Contains("typeof(string)", result.Only("NAXP0007").GetMessage());
	}

	[Fact]
	public void ANaxpThatDoesNotParse_IsNAXP0101()
	{
		Run(Source("[LogMu.Naxp(@\"\\A(\\9\", typeof(long))]", "internal partial class Codes"), out GeneratorResult result);

		result.Only("NAXP0101");
	}

	/// <summary>
	/// The point of mapping offsets: the squiggle lands on the character of the naxp that the
	/// library refused, inside the literal and past its escapes. Here that is the hyphen of
	/// <c>{2-5}</c>, which the parser refuses because an interval is written <c>{2,5}</c>. The
	/// naxp is written as an ordinary literal, so each of its backslashes is two characters of
	/// source and the unmapped offset would land four characters early.
	/// </summary>
	[Fact]
	public void ARefusal_PointsAtTheCharacterInTheLiteral()
	{
		string source = Source("[LogMu.Naxp(\"\\\\A\\\\9{2-5}\", typeof(long))]", "internal partial class Codes");

		Run(source, out GeneratorResult result);

		Diagnostic diagnostic = result.Only("NAXP0101");
		LinePositionSpan span = diagnostic.Location.GetLineSpan().Span;
		string line = source.Split('\n')[span.Start.Line].TrimEnd('\r');

		Assert.Equal("-", line.Substring(span.Start.Character, span.End.Character - span.Start.Character));
		Assert.Equal(line.IndexOf('-', StringComparison.Ordinal), span.Start.Character);
	}
	#endregion
	#region Driving the generator
	/// <summary>The user's source, wrapped in a namespace with the attribute above the type.</summary>
	static string Source(string attributes, string declaration)
		=> "namespace Example" + Environment.NewLine
		+ "{" + Environment.NewLine
		+ "\t" + attributes.Replace(Environment.NewLine, Environment.NewLine + "\t", StringComparison.Ordinal) + Environment.NewLine
		+ "\t" + declaration + Environment.NewLine
		+ "\t{" + Environment.NewLine
		+ "\t}" + Environment.NewLine
		+ "}" + Environment.NewLine;

	static void Run(string source, out GeneratorResult result)
	{
		CSharpCompilation compilation = Compile(source);

		// Compilation unqualified would be LogMu's own, this test living in LogMu.UnitTests.
		GeneratorDriver driver = Driver().RunGeneratorsAndUpdateCompilation(
			compilation,
			out Microsoft.CodeAnalysis.Compilation output,
			out ImmutableArray<Diagnostic> _);

		result = new GeneratorResult(driver.GetRunResult(), (CSharpCompilation)output);
	}

	static GeneratorDriver Driver()
		=> CSharpGeneratorDriver.Create(
			[new Generator.NaxpGenerator().AsSourceGenerator()],
			driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

	static CSharpCompilation Compile(params string[] sources)
		=> CSharpCompilation.Create(
			"LogMu.GeneratorTests",
			sources.Select(source => CSharpSyntaxTree.ParseText(source, path: "Test.cs")),
			References(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

	/// <summary>
	/// The references the generated code needs, taken from the running framework rather than from
	/// packages so nothing has to be restored.
	/// </summary>
	static ImmutableArray<MetadataReference> References()
	{
		string[] wanted = ["System.Private.CoreLib.dll", "System.Runtime.dll", "System.Memory.dll"];

		return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator)
			.Where(path => wanted.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
			.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
			.ToImmutableArray();
	}

	/// <summary>One run of the generator, asked the questions the tests ask.</summary>
	sealed class GeneratorResult
	{
		readonly GeneratorDriverRunResult run;
		readonly CSharpCompilation output;

		public GeneratorResult(GeneratorDriverRunResult run, CSharpCompilation output)
		{
			this.run = run;
			this.output = output;
		}

		/// <summary>Every file the generator added, the injected attribute included.</summary>
		public IEnumerable<string> FileNames => this.run.GeneratedTrees.Select(tree => Path.GetFileName(tree.FilePath));

		/// <summary>The files the generator added for naxps, so not the attribute.</summary>
		public IEnumerable<string> GeneratedFileNames
			=> this.FileNames.Where(name => !name.Equals("NaxpAttribute.g.cs", StringComparison.Ordinal));

		/// <summary>The text of one generated file.</summary>
		public string Source(string fileName)
			=> this.run.GeneratedTrees
				.Single(tree => Path.GetFileName(tree.FilePath).Equals(fileName, StringComparison.Ordinal))
				.ToString();

		/// <summary>
		/// The one naxp file holding a piece of text. The injected attribute is left out of the
		/// search: its documentation names the generated members, so it matches almost anything.
		/// </summary>
		public string SourceContaining(string text)
			=> this.GeneratedFileNames
				.Select(this.Source)
				.Single(source => source.Contains(text, StringComparison.Ordinal));

		/// <summary>The single diagnostic with an identifier, which the test then reads.</summary>
		public Diagnostic Only(string id)
		{
			Diagnostic[] matching = this.run.Diagnostics.Where(d => d.Id == id).ToArray();

			Assert.True(
				matching.Length == 1,
				$"Expected one {id} and got {matching.Length}. All of them: {this.Describe()}");

			return matching[0];
		}

		public void AssertNoDiagnostics()
			=> Assert.True(this.run.Diagnostics.IsEmpty, $"The generator complained: {this.Describe()}");

		/// <summary>That the user's source and the generated code together compile cleanly.</summary>
		public void AssertCompiles()
		{
			Diagnostic[] complaints = this.output.GetDiagnostics()
				.Where(d => d.Severity >= DiagnosticSeverity.Warning)
				.ToArray();

			Assert.True(
				complaints.Length == 0,
				"The generated code did not compile cleanly:" + Environment.NewLine
					+ string.Join(Environment.NewLine, complaints.Select(c => c.ToString())));
		}

		string Describe()
			=> this.run.Diagnostics.IsEmpty
				? "(none)"
				: string.Join(Environment.NewLine, this.run.Diagnostics.Select(d => d.ToString()));
	}
	#endregion
}

#endif
