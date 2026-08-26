// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;

namespace LogMu;

/// <summary>
/// What an <see cref="Rx"/> node is.
/// </summary>
enum RxKind
{
	/// <summary>The empty language, which arises only as a derivative.</summary>
	EmptySet,
	/// <summary>The language holding only the empty string.</summary>
	Epsilon,
	/// <summary>A non-empty set of characters matching one position.</summary>
	Chars,
	/// <summary>Two or more expressions in sequence.</summary>
	Concat,
	/// <summary>Two or more expressions in alternation.</summary>
	Union,
	/// <summary>An expression repeated between <see cref="Rx.MinCount"/> and <see cref="Rx.MaxCount"/> times.</summary>
	Interval,
}

/// <summary>
/// An expression in the algebra the state map is built over.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not <see cref="Ast"/>. The tree records what was written, whereas these
/// nodes are what derivatives are taken of: they carry an empty language, they are normalised by
/// their factory, and they are interned, so equal expressions are the same object and reference
/// equality can be relied on as a dictionary key.
/// </para>
/// <para>
/// Normalisation does not have to reduce every expression denoting the same language to one
/// form, and it does not. Making the machine canonical is the job of hash-consing on transition
/// lists in <see cref="StateMapBuilder"/>; normalisation here only keeps derivatives from
/// growing and makes memoisation bite.
/// </para>
/// <para>
/// Intervals stay symbolic. Expanding <c>(A{99}){99}</c> into nearly ten thousand nodes would
/// throw away the reason the count cap exists.
/// </para>
/// </remarks>
sealed class Rx
{
	AsciiCharSet[]? firstSets;

	internal Rx(int id, RxKind kind, AsciiCharSet charSet, Rx[] children, int minCount, int maxCount, bool isNullable, long maxLength)
	{
		this.Id = id;
		this.Kind = kind;
		this.CharSet = charSet;
		this.Children = children;
		this.MinCount = minCount;
		this.MaxCount = maxCount;
		this.IsNullable = isNullable;
		this.MaxLength = maxLength;
	}

	/// <summary>A number unique within the factory that made this node.</summary>
	public int Id { get; }

	public RxKind Kind { get; }

	/// <summary>The characters, for <see cref="RxKind.Chars"/>.</summary>
	public AsciiCharSet CharSet { get; }

	/// <summary>The operands, for <see cref="RxKind.Concat"/>, <see cref="RxKind.Union"/> and <see cref="RxKind.Interval"/>.</summary>
	public Rx[] Children { get; }

	public int MinCount { get; }

	public int MaxCount { get; }

	/// <summary>Whether the language holds the empty string.</summary>
	public bool IsNullable { get; }

	/// <summary>
	/// The length of the longest string in the language, or zero where the language is empty.
	/// </summary>
	/// <remarks>
	/// Exact rather than an upper bound, and it strictly decreases along every derivative, which
	/// is what lets the builder order the states without a topological sort.
	/// </remarks>
	public long MaxLength { get; }

	/// <summary>
	/// The character sets that can match the first character of a string in this language.
	/// </summary>
	/// <remarks>
	/// These overlap in general. The minterms of them refine the first classes the specification
	/// defines, and the builder recovers the classes themselves by merging afterwards.
	/// </remarks>
	public AsciiCharSet[] GetFirstSets()
	{
		if (this.firstSets is not null) { return this.firstSets; }

		var sets = new List<AsciiCharSet>();
		this.CollectFirstSets(sets);

		return this.firstSets = sets.ToArray();
	}

	void CollectFirstSets(List<AsciiCharSet> sets)
	{
		switch (this.Kind)
		{
			case RxKind.EmptySet:
			case RxKind.Epsilon:
				return;

			case RxKind.Chars:
				sets.Add(this.CharSet);
				return;

			case RxKind.Concat:
				foreach (Rx child in this.Children)
				{
					child.CollectFirstSets(sets);
					if (!child.IsNullable) { return; }
				}

				return;

			case RxKind.Union:
				foreach (Rx child in this.Children) { child.CollectFirstSets(sets); }
				return;

			case RxKind.Interval:
				this.Children[0].CollectFirstSets(sets);
				return;

			default:
				throw new InvalidOperationException($"Unhandled kind {this.Kind}.");
		}
	}
}

/// <summary>
/// Makes <see cref="Rx"/> nodes, normalising and interning as it goes.
/// </summary>
/// <remarks>
/// One factory per build. Interning is not shared between naxps, so nothing accumulates and
/// nothing needs locking.
/// </remarks>
sealed class RxFactory
{
	readonly Dictionary<RxKey, Rx> interned = new();
	readonly Dictionary<DerivativeKey, Rx> derivatives = new();
	int nextId;

	public RxFactory()
	{
		this.EmptySet = this.Intern(RxKind.EmptySet, AsciiCharSet.Empty, Array.Empty<Rx>(), 0, 0, false, 0L);
		this.Epsilon = this.Intern(RxKind.Epsilon, AsciiCharSet.Empty, Array.Empty<Rx>(), 0, 0, true, 0L);
	}

	/// <summary>The empty language.</summary>
	public Rx EmptySet { get; }

	/// <summary>The language holding only the empty string.</summary>
	public Rx Epsilon { get; }

	/// <summary>How many distinct expressions this factory has made.</summary>
	public int Count => this.interned.Count;

	public Rx Chars(AsciiCharSet set)
		=> set.IsEmpty
			? this.EmptySet
			: this.Intern(RxKind.Chars, set, Array.Empty<Rx>(), 0, 0, false, 1L)
			;

	/// <summary>
	/// Concatenation, flattened, with the empty string dropped and the empty language absorbing.
	/// </summary>
	public Rx Concat(IReadOnlyList<Rx> parts)
	{
		var flattened = new List<Rx>();

		foreach (Rx part in parts)
		{
			if (part.Kind == RxKind.EmptySet) { return this.EmptySet; }
			if (part.Kind == RxKind.Epsilon) { continue; }

			if (part.Kind == RxKind.Concat) { flattened.AddRange(part.Children); }
			else { flattened.Add(part); }
		}

		if (flattened.Count == 0) { return this.Epsilon; }
		if (flattened.Count == 1) { return flattened[0]; }

		bool isNullable = true;
		long maxLength = 0L;

		foreach (Rx part in flattened)
		{
			isNullable &= part.IsNullable;
			maxLength = SaturatingAdd(maxLength, part.MaxLength);
		}

		return this.Intern(RxKind.Concat, AsciiCharSet.Empty, flattened.ToArray(), 0, 0, isNullable, maxLength);
	}

	public Rx Concat(Rx first, Rx second) => this.Concat(new[] { first, second });

	/// <summary>
	/// Alternation, flattened, with the empty language dropped and duplicates removed.
	/// </summary>
	/// <remarks>
	/// The operands are sorted by <see cref="Rx.Id"/>. Ids differ between runs, but within one
	/// run two unions over the same operands sort the same way, which is all interning needs.
	/// </remarks>
	public Rx Union(IReadOnlyList<Rx> alternatives)
	{
		var flattened = new List<Rx>();

		foreach (Rx alternative in alternatives)
		{
			if (alternative.Kind == RxKind.EmptySet) { continue; }

			if (alternative.Kind == RxKind.Union) { flattened.AddRange(alternative.Children); }
			else { flattened.Add(alternative); }
		}

		flattened.Sort(static (left, right) => left.Id.CompareTo(right.Id));

		var distinct = new List<Rx>(flattened.Count);
		foreach (Rx alternative in flattened)
		{
			if (distinct.Count == 0 || !ReferenceEquals(distinct[distinct.Count - 1], alternative))
			{
				distinct.Add(alternative);
			}
		}

		if (distinct.Count == 0) { return this.EmptySet; }
		if (distinct.Count == 1) { return distinct[0]; }

		bool isNullable = false;
		long maxLength = 0L;

		foreach (Rx alternative in distinct)
		{
			isNullable |= alternative.IsNullable;
			maxLength = Math.Max(maxLength, alternative.MaxLength);
		}

		return this.Intern(RxKind.Union, AsciiCharSet.Empty, distinct.ToArray(), 0, 0, isNullable, maxLength);
	}

	public Rx Union(Rx first, Rx second) => this.Union(new[] { first, second });

	/// <summary>
	/// Between <paramref name="minCount"/> and <paramref name="maxCount"/> copies in sequence.
	/// </summary>
	public Rx Interval(Rx child, int minCount, int maxCount)
	{
		if (maxCount == 0) { return this.Epsilon; }
		if (child.Kind == RxKind.Epsilon) { return this.Epsilon; }
		if (child.Kind == RxKind.EmptySet) { return minCount == 0 ? this.Epsilon : this.EmptySet; }

		// Where the child accepts the empty string, so does every count above the minimum, and
		// x{m,n} and x{0,n} are the same language. Normalising here is what lets IsNullable be
		// read off the minimum alone.
		if (child.IsNullable) { minCount = 0; }

		if (minCount == 1 && maxCount == 1) { return child; }

		long maxLength = SaturatingMultiply(child.MaxLength, maxCount);

		return this.Intern(RxKind.Interval, AsciiCharSet.Empty, new[] { child }, minCount, maxCount, minCount == 0, maxLength);
	}

	/// <summary>
	/// The derivative of <paramref name="expression"/> after any character of
	/// <paramref name="minterm"/>.
	/// </summary>
	/// <param name="expression">The expression to differentiate.</param>
	/// <param name="minterm">
	/// A minterm of the expression's first sets. Every character in it must behave alike, which
	/// is what makes one derivative stand for the whole set.
	/// </param>
	/// <returns>The derivative, which is <see cref="EmptySet"/> where nothing follows.</returns>
	public Rx Derivative(Rx expression, AsciiCharSet minterm)
	{
		var key = new DerivativeKey(expression.Id, minterm);
		if (this.derivatives.TryGetValue(key, out Rx? cached)) { return cached; }

		Rx result = this.ComputeDerivative(expression, minterm);
		this.derivatives.Add(key, result);

		return result;
	}

	Rx ComputeDerivative(Rx expression, AsciiCharSet minterm)
	{
		switch (expression.Kind)
		{
			case RxKind.EmptySet:
			case RxKind.Epsilon:
				return this.EmptySet;

			case RxKind.Chars:
				// The minterm is wholly inside the set or wholly outside it.
				return minterm.IntersectsWith(expression.CharSet) ? this.Epsilon : this.EmptySet;

			case RxKind.Concat:
			{
				var alternatives = new List<Rx>();

				for (int i = 0; i < expression.Children.Length; ++i)
				{
					Rx head = this.Derivative(expression.Children[i], minterm);

					if (head.Kind != RxKind.EmptySet)
					{
						var parts = new List<Rx>(expression.Children.Length - i) { head };
						for (int j = i + 1; j < expression.Children.Length; ++j) { parts.Add(expression.Children[j]); }

						alternatives.Add(this.Concat(parts));
					}

					// Only a part that can match nothing lets the character be consumed later on.
					if (!expression.Children[i].IsNullable) { break; }
				}

				return this.Union(alternatives);
			}

			case RxKind.Union:
			{
				var alternatives = new List<Rx>(expression.Children.Length);
				foreach (Rx child in expression.Children) { alternatives.Add(this.Derivative(child, minterm)); }

				return this.Union(alternatives);
			}

			case RxKind.Interval:
			{
				Rx child = expression.Children[0];
				Rx head = this.Derivative(child, minterm);
				if (head.Kind == RxKind.EmptySet) { return this.EmptySet; }

				int minCount = expression.MinCount == 0 ? 0 : expression.MinCount - 1;

				return this.Concat(head, this.Interval(child, minCount, expression.MaxCount - 1));
			}

			default:
				throw new InvalidOperationException($"Unhandled kind {expression.Kind}.");
		}
	}

	Rx Intern(RxKind kind, AsciiCharSet charSet, Rx[] children, int minCount, int maxCount, bool isNullable, long maxLength)
	{
		var key = new RxKey(kind, charSet, children, minCount, maxCount);

		if (this.interned.TryGetValue(key, out Rx? existing)) { return existing; }

		var created = new Rx(this.nextId++, kind, charSet, children, minCount, maxCount, isNullable, maxLength);
		this.interned.Add(key, created);

		return created;
	}

	static long SaturatingAdd(long left, long right)
	{
		long sum = left + right;
		return sum < 0L ? long.MaxValue : sum;
	}

	static long SaturatingMultiply(long left, long right)
	{
		if (left == 0L || right == 0L) { return 0L; }

		return left > long.MaxValue / right ? long.MaxValue : left * right;
	}

	/// <summary>
	/// The identity of an expression: its shape and its operands, which are already interned and
	/// so are compared by id.
	/// </summary>
	readonly struct RxKey : IEquatable<RxKey>
	{
		readonly RxKind kind;
		readonly AsciiCharSet charSet;
		readonly Rx[] children;
		readonly int minCount;
		readonly int maxCount;
		readonly int hash;

		public RxKey(RxKind kind, AsciiCharSet charSet, Rx[] children, int minCount, int maxCount)
		{
			this.kind = kind;
			this.charSet = charSet;
			this.children = children;
			this.minCount = minCount;
			this.maxCount = maxCount;

			int accumulated = (int)kind;
			accumulated = (accumulated * 31) + charSet.GetHashCode();
			accumulated = (accumulated * 31) + minCount;
			accumulated = (accumulated * 31) + maxCount;
			foreach (Rx child in children) { accumulated = (accumulated * 31) + child.Id; }

			this.hash = accumulated;
		}

		public bool Equals(RxKey other)
		{
			if (this.kind != other.kind
				|| this.minCount != other.minCount
				|| this.maxCount != other.maxCount
				|| !this.charSet.Equals(other.charSet)
				|| this.children.Length != other.children.Length)
			{
				return false;
			}

			for (int i = 0; i < this.children.Length; ++i)
			{
				if (!ReferenceEquals(this.children[i], other.children[i])) { return false; }
			}

			return true;
		}

		public override bool Equals(object? obj) => obj is RxKey other && this.Equals(other);

		public override int GetHashCode() => this.hash;
	}

	readonly struct DerivativeKey : IEquatable<DerivativeKey>
	{
		readonly int expressionId;
		readonly AsciiCharSet minterm;

		public DerivativeKey(int expressionId, AsciiCharSet minterm)
		{
			this.expressionId = expressionId;
			this.minterm = minterm;
		}

		public bool Equals(DerivativeKey other)
			=> this.expressionId == other.expressionId && this.minterm.Equals(other.minterm);

		public override bool Equals(object? obj) => obj is DerivativeKey other && this.Equals(other);

		public override int GetHashCode() => (this.expressionId * 31) ^ this.minterm.GetHashCode();
	}
}
