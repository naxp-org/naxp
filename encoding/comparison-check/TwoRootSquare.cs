using System;
using System.Collections.Generic;
using System.Text;
using LogMu;

namespace Proto;

/// <summary>
/// W3Checker's Square, copied verbatim except that it is seeded with two roots built through
/// one shared factory, as the brief proposes. Reports the outcome and how many pairs it built.
/// </summary>
static class TwoRootSquare
{
	public static string Run(Compilation a, Compilation b, int maxStates, out int states)
	{
		var rx = new RxFactory();
		var tx = new TxFactory(rx);
		Tx rootA = TxConverter.Convert(a.Ast, tx, rx);
		Tx rootB = TxConverter.Convert(b.Ast, tx, rx);

		var square = new Square(tx, maxStates);
		string outcome = square.Run(rootA, rootB);
		states = square.StateCount;
		return outcome;
	}

	readonly struct Delay : IEquatable<Delay>
	{
		Delay(string? left, string? right) { this.Left = left; this.Right = right; }
		public string? Left { get; }
		public string? Right { get; }
		public bool IsMismatch => this.Left is null;
		public static Delay Mismatch { get; } = new(null, null);
		public static Delay None { get; } = new(string.Empty, string.Empty);

		public static Delay After(Delay current, string leftEmitted, string rightEmitted)
		{
			if (current.IsMismatch) { return Mismatch; }
			string left = current.Left + leftEmitted;
			string right = current.Right + rightEmitted;
			int common = 0;
			while (common < left.Length && common < right.Length && left[common] == right[common]) { ++common; }
			if (common < left.Length && common < right.Length) { return Mismatch; }
			return new Delay(left.Substring(common), right.Substring(common));
		}

		public Delay Swapped() => this.IsMismatch ? Mismatch : new Delay(this.Right, this.Left);
		public bool Equals(Delay other) => string.Equals(this.Left, other.Left, StringComparison.Ordinal) && string.Equals(this.Right, other.Right, StringComparison.Ordinal);
		public override bool Equals(object? obj) => obj is Delay other && this.Equals(other);
		public override int GetHashCode() => this.IsMismatch ? 0 : (StringComparer.Ordinal.GetHashCode(this.Left!) * 31) ^ StringComparer.Ordinal.GetHashCode(this.Right!);
	}

	readonly struct PairKey : IEquatable<PairKey>
	{
		public PairKey(Tx left, Tx right, Delay delay)
		{
			if (left.Id <= right.Id) { this.Left = left; this.Right = right; this.Delay = delay; }
			else { this.Left = right; this.Right = left; this.Delay = delay.Swapped(); }
		}

		public Tx Left { get; }
		public Tx Right { get; }
		public Delay Delay { get; }
		public bool Equals(PairKey other) => ReferenceEquals(this.Left, other.Left) && ReferenceEquals(this.Right, other.Right) && this.Delay.Equals(other.Delay);
		public override bool Equals(object? obj) => obj is PairKey other && this.Equals(other);
		public override int GetHashCode() => ((this.Left.Id * 31) ^ this.Right.Id) * 31 ^ this.Delay.GetHashCode();
	}

	sealed class Square
	{
		readonly TxFactory factory;
		readonly int maxStates;
		readonly Dictionary<PairKey, int> indexOf = new();
		readonly List<PairKey> states = new();
		readonly List<int> parents = new();
		readonly List<char> arrivals = new();

		public Square(TxFactory factory, int maxStates) { this.factory = factory; this.maxStates = maxStates; }

		public int StateCount => this.states.Count;

		public string Run(Tx left, Tx right)
		{
			int start = this.Add(new PairKey(left, right, Delay.None), -1, '\0');
			var queue = new Queue<int>();
			queue.Enqueue(start);

			while (queue.Count > 0)
			{
				int index = queue.Dequeue();
				PairKey state = this.states[index];

				if (this.Accepts(state, out bool eotTooLong)) { return $"rho differs, witness '{this.Witness(index)}'"; }
				if (eotTooLong) { return "abandoned"; }

				foreach (AsciiCharSet block in this.Blocks(state))
				{
					string? stop = this.Step(state, index, block, queue);
					if (stop is not null) { return stop; }
				}
			}

			return "rho agrees";
		}

		string? Step(PairKey state, int index, AsciiCharSet block, Queue<int> queue)
		{
			TxDerivative left = this.factory.Derivative(state.Left, block);
			TxDerivative right = this.factory.Derivative(state.Right, block);

			if (left.TooLong || right.TooLong) { return "abandoned"; }
			if (left.SkipsAmbiguously || right.SkipsAmbiguously) { throw new InvalidOperationException("ambiguous skip on a compiled naxp"); }
			if (left.Moves.Count == 0 || right.Moves.Count == 0) { return null; }

			if (this.NeedsNarrowing(state, left, right))
			{
				foreach (char c in block)
				{
					string? stop = this.Step(state, index, AsciiCharSet.FromSingleChar(c), queue);
					if (stop is not null) { return stop; }
				}

				return null;
			}

			char arrival = block.SingleCharacter ?? block.CharacterAt(0);

			foreach (TxMove leftMove in left.Moves)
			{
				foreach (TxMove rightMove in right.Moves)
				{
					var next = new PairKey(leftMove.Residual, rightMove.Residual, Delay.After(state.Delay, leftMove.Emitted, rightMove.Emitted));
					if (this.indexOf.ContainsKey(next)) { continue; }
					if (this.states.Count >= this.maxStates) { return "too many pair states"; }
					queue.Enqueue(this.Add(next, index, arrival));
				}
			}

			return null;
		}

		bool NeedsNarrowing(PairKey state, TxDerivative left, TxDerivative right)
		{
			bool noDelay = state.Delay.Equals(Delay.None);

			foreach (TxMove leftMove in left.Moves)
			{
				foreach (TxMove rightMove in right.Moves)
				{
					bool undecided = leftMove.Emitted.IndexOf(Tx.CopyMarker) >= 0 || rightMove.Emitted.IndexOf(Tx.CopyMarker) >= 0;
					if (!undecided) { continue; }
					if (noDelay && string.Equals(leftMove.Emitted, rightMove.Emitted, StringComparison.Ordinal)) { continue; }
					return true;
				}
			}

			return false;
		}

		bool Accepts(PairKey state, out bool eotTooLong)
		{
			eotTooLong = false;
			if (!state.Left.IsNullable || !state.Right.IsNullable) { return false; }

			Eot left = state.Left.GetEot();
			Eot right = state.Right.GetEot();

			if (left.Kind == EotKind.TooLong || right.Kind == EotKind.TooLong) { eotTooLong = true; return false; }
			if (left.Kind == EotKind.Multiple || right.Kind == EotKind.Multiple) { throw new InvalidOperationException("multiple eot on a compiled naxp"); }
			if (state.Delay.IsMismatch) { return true; }

			return !string.Equals(state.Delay.Left + left.Text, state.Delay.Right + right.Text, StringComparison.Ordinal);
		}

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

			var blocks = new List<AsciiCharSet>();
			StateMapBuilder.Minterms(sets, blocks);
			return blocks;
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

		string Witness(int index)
		{
			var builder = new StringBuilder();
			for (int at = index; this.parents[at] >= 0; at = this.parents[at]) { builder.Insert(0, this.arrivals[at]); }
			return builder.ToString();
		}
	}
}
