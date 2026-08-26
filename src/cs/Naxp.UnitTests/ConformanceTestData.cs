// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO;
#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#else
using Newtonsoft.Json;
#endif

namespace LogMu.UnitTests;

/// <summary>
/// <c>conformance/naxp-v0.5.json</c>, which was generated from the specification rather than
/// from any implementation. It is the oracle: the parser is not allowed to define its own truth.
/// </summary>
sealed class ConformanceTestData
{
	public string NaxpVersion { get; set; } = string.Empty;
	public int TestDataVersion { get; set; }
	public List<ConformanceCase> Cases { get; set; } = new();
	public List<ConformanceRejection> Rejected { get; set; } = new();

	public static ConformanceTestData Load()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "conformance", "naxp-v0.5.json");

		if (!File.Exists(path))
		{
			throw new FileNotFoundException(
				$"The conformance test data was not copied to the output directory. Expected it at {path}.",
				path);
		}

#if NET8_0_OR_GREATER
		// The counts and encoded values are carried as decimal strings, because a naxp may hold
		// up to 2^64 - 1 values and a JSON number is not safe above 2^53. Newtonsoft coerces
		// them for the net472 build without being asked.
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			NumberHandling = JsonNumberHandling.AllowReadingFromString,
		};
		ConformanceTestData? data = JsonSerializer.Deserialize<ConformanceTestData>(File.ReadAllText(path), options);
#else
		ConformanceTestData? data = null;
		using (StreamReader streamReader = File.OpenText(path))
		using (var jsonReader = new JsonTextReader(streamReader))
		{
			var serializer = new JsonSerializer();
			data = serializer.Deserialize<ConformanceTestData>(jsonReader);
		}
#endif

		return data ?? throw new InvalidOperationException($"{path} did not deserialise.");
	}
}

sealed class ConformanceCase
{
	public string Naxp { get; set; } = string.Empty;
	public string? Note { get; set; }
	public ulong ValueCount { get; set; }
	public ulong AcceptedCount { get; set; }
	public bool Complete { get; set; }
	public List<ConformanceValue> Values { get; set; } = new();
	public List<string> NotAccepted { get; set; } = new();
}

sealed class ConformanceValue
{
	public string In { get; set; } = string.Empty;
	public ulong Out { get; set; }
	public string? Canon { get; set; }
}

sealed class ConformanceRejection
{
	public string Naxp { get; set; } = string.Empty;
	public string Rule { get; set; } = string.Empty;
	public string? Note { get; set; }
}
