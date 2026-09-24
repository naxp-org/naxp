// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.Globalization;

namespace LogMu;

/// <summary>
/// Emits a compiled naxp as a C++ fragment, in C++17.
/// </summary>
/// <remarks>
/// <para>
/// The fragment is three constants, eight public functions and their steppers, every name in
/// snake_case under the caller's prefix, and its surface is the library's own
/// <c>logmu::naxp</c>: a <c>string_view</c> in, a <c>std::string</c> out, <c>decode</c>
/// throwing <c>std::out_of_range</c> and <c>try_decode</c> reporting instead, and a
/// <c>try_</c> overload writing into the caller's buffer wherever text comes out. It needs
/// <c>cstdint</c>, <c>optional</c>, <c>stdexcept</c>, <c>string</c> and <c>string_view</c>,
/// which the caller includes, and its first line says so.
/// </para>
/// <para>
/// Every function is <c>inline</c> and both constants <c>inline constexpr</c>, so the fragment
/// can sit in a header that several translation units include. That is also why the caller
/// gives each naxp its own prefix where several share a program: two inline functions of one
/// name and different bodies in different translation units break the one definition rule, and
/// no compiler is obliged to notice.
/// </para>
/// </remarks>
sealed class CppEmitter : CFamilyEmitter
{
	/// <summary>The shared instance, which is stateless and serves every call concurrently.</summary>
	public static CppEmitter Instance { get; } = new();

	#region The spelling of C++
	/// <inheritdoc/>
	protected override string StepLinkage => "inline";

	/// <inheritdoc/>
	protected override string TypePrefix => "std::";

	/// <summary>C++14's digit separator.</summary>
	protected override string? DigitSeparator => "'";

	/// <inheritdoc/>
	protected override string TextParameters => "std::string_view text";

	/// <inheritdoc/>
	protected override string Pointer(string type, string name) => $"{type}* {name}";

	/// <inheritdoc/>
	protected override string ByReference(string type, string name) => $"{type}& {name}";

	/// <inheritdoc/>
	protected override string Dereference(string name) => name;

	/// <inheritdoc/>
	protected override string Increment(string name) => $"{name}++";

	/// <inheritdoc/>
	protected override string AddressOf(string name) => name;

	/// <inheritdoc/>
	protected override string Cast(string type, string expression) => $"static_cast<{type}>({expression})";

	/// <inheritdoc/>
	protected override void Comment(CodeWriter writer, string text) => writer.Line($"/// {text}");
	#endregion
	#region The header
	/// <inheritdoc/>
	protected override void EmitHeader(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;

		writer.Line("// Needs <cstdint>, <optional>, <stdexcept>, <string> and <string_view>. Everything is inline,");
		writer.Line("// so the fragment can sit in a header; give each naxp its own prefix where several share a");
		writer.Line("// program.");
		writer.Line();
		this.Comment(writer, "The naxp this code was generated from.");
		writer.Line($"inline constexpr std::string_view {fragment.PatternName} = {PatternLiteral(fragment.Context.Compilation.Pattern)};");
		writer.Line();
		this.Comment(writer, "The largest encoded value this naxp produces, which is also how many it has.");
		writer.Line($"inline constexpr {fragment.ValueKeyword} {fragment.MaxEncodedValueName} = {fragment.MaxEncodedValueLiteral};");
		writer.Line();
		this.Comment(writer, "The length of the longest string this naxp can decode a value to.");
		writer.Line($"inline constexpr int {fragment.MaxLengthName} = {fragment.MaxLength.ToString(CultureInfo.InvariantCulture)};");
		writer.Line();
	}
	#endregion
	#region The public functions
	/// <inheritdoc/>
	protected override void EmitPublics(Fragment fragment)
	{
		this.EmitAccepts(fragment);
		this.EmitEncode(fragment);
		this.EmitDecode(fragment);
		this.EmitCanonicalForm(fragment);
	}

	void EmitAccepts(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;

		writer.Line("/// Whether this naxp accepts text. A byte outside ASCII is never accepted.");
		writer.Line("///");
		writer.Line("/// @param text The text to test.");
		writer.Line("/// @returns Whether the naxp accepts it.");
		writer.Line($"inline bool {fragment.AcceptsName}(std::string_view text)");
		writer.OpenBlock();
		writer.Line("int state = 0;");
		writer.Line();
		writer.Line("for (char c : text)");
		writer.OpenBlock();
		writer.Line($"state = {fragment.AcceptStepName}(state, static_cast<unsigned char>(c));");
		writer.Line();
		writer.Line("if (state < 0) { return false; }");
		writer.CloseBlock();
		writer.Line();
		writer.Line($"return {fragment.IsAcceptingName}(state);");
		writer.CloseBlock();
		writer.Line();
	}

	void EmitEncode(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;

		writer.Line("/// The encoded value of text.");
		writer.Line("///");
		writer.Line("/// @param text The text to encode.");
		writer.Line($"/// @returns The encoded value, from 1 to {fragment.MaxEncodedValueName}, or zero where the text is invalid.");
		writer.Line($"inline {fragment.ValueKeyword} {fragment.EncodeName}(std::string_view text)");
		writer.OpenBlock();

		if (fragment.Canonicalises)
		{
			writer.Line($"char canonical[{fragment.BufferSize}] = {{}};");
			writer.Line($"int length = {fragment.CanonicaliseName}(text, canonical);");
			writer.Line();
			writer.Line(fragment.ValueIsWidest
				? $"return length < 0 ? 0ULL : {fragment.RankName}(canonical, length);"
				: $"return length < 0 ? static_cast<{fragment.ValueKeyword}>(0) : static_cast<{fragment.ValueKeyword}>({fragment.RankName}(canonical, length));");
		}
		else
		{
			writer.Line("int state = 0;");
			writer.Line("std::uint64_t total = 0ULL;");
			writer.Line();
			writer.Line("for (char c : text)");
			writer.OpenBlock();
			writer.Line($"state = {fragment.EncodeStepName}(state, static_cast<unsigned char>(c), total);");
			writer.Line();
			writer.Line($"if (state < 0) {{ return {fragment.ValueZero}; }}");
			writer.CloseBlock();
			writer.Line();
			writer.Line(fragment.ValueIsWidest
				? $"return {fragment.IsAcceptingName}(state) ? total + 1ULL : 0ULL;"
				: $"return {fragment.IsAcceptingName}(state) ? static_cast<{fragment.ValueKeyword}>(total + 1ULL) : static_cast<{fragment.ValueKeyword}>(0);");
		}

		writer.CloseBlock();
		writer.Line();
	}

	void EmitDecode(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;
		string tryDecodeName = fragment.Name("TryDecode");

		writer.Line("/// The string a value stands for.");
		writer.Line("///");
		writer.Line($"/// @param value The encoded value, from 1 to {fragment.MaxEncodedValueName}.");
		writer.Line("/// @returns The string, which is in canonical form.");
		writer.Line("/// @throws std::out_of_range The value is not one this naxp produces.");
		writer.Line($"inline std::string {fragment.DecodeName}({fragment.ValueKeyword} value)");
		writer.OpenBlock();
		writer.Line($"if (value < {fragment.ValueOne} || value > {fragment.MaxEncodedValueName})");
		writer.OpenBlock();
		writer.Line($"throw std::out_of_range(\"This naxp encodes the values 1 to {fragment.MaxEncodedValueDigits}.\");");
		writer.CloseBlock();
		writer.Line();
		writer.Line($"char buffer[{fragment.BufferSize}] = {{}};");
		writer.Line($"int length = {fragment.DecodeCoreName}({fragment.DecodeCoreArgument}, buffer);");
		writer.Line();
		writer.Line("return std::string(buffer, buffer + length);");
		writer.CloseBlock();
		writer.Line();

		writer.Line("/// Tries to find the string a value stands for.");
		writer.Line("///");
		writer.Line("/// @param value The encoded value.");
		writer.Line("/// @param text Where the string goes, which is in canonical form. Untouched where the value is not one this naxp produces.");
		writer.Line("/// @returns Whether the value is one this naxp produces.");
		writer.Line($"inline bool {tryDecodeName}({fragment.ValueKeyword} value, std::string& text)");
		writer.OpenBlock();
		writer.Line($"if (value < {fragment.ValueOne} || value > {fragment.MaxEncodedValueName}) {{ return false; }}");
		writer.Line();
		writer.Line($"char buffer[{fragment.BufferSize}] = {{}};");
		writer.Line($"int length = {fragment.DecodeCoreName}({fragment.DecodeCoreArgument}, buffer);");
		writer.Line();
		writer.Line("text.assign(buffer, buffer + length);");
		writer.Line();
		writer.Line("return true;");
		writer.CloseBlock();
		writer.Line();

		writer.Line("/// Tries to write the string a value stands for, which is in canonical form, with no terminator.");
		writer.Line("///");
		writer.Line("/// @param value The encoded value.");
		writer.Line($"/// @param destination Where the string is written. {fragment.MaxLengthName} characters always suffice.");
		writer.Line("/// @param capacity How many characters there is room for.");
		writer.Line("/// @param length How many characters were written, or zero where none were.");
		writer.Line("/// @returns False where the value is not one this naxp produces, or the destination is too short, in which case nothing is written.");
		writer.Line($"inline bool {tryDecodeName}({fragment.ValueKeyword} value, char* destination, std::size_t capacity, std::size_t& length)");
		writer.OpenBlock();
		writer.Line($"if (value < {fragment.ValueOne} || value > {fragment.MaxEncodedValueName})");
		writer.OpenBlock();
		writer.Line("length = 0;");
		writer.Line("return false;");
		writer.CloseBlock();
		writer.Line();
		writer.Line($"char buffer[{fragment.BufferSize}] = {{}};");
		writer.Line($"int written = {fragment.DecodeCoreName}({fragment.DecodeCoreArgument}, buffer);");
		writer.Line();
		EmitCopyOut(writer, mayFail: false);
	}

	void EmitCanonicalForm(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;
		string tryName = fragment.Name("TryCanonicalForm");

		writer.Line("/// The canonical form of text, which is the text decoding its encoded value gives back.");
		writer.Line("///");
		writer.Line("/// @param text The text.");
		writer.Line("/// @returns The canonical form, or no value where the text is invalid.");
		writer.Line($"inline std::optional<std::string> {fragment.CanonicalFormName}(std::string_view text)");
		writer.OpenBlock();
		writer.Line($"char buffer[{fragment.BufferSize}] = {{}};");
		writer.Line($"int length = {fragment.CanonicaliseName}(text, buffer);");
		writer.Line();
		writer.Line("if (length < 0) { return std::nullopt; }");
		writer.Line();
		writer.Line("return std::string(buffer, buffer + length);");
		writer.CloseBlock();
		writer.Line();

		writer.Line("/// Tries to find the canonical form of text.");
		writer.Line("///");
		writer.Line("/// @param text The text.");
		writer.Line("/// @param canonical_form Where the canonical form goes. Untouched where the text is invalid.");
		writer.Line("/// @returns Whether the naxp accepts the text.");
		writer.Line($"inline bool {tryName}(std::string_view text, std::string& canonical_form)");
		writer.OpenBlock();
		writer.Line($"char buffer[{fragment.BufferSize}] = {{}};");
		writer.Line($"int length = {fragment.CanonicaliseName}(text, buffer);");
		writer.Line();
		writer.Line("if (length < 0) { return false; }");
		writer.Line();
		writer.Line("canonical_form.assign(buffer, buffer + length);");
		writer.Line();
		writer.Line("return true;");
		writer.CloseBlock();
		writer.Line();

		writer.Line("/// Tries to write the canonical form of text, with no terminator.");
		writer.Line("///");
		writer.Line("/// @param text The text.");
		writer.Line($"/// @param destination Where the canonical form is written. {fragment.MaxLengthName} characters always suffice.");
		writer.Line("/// @param capacity How many characters there is room for.");
		writer.Line("/// @param length How many characters were written, or zero where none were.");
		writer.Line("/// @returns False where the text is invalid, or the destination is too short, in which case nothing is written.");
		writer.Line($"inline bool {tryName}(std::string_view text, char* destination, std::size_t capacity, std::size_t& length)");
		writer.OpenBlock();
		writer.Line($"char buffer[{fragment.BufferSize}] = {{}};");
		writer.Line($"int written = {fragment.CanonicaliseName}(text, buffer);");
		writer.Line();
		EmitCopyOut(writer, mayFail: true);
	}

	/// <summary>
	/// The end of a function writing into the caller's buffer: the check that <c>written</c>
	/// characters of <c>buffer</c> fit, the copy, and the length.
	/// </summary>
	/// <param name="writer">Where the fragment is going.</param>
	/// <param name="mayFail">Whether <c>written</c> is -1 where the text was invalid.</param>
	static void EmitCopyOut(CodeWriter writer, bool mayFail)
	{
		writer.Line(mayFail
			? "if (written < 0 || static_cast<std::size_t>(written) > capacity)"
			: "if (static_cast<std::size_t>(written) > capacity)");
		writer.OpenBlock();
		writer.Line("length = 0;");
		writer.Line("return false;");
		writer.CloseBlock();
		writer.Line();
		writer.Line("std::char_traits<char>::copy(destination, buffer, static_cast<std::size_t>(written));");
		writer.Line("length = static_cast<std::size_t>(written);");
		writer.Line();
		writer.Line("return true;");
		writer.CloseBlock();
		writer.Line();
	}

	/// <inheritdoc/>
	protected override void EmitCanonicalise(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;

		fragment.OpenCanonicalise();

		if (!fragment.Canonicalises)
		{
			// Where nothing is unified an accepted string is its own canonical form, and being
			// accepted it fits the buffer.
			writer.Line($"if (!{fragment.AcceptsName}(text)) {{ return -1; }}");
			writer.Line();
			writer.Line("text.copy(canonical, text.size());");
			writer.Line();
			writer.Line("return static_cast<int>(text.size());");
			writer.CloseBlock();
			writer.Line();

			return;
		}

		fragment.DeclareRegister("{}");
		writer.Line("int length = 0;");
		writer.Line("int state = 0;");
		writer.Line();
		writer.Line("for (char c : text)");
		writer.OpenBlock();
		fragment.KeepCharacter("c");
		writer.Line($"state = {fragment.CanonicalStepName}(state, static_cast<unsigned char>(c), {fragment.StepArguments("length")});");
		writer.Line();
		writer.Line("if (state < 0) { return -1; }");
		writer.CloseBlock();
		writer.Line();
		writer.Line($"return {fragment.FinishCanonicalName}(state, {fragment.FinishArguments()});");
		writer.CloseBlock();
		writer.Line();
	}

	/// <summary>
	/// A pattern as a C++ literal: raw where every character is printable and nothing in it
	/// closes a raw string early, which keeps its backslashes as they were written, and escaped
	/// otherwise.
	/// </summary>
	static string PatternLiteral(string text)
	{
		foreach (char c in text)
		{
			if (c < ' ' || c > '~') { return StringLiteral(text); }
		}

		return text.Contains(")\"") ? StringLiteral(text) : $"R\"({text})\"";
	}
	#endregion
}
