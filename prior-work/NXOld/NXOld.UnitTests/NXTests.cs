// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace Naxp.UnitTests;

public sealed class NXTests
{
	[Fact]
	public void TestNX()
	{
		var testData = new[]
		{
			(nxText1: @"\x00#[1-10]", nxText2: @"\x00[1-9]|\x0010"
				, accepts: [ "\01", "\02", "\09", "\010", ]
				, rejects: [ "", " ", "\00", "\0A", "\011", "\0100", ]
			),
			(nxText1: @"A#[1-10]", nxText2: @"A[1-9]|A10"
				, accepts: [ "A1", "A2", "A9", "A10", ]
				, rejects: [ "", " ", "A0", "AA", "A11", "A100", ]
			),
			(nxText1: @"A#[01-23]Z", nxText2: @"A(0[1-9]|1[0-9]|2[0-3])Z"
				, accepts: [ "A01Z", "A10Z", "A15Z", "A19Z", "A21Z", "A23Z", ]
				, rejects: [ "", "AZ", "A0Z", "AAZ", "01", "23", ]
			),
			(nxText1: @"A#[23-7834]Z", nxText2: @"A(2[3-9]|[3-9][0-9]|[1-9][0-9][0-9]|[1-6][0-9][0-9][0-9]|7[0-7][0-9][0-9]|78[0-2][0-9]|783[0-4])Z"
				, accepts: [ "A23Z", "A345Z", "A6090Z", "A7834Z", ]
				, rejects: [ "", " ", "AZ", "A22Z", "A7835Z", "A7834ZZ", ]
			),
			(nxText1: @"E0000000000#[01-10]|N000000000001|S000000000001|W000000000001", nxText2: @"E#[000000000001-000000000010]|N000000000001|S000000000001|W000000000001"
				, accepts: [ "E000000000001", "E000000000005", "E000000000010", "N000000000001", "S000000000001", "E000000000001", "W000000000001", ]
				, rejects: [ "", " ", "E000000000011", "W000000000002", "S000000000000", ]
			),

			(nxText1: @"", nxText2: @""
				, accepts: new [] { "", }
				, rejects: new [] { "A", "12", }
			),
			(nxText1: @"A", nxText2: @"A"
				, accepts: [ "A", ]
				, rejects: [ "", "AA", "B", ]
			),
			(nxText1: @"(A1|A2|A3)(B2|B3|B4)", nxText2: @"A1B[234]|(A2|A3)B[234]|A1B2"
				, accepts: [ "A1B2", ]
				, rejects: [ "", "AA", "B", ]
			),
			(nxText1: @"(A1|B2)(C3|D4)", nxText2: @"A1D4|B2C3|B2D4|A1C3"
				, accepts: [ "A1C3", "A1D4", "B2C3", "B2D4", ]
				, rejects: [ "", "A1C", "A1B2", ]
			),
			(nxText1: @"\A?\A\9\X?", nxText2: @"\A\9\X?|\A\A?\9\X?|\A\9\X?|\A\A?\9"
				, accepts: [ "A0", "CD2", "G45", "JK78", "ST2U", ]
				, rejects: [ "", " ", "A", "A01BC", "2D2", ]
			),
			(nxText1: @"\A?\A\9\X? \s \9\A\A", nxText2: @"(\A\9 \s \9|(\A\A\9|\A?\A\9\X) \s \9)\A\A"
				, accepts: [ "A0 1BC", "CD2 3EF", "G45 6HI", "JK78 9LM", "N0P 1QR", "ST2U 3YZ", ]
				, rejects: [ "", " ", "A", "A01BC", "2D2 3EF", "G45 6hI", "JK78 9L1", "N0P 1Q", "ST2UU 3YZ", ]
			),
		};

		using Stream stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		var reader = new BinaryReader(stream);

		foreach (var (nxText1, nxText2, accepts, rejects) in testData)
		{
			var nx1 = NX.Parse(nxText1);
			var nx2 = NX.Parse(nxText2);

			Assert.Equal(nx1, nx2);

			// Test rehydration
			var nx1AsText = nx1.ToString();
			var nx2AsText = nx2.ToString();
			Assert.Equal(nx1AsText, nx2AsText);
			var nx3 = NX.Parse(nx1AsText);
			Assert.Equal(nx1, nx3);
			var nx3AsText = nx3.ToString();
			Assert.Equal(nx1AsText, nx3AsText);

			foreach (var text in accepts)
			{
				Assert.True(nx1.Accepts(text));
			}
			foreach (var text in rejects)
			{
				Assert.False(nx1.Accepts(text));
			}

			var text1 = nx1.ToString();
			var text2 = nx2.ToString();

			Assert.Equal(text1, text2);

			// Test binary IO
			stream.Position = 0;
			nx1.WriteTo(writer);
			var endPosition = stream.Position;

			stream.Position = 0;
			var nxRead = reader.Read<NX>();
			Assert.Equal(nx1, nxRead);
			Assert.Equal(endPosition, stream.Position);
		}
	}

	[Fact]
	public void TestNX_Encoding()
	{
		var testData = new[]
		{
			( nxText: "",  textEncodingPairs: new [] { ("", 1ul),  ("A", 0ul), } ),
			( nxText: "A[12]",  textEncodingPairs: [ ("A1", 1ul),  ("A2", 2ul), ("A", 0ul), ("A0", 0ul), ] ),
			( nxText: "[AB][123]",  textEncodingPairs: [ ("A1", 1ul), ("A2", 2ul), ("A3", 3ul), ("B1", 4ul), ("B3", 6ul), ("A", 0ul), ("A0", 0ul), ] ),
			( nxText: "[AB][123][abcd]",  textEncodingPairs: [
				("A1a", 1ul), ("A1b", 2ul), ("A1d", 4ul),
				("A2a", 5ul), ("A2d", 8ul),
				("A3a", 9ul), ("A3d", 12ul),
				("B1a", 13ul),
				("B3d", 24ul),
				("A", 0ul), ("A0", 0ul), ] ),
			( nxText: "(A[12])?",  textEncodingPairs: [
				("", 1ul), ("A1", 2ul),  ("A2", 3ul),
				("A", 0ul), ("A0", 0ul), ] ),

            /*
                \A → // 259740 = 26 × (370 + 9620) 
                    \9 → // 370 = 10 × (1 + 36) 
                        [] → ∅,  // 1
                        \X → ( [] → ∅ ) // 36
                    \A →  // 9620 = 26 × 370 
                        \9 →  // 370 = 10 × (1 + 36)
                            [] → ∅,  // 1
                            \X → ( [] → ∅ )  // 36
            */
            ( nxText: @"\A\A?\9\X?",  textEncodingPairs: [
				("A0", /*A*/0 * (370ul + 9620ul) + /*0 ∈ \9*/0 * (1ul+36ul) + 1ul),
				("C9", /*C*/2 * (370ul + 9620ul) + /*9 ∈ \9*/9 * (1ul+36ul) + 1ul),
				("AB2", /*A*/0 * (370ul + 9620ul) + /*B ∉ \9*/370ul + /*B ∈ \A*/1 * 370ul + /*2 ∈ \9*/2*(1ul+36ul) + 1ul),
				("BC3", /*B*/1 * (370ul + 9620ul) + /*C ∉ \9*/370ul + /*C ∈ \A*/2 * 370ul + /*3 ∈ \9*/3*(1ul+36ul) + 1ul),
				("CD12", /*C*/2 * (370ul + 9620ul) + /*D ∉ \9*/370ul + /*D ∈ \A*/3 * 370ul + /*1 ∈ \9*/1*(1ul+36ul) + /*2 ∉ []*/1ul + /*2 ∈ \X*/2 * 1ul  + 1ul),
				("SW2C", /*S*/18 * (370ul + 9620ul) + /*W ∉ \9*/370ul + /*W ∈ \A*/22 * 370ul + /*2 ∈ \9*/2*(1ul+36ul) + /*C ∉ []*/1ul + /*C ∈ \X*/12 * 1ul  + 1ul),
				("D89", /*D*/3 * (370ul + 9620ul) + /*8 ∈ \9*/8 * (1ul+36ul) + /*9 ∉ []*/1ul + /*9 ∈ \X*/9 * 1ul  + 1ul),
				("", 0ul),
			]),

            // Numbers are encoded as expected when all digits are always present.
            ( nxText: "A#[01-10]",  textEncodingPairs: [ ("A01", 1ul),  ("A09", 9ul), ("A10", 10ul), ("A", 0ul), ("A4", 0ul), ] ),
			( nxText: "A#[001-500]",  textEncodingPairs: [ ("A001", 1ul),  ("A009", 9ul), ("A010", 10ul), ("A099", 99ul), ("A100", 100ul), ("A456", 456ul), ("A", 0ul), ("A4", 0ul), ] ),

            // Allows use of [ ] literals above
            ( nxText: "",  textEncodingPairs: new [] { ("", 1ul),  ("A", 0ul), } ),
		};

		foreach (var (nxText, textEncodingPairs) in testData)
		{
			var nx = NX.Parse(nxText);
#if DEBUG
			var code = nx.GetComputationProgram(ProgrammingLanguage.CSharp);
#endif

			foreach (var (text, encoding) in textEncodingPairs)
			{
				Assert.Equal(encoding, nx.GetEncoding(text));
			}

			ValidateGeneratedCSharp(nx, textEncodingPairs);
		}
	}

	static void ValidateGeneratedCSharp(NX nx, (string, ulong)[] textEncodingPairs)
	{
		var sb = new StringBuilder();
		sb.Append("""
            namespace OnTheFlyNXTesting
            {
                using System; 
                using Naxp;
                public static class X
                {
                    public static bool Accepts2(string text) => Accepts(text.AsSpan());
                    public static ulong GetEncoding2(string text) => GetEncoding(text.AsSpan());
                        
            """);
		nx.AppendComputationProgram(sb, language: ProgrammingLanguage.CSharp, initialIndent: "    ", lineEnding: Environment.NewLine);
		sb.Append("""
                }
            }
            """);

		var code = sb.ToString();

		var syntaxTree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.CSharp7));

		string basePath = Path.GetDirectoryName(typeof(System.Runtime.GCSettings).GetTypeInfo().Assembly.Location)!;

		var references = ((CompilationUnitSyntax)syntaxTree.GetRoot()).Usings;

		var referencePaths = new List<string>
		{
			typeof(object).GetTypeInfo().Assembly.Location,
			Path.Combine(basePath, "System.Runtime.dll"),
			Path.Combine(basePath, "System.Runtime.Extensions.dll"),
			Path.Combine(basePath, "mscorlib.dll"),
			typeof(AsciiCharSet).GetTypeInfo().Assembly.Location,
		};

		referencePaths.AddRange(references.Select(x => Path.Combine(basePath, $"{x.Name}.dll")));

		var executableReferences = new List<PortableExecutableReference>();
		foreach (var reference in referencePaths)
		{
			if (File.Exists(reference))
			{
				executableReferences.Add(MetadataReference.CreateFromFile(reference));
			}
		}

		var compilation = CSharpCompilation.Create(Path.GetRandomFileName(), new[] { syntaxTree }, executableReferences, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		using var ms = new MemoryStream();

		EmitResult compilationResult = compilation.Emit(ms);

		if (!compilationResult.Success)
		{
			var errors = compilationResult.Diagnostics.Where(diagnostic
				=> diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error
				)?.ToList() ?? [];
		}

		Assert.True(compilationResult.Success);

		ms.Seek(0, SeekOrigin.Begin);

		AssemblyLoadContext assemblyContext = new(Path.GetRandomFileName(), true);
		Assembly assembly = assemblyContext.LoadFromStream(ms);

		var type = assembly.GetType("OnTheFlyNXTesting.X")!;

		var accepts = type.GetMethod("Accepts", BindingFlags.Public | BindingFlags.Static);
		Assert.NotNull(accepts);
		// We can't pass a text span in an object array, so we test via a version that takes a string instead.
		var accepts2 = type.GetMethod("Accepts2");

		var getEncoding = type.GetMethod("GetEncoding", BindingFlags.Public | BindingFlags.Static);
		Assert.NotNull(getEncoding);
		// We can't pass a text span in an object array, so we test via a version that takes a string instead.
		var getEncoding2 = type.GetMethod("GetEncoding2");

		foreach (var (text, encoding) in textEncodingPairs)
		{
			var resultAccepts = (bool)accepts2.Invoke(null, new object[] { text });
			Assert.Equal(encoding != 0, resultAccepts);

			var resultGetEncoding = (ulong)getEncoding2.Invoke(null, new object[] { text });
			Assert.Equal(encoding, resultGetEncoding);
		}

		assemblyContext.Unload();
	}
}