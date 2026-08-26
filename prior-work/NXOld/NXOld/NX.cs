// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using NXOld.NXComponents;

namespace NXOld;

/// <summary>
/// Represents an encoding expression ('NX)).
/// </summary>
public sealed class NX : IEquatable<NX>, IBinarySerializable<NX>
{
	#region Private data
	/// <summary>
	/// The state map.
	/// NB state 0 is the start state.
	/// </summary>
	readonly State[] states;
	string? cachedToString;
	#endregion
	#region Private ctors / d-ctors
	NX(State[] states)
	{
		this.states = states;
	}
	#endregion
	#region Public factory methods
	/// <summary>
	/// Creates an <see cref="NX"/> from the specfied text.
	/// </summary>
	/// <param name="text">The text specifying the <see cref="NX"/>.</param>
	/// <returns>The <see cref="NX"/>.</returns>
	public static NX Parse(ReadOnlySpan<char> text)
		=> TryParse(text, out var nx, out string? errorMessage, out int errorOffset)
			? nx
			: throw new ArgumentOutOfRangeException(nameof(text), $"Error at offset {errorOffset}: {errorMessage}")
			;
	/// <summary>
	/// Tries to create an <see cref="NX"/> from the specified text or reports the error.
	/// </summary>
	/// <param name="text">The text specifying the <see cref="NX"/>.</param>
	/// <param name="nx">The created <see cref="NX"/> (if the methods returns <see langword="true"/>).</param>
	/// <param name="errorMessage">The error message (if the methods returns <see langword="false"/>).</param>
	/// <param name="errorOffset">
	/// The (zero-based) offset to the position of the error in <paramref name="text"/>
	/// (if the methods returns <see langword="false"/>).
	/// </param>
	/// <returns>Whether the parse succeeded.</returns>
	public static bool TryParse(ReadOnlySpan<char> text
		, [NotNullWhen(true)] out NX? nx
		, [NotNullWhen(false)] out string? errorMessage
		, out int errorOffset
		)
	{
		if (!Parser.TryParse(text, out var ast, out errorMessage, out errorOffset))
		{
			nx = null;
			return false;
		}

		var stateMap = StateMapGenerator.CreateStateMap(ast);

		nx = new(stateMap);
		errorMessage = null;

		return true;
	}
	#endregion
	#region Public properties and methods
	/// <summary>
	/// Whether this NX accepts the specified text.
	/// </summary>
	/// <param name="text">The text to test for acceptance.</param>
	/// <returns>Whether <paramref name="text"/> is accepted.</returns>
	public bool Accepts(ReadOnlySpan<char> text) => this.states[0].Accepts(text);
	/// <summary>
	/// Whether this NX accepts the specified ASCII byte text.
	/// </summary>
	/// <param name="text">The text to test for acceptance.</param>
	/// <returns>Whether <paramref name="text"/> is accepted.</returns>
	public bool Accepts(ReadOnlySpan<byte> text) => this.states[0].Accepts(text);
	/// <summary>
	/// Gets the encoding of the text:
	/// <list type="bullet">
	/// <item>If the text can be encoded then the result non-zero.</item>
	/// <item>Zero means that the text is <i>not</i> included in the NX.</item>
	/// </list>
	/// </summary>
	/// <param name="text">The text to encode.</param>
	/// <returns>The encoding of the text.</returns>
	public ulong GetEncoding(ReadOnlySpan<char> text) => this.states[0].GetEncoding(text);
	/// <inheritdoc/>
	public bool Equals(NX? other)
	{
		if (other is null) { return false; }

		// It's sufficient to test only the first state
		return this.states[0].Equals(other.states[0]);
	}
	/// <inheritdoc/>
	public override bool Equals(object? obj) => this.Equals(obj as NX);
	/// <inheritdoc/>
	public override int GetHashCode() => this.states[0].GetHashCode();
	/// <summary>
	/// This returns a standardised text version of the NX.
	/// <para>Note</para>
	/// <list type="bullet">
	/// <item>This may well differ from the original form.</item>
	/// <item>
	/// You should not rely on this form remaining unchanged for different versions of the code. 
	/// Specifically, do <i>not</i> use this to test equality.
	/// </item>
	/// </list>
	/// </summary>
	/// <returns>The NX in a text format.</returns>
	public override string ToString()
	{
		if (this.cachedToString is not null) { return this.cachedToString; }

		var nxText = StateMapGenerator.Rehydrate(this.states[0]).ToString();

		this.cachedToString = nxText;

		return nxText;
	}
	/// <summary>
	/// Gets
	/// the source code in the specified programming language 
	/// that is equivalent to 
	/// this NX's <see cref="Accepts(ReadOnlySpan{char})"/> and 
	/// <see cref="GetEncoding(ReadOnlySpan{char})"/> methods.
	/// <para>The actual naming of <c>Accept</c> and <c>GetEncoding</c> 
	/// will be vary to be consistent with 
	/// the standard idiom of <paramref name="language"/>.
	/// For instance, they would become <c>accept</c> and <c>getEncoding</c> in 
	/// <see href="https://en.wikipedia.org/wiki/C_Sharp_(programming_language)">https://en.wikipedia.org/wiki/ECMAScript</see>.
	/// </para>
	/// </summary>
	/// <param name="language">The programming language to use.</param>
	/// <param name="initialIndent">The text for the initial indentation.</param>
	/// <param name="indentPerLevel">The text to add for each additional level of indentation.</param>
	/// <param name="functionModifer">Text to use as the function modifier. <see langword="null"/> means use the specifed languages equivalent to <see langword="public"/>.</param>
	/// <param name="functionNamePrefix">Text to prefix the names <c>Accept</c> and <c>GetEncoding</c>.</param>
	/// <param name="lineEnding">The line ending to use.</param>
	public string GetComputationProgram(ProgrammingLanguage language
		, string initialIndent = ""
		, string indentPerLevel = "    "
		, string? functionModifer = null
		, string functionNamePrefix = ""
		, string lineEnding = "\n"
		)
	{
		// ## Consider StringBuilder pooling
		var sb = new StringBuilder();
		this.AppendComputationProgram(sb, language
			, initialIndent: initialIndent
			, indentPerLevel: indentPerLevel
			, functionModifer: functionModifer
			, functionNamePrefix: functionNamePrefix
			, lineEnding: lineEnding
			);
		return sb.ToString();
	}

	/// <summary>
	/// Appends 
	/// the source code in the specified programming language 
	/// to the string builder 
	/// that is equivalent to 
	/// this NX's <see cref="Accepts(ReadOnlySpan{char})"/> and 
	/// <see cref="GetEncoding(ReadOnlySpan{char})"/> methods.
	/// <para>The actual naming of <c>Accept</c> and <c>GetEncoding</c> 
	/// will be vary to be consistent with 
	/// the standard idiom of <paramref name="language"/>.
	/// For instance, they would become <c>accept</c> and <c>getEncoding</c> in 
	/// <see href="https://en.wikipedia.org/wiki/C_Sharp_(programming_language)">https://en.wikipedia.org/wiki/ECMAScript</see>.
	/// </para>
	/// </summary>
	/// <param name="sb">The string builder to which the source code is to be added.</param>
	/// <param name="language">The programming language to use.</param>
	/// <param name="initialIndent">The text for the initial indentation.</param>
	/// <param name="indentPerLevel">The text to add for each additional level of indentation.</param>
	/// <param name="functionModifer">Text to use as the function modifier. <see langword="null"/> means use the specifed languages equivalent to <see langword="public"/>.</param>
	/// <param name="functionNamePrefix">Text to prefix the names <c>Accept</c> and <c>GetEncoding</c>.</param>
	/// <param name="lineEnding">The line ending to use.</param>
	public void AppendComputationProgram(StringBuilder sb, ProgrammingLanguage language
		, string initialIndent = ""
		, string indentPerLevel = "    "
		, string? functionModifer = null
		, string functionNamePrefix = ""
		, string lineEnding = "\n"
		)
	{
		switch (language)
		{
			case ProgrammingLanguage.CSharp:
				this.AppendNXComputationProgram_CSharp(sb, language, initialIndent, indentPerLevel, functionModifer, functionNamePrefix, lineEnding);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(language), language, "Unrecognised value.");
		}
	}
	#endregion
	#region Specific programming language implementations
	static string XmlEscape(string text) => System.Security.SecurityElement.Escape(text);
	void AppendNXComputationProgram_CSharp(StringBuilder sb, ProgrammingLanguage language
		, string initialIndent
		, string indentPerLevel
		, string? functionModifer
		, string functionNamePrefix
		, string lineEnding
		)
	{
		var genTimestampText = DateTime.UtcNow.ToString("u");
		var buildTimestampText = BuildInfo.UtcTimestamp.ToString("u");

		functionModifer ??= "public";

		var states = this.states;

		static void AddNumberLiteral32s(StringBuilder sb, int value) { sb.Append(value.ToString(NumberFormatInfo.InvariantInfo)); }
		static void AddNumberLiteral64u(StringBuilder sb, ulong value) { sb.Append(value.ToString(NumberFormatInfo.InvariantInfo)); }
		static void AddCharacterLiteral(StringBuilder sb, char value)
		{
			if (value < '\u0020' || value == '\u007F')
			{
				sb.Append(@$"\u{(uint)value:X4}");
			}
			else if (value == '\\')
			{
				sb.Append(@"\\");
			}
			else
			{
				sb.Append(value);
			}
		}

		#region Accepts
		for (int index = 0; index < states.Length; ++index)
		{
			var state = states[index];
			var transitions = state.transitions ?? throw new Exception();

			if (index == 0)
			{
				// Exemplar: [indent_0]/// <summary>
				sb.Append(initialIndent).Append("/// <summary>").Append(lineEnding);
				// Exemplar: [indent_0]/// Whether this NX accepts the specified text.
				sb.Append(initialIndent)
					.Append("/// Whether the NX <c>")
					.Append(XmlEscape(this.ToString()))
					.Append("</c> accepts the specified text.")
					.Append(lineEnding);
				// Exemplar: [indent_0]/// <para>This code was generated on [TIMESTAMP] by LogMu.NX (built on [TIMESTAMP]).</para>"
				sb
					.Append(initialIndent)
					.Append("/// <para>This code was generated on ")
					.Append(genTimestampText)
					.Append(" by LogMu.NX (built ")
					.Append(buildTimestampText)
					.Append(").</para>")
					.Append(lineEnding);
				// Exemplar: [indent_0]/// </summary>
				sb.Append(initialIndent).Append("/// </summary>").Append(lineEnding);
				// Exemplar: [indent_0]/// <param name="text">The text to test for acceptance.</param>
				sb.Append(initialIndent).Append("/// <param name=\"text\">The text to test for acceptance.</param>").Append(lineEnding);
				// Exemplar: [indent_0]/// <returns>Whether <paramref name="text"/> is accepted.</returns>
				sb.Append(initialIndent).Append("/// <returns>Whether <paramref name=\"text\"/> is accepted.</returns>").Append(lineEnding);
				sb.Append(initialIndent);
				// Exemplar: [indent_0]public bool [functionNamePrefix]Accepts(ReadOnlySpan<byte> text)
				if (functionModifer != "") { sb.Append(functionModifer).Append(' '); }
				sb
					.Append("static bool ")
					.Append(functionNamePrefix)
					.Append("Accepts");

			}
			else
			{
				// Exemplar: [indent_0]bool s1_Accepts(ReadOnlySpan<byte> text)
				sb
					.Append(initialIndent)
					.Append("static bool s");
				AddNumberLiteral32s(sb, index);
				sb.Append("_Accepts");
			}
			sb
				.Append("(ReadOnlySpan<char> text)")
				.Append(lineEnding);

			// Exemplar: [indent_0]{
			sb.Append(initialIndent).Append('{').Append(lineEnding);

			// Exemplar: [indent_0][indent_Δ]if (text.Length == 0) {return true/false; }
			bool includesEndOfText = false;
			foreach (var transition in transitions)
			{
				if (transition.CharSet.IsEmpty)
				{
					includesEndOfText = true;
					break;
				}
			}
			sb
				.Append(initialIndent)
				.Append(indentPerLevel)
				.Append("if (text.Length == 0) { return ")
				.Append(includesEndOfText ? "true" : "false")
				.Append("; }")
				.Append(lineEnding);

			if (transitions.Length > (includesEndOfText ? 1 : 0))
			{
				// Exemplar: [BLANK]
				sb.Append(lineEnding);

				// Exemplar: [indent_0][indent_Δ]var tail = text.Slice(1);
				sb
					.Append(initialIndent).Append(indentPerLevel)
					.Append("var tail = text.Slice(1);")
					.Append(lineEnding);

				// Exemplar: [indent_0][indent_Δ]switch(text[0])
				sb
					.Append(initialIndent).Append(indentPerLevel)
					.Append("switch(text[0])")
					.Append(lineEnding);

				// Exemplar: [indent_0][indent_Δ]{
				sb
					.Append(initialIndent).Append(indentPerLevel)
					.Append('{')
					.Append(lineEnding);

				foreach (var (charSet, nextState) in transitions)
				{
					if (!charSet.IsEmpty)
					{
						foreach (char c in charSet)
						{
							// Exemplar: [indent_0][indent_Δ][indent_Δ]case 'A':
							sb
								.Append(initialIndent).Append(indentPerLevel).Append(indentPerLevel)
								.Append("case '");
							AddCharacterLiteral(sb, c);
							sb
								.Append("':")
								.Append(lineEnding);
						}

						// Exemplar: [indent_0][indent_Δ][indent_Δ][indent_Δ]return s3_accepts(tail);
						sb
							.Append(initialIndent).Append(indentPerLevel).Append(indentPerLevel).Append(indentPerLevel)
							.Append("return s");
						var indexOfNextState = GetStateIndex(states, nextState);
						AddNumberLiteral32s(sb, indexOfNextState);
						sb
							.Append("_Accepts(tail);")
							.Append(lineEnding);
					}
				}

				// Exemplar: [indent_0][indent_Δ]}
				sb
					.Append(initialIndent).Append(indentPerLevel)
					.Append('}').Append(lineEnding);
			}

			// Exemplar: [indent_0][indent_Δ]return false;
			sb
				.Append(initialIndent).Append(indentPerLevel)
				.Append("return false;").Append(lineEnding);

			// Exemplar: [indent_0]}
			sb.Append(initialIndent).Append('}').Append(lineEnding);
		}
		#endregion

		#region GetEncoding
		for (int index = 0; index < states.Length; ++index)
		{
			var state = states[index];
			var transitions = state.transitions ?? throw new Exception();

			if (index == 0)
			{
				// Exemplar: [indent_0]/// <summary>
				sb.Append(initialIndent).Append("/// <summary>").Append(lineEnding);
				// Exemplar: [indent_0]/// 
				// Exemplar: [indent_0]/// Whether this NX accepts the specified text.
				sb.Append(initialIndent)
					.Append("/// Gets the encoding of the text by the NX <c>")
					.Append(XmlEscape(this.ToString()))
					.Append("</c>.")
					.Append(lineEnding);
				// Exemplar: [indent_0]/// <list type="bullet">
				sb.Append(initialIndent).Append("/// <list type=\"bullet\">").Append(lineEnding);
				// Exemplar: [indent_0]/// <item>If the text can be encoded then the result non-zero.</item>
				sb.Append(initialIndent).Append("/// <item>If the text can be encoded then the result non-zero.</item>").Append(lineEnding);
				// Exemplar: [indent_0]/// <item>Zero means that the text is <i>not</i> included in the NX.</item>
				sb.Append(initialIndent).Append("/// <item>Zero means that the text is <i>not</i> included in the NX.</item>").Append(lineEnding);
				// Exemplar: [indent_0]/// </list>
				sb.Append(initialIndent).Append("/// </list>").Append(lineEnding);
				// Exemplar: [indent_0]/// <para>This code was generated on [TIMESTAMP] by LogMu.NX (built on [TIMESTAMP]).</para>"
				sb
					.Append(initialIndent)
					.Append("/// <para>This code was generated on ")
					.Append(genTimestampText)
					.Append(" by LogMu.NX (built ")
					.Append(buildTimestampText)
					.Append(").</para>")
					.Append(lineEnding);
				// Exemplar: [indent_0]/// </summary>
				sb.Append(initialIndent).Append("/// </summary>").Append(lineEnding);
				// Exemplar: [indent_0]/// <param name="text">The text to encode.</param>
				sb.Append(initialIndent).Append("/// <param name=\"text\">The text to encode.</param>").Append(lineEnding);
				// Exemplar: [indent_0]/// <returns>The encoding of <paramref name="text"/> (which is zero if the text does not match the NX).</returns>
				sb.Append(initialIndent).Append("/// <returns>The encoding of <paramref name=\"text\"/> (which is zero if the text does not match the NX).</returns>").Append(lineEnding);
				sb.Append(initialIndent);
				// Exemplar: [indent_0]public ulong [functionNamePrefix]GetEncoding(ReadOnlySpan<byte> text)
				if (functionModifer != "") { sb.Append(functionModifer).Append(' '); }
				sb
					.Append("static ulong ")
					.Append(functionNamePrefix)
					.Append("GetEncoding");
			}
			else
			{
				// Exemplar: [indent_0]ulong s1_GetEncoding(ReadOnlySpan<byte> text)
				sb
					.Append(initialIndent)
					.Append("static ulong s");
				AddNumberLiteral32s(sb, index);
				sb.Append("_GetEncoding");
			}
			sb
				.Append("(ReadOnlySpan<char> text)")
				.Append(lineEnding);

			// Exemplar: [indent_0]{
			sb.Append(initialIndent).Append('{').Append(lineEnding);

			// Exemplar: [indent_0][indent_Δ]if (text.Length == 0) {return 1/0; }
			bool includesEndOfText = false;
			foreach (var transition in transitions)
			{
				if (transition.CharSet.IsEmpty)
				{
					includesEndOfText = true;
					break;
				}
			}
			sb
				.Append(initialIndent).Append(indentPerLevel)
				.Append("if (text.Length == 0) { return ")
				.Append(includesEndOfText ? "1" : "0")
				.Append("; }")
				.Append(lineEnding);

			int nonEndOfTextTransitions = transitions.Length;
			if (includesEndOfText) { --nonEndOfTextTransitions; }

			if (nonEndOfTextTransitions > 0)
			{
				// Exemplar: [BLANK]
				sb.Append(lineEnding);

				// Generally, we'll leave optimisation to the compiler.
				// But we do hand-optimise in the special case that there is a single character.

				char? singleChar = null;
				State singleCharNextState = default;
				if (nonEndOfTextTransitions == 1)
				{
					foreach (var transition in transitions)
					{
						singleChar = transition.CharSet.SingleCharacter;
						if (singleChar is not null)
						{
							singleCharNextState = transition.NextState;
							break;
						}
					}
				}

				if (singleChar is not null)
				{
					// Exemplar: [indent_0][indent_Δ]if (text[0] == 'A')
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append("if (text[0] == '");
					AddCharacterLiteral(sb, singleChar.Value);
					sb
						.Append("')")
						.Append(lineEnding);
					// Exemplar: [indent_0][indent_Δ]{;
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append('{')
						.Append(lineEnding);
					// Exemplar: [indent_0][indent_Δ][indent_Δ]var nextEncoding = s3_GetEncoding(text.Slice(1));
					sb
						.Append(initialIndent).Append(indentPerLevel).Append(indentPerLevel).Append(indentPerLevel)
						.Append("ulong nextEncoding = s");
					var indexOfNextState = GetStateIndex(states, singleCharNextState);
					AddNumberLiteral32s(sb, indexOfNextState);
					sb
						.Append("_GetEncoding(text.Slice(1));")
						.Append(lineEnding);

					if (transitions.Length == 1)
					{
						// Exemplar: [indent_0][indent_Δ]return nextEncoding;
						sb.Append("return nextEncoding;");
					}
					else
					{
						Debug.Assert(transitions.Length == 2);
						// This is preceded by an end of text transition so we need to add 1 to the encoding to account for it.
						// Exemplar: [indent_0][indent_Δ]return (nextEncoding == 0) ? 0 : 1ul + nextEncoding;
						sb
							.Append(initialIndent).Append(indentPerLevel).Append(indentPerLevel).Append(indentPerLevel)
							.Append("return (nextEncoding == 0) ? 0 : 1ul + nextEncoding;");
					}
					sb.Append(lineEnding);

					// Exemplar: [indent_0][indent_Δ]};
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append('}')
						.Append(lineEnding);
				}
				else
				{
					// Exemplar: [indent_0][indent_Δ]var tail = text.Slice(1);
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append("var tail = text.Slice(1);")
						.Append(lineEnding);

					// Exemplar: [indent_0][indent_Δ]int i = -1;)
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append("int i = -1;")
						.Append(lineEnding);

					// Exemplar: [indent_0][indent_Δ]ulong nextEncoding;)
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append("ulong nextEncoding;")
						.Append(lineEnding);

					// Exemplar: [indent_0][indent_Δ]switch ((int)text[0])
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append("switch ((int)text[0])")
						.Append(lineEnding);

					// Exemplar: [indent_0][indent_Δ]{
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append('{')
						.Append(lineEnding);

					ulong encodingOffset = 0u;
					foreach (var (charSet, nextState) in transitions)
					{
						var n = nextState.CharacterCombinationCount;
						if (!charSet.IsEmpty)
						{
							var indexOfChar = 0;
							int indexOfNextState = GetStateIndex(states, nextState);
							foreach (char c in charSet)
							{
								// Exemplar: [indent_0][indent_Δ][indent_Δ]case 'A': goto case -3;
								sb
									.Append(initialIndent).Append(indentPerLevel).Append(indentPerLevel)
									.Append("case '");
								AddCharacterLiteral(sb, c);
								sb.Append("': i = ");
								AddNumberLiteral32s(sb, indexOfChar);
								sb.Append("; goto case -");
								AddNumberLiteral32s(sb, indexOfNextState);
								sb
									.Append(";")
									.Append(lineEnding);
								++indexOfChar;
							}

							// Exemplar: [indent_0][indent_Δ][indent_Δ]case -3:
							sb
								.Append(initialIndent).Append(indentPerLevel).Append(indentPerLevel)
								.Append("case -");
							AddNumberLiteral32s(sb, indexOfNextState);
							sb
								.Append(":")
								.Append(lineEnding);

							// Exemplar: [indent_0][indent_Δ][indent_Δ][indent_Δ]var nextEncoding = s3_GetEncoding(tail);
							sb
								.Append(initialIndent).Append(indentPerLevel).Append(indentPerLevel).Append(indentPerLevel)
								.Append("nextEncoding = s");
							AddNumberLiteral32s(sb, indexOfNextState);
							sb
								.Append("_GetEncoding(tail);")
								.Append(lineEnding);

							// Exemplar: [indent_0][indent_Δ][indent_Δ][indent_Δ]return (nextEncoding == 0) ? 0 : [encodingOffset] + [n] * (ulong)i + nextEncoding;
							sb
								.Append(initialIndent).Append(indentPerLevel).Append(indentPerLevel).Append(indentPerLevel)
								.Append("return (nextEncoding == 0) ? 0 : ");
							if (encodingOffset != 0)
							{
								AddNumberLiteral64u(sb, encodingOffset);
								sb.Append("ul + ");
							}
							if (n != 1)
							{
								AddNumberLiteral64u(sb, n);
								sb.Append(" * ");
							}
							sb.Append("(ulong)i");
							sb.Append(" + nextEncoding;")
								.Append(lineEnding);
						}

						// The max below allows for a possible end of text transition which
						// *does* increase the offset but for which charSet.Count is 0.
						encodingOffset += n * (ulong)Math.Max(1, charSet.Count);
					}

					// Exemplar: [indent_0][indent_Δ]}
					sb
						.Append(initialIndent).Append(indentPerLevel)
						.Append('}').Append(lineEnding);
				}
			}

			// Exemplar: [indent_0][indent_Δ]return false;
			sb
				.Append(initialIndent).Append(indentPerLevel)
				.Append("return 0;").Append(lineEnding);

			// Exemplar: [indent_0]}
			sb.Append(initialIndent).Append('}').Append(lineEnding);
		}
		#endregion
	}
	#endregion
	#region Private properties and methods
	static int GetStateIndex(State[] states, State state)
	{
		for (int index = 0; index < states.Length; ++index)
		{
			if (state == states[index]) { return index; }
		}
		return -1;
	}
	#endregion
	#region IO
	/// <inheritdoc/>
	public void WriteTo(BinaryWriter writer)
	{
		writer.Write(IOMagicNumbers.NX_Begin);

		var states = this.states;

		writer.Write7BitEncodedInt(states.Length);

		// Write the states in *reverse* order so that
		// dependent states come later, which means
		// we can reconstruct the states when we de-serialise.
		for (int stateIndex = states.Length - 1; stateIndex >= 0; --stateIndex)
		{
			var state = states[stateIndex];
			var transitions = state.transitions!;
			writer.Write7BitEncodedInt(transitions.Length);
			foreach (var (charSet, nextState) in transitions)
			{
				charSet.WriteTo(writer);

				Debug.Assert(charSet.IsEmpty == nextState.IsNull);

				if (!charSet.IsEmpty)
				{
					int nextStateIndex = GetStateIndex(states, nextState);
					Debug.Assert(nextStateIndex < states.Length);
					Debug.Assert(nextStateIndex > stateIndex);
					writer.Write7BitEncodedInt(nextStateIndex);
				}
			}
		}

		writer.Write(IOMagicNumbers.NX_End);
	}
	/// <inheritdoc/>
	/// <inheritdoc/>
	public static bool TryReadFrom(BinaryReader reader, [MaybeNullWhen(false)] out NX instance, [NotNullWhen(false)] out string? errorMessage)
	{
		const string ErrorMessage = $"Binary deserialisation error when expecting an {nameof(NX)}.";

		try
		{
			if (reader.ReadUInt32() != IOMagicNumbers.NX_Begin)
			{
				instance = default;
				errorMessage = ErrorMessage;
				return false;
			}

			int stateCount = reader.Read7BitEncodedInt();
			var states = new State[stateCount];

			for (int stateIndex = states.Length - 1; stateIndex >= 0; --stateIndex)
			{
				int transitionCount = reader.Read7BitEncodedInt();
				if (transitionCount <= 0)
				{
					instance = default;
					errorMessage = ErrorMessage;
					return false;
				}
				else if (transitionCount == 1)
				{
					var charSet = reader.Read<AsciiCharSet>();
					State state;
					if (charSet.IsEmpty)
					{
						state = State.DefinitiveEndOfText;
					}
					else
					{
						int indexNextState = reader.Read7BitEncodedInt();
						if (indexNextState <= stateIndex || indexNextState > states.Length)
						{
							instance = default;
							errorMessage = ErrorMessage;
							return false;
						}
						var nextState = states[indexNextState];
						if (nextState.IsNull)
						{
							instance = default;
							errorMessage = ErrorMessage;
							return false;
						}
						var transition = new Transition(charSet, nextState);
						state = new State([transition], transitionsHaveBeenValidated: true);
					}
					states[stateIndex] = state;
				}
				else
				{
					var transitions = new Transition[transitionCount];
					for (int transtionIndex = 0; transtionIndex < transitions.Length; ++transtionIndex)
					{
						var charSet = reader.Read<AsciiCharSet>();
						Transition transition;
						if (charSet.IsEmpty)
						{
							transition = default;
						}
						else
						{
							int indexNextState = reader.Read7BitEncodedInt();
							if (indexNextState <= stateIndex || indexNextState > states.Length)
							{
								instance = default;
								errorMessage = ErrorMessage;
								return false;
							}
							var nextState = states[indexNextState];
							if (nextState.IsNull)
							{
								instance = default;
								errorMessage = ErrorMessage;
								return false;
							}
							transition = new Transition(charSet, nextState);
						}
						transitions[transtionIndex] = transition;
					}
					states[stateIndex] = new State(transitions, transitionsHaveBeenValidated: false);
				}
			}

			if (reader.ReadUInt32() != IOMagicNumbers.NX_End)
			{
				instance = default;
				errorMessage = ErrorMessage;
				return false;
			}

			instance = new NX(states);
			errorMessage = default;
			return true;
		}
		catch (Exception e)
		{
			instance = default;
			errorMessage = $"{ErrorMessage} {e.Message}";
			return false;
		}
	}
	#endregion
}