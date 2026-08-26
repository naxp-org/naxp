// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NXOld.NXComponents;

/// <summary>
/// Methods to transform an AST into states.
/// </summary>
static class StateMapGenerator
{
	#region Public (static) methods
	/// <summary>
	/// Gets the state map. The first element is the start state.
	/// </summary>
	/// <param name="ast">The AST to map.</param>
	/// <returns>All the states in a standard order, with the start state in the first element.</returns>
	public static State[] CreateStateMap(Ast ast)
	{
		if (ast is Empty)
		{
			return cachedEmptyMap is null ? cachedEmptyMap = [State.DefinitiveEndOfText] : cachedEmptyMap;
		}

		// Get all paths (list of lists of char sets)
		var allPaths = StateMapGenerator.Unroll(ast);

		if (allPaths.Count == 0) { return [State.DefinitiveEndOfText]; }

		// Cache of States so that we re-use common states.
		var allStates = new HashSet<State>();

		// Working space to avoid re-allocating at each recursion
		var disjointCharSetsListWS = new List<AsciiCharSet>();

		var startState = CreateStateMapRecursive(allPaths, 0, allStates, disjointCharSetsListWS);

		Debug.Assert(!startState.IsNull);

		var states = new State[allStates.Count];
		allStates.CopyTo(states);
		Array.Sort(states);

		return states;
	}

	/// <summary>
	/// Returns a list of all possible valid paths through <paramref name="ast"/>.
	/// </summary>
	/// <param name="ast">The AST to unroll.</param>
	/// <returns>A list of lists of <see cref="AsciiCharSet"/>s, representing all possible valid paths through the AST.</returns>
	public static List<List<AsciiCharSet>> Unroll(Ast ast)
	{
		// There must be a more efficient way of doing this.
		// TJG 2024-01-07

		if (ast is Chars chars)
		{
			return [[chars.CharSet]];
		}

		if (ast is Opt opt)
		{
			var optList = Unroll(opt.Child);
			optList.Add([]);
			return optList;
		}

		if (ast is Or or)
		{
			var children = or.Children;

			Debug.Assert(children.Length >= 2);

			var orList = Unroll(children[0]);

			for (int i = 1; i < children.Length; ++i)
			{
				var childList = Unroll(children[i]);
				orList.AddRange(childList);
			}

			return orList;
		}

		// At this point we know that `ast` is a `Seq`.
		var seq = (Seq)ast;
		return UnrollSeqChildren(seq.Children);
	}
	/// <summary>
	/// Updates the list of disjoint char sets in <paramref name="disjointCharSets"/>
	/// for <paramref name="newCharSet"/>.
	/// <para><paramref name="disjointCharSets"/> is undefined if it
	/// (a) contains empty char sets, or
	/// (b) is not actually disjoint.</para>
	/// </summary>
	/// <param name="disjointCharSets">The existing disjoint char sets.</param>
	/// <param name="newCharSet">The new char set to allow for.</param>
	public static void UpdateDisjointCharSets(List<AsciiCharSet> disjointCharSets, AsciiCharSet newCharSet)
	{
		if (newCharSet.IsEmpty) { return; }

		for (int k = 0; k < disjointCharSets.Count; ++k)
		{
			var existing = disjointCharSets[k];

			if (newCharSet.IntersectsWith(existing))
			{
				var (intersection, thisLessExisting, existingLessThis) = newCharSet.GetDisjointCombinations(existing);

				// `intersection` must be disjoint from other items in the list, so we can simply the existing element.
				disjointCharSets[k] = intersection;

				// This call order is most likely to preserve the existing order.
				if (!existingLessThis.IsEmpty) { UpdateDisjointCharSets(disjointCharSets, existingLessThis); }
				if (!thisLessExisting.IsEmpty) { UpdateDisjointCharSets(disjointCharSets, thisLessExisting); }

				return;
			}
		}

		// We get here if charSet is disjoint from all existing items in disjointCharSets.
		disjointCharSets.Add(newCharSet);
	}
	/// <summary>
	/// If multiple transitions in <paramref name="transitions"/> have the same next state 
	/// then these are merged (and <paramref name="transitions"/> is modified in place),
	/// </summary>
	/// <param name="transitions">The transitions to review and possibly modify.</param>
	public static void MergeTransitionsToSameState(ref Transition[] transitions)
	{
		bool changesWereMade;
		do
		{
			changesWereMade = false;
			for (int k = 1; k < transitions.Length; ++k)
			{
				var (charSet_k, nextState_k) = transitions[k];
				for (int i = 0; i < k; ++i)
				{
					var (charSet_i, nextState_i) = transitions[i];
					if (nextState_i.Equals(nextState_k))
					{
						Debug.Assert(!charSet_i.IntersectsWith(charSet_k));

						transitions[i] = new Transition(charSet_i | charSet_k, nextState_i);

						// New array without element k
						int n = transitions.Length;
						var newTransitions = new Transition[n - 1];
						Array.Copy(sourceArray: transitions, sourceIndex: 0, destinationArray: newTransitions, destinationIndex: 0, length: k);
						Array.Copy(sourceArray: transitions, sourceIndex: k + 1, destinationArray: newTransitions, destinationIndex: k, length: n - k - 1);
						transitions = newTransitions;
						changesWereMade = true;
						break;
					}
				}
				if (changesWereMade) { break; }
			}
		} while (changesWereMade);
	}
	/// <summary>
	/// Reforms an <see cref="Ast"/> given a <i>valid</i> state map.
	/// </summary>
	/// <param name="state">
	/// The state to translate into an <see cref="Ast"/>.
	/// <para>Must not be the null state.</para>
	/// </param>
	/// <returns>The <see cref="Ast"/>.</returns>
	public static Ast Rehydrate(in State state)
	{
		if (state.IsNull) { throw new ArgumentOutOfRangeException(nameof(state), "Cannot be the null state."); }

		// First, eliminate the empty case
		var transitions = state.transitions;
		if (transitions.Length == 1 && transitions[0].CharSet.IsEmpty) { return Empty.Instance; }

		// Canonical AST form is
		// Opt(Or(Seq(CharSet, CharSet, ...), Seq(CharSet, CharSet, ...), ...)
		// where:
		// (a) the wrapping Opt itself being optional (depending solely on whether this is a terminal transition from the initial state), and
		// (b) the wrapping Or and the Seqs can be elided if they are not required.

		var unrolledSequences = new List<AsciiCharSet[]>();
		UnrollToSequences(state, pathToThisState: new List<AsciiCharSet>(), unrolledSequences);

		static Ast CreateSeq(AsciiCharSet[] charSets)
		{
			if (charSets.Length == 1) { return new Chars(charSets[0]); }
			var childen = new Ast[charSets.Length];

			for (int i = 0; i < childen.Length; ++i)
			{
				childen[i] = new Chars(charSets[i]);
			}

			return new Seq(childen);
		}

		bool containsEmptySeq = false;
		var orArgs = new List<Ast>();
		foreach (var sequence in unrolledSequences)
		{
			if (sequence.Length == 0)
			{
				containsEmptySeq = true;
			}
			else
			{
				orArgs.Add(CreateSeq(sequence));
			}
		}

		Debug.Assert(orArgs.Count > 0);

		Ast ast = orArgs.Count == 1 ? orArgs[0] : new Or(orArgs.ToArray());

		if (containsEmptySeq) { ast = new Opt(ast); }

		Ast.Simplify(ref ast);

		return ast;
	}
	public static void UnrollToSequences(State state, List<AsciiCharSet> pathToThisState, List<AsciiCharSet[]> unrolledSequences)
	{
		foreach (var transition in state.transitions!)
		{
			if (transition.CharSet.IsEmpty)
			{
				unrolledSequences.Add(pathToThisState.ToArray());
			}
			else
			{
				pathToThisState.Add(transition.CharSet);
				UnrollToSequences(transition.NextState, pathToThisState, unrolledSequences);
				// This is clunky when all we really want is a pop method.
				// But Stack<T> does not copy to an array in the order we want.
				// This is the lessed evil.
				pathToThisState.RemoveAt(pathToThisState.Count - 1);
			}
		}
	}
	#endregion
	#region Private (static) methods
	static List<List<AsciiCharSet>> UnrollSeqChildren(ReadOnlySpan<Ast> remainingChildren)
	{
		Debug.Assert(remainingChildren.Length > 0);

		var unrolled_heads = Unroll(remainingChildren[0]);

		if ((uint)remainingChildren.Length <= (uint)1)
		{
			return unrolled_heads;
		}

		var unrolled_tails = UnrollSeqChildren(remainingChildren[1..]);

		var unrolled = new List<List<AsciiCharSet>>();

		foreach (var head in unrolled_heads)
		{
			foreach (var tail in unrolled_tails)
			{
				unrolled.Add([.. head, .. tail]);
			}
		}

		return unrolled;
	}
	static State CreateStateMapRecursive(List<List<AsciiCharSet>> pathsFromThisState, int charPos, HashSet<State> allStates, List<AsciiCharSet> disjointCharSetsListWS)
	{
		Debug.Assert(pathsFromThisState.Count > 0);

		// Get set of disjoint chars sets over all char sets at this point in the path
		disjointCharSetsListWS.Clear();
		bool includesEOT = false;
		foreach (var path in pathsFromThisState)
		{
			if (charPos < path.Count)
			{
				var charSet = path[charPos];
				UpdateDisjointCharSets(disjointCharSetsListWS, charSet);
			}
			else
			{
				includesEOT = true;
			}
		}

		int transitionCount = disjointCharSetsListWS.Count;
		if (includesEOT) { ++transitionCount; }

		Debug.Assert(transitionCount > 0);

		State state;

		if (includesEOT && transitionCount == 1)
		{
			// We've reached the end.
			state = State.DefinitiveEndOfText;
		}
		else
		{
			var transitions = new Transition[transitionCount];

			if (includesEOT) { transitions[0] = default; }

			if (!includesEOT && transitionCount == 1)
			{
				// We can re-use the existing all paths.
				// Calling CreateStateMapRecursive will overwrite disjointCharSetsListWS so copy it beforehand.
				var charSet_0 = disjointCharSetsListWS[0];
				var nextState = CreateStateMapRecursive(pathsFromThisState, charPos + 1, allStates, disjointCharSetsListWS);
				transitions[0] = new Transition(charSet_0, nextState);
			}
			else
			{
				// Calling CreateStateMapRecursive will overwrite disjointCharSetsListWS
				// and so we copy it (ideally to the stack).
				var listSpan = CollectionsMarshal.AsSpan(disjointCharSetsListWS);
				Debug.Assert(listSpan.Length == disjointCharSetsListWS.Count);
				int listCount = listSpan.Length;
				Span<AsciiCharSet> disjointCharSets = listCount <= RecursiveStackSafeAsciiCharSetCount
					? (stackalloc AsciiCharSet[RecursiveStackSafeAsciiCharSetCount])[..listCount]
					: new AsciiCharSet[listCount];
				listSpan.CopyTo(disjointCharSets);

				for (int i = includesEOT ? 1 : 0; i < transitions.Length; ++i)
				{
					int charSetIndex = i;
					if (includesEOT) { --charSetIndex; }

					var transitionCharSet = disjointCharSets[charSetIndex];

					var pathsFromNextState = new List<List<AsciiCharSet>>();
					foreach (var path in pathsFromThisState)
					{
						if (charPos < path.Count)
						{
							var charSet = path[charPos];
							if (charSet.IntersectsWith(transitionCharSet))
							{
								pathsFromNextState.Add(path);
							}
						}
					}

					var nextState = CreateStateMapRecursive(pathsFromNextState, charPos + 1, allStates, disjointCharSetsListWS);
					transitions[i] = new Transition(transitionCharSet, nextState);
				}

				if (transitions.Length > 0)
				{
					MergeTransitionsToSameState(ref transitions);
					Array.Sort(transitions, StateMapGenerator.sortByCharSets);
				}
			}

			state = new State(transitions, transitionsHaveBeenValidated: true);
		}

		if (allStates.TryGetValue(state, out var existingState))
		{
			state = existingState;
		}
		else
		{
			allStates.Add(state);
		}

		return state;
	}
	#endregion
	#region Private static data and constants
	static readonly Comparison<Transition> sortByCharSets = (Transition a, Transition b) => a.CharSet.CompareTo(b.CharSet);
	static State[]? cachedEmptyMap;
	/// <summary>
	/// Conservative safe stack alloc size.
	/// </summary>
	const int RecursiveStackSafeSizeInBytes = 256;
	const int RecursiveStackSafeAsciiCharSetCount = RecursiveStackSafeSizeInBytes / 16;
	#endregion
}