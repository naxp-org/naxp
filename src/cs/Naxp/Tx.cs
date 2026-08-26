// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Text;

namespace LogMu;

/// <summary>
/// What a <see cref="Tx"/> node is.
/// </summary>
enum TxKind
{
	/// <summary>The empty relation, which arises only as a derivative.</summary>
	EmptySet,
	/// <summary>Reads the empty string and emits nothing.</summary>
	Epsilon,
	/// <summary>Reads one character of a set and emits that same character.</summary>
	Chars,
	/// <summary>Reads any string of a subject and emits a fixed rendering when it completes.</summary>
	Repl,
	/// <summary>Two or more in sequence.</summary>
	Concat,
	/// <summary>Two or more in alternation.</summary>
	Union,
	/// <summary>One repeated between <see cref="Tx.MinCount"/> and <see cref="Tx.MaxCount"/> times.</summary>
	Interval,
}

/// <summary>
/// Whether an expression has exactly one way of emitting at end of text.
/// </summary>
enum EotKind
{
	/// <summary>There is no &#949;-parse, so the expression cannot accept end of text.</summary>
	None,
	/// <summary>Every &#949;-parse emits the same string.</summary>
	Single,
	/// <summary>Two &#949;-parses emit different strings, which is a W3 violation wherever it is reached.</summary>
	Multiple,
	/// <summary>Deciding would build a string longer than this implementation will materialise.</summary>
	TooLong,
}

/// <summary>
/// What an expression emits when it accepts the empty string.
/// </summary>
readonly struct Eot
{
	Eot(EotKind kind, string? text)
	{
		this.Kind = kind;
		this.Text = text;
	}

	public EotKind Kind { get; }

	/// <summary>The emitted string, for <see cref="EotKind.Single"/> only.</summary>
	public string? Text { get; }

	public static Eot None { get; } = new(EotKind.None, null);

	public static Eot Multiple { get; } = new(EotKind.Multiple, null);

	public static Eot TooLong { get; } = new(EotKind.TooLong, null);

	public static Eot Empty { get; } = new(EotKind.Single, string.Empty);

	public static Eot Single(string text)
		=> text.Length > Matcher.MaxGeneratedLength ? TooLong : new Eot(EotKind.Single, text);

	/// <summary>
	/// The end of text behaviour of two expressions in sequence, which is the product of theirs.
	/// </summary>
	public static Eot Concat(Eot left, Eot right)
	{
		if (left.Kind == EotKind.None || right.Kind == EotKind.None) { return None; }
		if (left.Kind == EotKind.TooLong || right.Kind == EotKind.TooLong) { return TooLong; }
		if (left.Kind == EotKind.Multiple || right.Kind == EotKind.Multiple) { return Multiple; }

		return Single(left.Text + right.Text);
	}

	/// <summary>
	/// The end of text behaviour of two expressions in alternation, which is the union of theirs.
	/// </summary>
	public static Eot Union(Eot left, Eot right)
	{
		if (left.Kind == EotKind.None) { return right; }
		if (right.Kind == EotKind.None) { return left; }
		if (left.Kind == EotKind.TooLong || right.Kind == EotKind.TooLong) { return TooLong; }
		if (left.Kind == EotKind.Multiple || right.Kind == EotKind.Multiple) { return Multiple; }

		return string.Equals(left.Text, right.Text, StringComparison.Ordinal) ? left : Multiple;
	}
}

/// <summary>
/// One way of consuming a block of characters: what was emitted, and what is left to do.
/// </summary>
readonly struct TxMove
{
	public TxMove(string emitted, Tx residual)
	{
		this.Emitted = emitted;
		this.Residual = residual;
	}

	/// <summary>
	/// What this step emits. A copied character appears as <see cref="Tx.CopyMarker"/> where the
	/// block holds more than one character, since which character was read is not yet decided.
	/// </summary>
	public string Emitted { get; }

	public Tx Residual { get; }
}

/// <summary>
/// The result of differentiating, cached whole because the ambiguity flag belongs to the step
/// rather than to any one move.
/// </summary>
sealed class TxDerivative
{
	internal TxDerivative(IReadOnlyList<TxMove> moves, bool skipsAmbiguously, bool tooLong)
	{
		this.Moves = moves;
		this.SkipsAmbiguously = skipsAmbiguously;
		this.TooLong = tooLong;
	}

	public IReadOnlyList<TxMove> Moves { get; }

	/// <summary>
	/// Whether a nullable element was skipped over that emits two different strings at end of
	/// text, with a live continuation beyond it.
	/// </summary>
	/// <remarks>
	/// That is a W3 violation on its own. The two skips give one input two outputs, and the
	/// continuation is non-empty because empty residuals are dropped, so both parses reach an
	/// accepting string.
	/// </remarks>
	public bool SkipsAmbiguously { get; }

	/// <summary>Whether the step was abandoned as too large to compute.</summary>
	public bool TooLong { get; }
}

/// <summary>
/// The transduction &#961; as an expression, so that derivatives of it can be taken.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="RxConverter"/> throws away. There a replaceable element becomes
/// either its subject or its rendering, depending on which language is being built, and W3 is
/// exactly the question of how the two behave together. <see cref="TxKind.Repl"/> is the node
/// that keeps them paired.
/// </para>
/// <para>
/// Emission is deferred to the end of the element. A replaceable consumes its subject one
/// character at a time emitting nothing, then emits the whole rendering when it completes, which
/// is why the difference between two branches' outputs has to be carried as a delay rather than
/// compared character by character.
/// </para>
/// <para>
/// Nodes are interned by their factory, so reference equality is structural equality and a node
/// can be a dictionary key.
/// </para>
/// </remarks>
sealed class Tx
{
	/// <summary>
	/// Stands for a copied character whose identity the block has not yet fixed.
	/// </summary>
	/// <remarks>
	/// Outside ASCII, so it cannot collide with anything a naxp emits. Where one of these
	/// survives into a delay the comparison it takes part in is undecided, and
	/// <see cref="W3Checker"/> retries that step one character at a time.
	/// </remarks>
	public const char CopyMarker = '￿';

	AsciiCharSet[]? firstSets;
	Eot? eot;

	internal Tx(int id, TxKind kind, AsciiCharSet charSet, Rx? subject, string? rendering, Tx[] children, int minCount, int maxCount, bool isNullable)
	{
		this.Id = id;
		this.Kind = kind;
		this.CharSet = charSet;
		this.Subject = subject;
		this.Rendering = rendering;
		this.Children = children;
		this.MinCount = minCount;
		this.MaxCount = maxCount;
		this.IsNullable = isNullable;
	}

	/// <summary>A number unique within the factory that made this node.</summary>
	public int Id { get; }

	public TxKind Kind { get; }

	/// <summary>The characters, for <see cref="TxKind.Chars"/>.</summary>
	public AsciiCharSet CharSet { get; }

	/// <summary>What is consumed, for <see cref="TxKind.Repl"/>.</summary>
	public Rx? Subject { get; }

	/// <summary>What is emitted, for <see cref="TxKind.Repl"/>. One string, by W1.</summary>
	public string? Rendering { get; }

	public Tx[] Children { get; }

	public int MinCount { get; }

	public int MaxCount { get; }

	/// <summary>Whether the empty string can be consumed. This is about input alone.</summary>
	public bool IsNullable { get; }

	/// <summary>What is emitted where the empty string is consumed.</summary>
	public Eot GetEot()
	{
		if (this.eot is not null) { return this.eot.Value; }

		Eot computed = this.ComputeEot();
		this.eot = computed;

		return computed;
	}

	/// <summary>
	/// The character sets that can match the first character consumed.
	/// </summary>
	public AsciiCharSet[] GetFirstSets()
	{
		if (this.firstSets is not null) { return this.firstSets; }

		var sets = new List<AsciiCharSet>();
		this.CollectFirstSets(sets);

		return this.firstSets = sets.ToArray();
	}

	Eot ComputeEot()
	{
		switch (this.Kind)
		{
			case TxKind.EmptySet:
			case TxKind.Chars:
				return Eot.None;

			case TxKind.Epsilon:
				return Eot.Empty;

			case TxKind.Repl:
				// Completing a replaceable emits its rendering even though nothing was consumed.
				return this.Subject!.IsNullable ? Eot.Single(this.Rendering!) : Eot.None;

			case TxKind.Concat:
			{
				Eot result = Eot.Empty;
				foreach (Tx child in this.Children) { result = Eot.Concat(result, child.GetEot()); }

				return result;
			}

			case TxKind.Union:
			{
				Eot result = Eot.None;
				foreach (Tx child in this.Children) { result = Eot.Union(result, child.GetEot()); }

				return result;
			}

			case TxKind.Interval:
			{
				Tx child = this.Children[0];

				// A count of zero denotes the empty string whatever the child would emit.
				if (!child.IsNullable) { return this.MinCount == 0 ? Eot.Empty : Eot.None; }

				Eot inner = child.GetEot();
				if (inner.Kind != EotKind.Single) { return inner; }

				// Repeating something that emits nothing emits nothing however often it happens.
				if (inner.Text!.Length == 0) { return Eot.Empty; }

				// Otherwise every count between the two bounds consumes nothing and emits a
				// different length, so a free count is more than one output. A fixed count is one
				// output, which is why '(A!!){2}' is well formed and '(A!!){0,2}' is not.
				if (this.MinCount != this.MaxCount) { return Eot.Multiple; }

				if ((long)inner.Text.Length * this.MinCount > Matcher.MaxGeneratedLength) { return Eot.TooLong; }

				var builder = new StringBuilder(inner.Text.Length * this.MinCount);
				for (int i = 0; i < this.MinCount; ++i) { builder.Append(inner.Text); }

				return Eot.Single(builder.ToString());
			}

			default:
				throw new InvalidOperationException($"Unhandled kind {this.Kind}.");
		}
	}

	void CollectFirstSets(List<AsciiCharSet> sets)
	{
		switch (this.Kind)
		{
			case TxKind.EmptySet:
			case TxKind.Epsilon:
				return;

			case TxKind.Chars:
				sets.Add(this.CharSet);
				return;

			case TxKind.Repl:
				sets.AddRange(this.Subject!.GetFirstSets());
				return;

			case TxKind.Concat:
				foreach (Tx child in this.Children)
				{
					child.CollectFirstSets(sets);
					if (!child.IsNullable) { return; }
				}

				return;

			case TxKind.Union:
				foreach (Tx child in this.Children) { child.CollectFirstSets(sets); }
				return;

			case TxKind.Interval:
				this.Children[0].CollectFirstSets(sets);
				return;

			default:
				throw new InvalidOperationException($"Unhandled kind {this.Kind}.");
		}
	}
}

/// <summary>
/// Makes <see cref="Tx"/> nodes, normalising and interning as it goes, and differentiates them.
/// </summary>
sealed class TxFactory
{
	readonly RxFactory rxFactory;
	readonly Dictionary<TxKey, Tx> interned = new();
	readonly Dictionary<DerivativeKey, TxDerivative> derivatives = new();
	readonly List<char> renderingCharacters = new();
	int nextId;

	public TxFactory(RxFactory rxFactory)
	{
		this.rxFactory = rxFactory;
		this.EmptySet = this.Intern(TxKind.EmptySet, AsciiCharSet.Empty, null, null, Array.Empty<Tx>(), 0, 0, false);
		this.Epsilon = this.Intern(TxKind.Epsilon, AsciiCharSet.Empty, null, null, Array.Empty<Tx>(), 0, 0, true);
	}

	public Tx EmptySet { get; }

	public Tx Epsilon { get; }

	/// <summary>How many distinct expressions this factory has made.</summary>
	public int Count => this.interned.Count;

	/// <summary>
	/// Every character that appears in some rendering.
	/// </summary>
	/// <remarks>
	/// Splitting these out as singleton blocks is what makes emission uniform over a block. A
	/// character set emits the character read and a replaceable emits a fixed string, so whether
	/// the two agree depends on which character of the block was read: in <c>[ab]|[ab]!a</c> they
	/// agree on <c>a</c> and disagree on <c>b</c>. Refining costs transitions, never states, and
	/// cannot change what is accepted, since the input side is already uniform over the coarser
	/// blocks.
	/// </remarks>
	public IReadOnlyList<char> RenderingCharacters => this.renderingCharacters;

	public Tx Chars(AsciiCharSet set)
		=> set.IsEmpty
			? this.EmptySet
			: this.Intern(TxKind.Chars, set, null, null, Array.Empty<Tx>(), 0, 0, false)
			;

	/// <summary>
	/// A replaceable element: consume any string of <paramref name="subject"/>, emit
	/// <paramref name="rendering"/>.
	/// </summary>
	public Tx Repl(Rx subject, string rendering)
	{
		if (subject.Kind == RxKind.EmptySet) { return this.EmptySet; }

		foreach (char c in rendering)
		{
			if (!this.renderingCharacters.Contains(c)) { this.renderingCharacters.Add(c); }
		}

		return this.Intern(TxKind.Repl, AsciiCharSet.Empty, subject, rendering, Array.Empty<Tx>(), 0, 0, subject.IsNullable);
	}

	public Tx Concat(IReadOnlyList<Tx> parts)
	{
		var flattened = new List<Tx>();

		foreach (Tx part in parts)
		{
			if (part.Kind == TxKind.EmptySet) { return this.EmptySet; }

			// An epsilon emits nothing, so dropping it changes neither input nor output.
			if (part.Kind == TxKind.Epsilon) { continue; }

			if (part.Kind == TxKind.Concat) { flattened.AddRange(part.Children); }
			else { flattened.Add(part); }
		}

		if (flattened.Count == 0) { return this.Epsilon; }
		if (flattened.Count == 1) { return flattened[0]; }

		bool isNullable = true;
		foreach (Tx part in flattened) { isNullable &= part.IsNullable; }

		return this.Intern(TxKind.Concat, AsciiCharSet.Empty, null, null, flattened.ToArray(), 0, 0, isNullable);
	}

	public Tx Concat(Tx first, Tx second) => this.Concat(new[] { first, second });

	/// <remarks>
	/// Duplicates are removed by identity, which is safe: two identical alternatives are one
	/// parse repeated, not two parses, so removing one removes no output.
	/// </remarks>
	public Tx Union(IReadOnlyList<Tx> alternatives)
	{
		var flattened = new List<Tx>();

		foreach (Tx alternative in alternatives)
		{
			if (alternative.Kind == TxKind.EmptySet) { continue; }

			if (alternative.Kind == TxKind.Union) { flattened.AddRange(alternative.Children); }
			else { flattened.Add(alternative); }
		}

		flattened.Sort(static (left, right) => left.Id.CompareTo(right.Id));

		var distinct = new List<Tx>(flattened.Count);
		foreach (Tx alternative in flattened)
		{
			if (distinct.Count == 0 || !ReferenceEquals(distinct[distinct.Count - 1], alternative))
			{
				distinct.Add(alternative);
			}
		}

		if (distinct.Count == 0) { return this.EmptySet; }
		if (distinct.Count == 1) { return distinct[0]; }

		bool isNullable = false;
		foreach (Tx alternative in distinct) { isNullable |= alternative.IsNullable; }

		return this.Intern(TxKind.Union, AsciiCharSet.Empty, null, null, distinct.ToArray(), 0, 0, isNullable);
	}

	public Tx Union(Tx first, Tx second) => this.Union(new[] { first, second });

	public Tx Interval(Tx child, int minCount, int maxCount)
	{
		if (maxCount == 0) { return this.Epsilon; }
		if (child.Kind == TxKind.EmptySet) { return minCount == 0 ? this.Epsilon : this.EmptySet; }

		// Unlike Rx, an epsilon child is not dropped here unless it emits nothing: a replaceable
		// with a nullable subject consumes nothing and still emits, and how often that happens is
		// what makes '(A!!){0,3}' ambiguous.
		if (child.Kind == TxKind.Epsilon) { return this.Epsilon; }

		// Rx drives the minimum to zero where the child is nullable, because for input alone
		// x{2} and x{0,2} then accept the same language. That is not available here: the count
		// decides how many renderings are emitted, and '(A!!){2}' emits 'AA' where '(A!!){0,2}'
		// emits one of three strings.
		if (minCount == 1 && maxCount == 1) { return child; }

		return this.Intern(TxKind.Interval, AsciiCharSet.Empty, null, null, new[] { child }, minCount, maxCount, minCount == 0 || child.IsNullable);
	}

	/// <summary>
	/// Every way of consuming one character of <paramref name="block"/>.
	/// </summary>
	/// <param name="expression">The expression to differentiate.</param>
	/// <param name="block">
	/// A block of characters that behave alike on the input side, and on the output side too once
	/// <see cref="RenderingCharacters"/> have been split out.
	/// </param>
	/// <returns>The moves, with the emitted string of each.</returns>
	public TxDerivative Derivative(Tx expression, AsciiCharSet block)
	{
		var key = new DerivativeKey(expression.Id, block);
		if (this.derivatives.TryGetValue(key, out TxDerivative? cached)) { return cached; }

		TxDerivative result = this.ComputeDerivative(expression, block);
		this.derivatives.Add(key, result);

		return result;
	}

	TxDerivative ComputeDerivative(Tx expression, AsciiCharSet block)
	{
		switch (expression.Kind)
		{
			case TxKind.EmptySet:
			case TxKind.Epsilon:
				return Nothing;

			case TxKind.Chars:
			{
				if (!block.IntersectsWith(expression.CharSet)) { return Nothing; }

				// A block of one character is already concrete; a wider one is not, and what it
				// emits stays undecided until the checker narrows it.
				char? single = block.SingleCharacter;
				string emitted = (single ?? Tx.CopyMarker).ToString();

				return new TxDerivative(new[] { new TxMove(emitted, this.Epsilon) }, false, false);
			}

			case TxKind.Repl:
			{
				Rx residual = this.rxFactory.Derivative(expression.Subject!, block);
				if (residual.Kind == RxKind.EmptySet) { return Nothing; }

				// Nothing is emitted while the subject is being consumed.
				return new TxDerivative(
					new[] { new TxMove(string.Empty, this.Repl(residual, expression.Rendering!)) },
					false,
					false);
			}

			case TxKind.Union:
			{
				var moves = new List<TxMove>();
				bool skipsAmbiguously = false;
				bool tooLong = false;

				foreach (Tx child in expression.Children)
				{
					TxDerivative sub = this.Derivative(child, block);
					moves.AddRange(sub.Moves);
					skipsAmbiguously |= sub.SkipsAmbiguously;
					tooLong |= sub.TooLong;
				}

				return new TxDerivative(moves, skipsAmbiguously, tooLong);
			}

			case TxKind.Concat:
			{
				var moves = new List<TxMove>();
				bool skipsAmbiguously = false;
				bool tooLong = false;

				// What the elements skipped over so far emit. Skipping a nullable element means
				// choosing one of its end of text parses, and that choice can emit.
				Eot skipped = Eot.Empty;

				for (int i = 0; i < expression.Children.Length; ++i)
				{
					TxDerivative sub = this.Derivative(expression.Children[i], block);
					skipsAmbiguously |= sub.SkipsAmbiguously;
					tooLong |= sub.TooLong;

					if (sub.Moves.Count > 0)
					{
						switch (skipped.Kind)
						{
							case EotKind.Multiple:
								// Two ways of skipping emit differently and both continue, so one
								// input has two outputs. There is nothing left to decide.
								skipsAmbiguously = true;
								break;

							case EotKind.TooLong:
								tooLong = true;
								break;

							default:
								foreach (TxMove move in sub.Moves)
								{
									var rest = new List<Tx>(expression.Children.Length - i) { move.Residual };
									for (int j = i + 1; j < expression.Children.Length; ++j) { rest.Add(expression.Children[j]); }

									moves.Add(new TxMove(skipped.Text + move.Emitted, this.Concat(rest)));
								}

								break;
						}
					}

					// Only an element that can consume nothing lets a later one take the character.
					if (!expression.Children[i].IsNullable) { break; }

					skipped = Eot.Concat(skipped, expression.Children[i].GetEot());
				}

				return new TxDerivative(moves, skipsAmbiguously, tooLong);
			}

			case TxKind.Interval:
			{
				Tx child = expression.Children[0];
				TxDerivative sub = this.Derivative(child, block);
				if (sub.Moves.Count == 0) { return Nothing; }

				bool skipsAmbiguously = sub.SkipsAmbiguously;
				bool tooLong = sub.TooLong;

				// Copies before the one that consumes may be skipped, and a skipped copy emits
				// what its child emits at end of text.
				Eot inner = child.IsNullable ? child.GetEot() : Eot.None;
				int skips = 0;

				if (child.IsNullable && expression.MaxCount >= 2)
				{
					if (inner.Kind == EotKind.Multiple)
					{
						// Two ways of skipping one copy emit differently and leave the same work
						// behind them, so the totals differ whatever follows.
						skipsAmbiguously = true;
					}
					else if (inner.Kind == EotKind.TooLong)
					{
						tooLong = true;
					}
					else if (inner.Text!.Length > 0)
					{
						// Skipping emits, so each count is a separate parse and has to be followed.
						// What it leaves behind shrinks as more are skipped, and that can pay the
						// difference back: '(A!!){2}' emits 'AA' by either route.
						skips = expression.MaxCount - 1;
					}
				}

				// Where a skipped copy emits nothing the parses differ only in a residual that the
				// unskipped one already covers, so one move stands for all of them.
				if (skips > MaxSkippedCopies)
				{
					return new TxDerivative(Array.Empty<TxMove>(), skipsAmbiguously, true);
				}

				var moves = new List<TxMove>(sub.Moves.Count * (skips + 1));
				var emittedBySkips = new StringBuilder();

				for (int skipped = 0; skipped <= skips; ++skipped)
				{
					int used = skipped + 1;
					Tx rest = this.Interval(
						child,
						expression.MinCount <= used ? 0 : expression.MinCount - used,
						expression.MaxCount - used);

					string prefix = emittedBySkips.ToString();

					foreach (TxMove move in sub.Moves)
					{
						moves.Add(new TxMove(prefix + move.Emitted, this.Concat(move.Residual, rest)));
					}

					if (inner.Kind == EotKind.Single) { emittedBySkips.Append(inner.Text); }
				}

				return new TxDerivative(moves, skipsAmbiguously, tooLong);
			}

			default:
				throw new InvalidOperationException($"Unhandled kind {expression.Kind}.");
		}
	}

	static readonly TxDerivative Nothing = new(Array.Empty<TxMove>(), false, false);

	/// <summary>
	/// The most skipped copies of an interval this implementation will follow separately.
	/// </summary>
	/// <remarks>
	/// Only reached where skipping a copy emits, which needs a replaceable element with a nullable
	/// subject inside an interval whose count can vary. Nothing a naxp is for goes near it, and a
	/// naxp that does is refused as an implementation limit rather than judged.
	/// </remarks>
	const int MaxSkippedCopies = 64;

	Tx Intern(TxKind kind, AsciiCharSet charSet, Rx? subject, string? rendering, Tx[] children, int minCount, int maxCount, bool isNullable)
	{
		var key = new TxKey(kind, charSet, subject, rendering, children, minCount, maxCount);

		if (this.interned.TryGetValue(key, out Tx? existing)) { return existing; }

		var created = new Tx(this.nextId++, kind, charSet, subject, rendering, children, minCount, maxCount, isNullable);
		this.interned.Add(key, created);

		return created;
	}

	readonly struct TxKey : IEquatable<TxKey>
	{
		readonly TxKind kind;
		readonly AsciiCharSet charSet;
		readonly Rx? subject;
		readonly string? rendering;
		readonly Tx[] children;
		readonly int minCount;
		readonly int maxCount;
		readonly int hash;

		public TxKey(TxKind kind, AsciiCharSet charSet, Rx? subject, string? rendering, Tx[] children, int minCount, int maxCount)
		{
			this.kind = kind;
			this.charSet = charSet;
			this.subject = subject;
			this.rendering = rendering;
			this.children = children;
			this.minCount = minCount;
			this.maxCount = maxCount;

			int accumulated = (int)kind;
			accumulated = (accumulated * 31) + charSet.GetHashCode();
			accumulated = (accumulated * 31) + (subject?.Id ?? 0);
			accumulated = (accumulated * 31) + (rendering is null ? 0 : StringComparer.Ordinal.GetHashCode(rendering));
			accumulated = (accumulated * 31) + minCount;
			accumulated = (accumulated * 31) + maxCount;
			foreach (Tx child in children) { accumulated = (accumulated * 31) + child.Id; }

			this.hash = accumulated;
		}

		public bool Equals(TxKey other)
		{
			if (this.kind != other.kind
				|| this.minCount != other.minCount
				|| this.maxCount != other.maxCount
				|| !this.charSet.Equals(other.charSet)
				|| !ReferenceEquals(this.subject, other.subject)
				|| !string.Equals(this.rendering, other.rendering, StringComparison.Ordinal)
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

		public override bool Equals(object? obj) => obj is TxKey other && this.Equals(other);

		public override int GetHashCode() => this.hash;
	}

	readonly struct DerivativeKey : IEquatable<DerivativeKey>
	{
		readonly int expressionId;
		readonly AsciiCharSet block;

		public DerivativeKey(int expressionId, AsciiCharSet block)
		{
			this.expressionId = expressionId;
			this.block = block;
		}

		public bool Equals(DerivativeKey other)
			=> this.expressionId == other.expressionId && this.block.Equals(other.block);

		public override bool Equals(object? obj) => obj is DerivativeKey other && this.Equals(other);

		public override int GetHashCode() => (this.expressionId * 31) ^ this.block.GetHashCode();
	}
}

/// <summary>
/// Turns a parsed naxp into the transducer algebra.
/// </summary>
static class TxConverter
{
	public static Tx Convert(Ast node, TxFactory factory, RxFactory rxFactory)
	{
		switch (node)
		{
			case AstEmpty:
				return factory.Epsilon;

			case AstChars chars:
				return factory.Chars(chars.CharSet);

			case AstDigitsRange:
				// A digits range emits what it consumed, so its expansion needs no output of its
				// own and the one RxConverter already knows how to build can be lifted.
				return Lift(RxConverter.Convert(node, rxFactory, NaxpLanguage.Accepted), factory);

			case AstSequence sequence:
			{
				var parts = new List<Tx>(sequence.Children.Count);
				foreach (Ast child in sequence.Children) { parts.Add(Convert(child, factory, rxFactory)); }

				return factory.Concat(parts);
			}

			case AstAlternation alternation:
			{
				var alternatives = new List<Tx>(alternation.Children.Count);
				foreach (Ast child in alternation.Children) { alternatives.Add(Convert(child, factory, rxFactory)); }

				return factory.Union(alternatives);
			}

			case AstOptional optional:
				return factory.Union(factory.Epsilon, Convert(optional.Child, factory, rxFactory));

			case AstInterval interval:
				return factory.Interval(Convert(interval.Child, factory, rxFactory), interval.MinCount, interval.MaxCount);

			case AstReplaceable replaceable:
			{
				// W1 has already established that the rendering generates exactly one string.
				if (Matcher.TryGetSingleString(replaceable.Rendering, out string? rendering) != SingleStringOutcome.Single)
				{
					throw new InvalidOperationException("A replaceable element passed W1 but has no single rendering.");
				}

				return factory.Repl(RxConverter.Convert(replaceable.Subject, rxFactory, NaxpLanguage.Accepted), rendering!);
			}

			default:
				throw new InvalidOperationException($"Unhandled node type {node.GetType().Name}.");
		}
	}

	/// <summary>
	/// Reads an expression with no replaceable elements as a transduction, which copies.
	/// </summary>
	static Tx Lift(Rx expression, TxFactory factory)
	{
		switch (expression.Kind)
		{
			case RxKind.EmptySet:
				return factory.EmptySet;

			case RxKind.Epsilon:
				return factory.Epsilon;

			case RxKind.Chars:
				return factory.Chars(expression.CharSet);

			case RxKind.Concat:
			{
				var parts = new List<Tx>(expression.Children.Length);
				foreach (Rx child in expression.Children) { parts.Add(Lift(child, factory)); }

				return factory.Concat(parts);
			}

			case RxKind.Union:
			{
				var alternatives = new List<Tx>(expression.Children.Length);
				foreach (Rx child in expression.Children) { alternatives.Add(Lift(child, factory)); }

				return factory.Union(alternatives);
			}

			case RxKind.Interval:
				return factory.Interval(Lift(expression.Children[0], factory), expression.MinCount, expression.MaxCount);

			default:
				throw new InvalidOperationException($"Unhandled kind {expression.Kind}.");
		}
	}
}
