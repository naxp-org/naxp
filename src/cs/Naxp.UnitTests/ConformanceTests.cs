// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The compiler against <c>conformance/naxp-v0.4.json</c>.
/// </summary>
/// <remarks>
/// W3 needs the single-valuedness of a transduction, which is not implemented, so the test data
/// entry for it is held in a test of its own that asserts the gap. It will fail the moment W3
/// lands, which is the point.
/// </remarks>
public class ConformanceTests
{
	static readonly ConformanceTestData TestData = ConformanceTestData.Load();

	/// <summary>The rules a naxp can currently be refused for.</summary>
	static readonly HashSet<string> ImplementedRules = new(StringComparer.Ordinal)
	{
		"syntax", "W1", "W2", "W3", "W4", "W5",
	};

	[Fact]
	public void TestData_IsForVersion05()
	{
		Assert.Equal("0.5", TestData.NaxpVersion);
		Assert.NotEmpty(TestData.Cases);
		Assert.NotEmpty(TestData.Rejected);
	}

	[Fact]
	public void Cases_AreAccepted()
	{
		var failures = new List<string>();

		foreach (ConformanceCase item in TestData.Cases)
		{
			if (!Compiler.TryCompile(item.Naxp, out _, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was refused: {error}");
			}
		}

		AssertNoFailures(failures);
	}

	/// <summary>
	/// The size of the canonical language, which is the count of encodable values, and the size
	/// of the accepted language. The two differ only where the naxp contains a replacement.
	/// </summary>
	[Fact]
	public void Cases_HaveTheStatedCounts()
	{
		var failures = new List<string>();

		foreach (ConformanceCase item in TestData.Cases)
		{
			if (!Compiler.TryCompile(item.Naxp, out Compilation? compilation, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was refused: {error}");
				continue;
			}

			if (compilation!.ValueCount != (ulong)item.ValueCount)
			{
				failures.Add($"{item.Naxp} has {compilation.ValueCount} values, and the test data says {item.ValueCount}.");
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
	/// matcher, and against the machine built from it. An entry whose value is zero is one the
	/// naxp does not accept; every other entry is accepted.
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
				failures.Add($"{item.Naxp} was refused: {error}");
				continue;
			}

			foreach (ConformanceValue value in item.Values)
			{
				++checkCount;
				Check(item.Naxp, compilation!, value.In, value.Out != 0L, failures);
			}

			foreach (string notAccepted in item.NotAccepted)
			{
				++checkCount;
				Check(item.Naxp, compilation!, notAccepted, false, failures);
			}
		}

		AssertNoFailures(failures);
		Assert.True(checkCount > 1400, $"Only {checkCount} strings were checked.");
	}

	static void Check(string naxp, Compilation compilation, string text, bool expected, List<string> failures)
	{
		bool byTree = Matcher.Generates(compilation.Ast, text, out bool tooLong);
		bool byMachine = Accepts(compilation.Accepted, text);

		if (tooLong)
		{
			failures.Add($"{naxp} abandoned '{text}' as too long.");
			return;
		}

		if (byTree != expected)
		{
			failures.Add($"{naxp} {(byTree ? "accepts" : "does not accept")} '{text}' by the tree, and the test data says otherwise.");
		}

		if (byMachine != expected)
		{
			failures.Add($"{naxp} {(byMachine ? "accepts" : "does not accept")} '{text}' by the machine, and the test data says otherwise.");
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
				failures.Add($"{item.Naxp} was refused: {error}");
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

			foreach (string notAccepted in item.NotAccepted)
			{
				++checkCount;

				ulong encoded = compilation!.Encode(notAccepted);

				if (encoded != 0UL)
				{
					failures.Add($"{item.Naxp} encodes '{notAccepted}' to {encoded}, and the test data lists it as not accepted.");
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

		foreach (ConformanceCase item in TestData.Cases.Where(c => c.Complete && c.ValueCount <= 2000L))
		{
			if (!Compiler.TryCompile(item.Naxp, out Compilation? compilation, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was refused: {error}");
				continue;
			}

			var seen = new HashSet<string>(StringComparer.Ordinal);

			for (ulong value = 1UL; value <= (ulong)item.ValueCount; ++value)
			{
				if (!compilation!.TryDecode(value, out string? decoded))
				{
					failures.Add($"{item.Naxp} could not decode {value}, and it claims {item.ValueCount} values.");
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

			if (compilation!.TryDecode((ulong)item.ValueCount + 1UL, out _))
			{
				failures.Add($"{item.Naxp} decoded a value above its count of {item.ValueCount}.");
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
	public void Rejected_AreRefusedForTheStatedRule()
	{
		var failures = new List<string>();

		foreach (ConformanceRejection item in TestData.Rejected.Where(r => ImplementedRules.Contains(r.Rule)))
		{
			if (Compiler.TryCompile(item.Naxp, out _, out NaxpError? error))
			{
				failures.Add($"{item.Naxp} was accepted; it breaks {item.Rule} ({item.Note}).");
				continue;
			}

			string actual = NaxpMessageRules.RuleOf(error!.Value.Message);

			if (!string.Equals(actual, item.Rule, StringComparison.Ordinal))
			{
				failures.Add($"{item.Naxp} was refused for {actual} rather than {item.Rule}: {error}");
			}
		}

		AssertNoFailures(failures);
	}

	/// <summary>
	/// Every rule the test data rejects for is now implemented, so nothing is skipped by
	/// <see cref="Rejected_AreRefusedForTheStatedRule"/>. This fails if a later version of the data
	/// introduces a rule the implementation does not know about.
	/// </summary>
	[Fact]
	public void Rejected_LeaveNoRuleUnimplemented()
	{
		string[] pending = TestData.Rejected
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
