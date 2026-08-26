// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NXOld.NXComponents;

partial class Ast
{
	public static void Simplify(ref Ast ast)
	{
		bool madeProgress;
		do
		{
			madeProgress = false;
			SimplifyRecursive(ref ast, ref madeProgress);
		} while (madeProgress);

	}
	static void SimplifyRecursive(ref Ast ast, ref bool madeProgress)
	{
		if (ast is Chars) { return; }

		#region Simplify this node
		if (ast is Opt opt) { X_FlattenOptionality(ref opt, ref madeProgress); }
		if (ast is Or or) { X_FlattenNested(ref or, ref madeProgress); }
		if (ast is Seq seq) { X_FlattenNested(ref seq, ref madeProgress); }

		X_FactoriseOptions(ref ast, ref madeProgress);
		X_UnifyCharSets(ref ast, ref madeProgress);
		// Deduplicate *before* factorising.
		X_DeduplicateAndSort(ref ast, ref madeProgress);

		bool localMadeProgress;
		do
		{
			localMadeProgress = false;

			X_Factorise(ref ast, ref localMadeProgress, fromLeft: true);
			X_Factorise(ref ast, ref localMadeProgress, fromLeft: false);

			X_ShiftOptionRight(ref ast, ref localMadeProgress);

			if (localMadeProgress) { madeProgress = true; }
		} while (localMadeProgress);

		#endregion

		#region Recursion
		if (ast is MultiChild multiChild2)
		{
			var children = multiChild2.Children;
			for (int i = 0; i < children.Length; ++i)
			{
				SimplifyRecursive(ref children[i], ref madeProgress);
			}
		}
		else if (ast is Opt opt2)
		{
			SimplifyRecursive(ref opt2.Child, ref madeProgress);
		}
		#endregion
	}

	#region Simplifications
	/// <summary> `a??` → `a?` ... flatten repeated optionality.</summary>
	static void X_FlattenOptionality(ref Opt opt, ref bool madeProgress)
	{
		if (opt.Child is Opt optChild)
		{
			madeProgress = true;
			opt = optChild;
		}
	}
	/// <summary> 
	/// Flatten ASTs with nested versions of themselves:
	/// <list type="bullet">
	/// <item>`(a|b)|c` → `a|b|c` ... flatten nested ors.</item>
	/// <item>`(ab)c` → `abc` ... flatten nested seqs.</item>
	/// </list>
	/// </summary>
	static void X_FlattenNested<T>(ref T t, ref bool madeProgress) where T : MultiChild
	{
		var children = t.Children;
		bool containsT = false;
		foreach (var child in children)
		{
			if (child is T)
			{
				containsT = true;
				break;
			}
		}
		if (!containsT) { return; }

		madeProgress = true;

		var newChildrenList = new List<Ast>();
		foreach (var child in children)
		{
			if (child is T childT)
			{
				newChildrenList.AddRange(childT.Children);
			}
			else
			{
				newChildrenList.Add(child);
			}
		}

		t.Children = newChildrenList.ToArray();
	}
	/// <summary> `a|b?|c` → `(a|b|c)?` ... factorise optionality towards AST root.</summary>
	static void X_FactoriseOptions(ref Ast ast, ref bool madeProgress)
	{
		if (ast is not Or or) { return; }

		var children = or.Children;

		bool containsAnOption = false;
		foreach (var child in children)
		{
			if (child is Opt)
			{
				containsAnOption = true;
				break;
			}
		}

		if (!containsAnOption) { return; }

		madeProgress = true;

		var newChildren = new Ast[children.Length];

		for (int i = 0; i < children.Length; ++i)
		{
			var child = children[i];
			if (child is Opt opt)
			{
				child = opt.Child;
			}
			newChildren[i] = child;
		}

		ast = new Opt(new Or(newChildren));
	}
	/// <summary> `A|B` → `[AB]` ... replace alternative char sets with a single char set.</summary>
	static void X_UnifyCharSets(ref Ast ast, ref bool madeProgress)
	{
		if (ast is not Or or) { return; }

		int countChars = 0;
		var children = or.Children;
		foreach (var child in children)
		{
			if (child is Chars) { ++countChars; }
		}

		if (countChars <= 1) { return; }

		madeProgress = true;

		if (countChars == children.Length)
		{
			#region All children are Chars so combine into a single Chars
			AsciiCharSet combinedCharSet = default;
			foreach (var child in children)
			{
				combinedCharSet |= ((Chars)child).CharSet;
			}

			ast = new Chars(combinedCharSet);
			#endregion
		}
		else
		{
			#region 2 or more children are Chars so combine them into a single Chars
			AsciiCharSet combinedCharSet = default;
			var newChildren = new Ast[children.Length - countChars + 1];
			int posNonChars = 1;
			foreach (var child in children)
			{
				if (child is Chars chars)
				{
					combinedCharSet |= ((Chars)child).CharSet;
				}
				else
				{
					newChildren[posNonChars] = child;
					++posNonChars;
				}
			}

			newChildren[0] = new Chars(combinedCharSet);
			or.Children = newChildren;
			#endregion
		}
	}
	/// <summary>
	/// `a|a` → `a` ... remove duplicate alternatives.
	/// <para>NB Sorting is required for logical equality to hold on <see cref="Or"/>.</para>
	/// </summary>
	static void X_DeduplicateAndSort(ref Ast ast, ref bool madeProgress)
	{
		if (ast is not Or or) { return; }

		var children = or.Children;

		#region Check for duplicates
		bool thereIsADuplicate = false;

		for (int i = 1; i < children.Length; ++i)
		{
			var child = children[i];
			for (int k = 0; k < i; ++k)
			{
				if (child.Equals(children[k]))
				{
					thereIsADuplicate = true;
					break;
				}
			}
			if (thereIsADuplicate) { break; }
		}
		#endregion

		if (thereIsADuplicate)
		{
			madeProgress = true;

			#region Remove duplicates
			var newChildrenList = new List<Ast>(children.Length - 1);

			for (int i = 0; i < children.Length; ++i)
			{
				var child = children[i];
				bool isDuplicate = false;
				for (int k = 0; k < i; ++k)
				{
					if (child.Equals(children[k]))
					{
						isDuplicate = true;
						break;
					}
				}
				if (!isDuplicate) { newChildrenList.Add(child); }
			}

			Debug.Assert(newChildrenList.Count < children.Length);

			if (newChildrenList.Count == 1)
			{
				ast = newChildrenList[0];
			}
			else
			{
				or.Children = newChildrenList.ToArray();
			}
			#endregion
		}

		#region Check is sorted
		bool isSorted = true;

		var prev_child = children[0];
		for (int i = 1; i < children.Length; ++i)
		{
			var child = children[i];
			if (prev_child.CompareTo(child) > 0)
			{
				isSorted = false;
				break;
			}
			prev_child = child;
		}
		#endregion

		if (!isSorted)
		{
			madeProgress = true;

			Array.Sort(children);
		}
	}

	/// <summary>
	/// Factorise from the left (<paramref name="fromLeft"/> is <see langword="true"/>) 
	/// or the right (<paramref name="fromLeft"/> is <see langword="false"/>). The following examples show <i>left</i> factorisation:
	/// <list type="bullet">
	///    <item> <term>Left full factorisation</term> <description>`ab|ac|ad` → `a (b|c|d)`</description> </item>
	///    <item> <term>Left partial factorisation</term> <description>`ab|ac|ad|e` → `a (b|c|d)|e`</description> </item>
	///    <item> <term>Left full option factorisation</term> <description>`ab|a` → `ab?`</description> </item>
	///    <item> <term>Left partial option factorisation</term> <description>`ab|a|c` → `ab?|c`</description> </item>
	///    <item> <term>Left mixed factorisation</term> <description>`ab|a|ac|d|e` → `a(b|c)?|d|e`</description> </item>
	/// </list>
	/// </summary>
	/// <param name="ast">The AST to simplify <i>in situ</i>.</param>
	/// <param name="madeProgress">Whether this simplification made any changes.</param>
	/// <param name="fromLeft">Whether to factorise from the left (as opposed to the right).</param>
	static void X_Factorise(ref Ast ast, ref bool madeProgress, bool fromLeft)
	{
		if (ast is not Or or) { return; }

		var children = or.Children;

		Ast? commonFactor = null;
		int commonFactorCount = 1;
		int commonSingletonFactorCount = -1;

		#region Check if there is a common factor
		for (int i = 0; i < children.Length - 1; ++i)
		{
			var factor_i = children[i];

			commonSingletonFactorCount = 1;
			if (factor_i is Seq seq_i)
			{
				factor_i = seq_i.HeadChild(fromLeft);
				commonSingletonFactorCount = 0;
			}

			for (int k = i + 1; k < children.Length; ++k)
			{
				var factor_k = children[k];

				bool isSingleton = true;
				if (factor_k is Seq seq_k)
				{
					factor_k = seq_k.HeadChild(fromLeft);
					isSingleton = false;
				}

				if (factor_i.Equals(factor_k))
				{
					// Simpler to overwrite than check if null.
					commonFactor = factor_i;
					++commonFactorCount;
					if (isSingleton)
					{
						++commonSingletonFactorCount;
					}
				}
			}

			if (commonFactor is not null) { break; }
		}
		#endregion

		if (commonFactor is not null)
		{
			madeProgress = true;

			// NB commonFactorXSCount == count(common factor occurrences) − 1
			int nonFactorisedCount = children.Length - commonFactorCount;
			int factorisedCount = commonFactorCount - commonSingletonFactorCount;

			Debug.Assert(nonFactorisedCount >= 0);
			Debug.Assert(factorisedCount >= 0);

			Ast? factorisedPart = null;
			#region Assign factorisedPart 
			if (factorisedCount == 1)
			{
				foreach (var child in children)
				{
					if (child is Seq seq)
					{
						var head = seq.HeadChild(fromLeft);
						if (commonFactor.Equals(head))
						{
							factorisedPart = seq.GetTailChildren(fromLeft);
							break;
						}
					}
				}
			}
			else
			{
				int pos = 0;
				var factorisedAsts = new Ast[factorisedCount];
				foreach (var child in children)
				{
					if (child is Seq seq)
					{
						var head = seq.HeadChild(fromLeft);
						if (commonFactor.Equals(head))
						{
							factorisedAsts[pos] = seq.GetTailChildren(fromLeft);
							++pos;
							//if (pos >= commonFactorCount) { break; }
						}
					}
				}

#if DEBUG
				foreach (var child in factorisedAsts)
				{
					Debug.Assert(child is not null);
				}
#endif

				factorisedPart = new Or(factorisedAsts);
			}
			#endregion

			var newTail = commonSingletonFactorCount != 0 ? new Opt(factorisedPart!) : factorisedPart;

			var newSeqChildren = fromLeft
				? new[] { commonFactor, newTail!, }
				: new[] { newTail!, commonFactor, }
				;

			ast = new Seq(newSeqChildren);

			if (nonFactorisedCount > 0)
			{
				var newOrChildren = new Ast[nonFactorisedCount + 1];

				newOrChildren[0] = ast;

				int pos = 1;
				foreach (var child in children)
				{
					// **Not** the condition used above
					var testFactor = child;
					if (testFactor is Seq seq)
					{
						testFactor = seq.HeadChild(fromLeft);
					}
					if (!commonFactor.Equals(testFactor))
					{
						newOrChildren[pos] = child;
						++pos;
					}
				}
				ast = new Or(newOrChildren);
			}
		}
	}

	/// <summary>
	/// `a?a` → `aa?` ... shift option right.
	/// </summary>
	static void X_ShiftOptionRight(ref Ast ast, ref bool madeProgress)
	{
		if (ast is not Seq seq) { return; }

		var children = seq.Children;

		var prev = children[0];
		var prevAsOpt_child = (prev as Opt)?.Child;

		for (int i = 1; i < children.Length; ++i)
		{
			var child = children[i];

			if (prevAsOpt_child is not null && prevAsOpt_child.Equals(child))
			{
				madeProgress = true;
				// We could use re-use prevAsOpt_child
				// but we've generally steered away
				// from duplicating references
				children[i - 1] = child;
				children[i] = prev;
				return;
			}

			prev = child;
			prevAsOpt_child = prev as Opt;
		}
	}
	#endregion
}