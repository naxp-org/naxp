// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace LogMu;

/// <summary>
/// The well-formedness rules that need the finished tree.
/// </summary>
/// <remarks>
/// <para>
/// W4 is decided by <see cref="Parser"/>, where the tokens are read. W3 needs the
/// single-valuedness of a transduction and W5 needs the size of the canonical language, so
/// both wait on the state map; neither is checked here and a naxp that breaks one is currently
/// accepted.
/// </para>
/// <para>
/// W1 asks whether a rendering is one of the strings its subject generates, which is
/// <see cref="Matcher"/>'s business rather than this class's.
/// </para>
/// </remarks>
static class WellFormedness
{
	#region Public entry point
	/// <summary>
	/// Checks the rules that can be decided from the tree, which is W2 then W1.
	/// </summary>
	/// <param name="ast">The tree, as returned by <see cref="Parser.TryParse"/>.</param>
	/// <param name="error">The refusal, or <see langword="null"/> if the tree passes.</param>
	/// <returns>Whether the tree passes.</returns>
	public static bool TryCheck(Ast ast, out NaxpError? error)
	{
		if (ast is null) { throw new ArgumentNullException(nameof(ast)); }

		// W2 first, because W1 reads inside both operands of a '!' and the answer is only
		// meaningful once nothing is hidden in there.
		if (!TryCheckW2(ast, out error)) { return false; }

		return TryCheckW1(ast, out error);
	}
	#endregion
	#region W2: '!' may not nest
	static bool TryCheckW2(Ast node, out NaxpError? error)
	{
		if (node is AstReplaceable replaceable)
		{
			if (Ast.ContainsReplaceable(replaceable.Subject) || Ast.ContainsReplaceable(replaceable.Rendering))
			{
				error = new NaxpError(NaxpMessage.NAXP1040_ReplaceableNested);
				return false;
			}
		}

		foreach (Ast child in Children(node))
		{
			if (!TryCheckW2(child, out error)) { return false; }
		}

		error = null;
		return true;
	}
	#endregion
	#region W1: a rendering must be one of the strings it replaces
	static bool TryCheckW1(Ast node, out NaxpError? error)
	{
		if (node is AstReplaceable replaceable)
		{
			SingleStringOutcome outcome = Matcher.TryGetSingleString(replaceable.Rendering, out string? rendering);

			if (outcome == SingleStringOutcome.TooLong)
			{
				error = TooLongError(node.SourceOffset);
				return false;
			}

			if (outcome == SingleStringOutcome.Multiple)
			{
				error = new NaxpError(replaceable.Form == ReplaceableForm.Reproduced ? NaxpMessage.NAXP1041_ReproducedSubjectNotSingle : NaxpMessage.NAXP1042_RenderingNotSingle);
				return false;
			}

			if (!Matcher.Generates(replaceable.Subject, rendering!, out bool tooLong))
			{
				if (tooLong)
				{
					error = TooLongError(node.SourceOffset);
					return false;
				}

				error = new NaxpError(rendering!.Length == 0 ? NaxpMessage.NAXP1043_ElementNotDeletable : NaxpMessage.NAXP1044_RenderingNotGenerated, rendering!.Length == 0 ? null : rendering);
				return false;
			}
		}

		foreach (Ast child in Children(node))
		{
			if (!TryCheckW1(child, out error)) { return false; }
		}

		error = null;
		return true;
	}

	static NaxpError TooLongError(int offset)
		=> new NaxpError(NaxpMessage.NAXP1048_ElementTooLong);
	#endregion
	#region Tree walking
	static IEnumerable<Ast> Children(Ast node)
	{
		switch (node)
		{
			case AstSequence sequence:
				return sequence.Children;

			case AstAlternation alternation:
				return alternation.Children;

			case AstOptional optional:
				return new[] { optional.Child };

			case AstInterval interval:
				return new[] { interval.Child };

			case AstReplaceable replaceable:
				return new[] { replaceable.Subject, replaceable.Rendering };

			default:
				return Array.Empty<Ast>();
		}
	}
	#endregion
}
