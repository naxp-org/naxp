// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace LogMu;

/// <summary>
/// What the C and C++ emitters share: the steppers, the prototypes C and C++ both need ahead of
/// a function's first use, and the snake_case names.
/// </summary>
/// <remarks>
/// <para>
/// The two fragments differ at their public surface, where C takes a pointer and a length and C++
/// a <c>string_view</c>, and in spelling: a pointer against a reference, a C cast against
/// <c>static_cast</c>, a block comment against a line comment, <c>static</c> against
/// <c>inline</c>. The steppers, which are most of a fragment, are otherwise the same text, so
/// they are written once here over those spellings and each language supplies its own.
/// </para>
/// <para>
/// Both compilers warn of a parameter a function never reads, and with <c>-Wextra -Werror</c>
/// the warning is fatal, so every stepper starts by voiding whatever its range of states leaves
/// unused: a literal run skips nothing, so its encode step never touches the total, and a
/// function holding only such states would otherwise not compile clean. The predicates deciding
/// that sit beside the case emitters they mirror.
/// </para>
/// </remarks>
abstract class CFamilyEmitter : Emitter
{
	protected override void Emit(Context context)
	{
		var fragment = new Fragment(this, context);

		this.EmitHeader(fragment);
		fragment.EmitPrototypes();
		this.EmitPublics(fragment);
		this.EmitCanonicalise(fragment);
		fragment.EmitSteppers();
	}

	#region The shape of the family
	/// <inheritdoc/>
	protected override void OpenFunction(CodeWriter writer, string name, string parameters)
	{
		writer.Line($"{this.StepLinkage} int {name}({parameters})");
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
		writer.CloseBlock();
		writer.Line();
		writer.Line($"return {result};");
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
	#region What each language spells
	/// <summary>Writes the fragment's opening comment and its two constants.</summary>
	protected abstract void EmitHeader(Fragment fragment);

	/// <summary>Writes the public functions, which are the whole difference between the two languages.</summary>
	protected abstract void EmitPublics(Fragment fragment);

	/// <summary>
	/// Writes the function that puts the canonical form of text into a buffer of the longest
	/// length, which every public function needing a canonical form calls. It reads its text as
	/// the public functions do, which is the language's own business.
	/// </summary>
	protected abstract void EmitCanonicalise(Fragment fragment);

	/// <summary>How text is taken in: a pointer and a length in C, a <c>string_view</c> in C++.</summary>
	protected abstract string TextParameters { get; }

	/// <summary>The linkage a stepper is declared with: <c>static</c> in C, <c>inline</c> in C++.</summary>
	protected abstract string StepLinkage { get; }

	/// <summary>What the fixed-width integer types are qualified with: nothing in C, <c>std::</c> in C++.</summary>
	protected abstract string TypePrefix { get; }

	/// <summary>A pointer parameter, which the two languages space differently.</summary>
	protected abstract string Pointer(string type, string name);

	/// <summary>A parameter the stepper writes through: a pointer in C, a reference in C++.</summary>
	protected abstract string ByReference(string type, string name);

	/// <summary>Reading or writing a <see cref="ByReference"/> parameter.</summary>
	protected abstract string Dereference(string name);

	/// <summary>Post-incrementing a <see cref="ByReference"/> parameter, as an array index.</summary>
	protected abstract string Increment(string name);

	/// <summary>Passing a local on to a <see cref="ByReference"/> parameter.</summary>
	protected abstract string AddressOf(string name);

	/// <summary>An explicit conversion of an expression, which the language brackets as it needs.</summary>
	protected abstract string Cast(string type, string expression);

	/// <summary>A one-line comment.</summary>
	protected abstract void Comment(CodeWriter writer, string text);

	/// <summary>The 64-bit unsigned type the steppers work in.</summary>
	protected string UInt64 => this.TypePrefix + "uint64_t";
	#endregion
	#region Text helpers
	/// <summary>
	/// The prefix and a member name, in snake_case. An underscore goes in wherever the case
	/// turns upward and the previous character was a letter or digit, and before an upper case
	/// letter that starts a new word after a run of them, so <c>UKPostcode</c> gives
	/// <c>uk_postcode</c>; a prefix already in snake_case comes through as it is.
	/// </summary>
	protected static string Snake(string prefix, string member)
	{
		string name = prefix + member;
		var builder = new StringBuilder(name.Length + 4);

		for (int i = 0; i < name.Length; ++i)
		{
			char c = name[i];

			if (i > 0 && IsUpper(c))
			{
				char previous = name[i - 1];
				bool boundary = IsLower(previous)
					|| IsDigit(previous)
					|| (IsUpper(previous) && i + 1 < name.Length && IsLower(name[i + 1]));

				if (boundary) { builder.Append('_'); }
			}

			builder.Append(IsUpper(c) ? (char)(c + ('a' - 'A')) : c);
		}

		return builder.ToString();
	}

	static bool IsUpper(char c) => c >= 'A' && c <= 'Z';

	static bool IsLower(char c) => c >= 'a' && c <= 'z';

	static bool IsDigit(char c) => c >= '0' && c <= '9';

	/// <summary>
	/// An ASCII character as a literal, with the two that need it escaped and the unprintable
	/// ones in hexadecimal. A hexadecimal escape runs to the closing quote, so it is safe here
	/// where it would not be inside a string.
	/// </summary>
	protected static string CharLiteral(char c)
	{
		switch (c)
		{
			case '\'': return @"'\''";
			case '\\': return @"'\\'";
			default:
				return c >= ' ' && c <= '~'
					? $"'{c}'"
					: $@"'\x{((int)c).ToString("X2", CultureInfo.InvariantCulture)}'"
					;
		}
	}

	/// <summary>
	/// A string as a C literal. A pattern may hold whitespace other than the space, which is
	/// escaped along with the quote and the backslash, and a question mark after another is escaped
	/// too, so that no pair of them starts a trigraph where a compiler still reads trigraphs.
	/// </summary>
	protected static string StringLiteral(string text)
	{
		var literal = new StringBuilder("\"");

		for (int i = 0; i < text.Length; ++i)
		{
			char c = text[i];

			if (c == '"' || c == '\\')
			{
				literal.Append('\\').Append(c);
			}
			else if (c == '?' && i > 0 && text[i - 1] == '?')
			{
				literal.Append("\\?");
			}
			else if (c == '\t')
			{
				literal.Append("\\t");
			}
			else if (c == '\n')
			{
				literal.Append("\\n");
			}
			else if (c == '\r')
			{
				literal.Append("\\r");
			}
			else if (c < ' ' || c > '~')
			{
				// Three octal digits, because a hexadecimal escape runs on into any hexadecimal
				// digit that follows it.
				literal.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
			}
			else
			{
				literal.Append(c);
			}
		}

		return literal.Append('"').ToString();
	}

	/// <summary>An unsigned 64-bit literal, grouped where the language has a separator.</summary>
	protected string Literal(ulong value) => this.Grouped(value) + "ULL";

	/// <summary>Whether an identifier needs no brackets under a C cast.</summary>
	protected static bool IsIdentifier(string expression)
	{
		foreach (char c in expression)
		{
			if (!IsUpper(c) && !IsLower(c) && !IsDigit(c) && c != '_') { return false; }
		}

		return true;
	}
	#endregion
	/// <summary>
	/// One emission call's state: the context, the generated names, the value type's spelling,
	/// and the steppers, which both languages write through this. Per call so the shared
	/// instance stays stateless.
	/// </summary>
	protected sealed class Fragment
	{
		readonly CFamilyEmitter emitter;
		readonly Context context;

		/// <summary>The name the generated code keeps the characters it has read under.</summary>
		public const string HeldName = "held";

		public Fragment(CFamilyEmitter emitter, Context context)
		{
			this.emitter = emitter;
			this.context = context;

			string bare;
			string suffix;

			switch (context.ValueType)
			{
				case NaxpValueType.Int8: bare = "int8_t"; suffix = ""; break;
				case NaxpValueType.UInt8: bare = "uint8_t"; suffix = ""; break;
				case NaxpValueType.Int16: bare = "int16_t"; suffix = ""; break;
				case NaxpValueType.UInt16: bare = "uint16_t"; suffix = ""; break;
				case NaxpValueType.Int32: bare = "int32_t"; suffix = ""; break;
				case NaxpValueType.UInt32: bare = "uint32_t"; suffix = "U"; break;
				case NaxpValueType.Int64: bare = "int64_t"; suffix = "LL"; break;
				case NaxpValueType.UInt64: bare = "uint64_t"; suffix = "ULL"; break;
				default: throw new InvalidOperationException($"Unhandled value type {context.ValueType}.");
			}

			this.ValueKeyword = emitter.TypePrefix + bare;
			this.ValueIsWidest = context.ValueType == NaxpValueType.UInt64;
			this.MaxEncodedValueLiteral = emitter.Grouped(context.Compilation.MaxEncodedValue) + suffix;
			this.ValueZero = this.ValueIsWidest ? "0ULL" : "0";
			this.ValueOne = this.ValueIsWidest ? "1ULL" : "1";

			// DecodeCore takes the widest type. Only that reaches it unconverted; every other type
			// needs the cast, which the range check just made safe.
			this.DecodeCoreArgument = this.ValueIsWidest ? "value" : emitter.Cast(emitter.UInt64, "value");

			string prefix = context.Prefix;
			this.PatternName = Snake(prefix, "Pattern");
			this.MaxEncodedValueName = Snake(prefix, "MaxEncodedValue");
			this.MaxLengthName = Snake(prefix, "MaxLength");
			this.AcceptsName = Snake(prefix, "Accepts");
			this.EncodeName = Snake(prefix, "Encode");
			this.DecodeName = Snake(prefix, "Decode");
			this.CanonicalFormName = Snake(prefix, "CanonicalForm");
			this.CanonicaliseName = Snake(prefix, "Canonicalise");
			this.RankName = Snake(prefix, "Rank");
			this.DecodeCoreName = Snake(prefix, "DecodeCore");
			this.AcceptStepName = Snake(prefix, "AcceptStep");
			this.IsAcceptingName = Snake(prefix, "IsAccepting");
			this.EncodeStepName = Snake(prefix, "EncodeStep");
			this.IsCanonicalAcceptingName = Snake(prefix, "IsCanonicalAccepting");
			this.DecodeStepName = Snake(prefix, "DecodeStep");
			this.CanonicalStepName = Snake(prefix, "CanonicalStep");
			this.FinishCanonicalName = Snake(prefix, "FinishCanonical");
		}

		public Context Context => this.context;

		public CodeWriter Writer => this.context.Writer;

		public ImmutableArray<StateModel> AcceptedStates => this.context.AcceptedStates;

		public ImmutableArray<StateModel> CanonicalStates => this.context.CanonicalStates;

		public ImmutableArray<TxStateModel> TransducerStates => this.context.TransducerStates;

		public int MaxLength => this.context.MaxLength;

		public int RegisterDepth => this.context.RegisterDepth;

		public bool NeedsRegister => this.context.NeedsRegister;

		public bool Canonicalises => !this.TransducerStates.IsDefault;

		/// <summary>The prefix in the language's own casing, for the public names each language adds.</summary>
		public string Name(string member) => Snake(this.context.Prefix, member);

		// The generated names, each the prefix plus the bare member name in snake_case.
		public string PatternName { get; }
		public string MaxEncodedValueName { get; }
		public string MaxLengthName { get; }
		public string AcceptsName { get; }
		public string EncodeName { get; }
		public string DecodeName { get; }
		public string CanonicalFormName { get; }
		public string CanonicaliseName { get; }
		public string RankName { get; }
		public string DecodeCoreName { get; }
		public string AcceptStepName { get; }
		public string IsAcceptingName { get; }
		public string EncodeStepName { get; }
		public string IsCanonicalAcceptingName { get; }
		public string DecodeStepName { get; }
		public string CanonicalStepName { get; }
		public string FinishCanonicalName { get; }

		// The spelling of the chosen value type. The steppers work in the 64-bit unsigned type
		// throughout whatever the choice; only the public boundary changes.
		public string ValueKeyword { get; }
		public string MaxEncodedValueLiteral { get; }
		public string ValueZero { get; }
		public string ValueOne { get; }
		public bool ValueIsWidest { get; }
		public string DecodeCoreArgument { get; }

		/// <summary>
		/// What sizes a character buffer. The longest string's length, except that a zero-length
		/// array is illegal in both languages, so a naxp whose only string is empty gets one byte
		/// that nothing writes.
		/// </summary>
		public string BufferSize => this.MaxLength == 0 ? "1" : this.MaxLengthName;

		/// <summary>The largest encoded value in plain digits, for a message.</summary>
		public string MaxEncodedValueDigits => this.context.Compilation.MaxEncodedValue.ToString(CultureInfo.InvariantCulture);

		#region The steppers' signatures
		// Each parameter list is written once here and read by the prototype and the definition
		// alike, so the two cannot drift.
		string CanonicaliseParameters => $"{this.emitter.TextParameters}, {this.emitter.Pointer("char", "canonical")}";

		string RankParameters => $"{this.emitter.Pointer("const char", "canonical")}, int length";

		string DecodeCoreParameters => $"{this.emitter.UInt64} value, {this.emitter.Pointer("char", "destination")}";

		string AcceptStepParameters => "int state, int c";

		string EncodeStepParameters => $"int state, int c, {this.emitter.ByReference(this.emitter.UInt64, "total")}";

		string DecodeStepParameters
			=> $"int state, {this.emitter.ByReference(this.emitter.UInt64, "remaining")}, {this.emitter.Pointer("char", "destination")}, {this.emitter.ByReference("int", "length")}";

		string HeldParameter => this.NeedsRegister ? $"{this.emitter.Pointer("const char", HeldName)}, " : string.Empty;

		string CanonicalStepParameters
			=> $"int state, int c, {this.HeldParameter}{this.emitter.Pointer("char", "canonical")}, {this.emitter.ByReference("int", "length")}";

		string FinishCanonicalParameters
			=> $"int state, {this.HeldParameter}{this.emitter.Pointer("char", "canonical")}, int length";

		/// <summary>The arguments the canonicalising step takes after its character, as a public function passes them.</summary>
		public string StepArguments(string lengthArgument)
			=> this.NeedsRegister ? $"{HeldName}, canonical, {lengthArgument}" : $"canonical, {lengthArgument}";

		/// <summary>The arguments the finishing step takes after its state.</summary>
		public string FinishArguments()
			=> this.NeedsRegister ? $"{HeldName}, canonical, length" : "canonical, length";
		#endregion
		#region Prototypes
		/// <summary>
		/// Declares every stepper ahead of the public functions that call it. Both languages need
		/// a function declared before its first use, and the public functions come first because
		/// they are what a reader is looking for.
		/// </summary>
		public void EmitPrototypes()
		{
			string linkage = this.emitter.StepLinkage;

			this.emitter.Comment(this.Writer, "The steppers, which are defined below the public functions.");
			this.Writer.Line($"{linkage} int {this.CanonicaliseName}({this.CanonicaliseParameters});");

			if (this.Canonicalises)
			{
				this.Writer.Line($"{linkage} {this.emitter.UInt64} {this.RankName}({this.RankParameters});");
			}

			this.Writer.Line($"{linkage} int {this.DecodeCoreName}({this.DecodeCoreParameters});");
			this.EmitStepPrototypes(this.AcceptStepName, this.AcceptStepParameters, this.AcceptedStates.Length);
			this.Writer.Line($"{linkage} bool {this.IsAcceptingName}(int state);");
			this.EmitStepPrototypes(this.EncodeStepName, this.EncodeStepParameters, this.CanonicalStates.Length);

			if (this.Canonicalises)
			{
				this.Writer.Line($"{linkage} bool {this.IsCanonicalAcceptingName}(int state);");
			}

			this.EmitStepPrototypes(this.DecodeStepName, this.DecodeStepParameters, this.CanonicalStates.Length);

			if (this.Canonicalises)
			{
				this.EmitStepPrototypes(this.CanonicalStepName, this.CanonicalStepParameters, this.TransducerStates.Length);
				this.EmitStepPrototypes(this.FinishCanonicalName, this.FinishCanonicalParameters, this.TransducerStates.Length);
			}

			this.Writer.Line();
		}

		/// <summary>A stepper's prototype, and its chunks' where the machine is split.</summary>
		void EmitStepPrototypes(string name, string parameters, int stateCount)
		{
			this.Writer.Line($"{this.emitter.StepLinkage} int {name}({parameters});");

			int chunkCount = ChunkCount(stateCount);

			if (chunkCount == 1) { return; }

			for (int chunk = 0; chunk < chunkCount; ++chunk)
			{
				this.Writer.Line($"{this.emitter.StepLinkage} int {name}{chunk.ToString(CultureInfo.InvariantCulture)}({parameters});");
			}
		}
		#endregion
		#region The steppers
		/// <summary>Opens the function that canonicalises, with its comment, which both languages share.</summary>
		public void OpenCanonicalise()
		{
			this.emitter.Comment(this.Writer, $"Writes the canonical form of text into a buffer of {this.MaxLengthName} characters, and returns its length, or -1 where the text is invalid.");
			this.Writer.Line($"{this.emitter.StepLinkage} int {this.CanonicaliseName}({this.CanonicaliseParameters})");
			this.Writer.OpenBlock();
		}

		/// <summary>Declares the register, where the naxp needs one, ahead of the loop that reads the text.</summary>
		/// <param name="zeroed">How an array is written zeroed: <c>{ 0 }</c> in C and <c>{}</c> in C++.</param>
		public void DeclareRegister(string zeroed)
		{
			if (!this.NeedsRegister) { return; }

			// The characters read, oldest first, so a reference of depth d is the one at
			// RegisterDepth - 1 - d. Shifting a buffer this small beats indexing a ring, and it
			// starts zeroed so that an early shift reads nothing undefined.
			this.Writer.Line($"char {HeldName}[{this.RegisterDepth.ToString(CultureInfo.InvariantCulture)}] = {zeroed};");
		}

		/// <summary>Keeps the character just read, where the naxp needs a register.</summary>
		/// <param name="character">The character, as a <c>char</c>.</param>
		public void KeepCharacter(string character)
		{
			if (!this.NeedsRegister) { return; }

			string top = (this.RegisterDepth - 1).ToString(CultureInfo.InvariantCulture);

			if (this.RegisterDepth > 1)
			{
				this.Writer.Line($"for (int h = 0; h < {top}; ++h) {{ {HeldName}[h] = {HeldName}[h + 1]; }}");
			}

			this.Writer.Line($"{HeldName}[{top}] = {character};");
		}

		public void EmitSteppers()
		{
			string linkage = this.emitter.StepLinkage;
			string uint64 = this.emitter.UInt64;

			if (this.Canonicalises)
			{
				this.emitter.Comment(this.Writer, "The rank of a canonical string within the canonical language, or zero where it is not in it.");
				this.Writer.Line($"{linkage} {uint64} {this.RankName}({this.RankParameters})");
				this.Writer.OpenBlock();
				this.Writer.Line("int state = 0;");
				this.Writer.Line($"{uint64} total = 0ULL;");
				this.Writer.Line();
				this.Writer.Line("for (int i = 0; i < length; ++i)");
				this.Writer.OpenBlock();
				this.Writer.Line($"state = {this.EncodeStepName}(state, canonical[i], {this.emitter.AddressOf("total")});");
				this.Writer.Line();
				this.Writer.Line("if (state < 0) { return 0ULL; }");
				this.Writer.CloseBlock();
				this.Writer.Line();
				this.Writer.Line($"return {this.IsCanonicalAcceptingName}(state) ? total + 1ULL : 0ULL;");
				this.Writer.CloseBlock();
				this.Writer.Line();
			}

			this.emitter.Comment(this.Writer, $"Writes the string of a value that was already checked against {this.MaxEncodedValueName}, and returns its length.");
			this.Writer.Line($"{linkage} int {this.DecodeCoreName}({this.DecodeCoreParameters})");
			this.Writer.OpenBlock();
			this.Writer.Line($"{uint64} remaining = value;");
			this.Writer.Line("int state = 0;");
			this.Writer.Line("int length = 0;");
			this.Writer.Line();
			this.Writer.Line("while (state >= 0)");
			this.Writer.OpenBlock();
			this.Writer.Line($"state = {this.DecodeStepName}(state, {this.emitter.AddressOf("remaining")}, destination, {this.emitter.AddressOf("length")});");
			this.Writer.CloseBlock();
			this.Writer.Line();
			this.Writer.Line("return length;");
			this.Writer.CloseBlock();
			this.Writer.Line();

			this.emitter.Comment(this.Writer, "The acceptor's transition: the next state, or -1 where the character fits nothing.");
			this.emitter.EmitStepFunctions(this.Writer,
				this.AcceptStepName,
				this.AcceptStepParameters,
				"state, c",
				this.AcceptedStates.Length,
				this.EmitAcceptCase,
				prologue: this.AcceptPrologue);
			this.Writer.Line();

			this.EmitAcceptingPredicate(this.IsAcceptingName, this.AcceptedStates);
			this.Writer.Line();

			this.emitter.Comment(this.Writer, "The canonical machine's transition, accumulating the values skipped: the next state, or -1.");
			this.emitter.EmitStepFunctions(this.Writer,
				this.EncodeStepName,
				this.EncodeStepParameters,
				"state, c, total",
				this.CanonicalStates.Length,
				this.EmitEncodeCase,
				prologue: this.EncodePrologue);
			this.Writer.Line();

			if (this.Canonicalises)
			{
				this.EmitAcceptingPredicate(this.IsCanonicalAcceptingName, this.CanonicalStates);
				this.Writer.Line();
			}

			this.emitter.Comment(this.Writer, "One step of decoding: appends at most one character and returns the next state, or -1 when the string is complete.");
			this.emitter.EmitStepFunctions(this.Writer,
				this.DecodeStepName,
				this.DecodeStepParameters,
				"state, remaining, destination, length",
				this.CanonicalStates.Length,
				this.EmitDecodeCase,
				$"{uint64} index;",
				id => NeedsIndex(this.CanonicalStates[id]),
				prologue: this.DecodePrologue);

			if (this.Canonicalises)
			{
				this.Writer.Line();
				this.emitter.Comment(this.Writer, "The canonicalising transition, appending what reading the character emits: the next state, or -1.");
				this.emitter.EmitStepFunctions(this.Writer,
					this.CanonicalStepName,
					this.CanonicalStepParameters,
					$"state, c, {this.StepArguments("length")}",
					this.TransducerStates.Length,
					this.EmitCanonicalCase,
					prologue: this.CanonicalPrologue);
				this.Writer.Line();

				this.emitter.Comment(this.Writer, "Appends what ending the input emits and returns the final length, or -1 where the input may not end here.");
				this.emitter.EmitStepFunctions(this.Writer,
					this.FinishCanonicalName,
					this.FinishCanonicalParameters,
					$"state, {this.FinishArguments()}",
					this.TransducerStates.Length,
					this.EmitFinishCase,
					caseNeeded: this.FinishCaseNeeded,
					prologue: this.FinishPrologue);
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

			this.Writer.Line("break;");
			this.Writer.Outdent();
		}

		void EmitEncodeCase(int id)
		{
			StateModel state = this.CanonicalStates[id];
			string total = this.emitter.Dereference("total");

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
					string? added = this.AddedExpression(skipped, arc.NextCount, first, last);
					string next = arc.Next.ToString(CultureInfo.InvariantCulture);

					this.Writer.Line(added is null
						? $"if ({this.emitter.RunCondition(first, last)}) {{ return {next}; }}"
						: $"if ({this.emitter.RunCondition(first, last)}) {{ {total} += {added}; return {next}; }}");

					offset += (ulong)(last - first + 1);
				}
			}

			this.Writer.Line("break;");
			this.Writer.Outdent();
		}

		void EmitDecodeCase(int id)
		{
			StateModel state = this.CanonicalStates[id];
			string remaining = this.emitter.Dereference("remaining");

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
				this.Writer.Line($"if ({remaining} == 1ULL) {{ return -1; }}");
				this.Writer.Line();
				this.Writer.Line($"{remaining} -= 1ULL;");
			}

			for (int i = 0; i < state.Arcs.Length; ++i)
			{
				ArcModel arc = state.Arcs[i];
				ulong block = arc.NextCount * (ulong)arc.Set.Count;

				if (i < state.Arcs.Length - 1)
				{
					if (i > 0 || state.AcceptsEnd) { this.Writer.Line(); }

					this.Writer.Line($"if ({remaining} <= {this.emitter.Literal(block)})");
					this.Writer.OpenBlock();
					this.EmitDecodeArc(arc);
					this.Writer.CloseBlock();
					this.Writer.Line();
					this.Writer.Line($"{remaining} -= {this.emitter.Literal(block)};");
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
			string remaining = this.emitter.Dereference("remaining");
			string append = $"destination[{this.emitter.Increment("length")}]";
			List<(char First, char Last)> runs = GetRuns(arc.Set);

			if (arc.Set.Count == 1)
			{
				// One character leaves the remaining value untouched: its rank is zero and the whole
				// block belongs to the next state.
				this.Writer.Line($"{append} = {CharLiteral(runs[0].First)};");
				this.Writer.Line($"return {next};");
				return;
			}

			// index is declared once at the top of the function, because these arcs sit at differing
			// brace depths within one switch and sibling declarations there collide.
			this.Writer.Line(arc.NextCount == 1UL
				? $"index = {remaining} - 1ULL;"
				: $"index = ({remaining} - 1ULL) / {this.emitter.Literal(arc.NextCount)};");
			this.Writer.Line($"{append} = {this.CharacterExpression(runs)};");
			this.Writer.Line(arc.NextCount == 1UL
				? $"{remaining} = 1ULL;"
				: $"{remaining} = (({remaining} - 1ULL) % {this.emitter.Literal(arc.NextCount)}) + 1ULL;");
			this.Writer.Line($"return {next};");
		}

		void EmitCanonicalCase(int id)
		{
			TxStateModel state = this.TransducerStates[id];
			string append = $"canonical[{this.emitter.Increment("length")}]";

			this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}:");
			this.Writer.Indent();

			foreach (TxArcModel arc in state.Arcs)
			{
				string condition = this.emitter.SetCondition(arc.Set);
				string next = arc.Next.ToString(CultureInfo.InvariantCulture);

				List<string> outputs = this.OutputExpressions(arc.Output, forFinish: false);

				if (outputs.Count == 0)
				{
					this.Writer.Line($"if ({condition}) {{ return {next}; }}");
				}
				else if (outputs.Count == 1)
				{
					this.Writer.Line($"if ({condition}) {{ {append} = {outputs[0]}; return {next}; }}");
				}
				else
				{
					this.Writer.Line($"if ({condition})");
					this.Writer.OpenBlock();

					foreach (string expression in outputs)
					{
						this.Writer.Line($"{append} = {expression};");
					}

					this.Writer.Line($"return {next};");
					this.Writer.CloseBlock();
				}
			}

			this.Writer.Line("break;");
			this.Writer.Outdent();
		}

		void EmitFinishCase(int id)
		{
			TxStateModel state = this.TransducerStates[id];

			// A state where the input may not end has no case, so it falls to the default.
			if (state.EndOutput is null) { return; }

			this.Writer.Line($"case {id.ToString(CultureInfo.InvariantCulture)}:");
			this.Writer.Indent();

			foreach (string expression in this.OutputExpressions(state.EndOutput, forFinish: true))
			{
				this.Writer.Line($"canonical[length++] = {expression};");
			}

			this.Writer.Line("return length;");
			this.Writer.Outdent();
		}

		bool FinishCaseNeeded(int id) => this.TransducerStates[id].EndOutput is not null;

		/// <summary>
		/// One expression per character an output emits, with each reference resolved against
		/// the characters kept.
		/// </summary>
		/// <param name="output">The output, over literals and references.</param>
		/// <param name="forFinish">Whether this is an end output, which has no character in hand.</param>
		List<string> OutputExpressions(string output, bool forFinish)
		{
			var expressions = new List<string>();

			for (int i = 0; i < output.Length; ++i)
			{
				if (output[i] != Tx.CopyMarker)
				{
					expressions.Add(CharLiteral(output[i]));
					continue;
				}

				int depth = output[i + 1] - TxReference.DepthBase;

				++i;

				// A step already holds the character it is reading, so depth zero needs no
				// buffer there. The finish function has no character, so it reads even that
				// one back. The character in hand is an int, so it is converted going in.
				if (depth == 0 && !forFinish)
				{
					expressions.Add(this.emitter.Cast("char", "c"));
					continue;
				}

				int at = this.RegisterDepth - 1 - depth;

				expressions.Add($"{HeldName}[{at.ToString(CultureInfo.InvariantCulture)}]");
			}

			return expressions;
		}

		void EmitAcceptingPredicate(string name, ImmutableArray<StateModel> states)
		{
			this.emitter.Comment(this.Writer, "Whether the input may end in this state.");
			this.Writer.Line($"{this.emitter.StepLinkage} bool {name}(int state)");
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
		#region What a range of states leaves unused
		// Each predicate mirrors the case emitter above it: a parameter is used exactly when some
		// state in the range writes a line that names it. The compiler harness in the tests is
		// what keeps them honest.

		void AcceptPrologue(int first, int count)
			=> this.MarkUnused(("c", AnyArcs(this.AcceptedStates, first, count)));

		void EncodePrologue(int first, int count)
			=> this.MarkUnused(
				("c", AnyArcs(this.CanonicalStates, first, count)),
				("total", this.AnyAddition(first, count)));

		void DecodePrologue(int first, int count)
		{
			bool appends = AnyArcs(this.CanonicalStates, first, count);

			this.MarkUnused(
				("remaining", this.AnyRemaining(first, count)),
				("destination", appends),
				("length", appends));
		}

		void CanonicalPrologue(int first, int count)
		{
			bool appends = this.AnyOutput(first, count);

			this.MarkUnused(
				("c", this.AnyTransducerArcs(first, count)),
				(HeldName, !this.NeedsRegister || this.AnyReachBack(first, count, forFinish: false)),
				("canonical", appends),
				("length", appends));
		}

		void FinishPrologue(int first, int count)
		{
			bool any = this.AnyEndOutput(first, count);

			this.MarkUnused(
				("state", any),
				(HeldName, !this.NeedsRegister || this.AnyReachBack(first, count, forFinish: true)),
				("canonical", this.AnyEndText(first, count)),
				("length", any));
		}

		/// <summary>
		/// Voids each parameter the range leaves unused, so the compiler does not warn of it. A
		/// parameter the function does not have counts as used, which is how the register is
		/// passed where there is none.
		/// </summary>
		void MarkUnused(params (string Name, bool Used)[] parameters)
		{
			bool any = false;

			foreach ((string name, bool used) in parameters)
			{
				if (used) { continue; }

				this.Writer.Line($"(void){name};");
				any = true;
			}

			if (any) { this.Writer.Line(); }
		}

		static bool AnyArcs(ImmutableArray<StateModel> states, int first, int count)
		{
			for (int id = first; id < first + count; ++id)
			{
				if (states[id].Arcs.Length > 0) { return true; }
			}

			return false;
		}

		/// <summary>Whether any run in the range adds to the total, which mirrors <see cref="AddedExpression"/>.</summary>
		bool AnyAddition(int first, int count)
		{
			for (int id = first; id < first + count; ++id)
			{
				foreach (ArcModel arc in this.CanonicalStates[id].Arcs)
				{
					if (arc.Set.Count > 1 || arc.SkippedBefore != 0UL) { return true; }
				}
			}

			return false;
		}

		/// <summary>Whether any state in the range reads the remaining value, which mirrors <see cref="EmitDecodeCase"/>.</summary>
		bool AnyRemaining(int first, int count)
		{
			for (int id = first; id < first + count; ++id)
			{
				StateModel state = this.CanonicalStates[id];

				// The terminal state returns before it reads anything.
				if (state.Arcs.Length == 0) { continue; }

				if (state.AcceptsEnd || state.Arcs.Length > 1 || NeedsIndex(state)) { return true; }
			}

			return false;
		}

		bool AnyTransducerArcs(int first, int count)
		{
			for (int id = first; id < first + count; ++id)
			{
				if (this.TransducerStates[id].Arcs.Length > 0) { return true; }
			}

			return false;
		}

		bool AnyOutput(int first, int count)
		{
			for (int id = first; id < first + count; ++id)
			{
				foreach (TxArcModel arc in this.TransducerStates[id].Arcs)
				{
					if (arc.Output.Length > 0) { return true; }
				}
			}

			return false;
		}

		bool AnyEndOutput(int first, int count)
		{
			for (int id = first; id < first + count; ++id)
			{
				if (this.TransducerStates[id].EndOutput is not null) { return true; }
			}

			return false;
		}

		bool AnyEndText(int first, int count)
		{
			for (int id = first; id < first + count; ++id)
			{
				if (this.TransducerStates[id].EndOutput is { Length: > 0 }) { return true; }
			}

			return false;
		}

		/// <summary>
		/// Whether any output in the range reads the register, which mirrors
		/// <see cref="OutputExpressions"/>: a step reads it for any reference past the character in
		/// hand, and the finish function for any reference at all.
		/// </summary>
		bool AnyReachBack(int first, int count, bool forFinish)
		{
			for (int id = first; id < first + count; ++id)
			{
				TxStateModel state = this.TransducerStates[id];

				if (forFinish)
				{
					if (state.EndOutput is not null && ReachesBack(state.EndOutput, forFinish: true)) { return true; }

					continue;
				}

				foreach (TxArcModel arc in state.Arcs)
				{
					if (ReachesBack(arc.Output, forFinish: false)) { return true; }
				}
			}

			return false;
		}

		static bool ReachesBack(string output, bool forFinish)
		{
			for (int i = 0; i < output.Length; ++i)
			{
				if (output[i] != Tx.CopyMarker) { continue; }

				int depth = output[i + 1] - TxReference.DepthBase;

				++i;

				if (depth > 0 || forFinish) { return true; }
			}

			return false;
		}
		#endregion
		#region Expression helpers
		/// <summary>
		/// What passing this run adds to the total, or <see langword="null"/> where it adds nothing.
		/// </summary>
		string? AddedExpression(ulong skipped, ulong count, char first, char last)
		{
			if (first == last) { return skipped == 0UL ? null : this.emitter.Literal(skipped); }

			// The accumulator is unsigned and the subtraction is an int, so the rank is converted
			// here rather than at every use.
			string index = this.emitter.Cast(this.emitter.UInt64, $"c - {CharLiteral(first)}");
			string term = count == 1UL ? index : $"{this.emitter.Literal(count)} * {index}";

			return skipped == 0UL ? term : $"{this.emitter.Literal(skipped)} + {term}";
		}

		/// <summary>
		/// The character at position <c>index</c> within a set, as an expression over its runs.
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
				builder.Append($"index < {this.emitter.Literal(cumulative)} ? {this.RunCharacter(runs[i], offset)} : ");
			}

			builder.Append(this.RunCharacter(runs[runs.Count - 1], cumulative));

			return builder.ToString();
		}

		string RunCharacter((char First, char Last) run, ulong offset)
		{
			if (run.First == run.Last) { return CharLiteral(run.First); }

			string index = offset == 0UL
				? this.emitter.Cast("int", "index")
				: this.emitter.Cast("int", $"index - {this.emitter.Literal(offset)}");

			return this.emitter.Cast("char", $"{CharLiteral(run.First)} + {index}");
		}
		#endregion
	}
}
