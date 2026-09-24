// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;

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
	/// The encoded value of a canonical string, or zero if the string is invalid.
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
				ulong count = transition.Next.StringCount;

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

		// A path through an acyclic machine visits no state twice, so no string is as long as
		// the machine has states.
		Span<char> buffer = stackalloc char[map.States.Count];
		int length = Decode(map, value, buffer);

		text = length < 0 ? null : buffer.Slice(0, length).ToString();

		return text is not null;
	}

	/// <summary>
	/// Writes the string of a value into a buffer, which is the value's position in the canonical
	/// language.
	/// </summary>
	/// <param name="map">The machine for the canonical language.</param>
	/// <param name="value">The value, from 1 to the size of that language.</param>
	/// <param name="destination">Where the string goes, which holds the longest string in the language.</param>
	/// <returns>The length of the string, or -1 if the value is out of range.</returns>
	public static int Decode(StateMap map, ulong value, Span<char> destination)
	{
		if (map is null) { throw new ArgumentNullException(nameof(map)); }

		// Zero is reserved for invalid text, so it decodes to nothing.
		if (value == 0UL || value > map.StringCount) { return -1; }

		State state = map.Start;
		ulong remaining = value;
		int length = 0;

		while (!state.IsTerminal)
		{
			State? next = null;

			foreach (Transition transition in state.Transitions)
			{
				if (transition.Set.IsEmpty)
				{
					if (remaining == 1UL) { return length; }

					remaining -= 1UL;
					continue;
				}

				ulong perCharacter = transition.Next.StringCount;
				ulong block = (ulong)transition.Set.Count * perCharacter;

				if (remaining <= block)
				{
					destination[length++] = transition.Set.CharacterAt((int)((remaining - 1UL) / perCharacter));
					remaining = ((remaining - 1UL) % perCharacter) + 1UL;
					next = transition.Next;
					break;
				}

				remaining -= block;
			}

			// The value was checked against the count of the start state, and each step leaves
			// it within the count of the state it moves to, so this cannot be reached.
			if (next is null) { return -1; }

			state = next;
		}

		return length;
	}
}
