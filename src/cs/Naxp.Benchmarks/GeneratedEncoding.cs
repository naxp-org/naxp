// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;

namespace LogMu.Benchmarks;

/// <summary>
/// The code <see cref="CSharpEmitter"/> emits against the library it came from, on the same
/// encode workload as the <see cref="Encoding"/> benchmark. The generated code is
/// <see cref="PostcodeGenerated"/>, checked in rather than emitted at run time so the benchmark
/// measures a direct static call with no delegate in the way.
/// </summary>
[HideColumns(
	Column.RatioSD,
	Column.Error, Column.StdDev, Column.Median
	)]
[MemoryDiagnoser(true)]
public class GeneratedEncoding
{
	/*

// * Summary * (2026-08-19, .NET 10.0.11, i7-11800H)

Checking naxp = '\A?\A\9\X? \s \9\A\A'

| Method    | Mean     | Ratio | Allocated | Alloc Ratio |
|---------- |---------:|------:|----------:|------------:|
| Library   | 252.0 us |  1.00 |         - |          NA |
| Generated | 209.0 us |  0.83 |         - |          NA |

-----------------------------
Conclusions:

1.	The generated code is ~ 1.2 times faster per encode (25 ns vs 21 ns per call), on top of
	needing no parse or state map construction at all.
2.	Both are allocation free.

	*/

	long result;
	const int N = 5000;

	/*
	The library naxp is parsed in GlobalSetup rather than inside the benchmark, so what is timed
	is solely encoding. The generated code has no initialisation at all, which is itself part of
	its point.
	*/
	Naxp libraryNaxp = null!;

	const string NaxpText = @"\A?\A\9\X? \s \9\A\A";
	const string Sample0 = "A0 1BC";
	const string Sample1 = "ST2U 3YZ";

	[GlobalSetup]
	public void GlobalSetup()
	{
		this.libraryNaxp = Naxp.Parse(NaxpText);

		this.RunPreFlightChecks();
	}

	void RunPreFlightChecks()
	{
		Console.WriteLine("Checking naxp = '" + NaxpText + "'");
		Console.WriteLine("Pre-flight checks");
		Console.WriteLine();

		(string name, long result) RunAndSaveResult(Action action, [CallerArgumentExpression(nameof(action))] string? name = null)
		{
			this.result = -1;
			action();
			return (name!, this.result);
		}

		(string name, long result)[] namedResults =
		[
			RunAndSaveResult(this.Library),
			RunAndSaveResult(this.Generated),
		];

		var (name_0, result_0) = namedResults[0];
		for (int i = 1; i < namedResults.Length; ++i)
		{
			var (name_i, result_i) = namedResults[i];

			if (result_i != result_0)
			{
				Console.WriteLine("Results from benchmarks are different!!!");
				Console.WriteLine($"        {name_0}: {result_0}");
				Console.WriteLine($"        {name_i}: {result_i}");
				throw new Exception("Results from algorithms are different!!!");
			}
		}

		Console.WriteLine($"All benchmarks gave consistent results.");
		Console.WriteLine();
	}

	[Benchmark(Baseline = true)]
	public void Library()
	{
		long checkSum = 0;

		Naxp naxp = this.libraryNaxp;
		for (int i = 0; i < N; ++i)
		{
			checkSum += (long)naxp.Encode(Sample0);
			checkSum += (long)naxp.Encode(Sample1);
		}

		this.result = checkSum;
	}

	[Benchmark]
	public void Generated()
	{
		long checkSum = 0;

		for (int i = 0; i < N; ++i)
		{
			checkSum += (long)PostcodeGenerated.Encode(Sample0);
			checkSum += (long)PostcodeGenerated.Encode(Sample1);
		}

		this.result = checkSum;
	}
}
