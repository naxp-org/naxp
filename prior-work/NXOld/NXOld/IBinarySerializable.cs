// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace NXOld;

/// <summary>
/// IBinarySerializable.
/// </summary>
public interface IBinarySerializable<TSelf> where TSelf : IBinarySerializable<TSelf>
{
	/// <summary>
	/// Serialises this instance to <paramref name="writer"/>.
	/// </summary>
	/// <param name="writer">The binary writer.</param>
	public void WriteTo(BinaryWriter writer);
	/// <summary>
	/// Tries to deserialises an instance of this type from <paramref name="reader"/>
	/// (assuming it was written using a corresponding <c>WriteTo(...)</c> method).
	/// </summary>
	/// <param name="reader">The binary reader.</param>
	/// <returns>Whether the deserialisation succeeded.</returns>
	/// <param name="instance">The read instance (provided the method returns <see langword="true"/>).</param>
	/// <param name="errorMessage">
	/// The error message if the method returns <see langword="false"/>.
	/// E.g. "Corrupt input when deserialising {typename}".
	/// </param>
	public static abstract bool TryReadFrom(BinaryReader reader, [MaybeNullWhen(false)] out TSelf instance, [NotNullWhen(false)] out string? errorMessage);
	/// <summary>
	/// Deserialises an instance of this type from <paramref name="reader"/>
	/// (assuming it was written using a corresponding <c>WriteTo(...)</c> method).
	/// </summary>
	/// <param name="reader">The binary reader.</param>
	/// <returns>The deserialised instance.</returns>
	/// <exception cref="IOException"></exception>
	public static virtual TSelf ReadFrom(BinaryReader reader) => TSelf.TryReadFrom(reader, out var instance, out string? errorMessage)
			? instance
			: throw new IOException(errorMessage);
}

partial class LogMuExtensions
{
	/// <summary>
	/// Deserialises an instance of <typeparamref name="TSelf"/> from <paramref name="reader"/>
	/// (assuming it was written using the corresponding <c>Write(...)</c> method).
	/// </summary>
	/// <typeparam name="TSelf"></typeparam>
	/// <summary>
	/// </summary>
	/// <param name="reader">The binary reader.</param>
	/// <returns>The deserialised instance.</returns>
	/// <exception cref="IOException"></exception>
	public static TSelf Read<TSelf>(this BinaryReader reader) where TSelf : IBinarySerializable<TSelf>
		=> TSelf.ReadFrom(reader);

	/// <summary>
	/// Serialises the specified item to <paramref name="writer"/>.
	/// </summary>
	/// <param name="writer">The binary writer.</param>
	/// <param name="item">The item to serialise.</param>
	public static void Write<TSelf>(this BinaryWriter writer, TSelf item) where TSelf : IBinarySerializable<TSelf>
		=> item.WriteTo(writer);
}

