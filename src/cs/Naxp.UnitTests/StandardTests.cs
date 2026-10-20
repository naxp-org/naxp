// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#else
using Newtonsoft.Json;
#endif
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The standard naxps built into the library, against <c>standards/naxp-standards.json</c>.
/// <c>StandardTable.g.cs</c> is generated from that file; these tests prove the library answers
/// as the file says, for every standard and variant.
/// </summary>
public class StandardTests
{
	static readonly StandardsFile Data = StandardsFile.Load();

	[Fact]
	public void AllStandardNaxps_ListsTheFileRowForRow()
	{
		var expected = new List<(string, string, string, string, string)>();
		var actual = new List<(string, string, string, string, string)>();

		foreach (StandardEntry standard in Data.Standards)
		{
			foreach (StandardVariantEntry variant in standard.Variants)
			{
				expected.Add((standard.Id, standard.Name, variant.Id, variant.Naxp, variant.Notes));
			}
		}

		foreach (StandardNaxpVariant row in Naxp.AllStandardNaxps)
		{
			actual.Add((row.Id, row.Name, row.Variant, row.Pattern, row.Notes));
		}

		Assert.Equal("naxp-standards/0.1", Data.Format);
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void StandardNaxp_GivesEveryVariant_AndItsExamplesEncodeAsTheFileSays()
	{
		foreach (StandardEntry standard in Data.Standards)
		{
			foreach (StandardVariantEntry variant in standard.Variants)
			{
				Naxp naxp = Naxp.StandardNaxp(standard.Id, variant.Id);

				Assert.Equal(variant.Naxp, naxp.Pattern);
				Assert.Equal(variant.MaxEncodedValue, naxp.MaxEncodedValue);

				foreach (StandardExample example in variant.Valid)
				{
					Assert.Equal(example.Value, naxp.Encode(example.Text));
					Assert.Equal(example.Canonical, naxp.GetCanonicalForm(example.Text));
					Assert.Equal(example.Canonical, naxp.Decode(example.Value));
				}

				foreach (string text in variant.Invalid)
				{
					Assert.Equal(0UL, naxp.Encode(text));
				}
			}
		}
	}

	[Fact]
	public void StandardNaxp_MatchesIdsWithoutRegardToAsciiCase()
	{
		foreach (StandardNaxpVariant row in Naxp.AllStandardNaxps)
		{
			Assert.True(Naxp.TryStandardNaxp(row.Id.ToUpperInvariant(), row.Variant.ToUpperInvariant(), out Naxp? naxp));
			Assert.Equal(row.Pattern, naxp.Pattern);
		}

		// The long s and the dotless i are s and i to OrdinalIgnoreCase, and the Kelvin sign is k
		// to ToLowerInvariant. None of them may be a letter of an id.
		Assert.False(Naxp.TryStandardNaxp("uk-poſtcode", out _));
		Assert.False(Naxp.TryStandardNaxp("eırcode", out _));
		Assert.False(Naxp.TryStandardNaxp("uK-postcode", out _));
	}

	[Fact]
	public void StandardNaxp_TakesANullOrEmptyVariantAsTheDefault()
	{
		string expected = Naxp.StandardNaxp("uk-postcode", "default").Pattern;

		Assert.Equal(expected, Naxp.StandardNaxp("uk-postcode").Pattern);
		Assert.Equal(expected, Naxp.StandardNaxp("uk-postcode", null).Pattern);
		Assert.Equal(expected, Naxp.StandardNaxp("uk-postcode", "").Pattern);
		Assert.Equal(expected, Naxp.StandardNaxp("uk-postcode", "DEFAULT").Pattern);
		Assert.True(Naxp.TryStandardNaxp("uk-postcode", out Naxp? naxp));
		Assert.Equal(expected, naxp.Pattern);
	}

	[Fact]
	public void StandardNaxp_RefusesAnUnknownIdOrVariant()
	{
		ArgumentException unknownId = Assert.Throws<ArgumentException>(() => Naxp.StandardNaxp("uk-postcod"));
		ArgumentException unknownVariant = Assert.Throws<ArgumentException>(() => Naxp.StandardNaxp("UK-Postcode", "strict"));

		Assert.Equal("id", unknownId.ParamName);
		Assert.StartsWith(
			"There is no standard naxp 'uk-postcod'. The standard naxps are uk-postcode, eircode, nl-postcode and flight-designator.",
			unknownId.Message);
		Assert.Equal("variant", unknownVariant.ParamName);
		Assert.StartsWith(
			"The standard naxp 'uk-postcode' has no variant 'strict'. Its variants are default, padding-tolerant and padding-canonical.",
			unknownVariant.Message);

		Assert.Throws<ArgumentException>(() => Naxp.StandardNaxp(""));
		Assert.False(Naxp.TryStandardNaxp("uk-postcod", out _));
		Assert.False(Naxp.TryStandardNaxp("uk-postcode", "strict", out _));
		Assert.Throws<ArgumentNullException>(() => Naxp.StandardNaxp(null!));
		Assert.Throws<ArgumentNullException>(() => Naxp.TryStandardNaxp(null!, out _));
	}

	/// <summary>
	/// <c>StandardNaxp.g.cs</c> holds each standard in a class of its own, whose constants are
	/// that standard's naxps and nothing else, its default in <c>Default</c>.
	/// </summary>
	[Fact]
	public void StandardNaxpConstants_HoldEachStandardInAClassOfItsOwn()
	{
		Type[] classes = typeof(StandardNaxp).GetNestedTypes();

		Assert.Equal(Data.Standards.Count, classes.Length);

		foreach (StandardEntry standard in Data.Standards)
		{
			string defaultNaxp = standard.Variants[0].Naxp;
			Type found = Assert.Single(classes, type => (string?)type.GetField("Default")?.GetRawConstantValue() == defaultNaxp);

			List<string> expected = standard.Variants.Select(variant => variant.Naxp).OrderBy(naxp => naxp, StringComparer.Ordinal).ToList();
			List<string> actual = found.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(field => field.IsLiteral)
				.Select(field => (string)field.GetRawConstantValue()!)
				.OrderBy(naxp => naxp, StringComparer.Ordinal)
				.ToList();

			Assert.Equal(expected, actual);
		}
	}

	[Fact]
	public void StandardNaxpConstants_AreNamedAfterTheIds()
	{
		Assert.Equal(StandardNaxp.UKPostcode.Default, Naxp.StandardNaxp("uk-postcode").Pattern);
		Assert.Equal(StandardNaxp.UKPostcode.PaddingTolerant, Naxp.StandardNaxp("uk-postcode", "padding-tolerant").Pattern);
		Assert.Equal(StandardNaxp.NLPostcode.NoSeparator, Naxp.StandardNaxp("nl-postcode", "no-separator").Pattern);
		Assert.Equal(StandardNaxp.FlightDesignator.Strict, Naxp.StandardNaxp("flight-designator", "strict").Pattern);
	}
}

/// <summary>
/// <c>standards/naxp-standards.json</c>, read as the conformance data is.
/// </summary>
sealed class StandardsFile
{
	public string Format { get; set; } = string.Empty;
	public List<StandardEntry> Standards { get; set; } = new();

	public static StandardsFile Load()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "standards", "naxp-standards.json");

#if NET8_0_OR_GREATER
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			NumberHandling = JsonNumberHandling.AllowReadingFromString,
		};
		StandardsFile? data = JsonSerializer.Deserialize<StandardsFile>(File.ReadAllText(path), options);
#else
		StandardsFile? data = null;
		using (StreamReader streamReader = File.OpenText(path))
		using (var jsonReader = new JsonTextReader(streamReader))
		{
			var serializer = new JsonSerializer();
			data = serializer.Deserialize<StandardsFile>(jsonReader);
		}
#endif

		return data ?? throw new InvalidOperationException($"{path} did not deserialise.");
	}
}

sealed class StandardEntry
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public List<StandardVariantEntry> Variants { get; set; } = new();
}

sealed class StandardVariantEntry
{
	public string Id { get; set; } = string.Empty;
	public string Naxp { get; set; } = string.Empty;
	public ulong MaxEncodedValue { get; set; }
	public string Notes { get; set; } = string.Empty;
	public List<StandardExample> Valid { get; set; } = new();
	public List<string> Invalid { get; set; } = new();
}

sealed class StandardExample
{
	public string Text { get; set; } = string.Empty;
	public ulong Value { get; set; }
	public string Canonical { get; set; } = string.Empty;
}
