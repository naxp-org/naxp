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
public class Initialisation
{
	/*

// * Summary *

NEW CODE:

BenchmarkDotNet v0.15.2, Windows 11 (10.0.26200.9168)
11th Gen Intel Core i7-11800H 2.30GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 10.0.11 (10.0.1126.37416), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.11 (10.0.1126.37416), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

A. Checking naxp = '\A?\A\9\X? \s \9\A\A'

| Method | Mean     | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------- |---------:|------:|-------:|-------:|----------:|------------:|
| Old    | 5.679 us |  1.00 | 0.6561 |      - |   8.05 KB |        1.00 |
| New    | 7.407 us |  1.31 | 1.8082 | 0.0610 |  22.23 KB |        2.76 |

| Method | Mean     | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------- |---------:|------:|-------:|-------:|----------:|------------:|
| Old    | 5.236 us |  1.00 | 0.6561 |      - |   8.05 KB |        1.00 |
| New    | 7.132 us |  1.36 | 1.8082 | 0.0610 |  22.23 KB |        2.76 |

B. Checking naxp = '#[0-10]'

| Method | Mean       | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------- |-----------:|------:|-------:|-------:|----------:|------------:|
| Old    |   890.4 ns |  1.00 | 0.1488 |      - |   1.83 KB |        1.00 |
| New    | 2,317.1 ns |  2.60 | 0.6866 | 0.0114 |   8.45 KB |        4.62 |

| Method | Mean       | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------- |-----------:|------:|-------:|-------:|----------:|------------:|
| Old    |   869.2 ns |  1.00 | 0.1488 |      - |   1.83 KB |        1.00 |
| New    | 2,283.7 ns |  2.63 | 0.6866 | 0.0114 |   8.45 KB |        4.62 |

C. Checking naxp = '#[0-999999999999]'

| Method | Mean          | Ratio | Gen0      | Gen1      | Gen2      | Allocated   | Alloc Ratio |
|------- |--------------:|------:|----------:|----------:|----------:|------------:|------------:|
| Old    | 163,012.89 us | 1.000 | 5750.0000 | 5500.0000 | 1500.0000 | 56479.99 KB |       1.000 |
| New    |      27.91 us | 0.000 |    7.5684 |    0.8240 |         - |    93.01 KB |       0.002 |

-----------------------------

OLD CODE (possibly after some initial improvements?):

A. Checking naxp = '\A?\A\9\X? \s \9\A\A'

| Method | Mean      | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------- |----------:|------:|-------:|-------:|----------:|------------:|
| Old    |  7.096 us |  1.00 | 0.6561 |      - |   8.05 KB |        1.00 |
| New    | 19.120 us |  2.70 | 3.6316 | 0.1221 |  44.65 KB |        5.55 |

B. Checking naxp = '#[0-10]'

| Method | Mean     | Ratio | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------- |---------:|------:|-------:|-------:|----------:|------------:|
| Old    | 1.003 us |  1.00 | 0.1488 |      - |   1.83 KB |        1.00 |
| New    | 4.633 us |  4.63 | 1.1673 | 0.0153 |  14.39 KB |        7.87 |

-----------------------------
Conclusions:

1.	The new version is 1.3 to 2.6 times slower to initialise than the old version.
2.	The new version allocated 2.5 to 5 times as much memory as the old version.

	*/

	long result;

#if true
	const string NaxpText = "#[0-999999999999]";
	const string Sample0 = "0";
	const string Sample1 = "999999999999";
#else
#if true
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

		NXOld.NX naxp = NXOld.NX.Parse(NaxpText);
		checkSum += (long)naxp.GetEncoding(Sample0);
		checkSum += (long)naxp.GetEncoding(Sample1);

		this.result = checkSum;
	}

	[Benchmark]
	public void New()
	{
		long checkSum = 0;

		Naxp naxp = Naxp.Parse(NaxpText);
		checkSum += (long)naxp.Encode(Sample0);
		checkSum += (long)naxp.Encode(Sample1);

		this.result = checkSum;
	}
}