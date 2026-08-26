// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Globalization;

namespace LogMu;

/// <summary>
/// A refusal: which message, where in the source, and what the message needs to say it.
/// </summary>
/// <remarks>
/// <para>
/// The text is not held. A refusal names a <see cref="NaxpMessage"/> and, where that message
/// interpolates something, supplies one string; the words are looked up only when somebody asks
/// for them. So nothing between the point of refusal and the public surface handles prose.
/// </para>
/// <para>
/// An <see cref="Offset"/> and a <see cref="Length"/> of zero together mean the whole naxp, which
/// is what most refusals want and none of them have to say. Only the parser knows a position, and
/// only the public surface knows how long the source is, so the substitution happens there. Every
/// refusal that does name a position uses a length of at least one, or it would read as this.
/// </para>
/// </remarks>
readonly struct NaxpError
{
	/// <summary>Constructs a refusal.</summary>
	/// <param name="message">Which refusal this is.</param>
	/// <param name="argument">
	/// What <paramref name="message"/> interpolates, or <see langword="null"/> where it takes
	/// nothing.
	/// </param>
	/// <param name="offset">Where the fault starts, or zero for the naxp as a whole.</param>
	/// <param name="length">How much is at fault, or zero for the naxp as a whole.</param>
	public NaxpError(NaxpMessage message, string? argument = null, int offset = 0, int length = 0)
	{
		this.Message = message;
		this.Argument = argument;
		this.Offset = offset;
		this.Length = length;
	}

	/// <summary>Which refusal this is.</summary>
	public NaxpMessage Message { get; }

	/// <summary>What the message interpolates, or <see langword="null"/>.</summary>
	public string? Argument { get; }

	/// <summary>Where the fault starts. Zero with a zero <see cref="Length"/> means the whole naxp.</summary>
	public int Offset { get; }

	/// <summary>How much is at fault. Zero with a zero <see cref="Offset"/> means the whole naxp.</summary>
	public int Length { get; }

	/// <summary>Whether this refusal belongs to the naxp as a whole rather than to a place in it.</summary>
	public bool IsWholeNaxp => this.Offset == 0 && this.Length == 0;

	/// <summary>The stable identifier for this refusal, such as <c>NAXP1002</c>.</summary>
	/// <remarks>
	/// The number alone. <see cref="NaxpMessage"/> spells each member <c>NAXP1002_IntervalHyphen</c>
	/// so that somebody reading the library can see at a glance which refusal a line is about, but
	/// that half is a note to ourselves: it is not part of the identifier, it would read as a
	/// promise about wording we have not made, and it must never reach a caller.
	/// </remarks>
	public string Code
	{
		get
		{
			string name = this.Message.ToString();
			int hint = name.IndexOf('_');

			return hint < 0 ? name : name.Substring(0, hint);
		}
	}

	/// <summary>What is wrong, and where practical what to write instead.</summary>
	public string Text => NaxpMessages.Format(this.Message, this.Argument);

	/// <inheritdoc/>
	public override string ToString()
		=> this.IsWholeNaxp
			? string.Format(CultureInfo.InvariantCulture, "{0}: {1}", this.Code, this.Text)
			: string.Format(
				CultureInfo.InvariantCulture,
				"{0} at {1}..{2}: {3}",
				this.Code,
				this.Offset,
				this.Offset + this.Length,
				this.Text)
			;
}
