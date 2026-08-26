// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// Compiling and running the generated code needs Roslyn, which the test project only references
// on net8.0. The netstandard2.0 build of the emitter itself is still covered, by
// CSharpEmitterTests on net472.
#if NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace LogMu.UnitTests;

delegate bool GeneratedAcceptsChars(ReadOnlySpan<char> text);
delegate bool GeneratedAcceptsBytes(ReadOnlySpan<byte> text);
delegate ulong GeneratedEncodeChars(ReadOnlySpan<char> text);
delegate ulong GeneratedEncodeBytes(ReadOnlySpan<byte> text);
delegate string GeneratedDecode(ulong value);
delegate byte[] GeneratedDecodeToBytes(ulong value);
delegate bool GeneratedTryDecodeChars(ulong value, Span<char> destination, out int charsWritten);
delegate bool GeneratedTryDecodeBytes(ulong value, Span<byte> destination, out int bytesWritten);
delegate byte GeneratedEncodeNarrow(ReadOnlySpan<char> text);
delegate string GeneratedDecodeNarrow(byte value);

/// <summary>
/// The generated code against the same conformance data as the library itself: every naxp in the
/// test data is emitted, compiled with Roslyn into one in-memory assembly, and then asked the
/// questions <see cref="ConformanceTests"/> asks the library.
/// </summary>
public class GeneratedCodeTests
{
	static readonly ConformanceTestData TestData = ConformanceTestData.Load();

	/// <summary>Three literal runs, so the acceptor spills over one chunk of states.</summary>
	const string ChunkedNaxp = "A{99}B{99}C{99}";

	/// <summary>A replaceable ahead of literal runs, so the canonicalising machine spills too.</summary>
	const string ChunkedReplaceableNaxp = "(B|b)!BA{99}C{99}D{99}";

	static readonly Lazy<IReadOnlyDictionary<string, GeneratedNaxp>> Compiled =
		new(CompileAll);

	#region Tests
	[Fact]
	public void Generated_HasTheStatedCounts()
	{
		var failures = new List<string>();

		foreach (ConformanceCase item in TestData.Cases)
		{
			GeneratedNaxp generated = Compiled.Value[item.Naxp];

			if (generated.ValueCount != item.ValueCount)
			{
				failures.Add($"{item.Naxp} generated ValueCount {generated.ValueCount}, and the test data says {item.ValueCount}.");
			}
		}

		AssertNoFailures(failures);
	}

	/// <summary>
	/// The contract of <see cref="ConformanceTests.Values_EncodeAndDecodeAsTheTestDataSays"/>,
	/// asked of the generated code, in both string flavours.
	/// </summary>
	[Fact]
	public void Generated_EncodesAndDecodesAsTheTestDataSays()
	{
		var failures = new List<string>();
		int checkCount = 0;

		foreach (ConformanceCase item in TestData.Cases)
		{
			GeneratedNaxp generated = Compiled.Value[item.Naxp];

			foreach (ConformanceValue value in item.Values)
			{
				++checkCount;

				ulong encoded = generated.EncodeChars(value.In);

				if (encoded != value.Out)
				{
					failures.Add($"{item.Naxp} encodes '{value.In}' to {encoded}, and the test data says {value.Out}.");
					continue;
				}

				ulong encodedFromBytes = generated.EncodeBytes(AsciiBytes(value.In));

				if (encodedFromBytes != value.Out)
				{
					failures.Add($"{item.Naxp} encodes '{value.In}' as bytes to {encodedFromBytes}, and the test data says {value.Out}.");
				}

				bool expected = value.Out != 0L;

				if (generated.AcceptsChars(value.In) != expected || generated.AcceptsBytes(AsciiBytes(value.In)) != expected)
				{
					failures.Add($"{item.Naxp} answers Accepts('{value.In}') against the test data.");
				}

				if (value.Out == 0L) { continue; }

				string decoded = generated.Decode(value.Out);

				if (decoded != value.Canon)
				{
					failures.Add($"{item.Naxp} decodes {value.Out} to '{decoded}', and the test data says '{value.Canon}'.");
				}

				ulong reEncoded = generated.EncodeChars(value.Canon!);

				if (reEncoded != encoded)
				{
					failures.Add($"{item.Naxp} encodes the canonical form '{value.Canon}' to {reEncoded} rather than {encoded}.");
				}
			}

			foreach (string notAccepted in item.NotAccepted)
			{
				++checkCount;

				if (generated.EncodeChars(notAccepted) != 0UL
					|| generated.AcceptsChars(notAccepted)
					|| generated.AcceptsBytes(AsciiBytes(notAccepted)))
				{
					failures.Add($"{item.Naxp} accepts '{notAccepted}', and the test data lists it as not accepted.");
				}
			}
		}

		AssertNoFailures(failures);
		Assert.True(checkCount > 1400, $"Only {checkCount} strings were checked.");
	}

	/// <summary>
	/// The contract of <see cref="ConformanceTests.CompleteCases_DecodeToABijection"/>, asked of
	/// the generated code.
	/// </summary>
	[Fact]
	public void Generated_CompleteCases_DecodeToABijection()
	{
		var failures = new List<string>();

		foreach (ConformanceCase item in TestData.Cases.Where(c => c.Complete && c.ValueCount <= 2000L))
		{
			GeneratedNaxp generated = Compiled.Value[item.Naxp];
			var seen = new HashSet<string>(StringComparer.Ordinal);

			for (ulong value = 1UL; value <= (ulong)item.ValueCount; ++value)
			{
				string decoded = generated.Decode(value);

				if (!seen.Add(decoded))
				{
					failures.Add($"{item.Naxp} decodes two values to '{decoded}'.");
				}

				ulong again = generated.EncodeChars(decoded);

				if (again != value)
				{
					failures.Add($"{item.Naxp} decodes {value} to '{decoded}', which encodes back to {again}.");
				}
			}

			Assert.Throws<ArgumentOutOfRangeException>(() => Compiled.Value[item.Naxp].Decode((ulong)item.ValueCount + 1UL));
			Assert.Throws<ArgumentOutOfRangeException>(() => Compiled.Value[item.Naxp].Decode(0UL));
		}

		AssertNoFailures(failures);
	}

	[Fact]
	public void Generated_TryDecode_WritesIntoSpans()
	{
		foreach (ConformanceCase item in TestData.Cases.Where(c => c.Complete))
		{
			GeneratedNaxp generated = Compiled.Value[item.Naxp];

			foreach (ConformanceValue value in item.Values.Where(v => v.Out != 0L))
			{
				string expected = value.Canon!;

				// A destination of MaxLength always suffices, and the written slice is the string.
				var roomy = new char[generated.MaxLength];
				Assert.True(generated.TryDecodeChars(value.Out, roomy, out int charsWritten));
				Assert.Equal(expected, new string(roomy, 0, charsWritten));

				// A destination of exactly the string's length suffices too, even below MaxLength.
				var exact = new char[expected.Length];
				Assert.True(generated.TryDecodeChars(value.Out, exact, out charsWritten));
				Assert.Equal(expected, new string(exact, 0, charsWritten));

				// One shorter refuses without writing.
				if (expected.Length > 0)
				{
					Assert.False(generated.TryDecodeChars(value.Out, new char[expected.Length - 1], out charsWritten));
					Assert.Equal(0, charsWritten);
				}

				var bytes = new byte[expected.Length];
				Assert.True(generated.TryDecodeBytes(value.Out, bytes, out int bytesWritten));
				Assert.Equal(expected, new string(bytes.Select(b => (char)b).ToArray(), 0, bytesWritten));

				Assert.Equal(expected, new string(generated.DecodeToBytes(value.Out).Select(b => (char)b).ToArray()));
			}

			// Out of range values refuse rather than throw.
			Assert.False(generated.TryDecodeChars(0UL, new char[generated.MaxLength], out _));
			Assert.False(generated.TryDecodeChars((ulong)item.ValueCount + 1UL, new char[generated.MaxLength], out _));
		}
	}

	/// <summary>
	/// The chunked machines answer just as the library does. Correctness elsewhere is carried by
	/// the conformance cases, which all fit one chunk; these two are the split's only exercise.
	/// </summary>
	[Fact]
	public void Generated_ChunkedMachines_AgreeWithTheLibrary()
	{
		var literals = new string('A', 99) + new string('B', 99) + new string('C', 99);
		GeneratedNaxp chunked = Compiled.Value[ChunkedNaxp];

		Assert.Equal(1UL, chunked.ValueCount);
		Assert.Equal(297, chunked.MaxLength);
		Assert.True(chunked.AcceptsChars(literals));
		Assert.Equal(1UL, chunked.EncodeChars(literals));
		Assert.Equal(literals, chunked.Decode(1UL));
		Assert.False(chunked.AcceptsChars(literals.Substring(1)));

		var tail = new string('A', 99) + new string('C', 99) + new string('D', 99);
		GeneratedNaxp replaceable = Compiled.Value[ChunkedReplaceableNaxp];

		Assert.Equal(1UL, replaceable.ValueCount);
		Assert.Equal(1UL, replaceable.EncodeChars("B" + tail));
		Assert.Equal(1UL, replaceable.EncodeChars("b" + tail));
		Assert.Equal(0UL, replaceable.EncodeChars("c" + tail));
		Assert.Equal("B" + tail, replaceable.Decode(1UL));
	}
	/// <summary>
	/// A fragment emitted with a narrow value type compiles and round-trips. The type only
	/// changes the public boundary, so one small case suffices; correctness of the machinery is
	/// carried by the conformance cases above.
	/// </summary>
	[Fact]
	public void Generated_NarrowValueType_RoundTrips()
	{
		Assert.True(Compiler.TryCompile("#[1-100]", out Compilation? compilation, out NaxpError? error), error?.ToString());

		string source = Wrap("Narrow", CSharpEmitter.Instance.Emit(compilation!, string.Empty, NaxpValueType.UInt8, "\t\t"));

		CSharpCompilation compiled = CSharpCompilation.Create(
			"LogMu.GeneratedNarrow",
			new[] { CSharpSyntaxTree.ParseText(source) },
			References(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

		Diagnostic[] complaints = compiled.GetDiagnostics()
			.Where(d => d.Severity >= DiagnosticSeverity.Warning)
			.ToArray();

		Assert.True(
			complaints.Length == 0,
			$"The narrow fragment did not compile cleanly:{Environment.NewLine}{string.Join(Environment.NewLine, complaints.Select(c => c.ToString()))}");

		using var stream = new MemoryStream();
		Assert.True(compiled.Emit(stream).Success);

		Type type = Assembly.Load(stream.ToArray()).GetType("LogMu.Generated.Narrow")!;

		Assert.Equal((byte)100, type.GetField("ValueCount")!.GetRawConstantValue());

		var encode = type.GetMethod("Encode", new[] { typeof(ReadOnlySpan<char>) })!.CreateDelegate<GeneratedEncodeNarrow>();
		var decode = type.GetMethod("Decode")!.CreateDelegate<GeneratedDecodeNarrow>();

		byte encoded = encode("42");

		Assert.NotEqual(0, (int)encoded);
		Assert.Equal("42", decode(encoded));
		Assert.Equal(0, (int)encode("101"));
	}
	#endregion
	#region Harness
	/// <summary>
	/// Emits every naxp under test, wraps each fragment in a class, compiles the lot as one
	/// assembly, and binds each class's public surface to delegates. One compilation rather than
	/// one per naxp, because Roslyn's start-up cost would otherwise dominate the test run.
	/// </summary>
	static IReadOnlyDictionary<string, GeneratedNaxp> CompileAll()
	{
		List<string> naxps = TestData.Cases
			.Select(c => c.Naxp)
			.Concat(new[] { ChunkedNaxp, ChunkedReplaceableNaxp })
			.Distinct(StringComparer.Ordinal)
			.ToList();

		var sources = new List<SyntaxTree>();
		var classNames = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (string naxp in naxps)
		{
			Assert.True(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error), $"{naxp}: {error}");

			string className = $"Case{classNames.Count}";
			classNames.Add(naxp, className);
			sources.Add(CSharpSyntaxTree.ParseText(Wrap(className, CSharpEmitter.Instance.Emit(compilation!, string.Empty, initialIndent: "\t\t"))));
		}

		CSharpCompilation compiled = CSharpCompilation.Create(
			"LogMu.Generated",
			sources,
			References(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

		Diagnostic[] complaints = compiled.GetDiagnostics()
			.Where(d => d.Severity >= DiagnosticSeverity.Warning)
			.ToArray();

		Assert.True(
			complaints.Length == 0,
			$"The generated code did not compile cleanly:{Environment.NewLine}{string.Join(Environment.NewLine, complaints.Select(c => c.ToString()))}");

		using var stream = new MemoryStream();
		Assert.True(compiled.Emit(stream).Success);

		Assembly assembly = Assembly.Load(stream.ToArray());

		return naxps.ToDictionary(
			naxp => naxp,
			naxp => Bind(assembly.GetType($"LogMu.Generated.{classNames[naxp]}")!),
			StringComparer.Ordinal);
	}

	/// <summary>
	/// The emitter writes a fragment, so the class and namespace around it are the caller's job -
	/// here, this wrapper. The fragment fully qualifies its System types, so no using directive
	/// is needed.
	/// </summary>
	static string Wrap(string className, string fragment)
		=> "namespace LogMu.Generated" + Environment.NewLine
		+ "{" + Environment.NewLine
		+ "\tinternal static class " + className + Environment.NewLine
		+ "\t{" + Environment.NewLine
		+ fragment
		+ "\t}" + Environment.NewLine
		+ "}" + Environment.NewLine;

	/// <summary>
	/// The references the generated code needs, taken from the running framework rather than
	/// from packages so nothing has to be restored.
	/// </summary>
	static ImmutableArray<MetadataReference> References()
	{
		string[] wanted = { "System.Private.CoreLib.dll", "System.Runtime.dll", "System.Memory.dll" };

		return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator)
			.Where(path => wanted.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
			.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
			.ToImmutableArray();
	}

	static GeneratedNaxp Bind(Type type)
		=> new GeneratedNaxp(
			(ulong)type.GetField("ValueCount")!.GetRawConstantValue()!,
			(int)type.GetField("MaxLength")!.GetRawConstantValue()!,
			type.GetMethod("Accepts", new[] { typeof(ReadOnlySpan<char>) })!.CreateDelegate<GeneratedAcceptsChars>(),
			type.GetMethod("Accepts", new[] { typeof(ReadOnlySpan<byte>) })!.CreateDelegate<GeneratedAcceptsBytes>(),
			type.GetMethod("Encode", new[] { typeof(ReadOnlySpan<char>) })!.CreateDelegate<GeneratedEncodeChars>(),
			type.GetMethod("Encode", new[] { typeof(ReadOnlySpan<byte>) })!.CreateDelegate<GeneratedEncodeBytes>(),
			type.GetMethod("Decode")!.CreateDelegate<GeneratedDecode>(),
			type.GetMethod("DecodeToBytes")!.CreateDelegate<GeneratedDecodeToBytes>(),
			type.GetMethod("TryDecode", new[] { typeof(ulong), typeof(Span<char>), typeof(int).MakeByRefType() })!.CreateDelegate<GeneratedTryDecodeChars>(),
			type.GetMethod("TryDecode", new[] { typeof(ulong), typeof(Span<byte>), typeof(int).MakeByRefType() })!.CreateDelegate<GeneratedTryDecodeBytes>());

	static byte[] AsciiBytes(string text)
	{
		var bytes = new byte[text.Length];

		for (int i = 0; i < text.Length; ++i) { bytes[i] = (byte)text[i]; }

		return bytes;
	}

	static void AssertNoFailures(List<string> failures)
	{
		Assert.True(
			failures.Count == 0,
			$"{failures.Count} failures:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
	}

	sealed class GeneratedNaxp
	{
		public GeneratedNaxp(
			ulong valueCount,
			int maxLength,
			GeneratedAcceptsChars acceptsChars,
			GeneratedAcceptsBytes acceptsBytes,
			GeneratedEncodeChars encodeChars,
			GeneratedEncodeBytes encodeBytes,
			GeneratedDecode decode,
			GeneratedDecodeToBytes decodeToBytes,
			GeneratedTryDecodeChars tryDecodeChars,
			GeneratedTryDecodeBytes tryDecodeBytes)
		{
			this.ValueCount = valueCount;
			this.MaxLength = maxLength;
			this.AcceptsChars = acceptsChars;
			this.AcceptsBytes = acceptsBytes;
			this.EncodeChars = encodeChars;
			this.EncodeBytes = encodeBytes;
			this.Decode = decode;
			this.DecodeToBytes = decodeToBytes;
			this.TryDecodeChars = tryDecodeChars;
			this.TryDecodeBytes = tryDecodeBytes;
		}

		public ulong ValueCount { get; }

		public int MaxLength { get; }

		public GeneratedAcceptsChars AcceptsChars { get; }

		public GeneratedAcceptsBytes AcceptsBytes { get; }

		public GeneratedEncodeChars EncodeChars { get; }

		public GeneratedEncodeBytes EncodeBytes { get; }

		public GeneratedDecode Decode { get; }

		public GeneratedDecodeToBytes DecodeToBytes { get; }

		public GeneratedTryDecodeChars TryDecodeChars { get; }

		public GeneratedTryDecodeBytes TryDecodeBytes { get; }
	}
	#endregion
}

#endif
