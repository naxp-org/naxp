// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LogMu;

/// <summary>
/// A transition of the canonicalisation machine.
/// </summary>
/// <remarks>
/// The sets of a state are disjoint, because they come from <see cref="StateMapBuilder.Minterms(System.Collections.Generic.IReadOnlyList{AsciiCharSet}, System.Collections.Generic.List{AsciiCharSet})"/>,
/// so a walk can stop at the first one that holds the character.
/// </remarks>
readonly struct TxTransition
{
	public TxTransition(AsciiCharSet set, string output, TxState next)
	{
		this.Set = set;
		this.Output = output;
		this.Next = next;
	}

	public AsciiCharSet Set { get; }

	/// <summary>
	/// What reading a character of <see cref="Set"/> emits. A <see cref="Tx.CopyMarker"/> in it
	/// stands for the character just read, which is what lets a whole set share one transition
	/// rather than needing one per character.
	/// </summary>
	public string Output { get; }

	public TxState Next { get; }
}

/// <summary>
/// A state of the canonicalisation machine.
/// </summary>
/// <remarks>
/// <para>
/// A state stands for a set of parses that agree on everything emitted so far, each carrying
/// whatever it has emitted beyond their common prefix. Two states are the same object when those
/// sets are equal.
/// </para>
/// <para>
/// That is sharing on the construction, not on behaviour, so it is weaker than what
/// <see cref="StateMap"/> gives. The acceptor is the minimal machine because it is acyclic and
/// hash-consed on what a state does; this one can hold two states that behave alike because
/// their branch sets differ. <c>A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)</c> builds eight states where
/// five would do.
/// </para>
/// </remarks>
sealed class TxState
{
	internal TxState(int id)
	{
		this.Id = id;
	}

	public int Id { get; }

	/// <summary>The transitions, sorted by the set order.</summary>
	/// <remarks>
	/// Filled after every state object exists, because a transition names its target and a target
	/// may have been discovered before the state that reaches it.
	/// </remarks>
	public TxTransition[] Transitions { get; internal set; } = Array.Empty<TxTransition>();

	/// <summary>Whether the input may end here.</summary>
	public bool AcceptsEndOfText => this.EndOutput is not null;

	/// <summary>
	/// What is emitted where the input ends here, or <see langword="null"/> where it may not.
	/// </summary>
	/// <remarks>
	/// This is never empty of meaning: a replaceable element that has consumed its subject emits
	/// its whole rendering at this point, so the machine can emit more after the last character
	/// than it did on any transition.
	/// </remarks>
	public string? EndOutput { get; internal set; }
}

/// <summary>
/// The canonicalisation &#961; as a machine, so that it can be walked rather than recursed over the
/// tree, and emitted as a table or a switch in another language.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Canonicaliser"/> does the same job over the <see cref="Ast"/>. That walk is the
/// reference, and this is the form the emitters need, because a tree walk has no table to emit.
/// It is also linear in the length of the input, which the tree walk is not.
/// </para>
/// <para>
/// The construction is the classical determinisation of a transducer: a state is a set of live
/// parses, each with the output it owes beyond what the others have already emitted, and a
/// transition emits the longest common prefix of what they all owe. That delay is needed because
/// a replaceable element emits nothing until it completes, so two branches can disagree about
/// what has been emitted for as long as the input has not yet told them apart.
/// </para>
/// <para>
/// It terminates by acyclicity. Every transition consumes a character and so strictly decreases
/// the longest string any live parse can still read, which bounds the depth; the delay is
/// bounded too, but that bounds nothing which would otherwise diverge.
/// </para>
/// <para>
/// The state count can be <b>exponential in the length of the naxp</b>, even where both language
/// machines are small. <c>[ab]{k}c|([ab]!a){k}d</c> builds exactly 2^(k+1) states, because
/// nothing before the final character says which branch was taken, so the machine has to
/// remember every character it has read in order to emit them later. That is not a weakness of
/// this construction: the lower bound in <c>encoding/w3-functionality.md</c> holds for any
/// finite-state machine that emits rho as it reads. Escaping it needs a different model, such as
/// buffering the input and copying spans from it.
/// </para>
/// <para>
/// The machine is built only where a naxp holds a replaceable element. Without one &#961; is the
/// identity, which <see cref="Compilation.CanonicalIsIdentity"/> already reports and which needs
/// no machine at all.
/// </para>
/// </remarks>
sealed class TxMachine
{
	internal TxMachine(TxState start, IReadOnlyList<TxState> states)
	{
		this.Start = start;
		this.States = states;
	}

	public TxState Start { get; }

	public IReadOnlyList<TxState> States { get; }

	/// <summary>
	/// The canonical form of a string, which is the string with each replaceable element replaced
	/// by its rendering.
	/// </summary>
	/// <param name="text">The string, which must be one the accepted language holds.</param>
	/// <param name="canonical">The canonical form, or <see langword="null"/> where the string is
	/// not accepted.</param>
	/// <returns>Whether the string is accepted.</returns>
	public bool TryCanonicalise(ReadOnlySpan<char> text, out string? canonical)
	{
		var builder = new StringBuilder();
		TxState state = this.Start;

		foreach (char c in text)
		{
			TxState? next = null;

			foreach (TxTransition transition in state.Transitions)
			{
				if (!transition.Set.Contains(c)) { continue; }

				AppendOutput(builder, transition.Output, c);
				next = transition.Next;
				break;
			}

			if (next is null)
			{
				canonical = null;
				return false;
			}

			state = next;
		}

		if (state.EndOutput is null)
		{
			canonical = null;
			return false;
		}

		builder.Append(state.EndOutput);
		canonical = builder.ToString();

		return true;
	}

	/// <summary>Appends a transition's output, resolving the copy marker to the character read.</summary>
	static void AppendOutput(StringBuilder builder, string output, char read)
	{
		foreach (char c in output)
		{
			builder.Append(c == Tx.CopyMarker ? read : c);
		}
	}
}

/// <summary>
/// Builds a <see cref="TxMachine"/> from a <see cref="Tx"/> by determinisation.
/// </summary>
/// <remarks>
/// <para>
/// The single-valuedness refusals here duplicate <see cref="W3Checker"/>, which decides the same
/// question over the same derivatives, so on an expression the checker has passed they are
/// unreachable. They are kept as defence in depth, because the two walk different shapes - the
/// checker walks pairs, this walks sets - and a machine built from an unchecked expression would
/// otherwise be silently wrong rather than refused.
/// </para>
/// <para>
/// The state cap is <b>not</b> a duplicate, and it is reachable on a naxp that is entirely legal.
/// <c>[ab]{16}c|([ab]!a){16}d</c> passes every rule, compiles, and then has no machine. A caller
/// has to decide what to do about that; see the remark on <see cref="TxMachine"/> for why the
/// size is intrinsic.
/// </para>
/// </remarks>
static class TxMachineBuilder
{
	/// <summary>
	/// Builds the machine for a transduction.
	/// </summary>
	/// <param name="root">The transduction.</param>
	/// <param name="factory">The factory that made it, whose derivative cache is reused.</param>
	/// <param name="machine">The machine, or <see langword="null"/> on failure.</param>
	/// <param name="error">The failure, or <see langword="null"/> on success.</param>
	/// <param name="maxStates">The budget, lowered by tests so the cap can be reached cheaply.</param>
	/// <returns>Whether the machine was built.</returns>
	public static bool TryBuild(
		Tx root,
		TxFactory factory,
		out TxMachine? machine,
		out NaxpError? error,
		int maxStates = NaxpLimits.MaxStates)
	{
		if (root is null) { throw new ArgumentNullException(nameof(root)); }
		if (factory is null) { throw new ArgumentNullException(nameof(factory)); }

		return new Builder(factory, maxStates).TryRun(root, out machine, out error);
	}

	/// <summary>One live parse: what is left to consume, and what it owes beyond the others.</summary>
	readonly struct Branch : IEquatable<Branch>
	{
		public Branch(Tx residual, string pending)
		{
			this.Residual = residual;
			this.Pending = pending;
		}

		public Tx Residual { get; }

		/// <summary>
		/// What this parse has emitted that the machine has not. Never holds a
		/// <see cref="Tx.CopyMarker"/>: the builder narrows a block to single characters rather
		/// than carry one past the step that read it, since nothing downstream could resolve it.
		/// </summary>
		public string Pending { get; }

		public bool Equals(Branch other)
			=> ReferenceEquals(this.Residual, other.Residual)
				&& string.Equals(this.Pending, other.Pending, StringComparison.Ordinal);

		public override bool Equals(object? obj) => obj is Branch other && this.Equals(other);

		public override int GetHashCode()
			=> (this.Residual.Id * 31) ^ StringComparer.Ordinal.GetHashCode(this.Pending);
	}

	/// <summary>A set of live parses, which is what a state of the machine is.</summary>
	readonly struct BranchSetKey : IEquatable<BranchSetKey>
	{
		readonly Branch[] branches;
		readonly int hash;

		/// <summary>Takes ownership of the array, which must already be sorted and deduplicated.</summary>
		public BranchSetKey(Branch[] branches)
		{
			this.branches = branches;

			int accumulated = branches.Length;
			foreach (Branch branch in branches)
			{
				accumulated = (accumulated * 31) + branch.GetHashCode();
			}

			this.hash = accumulated;
		}

		public Branch[] Branches => this.branches;

		public bool Equals(BranchSetKey other)
		{
			if (this.branches.Length != other.branches.Length) { return false; }

			for (int i = 0; i < this.branches.Length; ++i)
			{
				if (!this.branches[i].Equals(other.branches[i])) { return false; }
			}

			return true;
		}

		public override bool Equals(object? obj) => obj is BranchSetKey other && this.Equals(other);

		public override int GetHashCode() => this.hash;
	}

	/// <summary>A transition recorded before its target state object exists.</summary>
	readonly struct PendingTransition
	{
		public PendingTransition(AsciiCharSet set, string output, int next)
		{
			this.Set = set;
			this.Output = output;
			this.Next = next;
		}

		public AsciiCharSet Set { get; }

		public string Output { get; }

		public int Next { get; }
	}

	sealed class Builder
	{
		readonly TxFactory factory;
		readonly int maxStates;
		readonly Dictionary<BranchSetKey, int> indexOf = new();
		readonly List<BranchSetKey> keys = new();
		readonly List<List<PendingTransition>> transitionsOf = new();
		readonly List<string?> endOutputOf = new();
		readonly List<AsciiCharSet> blocks = new();
		readonly List<AsciiCharSet> firstSets = new();

		public Builder(TxFactory factory, int maxStates)
		{
			this.factory = factory;
			this.maxStates = maxStates;
		}

		public bool TryRun(Tx root, out TxMachine? machine, out NaxpError? error)
		{
			machine = null;

			var queue = new Queue<int>();

			if (!this.TryAdd(new BranchSetKey(new[] { new Branch(root, string.Empty) }), queue, out int start, out error))
			{
				return false;
			}

			while (queue.Count > 0)
			{
				int index = queue.Dequeue();

				if (!this.TrySetEndOutput(index, out error)) { return false; }
				if (!this.TryExplore(index, queue, out error)) { return false; }
			}

			machine = TxMachineMerger.Merge(this.Materialise(start));
			error = null;

			return true;
		}

		/// <summary>
		/// Records what the state emits where the input ends, refusing where the parses disagree.
		/// </summary>
		bool TrySetEndOutput(int index, out NaxpError? error)
		{
			string? endOutput = null;

			foreach (Branch branch in this.keys[index].Branches)
			{
				Eot eot = branch.Residual.GetEot();

				switch (eot.Kind)
				{
					case EotKind.None:
						continue;

					case EotKind.TooLong:
						error = TooLarge(this.maxStates);
						return false;

					case EotKind.Multiple:
						error = Violation();
						return false;

					case EotKind.Single:
					{
						string candidate = branch.Pending + eot.Text;

						if (endOutput is null)
						{
							endOutput = candidate;
						}
						else if (!string.Equals(endOutput, candidate, StringComparison.Ordinal))
						{
							error = Violation();
							return false;
						}

						break;
					}

					default:
						throw new InvalidOperationException($"Unhandled kind {eot.Kind}.");
				}
			}

			this.endOutputOf[index] = endOutput;
			error = null;

			return true;
		}

		/// <summary>Follows every block of characters the state can read.</summary>
		bool TryExplore(int index, Queue<int> queue, out NaxpError? error)
		{
			this.firstSets.Clear();

			foreach (Branch branch in this.keys[index].Branches)
			{
				this.firstSets.AddRange(branch.Residual.GetFirstSets());
			}

			if (this.firstSets.Count == 0)
			{
				error = null;
				return true;
			}

			StateMapBuilder.Minterms(this.firstSets, this.blocks);

			// Copied because narrowing a block re-enters the step, and the shared list must not be
			// the thing being iterated.
			AsciiCharSet[] snapshot = this.blocks.ToArray();

			foreach (AsciiCharSet block in snapshot)
			{
				if (!this.TryStep(index, block, queue, out error)) { return false; }
			}

			error = null;

			return true;
		}

		/// <summary>
		/// Takes one step, narrowing the block to single characters where what is emitted would
		/// otherwise stay undecided past this step.
		/// </summary>
		bool TryStep(int index, AsciiCharSet block, Queue<int> queue, out NaxpError? error)
		{
			var pendingOf = new Dictionary<Tx, string>();

			foreach (Branch branch in this.keys[index].Branches)
			{
				TxDerivative derivative = this.factory.Derivative(branch.Residual, block);

				if (derivative.TooLong)
				{
					error = TooLarge(this.maxStates);
					return false;
				}

				if (derivative.SkipsAmbiguously)
				{
					error = Violation();
					return false;
				}

				foreach (TxMove move in derivative.Moves)
				{
					string pending = branch.Pending + move.Emitted;

					if (!pendingOf.TryGetValue(move.Residual, out string? existing))
					{
						pendingOf.Add(move.Residual, pending);
					}
					else if (!string.Equals(existing, pending, StringComparison.Ordinal))
					{
						// Same continuation, two outputs. Every string the continuation accepts
						// would have two canonical forms.
						error = Violation();
						return false;
					}
				}
			}

			if (pendingOf.Count == 0)
			{
				error = null;
				return true;
			}

			string common = LongestCommonPrefix(pendingOf.Values);

			if (CarriesUndecidedCopy(pendingOf.Values, common.Length))
			{
				if (block.SingleCharacter is not null)
				{
					// A single character block decides every copy, so this cannot recur.
					throw new InvalidOperationException("A copy stayed undecided on a single character.");
				}

				foreach (char c in block)
				{
					if (!this.TryStep(index, AsciiCharSet.FromSingleChar(c), queue, out error)) { return false; }
				}

				error = null;

				return true;
			}

			var branches = new Branch[pendingOf.Count];
			int at = 0;

			foreach (KeyValuePair<Tx, string> entry in pendingOf)
			{
				branches[at++] = new Branch(entry.Key, entry.Value.Substring(common.Length));
			}

			Array.Sort(
				branches,
				static (left, right) =>
				{
					int byResidual = left.Residual.Id.CompareTo(right.Residual.Id);
					return byResidual != 0
						? byResidual
						: string.CompareOrdinal(left.Pending, right.Pending);
				});

			if (!this.TryAdd(new BranchSetKey(branches), queue, out int next, out error)) { return false; }

			this.transitionsOf[index].Add(new PendingTransition(block, common, next));
			error = null;

			return true;
		}

		/// <summary>Finds a state, adding it and queueing it where it is new.</summary>
		bool TryAdd(BranchSetKey key, Queue<int> queue, out int index, out NaxpError? error)
		{
			if (this.indexOf.TryGetValue(key, out index))
			{
				error = null;
				return true;
			}

			if (this.keys.Count >= this.maxStates)
			{
				index = -1;
				error = TooLarge(this.maxStates);
				return false;
			}

			index = this.keys.Count;

			this.keys.Add(key);
			this.transitionsOf.Add(new List<PendingTransition>());
			this.endOutputOf.Add(null);
			this.indexOf.Add(key, index);

			queue.Enqueue(index);
			error = null;

			return true;
		}

		/// <summary>Turns the recorded indices into linked state objects.</summary>
		TxMachine Materialise(int start)
		{
			var states = new TxState[this.keys.Count];

			for (int i = 0; i < states.Length; ++i)
			{
				states[i] = new TxState(i) { EndOutput = this.endOutputOf[i] };
			}

			for (int i = 0; i < states.Length; ++i)
			{
				List<PendingTransition> pending = this.transitionsOf[i];
				var transitions = new TxTransition[pending.Count];

				for (int t = 0; t < pending.Count; ++t)
				{
					transitions[t] = new TxTransition(pending[t].Set, pending[t].Output, states[pending[t].Next]);
				}

				Array.Sort(transitions, static (left, right) => left.Set.CompareTo(right.Set));

				states[i].Transitions = transitions;
			}

			return new TxMachine(states[start], states);
		}

		static string LongestCommonPrefix(Dictionary<Tx, string>.ValueCollection pendings)
		{
			string? shortest = null;

			foreach (string pending in pendings)
			{
				if (shortest is null || pending.Length < shortest.Length) { shortest = pending; }
			}

			int common = shortest!.Length;

			foreach (string pending in pendings)
			{
				int at = 0;
				while (at < common && pending[at] == shortest[at]) { ++at; }
				common = at;

				if (common == 0) { break; }
			}

			return shortest.Substring(0, common);
		}

		/// <summary>
		/// Whether any parse would carry a copy marker past this step, where nothing could later
		/// say which character it stood for.
		/// </summary>
		static bool CarriesUndecidedCopy(Dictionary<Tx, string>.ValueCollection pendings, int from)
		{
			foreach (string pending in pendings)
			{
				if (pending.IndexOf(Tx.CopyMarker, from) >= 0) { return true; }
			}

			return false;
		}

		static NaxpError Violation()
			=> new NaxpError(NaxpMessage.NAXP1045_ReplacementNotSingleValued);

		static NaxpError TooLarge(int maxStates)
			=> new NaxpError(NaxpMessage.NAXP1050_TooManyCanonicalStates);
	}
}

/// <summary>
/// Merges states of a built <see cref="TxMachine"/> that behave alike.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TxMachineBuilder"/> shares a state only where two branch sets are equal, which is a
/// property of the construction rather than of behaviour, so it can leave two states that do the
/// same thing. <c>A(Q|q)!QX(B|C)|B(Q|q)!Q(XB|XC)</c> is the smallest witness found: eight states
/// built, five after this pass.
/// </para>
/// <para>
/// The machine is acyclic, so a post-order walk reaches every successor before the state that
/// reaches it, and one bottom-up sweep suffices. A state is keyed on what it emits at end of text
/// and on its transitions once their targets have been replaced by the representatives already
/// chosen, which is the usual hash-consing. Merging targets can leave two transitions agreeing on
/// output and target, and those are unioned, which is safe because their sets were disjoint.
/// </para>
/// <para>
/// This makes the machine smaller. It does not make it canonical the way <see cref="StateMap"/>
/// is: that would need an onward normalisation of where output is emitted, which nothing
/// downstream asks for.
/// </para>
/// </remarks>
static class TxMachineMerger
{
	public static TxMachine Merge(TxMachine machine)
	{
		List<TxState> order = PostOrder(machine.Start);

		var representative = new Dictionary<TxState, TxState>();
		var canonical = new Dictionary<MergedKey, TxState>();
		var merged = new List<TxState>();
		var rebuilt = new List<TxTransition>();

		foreach (TxState state in order)
		{
			rebuilt.Clear();

			foreach (TxTransition transition in state.Transitions)
			{
				TxState target = representative[transition.Next];
				int at = IndexOfMergeable(rebuilt, target, transition.Output);

				if (at >= 0)
				{
					rebuilt[at] = new TxTransition(rebuilt[at].Set | transition.Set, transition.Output, target);
				}
				else
				{
					rebuilt.Add(new TxTransition(transition.Set, transition.Output, target));
				}
			}

			rebuilt.Sort(static (left, right) => left.Set.CompareTo(right.Set));

			var key = new MergedKey(state.EndOutput, rebuilt);

			if (canonical.TryGetValue(key, out TxState? existing))
			{
				representative[state] = existing;
				continue;
			}

			var created = new TxState(merged.Count)
			{
				EndOutput = state.EndOutput,
				Transitions = rebuilt.ToArray(),
			};

			merged.Add(created);
			canonical.Add(key, created);
			representative[state] = created;
		}

		return new TxMachine(representative[machine.Start], merged);
	}

	/// <summary>
	/// Post-order, so that every successor is ordered before the state that reaches it.
	/// </summary>
	/// <remarks>
	/// Iterative rather than recursive. A naxp is allowed to be a long chain - <c>(\A!A){99}</c>
	/// is legal, linear and ten thousand states - and recursing over that overflows the stack,
	/// which cannot be caught. <see cref="StateMapBuilder"/> avoids the same trap by ordering its
	/// states rather than recursing.
	/// </remarks>
	static List<TxState> PostOrder(TxState start)
	{
		var order = new List<TxState>();
		var seen = new HashSet<TxState>();
		var pending = new Stack<Step>();

		seen.Add(start);
		pending.Push(new Step(start, 0));

		while (pending.Count > 0)
		{
			Step step = pending.Pop();

			if (step.Index == step.State.Transitions.Length)
			{
				// Every successor has been finished, so this state may be finished too.
				order.Add(step.State);
				continue;
			}

			pending.Push(new Step(step.State, step.Index + 1));

			TxState next = step.State.Transitions[step.Index].Next;

			if (seen.Add(next)) { pending.Push(new Step(next, 0)); }
		}

		return order;
	}

	/// <summary>How far through one state's transitions the walk has got.</summary>
	readonly struct Step
	{
		public Step(TxState state, int index)
		{
			this.State = state;
			this.Index = index;
		}

		public TxState State { get; }

		public int Index { get; }
	}

	static int IndexOfMergeable(List<TxTransition> rebuilt, TxState target, string output)
	{
		for (int i = 0; i < rebuilt.Count; ++i)
		{
			if (ReferenceEquals(rebuilt[i].Next, target)
				&& string.Equals(rebuilt[i].Output, output, StringComparison.Ordinal))
			{
				return i;
			}
		}

		return -1;
	}

	/// <summary>What makes two states the same once their successors have been merged.</summary>
	readonly struct MergedKey : IEquatable<MergedKey>
	{
		readonly string? endOutput;
		readonly TxTransition[] transitions;
		readonly int hash;

		public MergedKey(string? endOutput, List<TxTransition> transitions)
		{
			this.endOutput = endOutput;
			this.transitions = transitions.ToArray();

			int accumulated = endOutput is null ? 0 : StringComparer.Ordinal.GetHashCode(endOutput);

			foreach (TxTransition transition in this.transitions)
			{
				accumulated = (accumulated * 31) + transition.Set.GetHashCode();
				accumulated = (accumulated * 31) + StringComparer.Ordinal.GetHashCode(transition.Output);
				accumulated = (accumulated * 31) + transition.Next.Id;
			}

			this.hash = accumulated;
		}

		public bool Equals(MergedKey other)
		{
			if (!string.Equals(this.endOutput, other.endOutput, StringComparison.Ordinal)
				|| this.transitions.Length != other.transitions.Length)
			{
				return false;
			}

			for (int i = 0; i < this.transitions.Length; ++i)
			{
				if (!this.transitions[i].Set.Equals(other.transitions[i].Set)
					|| !string.Equals(this.transitions[i].Output, other.transitions[i].Output, StringComparison.Ordinal)
					|| !ReferenceEquals(this.transitions[i].Next, other.transitions[i].Next))
				{
					return false;
				}
			}

			return true;
		}

		public override bool Equals(object? obj) => obj is MergedKey other && this.Equals(other);

		public override int GetHashCode() => this.hash;
	}
}
