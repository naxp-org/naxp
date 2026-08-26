// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace LogMu;

/// <summary>
/// Emits a compiled naxp as a C# fragment.
/// </summary>
/// <remarks>
/// <para>
/// The fragment is a set of static members - two consts, the public methods and their private
/// steppers - answering the same questions as <see cref="Naxp"/> for its one naxp, without
/// calling back into this library. Self-containment is a requirement rather than a taste: the
/// code lands in the caller's assembly, where nothing internal to this one is visible, and the
/// source generator that will carry it cannot resolve package references of its own. The
/// wrapping class, any namespace and any header comment are the caller's job. System types are
/// spelt <c>global::System.…</c>, the source generator convention, so no using directive or
/// shadowing type in the wrapper can disturb the fragment.
/// </para>
/// <para>
/// Every machine is emitted as a switch over states, never as a table. A table walk pays a
/// popcount over two ulongs per character to rank the character within its set; in a switch the
/// rank constant-folds into the arithmetic of its case. The table fallback the earlier plan
/// carried was set against a cap of 100 000 states, and with <see cref="NaxpLimits.MaxStates"/>
/// at 2 000 no machine is large enough to want it.
/// </para>
/// <para>
/// A switch is split into methods of at most <see cref="Emitter.ChunkSize"/> states, dispatched
/// by state number; that constant carries the per-method limits behind the split.
/// </para>
/// </remarks>
sealed class CSharpEmitter : Emitter
{
	/// <summary>The shared instance, which is stateless and serves every call concurrently.</summary>
	public static CSharpEmitter Instance { get; } = new();

	protected override void Emit(Context context) => new Fragment(this, context).Emit();

	#region The shape of C#
	/// <inheritdoc/>
	protected override void OpenFunction(CodeWriter writer, string name, string parameters)
	{
		writer.Line($"static int {name}({parameters})");
		writer.OpenBlock();
	}

	/// <inheritdoc/>
	protected override void CloseFunction(CodeWriter writer) => writer.CloseBlock();

	/// <inheritdoc/>
	protected override void OpenDispatch(CodeWriter writer)
	{
		writer.Line("switch (state)");
		writer.OpenBlock();
	}

	/// <inheritdoc/>
	protected override void CloseDispatch(CodeWriter writer, string result)
	{
		writer.Line("default:");
		writer.Indent();
		writer.Line($"return {result};");
		writer.Outdent();
		writer.CloseBlock();
	}

	/// <inheritdoc/>
	protected override void WriteReturn(CodeWriter writer, string expression) => writer.Line($"return {expression};");

	/// <inheritdoc/>
	protected override void WriteGuardedReturn(CodeWriter writer, string condition, string expression)
		=> writer.Line($"if ({condition}) {{ return {expression}; }}");

	/// <inheritdoc/>
	protected override string EqualsCharacter(char c) => $"c == {CharLiteral(c)}";

	/// <inheritdoc/>
	protected override string WithinRun(char first, char last)
		=> $"c >= {CharLiteral(first)} && c <= {CharLiteral(last)}";
	#endregion
	/// <summary>
	/// One emission call's state: the context and the generated names built from the prefix. Per
	/// call so the shared instance stays stateless.
	/// </summary>
	sealed class Fragment
	{
		readonly CSharpEmitter emitter;
		readonly Context context;

		// The generated names, each the prefix plus the bare member name.
		readonly string valueCountName;
		readonly string maxLengthName;
		readonly string acceptsName;
		readonly string encodeName;
		readonly string decodeName;
		readonly string decodeToBytesName;
		readonly string tryDecodeName;
		readonly string rankName;
		readonly string decodeCoreName;
		readonly string acceptStepName;
		readonly string isAcceptingName;
		readonly string encodeStepName;
		readonly string isCanonicalAcceptingName;
		readonly string decodeStepName;
		readonly string canonicalStepName;
		readonly string finishCanonicalName;

		// The System types the fragment uses, fully qualified.
		const string readOnlySpanOfChar = "global::System.ReadOnlySpan<char>";
		const string readOnlySpanOfByte = "global::System.ReadOnlySpan<byte>";
		const string spanOfChar = "global::System.Span<char>";
		const string spanOfByte = "global::System.Span<byte>";
		const string argumentOutOfRangeException = "global::System.ArgumentOutOfRangeException";

		// The C# spelling of the chosen value type. The steppers work in ulong throughout
		// whatever the choice, because C# promotes narrow operands to int anyway and casts
		// through the switch bodies would be pure noise; only the public boundary changes.
		readonly string valueKeyword;
		readonly string valueCountValue;
		readonly string valueZero;
		readonly string valueOne;
		readonly string decodeCoreArgument;
		readonly bool valueIsWidest;

		public Fragment(CSharpEmitter emitter, Context context)
		{
			this.emitter = emitter;
			this.context = context;

			string valueSuffix;

			switch (context.ValueType)
			{
				case NaxpValueType.Int8: this.valueKeyword = "sbyte"; valueSuffix = ""; break;
				case NaxpValueType.UInt8: this.valueKeyword = "byte"; valueSuffix = ""; break;
				case NaxpValueType.Int16: this.valueKeyword = "short"; valueSuffix = ""; break;
				case NaxpValueType.UInt16: this.valueKeyword = "ushort"; valueSuffix = ""; break;
				case NaxpValueType.Int32: this.valueKeyword = "int"; valueSuffix = ""; break;
				case NaxpValueType.UInt32: this.valueKeyword = "uint"; valueSuffix = "U"; break;
				case NaxpValueType.Int64: this.valueKeyword = "long"; valueSuffix = "L"; break;
				case NaxpValueType.UInt64: this.valueKeyword = "ulong"; valueSuffix = "UL"; break;
				default: throw new InvalidOperationException($"Unhandled value type {context.ValueType}.");
			}

			this.valueIsWidest = context.ValueType == NaxpValueType.UInt64;
			this.valueCountValue = emitter.Grouped(context.Compilation.ValueCount) + valueSuffix;
			this.valueZero = this.valueIsWidest ? "0UL" : "0";
			this.valueOne = this.valueIsWidest ? "1UL" : "1";

			// DecodeCore takes ulong. Only ulong reaches it unconverted; every other type needs
			// the cast, which the range check just made safe.
			this.decodeCoreArgument = context.ValueType == NaxpValueType.UInt64 ? "value" : "(ulong)value";

			string prefix = context.Prefix;
			this.valueCountName = prefix + "ValueCount";
			this.maxLengthName = prefix + "MaxLength";
			this.acceptsName = prefix + "Accepts";
			this.encodeName = prefix + "Encode";
			this.decodeName = prefix + "Decode";
			this.decodeToBytesName = prefix + "DecodeToBytes";
			this.tryDecodeName = prefix + "TryDecode";
			this.rankName = prefix + "Rank";
			this.decodeCoreName = prefix + "DecodeCore";
			this.acceptStepName = prefix + "AcceptStep";
			this.isAcceptingName = prefix + "IsAccepting";
			this.encodeStepName = prefix + "EncodeStep";
			this.isCanonicalAcceptingName = prefix + "IsCanonicalAccepting";
			this.decodeStepName = prefix + "DecodeStep";
			this.canonicalStepName = prefix + "CanonicalStep";
			this.finishCanonicalName = prefix + "FinishCanonical";
		}

		CodeWriter Writer => this.context.Writer;

		ImmutableArray<StateModel> AcceptedStates => this.context.AcceptedStates;

		ImmutableArray<StateModel> CanonicalStates => this.context.CanonicalStates;

		ImmutableArray<TxStateModel> TransducerStates => this.context.TransducerStates;

		int MaxLength => this.context.MaxLength;

		public void Emit()
		{
			this.EmitConstants();
			this.EmitAccepts(bytes: false);
			this.EmitAccepts(bytes: true);
			this.EmitEncode(bytes: false);
			this.EmitEncode(bytes: true);
			this.EmitDecodePublics();
			this.EmitSteppers();
		}

		void EmitConstants()
		{
			this.Writer.Line("/// <summary>The count of values this naxp encodes, which is the largest value it can produce.</summary>");
			this.Writer.Line($"public const {this.valueKeyword} {this.valueCountName} = {this.valueCountValue};");
			this.Writer.Line();
			this.Writer.Line("/// <summary>The length of the longest string this naxp can decode a value to.</summary>");
			this.Writer.Line($"public const int {this.maxLengthName} = {this.MaxLength.ToString(CultureInfo.InvariantCulture)};");
			this.Writer.Line();
		}

		#region Public methods
		void EmitAccepts(bool bytes)
		{
			if (bytes)
			{
				this.Writer.Line("/// <summary>Whether this naxp accepts the specified ASCII text. A byte outside ASCII is never accepted.</summary>");
				this.Writer.Line($"public static bool {this.acceptsName}({readOnlySpanOfByte} text)");
			}
			else
			{
				this.Writer.Line("/// <summary>Whether this naxp accepts the specified string.</summary>");
				this.Writer.Line($"public static bool {this.acceptsName}({readOnlySpanOfChar} text)");
			}

			this.Writer.OpenBlock();
			this.Writer.Line("int state = 0;");
			this.Writer.Line();
			this.Writer.Line(bytes ? "foreach (byte b in text)" : "foreach (char c in text)");
			this.Writer.OpenBlock();
			this.Writer.Line(bytes
				? $"state = {this.acceptStepName}(state, (char)b);"
				: $"state = {this.acceptStepName}(state, c);");
			this.Writer.Line();
			this.Writer.Line("if (state < 0) { return false; }");
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line($"return {this.isAcceptingName}(state);");
			this.Writer.CloseBlock();
			this.Writer.Line();
		}

		void EmitEncode(bool bytes)
		{
			if (bytes)
			{
				this.Writer.Line($"/// <summary>The value of ASCII text, from 1 to <see cref=\"{this.valueCountName}\"/>, or zero where this naxp does not accept it.</summary>");
				this.Writer.Line($"public static {this.valueKeyword} {this.encodeName}({readOnlySpanOfByte} text)");
			}
			else
			{
				this.Writer.Line($"/// <summary>The value of a string, from 1 to <see cref=\"{this.valueCountName}\"/>, or zero where this naxp does not accept it.</summary>");
				this.Writer.Line($"public static {this.valueKeyword} {this.encodeName}({readOnlySpanOfChar} text)");
			}

			this.Writer.OpenBlock();

			if (this.TransducerStates.IsDefault)
			{
				this.Writer.Line("int state = 0;");
				this.Writer.Line("ulong total = 0UL;");
				this.Writer.Line();
				this.Writer.Line(bytes ? "foreach (byte b in text)" : "foreach (char c in text)");
				this.Writer.OpenBlock();
				this.Writer.Line(bytes
					? $"state = {this.encodeStepName}(state, (char)b, ref total);"
					: $"state = {this.encodeStepName}(state, c, ref total);");
				this.Writer.Line();
				this.Writer.Line($"if (state < 0) {{ return {this.valueZero}; }}");
				this.Writer.CloseBlock();
				this.Writer.Line();
				this.Writer.Line(this.valueIsWidest
					? $"return {this.isAcceptingName}(state) ? total + 1UL : 0UL;"
					: $"return {this.isAcceptingName}(state) ? ({this.valueKeyword})(total + 1UL) : ({this.valueKeyword})0;");
			}
			else
			{
				this.Writer.Line($"{spanOfChar} canonical = stackalloc char[{this.maxLengthName}];");
				this.Writer.Line("int length = 0;");
				this.Writer.Line("int state = 0;");
				this.Writer.Line();
				this.Writer.Line(bytes ? "foreach (byte b in text)" : "foreach (char c in text)");
				this.Writer.OpenBlock();
				this.Writer.Line(bytes
					? $"state = {this.canonicalStepName}(state, (char)b, canonical, ref length);"
					: $"state = {this.canonicalStepName}(state, c, canonical, ref length);");
				this.Writer.Line();
				this.Writer.Line($"if (state < 0) {{ return {this.valueZero}; }}");
				this.Writer.CloseBlock();
				this.Writer.Line();
				this.Writer.Line($"length = {this.finishCanonicalName}(state, canonical, length);");
				this.Writer.Line();
				this.Writer.Line(this.valueIsWidest
					? $"return length < 0 ? 0UL : {this.rankName}(canonical.Slice(0, length));"
					: $"return length < 0 ? ({this.valueKeyword})0 : ({this.valueKeyword}){this.rankName}(canonical.Slice(0, length));");
			}

			this.Writer.CloseBlock();
			this.Writer.Line();
		}

		void EmitDecodePublics()
		{
			string countDigits = this.context.Compilation.ValueCount.ToString(CultureInfo.InvariantCulture);
			string throwStatement =
				$"throw new {argumentOutOfRangeException}(nameof(value), value, \"This naxp encodes the values 1 to {countDigits}.\");";

			this.Writer.Line("/// <summary>The string a value stands for, which is in canonical form.</summary>");
			this.Writer.Line($"/// <exception cref=\"{argumentOutOfRangeException}\">The value is not one this naxp produces.</exception>");
			this.Writer.Line($"public static string {this.decodeName}({this.valueKeyword} value)");
			this.Writer.OpenBlock();
			this.Writer.Line($"if (value < {this.valueOne} || value > {this.valueCountName})");
			this.Writer.OpenBlock();
			this.Writer.Line(throwStatement);
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line($"{spanOfChar} destination = stackalloc char[{this.maxLengthName}];");
			this.Writer.Line();
			this.Writer.Line($"return destination.Slice(0, {this.decodeCoreName}({this.decodeCoreArgument}, destination)).ToString();");
			this.Writer.CloseBlock();
			this.Writer.Line();

			this.Writer.Line("/// <summary>The string a value stands for, as ASCII bytes.</summary>");
			this.Writer.Line($"/// <exception cref=\"{argumentOutOfRangeException}\">The value is not one this naxp produces.</exception>");
			this.Writer.Line($"public static byte[] {this.decodeToBytesName}({this.valueKeyword} value)");
			this.Writer.OpenBlock();
			this.Writer.Line($"if (value < {this.valueOne} || value > {this.valueCountName})");
			this.Writer.OpenBlock();
			this.Writer.Line(throwStatement);
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line($"{spanOfChar} buffer = stackalloc char[{this.maxLengthName}];");
			this.Writer.Line($"int length = {this.decodeCoreName}({this.decodeCoreArgument}, buffer);");
			this.Writer.Line("var result = new byte[length];");
			this.Writer.Line();
			this.Writer.Line("for (int i = 0; i < length; ++i) { result[i] = (byte)buffer[i]; }");
			this.Writer.Line();
			this.Writer.Line("return result;");
			this.Writer.CloseBlock();
			this.Writer.Line();

			this.Writer.Line("/// <summary>Tries to write the string a value stands for. False where the value is not one");
			this.Writer.Line("/// this naxp produces, or the destination is too short for the string.</summary>");
			this.Writer.Line($"public static bool {this.tryDecodeName}({this.valueKeyword} value, {spanOfChar} destination, out int charsWritten)");
			this.Writer.OpenBlock();
			this.Writer.Line($"if (value < {this.valueOne} || value > {this.valueCountName})");
			this.Writer.OpenBlock();
			this.Writer.Line("charsWritten = 0;");
			this.Writer.Line("return false;");
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line($"if (destination.Length >= {this.maxLengthName})");
			this.Writer.OpenBlock();
			this.Writer.Line($"charsWritten = {this.decodeCoreName}({this.decodeCoreArgument}, destination);");
			this.Writer.Line("return true;");
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line($"{spanOfChar} buffer = stackalloc char[{this.maxLengthName}];");
			this.Writer.Line($"int length = {this.decodeCoreName}({this.decodeCoreArgument}, buffer);");
			this.Writer.Line();
			this.Writer.Line("if (length > destination.Length)");
			this.Writer.OpenBlock();
			this.Writer.Line("charsWritten = 0;");
			this.Writer.Line("return false;");
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line("buffer.Slice(0, length).CopyTo(destination);");
			this.Writer.Line("charsWritten = length;");
			this.Writer.Line("return true;");
			this.Writer.CloseBlock();
			this.Writer.Line();

			this.Writer.Line("/// <summary>Tries to write the string a value stands for, as ASCII bytes. False where the value");
			this.Writer.Line("/// is not one this naxp produces, or the destination is too short for the string.</summary>");
			this.Writer.Line($"public static bool {this.tryDecodeName}({this.valueKeyword} value, {spanOfByte} destination, out int bytesWritten)");
			this.Writer.OpenBlock();
			this.Writer.Line($"if (value < {this.valueOne} || value > {this.valueCountName})");
			this.Writer.OpenBlock();
			this.Writer.Line("bytesWritten = 0;");
			this.Writer.Line("return false;");
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line($"{spanOfChar} buffer = stackalloc char[{this.maxLengthName}];");
			this.Writer.Line($"int length = {this.decodeCoreName}({this.decodeCoreArgument}, buffer);");
			this.Writer.Line();
			this.Writer.Line("if (length > destination.Length)");
			this.Writer.OpenBlock();
			this.Writer.Line("bytesWritten = 0;");
			this.Writer.Line("return false;");
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line("for (int i = 0; i < length; ++i) { destination[i] = (byte)buffer[i]; }");
			this.Writer.Line();
			this.Writer.Line("bytesWritten = length;");
			this.Writer.Line("return true;");
			this.Writer.CloseBlock();
			this.Writer.Line();
		}
		#endregion
		#region Private methods
		void EmitSteppers()
		{
			if (!this.TransducerStates.IsDefault)
			{
				this.Writer.Line("/// <summary>The rank of a canonical string within the canonical language, or zero where it is not in it.</summary>");
				this.Writer.Line($"static ulong {this.rankName}({readOnlySpanOfChar} canonical)");
				this.Writer.OpenBlock();
				this.Writer.Line("int state = 0;");
				this.Writer.Line("ulong total = 0UL;");
				this.Writer.Line();
				this.Writer.Line("foreach (char c in canonical)");
				this.Writer.OpenBlock();
				this.Writer.Line($"state = {this.encodeStepName}(state, c, ref total);");
				this.Writer.Line();
				this.Writer.Line("if (state < 0) { return 0UL; }");
				this.Writer.CloseBlock();
				this.Writer.Line();
				this.Writer.Line($"return {this.isCanonicalAcceptingName}(state) ? total + 1UL : 0UL;");
				this.Writer.CloseBlock();
				this.Writer.Line();
			}

			this.Writer.Line($"/// <summary>Writes the string of a value that was already checked against <see cref=\"{this.valueCountName}\"/>, and returns its length.</summary>");
			this.Writer.Line($"static int {this.decodeCoreName}(ulong value, {spanOfChar} destination)");
			this.Writer.OpenBlock();
			this.Writer.Line("ulong remaining = value;");
			this.Writer.Line("int state = 0;");
			this.Writer.Line("int length = 0;");
			this.Writer.Line();
			this.Writer.Line("while (state >= 0)");
			this.Writer.OpenBlock();
			this.Writer.Line($"state = {this.decodeStepName}(state, ref remaining, destination, ref length);");
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line("return length;");
			this.Writer.CloseBlock();
			this.Writer.Line();

			this.Writer.Line("/// <summary>The acceptor's transition: the next state, or -1 where the character fits nothing.</summary>");
			this.emitter.EmitStepFunctions(this.Writer, 
				this.acceptStepName,
				"int state, char c",
				"state, c",
				this.AcceptedStates.Length,
				this.EmitAcceptCase);
			this.Writer.Line();

			this.EmitAcceptingPredicate(this.isAcceptingName, this.AcceptedStates);
			this.Writer.Line();

			this.Writer.Line("/// <summary>The canonical machine's transition, accumulating the values skipped: the next state, or -1.</summary>");
			this.emitter.EmitStepFunctions(this.Writer, 
				this.encodeStepName,
				"int state, char c, ref ulong total",
				"state, c, ref total",
				this.CanonicalStates.Length,
				this.EmitEncodeCase);
			this.Writer.Line();

			if (!this.TransducerStates.IsDefault)
			{
				this.EmitAcceptingPredicate(this.isCanonicalAcceptingName, this.CanonicalStates);
				this.Writer.Line();
			}

			this.Writer.Line("/// <summary>One step of decoding: appends at most one character and returns the next state, or -1 when the string is complete.</summary>");
			this.emitter.EmitStepFunctions(this.Writer, 
				this.decodeStepName,
				$"int state, ref ulong remaining, {spanOfChar} destination, ref int length",
				"state, ref remaining, destination, ref length",
				this.CanonicalStates.Length,
				this.EmitDecodeCase,
				"ulong index;",
				id => NeedsIndex(this.CanonicalStates[id]));

			if (!this.TransducerStates.IsDefault)
			{
				this.Writer.Line();
				this.Writer.Line("/// <summary>The canonicalising transition, appending what reading the character emits: the next state, or -1.</summary>");
				this.emitter.EmitStepFunctions(this.Writer, 
					this.canonicalStepName,
					$"int state, char c, {spanOfChar} canonical, ref int length",
					"state, c, canonical, ref length",
					this.TransducerStates.Length,
					this.EmitCanonicalCase);
				this.Writer.Line();

				this.Writer.Line("/// <summary>Appends what ending the input emits and returns the final length, or -1 where the input may not end here.</summary>");
				this.emitter.EmitStepFunctions(this.Writer, 
					this.finishCanonicalName,
					$"int state, {spanOfChar} canonical, int length",
					"state, canonical, length",
					this.TransducerStates.Length,
					this.EmitFinishCase);
			}
		}


		void EmitAcceptCase(int id)
		{
			StateModel state = this.AcceptedStates[id];

			this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}:");
			this.Writer.Indent();

			foreach (ArcModel arc in state.Arcs)
			{
				this.Writer.Line($"if ({this.emitter.SetCondition(arc.Set)}) {{ return {arc.Next.ToString(CultureInfo.InvariantCulture)}; }}");
			}

			this.Writer.Line("return -1;");
			this.Writer.Outdent();
		}

		void EmitEncodeCase(int id)
		{
			StateModel state = this.CanonicalStates[id];

			this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}:");
			this.Writer.Indent();

			foreach (ArcModel arc in state.Arcs)
			{
				ulong offset = 0UL;

				foreach ((char first, char last) in GetRuns(arc.Set))
				{
					// Passing this run skips the values below it: those skipped before the whole
					// transition, and this transition's earlier runs. Within the run the character's
					// rank folds into (c - first).
					ulong skipped = arc.SkippedBefore + (arc.NextCount * offset);
					string? added = this.emitter.AddedExpression(skipped, arc.NextCount, first, last);
					string next = arc.Next.ToString(CultureInfo.InvariantCulture);

					this.Writer.Line(added is null
						? $"if ({this.emitter.RunCondition(first, last)}) {{ return {next}; }}"
						: $"if ({this.emitter.RunCondition(first, last)}) {{ total += {added}; return {next}; }}");

					offset += (ulong)(last - first + 1);
				}
			}

			this.Writer.Line("return -1;");
			this.Writer.Outdent();
		}

		void EmitDecodeCase(int id)
		{
			StateModel state = this.CanonicalStates[id];

			this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}:");
			this.Writer.OpenBlock();

			if (state.Arcs.Length == 0)
			{
				// The terminal state. The remaining value is one here, because the caller checked the
				// value against the count of the start state and every step keeps it within the count
				// of the state it moves to.
				this.Writer.Line("return -1;");
				this.Writer.CloseBlock();
				return;
			}

			if (state.AcceptsEnd)
			{
				this.Writer.Line("if (remaining == 1UL) { return -1; }");
				this.Writer.Line();
				this.Writer.Line("remaining -= 1UL;");
			}

			for (int i = 0; i < state.Arcs.Length; ++i)
			{
				ArcModel arc = state.Arcs[i];
				ulong block = arc.NextCount * (ulong)arc.Set.Count;

				if (i < state.Arcs.Length - 1)
				{
					if (i > 0 || state.AcceptsEnd) { this.Writer.Line(); }

					this.Writer.Line($"if (remaining <= {this.emitter.Literal(block)})");
					this.Writer.OpenBlock();
					this.EmitDecodeArc(arc);
					this.Writer.CloseBlock();
					this.Writer.Line();
					this.Writer.Line($"remaining -= {this.emitter.Literal(block)};");
				}
				else
				{
					// The last transition takes whatever is left, by the same invariant as the
					// terminal state above.
					if (i > 0 || state.AcceptsEnd) { this.Writer.Line(); }

					this.EmitDecodeArc(arc);
				}
			}

			this.Writer.CloseBlock();
		}

		void EmitDecodeArc(ArcModel arc)
		{
			string next = arc.Next.ToString(CultureInfo.InvariantCulture);
			List<(char First, char Last)> runs = GetRuns(arc.Set);

			if (arc.Set.Count == 1)
			{
				// One character leaves the remaining value untouched: its rank is zero and the whole
				// block belongs to the next state.
				this.Writer.Line($"destination[length++] = {CharLiteral(runs[0].First)};");
				this.Writer.Line($"return {next};");
				return;
			}

			// index is declared once at the top of the method, because these arcs sit at differing
			// brace depths within one switch and sibling declarations there collide.
			this.Writer.Line(arc.NextCount == 1UL
				? "index = remaining - 1UL;"
				: $"index = (remaining - 1UL) / {this.emitter.Literal(arc.NextCount)};");
			this.Writer.Line($"destination[length++] = {this.emitter.CharacterExpression(runs)};");
			this.Writer.Line(arc.NextCount == 1UL
				? "remaining = 1UL;"
				: $"remaining = ((remaining - 1UL) % {this.emitter.Literal(arc.NextCount)}) + 1UL;");
			this.Writer.Line($"return {next};");
		}

		void EmitCanonicalCase(int id)
		{
			TxStateModel state = this.TransducerStates[id];

			this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}:");
			this.Writer.Indent();

			foreach (TxArcModel arc in state.Arcs)
			{
				string condition = this.emitter.SetCondition(arc.Set);
				string next = arc.Next.ToString(CultureInfo.InvariantCulture);

				if (arc.Output.Length == 0)
				{
					this.Writer.Line($"if ({condition}) {{ return {next}; }}");
				}
				else if (arc.Output.Length == 1)
				{
					this.Writer.Line($"if ({condition}) {{ canonical[length++] = {OutputExpression(arc.Output[0])}; return {next}; }}");
				}
				else
				{
					this.Writer.Line($"if ({condition})");
					this.Writer.OpenBlock();

					foreach (char output in arc.Output)
					{
						this.Writer.Line($"canonical[length++] = {OutputExpression(output)};");
					}

					this.Writer.Line($"return {next};");
					this.Writer.CloseBlock();
				}
			}

			this.Writer.Line("return -1;");
			this.Writer.Outdent();
		}

		void EmitFinishCase(int id)
		{
			TxStateModel state = this.TransducerStates[id];

			// A state where the input may not end has no case, so it falls to the default.
			if (state.EndOutput is null) { return; }

			this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}:");
			this.Writer.Indent();

			foreach (char output in state.EndOutput)
			{
				this.Writer.Line($"canonical[length++] = {CharLiteral(output)};");
			}

			this.Writer.Line("return length;");
			this.Writer.Outdent();
		}

		void EmitAcceptingPredicate(string name, ImmutableArray<StateModel> states)
		{
			this.Writer.Line("/// <summary>Whether the input may end in this state.</summary>");
			this.Writer.Line($"static bool {name}(int state)");
			this.Writer.OpenBlock();
			this.Writer.Line("switch (state)");
			this.Writer.OpenBlock();

			for (int id = 0; id < states.Length; ++id)
			{
				if (states[id].AcceptsEnd)
				{
					this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}:");
				}
			}

			this.Writer.Indent();
			this.Writer.Line("return true;");
			this.Writer.Outdent();
			this.Writer.Line("default:");
			this.Writer.Indent();
			this.Writer.Line("return false;");
			this.Writer.Outdent();
			this.Writer.CloseBlock();
			this.Writer.CloseBlock();
		}
		#endregion
	}

	#region Expression helpers
	/// <summary>
	/// What passing this run adds to the total, or <see langword="null"/> where it adds nothing.
	/// </summary>
	string? AddedExpression(ulong skipped, ulong count, char first, char last)
	{
		if (first == last) { return skipped == 0UL ? null : Literal(skipped); }

		// The accumulator is unsigned, and C# will not mix ulong with the int this subtraction
		// gives, so the rank is converted here rather than at every use.
		string index = $"(ulong)(c - {CharLiteral(first)})";
		string term = count == 1UL ? index : $"{Literal(count)} * {index}";

		return skipped == 0UL ? term : $"{Literal(skipped)} + {term}";
	}

	/// <summary>
	/// The character at position <c>index</c> within a set, as an expression over its runs.
	/// </summary>
	string CharacterExpression(List<(char First, char Last)> runs)
	{
		if (runs.Count == 1) { return RunCharacter(runs[0], 0UL); }

		var builder = new StringBuilder();
		ulong cumulative = 0UL;

		for (int i = 0; i < runs.Count - 1; ++i)
		{
			ulong offset = cumulative;
			cumulative += (ulong)(runs[i].Last - runs[i].First + 1);
			builder.Append($"index < {Literal(cumulative)} ? {RunCharacter(runs[i], offset)} : ");
		}

		builder.Append(RunCharacter(runs[runs.Count - 1], cumulative));

		return builder.ToString();
	}

	string RunCharacter((char First, char Last) run, ulong offset)
	{
		if (run.First == run.Last) { return CharLiteral(run.First); }

		string index = offset == 0UL ? "(int)index" : $"(int)(index - {Literal(offset)})";

		return $"(char)({CharLiteral(run.First)} + {index})";
	}


	/// <summary>A transducer output character: the copy marker stands for the character just read.</summary>
	static string OutputExpression(char output) => output == Tx.CopyMarker ? "c" : CharLiteral(output);

	static string CharLiteral(char c)
	{
		switch (c)
		{
			case '\'': return @"'\''";
			case '\\': return @"'\\'";
			default:
				return c >= ' ' && c <= '~'
					? $"'{c}'"
					: $@"'\u{((int)c).ToString("X4", CultureInfo.InvariantCulture)}'"
					;
		}
	}

	/// <summary>An unsigned literal, grouped with underscores the way this codebase writes big numbers.</summary>
	string Literal(ulong value) => Grouped(value) + "UL";
	#endregion
}
