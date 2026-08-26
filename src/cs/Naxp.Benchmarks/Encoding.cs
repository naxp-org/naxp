// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Numerics;
using BenchmarkDotNet;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;

namespace LogMu.Benchmarks;

[HideColumns(
	//Column.Mean,
	Column.RatioSD,
	Column.Error, Column.StdDev, Column.Median
	//, Column.Allocated, Column.AllocRatio
	)]
[MemoryDiagnoser(true)]
public class Encoding
{
	/*

// * Summary *

NEW CODE (EXCLUDING INIT):

A. Checking naxp = '\A?\A\9\X? \s \9\A\A'

| Method | Mean     | Ratio | Allocated | Alloc Ratio |
|------- |---------:|------:|----------:|------------:|
| Old    | 573.1 us |  1.00 |         - |          NA |
| New    | 212.2 us |  0.37 |         - |          NA |

B. Checking naxp = '#[0-10]'

| Method | Mean      | Ratio | Allocated | Alloc Ratio |
|------- |----------:|------:|----------:|------------:|
| Old    | 255.66 us |  1.00 |         - |          NA |
| New    |  76.39 us |  0.30 |         - |          NA |

C. Checking naxp = '#[0-999999999999]'

| Method | Mean     | Ratio | Allocated | Alloc Ratio |
|------- |---------:|------:|----------:|------------:|
| Old    | 900.8 us |  1.00 |         - |          NA |
| New    | 250.6 us |  0.28 |         - |          NA |
-----------------------------

NEW CODE, BUT BENCHMARK INCLUDING INIT:

A. Checking naxp = '\A?\A\9\X? \s \9\A\A'

| Method | Mean     | Ratio | Gen0   | Allocated | Alloc Ratio |
|------- |---------:|------:|-------:|----------:|------------:|
| Old    | 581.7 us |  1.00 |      - |   8.05 KB |        1.00 |
| New    | 225.7 us |  0.39 | 1.7090 |  22.23 KB |        2.76 |

B. Checking naxp = '#[0-10]'

| Method | Mean      | Ratio | Gen0   | Allocated | Alloc Ratio |
|------- |----------:|------:|-------:|----------:|------------:|
| Old    | 326.27 us |  1.00 |      - |   1.83 KB |        1.00 |
| New    |  85.41 us |  0.26 | 0.6104 |   8.51 KB |        4.65 |

C. Checking naxp = '#[0-999999999999]'

| Method | Mean         | Ratio | Gen0      | Gen1      | Gen2      | Allocated   | Alloc Ratio |
|------- |-------------:|------:|----------:|----------:|----------:|------------:|------------:|
| Old    | 164,172.6 us | 1.000 | 5750.0000 | 5500.0000 | 1500.0000 | 56479.97 KB |       1.000 |
| New    |     290.7 us | 0.002 |    7.3242 |    0.4883 |         - |    93.06 KB |       0.002 |

-----------------------------

OLD CODE, BUT BENCHMARK INCLUDING INIT:

A. Checking naxp = '\A?\A\9\X? \s \9\A\A'

| Method | Mean       | Ratio | Gen0      | Allocated   | Alloc Ratio |
|------- |-----------:|------:|----------:|------------:|------------:|
| Old    |   837.0 us |  1.01 |         - |     8.05 KB |        1.00 |
| New    | 8,040.5 us |  9.74 | 2140.6250 | 26250.34 KB |    3,262.18 |

B. Checking naxp = '#[0-10]'

| Method | Mean       | Ratio | Gen0     | Allocated  | Alloc Ratio |
|------- |-----------:|------:|---------:|-----------:|------------:|
| Old    |   423.2 us |  1.01 |        - |    1.83 KB |        1.00 |
| New    | 2,255.0 us |  5.36 | 707.0313 | 8684.53 KB |    4,750.51 |

-----------------------------
Conclusions:

1.	The new version ~ 3 times faster.

	*/

	long result;
	const int N = 5000;

	/*
	The naxp implementations are parsed in GlobalSetup rather than inside the benchmark,
	so what is timed is solely encoding. For '#[0-999999999999]' about 99% of the old figure
	was the parse, which is why the tables above that include init differ so widely from these.
	*/
	NXOld.NX oldNaxp = null!;
	Naxp newNaxp = null!;

#if false
	const string NaxpText = "#[0-999999999999]";
	const string Sample0 = "0";
	const string Sample1 = "999999999999";
#else
#if false
	const string NaxpText = "#[0-10]";
	const string Sample0 = "0";
	const string Sample1 = "10";
#else
	const string NaxpText = @"\A?\A\9\X? \s \9\A\A";
	const string Sample0 = "A0 1BC";
	const string Sample1 = "ST2U 3YZ";
#endif
#endif

	[GlobalSetup]
	public void GlobalSetup()
	{
		this.oldNaxp = NXOld.NX.Parse(NaxpText);
		this.newNaxp = Naxp.Parse(NaxpText);

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
			RunAndSaveResult(this.Old),
			RunAndSaveResult(this.New),
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
	public void Old()
	{
		long checkSum = 0;

		NXOld.NX naxp = this.oldNaxp;
		for (int i = 0; i < N; ++i)
		{
			checkSum += (long)naxp.GetEncoding(Sample0);
			checkSum += (long)naxp.GetEncoding(Sample1);
		}

		this.result = checkSum;
	}

	[Benchmark]
	public void New()
	{
		long checkSum = 0;

		Naxp naxp = this.newNaxp;
		for (int i = 0; i < N; ++i)
		{
			checkSum += (long)naxp.Encode(Sample0);
			checkSum += (long)naxp.Encode(Sample1);
		}

		this.result = checkSum;
	}
}