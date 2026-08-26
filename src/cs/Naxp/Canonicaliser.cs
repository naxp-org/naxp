// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;

namespace LogMu;

/// <summary>
/// Computes &#961;, the map from an accepted string to its canonical form.
/// </summary>
/// <remarks>
/// <para>
/// &#961;(<i>w</i>) is <i>w</i> with the match of each replaceable element replaced by that
/// element's rendering. The tree is where that is visible, since the machines have already
/// resolved it one way or the other, so this works over the tree.
/// </para>
/// <para>
/// It is <see cref="Matcher"/>'s set of positions with one output carried alongside each
/// position. A replaceable element contributes its rendering whatever it matched, which is the
/// whole of what makes the canonical form differ from the input.
/// </para>
/// <para>
/// One output per position is enough, and that rests on W3 being decided when the naxp was
/// compiled. Everything reached at a given point in the walk has the same future, since what
/// follows depends on the position alone; so two partial parses that meet at one position either
/// both reach the end or neither does. If both reach it they append the same remainder, and W3
/// says the two totals agree, which forces the two outputs to have agreed already. Carrying the
/// whole set would therefore only ever record the same string twice — and it was what made this
/// exponential, since <c>([ab]|[ab]!a){17}</c> reaches 2^17 outputs on an all-<c>b</c> input.
/// </para>
/// </remarks>
readonly ref struct Canonicaliser
{
	readonly ReadOnlySpan<char> text;
	readonly Dictionary<Ast, string> renderings;

	Canonicaliser(ReadOnlySpan<char> text)
	{
		this.text = text;
		this.renderings = [];
	}

	/// <summary>
	/// The canonical form of a string.
	/// </summary>
	/// <param name="ast">The parsed naxp, which must have been through <see cref="W3Checker"/>.</param>
	/// <param name="text">The string.</param>
	/// <param name="canonical">The canonical form, or <see langword="null"/> where there is none.</param>
	/// <returns>Whether the naxp accepts the string.</returns>
	public static bool TryCanonicalise(Ast ast, ReadOnlySpan<char> text, out string? canonical)
	{
		if (ast is null) { throw new ArgumentNullException(nameof(ast)); }

		canonical = null;

		// A naxp within the state budget has a longest string shorter than this, so anything
		// longer is not accepted rather than too costly to decide.
		if (text.Length > NaxpLimits.MaxStringLength) { return false; }

		var canonicaliser = new Canonicaliser(text);
		Dictionary<int, string> reached = canonicaliser.Advance(
			ast,
			new Dictionary<int, string> { [0] = string.Empty });

		if (!reached.TryGetValue(text.Length, out string? output)) { return false; }

		canonical = output;
		return true;
	}

	Dictionary<int, string> Advance(Ast node, Dictionary<int, string> starts)
	{
		if (starts.Count == 0) { return starts; }

		switch (node)
		{
			case AstEmpty:
				return starts;

			case AstChars chars:
			{
				var result = new Dictionary<int, string>();

				foreach (KeyValuePair<int, string> start in starts)
				{
					if (start.Key < this.text.Length && chars.CharSet.Contains(this.text[start.Key]))
					{
						Put(result, start.Key + 1, start.Value + this.text[start.Key]);
					}
				}

				return result;
			}

			case AstDigitsRange:
				// A digits range emits what it consumed.
				return this.Consume(node, starts);

			case AstSequence sequence:
			{
				var current = starts;

				foreach (Ast child in sequence.Children)
				{
					current = this.Advance(child, current);
					if (current.Count == 0) { break; }
				}

				return current;
			}

			case AstAlternation alternation:
			{
				var result = new Dictionary<int, string>();
				foreach (Ast child in alternation.Children) { PutAll(result, this.Advance(child, starts)); }

				return result;
			}

			case AstOptional optional:
			{
				var result = new Dictionary<int, string>(starts);
				PutAll(result, this.Advance(optional.Child, starts));

				return result;
			}

			case AstInterval interval:
			{
				Dictionary<int, string> result = interval.MinCount == 0
					? new Dictionary<int, string>(starts)
					: new Dictionary<int, string>()
					;
				Dictionary<int, string> current = starts;

				for (int i = 1; i <= interval.MaxCount; ++i)
				{
					Dictionary<int, string> next = this.Advance(interval.Child, current);
					if (next.Count == 0) { break; }

					if (i >= interval.MinCount) { PutAll(result, next); }

					// A child that matches nothing reaches a fixed point at once.
					if (SameAs(next, current) && i >= interval.MinCount) { break; }

					current = next;
				}

				return result;
			}

			case AstReplaceable replaceable:
			{
				// This is the whole of the difference between a string and its canonical form:
				// whatever the subject matched, the rendering is what comes out.
				string rendering = this.RenderingOf(replaceable);
				var result = new Dictionary<int, string>();

				foreach (KeyValuePair<int, string> start in starts)
				{
					foreach (int end in Matcher.Advance(replaceable.Subject, this.text, new HashSet<int> { start.Key }))
					{
						Put(result, end, start.Value + rendering);
					}
				}

				return result;
			}

			default:
				throw new InvalidOperationException($"Unhandled node type {node.GetType().Name}.");
		}
	}

	/// <summary>
	/// Advances by a node that emits exactly the characters it consumed.
	/// </summary>
	Dictionary<int, string> Consume(Ast node, Dictionary<int, string> starts)
	{
		var result = new Dictionary<int, string>();

		foreach (KeyValuePair<int, string> start in starts)
		{
			foreach (int end in Matcher.Advance(node, this.text, new HashSet<int> { start.Key }))
			{
				Put(result, end, start.Value + this.text.Slice(start.Key, end - start.Key).ToString());
			}
		}

		return result;
	}

	string RenderingOf(AstReplaceable replaceable)
	{
		if (this.renderings.TryGetValue(replaceable, out string? cached)) { return cached; }

		// W1 has already established that the rendering generates exactly one string.
		if (Matcher.TryGetSingleString(replaceable.Rendering, out string? rendering) != SingleStringOutcome.Single)
		{
			throw new InvalidOperationException("A replaceable element passed W1 but has no single rendering.");
		}

		this.renderings.Add(replaceable, rendering!);

		return rendering!;
	}

	/// <summary>
	/// Records an output for a position, keeping whichever arrived first.
	/// </summary>
	/// <remarks>
	/// Under W3 a second one that matters cannot differ from the first; see the note on the class.
	/// </remarks>
	static void Put(Dictionary<int, string> target, int end, string output)
	{
		if (!target.ContainsKey(end)) { target.Add(end, output); }
	}

	static void PutAll(Dictionary<int, string> target, Dictionary<int, string> source)
	{
		foreach (KeyValuePair<int, string> item in source) { Put(target, item.Key, item.Value); }
	}

	static bool SameAs(Dictionary<int, string> left, Dictionary<int, string> right)
	{
		if (left.Count != right.Count) { return false; }

		foreach (KeyValuePair<int, string> item in left)
		{
			if (!right.TryGetValue(item.Key, out string? other)) { return false; }
			if (!string.Equals(item.Value, other, StringComparison.Ordinal)) { return false; }
		}

		return true;
	}
}
