// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace LogMu;

/// <summary>
/// The JavaScript emitter: one naxp as a fragment of declarations, in the shape a module or a
/// script tag can hold.
/// </summary>
/// <remarks>
/// <para>
/// The fragment is a set of const and function declarations - two consts, the public functions and
/// their private steppers - every name prefixed with the caller's prefix and camel cased, which is
/// what JavaScript readers expect. Nothing is exported: the module wrapper, the export list and any
/// header comment are the caller's job.
/// </para>
/// <para>
/// Characters are handled as ASCII code points rather than one-character strings, so the steppers
/// compare numbers and the byte entry points feed their bytes straight in. That is why one stepper
/// serves both the string and the <c>Uint8Array</c> forms, where the C# emitter needs a cast.
/// </para>
/// <para>
/// JavaScript has one number type, exact to 2^53 - 1, so the emitter reads the naxp's value count
/// and picks: ordinary numbers where every value and every intermediate rank fits, BigInt above
/// that. Ranks are bounded by the value count, so in the number case the arithmetic is exact.
/// The BigInt case needs ES2020; everything else needs ES2015.
/// </para>
/// </remarks>
sealed class JavaScriptEmitter : Emitter
{
	/// <summary>The largest integer a JavaScript number holds exactly, <c>Number.MAX_SAFE_INTEGER</c>.</summary>
	const ulong MaxSafeInteger = 9_007_199_254_740_991UL;

	/// <summary>The shared instance, which is stateless and serves every call concurrently.</summary>
	public static JavaScriptEmitter Instance { get; } = new();

	/// <summary>
	/// JavaScript puts an opening brace at the end of the line it belongs to, so the fragment
	/// writes its own braces and takes only the indenting from <see cref="CodeWriter"/>.
	/// </summary>
	JavaScriptEmitter()
		: base("\t", blockOpen: null, blockClose: null)
	{
	}

	protected override void Emit(Context context) => new Fragment(this, context).Emit();

	#region The shape of JavaScript
	/// <inheritdoc/>
	protected override void OpenFunction(CodeWriter writer, string name, string parameters)
	{
		writer.Line($"function {name}({parameters}) {{");
		writer.Indent();
	}

	/// <inheritdoc/>
	protected override void CloseFunction(CodeWriter writer)
	{
		writer.Outdent();
		writer.Line("}");
	}

	/// <inheritdoc/>
	protected override void OpenDispatch(CodeWriter writer)
	{
		writer.Line("switch (state) {");
		writer.Indent();
	}

	/// <inheritdoc/>
	protected override void CloseDispatch(CodeWriter writer, string result)
	{
		writer.Line("default:");
		writer.Indent();
		writer.Line($"return {result};");
		writer.Outdent();
		writer.Outdent();
		writer.Line("}");
	}

	/// <inheritdoc/>
	protected override void WriteReturn(CodeWriter writer, string expression) => writer.Line($"return {expression};");

	/// <inheritdoc/>
	protected override void WriteGuardedReturn(CodeWriter writer, string condition, string expression)
		=> writer.Line($"if ({condition}) {{ return {expression}; }}");

	/// <inheritdoc/>
	protected override string EqualsCharacter(char c) => $"c === {CodeLiteral(c)}";

	/// <inheritdoc/>
	protected override string WithinRun(char first, char last)
		=> $"c >= {CodeLiteral(first)} && c <= {CodeLiteral(last)}";
	#endregion
	/// <summary>
	/// One emission call's state: the context, the generated names, and the choice between numbers
	/// and BigInt. Per call so the shared instance stays stateless.
	/// </summary>
	sealed class Fragment
	{
		readonly JavaScriptEmitter emitter;
		readonly Context context;

		// The generated names, each the prefix plus the bare member name, camel cased.
		readonly string valueCountName;
		readonly string maxLengthName;
		readonly string acceptsName;
		readonly string acceptsBytesName;
		readonly string encodeName;
		readonly string encodeBytesName;
		readonly string decodeName;
		readonly string decodeToBytesName;
		readonly string rankName;
		readonly string decodeCoreName;
		readonly string acceptStepName;
		readonly string isAcceptingName;
		readonly string encodeStepName;
		readonly string isCanonicalAcceptingName;
		readonly string decodeStepName;
		readonly string canonicalStepName;
		readonly string finishCanonicalName;

		/// <summary>Whether values are BigInt rather than number.</summary>
		readonly bool big;

		readonly string zero;
		readonly string one;

		public Fragment(JavaScriptEmitter emitter, Context context)
		{
			this.emitter = emitter;
			this.context = context;
			this.big = context.Compilation.ValueCount > MaxSafeInteger;
			this.zero = this.big ? "0n" : "0";
			this.one = this.big ? "1n" : "1";

			string prefix = context.Prefix;
			this.valueCountName = Camel(prefix, "ValueCount");
			this.maxLengthName = Camel(prefix, "MaxLength");
			this.acceptsName = Camel(prefix, "Accepts");
			this.acceptsBytesName = Camel(prefix, "AcceptsBytes");
			this.encodeName = Camel(prefix, "Encode");
			this.encodeBytesName = Camel(prefix, "EncodeBytes");
			this.decodeName = Camel(prefix, "Decode");
			this.decodeToBytesName = Camel(prefix, "DecodeToBytes");
			this.rankName = Camel(prefix, "Rank");
			this.decodeCoreName = Camel(prefix, "DecodeCore");
			this.acceptStepName = Camel(prefix, "AcceptStep");
			this.isAcceptingName = Camel(prefix, "IsAccepting");
			this.encodeStepName = Camel(prefix, "EncodeStep");
			this.isCanonicalAcceptingName = Camel(prefix, "IsCanonicalAccepting");
			this.decodeStepName = Camel(prefix, "DecodeStep");
			this.canonicalStepName = Camel(prefix, "CanonicalStep");
			this.finishCanonicalName = Camel(prefix, "FinishCanonical");
		}

		CodeWriter Writer => this.context.Writer;

		ImmutableArray<StateModel> AcceptedStates => this.context.AcceptedStates;

		ImmutableArray<StateModel> CanonicalStates => this.context.CanonicalStates;

		ImmutableArray<TxStateModel> TransducerStates => this.context.TransducerStates;

		int MaxLength => this.context.MaxLength;

		bool Canonicalises => !this.TransducerStates.IsDefault;

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
			this.Writer.Line("/** The count of values this naxp encodes, which is the largest value it can produce. */");
			this.Writer.Line($"const {this.valueCountName} = {this.Value(this.context.Compilation.ValueCount)};");
			this.Writer.Line();
			this.Writer.Line("/** The length of the longest string this naxp can decode a value to. */");
			this.Writer.Line($"const {this.maxLengthName} = {this.MaxLength.ToString(CultureInfo.InvariantCulture)};");
			this.Writer.Line();
		}

		#region Public functions
		void EmitAccepts(bool bytes)
		{
			this.Writer.Line(bytes
				? "/** Whether this naxp accepts the ASCII text in a Uint8Array. A byte outside ASCII is never accepted."
				: "/** Whether this naxp accepts a string.");
			this.Writer.Line(bytes
				? " * @param {Uint8Array} bytes"
				: " * @param {string} text");
			this.Writer.Line(" * @returns {boolean}");
			this.Writer.Line(" */");
			this.Writer.Line($"function {(bytes ? this.acceptsBytesName : this.acceptsName)}({(bytes ? "bytes" : "text")}) {{");
			this.Writer.Indent();
			this.Writer.Line("let state = 0;");
			this.Writer.Line();
			this.EmitReadLoop(bytes, code => $"state = {this.acceptStepName}(state, {code});", "return false;");
			this.Writer.Line();
			this.Writer.Line($"return {this.isAcceptingName}(state);");
			this.Writer.Outdent();
			this.Writer.Line("}");
			this.Writer.Line();
		}

		void EmitEncode(bool bytes)
		{
			this.Writer.Line(bytes
				? "/** The value of the ASCII text in a Uint8Array, from 1 to the value count, or zero where this naxp does not accept it."
				: "/** The value of a string, from 1 to the value count, or zero where this naxp does not accept it.");
			this.Writer.Line(bytes
				? " * @param {Uint8Array} bytes"
				: " * @param {string} text");
			this.Writer.Line($" * @returns {{{this.NumberType}}}");
			this.Writer.Line(" */");
			this.Writer.Line($"function {(bytes ? this.encodeBytesName : this.encodeName)}({(bytes ? "bytes" : "text")}) {{");
			this.Writer.Indent();

			if (this.Canonicalises)
			{
				this.Writer.Line("const canonical = [];");
				this.Writer.Line("let state = 0;");
				this.Writer.Line();
				this.EmitReadLoop(bytes, code => $"state = {this.canonicalStepName}(state, {code}, canonical);", $"return {this.zero};");
				this.Writer.Line();
				this.Writer.Line($"return {this.finishCanonicalName}(state, canonical) ? {this.rankName}(canonical) : {this.zero};");
			}
			else
			{
				this.Writer.Line($"const acc = {{ total: {this.zero} }};");
				this.Writer.Line("let state = 0;");
				this.Writer.Line();
				this.EmitReadLoop(bytes, code => $"state = {this.encodeStepName}(state, {code}, acc);", $"return {this.zero};");
				this.Writer.Line();
				this.Writer.Line($"return {this.isAcceptingName}(state) ? acc.total + {this.one} : {this.zero};");
			}

			this.Writer.Outdent();
			this.Writer.Line("}");
			this.Writer.Line();
		}

		/// <summary>
		/// The loop both entry points read their input with. A byte is already the code point, and
		/// anything above ASCII fits no transition, so one stepper serves both forms.
		/// </summary>
		void EmitReadLoop(bool bytes, Func<string, string> step, string onRefusal)
		{
			string source = bytes ? "bytes" : "text";
			string code = bytes ? "bytes[i]" : "text.charCodeAt(i)";

			this.Writer.Line($"for (let i = 0; i < {source}.length; i++) {{");
			this.Writer.Indent();
			this.Writer.Line(step(code));
			this.Writer.Line();
			this.Writer.Line($"if (state < 0) {{ {onRefusal} }}");
			this.Writer.Outdent();
			this.Writer.Line("}");
		}

		void EmitDecodePublics()
		{
			string countDigits = this.context.Compilation.ValueCount.ToString(CultureInfo.InvariantCulture);
			string message = $"'This naxp encodes the values 1 to {countDigits}.'";

			this.Writer.Line("/** The string a value stands for, which is in canonical form.");
			this.Writer.Line($" * @param {{{this.NumberType}}} value");
			this.Writer.Line(" * @returns {string}");
			this.Writer.Line(" * @throws {RangeError} The value is not one this naxp produces.");
			this.Writer.Line(" */");
			this.Writer.Line($"function {this.decodeName}(value) {{");
			this.Writer.Indent();
			this.EmitRangeCheck(message);
			this.Writer.Line($"return String.fromCharCode.apply(null, {this.decodeCoreName}(value));");
			this.Writer.Outdent();
			this.Writer.Line("}");
			this.Writer.Line();

			this.Writer.Line("/** The string a value stands for, as ASCII bytes.");
			this.Writer.Line($" * @param {{{this.NumberType}}} value");
			this.Writer.Line(" * @returns {Uint8Array}");
			this.Writer.Line(" * @throws {RangeError} The value is not one this naxp produces.");
			this.Writer.Line(" */");
			this.Writer.Line($"function {this.decodeToBytesName}(value) {{");
			this.Writer.Indent();
			this.EmitRangeCheck(message);
			this.Writer.Line($"return Uint8Array.from({this.decodeCoreName}(value));");
			this.Writer.Outdent();
			this.Writer.Line("}");
			this.Writer.Line();
		}

		void EmitRangeCheck(string message)
		{
			this.Writer.Line($"if (value < {this.one} || value > {this.valueCountName}) {{");
			this.Writer.Indent();
			this.Writer.Line($"throw new RangeError({message});");
			this.Writer.Outdent();
			this.Writer.Line("}");
			this.Writer.Line();
		}
		#endregion
		#region Private functions
		void EmitSteppers()
		{
			if (this.Canonicalises)
			{
				this.Writer.Line("/** The rank of a canonical string, as code points, within the canonical language, or zero where it is not in it. */");
				this.Writer.Line($"function {this.rankName}(codes) {{");
				this.Writer.Indent();
				this.Writer.Line($"const acc = {{ total: {this.zero} }};");
				this.Writer.Line("let state = 0;");
				this.Writer.Line();
				this.Writer.Line("for (let i = 0; i < codes.length; i++) {");
				this.Writer.Indent();
				this.Writer.Line($"state = {this.encodeStepName}(state, codes[i], acc);");
				this.Writer.Line();
				this.Writer.Line($"if (state < 0) {{ return {this.zero}; }}");
				this.Writer.Outdent();
				this.Writer.Line("}");
				this.Writer.Line();
				this.Writer.Line($"return {this.isCanonicalAcceptingName}(state) ? acc.total + {this.one} : {this.zero};");
				this.Writer.Outdent();
				this.Writer.Line("}");
				this.Writer.Line();
			}

			this.Writer.Line("/** The code points of a value that was already checked against the value count. */");
			this.Writer.Line($"function {this.decodeCoreName}(value) {{");
			this.Writer.Indent();
			this.Writer.Line("const codes = [];");
			this.Writer.Line("const box = { remaining: value };");
			this.Writer.Line("let state = 0;");
			this.Writer.Line();
			this.Writer.Line("while (state >= 0) {");
			this.Writer.Indent();
			this.Writer.Line($"state = {this.decodeStepName}(state, box, codes);");
			this.Writer.Outdent();
			this.Writer.Line("}");
			this.Writer.Line();
			this.Writer.Line("return codes;");
			this.Writer.Outdent();
			this.Writer.Line("}");
			this.Writer.Line();

			this.Writer.Line("/** The acceptor's transition: the next state, or -1 where the code point fits nothing. */");
			this.emitter.EmitStepFunctions(
				this.Writer,
				this.acceptStepName,
				"state, c",
				"state, c",
				this.AcceptedStates.Length,
				this.EmitAcceptCase);
			this.Writer.Line();

			this.EmitAcceptingPredicate(this.isAcceptingName, this.AcceptedStates);
			this.Writer.Line();

			this.Writer.Line("/** The canonical machine's transition, accumulating the values skipped: the next state, or -1. */");
			this.emitter.EmitStepFunctions(
				this.Writer,
				this.encodeStepName,
				"state, c, acc",
				"state, c, acc",
				this.CanonicalStates.Length,
				this.EmitEncodeCase);
			this.Writer.Line();

			if (this.Canonicalises)
			{
				this.EmitAcceptingPredicate(this.isCanonicalAcceptingName, this.CanonicalStates);
				this.Writer.Line();
			}

			this.Writer.Line("/** One step of decoding: appends at most one code point and returns the next state, or -1 when the string is complete. */");
			this.emitter.EmitStepFunctions(
				this.Writer,
				this.decodeStepName,
				"state, box, codes",
				"state, box, codes",
				this.CanonicalStates.Length,
				this.EmitDecodeCase,
				"let index;",
				id => NeedsIndex(this.CanonicalStates[id]));

			if (this.Canonicalises)
			{
				this.Writer.Line();
				this.Writer.Line("/** The canonicalising transition, appending what reading the code point emits: the next state, or -1. */");
				this.emitter.EmitStepFunctions(
					this.Writer,
					this.canonicalStepName,
					"state, c, canonical",
					"state, c, canonical",
					this.TransducerStates.Length,
					this.EmitCanonicalCase);
				this.Writer.Line();

				this.Writer.Line("/** Appends what ending the input emits, and returns whether the input may end here. */");
				this.emitter.EmitStepFunctions(
					this.Writer,
					this.finishCanonicalName,
					"state, canonical",
					"state, canonical",
					this.TransducerStates.Length,
					this.EmitFinishCase,
					defaultResult: "false");
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
					// transition, and this transition's earlier runs. Within the run the code point's
					// rank folds into (c - first).
					ulong skipped = arc.SkippedBefore + (arc.NextCount * offset);
					string? added = this.AddedExpression(skipped, arc.NextCount, first, last);
					string next = arc.Next.ToString(CultureInfo.InvariantCulture);

					this.Writer.Line(added is null
						? $"if ({this.emitter.RunCondition(first, last)}) {{ return {next}; }}"
						: $"if ({this.emitter.RunCondition(first, last)}) {{ acc.total += {added}; return {next}; }}");

					offset += (ulong)(last - first + 1);
				}
			}

			this.Writer.Line("return -1;");
			this.Writer.Outdent();
		}

		void EmitDecodeCase(int id)
		{
			StateModel state = this.CanonicalStates[id];

			this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}: {{");
			this.Writer.Indent();

			if (state.Arcs.Length == 0)
			{
				// The terminal state. The remaining value is one here, because the caller checked the
				// value against the count of the start state and every step keeps it within the count
				// of the state it moves to.
				this.Writer.Line("return -1;");
				this.Writer.Outdent();
				this.Writer.Line("}");
				return;
			}

			if (state.AcceptsEnd)
			{
				this.Writer.Line($"if (box.remaining === {this.one}) {{ return -1; }}");
				this.Writer.Line();
				this.Writer.Line($"box.remaining -= {this.one};");
			}

			for (int i = 0; i < state.Arcs.Length; ++i)
			{
				ArcModel arc = state.Arcs[i];
				ulong block = arc.NextCount * (ulong)arc.Set.Count;

				if (i < state.Arcs.Length - 1)
				{
					if (i > 0 || state.AcceptsEnd) { this.Writer.Line(); }

					this.Writer.Line($"if (box.remaining <= {this.Value(block)}) {{");
					this.Writer.Indent();
					this.EmitDecodeArc(arc);
					this.Writer.Outdent();
					this.Writer.Line("}");
					this.Writer.Line();
					this.Writer.Line($"box.remaining -= {this.Value(block)};");
				}
				else
				{
					// The last transition takes whatever is left, by the same invariant as the
					// terminal state above.
					if (i > 0 || state.AcceptsEnd) { this.Writer.Line(); }

					this.EmitDecodeArc(arc);
				}
			}

			this.Writer.Outdent();
			this.Writer.Line("}");
		}

		void EmitDecodeArc(ArcModel arc)
		{
			string next = arc.Next.ToString(CultureInfo.InvariantCulture);
			List<(char First, char Last)> runs = GetRuns(arc.Set);

			if (arc.Set.Count == 1)
			{
				// One code point leaves the remaining value untouched: its rank is zero and the whole
				// block belongs to the next state.
				this.Writer.Line($"codes.push({CodeLiteral(runs[0].First)});");
				this.Writer.Line($"return {next};");
				return;
			}

			// index is declared once at the top of the function, because these arcs sit at differing
			// brace depths within one switch and sibling declarations there collide.
			this.Writer.Line(arc.NextCount == 1UL
				? $"index = box.remaining - {this.one};"
				: this.big
					? $"index = (box.remaining - 1n) / {this.Value(arc.NextCount)};"
					: $"index = Math.floor((box.remaining - 1) / {this.Value(arc.NextCount)});");
			this.Writer.Line($"codes.push({this.CharacterExpression(runs)});");
			this.Writer.Line(arc.NextCount == 1UL
				? $"box.remaining = {this.one};"
				: $"box.remaining = ((box.remaining - {this.one}) % {this.Value(arc.NextCount)}) + {this.one};");
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
					this.Writer.Line($"if ({condition}) {{ canonical.push({OutputExpression(arc.Output[0])}); return {next}; }}");
				}
				else
				{
					this.Writer.Line($"if ({condition}) {{");
					this.Writer.Indent();

					foreach (char output in arc.Output)
					{
						this.Writer.Line($"canonical.push({OutputExpression(output)});");
					}

					this.Writer.Line($"return {next};");
					this.Writer.Outdent();
					this.Writer.Line("}");
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
				this.Writer.Line($"canonical.push({CodeLiteral(output)});");
			}

			this.Writer.Line("return true;");
			this.Writer.Outdent();
		}

		void EmitAcceptingPredicate(string name, ImmutableArray<StateModel> states)
		{
			this.Writer.Line("/** Whether the input may end in this state. */");
			this.Writer.Line($"function {name}(state) {{");
			this.Writer.Indent();
			this.Writer.Line("switch (state) {");
			this.Writer.Indent();

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
			this.Writer.Outdent();
			this.Writer.Line("}");
			this.Writer.Outdent();
			this.Writer.Line("}");
		}
		#endregion
		#region Expression helpers
		/// <summary>How a JSDoc comment names the value type.</summary>
		string NumberType => this.big ? "bigint" : "number";

		/// <summary>A value literal, BigInt or number as the naxp's size decided.</summary>
		string Value(ulong value) => this.emitter.Grouped(value) + (this.big ? "n" : "");

		/// <summary>
		/// What passing this run adds to the total, or <see langword="null"/> where it adds nothing.
		/// </summary>
		string? AddedExpression(ulong skipped, ulong count, char first, char last)
		{
			if (first == last) { return skipped == 0UL ? null : this.Value(skipped); }

			// The code point's rank is a number either way, so BigInt arithmetic converts it.
			string index = this.big ? $"BigInt(c - {CodeLiteral(first)})" : $"(c - {CodeLiteral(first)})";
			string term = count == 1UL ? index : $"{this.Value(count)} * {index}";

			return skipped == 0UL ? term : $"{this.Value(skipped)} + {term}";
		}

		/// <summary>
		/// The code point at position <c>index</c> within a set, as an expression over its runs.
		/// </summary>
		string CharacterExpression(List<(char First, char Last)> runs)
		{
			if (runs.Count == 1) { return this.RunCharacter(runs[0], 0UL); }

			var builder = new StringBuilder();
			ulong cumulative = 0UL;

			for (int i = 0; i < runs.Count - 1; ++i)
			{
				ulong offset = cumulative;
				cumulative += (ulong)(runs[i].Last - runs[i].First + 1);
				builder.Append($"index < {this.Value(cumulative)} ? {this.RunCharacter(runs[i], offset)} : ");
			}

			builder.Append(this.RunCharacter(runs[runs.Count - 1], cumulative));

			return builder.ToString();
		}

		string RunCharacter((char First, char Last) run, ulong offset)
		{
			if (run.First == run.Last) { return CodeLiteral(run.First); }

			// A code point is a number, so the BigInt index converts back on the way out.
			string index = this.big
				? offset == 0UL ? "Number(index)" : $"Number(index - {this.Value(offset)})"
				: offset == 0UL ? "index" : $"(index - {this.Value(offset)})";

			return $"{CodeLiteral(run.First)} + {index}";
		}
		#endregion
	}

	#region Text helpers
	/// <summary>A transducer output code point: the copy marker stands for the one just read.</summary>
	static string OutputExpression(char output) => output == Tx.CopyMarker ? "c" : CodeLiteral(output);

	/// <summary>
	/// An ASCII code point, in hexadecimal. Bare, with no comment naming the character: the
	/// comparisons sit two or three to a line, and the annotations cost more in noise than they
	/// return in clarity when <c>charCodeAt</c> is in plain view above.
	/// </summary>
	static string CodeLiteral(char c) => "0x" + ((int)c).ToString("X2", CultureInfo.InvariantCulture);

	/// <summary>The prefix and a member name, camel cased as JavaScript writes function names.</summary>
	static string Camel(string prefix, string member)
	{
		string name = prefix + member;

		return char.IsUpper(name[0]) ? char.ToLowerInvariant(name[0]) + name.Substring(1) : name;
	}
	#endregion
}
