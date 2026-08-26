// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using LogMu;

namespace LogMu.UnitTests;

/// <summary>
/// What came of asking this reference for a string's canonical form.
/// </summary>
enum ReferenceOutcome
{
	/// <summary>The naxp does not accept the string, so it has no canonical form.</summary>
	NotAccepted,

	/// <summary>The string has exactly one canonical form.</summary>
	Single,

	/// <summary>The string has more than one canonical form, so the naxp breaks W3.</summary>
	Ambiguous,

	/// <summary>Deciding would cost more than this is willing to spend.</summary>
	TooLarge,
}

/// <summary>
/// &#961; computed by carrying every output, kept as an oracle for the W3 tests.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="Canonicaliser"/> used to be, before W3 moved to construction time.
/// Because it keeps the whole set of outputs rather than one per position, it can see a string
/// with two canonical forms, which the production one deliberately no longer can: it is entitled
/// to assume W3 holds and would quietly return whichever output it met first.
/// </para>
/// <para>
/// That is why this lives here. <see cref="W3Tests"/> needs an oracle that shares no reasoning
/// with the square — a walk of the tree carrying full outputs, against a walk of an automaton
/// carrying pairs and a delay — and the production code is no longer able to be one. The cost
/// this pays for saying more is the exponential the production one was simplified to escape, so
/// it is only ever pointed at small naxps.
/// </para>
/// </remarks>
static class ReferenceCanonicaliser
{
	/// <summary>Where the output set is allowed to stop.</summary>
	const int MaxOutputs = 20_000;

	public static ReferenceOutcome TryCanonicalise(Ast ast, string text, out string? canonical)
	{
		if (ast is null) { throw new ArgumentNullException(nameof(ast)); }
		if (text is null) { throw new ArgumentNullException(nameof(text)); }

		canonical = null;

		if (text.Length > NaxpLimits.MaxStringLength) { return ReferenceOutcome.TooLarge; }

		var walk = new Walk(text);
		HashSet<Partial> reached = walk.Advance(ast, new HashSet<Partial> { new Partial(0, string.Empty) });

		if (walk.TooLarge) { return ReferenceOutcome.TooLarge; }

		var outputs = new HashSet<string>(StringComparer.Ordinal);
		foreach (Partial partial in reached)
		{
			if (partial.End == text.Length) { outputs.Add(partial.Output); }
		}

		if (outputs.Count == 0) { return ReferenceOutcome.NotAccepted; }
		if (outputs.Count > 1) { return ReferenceOutcome.Ambiguous; }

		foreach (string output in outputs) { canonical = output; }

		return ReferenceOutcome.Single;
	}

	sealed class Walk
	{
		readonly string text;
		readonly Dictionary<Ast, string> renderings = new();

		public Walk(string text)
		{
			this.text = text;
		}

		public bool TooLarge { get; private set; }

		public HashSet<Partial> Advance(Ast node, HashSet<Partial> starts)
		{
			if (this.TooLarge || starts.Count == 0) { return starts; }

			switch (node)
			{
				case AstEmpty:
					return starts;

				case AstChars chars:
				{
					var result = new HashSet<Partial>();

					foreach (Partial partial in starts)
					{
						if (partial.End < this.text.Length && chars.CharSet.Contains(this.text[partial.End]))
						{
							result.Add(partial.Extend(partial.End + 1, this.text[partial.End].ToString()));
						}
					}

					return this.Guard(result);
				}

				case AstDigitsRange:
					return this.Guard(this.Consume(node, starts));

				case AstSequence sequence:
				{
					HashSet<Partial> current = starts;

					foreach (Ast child in sequence.Children)
					{
						current = this.Advance(child, current);
						if (current.Count == 0) { break; }
					}

					return current;
				}

				case AstAlternation alternation:
				{
					var result = new HashSet<Partial>();
					foreach (Ast child in alternation.Children) { result.UnionWith(this.Advance(child, starts)); }

					return this.Guard(result);
				}

				case AstOptional optional:
				{
					var result = new HashSet<Partial>(starts);
					result.UnionWith(this.Advance(optional.Child, starts));

					return this.Guard(result);
				}

				case AstInterval interval:
				{
					HashSet<Partial> result = interval.MinCount == 0
						? new HashSet<Partial>(starts)
						: new HashSet<Partial>()
						;
					HashSet<Partial> current = starts;

					for (int i = 1; i <= interval.MaxCount; ++i)
					{
						HashSet<Partial> next = this.Advance(interval.Child, current);
						if (next.Count == 0 || this.TooLarge) { break; }

						if (i >= interval.MinCount) { result.UnionWith(next); }

						if (next.SetEquals(current) && i >= interval.MinCount) { break; }

						current = next;
					}

					return this.Guard(result);
				}

				case AstReplaceable replaceable:
				{
					string rendering = this.RenderingOf(replaceable);
					var result = new HashSet<Partial>();

					foreach (Partial partial in starts)
					{
						foreach (int end in Matcher.Advance(replaceable.Subject, this.text, new HashSet<int> { partial.End }))
						{
							result.Add(partial.Extend(end, rendering));
						}
					}

					return this.Guard(result);
				}

				default:
					throw new InvalidOperationException($"Unhandled node type {node.GetType().Name}.");
			}
		}

		HashSet<Partial> Consume(Ast node, HashSet<Partial> starts)
		{
			var result = new HashSet<Partial>();

			foreach (Partial partial in starts)
			{
				foreach (int end in Matcher.Advance(node, this.text, new HashSet<int> { partial.End }))
				{
					result.Add(partial.Extend(end, this.text.Substring(partial.End, end - partial.End)));
				}
			}

			return result;
		}

		string RenderingOf(AstReplaceable replaceable)
		{
			if (this.renderings.TryGetValue(replaceable, out string? cached)) { return cached; }

			if (Matcher.TryGetSingleString(replaceable.Rendering, out string? rendering) != SingleStringOutcome.Single)
			{
				throw new InvalidOperationException("A replaceable element passed W1 but has no single rendering.");
			}

			this.renderings.Add(replaceable, rendering!);

			return rendering!;
		}

		HashSet<Partial> Guard(HashSet<Partial> result)
		{
			if (result.Count > MaxOutputs)
			{
				this.TooLarge = true;
				return new HashSet<Partial>();
			}

			return result;
		}
	}

	/// <summary>
	/// A position in the string, paired with what has been emitted to reach it.
	/// </summary>
	readonly struct Partial : IEquatable<Partial>
	{
		public Partial(int end, string output)
		{
			this.End = end;
			this.Output = output;
		}

		public int End { get; }

		public string Output { get; }

		public Partial Extend(int end, string emitted) => new Partial(end, this.Output + emitted);

		public bool Equals(Partial other)
			=> this.End == other.End && string.Equals(this.Output, other.Output, StringComparison.Ordinal);

		public override bool Equals(object? obj) => obj is Partial other && this.Equals(other);

		public override int GetHashCode() => (this.End * 31) ^ StringComparer.Ordinal.GetHashCode(this.Output);
	}
}
