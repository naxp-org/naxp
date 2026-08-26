// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The JavaScript emitter: what its text says, and what its output does when Node runs it against
/// the conformance data.
/// </summary>
public class JavaScriptEmitterTests
{
	static readonly ConformanceTestData TestData = ConformanceTestData.Load();

	static Compilation Compile(string naxp)
	{
		Assert.True(Compiler.TryCompile(naxp, out Compilation? compilation, out NaxpError? error), error?.ToString());

		return compilation!;
	}

	static string Emit(string naxp, string prefix = "")
		=> JavaScriptEmitter.Instance.Emit(Compile(naxp), prefix);

	#region The text
	[Fact]
	public void Emit_CamelCasesThePrefix()
	{
		string source = Emit(@"\A\9", "Postcode");

		Assert.Contains("const postcodeValueCount = 260;", source, StringComparison.Ordinal);
		Assert.Contains("const postcodeMaxLength = 2;", source, StringComparison.Ordinal);
		Assert.Contains("function postcodeAccepts(text) {", source, StringComparison.Ordinal);
		Assert.Contains("function postcodeAcceptsBytes(bytes) {", source, StringComparison.Ordinal);
		Assert.Contains("function postcodeEncode(text) {", source, StringComparison.Ordinal);
		Assert.Contains("function postcodeEncodeBytes(bytes) {", source, StringComparison.Ordinal);
		Assert.Contains("function postcodeDecode(value) {", source, StringComparison.Ordinal);
		Assert.Contains("function postcodeDecodeToBytes(value) {", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_AllowsABlankPrefix()
	{
		string source = Emit(@"\A\9");

		Assert.Contains("function accepts(text) {", source, StringComparison.Ordinal);
		Assert.Contains("const valueCount = 260;", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// One stepper serves the string and the byte entry points, because a byte is already the code
	/// point it stands for.
	/// </summary>
	[Fact]
	public void Emit_ReadsCodePoints()
	{
		string source = Emit(@"\A\9");

		Assert.Contains("acceptStep(state, text.charCodeAt(i));", source, StringComparison.Ordinal);
		Assert.Contains("acceptStep(state, bytes[i]);", source, StringComparison.Ordinal);
		Assert.Contains("c >= 0x41 && c <= 0x5A", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_UsesNumbersWhereEveryValueFitsOneExactly()
	{
		string source = Emit(@"\A\9");

		Assert.Contains("@returns {number}", source, StringComparison.Ordinal);
		Assert.DoesNotContain("n;", source, StringComparison.Ordinal);
		Assert.DoesNotContain("BigInt(", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// Twelve letters is 95 428 956 661 682 176 values, past the 2^53 - 1 a JavaScript number holds
	/// exactly, so the fragment turns to BigInt.
	/// </summary>
	[Fact]
	public void Emit_UsesBigIntAboveTheSafeRange()
	{
		string source = Emit(@"\A{12}");

		Assert.Contains("const valueCount = 95_428_956_661_682_176n;", source, StringComparison.Ordinal);
		Assert.Contains("@returns {bigint}", source, StringComparison.Ordinal);
		Assert.Contains("acc.total += ", source, StringComparison.Ordinal);
		Assert.Contains("BigInt(c - 0x41)", source, StringComparison.Ordinal);
	}

	[Fact]
	public void Emit_CanonicalisesWhereTheNaxpReplaces()
	{
		string source = Emit(@"(B|b)!B");

		Assert.Contains("canonicalStep(state,", source, StringComparison.Ordinal);
		Assert.Contains("finishCanonical(state, canonical)", source, StringComparison.Ordinal);
		Assert.Contains("rank(canonical)", source, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("1Bad")]
	[InlineData("Bad.Name")]
	[InlineData("Bad Name")]
	public void Emit_RefusesABadPrefix(string prefix)
	{
		Compilation compilation = Compile("A");

		Assert.Throws<ArgumentException>(() => JavaScriptEmitter.Instance.Emit(compilation, prefix));
	}
	#endregion
	#region What it does when it runs
	/// <summary>
	/// Every naxp of the conformance data emitted into one file, and the whole of the test data
	/// checked against it by Node. The comparison happens in JavaScript, so the values never make
	/// a round trip through another language's idea of an integer; values are compared as text, so
	/// that a fragment using BigInt and one using numbers are asked the same question.
	/// </summary>
	[Fact]
	public void Generated_MatchesTheConformanceDataUnderNode()
	{
		string harness = BuildHarness();
		string directory = Path.Combine(Path.GetTempPath(), "naxp-js-" + Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(directory);

		try
		{
			string file = Path.Combine(directory, "conformance.js");

			File.WriteAllText(file, harness, new UTF8Encoding(false));

			// Node is how this test checks anything, so its absence is a failure and not a pass.
			Assert.True(
				TryRunNode(file, out int exitCode, out string output),
				"Node is not installed, so the emitted JavaScript cannot be run.");

			Assert.True(exitCode == 0, output);

			// Without this a harness that checked nothing and exited zero would pass silently.
			Assert.Contains("checks passed", output, StringComparison.Ordinal);
		}
		finally
		{
			try { Directory.Delete(directory, recursive: true); }
			catch (IOException) { }
		}
	}

	static bool TryRunNode(string file, out int exitCode, out string output)
	{
		var start = new ProcessStartInfo("node", "\"" + file + "\"")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		exitCode = 0;
		output = string.Empty;

		try
		{
			using Process? process = Process.Start(start);

			if (process is null) { return false; }

			output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
			process.WaitForExit();
			exitCode = process.ExitCode;

			return true;
		}
		catch (System.ComponentModel.Win32Exception)
		{
			// Node is not installed here.
			return false;
		}
	}

	/// <summary>The fragments for every conformance naxp, a table over them, and the checks.</summary>
	static string BuildHarness()
	{
		var builder = new StringBuilder();

		builder.AppendLine("'use strict';");
		builder.AppendLine();
		builder.AppendLine("// Generated by JavaScriptEmitterTests. One fragment per conformance case.");
		builder.AppendLine();

		for (int i = 0; i < TestData.Cases.Count; ++i)
		{
			ConformanceCase item = TestData.Cases[i];

			builder.AppendLine($"// {i.ToString(CultureInfo.InvariantCulture)}: {Emitter.CommentText(item.Naxp)}");
			JavaScriptEmitter.Instance.Emit(Compile(item.Naxp), Prefix(i), builder);
			builder.AppendLine();
		}

		builder.AppendLine("const cases = [");

		for (int i = 0; i < TestData.Cases.Count; ++i)
		{
			ConformanceCase item = TestData.Cases[i];
			string prefix = Prefix(i);

			builder.AppendLine("\t{");
			builder.AppendLine($"\t\tnaxp: {JsString(item.Naxp)},");
			builder.AppendLine($"\t\tvalueCount: {prefix}ValueCount,");
			builder.AppendLine($"\t\tvalueCountText: {JsString(item.ValueCount.ToString(CultureInfo.InvariantCulture))},");
			builder.AppendLine($"\t\taccepts: {prefix}Accepts,");
			builder.AppendLine($"\t\tacceptsBytes: {prefix}AcceptsBytes,");
			builder.AppendLine($"\t\tencode: {prefix}Encode,");
			builder.AppendLine($"\t\tencodeBytes: {prefix}EncodeBytes,");
			builder.AppendLine($"\t\tdecode: {prefix}Decode,");
			builder.AppendLine($"\t\tdecodeToBytes: {prefix}DecodeToBytes,");
			builder.Append("\t\tvalues: [");

			foreach (ConformanceValue value in item.Values)
			{
				builder.Append($"[{JsString(value.In)}, {JsString(value.Out.ToString(CultureInfo.InvariantCulture))}, {JsString(value.Canon)}], ");
			}

			builder.AppendLine("],");
			builder.Append("\t\tnotAccepted: [");

			foreach (string refused in item.NotAccepted)
			{
				builder.Append($"{JsString(refused)}, ");
			}

			builder.AppendLine("],");
			builder.AppendLine("\t},");
		}

		builder.AppendLine("];");
		builder.AppendLine();
		builder.Append(Driver);

		return builder.ToString();
	}

	/// <summary>Lower case already, so that the camel cased names are the prefix as written.</summary>
	static string Prefix(int index) => "case" + index.ToString(CultureInfo.InvariantCulture);

	/// <summary>The checks, which are the same questions ConformanceTests asks the library.</summary>
	const string Driver = """
		function toBytes(text) {
			const bytes = new Uint8Array(text.length);

			for (let i = 0; i < text.length; i++) { bytes[i] = text.charCodeAt(i); }

			return bytes;
		}

		function fromBytes(bytes) {
			return String.fromCharCode.apply(null, Array.from(bytes));
		}

		const failures = [];
		let checks = 0;

		function check(condition, message) {
			checks++;

			if (!condition) { failures.push(message); }
		}

		for (const c of cases) {
			check(String(c.valueCount) === c.valueCountText,
				`${c.naxp}: valueCount is ${c.valueCount}, the test data says ${c.valueCountText}`);

			for (const [text, expected, canon] of c.values) {
				const encoded = c.encode(text);

				check(String(encoded) === expected,
					`${c.naxp}: encode('${text}') is ${encoded}, the test data says ${expected}`);
				check(String(c.encodeBytes(toBytes(text))) === expected,
					`${c.naxp}: encodeBytes('${text}') disagrees with encode`);
				check(c.accepts(text) === (expected !== '0'),
					`${c.naxp}: accepts('${text}') is ${c.accepts(text)}, the test data says ${expected !== '0'}`);
				check(c.acceptsBytes(toBytes(text)) === (expected !== '0'),
					`${c.naxp}: acceptsBytes('${text}') disagrees with accepts`);

				if (expected !== '0') {
					const decoded = c.decode(encoded);

					check(decoded === canon,
						`${c.naxp}: decode(${encoded}) is '${decoded}', the test data says '${canon}'`);
					check(fromBytes(c.decodeToBytes(encoded)) === canon,
						`${c.naxp}: decodeToBytes(${encoded}) disagrees with decode`);
					check(String(c.encode(decoded)) === expected,
						`${c.naxp}: '${decoded}' does not encode back to ${expected}`);
				}
			}

			for (const refused of c.notAccepted) {
				check(!c.accepts(refused),
					`${c.naxp}: accepts('${refused}'), which the test data says it must not`);
				check(String(c.encode(refused)) === '0',
					`${c.naxp}: encode('${refused}') is ${c.encode(refused)} rather than zero`);
			}

			let threw = false;

			try { c.decode(0); } catch (e) { threw = e instanceof RangeError; }

			check(threw, `${c.naxp}: decode(0) did not throw a RangeError`);
		}

		if (failures.length !== 0) {
			console.log(`${failures.length} of ${checks} checks failed:`);

			for (const failure of failures.slice(0, 40)) { console.log('  ' + failure); }

			process.exit(1);
		}

		console.log(`${checks} checks passed over ${cases.length} naxps.`);
		""";

	static string JsString(string? text)
	{
		if (text is null) { return "null"; }

		var builder = new StringBuilder(text.Length + 2);

		builder.Append('\'');

		foreach (char c in text)
		{
			switch (c)
			{
				case '\'': builder.Append("\\'"); break;
				case '\\': builder.Append("\\\\"); break;
				case '\n': builder.Append("\\n"); break;
				case '\r': builder.Append("\\r"); break;
				case '\t': builder.Append("\\t"); break;
				default:
					if (c < ' ' || c > '~')
					{
						builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
					}
					else
					{
						builder.Append(c);
					}

					break;
			}
		}

		return builder.Append('\'').ToString();
	}
	#endregion
}
