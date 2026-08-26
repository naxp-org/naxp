// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;

namespace LogMu;

/// <summary>
/// A node in the abstract syntax tree of a naxp.
/// </summary>
/// <remarks>
/// <para>
/// The tree keeps the structure the source was written in. Intervals and digits ranges are
/// deliberately <em>not</em> expanded here. The cap on an interval count exists so
/// that an implementation can reject a naxp before expanding it, and expanding at parse time
/// would throw that away: <c>(A{99}){99}</c> is eleven characters of source and nearly ten
/// thousand characters of expansion.
/// </para>
/// <para>
/// Groups do not survive parsing. <c>(A)</c> and <c>A</c> give the same tree, and the two
/// abbreviated replaceable forms are expanded into the general one, since version 0.4 defines
/// them structurally rather than textually.
/// </para>
/// </remarks>
abstract class Ast
{
	/// <summary>
	/// The offset in the source at which this node starts. Diagnostics only.
	/// </summary>
	public int SourceOffset { get; set; }

	/// <summary>
	/// Whether the tree holds a replaceable element anywhere.
	/// </summary>
	/// <remarks>
	/// This decides two things at once, which is why it lives here rather than with either of
	/// them. Without a replaceable element &#961; is the identity, so W3 holds for nothing and the
	/// canonical language is the accepted one.
	/// </remarks>
	/// <param name="node">The node to search from.</param>
	/// <returns>Whether one was found.</returns>
	public static bool ContainsReplaceable(Ast node)
	{
		switch (node)
		{
			case AstReplaceable:
				return true;

			case AstSequence sequence:
				foreach (Ast child in sequence.Children)
				{
					if (ContainsReplaceable(child)) { return true; }
				}

				return false;

			case AstAlternation alternation:
				foreach (Ast child in alternation.Children)
				{
					if (ContainsReplaceable(child)) { return true; }
				}

				return false;

			case AstOptional optional:
				return ContainsReplaceable(optional.Child);

			case AstInterval interval:
				return ContainsReplaceable(interval.Child);

			default:
				return false;
		}
	}
}

/// <summary>The empty string, written <c>()</c>.</summary>
sealed class AstEmpty : Ast
{
}

/// <summary>A set of characters matching one position, such as <c>A</c>, <c>\9</c> or <c>[A-F]</c>.</summary>
sealed class AstChars : Ast
{
	public AstChars(AsciiCharSet charSet)
	{
		this.CharSet = charSet;
	}

	public AsciiCharSet CharSet { get; }
}

/// <summary>A digits range, written <c>#[</c><i>lo</i><c>-</c><i>hi</i><c>]</c>.</summary>
/// <remarks>
/// The digit counts are the counts <em>as written</em>, which is what fixes the widths
/// generated: <c>#[00-105]</c> does not match <c>7</c> while <c>#[0-105]</c> does.
/// </remarks>
sealed class AstDigitsRange : Ast
{
	public AstDigitsRange(ulong low, int lowDigitCount, ulong high, int highDigitCount)
	{
		this.Low = low;
		this.LowDigitCount = lowDigitCount;
		this.High = high;
		this.HighDigitCount = highDigitCount;
	}

	public ulong Low { get; }
	public int LowDigitCount { get; }
	public ulong High { get; }
	public int HighDigitCount { get; }
}

/// <summary>Two or more elements in sequence.</summary>
sealed class AstSequence : Ast
{
	public AstSequence(IReadOnlyList<Ast> children)
	{
		this.Children = children;
	}

	public IReadOnlyList<Ast> Children { get; }
}

/// <summary>Two or more alternatives separated by <c>|</c>.</summary>
sealed class AstAlternation : Ast
{
	public AstAlternation(IReadOnlyList<Ast> children)
	{
		this.Children = children;
	}

	public IReadOnlyList<Ast> Children { get; }
}

/// <summary>An optional element, written <c>x?</c>.</summary>
sealed class AstOptional : Ast
{
	public AstOptional(Ast child)
	{
		this.Child = child;
	}

	public Ast Child { get; }
}

/// <summary>A bounded interval, written <c>x{n}</c> or <c>x{m,n}</c>.</summary>
sealed class AstInterval : Ast
{
	public AstInterval(Ast child, int minCount, int maxCount)
	{
		this.Child = child;
		this.MinCount = minCount;
		this.MaxCount = maxCount;
	}

	public Ast Child { get; }
	public int MinCount { get; }
	public int MaxCount { get; }
}

/// <summary>
/// How a replaceable element was written, which is needed only so that a well-formedness
/// message can name the form the author used rather than the form it expands to.
/// </summary>
enum ReplaceableForm
{
	/// <summary><c>x!y</c>.</summary>
	Explicit,
	/// <summary><c>x!!</c>, which expands to <c>x?!(x)</c>.</summary>
	Reproduced,
	/// <summary><c>x!?</c>, which expands to <c>x?!()</c>.</summary>
	Dropped,
}

/// <summary>
/// A replaceable element, written <c>x!y</c>. Which of the strings the subject accepts was
/// matched is not part of the encoding, and the rendering is printed in its place.
/// </summary>
/// <remarks>
/// For the two abbreviated forms the subject is the <see cref="AstOptional"/> wrapping what
/// was written, so <see cref="Subject"/> is always the expression whose choice goes unencoded.
/// The <c>x!!</c> form shares one subtree between <see cref="Subject"/> and
/// <see cref="Rendering"/>; nothing in the tree is mutated after parsing, so that is safe.
/// </remarks>
sealed class AstReplaceable : Ast
{
	public AstReplaceable(Ast subject, Ast rendering, ReplaceableForm form)
	{
		this.Subject = subject;
		this.Rendering = rendering;
		this.Form = form;
	}

	public Ast Subject { get; }
	public Ast Rendering { get; }
	public ReplaceableForm Form { get; }
}
