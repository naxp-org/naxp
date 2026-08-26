// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Diagnostics;
using System.Text;

namespace NXOld.NXComponents;

/// <summary>An NX AST.</summary>
abstract partial class Ast : IEquatable<Ast>, IComparable<Ast>
{
#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
	public abstract bool Equals(Ast other);
	public abstract int CompareTo(Ast other);
#pragma warning restore CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
	/// <summary>
	/// This <see cref="Ast"/> as NX text.
	/// </summary>
	public sealed override string ToString()
	{
		var sb = new StringBuilder();
		this.WriteTextTo(sb);
		return sb.ToString();
	}
	/// <summary>
	/// Writes the NX text to the specified string builder.
	/// </summary>
	/// <param name="sb">The string builder to which the NS is to be written.</param>
	public abstract void WriteTextTo(StringBuilder sb);
	public abstract int Precedence { get; }

	#region Constants
	private protected const int HashBase_Chars = 0x73e2_0f3c;
	private protected const int HashBase_Opt = 0x5f40_7149;
	private protected const int HashBase_Seq = 0x309e_8a15;
	private protected const int HashBase_Or = 0x6f51_01e2;

	// 0 == `|` Or, 1 == Seq, 2 == ? Opt, 3 == char set
	private protected const int Precedence_Empty = 4;
	private protected const int Precedence_Chars = 3;
	private protected const int Precedence_Opt = 2;
	private protected const int Precedence_Seq = 1;
	private protected const int Precedence_Or = 0;
	#endregion
}
/// <summary>An <see cref="Ast"/> representing an empty NX (and nothing else).</summary>
sealed class Empty : Ast
{
	private Empty() { }
	public override bool Equals(Ast other) => other is Empty;
	public override int CompareTo(Ast other)
		=> other is Empty ? 0 : -1; // *Opposite* order to precedence
	public override int GetHashCode() => 0;
	public override void WriteTextTo(StringBuilder sb) { }
	public override int Precedence => Precedence_Empty;
	static Empty? cachedInstance;
	public static Empty Instance
		=> cachedInstance is null ? cachedInstance = new Empty() : cachedInstance;
}
/// <summary>An <see cref="Ast"/> representing a known set of characters.</summary>
sealed class Chars : Ast
{
	public Chars(AsciiCharSet charSet)
	{
		this.CharSet = charSet;
	}
	public AsciiCharSet CharSet;
	public override bool Equals(Ast other) => other is Chars otherAsChars && this.Equals(otherAsChars);
	public bool Equals(Chars other) => this.CharSet.Equals(other.CharSet);
	public override int CompareTo(Ast other) => other is Chars otherAsChars
		? this.CompareTo(otherAsChars)
		: other.Precedence.CompareTo(this.Precedence) // *Opposite* order to precedence
		;
	public int CompareTo(Chars other) => this.CharSet.CompareTo(other.CharSet);
	public override int GetHashCode() => HashCode.Combine(HashBase_Chars, this.CharSet.GetHashCode());
	/// <summary>
	/// Writes the NX text to the specified string builder.
	/// </summary>
	/// <param name="sb">The string builder to which the NS is to be written.</param>
	public override void WriteTextTo(StringBuilder sb) => this.CharSet.WriteTo(sb);
	public override int Precedence => Precedence_Chars; // 0 == `|` Or, 1 == Seq, 2 == ? Opt, 3 == char set.
}
/// <summary>An <see cref="Ast"/> representing an <b>optional</b> expression, e.g. <c>A?</c>.</summary>
sealed class Opt : Ast
{
	public Opt(Ast child)
	{
		this.Child = child;
	}
	public Ast Child;
	public override bool Equals(Ast other) => other is Opt otherAsOpt && this.Equals(otherAsOpt);
	public bool Equals(Opt other) => this.Child.Equals(other.Child);
	public override int CompareTo(Ast other) => other is Opt otherAsOpt
		? this.CompareTo(otherAsOpt)
		: other.Precedence.CompareTo(this.Precedence) // *Opposite* order to precedence
		;
	public int CompareTo(Opt other) => this.Child.CompareTo(other);
	public override int GetHashCode() => HashCode.Combine(HashBase_Opt, this.Child);
	/// <summary>
	/// Writes the NX text to the specified string builder.
	/// </summary>
	/// <param name="sb">The string builder to which the NS is to be written.</param>
	public override void WriteTextTo(StringBuilder sb)
	{
		bool useParentheses = this.Precedence >= this.Child.Precedence;

		if (useParentheses) { sb.Append('('); }
		this.Child.WriteTextTo(sb);
		if (useParentheses) { sb.Append(')'); }
		sb.Append('?');
	}
	public override int Precedence => Precedence_Opt; // 0 == `|` Or, 1 == Seq, 2 == ? Opt, 3 == char set.
}
/// <summary>Absrtact class for an <see cref="Ast"/> representing an <b>or</b> or <b>sequence</b>.</summary>
abstract class MultiChild : Ast
{
	public MultiChild(Ast[] children)
	{
		Debug.Assert(children.Length >= 2);
#if DEBUG
		foreach (var child in children)
		{
			Debug.Assert(child is not null);
		}
#endif

		this.Children = children;
	}
	/// <summary>
	/// It is guranteed that there are at least two children.
	/// </summary>
	public Ast[] Children;
	protected int CompareTo(MultiChild other)
	{
		var thisChildren = this.Children;
		var otherChildren = other.Children;

		int minLength = Math.Min(thisChildren.Length, otherChildren.Length);
		for (int i = 0; i < minLength; ++i)
		{
			var comparison = thisChildren[i].CompareTo(otherChildren[i]);
			if (comparison != 0) { return comparison; }
		}

		return thisChildren.Length.CompareTo(otherChildren.Length);
	}
}
/// <summary>An <see cref="Ast"/> representing a <b>sequence</b> of two or more expressions, e.g. <c>ABC</c>.</summary>
sealed class Seq : MultiChild
{
	public Seq(Ast[] children)
		: base(children)
	{ }
	public override bool Equals(Ast other) => other is Seq otherAsSeq && this.Equals(otherAsSeq);
	public bool Equals(Seq other)
	{
		var thisChildren = this.Children;
		var otherChildren = other.Children;
		if (thisChildren.Length != otherChildren.Length) { return false; }
		for (int i = 0; i < thisChildren.Length; ++i)
		{
			if (!thisChildren[i].Equals(otherChildren[i])) { return false; }
		}
		return true;
	}
	public override int CompareTo(Ast other) => other is Seq otherAsSeq
		? this.CompareTo(otherAsSeq)
		: other.Precedence.CompareTo(this.Precedence) // *Opposite* order to precedence
		;
	public int CompareTo(Seq other) => base.CompareTo(other);
	public override int GetHashCode()
	{
		var thisChildren = this.Children;
		int hashCode = HashCode.Combine(HashBase_Seq, thisChildren.Length);
		foreach (var child in thisChildren)
		{
			hashCode = HashCode.Combine(hashCode, child.GetHashCode());
		}
		return hashCode;
	}
	/// <summary>
	/// Writes the NX text to the specified string builder.
	/// </summary>
	/// <param name="sb">The string builder to which the NS is to be written.</param>
	public override void WriteTextTo(StringBuilder sb)
	{
		var children = this.Children;
		for (int i = 0; i < children.Length; ++i)
		{
			var child = children[i];
			bool useParentheses = this.Precedence >= child.Precedence;
			if (useParentheses) { sb.Append('('); }
			child.WriteTextTo(sb);
			if (useParentheses) { sb.Append(')'); }
		}
	}
	public override int Precedence => Precedence_Seq; // 0 == `|` Or, 1 == Seq, 2 == ? Opt, 3 == char set.
	/// <summary>
	/// Get the element at the edge of the sequence, this being
	/// <list type="bullet">
	/// <item>the leftmost element if <paramref name="fromLeft"/> is <see langword="true"/>, e.g. <c>a</c> from <c>abc</c>, or else</item>
	/// <item>the rightmost element if <paramref name="fromLeft"/> is <see langword="false"/>, e.g.  <c>c</c> from <c>abc</c>.</item>
	/// </list>
	/// </summary>
	/// <param name="fromLeft">Whether to start from the left (as opposed to the right).</param>
	/// <returns>The element at the edge of the sequence.</returns>
	public Ast HeadChild(bool fromLeft) => fromLeft ? this.Children[0] : this.Children[^1];
	/// <summary>
	/// Get the tail elements of the sequence, this being
	/// <list type="bullet">
	/// <item>all the elements <i>except the leftmost</i> if <paramref name="fromLeft"/> is <see langword="true"/>, e.g. <c>bc</c> from <c>abc</c>, or else</item>
	/// <item>the rightmost element if <paramref name="fromLeft"/> is <see langword="false"/>, e.g. <c>ab</c> from <c>abc</c>.</item>
	/// </list>
	/// </summary>
	/// <param name="fromLeft">Whether to start from the left (as opposed to the right).</param>
	/// <returns>The element at the edge of the sequence.</returns>
	public Ast GetTailChildren(bool fromLeft)
	{
		var children = this.Children;

		if (children.Length == 2)
		{
			return fromLeft ? children[^1] : children[0];
		}

		Ast[] tailChildren = fromLeft ? children[1..] : children[..^1];

		Debug.Assert(tailChildren.Length == children.Length - 1);

		return new Seq(tailChildren);
	}
}
/// <summary>An <see cref="Ast"/> representing a <b>or</b> of ideally two or more expressions, e.g. <c>ABC</c>.</summary>
sealed class Or : MultiChild
{
	public Or(Ast[] children)
		: base(children)
	{ }
	public override bool Equals(Ast other) => other is Or otherAsOr && this.Equals(otherAsOr);
	public bool Equals(Or other)
	{
		var thisChildren = this.Children;
		var otherChildren = other.Children;
		if (thisChildren.Length != otherChildren.Length) { return false; }
		for (int i = 0; i < thisChildren.Length; ++i)
		{
			if (!thisChildren[i].Equals(otherChildren[i])) { return false; }
		}
		return true;
	}
	public override int CompareTo(Ast other) => other is Or otherAsOr
		? this.CompareTo(otherAsOr)
		: other.Precedence.CompareTo(this.Precedence) // *Opposite* order to precedence
		;
	public int CompareTo(Or other) => base.CompareTo(other);
	public override int GetHashCode()
	{
		var thisChildren = this.Children;
		int hashCode = HashCode.Combine(HashBase_Or, thisChildren.Length);
		foreach (var child in thisChildren)
		{
			hashCode = HashCode.Combine(hashCode, child.GetHashCode());
		}
		return hashCode;
	}
	/// <summary>
	/// Writes the NX text to the specified string builder.
	/// </summary>
	/// <param name="sb">The string builder to which the NS is to be written.</param>
	public override void WriteTextTo(StringBuilder sb)
	{
		var children = this.Children;
		for (int i = 0; i < children.Length; ++i)
		{
			if (i != 0) { sb.Append('|'); }
			var child = children[i];
			bool useParentheses = this.Precedence >= child.Precedence;
			if (useParentheses) { sb.Append('('); }
			child.WriteTextTo(sb);
			if (useParentheses) { sb.Append(')'); }
		}
	}
	public override int Precedence => Precedence_Or; // 0 == `|` Or, 1 == Seq, 2 == ? Opt, 3 == char set.
}
