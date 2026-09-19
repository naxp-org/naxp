// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

namespace LogMu;

/// <summary>
/// How one set stands to another, for two sets called <c>a</c> and <c>b</c>.
/// </summary>
public enum SetRelationship : byte
{
	/// <summary>
	/// Neither set contains the other. They may be wholly disjoint or overlap in part.
	/// </summary>
	/// <remarks>
	/// This is the default so that a comparison which was never made says nothing rather than
	/// claiming the two sets are alike.
	/// </remarks>
	Incomparable = 0,

	/// <summary>The two sets have exactly the same members.</summary>
	Equal = 1,

	/// <summary>
	/// Every member of <c>a</c> is a member of <c>b</c>, and <c>b</c> has at least one more.
	/// </summary>
	SubsetOf = 2,

	/// <summary>
	/// Every member of <c>b</c> is a member of <c>a</c>, and <c>a</c> has at least one more.
	/// </summary>
	SupersetOf = 3,
}
