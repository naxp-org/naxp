// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LogMu;

/// <summary>
/// Decides W3: whether &#961; is single valued, so that every accepted string has exactly one
/// canonical form and therefore exactly one value.
/// </summary>
/// <remarks>
/// <para>
/// W3 is a property of <em>pairs</em> of parses, so this tracks pairs and never sets. The
/// obvious alternative, a subset construction over sets of live branches, is a determinisation:
/// it computes &#961; online, and for <c>[ab]{17}c|([ab]!a){17}d</c> that function provably needs
/// 2^17 states even though both of the naxp's machines have fewer than forty. Tracking pairs
/// decides the same naxp in a few dozen. The argument is in <c>encoding/w3-functionality.md</c>.
/// </para>
/// <para>
/// A state is two residuals and a delay. The delay is what one branch has emitted beyond the
/// other, so at most one side of it is non-empty; the moment both are, the two outputs differ at
/// a position neither can revisit and the delay collapses to <see cref="Delay.Mismatch"/>, after
/// which no output need be tracked at all.
/// </para>
/// <para>
/// The check is skipped outright for a naxp with no <c>!</c>, where &#961; is the identity. That
/// is the only short-circuit: the by-eye rule in the specification is sufficient rather than
/// necessary, and putting an unproved condition in front of the decision is the mistake that
/// <c>encoding/canonicity.md</c> records.
/// </para>
/// </remarks>
static class W3Checker
{
	/// <summary>
	/// Checks that replacement is single valued.
	/// </summary>
	/// <param name="ast">The tree, which must already have passed W1 and W2.</param>
	/// <param name="rxFactory">The factory the machines will be built with, reused for interning.</param>
	/// <param name="error">The refusal, or <see langword="null"/> if the naxp passes.</param>
	/// <param name="maxStates">The budget, lowered by tests so the cap can be reached cheaply.</param>
	/// <returns>Whether the naxp passes.</returns>
	public static bool TryCheck(Ast ast, RxFactory rxFactory, out NaxpError? error, int maxStates = NaxpLimits.MaxStates)
	{
		// Both arguments are checked before the tree is walked, so a bad call fails at once rather
		// than after the work.
		if (ast is null) { throw new ArgumentNullException(nameof(ast)); }
		if (rxFactory is null) { throw new ArgumentNullException(nameof(rxFactory)); }

		return TryCheck(ast, rxFactory, Ast.ContainsReplaceable(ast), out error, maxStates);
	}

	/// <summary>
	/// Checks that replacement is single valued, where the caller already knows whether there is
	/// anything to check.
	/// </summary>
	/// <param name="ast">The tree, which must already have passed W1 and W2.</param>
	/// <param name="rxFactory">The factory the machines will be built with, reused for interning.</param>
	/// <param name="hasReplaceable">
	/// <see cref="Ast.ContainsReplaceable"/> for <paramref name="ast"/>, so that a caller which
	/// needs the same fact does not walk the tree for it twice.
	/// </param>
	/// <param name="error">The refusal, or <see langword="null"/> if the naxp passes.</param>
	/// <param name="maxStates">The budget, lowered by tests so the cap can be reached cheaply.</param>
	/// <returns>Whether the naxp passes.</returns>
	public static bool TryCheck(Ast ast, RxFactory rxFactory, bool hasReplaceable, out NaxpError? error, int maxStates = NaxpLimits.MaxStates)
	{
		if (ast is null) { throw new ArgumentNullException(nameof(ast)); }
		if (rxFactory is null) { throw new ArgumentNullException(nameof(rxFactory)); }

		// Without a '!' the transduction is the identity, which is single valued for nothing.
		if (!hasReplaceable)
		{
			error = null;
			return true;
		}

		var factory = new TxFactory(rxFactory);
		Tx root = TxConverter.Convert(ast, factory, rxFactory);

		return TryCheck(root, factory, out error, maxStates);
	}

	/// <summary>
	/// Checks a transduction that has already been built.
	/// </summary>
	/// <remarks>
	/// <see cref="Compiler"/> needs the same transduction afterwards, to build the machine that
	/// canonicalises, so it converts once and passes it to both rather than paying for the
	/// derivatives twice.
	/// </remarks>
	/// <param name="root">The transduction.</param>
	/// <param name="txFactory">The factory that made it, whose derivative cache is reused.</param>
	/// <param name="error">The violation, or <see langword="null"/> where there is none.</param>
	/// <param name="maxStates">The budget, lowered by tests so the cap can be reached cheaply.</param>
	/// <returns>Whether replacement is single valued.</returns>
	public static bool TryCheck(Tx root, TxFactory txFactory, out NaxpError? error, int maxStates = NaxpLimits.MaxStates)
	{
		if (root is null) { throw new ArgumentNullException(nameof(root)); }
		if (txFactory is null) { throw new ArgumentNullException(nameof(txFactory)); }

		return new Square(txFactory, maxStates).TryRun(root, out error);
	}

	/// <summary>
	/// How far one branch's output runs ahead of the other's.
	/// </summary>
	/// <remarks>
	/// At most one side is non-empty, because a common prefix is committed at every step. Where
	/// both would be non-empty the outputs disagree at a position that is already fixed, so the
	/// delay collapses to the mismatch mark and the strings stop mattering.
	/// </remarks>
	readonly struct Delay : IEquatable<Delay>
	{
		Delay(string? left, string? right)
		{
			this.Left = left;
			this.Right = right;
		}

		/// <summary>What the first branch has emitted beyond the second. Null when mismatched.</summary>
		public string? Left { get; }

		/// <summary>What the second branch has emitted beyond the first. Null when mismatched.</summary>
		public string? Right { get; }

		/// <summary>The two outputs already differ and can never agree again.</summary>
		public bool IsMismatch => this.Left is null;

		public static Delay Mismatch { get; } = new(null, null);

		public static Delay None { get; } = new(string.Empty, string.Empty);

		/// <summary>
		/// The delay after both branches have emitted, with their common prefix committed.
		/// </summary>
		public static Delay After(Delay current, string leftEmitted, string rightEmitted)
		{
			if (current.IsMismatch) { return Mismatch; }

			string left = current.Left + leftEmitted;
			string right = current.Right + rightEmitted;

			int common = 0;
			while (common < left.Length && common < right.Length && left[common] == right[common]) { ++common; }

			// One of them is exhausted, or they differ here and will differ forever.
			if (common < left.Length && common < right.Length) { return Mismatch; }

			return new Delay(left.Substring(common), right.Substring(common));
		}

		/// <summary>Whether either side still holds a character the block has not decided.</summary>
		public bool HasUndecidedCopy
			=> !this.IsMismatch
				&& (this.Left!.IndexOf(Tx.CopyMarker) >= 0 || this.Right!.IndexOf(Tx.CopyMarker) >= 0);

		public Delay Swapped() => this.IsMismatch ? Mismatch : new Delay(this.Right, this.Left);

		public bool Equals(Delay other)
			=> string.Equals(this.Left, other.Left, StringComparison.Ordinal)
				&& string.Equals(this.Right, other.Right, StringComparison.Ordinal);

		public override bool Equals(object? obj) => obj is Delay other && this.Equals(other);

		public override int GetHashCode()
			=> this.IsMismatch
				? 0
				: (StringComparer.Ordinal.GetHashCode(this.Left!) * 31) ^ StringComparer.Ordinal.GetHashCode(this.Right!)
				;
	}

	/// <summary>
	/// A pair of live branches and the delay between them, as an unordered pair.
	/// </summary>
	readonly struct PairKey : IEquatable<PairKey>
	{
		public PairKey(Tx left, Tx right, Delay delay)
		{
			// The pair is unordered, so one orientation is chosen and the delay follows it.
			if (left.Id <= right.Id)
			{
				this.Left = left;
				this.Right = right;
				this.Delay = delay;
			}
			else
			{
				this.Left = right;
				this.Right = left;
				this.Delay = delay.Swapped();
			}
		}

		public Tx Left { get; }

		public Tx Right { get; }

		public Delay Delay { get; }

		public bool Equals(PairKey other)
			=> ReferenceEquals(this.Left, other.Left)
				&& ReferenceEquals(this.Right, other.Right)
				&& this.Delay.Equals(other.Delay);

		public override bool Equals(object? obj) => obj is PairKey other && this.Equals(other);

		public override int GetHashCode()
			=> ((this.Left.Id * 31) ^ this.Right.Id) * 31 ^ this.Delay.GetHashCode();
	}

	/// <summary>
	/// Explores the pairs reachable on a common input, reporting the first that can accept with
	/// two different outputs.
	/// </summary>
	sealed class Square
	{
		readonly TxFactory factory;
		readonly int maxStates;
		readonly Dictionary<PairKey, int> indexOf = new();
		readonly List<PairKey> states = new();
		readonly List<int> parents = new();
		readonly List<char> arrivals = new();

		public Square(TxFactory factory, int maxStates)
		{
			this.factory = factory;
			this.maxStates = maxStates;
		}

		public bool TryRun(Tx root, out NaxpError? error)
		{
			int start = this.Add(new PairKey(root, root, Delay.None), -1, '\0');
			var queue = new Queue<int>();
			queue.Enqueue(start);

			while (queue.Count > 0)
			{
				int index = queue.Dequeue();
				PairKey state = this.states[index];

				if (this.Accepts(state, out bool eotTooLong))
				{
					error = Violation(this.Witness(index));
					return false;
				}

				if (eotTooLong)
				{
					error = Abandoned();
					return false;
				}

				foreach (AsciiCharSet block in this.Blocks(state))
				{
					if (!this.TryStep(state, index, block, queue, out error)) { return false; }
				}
			}

			error = null;
			return true;
		}

		/// <summary>
		/// Takes one step of the input, narrowing the block to single characters where what is
		/// emitted would otherwise stay undecided.
		/// </summary>
		bool TryStep(PairKey state, int index, AsciiCharSet block, Queue<int> queue, out NaxpError? error)
		{
			TxDerivative left = this.factory.Derivative(state.Left, block);
			TxDerivative right = this.factory.Derivative(state.Right, block);

			if (left.TooLong || right.TooLong)
			{
				error = Abandoned();
				return false;
			}

			// Before the test for no moves, which an ambiguous skip can itself cause: the moves
			// past it are dropped because the verdict no longer depends on them.
			if (left.SkipsAmbiguously || right.SkipsAmbiguously)
			{
				error = Violation(this.Witness(index) + block.CharacterAt(0));
				return false;
			}

			if (left.Moves.Count == 0 || right.Moves.Count == 0)
			{
				// One side cannot consume this block, so there is no pair to follow. The other
				// side's own future is covered by its diagonal pair.
				error = null;
				return true;
			}

			if (this.NeedsNarrowing(state, left, right))
			{
				foreach (char c in block)
				{
					if (!this.TryStep(state, index, AsciiCharSet.FromSingleChar(c), queue, out error)) { return false; }
				}

				error = null;
				return true;
			}

			char arrival = block.SingleCharacter ?? block.CharacterAt(0);

			foreach (TxMove leftMove in left.Moves)
			{
				foreach (TxMove rightMove in right.Moves)
				{
					var next = new PairKey(
						leftMove.Residual,
						rightMove.Residual,
						Delay.After(state.Delay, leftMove.Emitted, rightMove.Emitted));

					if (this.indexOf.ContainsKey(next)) { continue; }

					if (this.states.Count >= this.maxStates)
					{
						error = TooLarge();
						return false;
					}

					queue.Enqueue(this.Add(next, index, arrival));
				}
			}

			error = null;
			return true;
		}

		/// <summary>
		/// Whether this step has to be retried one character at a time.
		/// </summary>
		/// <remarks>
		/// A character set emits the character read. Where the block holds more than one character
		/// that emission is not yet a known string, and comparing it against a rendering, or
		/// against a character copied at some other position, has no answer until the character is
		/// fixed. The one case that needs no narrowing is the common one: both branches emit the
		/// very same thing at the same step from equal delays, which cancels whatever the
		/// character turns out to be.
		/// </remarks>
		bool NeedsNarrowing(PairKey state, TxDerivative left, TxDerivative right)
		{
			bool noDelay = state.Delay.Equals(Delay.None);

			foreach (TxMove leftMove in left.Moves)
			{
				foreach (TxMove rightMove in right.Moves)
				{
					bool undecided = leftMove.Emitted.IndexOf(Tx.CopyMarker) >= 0
						|| rightMove.Emitted.IndexOf(Tx.CopyMarker) >= 0;

					if (!undecided) { continue; }

					// Identical emissions from one step cancel exactly, whatever was read, but only
					// where there is no earlier delay to shift one against the other.
					if (noDelay && string.Equals(leftMove.Emitted, rightMove.Emitted, StringComparison.Ordinal))
					{
						continue;
					}

					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Whether both branches can accept here, with different outputs.
		/// </summary>
		bool Accepts(PairKey state, out bool eotTooLong)
		{
			eotTooLong = false;

			if (!state.Left.IsNullable || !state.Right.IsNullable) { return false; }

			Eot left = state.Left.GetEot();
			Eot right = state.Right.GetEot();

			if (left.Kind == EotKind.TooLong || right.Kind == EotKind.TooLong)
			{
				eotTooLong = true;
				return false;
			}

			// One residual with two end of text outputs is a violation on its own, which is how a
			// naxp such as 'A!?|A!!' is caught before any character is read.
			if (left.Kind == EotKind.Multiple || right.Kind == EotKind.Multiple) { return true; }

			// Both can accept, and their outputs already differ.
			if (state.Delay.IsMismatch) { return true; }

			return !string.Equals(
				state.Delay.Left + left.Text,
				state.Delay.Right + right.Text,
				StringComparison.Ordinal);
		}

		/// <summary>
		/// The blocks to step by: the minterms of both branches' first sets, refined so that every
		/// character appearing in a rendering stands alone.
		/// </summary>
		IEnumerable<AsciiCharSet> Blocks(PairKey state)
		{
			var sets = new List<AsciiCharSet>();
			sets.AddRange(state.Left.GetFirstSets());
			sets.AddRange(state.Right.GetFirstSets());

			if (sets.Count == 0) { return Array.Empty<AsciiCharSet>(); }

			AsciiCharSet universe = AsciiCharSet.Empty;
			foreach (AsciiCharSet set in sets) { universe |= set; }

			foreach (char c in this.factory.RenderingCharacters)
			{
				if (universe.Contains(c)) { sets.Add(AsciiCharSet.FromSingleChar(c)); }
			}

			return StateMapBuilder.Minterms(sets);
		}

		int Add(PairKey key, int parent, char arrival)
		{
			int index = this.states.Count;

			this.indexOf.Add(key, index);
			this.states.Add(key);
			this.parents.Add(parent);
			this.arrivals.Add(arrival);

			return index;
		}

		/// <summary>The input that reaches a state, read back along the path that found it.</summary>
		string Witness(int index)
		{
			var builder = new StringBuilder();

			for (int at = index; this.parents[at] >= 0; at = this.parents[at])
			{
				builder.Insert(0, this.arrivals[at]);
			}

			return builder.ToString();
		}

		/// <summary>
		/// The decision was abandoned because an intermediate result grew too large, which is a
		/// different thing from running out of pair states and must not claim to be that.
		/// </summary>
		static NaxpError Abandoned()
			=> new NaxpError(NaxpMessage.NAXP1052_PairOutputAbandoned);

		NaxpError TooLarge()
			=> new NaxpError(NaxpMessage.NAXP1051_TooManyPairStates);

		static NaxpError Violation(string witness)
			=> new NaxpError(NaxpMessage.NAXP1046_ReplacementNotSingleValuedWitness, witness);
	}
}
