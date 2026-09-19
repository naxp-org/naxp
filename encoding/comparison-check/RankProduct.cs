using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using LogMu;

namespace Proto;

enum Verdict { Agrees, Differs, Undecided }

sealed class RankProductResult
{
	public Verdict Verdict;
	public int Tuples;
	public int Conflicts;
	public string? WitnessA;
	public string? WitnessB;
	public string? Note;
}

/// <summary>
/// Decides whether two naxps give every string they both accept the same encoded value, by a
/// product over pairs of transduction residuals that feeds each side's concrete output into its
/// own canonical machine as it is emitted and carries the difference of the two running totals.
/// There is no delay: nothing is ever held, because ranks are compared rather than strings.
/// </summary>
static class RankProduct
{
	readonly record struct Key(int Ta, int Tb, int Pa, int Qb);

	sealed class Node
	{
		public Node(Tx ta, Tx tb, State pa, State qb, BigInteger diff, int parent, char arrival)
		{
			this.Ta = ta; this.Tb = tb; this.Pa = pa; this.Qb = qb; this.Diff = diff; this.Parent = parent; this.Arrival = arrival;
		}

		public Tx Ta; public Tx Tb; public State Pa; public State Qb; public BigInteger Diff; public int Parent; public char Arrival;
	}

	public static RankProductResult Compare(Compilation a, Compilation b, int maxTuples = 200_000)
	{
		// Separate factories: the sides are ordered, so nothing needs to be shared.
		var rxA = new RxFactory(); var txA = new TxFactory(rxA); Tx rootA = TxConverter.Convert(a.Ast, txA, rxA);
		var rxB = new RxFactory(); var txB = new TxFactory(rxB); Tx rootB = TxConverter.Convert(b.Ast, txB, rxB);

		return new Walker(txA, txB, maxTuples).Run(rootA, rootB, a.Canonical.Start, b.Canonical.Start);
	}

	sealed class Walker
	{
		readonly TxFactory txA, txB;
		readonly int maxTuples;
		readonly List<Node> nodes = new();
		readonly Dictionary<Key, int> indexOf = new();
		readonly List<(int existing, int parent, char arrival)> conflicts = new();
		readonly Dictionary<(int, int), string?> completions = new();
		readonly List<AsciiCharSet> blocks = new();

		public Walker(TxFactory txA, TxFactory txB, int maxTuples)
		{
			this.txA = txA; this.txB = txB; this.maxTuples = maxTuples;
		}

		public RankProductResult Run(Tx rootA, Tx rootB, State pa, State qb)
		{
			var result = new RankProductResult();
			var queue = new Queue<int>();
			this.Add(new Node(rootA, rootB, pa, qb, BigInteger.Zero, -1, '\0'), queue);

			while (queue.Count > 0)
			{
				int index = queue.Dequeue();
				Node node = this.nodes[index];

				// Both can end here: a shared string, whose two values are decided by the totals
				// once each side's end of text output has been walked.
				if (node.Ta.IsNullable && node.Tb.IsNullable)
				{
					Eot ea = node.Ta.GetEot(), eb = node.Tb.GetEot();
					if (ea.Kind == EotKind.TooLong || eb.Kind == EotKind.TooLong) { result.Verdict = Verdict.Undecided; result.Note = "eot too long"; result.Tuples = this.nodes.Count; return result; }
					if (ea.Kind != EotKind.Single || eb.Kind != EotKind.Single) { throw new InvalidOperationException("A compiled naxp has a multiple end of text output."); }

					BigInteger ca = Walk(node.Pa, ea.Text!, out State endA);
					BigInteger cb = Walk(node.Qb, eb.Text!, out State endB);
					if (!endA.AcceptsEndOfText || !endB.AcceptsEndOfText) { throw new InvalidOperationException("A canonical form fell off its own machine."); }

					if (node.Diff + ca - cb != BigInteger.Zero)
					{
						result.Verdict = Verdict.Differs;
						result.WitnessA = this.Path(index);
						result.Tuples = this.nodes.Count;
						return result;
					}
				}

				var sets = new List<AsciiCharSet>();
				sets.AddRange(node.Ta.GetFirstSets());
				sets.AddRange(node.Tb.GetFirstSets());
				if (sets.Count == 0) { continue; }

				StateMapBuilder.Minterms(sets, this.blocks);
				AsciiCharSet[] snapshot = this.blocks.ToArray();

				foreach (AsciiCharSet block in snapshot)
				{
					if (!this.TryStep(index, block, queue, result)) { result.Tuples = this.nodes.Count; return result; }
				}
			}

			result.Tuples = this.nodes.Count;
			result.Conflicts = this.conflicts.Count;

			// A conflict matters only where the two residuals still share a completion.
			foreach ((int existing, int parent, char arrival) in this.conflicts)
			{
				Node node = this.nodes[existing];
				string? completion = this.Completion(node.Ta, node.Tb);
				if (completion is null) { continue; }

				result.Verdict = Verdict.Differs;
				result.WitnessA = this.Path(existing) + completion;
				result.WitnessB = this.Path(parent) + arrival + completion;
				return result;
			}

			result.Verdict = Verdict.Agrees;
			return result;
		}

		bool TryStep(int index, AsciiCharSet block, Queue<int> queue, RankProductResult result)
		{
			Node node = this.nodes[index];
			TxDerivative da = this.txA.Derivative(node.Ta, block);
			TxDerivative db = this.txB.Derivative(node.Tb, block);

			if (da.TooLong || db.TooLong) { result.Verdict = Verdict.Undecided; result.Note = "derivative too long"; return false; }
			if (da.SkipsAmbiguously || db.SkipsAmbiguously) { throw new InvalidOperationException("A compiled naxp skips ambiguously."); }
			if (da.Moves.Count == 0 || db.Moves.Count == 0) { return true; }

			// A copied character has to be known before it can be walked through a canonical
			// machine, since its rank depends on which character it is. Nothing else does.
			if (block.SingleCharacter is null && (Copies(da) || Copies(db)))
			{
				foreach (char c in block)
				{
					if (!this.TryStep(index, AsciiCharSet.FromSingleChar(c), queue, result)) { return false; }
				}

				return true;
			}

			char arrival = block.SingleCharacter ?? block.CharacterAt(0);

			foreach (TxMove ma in da.Moves)
			{
				foreach (TxMove mb in db.Moves)
				{
					BigInteger ca = Walk(node.Pa, ma.Emitted, out State pa);
					BigInteger cb = Walk(node.Qb, mb.Emitted, out State qb);
					BigInteger diff = node.Diff + ca - cb;
					var key = new Key(ma.Residual.Id, mb.Residual.Id, pa.Id, qb.Id);

					if (this.indexOf.TryGetValue(key, out int existing))
					{
						if (this.nodes[existing].Diff != diff) { this.conflicts.Add((existing, index, arrival)); }
						continue;
					}

					if (this.nodes.Count >= this.maxTuples) { result.Verdict = Verdict.Undecided; result.Note = "budget"; return false; }

					this.Add(new Node(ma.Residual, mb.Residual, pa, qb, diff, index, arrival), queue);
				}
			}

			return true;
		}

		static bool Copies(TxDerivative d)
		{
			foreach (TxMove m in d.Moves) { if (m.Emitted.IndexOf(Tx.CopyMarker) >= 0) { return true; } }
			return false;
		}

		void Add(Node node, Queue<int> queue)
		{
			int index = this.nodes.Count;
			this.nodes.Add(node);
			this.indexOf.Add(new Key(node.Ta.Id, node.Tb.Id, node.Pa.Id, node.Qb.Id), index);
			queue.Enqueue(index);
		}

		string Path(int index)
		{
			var builder = new StringBuilder();
			for (int at = index; this.nodes[at].Parent >= 0; at = this.nodes[at].Parent) { builder.Insert(0, this.nodes[at].Arrival); }
			return builder.ToString();
		}

		/// <summary>A string both residuals accept, or null where there is none.</summary>
		string? Completion(Tx ta, Tx tb)
		{
			var key = (ta.Id, tb.Id);
			if (this.completions.TryGetValue(key, out string? known)) { return known; }
			this.completions[key] = null;

			string? found = null;
			if (ta.IsNullable && tb.IsNullable) { found = string.Empty; }
			else
			{
				var sets = new List<AsciiCharSet>();
				sets.AddRange(ta.GetFirstSets());
				sets.AddRange(tb.GetFirstSets());
				var local = new List<AsciiCharSet>();
				StateMapBuilder.Minterms(sets, local);

				foreach (AsciiCharSet block in local)
				{
					TxDerivative da = this.txA.Derivative(ta, block);
					TxDerivative db = this.txB.Derivative(tb, block);
					if (da.Moves.Count == 0 || db.Moves.Count == 0) { continue; }

					foreach (TxMove ma in da.Moves)
					{
						foreach (TxMove mb in db.Moves)
						{
							string? sub = this.Completion(ma.Residual, mb.Residual);
							if (sub is not null) { found = block.CharacterAt(0) + sub; goto done; }
						}
					}
				}
			}

		done:
			this.completions[key] = found;
			return found;
		}

		/// <summary>Walks a concrete output through a canonical machine, returning what it adds to the rank.</summary>
		static BigInteger Walk(State from, string text, out State to)
		{
			BigInteger total = BigInteger.Zero;
			State state = from;

			foreach (char c in text)
			{
				if (c == Tx.CopyMarker) { throw new InvalidOperationException("An undecided copy reached the walk."); }

				Transition? taken = null;
				foreach (Transition t in state.Transitions) { if (!t.Set.IsEmpty && t.Set.Contains(c)) { taken = t; break; } }
				if (taken is null) { throw new InvalidOperationException($"Output '{text}' fell off the canonical machine at '{c}'."); }

				total += Contribution(state, taken.Value, c);
				state = taken.Value.Next;
			}

			to = state;
			return total;
		}

		// The same arithmetic as RankAgreement.Contribution and Codec.Encode.
		static BigInteger Contribution(State state, Transition taken, char c)
		{
			BigInteger skipped = BigInteger.Zero;

			foreach (Transition transition in state.Transitions)
			{
				ulong count = transition.Next.StringCount;
				if (transition.Set == taken.Set) { return skipped + (BigInteger)count * taken.Set.IndexOf(c); }
				skipped += (BigInteger)count * (transition.Set.IsEmpty ? 1UL : (ulong)transition.Set.Count);
			}

			return skipped;
		}
	}
}
