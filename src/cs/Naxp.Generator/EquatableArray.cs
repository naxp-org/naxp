// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace LogMu.Generator;

/// <summary>
/// An <see cref="ImmutableArray{T}"/> that compares by value, so a model holding one can sit in
/// an incremental generator's pipeline without defeating its caching.
/// </summary>
/// <typeparam name="T">The element type, itself compared by value.</typeparam>
readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
	where T : IEquatable<T>
{
	readonly ImmutableArray<T> items;

	/// <summary>Wraps an array, treating the default as empty.</summary>
	public EquatableArray(ImmutableArray<T> items)
	{
		this.items = items.IsDefault ? ImmutableArray<T>.Empty : items;
	}

	/// <summary>The empty array.</summary>
	public static EquatableArray<T> Empty { get; } = new(ImmutableArray<T>.Empty);

	/// <summary>The number of elements.</summary>
	public int Count => this.items.IsDefault ? 0 : this.items.Length;

	/// <summary>The element at an index.</summary>
	public T this[int index] => this.items[index];

	/// <summary>The wrapped array.</summary>
	public ImmutableArray<T> Items => this.items.IsDefault ? ImmutableArray<T>.Empty : this.items;

	/// <inheritdoc/>
	public bool Equals(EquatableArray<T> other)
	{
		if (this.Count != other.Count) { return false; }

		for (int i = 0; i < this.Count; i++)
		{
			if (!this[i].Equals(other[i])) { return false; }
		}

		return true;
	}

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is EquatableArray<T> other && this.Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		unchecked
		{
			const uint Offset = 2166136261;
			const uint Prime = 16777619;

			uint hash = Offset;

			foreach (T item in this.Items)
			{
				hash = (hash ^ (uint)item.GetHashCode()) * Prime;
			}

			return (int)hash;
		}
	}

	/// <inheritdoc/>
	public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)this.Items).GetEnumerator();

	/// <inheritdoc/>
	IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}

/// <summary>Turning ordinary collections into <see cref="EquatableArray{T}"/>.</summary>
static class EquatableArray
{
	/// <summary>Wraps an immutable array.</summary>
	public static EquatableArray<T> AsEquatable<T>(this ImmutableArray<T> items)
		where T : IEquatable<T>
		=> new(items);

	/// <summary>Copies a sequence into an equatable array.</summary>
	public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
		where T : IEquatable<T>
		=> new(ImmutableArray.CreateRange(items));
}
