// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace LogMu;

/// <summary>
/// The integer type generated code uses for encoded values.
/// </summary>
/// <remarks>
/// The default everywhere is <see cref="UInt64"/>, which every naxp fits: W5 caps the value
/// count at 2^64 - 1, so nothing narrower is guaranteed to hold one. A narrower choice is
/// validated against the naxp's value count when emitting, so a naxp that outgrows the type
/// its caller pinned is refused rather than silently widened. Each emitter maps the choice to
/// its language's own types; a language without unsigned integers, such as Java, refuses the
/// unsigned members.
/// </remarks>
enum NaxpValueType
{
	Int8,
	UInt8,
	Int16,
	UInt16,
	Int32,
	UInt32,
	Int64,
	UInt64,
}

/// <summary>
/// The base of the language emitters, which turn a compiled naxp into recogniser and codec
/// source in one language.
/// </summary>
/// <remarks>
/// <para>
/// An emitter writes a fragment: function definitions and the constants they share, every name
/// prefixed with the caller's prefix. The language paraphernalia around the fragment - a header
/// comment, imports, a namespace or wrapping class - is the caller's job, which is what lets
/// the same fragment land in a source generator's partial class and on a web page alike.
/// </para>
/// <para>
/// An instance holds only its language. Everything the naxp decides - the renumbered machines,
/// the constants encode and decode fold their arithmetic into, the buffer bound - is computed
/// per call into a <see cref="Context"/>, so one instance serves every naxp, concurrently.
/// The derived emitters each cache such an instance.
/// </para>
/// <para>
/// States are renumbered breadth first from the start, so the start is state zero and an
/// ordinary naxp's whole machine lands in the first chunk.
/// </para>
/// </remarks>
abstract class Emitter
{
	/// <summary>
	/// The most states one generated method may hold. A machine above this is emitted as methods
	/// of this many states behind a dispatcher.
	/// </summary>
	/// <remarks>
	/// One method holding 2 000 states would meet per-method limits: the .NET JIT stops
	/// optimising very large methods, and Java has a hard 64 KB of bytecode per method. Machines
	/// above this size only arise from long literal runs, whose cases are trivial, so the split
	/// costs an ordinary naxp nothing - the five naxps on naxp.org's landing page are all under
	/// fifty states.
	/// </remarks>
	internal const int ChunkSize = 250;

	readonly string indent;
	readonly string? blockOpen;
	readonly string? blockClose;

	/// <summary>
	/// Constructs an emitter over its language's block syntax, with the meanings
	/// <see cref="CodeWriter"/> gives the arguments. The defaults suit the brace languages.
	/// </summary>
	protected Emitter(string indent = "\t", string? blockOpen = "{", string? blockClose = "}")
	{
		this.indent = indent;
		this.blockOpen = blockOpen;
		this.blockClose = blockClose;
	}

	#region Emitting
	/// <summary>
	/// Emits a compiled naxp as a source fragment.
	/// </summary>
	/// <param name="compilation">The naxp.</param>
	/// <param name="prefix">
	/// The prefix every generated name starts with, so several naxps can share one scope. May be
	/// empty, where the bare names are wanted.
	/// </param>
	/// <param name="valueType">
	/// The integer type the generated code uses for encoded values. The naxp's value count must
	/// fit it.
	/// </param>
	/// <param name="initialIndent">
	/// What every line of the fragment is indented with ahead of its own depth, so it can sit
	/// inside an already-indented wrapper.
	/// </param>
	/// <returns>The fragment.</returns>
	/// <exception cref="ArgumentException">
	/// The prefix is neither empty nor an ASCII identifier, or the value count does not fit
	/// <paramref name="valueType"/>.
	/// </exception>
	public string Emit(Compilation compilation, string prefix, NaxpValueType valueType = NaxpValueType.UInt64, string initialIndent = "")
	{
		var builder = new StringBuilder();
		this.Emit(compilation, prefix, builder, valueType, initialIndent);

		return builder.ToString();
	}

	/// <summary>
	/// Emits a compiled naxp as a source fragment appended to a <see cref="StringBuilder"/>.
	/// The parameters are those of <see cref="Emit(Compilation, string, NaxpValueType, string)"/>.
	/// </summary>
	public void Emit(Compilation compilation, string prefix, StringBuilder builder, NaxpValueType valueType = NaxpValueType.UInt64, string initialIndent = "")
	{
		if (builder is null) { throw new ArgumentNullException(nameof(builder)); }

		this.Emit(compilation, prefix, valueType, new CodeWriterSB(builder, initialIndent, this.indent, this.blockOpen, this.blockClose));
	}

	/// <summary>
	/// Emits a compiled naxp as a source fragment written to a <see cref="TextWriter"/>.
	/// The parameters are those of <see cref="Emit(Compilation, string, NaxpValueType, string)"/>.
	/// </summary>
	public void Emit(Compilation compilation, string prefix, TextWriter writer, NaxpValueType valueType = NaxpValueType.UInt64, string initialIndent = "")
	{
		if (writer is null) { throw new ArgumentNullException(nameof(writer)); }

		this.Emit(compilation, prefix, valueType, new CodeWriterTW(writer, initialIndent, this.indent, this.blockOpen, this.blockClose));
	}

	void Emit(Compilation compilation, string prefix, NaxpValueType valueType, CodeWriter writer)
	{
		if (compilation is null) { throw new ArgumentNullException(nameof(compilation)); }
		if (prefix is null) { throw new ArgumentNullException(nameof(prefix)); }

		if (prefix.Length != 0) { ValidateIdentifier(prefix, nameof(prefix)); }

		if (compilation.ValueCount > Capacity(valueType))
		{
			throw new ArgumentException($"This naxp encodes {compilation.ValueCount} values, which does not fit {valueType}.", nameof(valueType));
		}

		this.Emit(new Context(compilation, prefix, valueType, writer));
	}

	/// <summary>The largest value count a type can hold, remembering that zero is reserved.</summary>
	internal static ulong Capacity(NaxpValueType valueType)
	{
		switch (valueType)
		{
			case NaxpValueType.Int8: return (ulong)sbyte.MaxValue;
			case NaxpValueType.UInt8: return byte.MaxValue;
			case NaxpValueType.Int16: return (ulong)short.MaxValue;
			case NaxpValueType.UInt16: return ushort.MaxValue;
			case NaxpValueType.Int32: return int.MaxValue;
			case NaxpValueType.UInt32: return uint.MaxValue;
			case NaxpValueType.Int64: return long.MaxValue;
			case NaxpValueType.UInt64: return ulong.MaxValue;
			default: throw new ArgumentException($"'{valueType}' is not a value type.", nameof(valueType));
		}
	}

	/// <summary>Writes one naxp's fragment in the derived emitter's language.</summary>
	protected abstract void Emit(Context context);
	#endregion
	#region Shared helpers
	/// <summary>The set's characters as inclusive runs of consecutive codes, in ascending order.</summary>
	protected static List<(char First, char Last)> GetRuns(AsciiCharSet set)
	{
		var runs = new List<(char First, char Last)>();
		char first = default;
		char previous = default;
		bool open = false;

		foreach (char c in set)
		{
			if (!open)
			{
				first = c;
				open = true;
			}
			else if (c != previous + 1)
			{
				runs.Add((first, previous));
				first = c;
			}

			previous = c;
		}

		if (open) { runs.Add((first, previous)); }

		return runs;
	}

	/// <summary>The version for a generated header, which writing one is the caller's job.</summary>
	internal static string PackageVersion()
	{
		string? version = typeof(Emitter).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

		if (version is null) { return "(unversioned)"; }

		// Informational versions may carry build metadata after a plus, which nobody reading the
		// generated file wants.
		int metadata = version.IndexOf('+');

		return metadata < 0 ? version : version.Substring(0, metadata);
	}

	/// <summary>The naxp's source, made safe for a line comment.</summary>
	internal static string CommentText(string text)
	{
		var builder = new StringBuilder(text.Length);

		foreach (char c in text)
		{
			builder.Append(c < ' ' ? ' ' : c);
		}

		return builder.ToString();
	}

	/// <summary>
	/// Throws where a name is not an ASCII identifier: an ASCII letter or underscore, then ASCII
	/// letters, digits and underscores. ASCII rather than the target language's own rule, because
	/// one name feeds emitters in several languages and this is what they all accept.
	/// </summary>
	protected static void ValidateIdentifier(string name, string parameterName)
	{
		if (!TryValidateIdentifier(name, out string? reason))
		{
			throw new ArgumentException(reason, parameterName);
		}
	}

	/// <summary>
	/// Whether a name passes <see cref="ValidateIdentifier"/>, saying why not rather than
	/// throwing. This is what a caller reporting to a user, such as the source generator, needs.
	/// </summary>
	internal static bool TryValidateIdentifier(string name, out string? reason)
	{
		if (string.IsNullOrEmpty(name))
		{
			reason = "The name must not be empty.";

			return false;
		}

		if (!IsAsciiLetter(name[0]) && name[0] != '_')
		{
			reason = $"'{name}' is not an ASCII identifier: it starts with '{name[0]}'.";

			return false;
		}

		foreach (char c in name)
		{
			if (!IsAsciiLetter(c) && (c < '0' || c > '9') && c != '_')
			{
				reason = $"'{name}' is not an ASCII identifier: it contains '{c}'.";

				return false;
			}
		}

		reason = null;

		return true;
	}

	static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

	/// <summary>
	/// What separates groups of three digits in a numeric literal, or <see langword="null"/> where
	/// the language has no such thing.
	/// </summary>
	/// <remarks>
	/// An underscore in C#, JavaScript, Java and Python; an apostrophe in C++14; nothing at all in
	/// C, which never adopted a separator.
	/// </remarks>
	protected virtual string? DigitSeparator => "_";

	/// <summary>
	/// The digits of a value, in groups of three where the language has a separator for them, and
	/// without a type suffix.
	/// </summary>
	protected string Grouped(ulong value)
	{
		string digits = value.ToString(CultureInfo.InvariantCulture);

		if (digits.Length <= 4 || this.DigitSeparator is not string separator) { return digits; }

		var builder = new StringBuilder();
		int leading = digits.Length % 3;

		if (leading == 0) { leading = 3; }

		builder.Append(digits, 0, leading);

		for (int i = leading; i < digits.Length; i += 3)
		{
			builder.Append(separator).Append(digits, i, 3);
		}

		return builder.ToString();
	}

	/// <summary>Whether any of a state's decode arcs picks a character by rank, wanting a local for it.</summary>
	protected static bool NeedsIndex(StateModel state)
	{
		foreach (ArcModel arc in state.Arcs)
		{
			if (arc.Set.Count > 1) { return true; }
		}

		return false;
	}
	#endregion
	#region The shape of one language
	/// <summary>Writes a function's header and opens its body.</summary>
	/// <remarks>
	/// A language's whole function syntax sits behind this and <see cref="CloseFunction"/>: C#
	/// writes a return type and a brace on a line of its own, JavaScript a keyword and a brace at
	/// the end of the line, and a Python emitter would write <c>def</c> and a colon and let the
	/// indenting do the rest. Every function these skeletons write returns the same thing, a state
	/// or a flag, so the return type belongs to the language rather than to the call.
	/// </remarks>
	protected abstract void OpenFunction(CodeWriter writer, string name, string parameters);

	/// <summary>Closes a body opened by <see cref="OpenFunction"/>.</summary>
	protected abstract void CloseFunction(CodeWriter writer);

	/// <summary>Opens the dispatch on the state, by whatever construct the language dispatches with.</summary>
	protected abstract void OpenDispatch(CodeWriter writer);

	/// <summary>Writes the result for a state the dispatch does not name, and closes it.</summary>
	protected abstract void CloseDispatch(CodeWriter writer, string result);

	/// <summary>Returns an expression, as one statement.</summary>
	protected abstract void WriteReturn(CodeWriter writer, string expression);

	/// <summary>Returns an expression where a condition holds, on one line.</summary>
	protected abstract void WriteGuardedReturn(CodeWriter writer, string condition, string expression);

	/// <summary>The test that the character in hand is one particular character.</summary>
	protected abstract string EqualsCharacter(char c);

	/// <summary>The test that the character in hand lies within an inclusive run.</summary>
	protected abstract string WithinRun(char first, char last);

	/// <summary>How the language spells 'or', which joins the tests of a set's runs.</summary>
	protected virtual string OrOperator => "||";

	/// <summary>The test for one inclusive run, which is an equality where the run holds one character.</summary>
	protected string RunCondition(char first, char last)
		=> first == last ? this.EqualsCharacter(first) : this.WithinRun(first, last);

	/// <summary>Membership of a whole set, as a test over its runs.</summary>
	protected string SetCondition(AsciiCharSet set)
	{
		List<(char First, char Last)> runs = GetRuns(set);

		if (runs.Count == 1) { return this.RunCondition(runs[0].First, runs[0].Last); }

		var builder = new StringBuilder();

		for (int i = 0; i < runs.Count; ++i)
		{
			if (i > 0) { builder.Append(' ').Append(this.OrOperator).Append(' '); }

			// A run of more than one character is a conjunction in most languages, so it is
			// bracketed where it sits beside another test.
			builder.Append(runs[i].First == runs[i].Last
				? this.EqualsCharacter(runs[i].First)
				: $"({this.WithinRun(runs[i].First, runs[i].Last)})");
		}

		return builder.ToString();
	}
	#endregion
	#region The stepper skeleton
	/// <summary>
	/// Emits a dispatch over states as one function, or over <see cref="ChunkSize"/> states at a
	/// time as a dispatcher and one function per chunk.
	/// </summary>
	/// <param name="writer">Where the fragment is going.</param>
	/// <param name="name">The function name, which chunks suffix with their number.</param>
	/// <param name="parameters">The parameter list. The first parameter must be the state.</param>
	/// <param name="arguments">The same list as arguments, for the dispatcher to pass on.</param>
	/// <param name="stateCount">The count of states.</param>
	/// <param name="emitCase">Writes one state's whole case, label included, or nothing to leave it to the default.</param>
	/// <param name="preamble">
	/// A declaration each function needs ahead of its dispatch, or <see langword="null"/> for none.
	/// </param>
	/// <param name="preambleNeeded">
	/// Whether a state's case uses the preamble. A function holding no such state leaves the
	/// declaration out, since an unused local is a warning in some of the target languages.
	/// </param>
	/// <param name="defaultResult">What a state the dispatch does not name returns.</param>
	protected void EmitStepFunctions(
		CodeWriter writer,
		string name,
		string parameters,
		string arguments,
		int stateCount,
		Action<int> emitCase,
		string? preamble = null,
		Func<int, bool>? preambleNeeded = null,
		string defaultResult = "-1")
	{
		if (stateCount <= ChunkSize)
		{
			this.EmitStepFunction(writer, name, parameters, 0, stateCount, emitCase, preamble, preambleNeeded, defaultResult);

			return;
		}

		int chunkCount = ((stateCount - 1) / ChunkSize) + 1;

		this.OpenFunction(writer, name, parameters);

		for (int chunk = 0; chunk < chunkCount - 1; ++chunk)
		{
			string bound = ((chunk + 1) * ChunkSize).ToString(CultureInfo.InvariantCulture);

			this.WriteGuardedReturn(writer, $"state < {bound}", $"{name}{chunk.ToString(CultureInfo.InvariantCulture)}({arguments})");
		}

		writer.Line();
		this.WriteReturn(writer, $"{name}{(chunkCount - 1).ToString(CultureInfo.InvariantCulture)}({arguments})");
		this.CloseFunction(writer);

		for (int chunk = 0; chunk < chunkCount; ++chunk)
		{
			int first = chunk * ChunkSize;
			int count = Math.Min(ChunkSize, stateCount - first);

			writer.Line();
			this.EmitStepFunction(
				writer,
				$"{name}{chunk.ToString(CultureInfo.InvariantCulture)}",
				parameters,
				first,
				count,
				emitCase,
				preamble,
				preambleNeeded,
				defaultResult);
		}
	}

	void EmitStepFunction(
		CodeWriter writer,
		string name,
		string parameters,
		int firstState,
		int stateCount,
		Action<int> emitCase,
		string? preamble,
		Func<int, bool>? preambleNeeded,
		string defaultResult)
	{
		this.OpenFunction(writer, name, parameters);

		if (preamble is not null && NeedsPreamble(firstState, stateCount, preambleNeeded))
		{
			writer.Line(preamble);
			writer.Line();
		}

		this.OpenDispatch(writer);

		for (int id = firstState; id < firstState + stateCount; ++id)
		{
			emitCase(id);
		}

		this.CloseDispatch(writer, defaultResult);
		this.CloseFunction(writer);
	}

	static bool NeedsPreamble(int firstState, int stateCount, Func<int, bool>? preambleNeeded)
	{
		if (preambleNeeded is null) { return true; }

		for (int id = firstState; id < firstState + stateCount; ++id)
		{
			if (preambleNeeded(id)) { return true; }
		}

		return false;
	}
	#endregion
	#region The per call context
	/// <summary>
	/// What one emission call works from: the naxp's machines in the form generated code takes,
	/// the prefix, and the writer the fragment goes through.
	/// </summary>
	internal sealed class Context
	{
		internal Context(Compilation compilation, string prefix, NaxpValueType valueType, CodeWriter writer)
		{
			this.Compilation = compilation;
			this.Prefix = prefix;
			this.ValueType = valueType;
			this.Writer = writer;

			this.CanonicalStates = BuildMachine(compilation.Canonical, withCounts: true);
			this.AcceptedStates = compilation.CanonicalIsIdentity
				? this.CanonicalStates
				: BuildMachine(compilation.Accepted, withCounts: false)
				;
			this.TransducerStates = compilation.CanonicalMachine is null
				? default
				: BuildTransducer(compilation.CanonicalMachine)
				;
			this.MaxLength = LongestPath(compilation.Canonical);
		}

		public Compilation Compilation { get; }

		/// <summary>The prefix every generated name starts with, possibly empty.</summary>
		public string Prefix { get; }

		/// <summary>The integer type for encoded values, already validated against the value count.</summary>
		public NaxpValueType ValueType { get; }

		/// <summary>The writer the fragment goes through, configured for the language.</summary>
		public CodeWriter Writer { get; }

		/// <summary>The machine for the accepted language <i>L</i>.</summary>
		public ImmutableArray<StateModel> AcceptedStates { get; }

		/// <summary>The machine for the canonical language <i>C</i>, which encode and decode rank over.</summary>
		public ImmutableArray<StateModel> CanonicalStates { get; }

		/// <summary>The canonicalisation machine, or default where &#961; is the identity.</summary>
		public ImmutableArray<TxStateModel> TransducerStates { get; }

		/// <summary>The length of the longest canonical string, which bounds every buffer the generated code needs.</summary>
		public int MaxLength { get; }
	}
	#endregion
	#region Building the models
	/// <summary>
	/// Renumbers a machine breadth first from its start.
	/// </summary>
	/// <param name="map">The machine.</param>
	/// <param name="withCounts">
	/// Whether to carry each transition's value counts, which encode and decode fold into their
	/// constants. The accepted machine's counts may be saturated and nothing generated reads
	/// them, so it does not carry them.
	/// </param>
	static ImmutableArray<StateModel> BuildMachine(StateMap map, bool withCounts)
	{
		var idOf = new Dictionary<State, int>();
		var ordered = new List<State> { map.Start };
		var queue = new Queue<State>();

		idOf.Add(map.Start, 0);
		queue.Enqueue(map.Start);

		while (queue.Count > 0)
		{
			foreach (Transition transition in queue.Dequeue().Transitions)
			{
				if (transition.Set.IsEmpty || idOf.ContainsKey(transition.Next)) { continue; }

				idOf.Add(transition.Next, ordered.Count);
				ordered.Add(transition.Next);
				queue.Enqueue(transition.Next);
			}
		}

		var states = new StateModel[ordered.Count];

		for (int id = 0; id < states.Length; ++id)
		{
			State state = ordered[id];
			var arcs = new List<ArcModel>();
			ulong skipped = 0UL;

			foreach (Transition transition in state.Transitions)
			{
				// The end of text transition sorts first, so where it exists every arc's skipped
				// count starts from the one value it stands for.
				if (transition.Set.IsEmpty)
				{
					skipped = 1UL;
					continue;
				}

				ulong count = withCounts ? transition.Next.ValueCount : 0UL;

				arcs.Add(new ArcModel(transition.Set, idOf[transition.Next], count, skipped));
				skipped += count * (ulong)transition.Set.Count;
			}

			states[id] = new StateModel(state.AcceptsEndOfText, arcs.ToImmutableArray());
		}

		return AsImmutable(states);
	}

	static ImmutableArray<TxStateModel> BuildTransducer(TxMachine machine)
	{
		var idOf = new Dictionary<TxState, int>();
		var ordered = new List<TxState> { machine.Start };
		var queue = new Queue<TxState>();

		idOf.Add(machine.Start, 0);
		queue.Enqueue(machine.Start);

		while (queue.Count > 0)
		{
			foreach (TxTransition transition in queue.Dequeue().Transitions)
			{
				if (idOf.ContainsKey(transition.Next)) { continue; }

				idOf.Add(transition.Next, ordered.Count);
				ordered.Add(transition.Next);
				queue.Enqueue(transition.Next);
			}
		}

		var states = new TxStateModel[ordered.Count];

		for (int id = 0; id < states.Length; ++id)
		{
			TxState state = ordered[id];

			// The builder narrows any block whose marker would survive the step that read it, so
			// an end output holding one would mean the machine is broken, not the naxp.
			if (state.EndOutput is not null && state.EndOutput.IndexOf(Tx.CopyMarker) >= 0)
			{
				throw new InvalidOperationException("A copy marker survived into an end output.");
			}

			var arcs = new List<TxArcModel>();

			foreach (TxTransition transition in state.Transitions)
			{
				arcs.Add(new TxArcModel(transition.Set, transition.Output, idOf[transition.Next]));
			}

			states[id] = new TxStateModel(state.EndOutput, arcs.ToImmutableArray());
		}

		return AsImmutable(states);
	}

	/// <summary>
	/// An array as an <see cref="ImmutableArray{T}"/>. On .NET 8 the array is wrapped rather than
	/// copied, which is safe because every caller hands over an array nothing else references.
	/// </summary>
	static ImmutableArray<T> AsImmutable<T>(T[] array)
	{
#if NET8_0_OR_GREATER
		return System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(array);
#else
		return ImmutableArray.Create(array);
#endif
	}

	/// <summary>
	/// The length of the longest string a machine generates.
	/// </summary>
	/// <remarks>
	/// The states are listed in creation order and every transition points at an earlier state,
	/// because the builder interns each state's successors before the state itself, so a single
	/// pass has every target's length ready when it is read.
	/// </remarks>
	static int LongestPath(StateMap map)
	{
		var lengths = new int[map.States.Count];

		for (int id = 0; id < map.States.Count; ++id)
		{
			int longest = 0;

			foreach (Transition transition in map.States[id].Transitions)
			{
				if (transition.Set.IsEmpty) { continue; }

				int viaTransition = lengths[transition.Next.Id] + 1;

				if (viaTransition > longest) { longest = viaTransition; }
			}

			lengths[id] = longest;
		}

		return lengths[map.Start.Id];
	}
	#endregion
	#region The models
	internal sealed class StateModel
	{
		public StateModel(bool acceptsEnd, ImmutableArray<ArcModel> arcs)
		{
			this.AcceptsEnd = acceptsEnd;
			this.Arcs = arcs;
		}

		public bool AcceptsEnd { get; }

		public ImmutableArray<ArcModel> Arcs { get; }
	}

	internal sealed class ArcModel
	{
		public ArcModel(AsciiCharSet set, int next, ulong nextCount, ulong skippedBefore)
		{
			this.Set = set;
			this.Next = next;
			this.NextCount = nextCount;
			this.SkippedBefore = skippedBefore;
		}

		public AsciiCharSet Set { get; }

		public int Next { get; }

		/// <summary>The count of values of the state this arc reaches.</summary>
		public ulong NextCount { get; }

		/// <summary>The count of values sitting below this arc in its state's order.</summary>
		public ulong SkippedBefore { get; }
	}

	internal sealed class TxStateModel
	{
		public TxStateModel(string? endOutput, ImmutableArray<TxArcModel> arcs)
		{
			this.EndOutput = endOutput;
			this.Arcs = arcs;
		}

		public string? EndOutput { get; }

		public ImmutableArray<TxArcModel> Arcs { get; }
	}

	internal sealed class TxArcModel
	{
		public TxArcModel(AsciiCharSet set, string output, int next)
		{
			this.Set = set;
			this.Output = output;
			this.Next = next;
		}

		public AsciiCharSet Set { get; }

		public string Output { get; }

		public int Next { get; }
	}
	#endregion
}
