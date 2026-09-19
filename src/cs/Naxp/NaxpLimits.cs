// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;

namespace LogMu;

/// <summary>
/// The figures W6 fixes, and W5's ceiling on an encoded value.
/// </summary>
/// <remarks>
/// <para>
/// W6 exists because W5 does not bound the work. W5 caps the largest encoded value, and a naxp
/// can be enormous while producing very few of them: <c>(A{99}){99}</c> is eleven characters of
/// pattern denoting a single string of 9 801 characters, whose minimal machine has 9 802 states
/// and exactly one encoded value.
/// </para>
/// <para>
/// There is one number, with the others derived from it, so they cannot drift apart. A string
/// of <i>n</i> characters forces a machine of at least <i>n</i> + 1 states, so the longest
/// string a naxp satisfying W6 can generate is one shorter than the budget.
/// </para>
/// <para>
/// These are figures of the language rather than of this implementation, which is why W6
/// states them. Two implementations disagreeing here would disagree about which naxps are
/// valid, and the value of a naxp is that it means the same thing everywhere.
/// </para>
/// <para>
/// The figures are set from measurement. Building a machine costs about five microseconds per
/// state, near enough linearly, so a state budget is a time budget. A naxp holding a unified
/// element pays it up to four times over - the canonical machine, the accepted machine, the W3
/// square and the machine that canonicalises - so the worst a compilation can cost is roughly
/// four times the budget. That is what these numbers are chosen against, because naxp.org will
/// compile naxps supplied by strangers and a fault costs as much as an acceptance.
/// </para>
/// </remarks>
static class NaxpLimits
{
	/// <summary>
	/// The most states a naxp's machine may have, which is W6.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Nothing a naxp is for comes near this. The five examples on naxp.org are between seven and
	/// forty six states, so this leaves more than forty times over.
	/// </para>
	/// <para>
	/// What sets the floor is the grammar rather than any use. An interval count may have two
	/// digits, so the largest a single interval can expand to is <c>A{99}</c> at a hundred and one
	/// states, and this admits that twenty times over. What W6 rules out is nesting, which
	/// multiplies: <c>(A{99}){99}</c> wants 9 802 states and is the shape a hostile naxp takes.
	/// </para>
	/// <para>
	/// The two digit cap on an interval count is what keeps that nesting small enough for a
	/// figure this low; with four digit counts it would have had to be 100 000.
	/// </para>
	/// </remarks>
	public const int MaxStates = 2_000;

	/// <summary>
	/// The longest string a naxp satisfying W6 can generate.
	/// </summary>
	/// <remarks>
	/// Derived rather than chosen, and a consequence of W6 rather than a rule beside it. A
	/// machine of <see cref="MaxStates"/> states has a longest path of one fewer, so a naxp
	/// generating a longer string than this breaks the state budget in any case.
	/// </remarks>
	public const int MaxStringLength = MaxStates - 1;

	/// <summary>
	/// The most states the canonicalisation machine may have, which W6 also fixes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Chosen, not derived, and far smaller than <see cref="MaxStates"/>, because this machine
	/// grows on a different axis. Its size can be <b>exponential in the length of the naxp</b>
	/// while both language machines stay tiny: <c>[ab]{16}c|([ab]!a){16}d</c> has an eighteen
	/// state acceptor and would need 131 072 states here, since nothing before the final
	/// character says which branch was taken and every character read has to be remembered until
	/// it does. See <c>encoding/transducer-determinisation.md</c>.
	/// </para>
	/// <para>
	/// W5 does not catch this. It caps encoded values, and the unified branch above
	/// contributes one encoded value for 65 536 strings, so the whole naxp has 65 537 of them
	/// and passes.
	/// </para>
	/// <para>
	/// It is nonetheless the same figure as <see cref="MaxStates"/> rather than a separate one.
	/// A second number would buy one step - the family above would be invalid at width ten
	/// rather than width eleven - and cost a second thing to reason about. What actually bounds
	/// a whole compilation is a budget shared across its phases, which this is not; see the
	/// remark on this class.
	/// </para>
	/// </remarks>
	public const int MaxCanonicalStates = MaxStates;

	/// <summary>
	/// The largest encoded value the encoding can produce, which is W5's limit of 2^64 - 1.
	/// </summary>
	/// <remarks>
	/// The limit is the full width of the accumulator, so a count that reaches it cannot be told
	/// from one that overflowed by its value alone. <see cref="StateMap.CountSaturated"/> carries
	/// that apart.
	/// </remarks>
	public const ulong MaxEncodedValue = ulong.MaxValue;
}
