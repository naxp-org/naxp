// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.Collections.Generic;
using System.Numerics;

namespace LogMu;

/// <summary>
/// Whether two machines give the same encoded value to every string they both hold.
/// </summary>
enum Agreement
{
	/// <summary>Every string both machines hold takes the same value under each.</summary>
	Agrees,

	/// <summary>At least one string both hold takes different values.</summary>
	Differs,

	/// <summary>
	/// Not decided. Either a count saturated, so the arithmetic is not the true rank, or the
	/// walk outgrew its budget.
	/// </summary>
	Undecided,
}

/// <summary>
/// Comparing the encoded values two canonical machines give the strings they share.
/// </summary>
/// <remarks>
/// A value is a rank: the count of strings that precede this one. Walking a machine accumulates
/// that count a character at a time, so walking two machines together and carrying the
/// difference of the two running totals says whether they will end up agreeing.
///
/// The difference at a state pair is a property of the pair, not of the path that reached it.
/// Two strings that arrive at the same pair with different differences have the same
/// completions available to both machines from there, so whatever suffix takes one to an
/// accepting pair takes the other as well, and the two cannot both come out at zero. A second,
/// unequal arrival is therefore a disagreement rather than something to explore - which is what
/// keeps this linear in the size of the product rather than exponential in the alphabet.
/// </remarks>
static class RankAgreement
{
	/// <summary>
	/// How many state pairs the walk may visit before giving up.
	/// </summary>
	/// <remarks>
	/// W6 caps each machine at 2000 states, so a product can in principle reach four million
	/// pairs. This is far below that, because a comparison worth making is between two naxps
	/// that resemble one another, and one that explodes is telling you they do not.
	/// </remarks>
	public const int MaxPairs = 200_000;

	/// <summary>
	/// Whether <paramref name="a"/> and <paramref name="b"/> give the same value to every
	/// string they both hold.
	/// </summary>
	/// <param name="a">The first canonical machine.</param>
	/// <param name="b">The second canonical machine.</param>
	/// <param name="maxPairs">How many state pairs to allow.</param>
	/// <returns>Whether they agree, differ, or could not be decided.</returns>
	public static Agreement Compare(StateMap a, StateMap b, int maxPairs = MaxPairs)
	{
		// A saturated count is a stand-in for the real one, so the arithmetic below would be
		// comparing two approximations rather than two ranks.
		if (a.CountSaturated || b.CountSaturated) { return Agreement.Undecided; }

		var difference = new Dictionary<(int, int), BigInteger>();
		var conflicts = new List<(State, State)>();
		bool outgrew = false;

		Visit(a.Start, b.Start, BigInteger.Zero);

		if (outgrew) { return Agreement.Undecided; }

		// A conflict only matters where the two machines still share a string from that pair.
		// Where the shared language dies out below it, nothing reaches an accepting pair and
		// the differing totals are never compared.
		var reaches = new Dictionary<(int, int), bool>();

		foreach ((State p, State q) in conflicts)
		{
			if (SharesAnEnding(p, q)) { return Agreement.Differs; }
		}

		return Agreement.Agrees;

		void Visit(State p, State q, BigInteger diff)
		{
			if (outgrew) { return; }

			var key = (p.Id, q.Id);

			if (difference.TryGetValue(key, out BigInteger seen))
			{
				if (seen != diff) { conflicts.Add((p, q)); }

				return;
			}

			if (difference.Count >= maxPairs) { outgrew = true; return; }

			difference.Add(key, diff);

			// Both ending here is a string they share, and the value each gives it is the
			// running total plus one, so the difference decides it.
			if (p.AcceptsEndOfText && q.AcceptsEndOfText && !diff.IsZero)
			{
				conflicts.Add((p, q));
			}

			foreach (Transition left in p.Transitions)
			{
				if (left.Set.IsEmpty) { continue; }

				foreach (Transition right in q.Transitions)
				{
					if (right.Set.IsEmpty) { continue; }

					AsciiCharSet shared = left.Set & right.Set;

					if (shared.IsEmpty) { continue; }

					// The rank a character contributes depends on where it sits in its own
					// set, so each shared character is its own step. They agree on the pair
					// they move to, which is what the memo above collapses them onto.
					for (int i = 0; i < shared.Count; i++)
					{
						char c = shared.CharacterAt(i);

						Visit(
							left.Next,
							right.Next,
							diff + Contribution(p, left, c) - Contribution(q, right, c));
					}
				}
			}
		}

		// Whether the two states still hold a string in common, which is what makes a
		// difference between their totals something anybody can observe.
		bool SharesAnEnding(State p, State q)
		{
			var key = (p.Id, q.Id);

			if (reaches.TryGetValue(key, out bool known)) { return known; }

			// False while the answer is being worked out, so a pair cannot depend on itself.
			reaches[key] = false;

			bool found = p.AcceptsEndOfText && q.AcceptsEndOfText;

			if (!found)
			{
				foreach (Transition left in p.Transitions)
				{
					if (left.Set.IsEmpty || found) { continue; }

					foreach (Transition right in q.Transitions)
					{
						if (right.Set.IsEmpty) { continue; }

						if (left.Set.IntersectsWith(right.Set) && SharesAnEnding(left.Next, right.Next))
						{
							found = true;
							break;
						}
					}
				}
			}

			reaches[key] = found;

			return found;
		}
	}

	/// <summary>
	/// What taking one character adds to the running total, which is the count of strings the
	/// machine passes over to reach it.
	/// </summary>
	/// <remarks>
	/// The same arithmetic as <see cref="Codec.Encode"/>, and it has to stay the same: the
	/// transitions before the one taken are skipped whole, and within the one taken the
	/// character's own position counts once for each string its successor holds.
	/// <see cref="ValueAgreement"/> walks canonical outputs with it for the same reason.
	/// </remarks>
	/// <param name="state">The state the character is read in.</param>
	/// <param name="taken">The transition the character belongs to.</param>
	/// <param name="c">The character.</param>
	/// <returns>The amount added.</returns>
	internal static BigInteger Contribution(State state, Transition taken, char c)
	{
		BigInteger skipped = BigInteger.Zero;

		foreach (Transition transition in state.Transitions)
		{
			ulong count = transition.Next.StringCount;

			if (transition.Set == taken.Set)
			{
				return skipped + (BigInteger)count * taken.Set.IndexOf(c);
			}

			// An empty set is the end of text transition, which stands for one string.
			skipped += (BigInteger)count * (transition.Set.IsEmpty ? 1UL : (ulong)transition.Set.Count);
		}

		return skipped;
	}
}
