// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;

namespace LogMu;

/// <summary>
/// The rewrite a case fold stands for, written <c>\C</c> for upper case canonical and
/// <c>\c</c> for lower.
/// </summary>
/// <remarks>
/// <para>
/// A fold is shorthand and nothing downstream of the parser sees one. <c>\CA</c> becomes
/// <c>[Aa]!A</c> and <c>\C[A-F]</c> becomes the six way alternation the specification writes out,
/// so the language, the well-formedness rules and the encoding all apply to the expansion and none
/// of them needs a case for folding. W2 falls out of this: a fold inside the operand of a <c>!</c>
/// puts a <c>!</c> there, which the existing nesting check refuses.
/// </para>
/// <para>
/// The expansion grows with the alphabet rather than with the pattern, <c>\C\a</c> being twenty six
/// alternatives. That is affordable because the branches are disjoint on their first character, so
/// the machines built from them merge back into one state with a wider transition table. Carrying
/// the fold on the character set instead would keep the tree small at the cost of a case in
/// <see cref="Tx"/> that emits a function of the character read; the specification defines the
/// alternation and this follows it.
/// </para>
/// <para>
/// Every node made here takes the pattern offset of the node it replaces, so a fault found in an
/// expansion still points at what the author wrote.
/// </para>
/// </remarks>
static class Folding
{
	#region Public entry point
	/// <summary>
	/// Expands a fold over the element it binds to.
	/// </summary>
	/// <param name="node">The element, already parsed.</param>
	/// <param name="toUpper">Whether upper case is canonical, which is <c>\C</c>.</param>
	/// <returns>The element with the fold expanded, or the element itself where nothing has case.</returns>
	public static Ast Apply(Ast node, bool toUpper)
	{
		if (node is null) { throw new ArgumentNullException(nameof(node)); }

		switch (node)
		{
			case AstChars chars:
				return ApplyToChars(chars, toUpper);

			case AstSequence sequence:
				return new AstSequence(ApplyToEach(sequence.Children, toUpper)) { PatternOffset = sequence.PatternOffset };

			case AstAlternation alternation:
				return new AstAlternation(ApplyToEach(alternation.Children, toUpper)) { PatternOffset = alternation.PatternOffset };

			case AstOptional optional:
				return new AstOptional(Apply(optional.Child, toUpper)) { PatternOffset = optional.PatternOffset };

			case AstInterval interval:
				return new AstInterval(Apply(interval.Child, toUpper), interval.MinCount, interval.MaxCount) { PatternOffset = interval.PatternOffset };

			case AstUnified unified:
				return ApplyToUnified(unified, toUpper);

			default:
				// The empty string and a decimal range have no character to fold.
				return node;
		}
	}
	#endregion
	#region Character sets
	/// <summary>
	/// Expands a fold over one character set, which is where a fold does its work.
	/// </summary>
	/// <remarks>
	/// The characters with no case stay in a set of their own and are matched as they were. Each
	/// letter present in either case becomes one unified element accepting the pair and rendering
	/// the canonical one, so <c>\C[\9A-F]</c> is <c>[\9]</c> beside six of them.
	/// </remarks>
	static Ast ApplyToChars(AstChars chars, bool toUpper)
	{
		AsciiCharSet uncased = AsciiCharSet.Empty;
		AsciiCharSet canonicalLetters = AsciiCharSet.Empty;

		foreach (char c in chars.CharSet)
		{
			if (IsCased(c))
			{
				canonicalLetters |= AsciiCharSet.FromSingleChar(Canonical(c, toUpper));
			}
			else
			{
				uncased |= AsciiCharSet.FromSingleChar(c);
			}
		}

		// A fold over characters with no case is not an error, it just has nothing to do.
		if (canonicalLetters.IsEmpty) { return chars; }

		int offset = chars.PatternOffset;
		var branches = new List<Ast>();

		if (!uncased.IsEmpty)
		{
			branches.Add(new AstChars(uncased) { PatternOffset = offset });
		}

		foreach (char c in canonicalLetters)
		{
			AsciiCharSet pair = AsciiCharSet.FromSingleChar(c) | AsciiCharSet.FromSingleChar(OtherCase(c));

			branches.Add(new AstUnified(
				new AstChars(pair) { PatternOffset = offset },
				new AstChars(AsciiCharSet.FromSingleChar(c)) { PatternOffset = offset },
				UnifiedForm.Fold)
			{
				PatternOffset = offset,
			});
		}

		return branches.Count == 1
			? branches[0]
			: new AstAlternation(branches) { PatternOffset = offset }
			;
	}
	#endregion
	#region Unified elements
	/// <summary>
	/// Folds a unified element, which widens its subject and canonicalises its rendering.
	/// </summary>
	/// <remarks>
	/// No <c>!</c> is added. Which of the strings the subject accepts was matched is unencoded
	/// already, so there is nothing there for a fold to unify; all that is wanted is that the
	/// subject accept both cases and that the rendering come out in the canonical one. W1 then
	/// holds without being checked, the rendering being a case variant of a string the subject
	/// generated and the widened subject generating every case variant of what it generated before.
	/// </remarks>
	static Ast ApplyToUnified(AstUnified unified, bool toUpper)
	{
		// A fold already expanded within the extent is treated like any other unified element: its
		// subject is widened again, which changes nothing, and its rendering takes the outer fold's
		// case. The outer fold governs.
		return new AstUnified(
			MapCharSets(unified.Subject, WidenSet),
			MapCharSets(unified.Rendering, set => CanonicaliseSet(set, toUpper)),
			unified.Form)
		{
			PatternOffset = unified.PatternOffset,
		};
	}
	#endregion
	#region Tree walking
	static IReadOnlyList<Ast> ApplyToEach(IReadOnlyList<Ast> children, bool toUpper)
	{
		var folded = new Ast[children.Count];

		for (int i = 0; i < children.Count; ++i)
		{
			folded[i] = Apply(children[i], toUpper);
		}

		return folded;
	}

	/// <summary>
	/// Rebuilds a subtree with every character set passed through <paramref name="map"/>.
	/// </summary>
	/// <remarks>
	/// The <c>x!!</c> form shares one subtree between its subject and its rendering. Rebuilding
	/// rather than mutating is what allows the two to be mapped differently, which is exactly what
	/// folding one of them does.
	/// </remarks>
	static Ast MapCharSets(Ast node, Func<AsciiCharSet, AsciiCharSet> map)
	{
		switch (node)
		{
			case AstChars chars:
				return new AstChars(map(chars.CharSet)) { PatternOffset = chars.PatternOffset };

			case AstSequence sequence:
				return new AstSequence(MapEach(sequence.Children, map)) { PatternOffset = sequence.PatternOffset };

			case AstAlternation alternation:
				return new AstAlternation(MapEach(alternation.Children, map)) { PatternOffset = alternation.PatternOffset };

			case AstOptional optional:
				return new AstOptional(MapCharSets(optional.Child, map)) { PatternOffset = optional.PatternOffset };

			case AstInterval interval:
				return new AstInterval(MapCharSets(interval.Child, map), interval.MinCount, interval.MaxCount) { PatternOffset = interval.PatternOffset };

			case AstUnified unified:
				// Nesting is W2's to refuse, and it reads a tree this has already been through.
				return new AstUnified(
					MapCharSets(unified.Subject, map),
					MapCharSets(unified.Rendering, map),
					unified.Form)
				{
					PatternOffset = unified.PatternOffset,
				};

			default:
				return node;
		}
	}

	static IReadOnlyList<Ast> MapEach(IReadOnlyList<Ast> children, Func<AsciiCharSet, AsciiCharSet> map)
	{
		var mapped = new Ast[children.Count];

		for (int i = 0; i < children.Count; ++i)
		{
			mapped[i] = MapCharSets(children[i], map);
		}

		return mapped;
	}
	#endregion
	#region Case
	/// <summary>Adds the other case of every cased character, leaving the rest alone.</summary>
	static AsciiCharSet WidenSet(AsciiCharSet set)
	{
		AsciiCharSet widened = set;

		foreach (char c in set)
		{
			if (IsCased(c)) { widened |= AsciiCharSet.FromSingleChar(OtherCase(c)); }
		}

		return widened;
	}

	/// <summary>Replaces every cased character by its canonical case, which may shrink the set.</summary>
	static AsciiCharSet CanonicaliseSet(AsciiCharSet set, bool toUpper)
	{
		AsciiCharSet canonical = AsciiCharSet.Empty;

		foreach (char c in set)
		{
			canonical |= AsciiCharSet.FromSingleChar(Canonical(c, toUpper));
		}

		return canonical;
	}

	static bool IsCased(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

	static char Canonical(char c, bool toUpper) => toUpper ? ToUpper(c) : ToLower(c);

	static char OtherCase(char c)
	{
		if (c >= 'A' && c <= 'Z') { return (char)(c + 32); }
		if (c >= 'a' && c <= 'z') { return (char)(c - 32); }

		return c;
	}

	static char ToUpper(char c) => c >= 'a' && c <= 'z' ? (char)(c - 32) : c;

	static char ToLower(char c) => c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
	#endregion
}
