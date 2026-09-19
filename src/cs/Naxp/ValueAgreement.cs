// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace LogMu;

/// <summary>
/// Whether two naxps give the same encoded value to every string they both accept.
/// </summary>
/// <remarks>
/// <para>
/// This is the question the encoding relation turns on, and it is not the question of whether
/// the two canonicalise alike. <c>(A|B)!A</c> and <c>(A|B)!B</c> print different things for
/// <c>B</c> and give it the same value, because each rank is taken in its own canonical
/// language. So canonical forms are never compared here. Each side's output is fed into its
/// own canonical machine as it is emitted, and what is carried is the difference of the two
/// running rank totals, as <see cref="RankAgreement"/> carries it over two machines.
/// </para>
/// <para>
/// A state is a pair of transduction residuals, one from each naxp, with the state each
/// canonical machine has reached on its side's output so far. Nothing is held back: a copied
/// character is fixed by narrowing the block to single characters before it is walked, a
/// rendering is a fixed string, and a skipped element's end of text output is a fixed string,
/// so every emission is concrete at the step that makes it. That is why there is no delay
/// here where <see cref="W3Checker"/> needs one. The square compares two strings that arrive
/// at different times; this compares two numbers, and a number can be accumulated as its
/// string arrives.
/// </para>
/// <para>
/// The difference at a state is a property of the state, not of the path: from a state every
/// completion both sides accept has one output per side, by W3, so the rank each side adds
/// over it is fixed, and two arrivals with different differences cannot both finish at zero.
/// A second and unequal arrival is therefore a disagreement rather than a branch, with one
/// proviso that <see cref="RankAgreement"/> also makes: it counts only where the two residuals
/// still share a completion. That proviso is exercised constantly rather than rarely. The
/// postcode against itself reaches the same state by different inputs through cross pairs of
/// parses, <c>\X?</c> taken against <c>\X?</c> skipped, whose residuals want a digit and a
/// letter next and so share nothing.
/// </para>
/// <para>
/// The argument, the pairs it was tried on and the construction it replaces are in
/// <c>encoding/comparison-square-review.md</c>, with a harness in
/// <c>encoding/comparison-check/</c>.
/// </para>
/// </remarks>
static class ValueAgreement
{
	/// <summary>
	/// How many product states the walk may build before giving up.
	/// </summary>
	/// <remarks>
	/// The same figure as <see cref="RankAgreement.MaxPairs"/>, for the same reason: a
	/// comparison worth making is between two naxps that resemble one another, and none of the
	/// pairs anybody has reason to compare has needed more than a few hundred.
	/// </remarks>
	public const int MaxTuples = 200_000;

	/// <summary>
	/// Whether <paramref name="a"/> and <paramref name="b"/> give the same value to every string
	/// both accept.
	/// </summary>
	/// <param name="a">The first naxp.</param>
	/// <param name="b">The second naxp.</param>
	/// <param name="witness">
	/// Where they differ, a string both accept and value differently, the shortest the walk
	/// found. Otherwise <see langword="null"/>.
	/// </param>
	/// <param name="maxTuples">How many product states to allow.</param>
	/// <returns>Whether they agree, differ, or could not be decided within the budget.</returns>
	public static Agreement Compare(Compilation a, Compilation b, out string? witness, int maxTuples = MaxTuples)
	{
		if (a is null) { throw new ArgumentNullException(nameof(a)); }
		if (b is null) { throw new ArgumentNullException(nameof(b)); }

		// Each side has its own factories. The sides are ordered and never compared for
		// identity, so there is nothing to gain from sharing and nothing to get wrong.
		var leftRxFactory = new RxFactory();
		var leftTxFactory = new TxFactory(leftRxFactory);
		Tx left = TxConverter.Convert(a.Ast, leftTxFactory, leftRxFactory);

		var rightRxFactory = new RxFactory();
		var rightTxFactory = new TxFactory(rightRxFactory);
		Tx right = TxConverter.Convert(b.Ast, rightTxFactory, rightRxFactory);

		return new Product(a, b, leftTxFactory, rightTxFactory, maxTuples).Run(left, right, out witness);
	}

	/// <summary>
	/// A product state: where each parse has got to, and where each canonical machine has got
	/// to on what that parse has emitted.
	/// </summary>
	readonly struct Key : IEquatable<Key>
	{
		public Key(Tx left, Tx right, State leftState, State rightState)
		{
			this.Left = left;
			this.Right = right;
			this.LeftState = leftState;
			this.RightState = rightState;
		}

		public Tx Left { get; }

		public Tx Right { get; }

		public State LeftState { get; }

		public State RightState { get; }

		// The two transductions come from different factories, so identity is by side and id
		// rather than by reference alone; the same holds of the two machines.
		public bool Equals(Key other)
			=> this.Left.Id == other.Left.Id
				&& this.Right.Id == other.Right.Id
				&& this.LeftState.Id == other.LeftState.Id
				&& this.RightState.Id == other.RightState.Id;

		public override bool Equals(object? obj) => obj is Key other && this.Equals(other);

		public override int GetHashCode()
			=> (((((this.Left.Id * 31) ^ this.Right.Id) * 31) ^ this.LeftState.Id) * 31) ^ this.RightState.Id;
	}

	/// <summary>
	/// Explores the product states reachable on a common input, reporting the first shared
	/// string the two value differently.
	/// </summary>
	sealed class Product
	{
		readonly Compilation a;
		readonly Compilation b;
		readonly TxFactory leftFactory;
		readonly TxFactory rightFactory;
		readonly int maxTuples;
		readonly Dictionary<Key, int> indexOf = new();
		readonly List<Key> keys = new();
		readonly List<BigInteger> differences = new();
		readonly List<int> parents = new();
		readonly List<char> arrivals = new();
		readonly List<(int existing, int parent, char arrival)> conflicts = new();
		readonly Dictionary<(int, int), string?> completions = new();
		readonly List<AsciiCharSet> blocks = new();
		readonly List<AsciiCharSet> firstSets = new();

		public Product(Compilation a, Compilation b, TxFactory leftFactory, TxFactory rightFactory, int maxTuples)
		{
			this.a = a;
			this.b = b;
			this.leftFactory = leftFactory;
			this.rightFactory = rightFactory;
			this.maxTuples = maxTuples;
		}

		public Agreement Run(Tx left, Tx right, out string? witness)
		{
			witness = null;

			var queue = new Queue<int>();
			queue.Enqueue(this.Add(new Key(left, right, this.a.Canonical.Start, this.b.Canonical.Start), BigInteger.Zero, -1, '\0'));

			while (queue.Count > 0)
			{
				int index = queue.Dequeue();

				Agreement ending = this.AtEndOfText(index);

				if (ending == Agreement.Differs)
				{
					witness = this.Path(index);
					return Agreement.Differs;
				}

				if (ending == Agreement.Undecided) { return Agreement.Undecided; }

				Key key = this.keys[index];
				this.firstSets.Clear();
				this.firstSets.AddRange(key.Left.GetFirstSets());
				this.firstSets.AddRange(key.Right.GetFirstSets());

				if (this.firstSets.Count == 0) { continue; }

				StateMapBuilder.Minterms(this.firstSets, this.blocks);

				// Copied so that narrowing, which re-enters the step, cannot disturb the list
				// being iterated.
				AsciiCharSet[] snapshot = this.blocks.ToArray();

				foreach (AsciiCharSet block in snapshot)
				{
					if (!this.TryStep(index, block, queue)) { return Agreement.Undecided; }
				}
			}

			// A differing arrival matters only where the two residuals still share a string, so
			// that the differing totals are ever compared. Where they do, the two paths through
			// the state finish at different differences, so at least one of them is a shared
			// string the two value differently; but only one need be, and which is not known
			// without the rest of the walk, so each is asked the direct way.
			foreach ((int existing, int parent, char arrival) in this.conflicts)
			{
				Key key = this.keys[existing];
				string? completion = this.Completion(key.Left, key.Right);

				if (completion is null) { continue; }

				string first = this.Path(existing) + completion;
				string second = this.Path(parent) + arrival + completion;

				if (second.Length < first.Length) { (first, second) = (second, first); }

				if (this.a.Encode(first) != this.b.Encode(first)) { witness = first; }
				else if (this.a.Encode(second) != this.b.Encode(second)) { witness = second; }
				else { throw new InvalidOperationException("Two arrivals differed and neither is a witness."); }

				return Agreement.Differs;
			}

			return Agreement.Agrees;
		}

		/// <summary>
		/// Whether both sides can end the input here with different values.
		/// </summary>
		Agreement AtEndOfText(int index)
		{
			Key key = this.keys[index];

			if (!key.Left.IsNullable || !key.Right.IsNullable) { return Agreement.Agrees; }

			Eot left = key.Left.GetEot();
			Eot right = key.Right.GetEot();

			if (left.Kind == EotKind.TooLong || right.Kind == EotKind.TooLong) { return Agreement.Undecided; }

			if (left.Kind != EotKind.Single || right.Kind != EotKind.Single)
			{
				throw new InvalidOperationException("A compiled naxp emits more than one thing at end of text.");
			}

			// Ending emits whatever the residual still owes, which the canonical machine has to
			// be walked over. The end of text transition itself contributes nothing on either
			// side, and the one that turns a total into a value cancels.
			BigInteger leftAdded = Walk(key.LeftState, left.Text!, out State leftEnd);
			BigInteger rightAdded = Walk(key.RightState, right.Text!, out State rightEnd);

			if (!leftEnd.AcceptsEndOfText || !rightEnd.AcceptsEndOfText)
			{
				throw new InvalidOperationException("A canonical form is not accepted by its own machine.");
			}

			return this.differences[index] + leftAdded - rightAdded == BigInteger.Zero ? Agreement.Agrees : Agreement.Differs;
		}

		/// <summary>
		/// Takes one step of the input, narrowing the block to single characters where either
		/// side would copy one.
		/// </summary>
		/// <remarks>
		/// A copied character has to be known before it can be walked through a canonical
		/// machine, since the rank it contributes depends on which character it is. That is the
		/// only reason to narrow. <see cref="W3Checker"/> can let two identical copies cancel
		/// because it compares them with each other; here they are walked through two different
		/// machines, so they cannot.
		/// </remarks>
		bool TryStep(int index, AsciiCharSet block, Queue<int> queue)
		{
			Key key = this.keys[index];
			TxDerivative left = this.leftFactory.Derivative(key.Left, block);
			TxDerivative right = this.rightFactory.Derivative(key.Right, block);

			if (left.TooLong || right.TooLong) { return false; }

			if (left.SkipsAmbiguously || right.SkipsAmbiguously)
			{
				throw new InvalidOperationException("A compiled naxp skips ambiguously.");
			}

			// One side cannot consume this block, so no shared string passes through it.
			if (left.Moves.Count == 0 || right.Moves.Count == 0) { return true; }

			if (block.SingleCharacter is null && (Copies(left) || Copies(right)))
			{
				foreach (char c in block)
				{
					if (!this.TryStep(index, AsciiCharSet.FromSingleChar(c), queue)) { return false; }
				}

				return true;
			}

			char arrival = block.SingleCharacter ?? block.CharacterAt(0);
			BigInteger difference = this.differences[index];

			foreach (TxMove leftMove in left.Moves)
			{
				foreach (TxMove rightMove in right.Moves)
				{
					BigInteger leftAdded = Walk(key.LeftState, leftMove.Emitted, out State leftState);
					BigInteger rightAdded = Walk(key.RightState, rightMove.Emitted, out State rightState);
					BigInteger next = difference + leftAdded - rightAdded;
					var nextKey = new Key(leftMove.Residual, rightMove.Residual, leftState, rightState);

					if (this.indexOf.TryGetValue(nextKey, out int existing))
					{
						if (this.differences[existing] != next) { this.conflicts.Add((existing, index, arrival)); }

						continue;
					}

					if (this.keys.Count >= this.maxTuples) { return false; }

					queue.Enqueue(this.Add(nextKey, next, index, arrival));
				}
			}

			return true;
		}

		static bool Copies(TxDerivative derivative)
		{
			foreach (TxMove move in derivative.Moves)
			{
				if (move.Emitted.IndexOf(Tx.CopyMarker) >= 0) { return true; }
			}

			return false;
		}

		int Add(Key key, BigInteger difference, int parent, char arrival)
		{
			int index = this.keys.Count;

			this.indexOf.Add(key, index);
			this.keys.Add(key);
			this.differences.Add(difference);
			this.parents.Add(parent);
			this.arrivals.Add(arrival);

			return index;
		}

		/// <summary>The input that reaches a state, read back along the path that found it.</summary>
		string Path(int index)
		{
			var builder = new StringBuilder();

			for (int at = index; this.parents[at] >= 0; at = this.parents[at])
			{
				builder.Insert(0, this.arrivals[at]);
			}

			return builder.ToString();
		}

		/// <summary>
		/// A string both residuals accept, or <see langword="null"/> where there is none.
		/// </summary>
		/// <remarks>
		/// Existence needs no narrowing: what a block emits varies by character, but whether it
		/// is consumed does not.
		/// </remarks>
		string? Completion(Tx left, Tx right)
		{
			var key = (left.Id, right.Id);

			if (this.completions.TryGetValue(key, out string? known)) { return known; }

			// Null while the answer is being worked out, so a pair cannot depend on itself.
			this.completions[key] = null;

			string? found = null;

			if (left.IsNullable && right.IsNullable)
			{
				found = string.Empty;
			}
			else
			{
				var sets = new List<AsciiCharSet>();
				sets.AddRange(left.GetFirstSets());
				sets.AddRange(right.GetFirstSets());

				var local = new List<AsciiCharSet>();
				StateMapBuilder.Minterms(sets, local);

				foreach (AsciiCharSet block in local)
				{
					TxDerivative leftDerivative = this.leftFactory.Derivative(left, block);
					TxDerivative rightDerivative = this.rightFactory.Derivative(right, block);

					if (leftDerivative.Moves.Count == 0 || rightDerivative.Moves.Count == 0) { continue; }

					foreach (TxMove leftMove in leftDerivative.Moves)
					{
						foreach (TxMove rightMove in rightDerivative.Moves)
						{
							string? rest = this.Completion(leftMove.Residual, rightMove.Residual);

							if (rest is not null)
							{
								found = block.CharacterAt(0) + rest;
								break;
							}
						}

						if (found is not null) { break; }
					}

					if (found is not null) { break; }
				}
			}

			this.completions[key] = found;

			return found;
		}

		/// <summary>
		/// Walks a concrete emission through a canonical machine, returning what it adds to the
		/// running total.
		/// </summary>
		static BigInteger Walk(State from, string emitted, out State to)
		{
			BigInteger added = BigInteger.Zero;
			State state = from;

			foreach (char c in emitted)
			{
				if (c == Tx.CopyMarker) { throw new InvalidOperationException("An undecided copy reached the walk."); }

				Transition? taken = null;

				foreach (Transition transition in state.Transitions)
				{
					if (!transition.Set.IsEmpty && transition.Set.Contains(c))
					{
						taken = transition;
						break;
					}
				}

				// A partial parse's output is a prefix of a canonical string, so it cannot fall
				// off the machine of the canonical language.
				if (taken is null) { throw new InvalidOperationException("A canonical form is not accepted by its own machine."); }

				added += RankAgreement.Contribution(state, taken.Value, c);
				state = taken.Value.Next;
			}

			to = state;

			return added;
		}
	}
}
