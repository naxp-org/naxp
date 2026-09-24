// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;

namespace LogMu;

/// <summary>
/// A naxp that has been parsed, checked and turned into machines.
/// </summary>
sealed class Compilation
{
	internal Compilation(
		string pattern,
		Ast ast,
		StateMap accepted,
		StateMap canonical,
		bool canonicalIsIdentity,
		TxMachine? canonicalMachine)
	{
		this.Pattern = pattern;
		this.Ast = ast;
		this.Accepted = accepted;
		this.Canonical = canonical;
		this.CanonicalIsIdentity = canonicalIsIdentity;
		this.CanonicalMachine = canonicalMachine;
		this.MaxLength = LongestPath(canonical);
	}

	public string Pattern { get; }

	public Ast Ast { get; }

	/// <summary>The machine for the accepted language <i>L</i>.</summary>
	public StateMap Accepted { get; }

	/// <summary>The machine for the canonical language <i>C</i>, which the encoding ranks over.</summary>
	public StateMap Canonical { get; }

	/// <summary>The largest encoded value, which is the size of <i>C</i>.</summary>
	public ulong MaxEncodedValue => this.Canonical.StringCount;

	/// <summary>The count of strings the naxp accepts, which is the size of <i>L</i>.</summary>
	public ulong AcceptedCount => this.Accepted.StringCount;

	/// <summary>The length of the longest string in <i>C</i>, which bounds every buffer a decode needs.</summary>
	public int MaxLength { get; }

	/// <summary>
	/// Whether &#961; is the identity, so that every accepted string is its own canonical form.
	/// </summary>
	/// <remarks>
	/// True exactly when the tree holds no unified element, since that is the only thing
	/// that makes the canonical form differ from the input. Then <i>C</i> and <i>L</i> are the
	/// same language and encoding is a walk of the machine, with no canonicalisation and nothing
	/// allocated.
	/// </remarks>
	public bool CanonicalIsIdentity { get; }

	/// <summary>
	/// The machine that canonicalises, or <see langword="null"/> where &#961; is the identity and
	/// there is nothing to canonicalise.
	/// </summary>
	/// <remarks>
	/// Non-null exactly when <see cref="CanonicalIsIdentity"/> is false. <see cref="Compiler"/>
	/// builds it while compiling, and the naxp is invalid where it will not fit
	/// <see cref="NaxpLimits.MaxCanonicalStates"/>, so a compilation that succeeded always has
	/// one when it needs one.
	/// </remarks>
	public TxMachine? CanonicalMachine { get; }

	/// <summary>
	/// Whether the naxp accepts the specified string.
	/// </summary>
	/// <remarks>
	/// This walks the machine for <i>L</i>, which is one transition per character.
	/// <see cref="Encode"/> answers the same question, but where the naxp has a unified
	/// element it canonicalises first and then ranks, so it is two walks rather than one and the
	/// wrong way round to ask it.
	/// </remarks>
	/// <param name="text">The string to test.</param>
	/// <returns>Whether the naxp accepts it.</returns>
	public bool Accepts(ReadOnlySpan<char> text) => this.Accepted.Accepts(text);

	/// <summary>
	/// The encoded value of a string, which is zero exactly when the string is invalid.
	/// </summary>
	/// <remarks>
	/// Encoding cannot fail. Every rule is decided when the naxp is compiled, W3 among them, so
	/// the string either has one value or is not in the language.
	/// </remarks>
	/// <param name="text">The string to encode.</param>
	/// <returns>The value, from 1 to <see cref="MaxEncodedValue"/>, or zero.</returns>
	public ulong Encode(ReadOnlySpan<char> text)
	{
		if (this.CanonicalIsIdentity) { return Codec.Encode(this.Canonical, text); }

		Span<char> canonical = stackalloc char[this.MaxLength];
		int length = this.CanonicalMachine!.Canonicalise(text, canonical);

		return length < 0 ? 0UL : Codec.Encode(this.Canonical, canonical.Slice(0, length));
	}

	/// <summary>
	/// The string a value stands for, which is a canonical form.
	/// </summary>
	/// <param name="value">The value, from 1 to <see cref="MaxEncodedValue"/>.</param>
	/// <param name="text">The string, or <see langword="null"/> if the value is out of range.</param>
	/// <returns>Whether the value is one this naxp can produce.</returns>
	public bool TryDecode(ulong value, out string? text)
	{
		Span<char> buffer = stackalloc char[this.MaxLength];
		int length = this.Decode(value, buffer);

		text = length < 0 ? null : buffer.Slice(0, length).ToString();

		return text is not null;
	}

	/// <summary>
	/// Writes the string a value stands for, which is a canonical form, into a buffer.
	/// </summary>
	/// <param name="value">The value, from 1 to <see cref="MaxEncodedValue"/>.</param>
	/// <param name="destination">Where the string goes, which holds <see cref="MaxLength"/> characters.</param>
	/// <returns>The length of the string, or -1 if the value is out of range.</returns>
	public int Decode(ulong value, Span<char> destination) => Codec.Decode(this.Canonical, value, destination);

	/// <summary>
	/// The canonical form of a string, which is the string with the match of each unified
	/// element replaced by that element's rendering.
	/// </summary>
	/// <param name="text">The string.</param>
	/// <param name="canonical">
	/// The canonical form, or <see langword="null"/> if the string is invalid.
	/// </param>
	/// <returns>Whether the naxp accepts the string.</returns>
	public bool TryGetCanonicalForm(ReadOnlySpan<char> text, out string? canonical)
	{
		Span<char> buffer = stackalloc char[this.MaxLength];
		int length = this.Canonicalise(text, buffer);

		canonical = length < 0 ? null : buffer.Slice(0, length).ToString();

		return canonical is not null;
	}

	/// <summary>
	/// Writes the canonical form of a string into a buffer.
	/// </summary>
	/// <param name="text">The string.</param>
	/// <param name="destination">Where the canonical form goes, which holds <see cref="MaxLength"/> characters.</param>
	/// <returns>The length of the canonical form, or -1 if the string is invalid.</returns>
	public int Canonicalise(ReadOnlySpan<char> text, Span<char> destination)
	{
		// Where ρ is the identity an accepted string is its own canonical form, so the answer
		// is the machine's, and being in the canonical language it fits the buffer.
		if (this.CanonicalIsIdentity)
		{
			if (!this.Accepts(text)) { return -1; }

			text.CopyTo(destination);

			return text.Length;
		}

		// Both walk the same relation and agree everywhere, which the tests check exhaustively.
		// The machine is linear in the length of the input where the tree walk is not, and it is
		// the form the emitters need, so it is the one the runtime uses. Canonicaliser stays as
		// the reference the machine is tested against.
		return this.CanonicalMachine!.Canonicalise(text, destination);
	}

	/// <summary>
	/// The length of the longest string a machine generates.
	/// </summary>
	/// <remarks>
	/// The states are listed in creation order and every transition points at an earlier state,
	/// because the builder interns each state's successors before the state itself, so a single
	/// pass has every target's length ready when it is read.
	/// </remarks>
	static int LongestPath(StateMap map)
	{
		var lengths = new int[map.States.Count];

		for (int id = 0; id < map.States.Count; ++id)
		{
			int longest = 0;

			foreach (Transition transition in map.States[id].Transitions)
			{
				if (transition.Set.IsEmpty) { continue; }

				int viaTransition = lengths[transition.Next.Id] + 1;

				if (viaTransition > longest) { longest = viaTransition; }
			}

			lengths[id] = longest;
		}

		return lengths[map.Start.Id];
	}
}

/// <summary>
/// Parses a naxp, checks it and builds its machines.
/// </summary>
/// <remarks>
/// Every rule is checked here or below: W4 in <see cref="Parser"/>, W2 and W1 in
/// <see cref="WellFormedness"/>, W3 in <see cref="W3Checker"/> and W5 from the size of the
/// canonical language. A compilation that succeeds is a well-formed naxp.
/// </remarks>
static class Compiler
{
	public static bool TryCompile(ReadOnlySpan<char> text, out Compilation? compilation, out NaxpError? error)
	{
		compilation = null;

		if (!Parser.TryParse(text, out Ast? ast, out error)) { return false; }
		if (!WellFormedness.TryCheck(ast!, out error)) { return false; }

		// One factory across both languages and the W3 check, so the shared sub-expressions and
		// their derivatives are computed once.
		var factory = new RxFactory();

		// Everything below turns on this, so the tree is walked for it once.
		bool hasUnified = Ast.ContainsUnified(ast!);

		// The transduction is wanted twice, by the W3 check and then by the machine that
		// canonicalises, so it is converted once and both are given it.
		TxFactory? txFactory = null;
		Tx? txRoot = null;

		if (hasUnified)
		{
			txFactory = new TxFactory(factory);
			txRoot = TxConverter.Convert(ast!, txFactory, factory);

			// Before the machines, because a naxp that breaks W3 has no well defined encoding and
			// building its machines would say nothing about that.
			if (!W3Checker.TryCheck(txRoot, txFactory, out error)) { return false; }
		}

		// A unified element is the only node RxConverter reads the language at, so without one
		// the two conversions would give the same expression and the same machine.
		bool canonicalIsIdentity = !hasUnified;

		Rx canonicalExpression = RxConverter.Convert(ast!, factory, isCanonical: true);
		if (!StateMapBuilder.TryBuild(canonicalExpression, factory, out StateMap? canonical, out error)) { return false; }

		if (canonical!.CountSaturated)
		{
			error = new NaxpError(NaxpMessage.NAXP1046_TooManyValues);
			return false;
		}

		// The accepted language can legitimately be larger than the canonical one, and W5 says
		// nothing about it, so its count is allowed to saturate.
		StateMap? accepted;

		if (canonicalIsIdentity)
		{
			accepted = canonical;
		}
		else
		{
			Rx acceptedExpression = RxConverter.Convert(ast!, factory, isCanonical: false);
			if (!StateMapBuilder.TryBuild(acceptedExpression, factory, out accepted, out error)) { return false; }
		}

		// Last, because it is the part of W6 a naxp can fail after passing every other rule, and
		// the cheaper checks should come first; see NaxpLimits.MaxCanonicalStates.
		TxMachine? canonicalMachine = null;

		if (hasUnified
			&& !TxMachineBuilder.TryBuild(
				txRoot!,
				txFactory!,
				out canonicalMachine,
				out error,
				NaxpLimits.MaxCanonicalStates))
		{
			return false;
		}

		compilation = new Compilation(
			text.ToString(),
			ast!,
			accepted!,
			canonical,
			canonicalIsIdentity,
			canonicalMachine);

		error = null;
		return true;
	}
}
