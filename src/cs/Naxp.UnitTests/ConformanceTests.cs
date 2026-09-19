// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The compiler against <c>conformance/naxp-v0.10.json</c>.
/// </summary>
/// <remarks>
/// Every rule the data tags is now implemented, W3, W5 and W6 included, so
/// <see cref="ImplementedRules"/> covers all of them and nothing here asserts a gap.
/// </remarks>
public class ConformanceTests
{
	static readonly ConformanceTestData TestData = ConformanceTestData.Load();

	/// <summary>The rules a naxp can currently be invalid for.</summary>
	static readonly HashSet<string> ImplementedRules = new(StringComparer.Ordinal)
	{
		"syntax", "W1", "W2", "W3", "W4", "W5", "W6",
	};

	[Fact]
	public void TestData_IsForVersion09()
	{
		Assert.Equal("0.10", TestData.NaxpVersion);
		Assert.NotEmpty(TestData.Cases);
		Assert.NotEmpty(TestData.InvalidNaxps);
	}

	[Fact]
	public void Cases_AreAccepted()
	{
		var failures = new List<string>();

		foreach (ConformanceCase item in TestData.Cases)
		{
			if (!Compiler.TryCompile(item.Naxp, out _, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was invalid: {error}");
			}
		}

		AssertNoFailures(failures);
	}

	/// <summary>
	/// The size of the canonical language, which is the count of encodable values, and the size
	/// of the accepted language. The two differ only where the naxp contains a unified element.
	/// </summary>
	[Fact]
	public void Cases_HaveTheStatedCounts()
	{
		var failures = new List<string>();

		foreach (ConformanceCase item in TestData.Cases)
		{
			if (!Compiler.TryCompile(item.Naxp, out Compilation? compilation, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was invalid: {error}");
				continue;
			}

			if (compilation!.MaxEncodedValue != (ulong)item.MaxEncodedValue)
			{
				failures.Add($"{item.Naxp} has {compilation.MaxEncodedValue} values, and the test data says {item.MaxEncodedValue}.");
			}

			if (compilation.AcceptedCount != (ulong)item.AcceptedCount)
			{
				failures.Add($"{item.Naxp} accepts {compilation.AcceptedCount} strings, and the test data says {item.AcceptedCount}.");
			}
		}

		AssertNoFailures(failures);
	}

	/// <summary>
	/// Every string the test data lists, matched two ways: against the tree by the backtracking
	/// tree walker, and against the machine built from it. An entry whose value is zero is one the
	/// naxp finds invalid; every other entry it accepts.
	/// </summary>
	[Fact]
	public void Values_AreAcceptedExactlyWhenTheTestDataSaysSo()
	{
		var failures = new List<string>();
		int checkCount = 0;

		foreach (ConformanceCase item in TestData.Cases)
		{
			if (!Compiler.TryCompile(item.Naxp, out Compilation? compilation, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was invalid: {error}");
				continue;
			}

			foreach (ConformanceValue value in item.Values)
			{
				++checkCount;
				Check(item.Naxp, compilation!, value.In, value.Out != 0L, failures);
			}

			foreach (string invalidText in item.Invalid)
			{
				++checkCount;
				Check(item.Naxp, compilation!, invalidText, false, failures);
			}
		}

		AssertNoFailures(failures);
		Assert.True(checkCount > 1400, $"Only {checkCount} strings were checked.");
	}

	static void Check(string naxp, Compilation compilation, string text, bool expected, List<string> failures)
	{
		bool byTree = TreeWalker.Generates(compilation.Ast, text, out bool tooLong);
		bool byMachine = Accepts(compilation.Accepted, text);

		if (tooLong)
		{
			failures.Add($"{naxp} abandoned '{text}' as too long.");
			return;
		}

		if (byTree != expected)
		{
			failures.Add($"{naxp} finds '{text}' {(byTree ? "valid" : "invalid")} by the tree, and the test data says otherwise.");
		}

		if (byMachine != expected)
		{
			failures.Add($"{naxp} finds '{text}' {(byMachine ? "valid" : "invalid")} by the machine, and the test data says otherwise.");
		}
	}

	/// <summary>
	/// Whether a machine accepts a string. Walking the transitions is all membership takes; the
	/// encoding built on the same walk is the next piece of work.
	/// </summary>
	static bool Accepts(StateMap map, string text)
	{
		State state = map.Start;

		foreach (char c in text)
		{
			State? next = null;

			foreach (Transition transition in state.Transitions)
			{
				if (transition.Set.Contains(c)) { next = transition.Next; break; }
			}

			if (next is null) { return false; }

			state = next;
		}

		return state.AcceptsEndOfText;
	}

	/// <summary>
	/// The whole contract the test data states: <c>out</c> is zero exactly where the naxp does not
	/// accept <c>in</c>; otherwise the canonical form of <c>in</c> is <c>canon</c>, decoding
	/// <c>out</c> gives <c>canon</c> back, and <c>canon</c> encodes to <c>out</c> itself.
	/// </summary>
	[Fact]
	public void Values_EncodeAndDecodeAsTheTestDataSays()
	{
		var failures = new List<string>();
		int checkCount = 0;

		foreach (ConformanceCase item in TestData.Cases)
		{
			if (!Compiler.TryCompile(item.Naxp, out Compilation? compilation, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was invalid: {error}");
				continue;
			}

			foreach (ConformanceValue value in item.Values)
			{
				++checkCount;

				ulong encoded = compilation!.Encode(value.In);

				if (encoded != (ulong)value.Out)
				{
					failures.Add($"{item.Naxp} encodes '{value.In}' to {encoded}, and the test data says {value.Out}.");
					continue;
				}

				if (value.Out == 0L) { continue; }

				if (!compilation.TryGetCanonicalForm(value.In, out string? canonical) || canonical != value.Canon)
				{
					failures.Add($"{item.Naxp} canonicalises '{value.In}' to '{canonical}', and the test data says '{value.Canon}'.");
				}

				if (!compilation.TryDecode(encoded, out string? decoded) || decoded != value.Canon)
				{
					failures.Add($"{item.Naxp} decodes {encoded} to '{decoded}', and the test data says '{value.Canon}'.");
				}

				ulong reEncoded = compilation.Encode(value.Canon!);

				if (reEncoded != encoded)
				{
					failures.Add($"{item.Naxp} encodes the canonical form '{value.Canon}' to {reEncoded} rather than {encoded}.");
				}
			}

			foreach (string invalidText in item.Invalid)
			{
				++checkCount;

				ulong encoded = compilation!.Encode(invalidText);

				if (encoded != 0UL)
				{
					failures.Add($"{item.Naxp} encodes '{invalidText}' to {encoded}, and the test data lists it as invalid.");
				}
			}
		}

		AssertNoFailures(failures);
		Assert.True(checkCount > 1400, $"Only {checkCount} strings were checked.");
	}

	/// <summary>
	/// Where the test data lists every accepted string, decoding each value in turn must give every
	/// canonical form exactly once, which is what makes the values a bijection onto 1..<i>k</i>.
	/// </summary>
	[Fact]
	public void CompleteCases_DecodeToABijection()
	{
		var failures = new List<string>();

		foreach (ConformanceCase item in TestData.Cases.Where(c => c.Complete && c.MaxEncodedValue <= 2000L))
		{
			if (!Compiler.TryCompile(item.Naxp, out Compilation? compilation, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was invalid: {error}");
				continue;
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);

			for (ulong value = 1UL; value <= (ulong)item.MaxEncodedValue; ++value)
			{
				if (!compilation!.TryDecode(value, out string? decoded))
				{
					failures.Add($"{item.Naxp} could not decode {value}, and it claims {item.MaxEncodedValue} values.");
					continue;
				}

				if (!seen.Add(decoded!))
				{
					failures.Add($"{item.Naxp} decodes two values to '{decoded}'.");
				}

				ulong again = compilation.Encode(decoded!);

				if (again != value)
				{
					failures.Add($"{item.Naxp} decodes {value} to '{decoded}', which encodes back to {again}.");
				}
			}

			if (compilation!.TryDecode((ulong)item.MaxEncodedValue + 1UL, out _))
			{
				failures.Add($"{item.Naxp} decoded a value above its count of {item.MaxEncodedValue}.");
			}
		}

		AssertNoFailures(failures);
	}

	/// <summary>
	/// Where the test data lists every accepted string, the count of them must match.
	/// </summary>
	[Fact]
	public void CompleteCases_ListEveryAcceptedString()
	{
		var failures = new List<string>();

		foreach (ConformanceCase item in TestData.Cases.Where(c => c.Complete))
		{
			ulong accepted = (ulong)item.Values.Count(v => v.Out != 0UL);

			if (accepted != item.AcceptedCount)
			{
				failures.Add($"{item.Naxp} lists {accepted} accepted strings but claims {item.AcceptedCount}.");
			}
		}

		AssertNoFailures(failures);
	}

	[Fact]
	public void InvalidNaxps_AreInvalidForTheStatedRule()
	{
		var failures = new List<string>();

		foreach (ConformanceInvalidNaxp item in TestData.InvalidNaxps.Where(r => ImplementedRules.Contains(r.Rule)))
		{
			if (Compiler.TryCompile(item.Naxp, out _, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was accepted; it breaks {item.Rule} ({item.Note}).");
				continue;
			}

			string actual = NaxpMessageRules.RuleOf(error!.Value.Message);

			if (!string.Equals(actual, item.Rule, StringComparison.Ordinal))
			{
				failures.Add($"{item.Naxp} was invalid for {actual} rather than {item.Rule}: {error}");
			}
		}

		AssertNoFailures(failures);
	}

	/// <summary>
	/// Every rule the test data names is now implemented, so nothing is skipped by
	/// <see cref="InvalidNaxps_AreInvalidForTheStatedRule"/>. This fails if a later version of the data
	/// introduces a rule the implementation does not know about.
	/// </summary>
	[Fact]
	public void InvalidNaxps_LeaveNoRuleUnimplemented()
	{
		string[] pending = TestData.InvalidNaxps
			.Select(r => r.Rule)
			.Where(rule => !ImplementedRules.Contains(rule))
			.Distinct()
			.ToArray();

		Assert.Empty(pending);
	}

	static void AssertNoFailures(List<string> failures)
	{
		Assert.True(
			failures.Count == 0,
			$"{failures.Count} failures:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
	}
}
