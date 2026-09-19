// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// What the C and C++ emitter tests share: writing a harness to a scratch directory, putting it
/// through a compiler with every warning fatal, running it, and reading the verdict. The
/// compiler is how those tests check anything, so its absence is a failure and not a pass.
/// </summary>
static class NativeHarness
{
	/// <summary>The flags every harness compiles under. A fragment that is not clean under these is not finished.</summary>
	const string Warnings = "-Wall -Wextra -Werror -pedantic";

	/// <summary>
	/// Compiles and runs a harness, and asserts that it compiled, that it exited zero, and that
	/// it reported checks passed, so that a harness which checked nothing cannot pass quietly.
	/// </summary>
	/// <param name="compiler">The compiler, <c>gcc</c> or <c>g++</c>.</param>
	/// <param name="standard">The language standard flag.</param>
	/// <param name="fileName">What to call the source file.</param>
	/// <param name="source">The harness.</param>
	public static void CompileAndRun(string compiler, string standard, string fileName, string source)
	{
		string directory = Path.Combine(Path.GetTempPath(), "naxp-native-" + Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(directory);

		try
		{
			string file = Path.Combine(directory, fileName);
			string executable = Path.Combine(directory, "harness.exe");

			File.WriteAllText(file, source, new UTF8Encoding(false));

			Assert.True(
				TryRun(compiler, $"{standard} {Warnings} -O1 \"{file}\" -o \"{executable}\"", out int compileExit, out string compileOutput),
				$"{compiler} is not installed, so the emitted code cannot be compiled.");
			Assert.True(compileExit == 0, compileOutput);

			Assert.True(TryRun(executable, string.Empty, out int runExit, out string runOutput), "The harness did not start.");
			Assert.True(runExit == 0, runOutput);
			Assert.Contains("checks passed", runOutput, StringComparison.Ordinal);
		}
		finally
		{
			try { Directory.Delete(directory, recursive: true); }
			catch (IOException) { }
		}
	}

	static bool TryRun(string program, string arguments, out int exitCode, out string output)
	{
		var start = new ProcessStartInfo(program, arguments)
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

			// Both streams are drained together. A compiler with a pipe's worth of errors to
			// report blocks on the one nobody is reading, and so does this.
			System.Threading.Tasks.Task<string> standardError = process.StandardError.ReadToEndAsync();
			string standardOutput = process.StandardOutput.ReadToEnd();

			output = standardOutput + standardError.Result;
			process.WaitForExit();
			exitCode = process.ExitCode;

			return true;
		}
		catch (System.ComponentModel.Win32Exception)
		{
			// The program is not installed here.
			return false;
		}
	}

	/// <summary>
	/// Text as a C or C++ string literal. Unprintable characters go in octal, which stops at
	/// three digits where a hexadecimal escape would swallow the digit after it; a question mark
	/// is escaped because <c>-std=c99</c> honours trigraphs, and C++ does not mind.
	/// </summary>
	public static string CString(string text)
	{
		var builder = new StringBuilder(text.Length + 2);

		builder.Append('"');

		foreach (char c in text)
		{
			switch (c)
			{
				case '"': builder.Append("\\\""); break;
				case '\\': builder.Append("\\\\"); break;
				case '?': builder.Append("\\?"); break;
				default:
					if (c < ' ' || c > '~')
					{
						builder.Append('\\').Append(Convert.ToString((int)c, 8).PadLeft(3, '0'));
					}
					else
					{
						builder.Append(c);
					}

					break;
			}
		}

		return builder.Append('"').ToString();
	}

	/// <summary>A length as a plain decimal.</summary>
	public static string Length(string text) => text.Length.ToString(CultureInfo.InvariantCulture);
}
