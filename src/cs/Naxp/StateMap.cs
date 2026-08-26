// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LogMu;

/// <summary>
/// A character set paired with the state reached by consuming one of its characters.
/// </summary>
/// <remarks>
/// An empty set is the end of text transition. The empty set is least in the set order, so it
/// sorts first and needs no special handling.
/// </remarks>
readonly struct Transition
{
	public Transition(AsciiCharSet set, State next)
	{
		this.Set = set;
		this.Next = next;
	}

	public AsciiCharSet Set { get; }

	public State Next { get; }
}

/// <summary>
/// A state of the machine, which stands for a language.
/// </summary>
/// <remarks>
/// States are shared: two languages that are equal give the same object. That is what makes the
/// machine the minimal one and the encoding a property of the language rather than of the
/// spelling.
/// </remarks>
sealed class State
{
	internal State(int id, Transition[] transitions, ulong valueCount)
	{
		this.Id = id;
		this.Transitions = transitions;
		this.ValueCount = valueCount;
	}

	public int Id { get; }

	/// <summary>The transitions, sorted by the set order, end of text first where present.</summary>
	public Transition[] Transitions { get; }

	/// <summary>
	/// The count of strings the state's language holds, saturated at 2^64 - 1.
	/// </summary>
	public ulong ValueCount { get; }

	/// <summary>Whether this is the terminal state, whose language is the empty string alone.</summary>
	public bool IsTerminal => this.Transitions.Length == 0;

	/// <summary>Whether the language holds the empty string.</summary>
	public bool AcceptsEndOfText
		=> this.IsTerminal || this.Transitions[0].Set.IsEmpty;
}

/// <summary>
/// The machine for one of a naxp's languages.
/// </summary>
sealed class StateMap
{
	internal StateMap(State start, IReadOnlyList<State> states, bool countSaturated)
	{
		this.Start = start;
		this.States = states;
		this.CountSaturated = countSaturated;
	}

	public State Start { get; }

	public IReadOnlyList<State> States { get; }

	/// <summary>The size of the language, saturated at 2^64 - 1.</summary>
	public ulong ValueCount => this.Start.ValueCount;

	/// <summary>
	/// Whether the true count exceeds 2^64 - 1, in which case <see cref="ValueCount"/> is that
	/// limit rather than the count.
	/// </summary>
	public bool CountSaturated { get; }

	/// <summary>
	/// Whether this machine's language holds the specified string.
	/// </summary>
	/// <remarks>
	/// One transition per character and no allocation. A string longer than any the machine
	/// generates runs out of transitions and is refused, so no length guard is needed.
	/// </remarks>
	/// <param name="text">The string to test.</param>
	/// <returns>Whether the language holds it.</returns>
	public bool Accepts(ReadOnlySpan<char> text)
	{
		State state = this.Start;

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
}

/// <summary>
/// Builds the machine the specification defines, by symbolic derivatives.
/// </summary>
/// <remarks>
/// <para>
/// The specification defines a state as a language, with one transition per first class. The
/// minterms of the first sets refine those classes rather than equalling them, so the classes
/// are recovered afterwards by merging transitions that reach the same state. Where
/// <c>[AB]C|[BC]C</c> gives minterms <c>[A]</c>, <c>[B]</c> and <c>[C]</c>, all three have the
/// derivative <c>C</c>, and the merge recombines them into the single class <c>[ABC]</c>.
/// </para>
/// <para>
/// Nothing here recurses over the machine. States are built in order of the longest string
/// remaining, which strictly decreases along every derivative, so each state's successors are
/// already built when it is reached. A long chain of states would otherwise want nine
/// thousand stack frames.
/// </para>
/// </remarks>
sealed class StateMapBuilder
{
	readonly RxFactory factory;
	readonly int maxStates;
	readonly Dictionary<StateKey, State> interned = new();
	readonly List<State> states = new();

	// Working space for one state at a time, cleared at the top of each state rather than
	// allocated per state. None of it outlives the loop it is used in, which holds because a
	// builder serves one TryBuild and nothing in those loops re-enters it.
	readonly List<AsciiCharSet> mintermBlocks = new();
	readonly Dictionary<State, AsciiCharSet> byNext = new();
	readonly List<Transition> transitions = new();

	bool saturated;

	StateMapBuilder(RxFactory factory, int maxStates)
	{
		this.factory = factory;
		this.maxStates = maxStates;
	}

	/// <summary>
	/// Builds the machine for an expression.
	/// </summary>
	/// <param name="start">The expression, as produced by <see cref="RxConverter"/>.</param>
	/// <param name="factory">The factory that made it, reused so derivatives stay interned.</param>
	/// <param name="map">The machine, or <see langword="null"/> if it was refused.</param>
	/// <param name="error">The refusal, or <see langword="null"/>.</param>
	/// <param name="maxStates">The budget, lowered by tests so the cap can be reached cheaply.</param>
	/// <returns>Whether the machine was built.</returns>
	public static bool TryBuild(Rx start, RxFactory factory, out StateMap? map, out NaxpError? error, int maxStates = NaxpLimits.MaxStates)
		=> new StateMapBuilder(factory, maxStates).TryBuildCore(start, out map, out error);

	bool TryBuildCore(Rx start, out StateMap? map, out NaxpError? error)
	{
		map = null;

		if (!this.TryExplore(start, out List<Rx>? explored, out Dictionary<Rx, List<Edge>>? edges, out error))
		{
			return false;
		}

		// The successors of an expression all have a strictly shorter longest string, so this
		// ordering puts every state after the states it points at. The terminal expressions,
		// whose longest string is empty, come first.
		explored!.Sort(static (left, right) => left.MaxLength.CompareTo(right.MaxLength));

		State terminal = this.Intern(Array.Empty<Transition>());
		var stateOf = new Dictionary<Rx, State>();

		foreach (Rx expression in explored)
		{
			if (!edges!.TryGetValue(expression, out List<Edge>? outgoing))
			{
				// No first sets, so the language is the empty string alone.
				stateOf.Add(expression, terminal);
				continue;
			}

			Dictionary<State, AsciiCharSet> byNext = this.byNext;
			byNext.Clear();

			foreach (Edge edge in outgoing)
			{
				State next = stateOf[edge.Derivative];
				byNext[next] = byNext.TryGetValue(next, out AsciiCharSet already)
					? already | edge.Set
					: edge.Set
					;
			}

			List<Transition> transitions = this.transitions;
			transitions.Clear();

			if (expression.IsNullable) { transitions.Add(new Transition(AsciiCharSet.Empty, terminal)); }

			foreach (KeyValuePair<State, AsciiCharSet> pair in byNext)
			{
				transitions.Add(new Transition(pair.Value, pair.Key));
			}

			// After merging the sets are disjoint and non-empty apart from end of text, so the
			// sort has one outcome and its stability does not matter.
			transitions.Sort(static (left, right) => left.Set.CompareTo(right.Set));

			stateOf.Add(expression, this.Intern(transitions.ToArray()));
		}

		map = new StateMap(stateOf[start], this.states, this.saturated);
		error = null;
		return true;
	}

	/// <summary>
	/// Walks the derivatives breadth first, collecting the distinct expressions and the edges
	/// between them.
	/// </summary>
	bool TryExplore(Rx start, out List<Rx>? explored, out Dictionary<Rx, List<Edge>>? edges, out NaxpError? error)
	{
		explored = new List<Rx> { start };
		edges = new Dictionary<Rx, List<Edge>>();

		var seen = new HashSet<Rx> { start };
		var queue = new Queue<Rx>();
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			Rx expression = queue.Dequeue();
			AsciiCharSet[] firstSets = expression.GetFirstSets();

			if (firstSets.Length == 0) { continue; }

			var outgoing = new List<Edge>();

			Minterms(firstSets, this.mintermBlocks);

			foreach (AsciiCharSet minterm in this.mintermBlocks)
			{
				Rx derivative = this.factory.Derivative(expression, minterm);
				if (derivative.Kind == RxKind.EmptySet) { continue; }

				outgoing.Add(new Edge(minterm, derivative));

				if (seen.Add(derivative))
				{
					explored.Add(derivative);
					queue.Enqueue(derivative);

					if (explored.Count > this.maxStates)
					{
						explored = null;
						edges = null;
						error = new NaxpError(NaxpMessage.NAXP1049_TooManyStates);
						return false;
					}
				}
			}

			edges.Add(expression, outgoing);
		}

		error = null;
		return true;
	}

	/// <summary>
	/// Splits the characters covered by <paramref name="sets"/> into the coarsest blocks that
	/// each set is a union of.
	/// </summary>
	/// <param name="sets">The sets to separate.</param>
	/// <param name="blocks">
	/// The working list, cleared first and left holding the blocks. The caller owns it so that a
	/// builder can reuse one list across every state instead of allocating per state. It must not
	/// alias <paramref name="sets"/>, which the clearing would destroy; passing the same object
	/// throws, but a view onto it, such as one from <c>AsReadOnly</c>, cannot be detected.
	/// </param>
	internal static void Minterms(IReadOnlyList<AsciiCharSet> sets, List<AsciiCharSet> blocks)
	{
		// A List<AsciiCharSet> satisfies both parameters, so this is a mistake the compiler will
		// not catch. Clearing the output would empty the input and quietly give back no blocks at
		// all, which reads downstream as a state with no transitions rather than as a fault.
		if (ReferenceEquals(sets, blocks))
		{
			throw new ArgumentException("The working list must not be the list of sets.", nameof(blocks));
		}

		AsciiCharSet universe = AsciiCharSet.Empty;
		foreach (AsciiCharSet set in sets) { universe |= set; }

		blocks.Clear();

		if (universe.IsEmpty) { return; }

		blocks.Add(universe);

		foreach (AsciiCharSet set in sets)
		{
			// Once every block is a single character no further set can split anything.
			if (blocks.Count == universe.Count) { break; }

			// Only the blocks already present can be cut by this set. What gets appended below is
			// the part that fell outside it, which this set cannot cut again.
			int count = blocks.Count;

			for (int i = 0; i < count; ++i)
			{
				(AsciiCharSet inside, AsciiCharSet outside, _) = blocks[i].GetDisjointCombinations(set);

				// The block lies wholly inside the set or wholly outside it, so it stands.
				if (inside.IsEmpty || outside.IsEmpty) { continue; }

				blocks[i] = inside;
				blocks.Add(outside);
			}
		}
	}

	/// <summary>
	/// Splits the characters covered by <paramref name="sets"/> into the coarsest blocks that
	/// each set is a union of, in a list of its own.
	/// </summary>
	internal static List<AsciiCharSet> Minterms(IReadOnlyList<AsciiCharSet> sets)
	{
		var blocks = new List<AsciiCharSet>();
		Minterms(sets, blocks);

		return blocks;
	}

	State Intern(Transition[] transitions)
	{
		var key = new StateKey(transitions);

		if (this.interned.TryGetValue(key, out State? existing)) { return existing; }

		var created = new State(this.states.Count, transitions, this.CountValues(transitions));
		this.interned.Add(key, created);
		this.states.Add(created);

		return created;
	}

	/// <summary>
	/// The count of strings a state's language holds, which is the sum over its transitions of
	/// max(1, size of the set) times the count of the next state.
	/// </summary>
	ulong CountValues(Transition[] transitions)
	{
		if (transitions.Length == 0) { return 1UL; }

		ulong total = 0UL;

		foreach (Transition transition in transitions)
		{
			ulong width = transition.Set.IsEmpty ? 1UL : (ulong)transition.Set.Count;
			total = this.Add(total, this.Multiply(width, transition.Next.ValueCount));
		}

		return total;
	}

	// The limit is the full width of the accumulator, so a single step can wrap from operands
	// that were both themselves legal, and a wrap cannot be recognised by comparing the result
	// against the limit afterwards. Every step is therefore tested before it is taken.
	ulong Multiply(ulong left, ulong right)
	{
		if (left == 0UL || right == 0UL) { return 0UL; }

		if (left > NaxpLimits.MaxValueCount / right)
		{
			this.saturated = true;
			return NaxpLimits.MaxValueCount;
		}

		return left * right;
	}

	ulong Add(ulong left, ulong right)
	{
		ulong sum = left + right;

		// A sum below either operand is one that wrapped. Two alternatives that are each legal
		// on their own reach here, so this is not a theoretical case.
		if (sum < left)
		{
			this.saturated = true;
			return NaxpLimits.MaxValueCount;
		}

		return sum;
	}

	readonly struct Edge
	{
		public Edge(AsciiCharSet set, Rx derivative)
		{
			this.Set = set;
			this.Derivative = derivative;
		}

		public AsciiCharSet Set { get; }

		public Rx Derivative { get; }
	}

	/// <summary>
	/// The identity of a state, which is its transition list and nothing else. Two states are
	/// equal when their transitions are, which by induction means their languages are.
	/// </summary>
	readonly struct StateKey : IEquatable<StateKey>
	{
		readonly Transition[] transitions;
		readonly int hash;

		public StateKey(Transition[] transitions)
		{
			this.transitions = transitions;

			int accumulated = transitions.Length;
			foreach (Transition transition in transitions)
			{
				accumulated = (accumulated * 31) + transition.Set.GetHashCode();
				accumulated = (accumulated * 31) + transition.Next.Id;
			}

			this.hash = accumulated;
		}

		public bool Equals(StateKey other)
		{
			if (this.transitions.Length != other.transitions.Length) { return false; }

			for (int i = 0; i < this.transitions.Length; ++i)
			{
				if (!this.transitions[i].Set.Equals(other.transitions[i].Set)
					|| !ReferenceEquals(this.transitions[i].Next, other.transitions[i].Next))
				{
					return false;
				}
			}

			return true;
		}

		public override bool Equals(object? obj) => obj is StateKey other && this.Equals(other);

		public override int GetHashCode() => this.hash;
	}
}
