using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LogMu;
using Proto;

static class Program
{
	static readonly (string label, string a, string b)[] Pairs =
	{
		("rendering swapped, disjoint C", "(A|B)!A", "(A|B)!B"),
		("rendering swapped, shared C string", "(A|B)!A|C", "(A|B)!B|C"),
		("the specification's own pair", "[\\s\\-]?!\\-", "[\\s\\-]!?"),
		("held rendering against a copy, k=2", "[ab]{2}", "([ab]{2})!(aa)"),
		("held rendering against a copy, k=17", "[ab]{17}", "([ab]{17})!(a{17})"),
		("space made optional", "\\A\\9\\s\\A", "\\A\\9\\s!!\\A"),
		("case fold added", "\\A\\9\\A", "\\C(\\A\\9\\A)"),
		("upper fold against lower fold", "\\C(\\A\\9\\s!!\\A)", "\\c(\\A\\9\\s!!\\A)"),
		("postcode: space made optional", "\\A\\A?\\9\\X?\\s\\9\\A\\A", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A"),
		("postcode: case fold added", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A", "\\C(\\A\\A?\\9\\X?\\s!!\\9\\A\\A)"),
		("postcode: GIR 0AA added", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A|GIR\\s!!0AA"),
		("postcode: last first-letter removed", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A", "[A-Y]\\A?\\9\\X?\\s!!\\9\\A\\A"),
		("postcode: middle first-letter removed", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A", "[A-PR-Z]\\A?\\9\\X?\\s!!\\9\\A\\A"),
		("postcode against itself", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A"),
		("postcode: space dropped", "\\A\\A?\\9\\X?\\s!!\\9\\A\\A", "\\A\\A?\\9\\X?\\s!?\\9\\A\\A"),
		("register family, alternative added after", "[ab]{16}c|([ab]!a){16}d", "[ab]{16}c|([ab]!a){16}d|e"),
		("register family against its fold", "[ab]{16}c|([ab]!a){16}d", "\\c([ab]{16}c|([ab]!a){16}d)"),
		("padding added to a decimal range", "#[0-105]", "#[0?0!0-105]"),
		("padding changed on a decimal range", "#[0!0-105]", "#[0?0-105]"),
		("A!? against its fold", "A!?|B", "\\C(A!?|B)"),
	};

	static void Main()
	{
		foreach ((string label, string na, string nb) in Pairs)
		{
			if (!Compiler.TryCompile(na, out Compilation? a, out NaxpError? ea)) { Console.WriteLine($"== {label}: a does not compile: {ea}"); continue; }
			if (!Compiler.TryCompile(nb, out Compilation? b, out NaxpError? eb)) { Console.WriteLine($"== {label}: b does not compile: {eb}"); continue; }

			Console.WriteLine($"== {label}");
			Console.WriteLine($"   a = {na}");
			Console.WriteLine($"   b = {nb}");

			var sw = Stopwatch.StartNew();
			RankProductResult product = RankProduct.Compare(a!, b!);
			sw.Stop();
			Console.WriteLine($"   rank product:    {product.Verdict}, {product.Tuples} tuples, {product.Conflicts} conflicts, {sw.ElapsedMilliseconds} ms{(product.Note is null ? "" : $" ({product.Note})")}");
			if (product.WitnessA is not null)
			{
				Console.WriteLine($"                    witness '{product.WitnessA}': a {a!.Encode(product.WitnessA)} b {b!.Encode(product.WitnessA)}");
				if (product.WitnessB is not null) { Console.WriteLine($"                    witness '{product.WitnessB}': a {a.Encode(product.WitnessB)} b {b.Encode(product.WitnessB)}"); }
			}

			sw.Restart();
			string square = TwoRootSquare.Run(a!, b!, 100_000, out int squareStates);
			sw.Stop();
			Console.WriteLine($"   two-root square: {square}, {squareStates} pairs, {sw.ElapsedMilliseconds} ms");

			sw.Restart();
			Agreement ranks = RankAgreement.Compare(a!.Canonical, b!.Canonical);
			sw.Stop();
			Console.WriteLine($"   rank agreement on shared C: {ranks}");

			string check = CrossCheck(a, b);
			Console.WriteLine($"   enumeration:     {check}");
			Console.WriteLine();
		}
	}

	/// <summary>Encode agreement over the strings both accept, exhaustively where L is small and by uniform sampling otherwise.</summary>
	static string CrossCheck(Compilation a, Compilation b)
	{
		const int cap = 300_000;
		int shared = 0;
		string? witness = null;

		foreach (Compilation side in new[] { a, b })
		{
			IEnumerable<string> strings = side.Accepted.StringCount <= (ulong)cap ? Enumerate(side.Accepted) : Sample(side.Accepted, 50_000, new Random(1));

			foreach (string w in strings)
			{
				if (!a.Accepts(w) || !b.Accepts(w)) { continue; }
				++shared;
				if (a.Encode(w) != b.Encode(w)) { witness ??= w; }
			}
		}

		bool exhaustive = a.Accepted.StringCount <= (ulong)cap && b.Accepted.StringCount <= (ulong)cap;
		string mode = exhaustive ? "exhaustive" : "sampled";
		return witness is null
			? $"agrees on {shared} shared strings ({mode})"
			: $"DIFFERS at '{witness}': a {a.Encode(witness)} b {b.Encode(witness)} ({mode})";
	}

	static IEnumerable<string> Enumerate(StateMap map)
	{
		var stack = new Stack<(State state, string prefix)>();
		stack.Push((map.Start, string.Empty));

		while (stack.Count > 0)
		{
			(State state, string prefix) = stack.Pop();

			foreach (Transition t in state.Transitions)
			{
				if (t.Set.IsEmpty) { yield return prefix; continue; }
				foreach (char c in t.Set) { stack.Push((t.Next, prefix + c)); }
			}

			if (state.IsTerminal) { yield return prefix; }
		}
	}

	static IEnumerable<string> Sample(StateMap map, int count, Random random)
	{
		for (int i = 0; i < count; ++i)
		{
			var builder = new StringBuilder();
			State state = map.Start;

			while (!state.IsTerminal)
			{
				ulong r = (ulong)random.NextInt64((long)state.StringCount);
				State? next = null;

				foreach (Transition t in state.Transitions)
				{
					ulong per = t.Next.StringCount;
					ulong chunk = t.Set.IsEmpty ? 1UL : per * (ulong)t.Set.Count;

					if (r < chunk)
					{
						if (t.Set.IsEmpty) { next = null; }
						else { builder.Append(t.Set.CharacterAt((int)(r / per))); next = t.Next; }
						break;
					}

					r -= chunk;
				}

				if (next is null) { break; }
				state = next;
			}

			yield return builder.ToString();
		}
	}
}
