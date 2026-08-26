// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Text;

namespace LogMu;

/// <summary>
/// The encoding and its inverse, as walks of the canonical machine.
/// </summary>
/// <remarks>
/// <para>
/// The value is a mixed radix positional number, most significant digit first. Passing a
/// transition skips every value below it, the rank of the character within its set is the
/// leading digit, and the value of the remainder of the string is the rest.
/// </para>
/// <para>
/// Neither walk recurses. The specification writes both as recursions, but the encoding one
/// only ever adds its result to what the caller accumulated, so it flattens into a loop, and
/// the longest string a naxp may generate would otherwise want
/// <see cref="NaxpLimits.MaxStringLength"/> stack frames.
/// </para>
/// <para>
/// Decoding uses the canonical machine only. The accepted language plays no part in it.
/// </para>
/// </remarks>
static class Codec
{
	/// <summary>
	/// The value of a canonical string, or zero if the machine does not accept it.
	/// </summary>
	/// <param name="map">The machine for the canonical language.</param>
	/// <param name="text">The string, which must already be in canonical form.</param>
	/// <returns>The value, from 1 upwards, or zero.</returns>
	public static ulong Encode(StateMap map, ReadOnlySpan<char> text)
	{
		if (map is null) { throw new ArgumentNullException(nameof(map)); }

		State state = map.Start;
		ulong total = 0UL;

		foreach (char c in text)
		{
			ulong skipped = 0UL;
			State? next = null;

			foreach (Transition transition in state.Transitions)
			{
				ulong count = transition.Next.ValueCount;

				if (transition.Set.Contains(c))
				{
					total += skipped + (count * (ulong)transition.Set.IndexOf(c));
					next = transition.Next;
					break;
				}

				// An empty set is the end of text transition, which stands for one value.
				skipped += count * (transition.Set.IsEmpty ? 1UL : (ulong)transition.Set.Count);
			}

			if (next is null) { return 0UL; }

			state = next;
		}

		return state.AcceptsEndOfText ? total + 1UL : 0UL;
	}

	/// <summary>
	/// The string of a value, which is the value's position in the canonical language.
	/// </summary>
	/// <param name="map">The machine for the canonical language.</param>
	/// <param name="value">The value, from 1 to the size of that language.</param>
	/// <param name="text">The string, or <see langword="null"/> if the value is out of range.</param>
	/// <returns>Whether the value is one the naxp can produce.</returns>
	public static bool TryDecode(StateMap map, ulong value, out string? text)
	{
		if (map is null) { throw new ArgumentNullException(nameof(map)); }

		text = null;

		// Zero is reserved for a string the naxp does not accept, so it decodes to nothing.
		if (value == 0UL || value > map.ValueCount) { return false; }

		var builder = new StringBuilder();
		State state = map.Start;
		ulong remaining = value;

		while (!state.IsTerminal)
		{
			State? next = null;

			foreach (Transition transition in state.Transitions)
			{
				if (transition.Set.IsEmpty)
				{
					if (remaining == 1UL) { text = builder.ToString(); return true; }

					remaining -= 1UL;
					continue;
				}

				ulong perCharacter = transition.Next.ValueCount;
				ulong block = (ulong)transition.Set.Count * perCharacter;

				if (remaining <= block)
				{
					builder.Append(transition.Set.CharacterAt((int)((remaining - 1UL) / perCharacter)));
					remaining = ((remaining - 1UL) % perCharacter) + 1UL;
					next = transition.Next;
					break;
				}

				remaining -= block;
			}

			// The value was checked against the count of the start state, and each step leaves
			// it within the count of the state it moves to, so this cannot be reached.
			if (next is null) { return false; }

			state = next;
		}

		text = builder.ToString();
		return true;
	}
}
