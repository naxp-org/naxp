// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;

namespace LogMu;

/// <summary>
/// How the languages of two machines, and the encodings of two naxps, stand to one another.
/// </summary>
/// <remarks>
/// States are interned within one build, so two languages built through the same factory are
/// equal exactly when their start states are the same object. Two naxps parsed separately do
/// not share a factory, so that shortcut is unavailable here and the walk below is what decides
/// it.
/// </remarks>
static class Relations
{
	/// <summary>
	/// How the language of <paramref name="a"/> stands to the language of <paramref name="b"/>.
	/// </summary>
	/// <remarks>
	/// A walk of the product, carrying two facts: whether <paramref name="a"/> holds a string
	/// <paramref name="b"/> does not, and whether the reverse holds. Both machines are acyclic
	/// and trimmed, so a state reached with characters the other side cannot take is a witness
	/// on its own and needs no completion.
	/// </remarks>
	/// <param name="a">The first machine.</param>
	/// <param name="b">The second machine.</param>
	/// <returns>The relationship, from <c>a</c>'s point of view.</returns>
	public static SetRelationship CompareLanguages(StateMap a, StateMap b)
	{
		bool aHasExtra = false;
		bool bHasExtra = false;
		var visited = new HashSet<(int, int)>();

		Visit(a.Start, b.Start);

		return (aHasExtra, bHasExtra) switch
		{
			(false, false) => SetRelationship.Equal,
			(true, false) => SetRelationship.SupersetOf,
			(false, true) => SetRelationship.SubsetOf,
			_ => SetRelationship.Incomparable,
		};

		void Visit(State p, State q)
		{
			// Nothing further can change the answer once both are known.
			if (aHasExtra && bHasExtra) { return; }

			if (!visited.Add((p.Id, q.Id))) { return; }

			if (p.AcceptsEndOfText != q.AcceptsEndOfText)
			{
				if (p.AcceptsEndOfText) { aHasExtra = true; } else { bHasExtra = true; }
			}

			AsciiCharSet takenByP = Taken(p);
			AsciiCharSet takenByQ = Taken(q);

			// A character one side can take and the other cannot leads somewhere, because every
			// state of a trimmed machine holds at least one string.
			foreach (Transition transition in p.Transitions)
			{
				if (transition.Set.IsEmpty) { continue; }

				if (!(transition.Set - takenByQ).IsEmpty) { aHasExtra = true; }
			}

			foreach (Transition transition in q.Transitions)
			{
				if (transition.Set.IsEmpty) { continue; }

				if (!(transition.Set - takenByP).IsEmpty) { bHasExtra = true; }
			}

			foreach (Transition left in p.Transitions)
			{
				if (left.Set.IsEmpty) { continue; }

				foreach (Transition right in q.Transitions)
				{
					if (right.Set.IsEmpty) { continue; }

					if (left.Set.IntersectsWith(right.Set)) { Visit(left.Next, right.Next); }
				}
			}
		}
	}

	/// <summary>
	/// How the encoding of <paramref name="a"/> stands to the encoding of <paramref name="b"/>,
	/// each taken as the set of (text, value) pairs it defines.
	/// </summary>
	/// <remarks>
	/// <para>
	/// One graph contains another exactly when its domain does and the two functions agree on
	/// the smaller domain. So the relationship is the one between the accepted languages,
	/// provided every string both accept takes the same value under each, and
	/// <see cref="SetRelationship.Incomparable"/> otherwise, since a string valued differently
	/// puts a pair in each graph that the other lacks.
	/// </para>
	/// <para>
	/// Value agreement is asked of the values and never of the canonical forms.
	/// <c>(A|B)!A</c> and <c>(A|B)!B</c> print <c>B</c> differently and value it alike, and
	/// their encodings are equal. Where neither naxp holds a unified element the canonical
	/// machines are the accepted ones and <see cref="RankAgreement"/> decides exactly. Otherwise
	/// its disagreement is still exact, because a shared canonical string is fixed by both
	/// canonicalisations, and it is tried first for that reason; its agreement says nothing
	/// about the strings a unified element rewrites, which <see cref="ValueAgreement"/> then
	/// decides over parses.
	/// </para>
	/// <para>
	/// This is the one axis of the comparison that can fail to be decided, when a walk outgrows
	/// its budget. It then returns <see langword="false"/> and the relationship is left as
	/// <see cref="SetRelationship.Incomparable"/>, which is the value that claims nothing.
	/// </para>
	/// </remarks>
	/// <param name="a">The first naxp.</param>
	/// <param name="b">The second naxp.</param>
	/// <param name="relationship">The relationship, from <c>a</c>'s point of view.</param>
	/// <param name="budget">How many product states either walk may build.</param>
	/// <returns>Whether the relationship was decided.</returns>
	public static bool TryCompareEncodings(Compilation a, Compilation b, out SetRelationship relationship, int budget = ValueAgreement.MaxTuples)
	{
		if (a is null) { throw new ArgumentNullException(nameof(a)); }
		if (b is null) { throw new ArgumentNullException(nameof(b)); }

		relationship = SetRelationship.Incomparable;

		// With no string held in common, or with each holding strings the other lacks, the
		// graphs are incomparable before any value is looked at.
		SetRelationship languages = CompareLanguages(a.Accepted, b.Accepted);
		if (languages == SetRelationship.Incomparable) { return true; }

		Agreement ranks = RankAgreement.Compare(a.Canonical, b.Canonical, budget);

		if (ranks == Agreement.Differs) { return true; }

		Agreement values;

		if (a.CanonicalIsIdentity && b.CanonicalIsIdentity)
		{
			// Every accepted string is canonical, so the rank walk has already seen them all.
			values = ranks;
		}
		else
		{
			values = ValueAgreement.Compare(a, b, out _, budget);
		}

		if (values == Agreement.Undecided) { return false; }

		relationship = values == Agreement.Agrees ? languages : SetRelationship.Incomparable;

		return true;
	}

	/// <summary>
	/// The lowest value both machines hold that they decode to different strings, or zero where
	/// every value both hold decodes alike.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a different question from the encoding relation. <c>(A|B)!A</c> and
	/// <c>(A|B)!B</c> give every string the same value and decode value 1 to <c>A</c> and to
	/// <c>B</c>; equal encodings, divergent at 1. A naxp extended by values that sort after all
	/// its own decodes every value it had as before, so the answer is zero however many were
	/// added, and the count of values says the rest.
	/// </para>
	/// <para>
	/// The walk enumerates both canonical languages in the order the specification defines,
	/// the empty string first and then each first class in set order, each character of it in
	/// ASCII order and each continuation in turn, and compares the two enumerations position
	/// by position. Where two continuations are reached by the same character the comparison
	/// recurses and a whole chunk is stepped over by its count, which is what keeps the walk
	/// linear in the product of the machines rather than in the number of values. The result at
	/// a pair of states does not depend on how the pair was reached, so it is memoised.
	/// </para>
	/// <para>
	/// Counts are trusted, which W5 guarantees for a canonical machine: the count of values is
	/// capped at 2^64 - 1, so no count here is saturated.
	/// </para>
	/// </remarks>
	/// <param name="a">The first canonical machine.</param>
	/// <param name="b">The second canonical machine.</param>
	/// <returns>The value, or zero for none.</returns>
	public static ulong FirstDivergentValue(StateMap a, StateMap b)
	{
		if (a is null) { throw new ArgumentNullException(nameof(a)); }
		if (b is null) { throw new ArgumentNullException(nameof(b)); }

		var memo = new Dictionary<(int, int), (bool found, ulong index)>();

		return Diverge(a.Start, b.Start, out ulong index) ? index + 1UL : 0UL;

		// Whether the two enumerations differ within the length of the shorter, and where.
		bool Diverge(State p, State q, out ulong index)
		{
			var key = (p.Id, q.Id);

			if (memo.TryGetValue(key, out (bool found, ulong index) known))
			{
				index = known.index;
				return known.found;
			}

			bool found = Compare(p, q, out index);
			memo[key] = (found, index);

			return found;
		}

		bool Compare(State p, State q, out ulong index)
		{
			List<(char c, State? next)> left = Chunks(p);
			List<(char c, State? next)> right = Chunks(q);
			int i = 0;
			int j = 0;
			ulong offset = 0UL;

			while (i < left.Count && j < right.Count)
			{
				(char c, State? next) x = left[i];
				(char c, State? next) y = right[j];

				// Both end here: one value each, the same string.
				if (x.next is null && y.next is null)
				{
					++offset;
					++i;
					++j;
					continue;
				}

				// One ends where the other reads on, or they read different characters, so the
				// strings at this position differ in this very character.
				if (x.next is null || y.next is null || x.c != y.c)
				{
					index = offset;
					return true;
				}

				if (Diverge(x.next, y.next, out ulong inner))
				{
					index = offset + inner;
					return true;
				}

				ulong leftCount = x.next.StringCount;
				ulong rightCount = y.next.StringCount;

				if (leftCount == rightCount)
				{
					offset += leftCount;
					++i;
					++j;
					continue;
				}

				// The shorter continuation ran out inside the chunk. The longer side's next
				// string still begins with this character; the shorter side's next chunk
				// begins with another, so they differ there, unless the shorter side has no
				// next chunk and its whole enumeration has ended.
				bool shorterHasMore = leftCount < rightCount ? i + 1 < left.Count : j + 1 < right.Count;

				if (shorterHasMore)
				{
					index = offset + Math.Min(leftCount, rightCount);
					return true;
				}

				index = 0UL;
				return false;
			}

			// One enumeration ended without a difference, so there is none within the shorter.
			index = 0UL;
			return false;
		}

		// A state's enumeration as chunks in order: the empty string, then one chunk per
		// character in class order and ASCII order within a class. A null state marks the end
		// of text.
		static List<(char c, State? next)> Chunks(State state)
		{
			var chunks = new List<(char c, State? next)>();

			foreach (Transition transition in state.Transitions)
			{
				if (transition.Set.IsEmpty)
				{
					chunks.Add(('\0', null));
					continue;
				}

				foreach (char c in transition.Set) { chunks.Add((c, transition.Next)); }
			}

			if (state.IsTerminal) { chunks.Add(('\0', null)); }

			return chunks;
		}
	}

	/// <summary>
	/// The characters a state can consume, the end of text transition excepted.
	/// </summary>
	/// <param name="state">The state.</param>
	/// <returns>The union of its transition sets.</returns>
	static AsciiCharSet Taken(State state)
	{
		AsciiCharSet taken = AsciiCharSet.Empty;

		foreach (Transition transition in state.Transitions)
		{
			taken |= transition.Set;
		}

		return taken;
	}
}
