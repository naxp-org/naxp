// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Text;
using BenchmarkDotNet.Running;

namespace LogMu.Benchmarks;

static class Program
{
	static void Main(string[] args)
	{
		//BenchmarkRunner.Run<Initialisation>();
		//BenchmarkRunner.Run<Encoding>();
		BenchmarkRunner.Run<GeneratedEncoding>();
	}
}
