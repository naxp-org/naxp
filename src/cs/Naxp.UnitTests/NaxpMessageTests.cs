// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace LogMu.UnitTests;

/// <summary>
/// The messages themselves: that the table matches the enum, that every member is reachable by
/// name, and that nothing in the wording contradicts how it is used.
/// </summary>
/// <remarks>
/// Three tables have to agree - the enum, the formats beside it, and the rules in
/// <see cref="NaxpMessageRules"/> - and two of them are ordered lists that a member inserted in
/// the middle would silently shift. That is what most of this file is for.
/// </remarks>
public class NaxpMessageTests
{
	static readonly NaxpMessage[] All = (NaxpMessage[])Enum.GetValues(typeof(NaxpMessage));

	[Fact]
	public void Formats_HoldsOneEntryPerMessage()
		=> Assert.Equal(All.Length, NaxpMessages.Count);

	/// <summary>
	/// The number in a member's name is its position, so a member inserted without renumbering is
	/// caught here rather than by a message coming out under the wrong code.
	/// </summary>
	[Fact]
	public void Codes_AreNumberedByPosition()
	{
		for (int i = 0; i < All.Length; ++i)
		{
			string expected = string.Format(CultureInfo.InvariantCulture, "NAXP{0}_", 1001 + i);

			Assert.StartsWith(expected, All[i].ToString(), StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Codes_AreDistinct()
	{
		string[] codes = All.Select(message => message.ToString()).ToArray();

		Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
	}

	/// <summary>
	/// Every message says something, and nothing is left as a placeholder.
	/// </summary>
	[Fact]
	public void EveryMessage_HasText()
	{
		foreach (NaxpMessage message in All)
		{
			string text = NaxpMessages.Format(message, null);

			Assert.False(string.IsNullOrWhiteSpace(text), message.ToString());
			Assert.EndsWith(".", text.TrimEnd(), StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// A message that interpolates must be given something, and one that does not must not
	/// silently swallow it. This is what keeps the single argument honest.
	/// </summary>
	[Fact]
	public void MessagesTakingAnArgument_AreExactlyThoseThatUseIt()
	{
		List<NaxpMessage> withArgument = All.Where(TakesAnArgument).ToList();

		Assert.Equal(
			new[]
			{
				NaxpMessage.NAXP1027_RangeReversed,
				NaxpMessage.NAXP1032_EscapeUndefined,
				NaxpMessage.NAXP1033_CharacterNotAllowed,
				NaxpMessage.NAXP1038_ReservedCharacterHere,
				NaxpMessage.NAXP1039_CharacterHere,
				NaxpMessage.NAXP1044_RenderingNotGenerated,
				NaxpMessage.NAXP1046_ReplacementNotSingleValuedWitness,
			},
			withArgument);
	}

	/// <summary>
	/// A message that interpolates must survive being formatted, which a literal brace in it
	/// would prevent.
	/// </summary>
	/// <remarks>
	/// Several messages quote naxps holding braces - <c>'A{2,5}'</c> and <c>Add a '}'</c> among
	/// them - and those never reach <see cref="string.Format(IFormatProvider, string, object?)"/>
	/// because they take no argument. Giving one an argument later without doubling its braces
	/// would throw at the moment of refusal, which is the worst time to find out.
	/// </remarks>
	[Fact]
	public void EveryMessageTakingAnArgument_FormatsWithoutThrowing()
	{
		foreach (NaxpMessage message in All)
		{
			if (!TakesAnArgument(message)) { continue; }

			string text = NaxpMessages.Format(message, "WITNESS");

			Assert.Contains("WITNESS", text, StringComparison.Ordinal);
			Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// The budgets are read from <see cref="NaxpLimits"/> rather than passed in, so a message
	/// naming one must name the figure this build actually ships with.
	/// </summary>
	[Fact]
	public void MessagesNamingABudget_NameTheRealOne()
	{
		string states = NaxpLimits.MaxStates.ToString(CultureInfo.InvariantCulture);

		Assert.Contains(states, NaxpMessages.Format(NaxpMessage.NAXP1049_TooManyStates, null), StringComparison.Ordinal);
		Assert.Contains(states, NaxpMessages.Format(NaxpMessage.NAXP1051_TooManyPairStates, null), StringComparison.Ordinal);

		Assert.Contains(
			NaxpLimits.MaxCanonicalStates.ToString(CultureInfo.InvariantCulture),
			NaxpMessages.Format(NaxpMessage.NAXP1050_TooManyCanonicalStates, null),
			StringComparison.Ordinal);

		Assert.Contains(
			Matcher.MaxGeneratedLength.ToString(CultureInfo.InvariantCulture),
			NaxpMessages.Format(NaxpMessage.NAXP1048_ElementTooLong, null),
			StringComparison.Ordinal);
	}

	[Fact]
	public void EveryMessage_HasARule()
	{
		foreach (NaxpMessage message in All)
		{
			Assert.False(
				string.IsNullOrEmpty(NaxpMessageRules.RuleOf(message)),
				message.ToString());
		}

		Assert.Equal(All.Length, NaxpMessageRules.Mapped.Count);
	}

	/// <summary>
	/// A refusal that names no place in the naxp leaves both numbers at zero, which the public
	/// surface reads as the whole of it. A refusal that does name one must never do that, or it
	/// would be mistaken for the same thing.
	/// </summary>
	[Fact]
	public void ARefusalWithAPosition_IsNotMistakenForTheWholeNaxp()
	{
		Assert.True(new NaxpError(NaxpMessage.NAXP1047_TooManyValues).IsWholeNaxp);
		Assert.False(new NaxpError(NaxpMessage.NAXP1002_IntervalHyphen, offset: 0, length: 1).IsWholeNaxp);
		Assert.False(new NaxpError(NaxpMessage.NAXP1002_IntervalHyphen, offset: 3, length: 1).IsWholeNaxp);
	}

	/// <summary>
	/// The code a caller is given is the number alone, never the hint beside it.
	/// </summary>
	/// <remarks>
	/// A member is spelled <c>NAXP1002_IntervalHyphen</c> so that a line of the library says which
	/// refusal it is about at a glance. That half is a note to ourselves: it would read as a
	/// promise about wording nobody has made, so it stops at the boundary.
	/// </remarks>
	[Fact]
	public void Code_IsTheNumberAloneAndNeverTheHint()
	{
		foreach (NaxpMessage message in All)
		{
			string code = new NaxpError(message).Code;

			Assert.Matches("^NAXP[0-9]{4}$", code);
			Assert.StartsWith(code + "_", message.ToString(), StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// Nor does the hint reach a caller by way of the words, which is the other route out.
	/// </summary>
	[Fact]
	public void NoMessage_QuotesItsOwnMemberName()
	{
		foreach (NaxpMessage message in All)
		{
			var error = new NaxpError(message, TakesAnArgument(message) ? "x" : null);

			Assert.DoesNotContain(message.ToString(), error.Text, StringComparison.Ordinal);
			Assert.DoesNotContain(message.ToString(), error.ToString(), StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// Whether a message interpolates, judged by reading its format rather than by trying it.
	/// </summary>
	static bool TakesAnArgument(NaxpMessage message)
		=> NaxpMessages.Format(message, null).IndexOf("{0}", StringComparison.Ordinal) >= 0;
}
