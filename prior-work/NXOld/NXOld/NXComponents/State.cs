// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace NXOld.NXComponents;

/// <summary>
/// An NX state.
/// <para>The default value is the terminal state (which can be tested for using <see cref="IsNull"/>.</para>
/// </summary>
#if DEBUG
[DebuggerDisplay("{DebuggerDisplay,nq}")]
#endif
public struct State : IEquatable<State>, IComparable<State>
{
	#region Public data
	/// <summary>
	/// The minimum number of characters to read from this state to the terminal state. 
	/// This is at least 1 unless this is the terminal state.
	/// </summary>
	public int MinLength { get; }
	/// <summary>
	/// The minimum number of characters to read from this state to the terminal state. 
	/// This is at least 1 unless this is the terminal state.
	/// </summary>
	public int MaxLength { get; }
	#endregion
	#region Internal data
	// We can't make this readonly *and* represent it externally as immutable.
	internal Transition[]? transitions;
	#endregion
	#region Private data
	readonly ulong characterCombinationCountLessOne;
	readonly int hashCode;
	#endregion
	#region Public ctors / d-ctors
	/// <summary>
	/// Initialises a <i>non-null </i><see cref="State"/> by specifying its transitions.
	/// </summary>
	/// <param name="transitions">
	/// The transitions from this state. 
	/// <para>
	/// These must meet the following requirements 
	/// in order to be valid (which you can check using 
	/// <see cref="TransitionsAreValid(Transition[], out string?)"/>):
	/// </para>
	/// <list type="number">
	/// <item>The array must contain at least one element.</item>
	/// <item>
	/// A maximum of one transition can have an empty character set (representing the end of text). 
	/// In that case, the target state must be null (i.e. <see langword="default"/>).
	/// </item>
	/// <item>The character sets for different transitions do not intersect.</item>
	/// <item>The transitions must be sorted using <see cref="Transition.CharSet"/>.</item>
	/// <item>The different transitions should map to different next states.</item>
	/// </list>
	/// </param>
	public State(ImmutableArray<Transition> transitions)
		: this(ImmutableCollectionsMarshal.AsArray(transitions)!, transitionsHaveBeenValidated: false)
	{ }
	#endregion
	#region Internal ctors / d-ctors
	/// <summary>
	/// <inheritdoc cref="State(ImmutableArray{Transition})"/>
	/// <para>You can opt out of validation by specifying <paramref name="transitionsHaveBeenValidated"/> as <see langword="true"/>.</para>
	/// </summary>
	/// <param name="transitions">
	/// <inheritdoc cref="State(ImmutableArray{Transition})"/>
	/// </param>
	/// <param name="transitionsHaveBeenValidated">
	/// If this is <see langword="true"/> then it is assumed that <paramref name="transitions"/> has already been validated.
	/// </param>
	internal State(Transition[] transitions, bool transitionsHaveBeenValidated)
	{
#if DEBUG
		ValidateTransitions(transitions);
#else
        if (!transitionsHaveBeenValidated)
        {
            ValidateTransitions(transitions);
        }
#endif

		ulong characterCombinationCount = 0;
		int minLength = int.MaxValue;
		int maxLength = 0;
		int hashCode = transitions.Length;
		foreach (var transition in transitions)
		{
			characterCombinationCount += transition.CharacterCombinationCount;
			var nextState = transition.NextState;
			if (nextState.IsNull)
			{
				minLength = Math.Min(minLength, 0);
				//maxLength = Math.Max(maxLength, 0);
			}
			else
			{
				minLength = Math.Min(minLength, nextState.MinLength + 1);
				maxLength = Math.Max(maxLength, nextState.MaxLength + 1);
			}
			hashCode = HashCode.Combine(hashCode, transition);
		}

		this.characterCombinationCountLessOne = characterCombinationCount - 1;
		this.MinLength = minLength;
		this.MaxLength = maxLength;
		this.hashCode = hashCode;
		this.transitions = transitions;
	}
	#endregion
	#region Public properties and methods
	/// <summary>
	/// The transitions from this state.
	/// This is <see langword="null"/> for the terminal state (which is what <see cref="IsNull"/> tests).
	/// <para>If the array is non-null then it is guaranteed to have at least one element.</para>
	/// </summary>
#pragma warning disable CS8601 // Possible null reference assignment.
	public readonly ImmutableArray<Transition> Transitions
		=> ImmutableCollectionsMarshal.AsImmutableArray(this.transitions);
#pragma warning restore CS8601 // Possible null reference assignment.
	/// <summary>
	/// The number of different valid remaining character input combinations starting at this state.
	/// <para>This is guaranteed to be at least one, including for the null state.</para>
	/// </summary>
	public readonly ulong CharacterCombinationCount
		=> this.characterCombinationCountLessOne + 1;
	/// <summary>
	/// Whether this state is the terminal state.
	/// <para>This is identical to testing whether <see cref="transitions"/> is <see langword="null"/>.</para>
	/// </summary>
	[MemberNotNullWhen(false, nameof(transitions))]
	public readonly bool IsNull => this.transitions is null;
	/// <summary>
	/// Whether this state accepts the specified text.
	/// </summary>
	/// <param name="text">The text to test for acceptance.</param>
	/// <returns>Whether <paramref name="text"/> is accepted.</returns>
	public readonly bool Accepts(ReadOnlySpan<char> text)
	{
		if (this.IsNull) { ThrowInvalidOperation(); }

		if (text.Length == 0)
		{
			foreach (var transition in this.transitions)
			{
				if (transition.CharSet.IsEmpty) { return true; }
			}
			return false;
		}

		var c = text[0];
		foreach (var transition in this.transitions)
		{
			if (transition.CharSet.Contains(c))
			{
				return transition.NextState.Accepts(text[1..]);
			}
		}

		return false;
	}
	/// <summary>
	/// Whether this state accepts the specified ASCII byte text.
	/// </summary>
	/// <param name="text">The text to test for acceptance.</param>
	/// <returns>Whether <paramref name="text"/> is accepted.</returns>
	public readonly bool Accepts(ReadOnlySpan<byte> text)
	{
		if (this.IsNull) { ThrowInvalidOperation(); }

		if (text.Length == 0)
		{
			foreach (var transition in this.transitions)
			{
				if (transition.CharSet.IsEmpty) { return true; }
			}
			return false;
		}

		var b = text[0];
		foreach (var transition in this.transitions)
		{
			if (transition.CharSet.Contains((char)b))
			{
				return transition.NextState.Accepts(text[1..]);
			}
		}

		return false;
	}
	/// <summary>
	/// Gets the encoding of the text:
	/// <list type="bullet">
	/// <item>If the text can be encoded then the result non-zero.</item>
	/// <item>Zero means that the text is <i>not</i> included in the NX.</item>
	/// </list>
	/// </summary>
	/// <param name="text">The text to encode.</param>
	/// <returns>The encoding of the text.</returns>
	public readonly ulong GetEncoding(ReadOnlySpan<char> text)
	{
		if (this.IsNull) { ThrowInvalidOperation(); }

		if (text.Length == 0)
		{
			foreach (var transition in this.transitions)
			{
				if (transition.CharSet.IsEmpty) { return 1u; }
			}
			return 0u;
		}

		ulong encodingOffset = 0u;
		var c = text[0];
		foreach (var (charSet, nextState) in this.transitions)
		{
			var n = nextState.CharacterCombinationCount;
			if (charSet.Contains(c))
			{
				var nextEncoding = nextState.GetEncoding(text[1..]);
				if (nextEncoding == 0) { return 0; }
				return encodingOffset + n * (ulong)charSet.IndexOf(c) + nextEncoding;
			}
			// The max below allows for a possible end of text transition which
			// *does* increase the offset but for which charSet.Count is 0.
			encodingOffset += n * (ulong)Math.Max(1, charSet.Count);
		}

		return 0;
	}
	/// <inheritdoc/>
	public readonly bool Equals(State other)
	{
		// Short cut unequal comparisons
		if (this.hashCode != other.hashCode
			|| this.MaxLength != other.MaxLength
			|| this.MinLength != other.MinLength
			)
		{
			return false;
		}

		var thisTransitions = this.transitions;
		var otherTransitions = other.transitions;

		if (thisTransitions is null) { return otherTransitions is null; }
		if (otherTransitions is null) { return false; }

		// Do it the hard way
		for (int i = 0; i < thisTransitions.Length; ++i)
		{
			if (!thisTransitions[i].Equals(otherTransitions[i])) { return false; }
		}

		// Comparing char sets is local, so do this first.
		for (int i = 0; i < thisTransitions.Length; ++i)
		{
			if (!thisTransitions[i].CharSet.Equals(otherTransitions[i].CharSet)) { return false; }
		}

		// If we get here, then we have no option other than to recurse.
		for (int i = 0; i < thisTransitions.Length; ++i)
		{
			if (!thisTransitions[i].NextState.Equals(otherTransitions[i].NextState)) { return false; }
		}

		return true;
	}
	/// <inheritdoc/>
	public readonly override bool Equals([NotNullWhen(true)] object? obj)
		=> obj is State other && this.Equals(other);
	/// <inheritdoc/>
	public readonly override int GetHashCode() => this.hashCode;
	/// <summary>
	/// <para>
	/// It is guaranteed that if 
	/// state <i>a</i> depends on state <i>b</i>
	/// then state <i>a</i> is less than state <i>b</i>.
	/// </para>
	/// <inheritdoc/>
	/// </summary>
	/// <inheritdoc/>
	public readonly int CompareTo(State other)
	{
		int comparison;

		// NB reversed (because *longest* is most dependent).
		comparison = other.MaxLength.CompareTo(this.MaxLength);
		if (comparison != 0) { return comparison; }

		// The above comparison is sufficient to achieve the ordering by dependence guarantee.
		// The remaining comparisons are designed
		// (a) to ensure a unique sorting order, and
		// (b) to keep the calculations local to these two states to avoid combinatoric blow up.

		// NB reversed -- consistent with MaxLength comparison..
		comparison = other.MinLength.CompareTo(this.MinLength);
		if (comparison != 0) { return comparison; }

		var thisTransitions = this.transitions;
		var otherTransitions = other.transitions;

		if (thisTransitions is null) { return otherTransitions is null ? 0 : +1; }
		if (otherTransitions is null) { return -1; }

		comparison = thisTransitions.Length.CompareTo(otherTransitions.Length);
		if (comparison != 0) { return comparison; }

		// Comparing char sets is local, so do this first.
		for (int i = 0; i < thisTransitions.Length; ++i)
		{
			comparison = thisTransitions[i].CharSet.CompareTo(otherTransitions[i].CharSet);
			if (comparison != 0) { return comparison; }
		}

		// If we get here, then we have no option other than to recurse.
		for (int i = 0; i < thisTransitions.Length; ++i)
		{
			comparison = thisTransitions[i].NextState.CompareTo(otherTransitions[i].NextState);
			if (comparison != 0) { return comparison; }
		}

		return 0;
	}
	#endregion
	#region Internal properties and methods
#if DEBUG
	internal string DebuggerDisplay
	{
		get
		{
			var transitions = this.transitions;
			if (transitions is null) { return "∅"; }

			var sb = new StringBuilder();
			sb.Append("( ");
			foreach (var transition in transitions)
			{
				sb.Append(transition.DebuggerDisplay);
				sb.Append(", ");
			}
			sb.Length -= 2;
			sb.Append(" )");
			return sb.ToString();
		}
	}
#endif
	#endregion
	#region Public operators and conversions
	/// <summary>
	/// Implements the equality operator.
	/// </summary>
	/// <param name="left">The left argument.</param>
	/// <param name="right">The right argument.</param>
	/// <returns>Whether the two arguments are equal.</returns>
	public static bool operator ==(State left, State right) => left.Equals(right);

	/// <summary>
	/// Implements the inequality operator.
	/// </summary>
	/// <param name="left">The left argument.</param>
	/// <param name="right">The right argument.</param>
	/// <returns>Whether the two arguments are not equal.</returns>
	public static bool operator !=(State left, State right) => !left.Equals(right);
	/// <summary>
	/// Implements the addition operator such that an <see cref="AsciiCharSet"/> plus a <see cref="State"/> generates a transition.
	/// </summary>
	/// <param name="charSet">The char set to enter this transition.</param>
	/// <param name="nextState">The state this transition maps to.</param>
	/// <returns>Whether the two arguments are not equal.</returns>
	public static Transition operator +(AsciiCharSet charSet, State nextState) => new Transition(charSet, nextState);
	#endregion
	#region Public static data and constants
	/// <summary>
	/// Definitive end of text state.
	/// The only transition from this state is end of text.
	/// </summary>
	public static readonly State DefinitiveEndOfText = new([default], transitionsHaveBeenValidated: true);
	#endregion
	#region Public static properties and methods
	/// <summary>
	/// Checks whether <paramref name="transitions"/> meets the requirements for a 
	/// non-null <see cref="State"/>, which are:
	/// <list type="number">
	/// <item>There must be at least one transition.</item>
	/// <item>
	/// A maximum of one transition can have an empty character set 
	/// (to represent the end of text). For that transition, the target state must be 'null'.
	/// </item>
	/// <item>The character sets for different transitions must not intersect.</item>
	/// <item>The transitions must be sorted in the same order as <see cref="Transition.CharSet"/>.</item>
	/// <item>
	/// The states mapped to should be different for each transition. 
	/// </item>
	/// </list>
	/// </summary>
	/// <param name="transitions">The transitions to check.</param>
	/// <param name="errorMessage">The error message if this method returns <see langword="false"/>.</param>
	/// <returns>
	/// Whether <paramref name="transitions"/> is valid.
	/// </returns>
	public static bool TransitionsAreValid(Transition[] transitions, [NotNullWhen(false)] out string? errorMessage)
	{
		// There must be at least one transition.
		if (transitions is null || transitions.Length < 1)
		{
			errorMessage = "There must be at least one transition.";
			return false;
		}

		bool seenEmptyCharSet = false;
		for (int i = 0; i < transitions.Length; ++i)
		{
			var transition_i = transitions[i];

			// A maximum of one transition can have an empty character set 
			// (to represent the end of text). For that transition, the target state must be 'null'.

			if (transition_i.CharSet.Count == 0)
			{
				if (seenEmptyCharSet)
				{
					errorMessage = "More than one transition has an empty character set (representing the end of text).";
					return false;
				}
				seenEmptyCharSet = true;
			}

			for (int k = i + 1; k < transitions.Length; ++k)
			{
				var transition_k = transitions[k];

				// The character sets for different transitions must not intersect.
				if ((transition_i.CharSet & transition_k.CharSet).Count != 0)
				{
					errorMessage = "The character sets for different transitions must not intersect.";
					return false;
				}

				// The ordering of the transitions must be the ordering of their char sets.
				if (transition_i.CharSet.CompareTo(transition_k.CharSet) > 0)
				{
					errorMessage = "Transitions are in the wrong order – transitions must be ordered by their char sets.";
					return false;
				}

				// The states mapped to should be different for each transition. 
				if (transition_i.NextState.Equals(transition_k.NextState))
				{
					errorMessage = "The states mapped to should be different for each transition.";
					return false;
				}
			}
		}

		errorMessage = null;
		return true;
	}
	/// <summary>
	/// Validates whether <paramref name="transitions"/> meets the requirements for a 
	/// non-null <see cref="State"/>, which are:
	/// <list type="number">
	/// <item>There must be at least one transition.</item>
	/// <item>
	/// A maximum of one transition can have an empty character set 
	/// (to represent the end of text). For that transition, the target state must be 'null'.
	/// </item>
	/// <item>The character sets for different transitions must not intersect.</item>
	/// <item>The transitions must be sorted in the same order as <see cref="Transition.CharSet"/>.</item>
	/// <item>
	/// The states mapped to should be different for each transition. 
	/// </item>
	/// </list>
	/// </summary>
	/// <param name="transitions">The transitions to check.</param>
	public static void ValidateTransitions(Transition[] transitions)
	{
		if (!TransitionsAreValid(transitions, out var errorMessage))
		{
			throw new ArgumentOutOfRangeException(nameof(transitions), errorMessage);
		}
	}
	#endregion
	#region Private static properties and methods
	[DoesNotReturn]
	static void ThrowInvalidOperation()
	{
		throw new InvalidOperationException("This method cannot be called on an uninitialised state.");
	}
	#endregion
}