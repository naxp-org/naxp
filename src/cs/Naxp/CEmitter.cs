// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.Globalization;

namespace LogMu;

/// <summary>
/// Emits a compiled naxp as a C fragment, in C99.
/// </summary>
/// <remarks>
/// <para>
/// The fragment is two constants, nine public functions and their steppers, every name in
/// snake_case under the caller's prefix. It needs <c>stdbool.h</c>, <c>stddef.h</c>,
/// <c>stdint.h</c> and <c>string.h</c>, which the caller includes, and its first line says so.
/// Everything has internal linkage - the public functions <c>static inline</c>, the steppers
/// <c>static</c> - so the fragment can sit in a header or a source file alike and two
/// translation units holding it never collide.
/// </para>
/// <para>
/// Text comes in two ways, as a pointer and a length and as a NUL-terminated string, each
/// public function having a <c>_cstr</c> twin for the second. Decoding writes into the caller's
/// buffer and reports its length rather than returning anything, which is the only shape C
/// has, and mirrors the library's own C façade. The largest encoded value is a
/// <c>static const</c> rather than a macro, which stays out of the caller's namespace; the
/// longest length is an enum constant, because it sizes arrays and a <c>static const</c> cannot.
/// </para>
/// </remarks>
sealed class CEmitter : CFamilyEmitter
{
	/// <summary>The shared instance, which is stateless and serves every call concurrently.</summary>
	public static CEmitter Instance { get; } = new();

	#region The spelling of C
	/// <inheritdoc/>
	protected override string StepLinkage => "static";

	/// <inheritdoc/>
	protected override string TypePrefix => "";

	/// <summary>C never adopted a digit separator.</summary>
	protected override string? DigitSeparator => null;

	/// <inheritdoc/>
	protected override string TextParameters => "const char *text, size_t text_length";

	/// <inheritdoc/>
	protected override string Pointer(string type, string name) => $"{type} *{name}";

	/// <inheritdoc/>
	protected override string ByReference(string type, string name) => $"{type} *{name}";

	/// <inheritdoc/>
	protected override string Dereference(string name) => "*" + name;

	/// <inheritdoc/>
	protected override string Increment(string name) => $"(*{name})++";

	/// <inheritdoc/>
	protected override string AddressOf(string name) => "&" + name;

	/// <inheritdoc/>
	protected override string Cast(string type, string expression)
		=> IsIdentifier(expression) ? $"({type}){expression}" : $"({type})({expression})";

	/// <inheritdoc/>
	protected override void Comment(CodeWriter writer, string text) => writer.Line($"/* {text} */");
	#endregion
	#region The header
	/// <inheritdoc/>
	protected override void EmitHeader(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;

		this.Comment(writer, "Needs <stdbool.h>, <stddef.h>, <stdint.h> and <string.h>.");
		writer.Line();
		this.Comment(writer, "The largest encoded value this naxp produces, which is also how many it has.");
		writer.Line($"static const {fragment.ValueKeyword} {fragment.MaxEncodedValueName} = {fragment.MaxEncodedValueLiteral};");
		writer.Line();
		this.Comment(writer, "The length of the longest string this naxp can decode a value to.");
		writer.Line($"enum {{ {fragment.MaxLengthName} = {fragment.MaxLength.ToString(CultureInfo.InvariantCulture)} }};");
		writer.Line();
	}
	#endregion
	#region The public functions
	/// <inheritdoc/>
	protected override void EmitPublics(Fragment fragment)
	{
		this.EmitPattern(fragment);
		this.EmitAccepts(fragment);
		this.EmitEncode(fragment);
		this.EmitDecode(fragment);
		this.EmitCanonicalForm(fragment);
	}

	void EmitPattern(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;
		string pattern = fragment.Context.Compilation.Pattern;

		// A function rather than a constant, as the library's is, because an unused static array
		// is a warning in C where an unused static inline function is not.
		this.Comment(writer, "The naxp this code was generated from, NUL-terminated.");
		writer.Line($"static inline const char *{fragment.PatternName}(void)");
		writer.OpenBlock();
		writer.Line($"return {StringLiteral(pattern)};");
		writer.CloseBlock();
		writer.Line();
	}

	void EmitAccepts(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;
		string cstrName = fragment.Name("AcceptsCstr");

		this.Comment(writer, "Whether this naxp accepts text. A byte outside ASCII is never accepted.");
		writer.Line($"static inline bool {fragment.AcceptsName}(const char *text, size_t text_length)");
		writer.OpenBlock();
		writer.Line("int state = 0;");
		writer.Line();
		writer.Line("for (size_t i = 0; i < text_length; ++i)");
		writer.OpenBlock();
		writer.Line($"state = {fragment.AcceptStepName}(state, (unsigned char)text[i]);");
		writer.Line();
		writer.Line("if (state < 0) { return false; }");
		writer.CloseBlock();
		writer.Line();
		writer.Line($"return {fragment.IsAcceptingName}(state);");
		writer.CloseBlock();
		writer.Line();

		this.Comment(writer, "Whether this naxp accepts a NUL-terminated string.");
		writer.Line($"static inline bool {cstrName}(const char *text)");
		writer.OpenBlock();
		writer.Line($"return {fragment.AcceptsName}(text, strlen(text));");
		writer.CloseBlock();
		writer.Line();
	}

	void EmitEncode(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;
		string cstrName = fragment.Name("EncodeCstr");

		this.Comment(writer, $"The encoded value of text: from 1 to {fragment.MaxEncodedValueName}, or zero where the text is invalid.");
		writer.Line($"static inline {fragment.ValueKeyword} {fragment.EncodeName}(const char *text, size_t text_length)");
		writer.OpenBlock();

		if (fragment.Canonicalises)
		{
			writer.Line($"char canonical[{fragment.BufferSize}] = {{ 0 }};");
			writer.Line($"int length = {fragment.CanonicaliseName}(text, text_length, canonical);");
			writer.Line();
			writer.Line(fragment.ValueIsWidest
				? $"return length < 0 ? 0ULL : {fragment.RankName}(canonical, length);"
				: $"return length < 0 ? ({fragment.ValueKeyword})0 : ({fragment.ValueKeyword}){fragment.RankName}(canonical, length);");
		}
		else
		{
			writer.Line("int state = 0;");
			writer.Line("uint64_t total = 0ULL;");
			writer.Line();
			writer.Line("for (size_t i = 0; i < text_length; ++i)");
			writer.OpenBlock();
			writer.Line($"state = {fragment.EncodeStepName}(state, (unsigned char)text[i], &total);");
			writer.Line();
			writer.Line($"if (state < 0) {{ return {fragment.ValueZero}; }}");
			writer.CloseBlock();
			writer.Line();
			writer.Line(fragment.ValueIsWidest
				? $"return {fragment.IsAcceptingName}(state) ? total + 1ULL : 0ULL;"
				: $"return {fragment.IsAcceptingName}(state) ? ({fragment.ValueKeyword})(total + 1ULL) : ({fragment.ValueKeyword})0;");
		}

		writer.CloseBlock();
		writer.Line();

		this.Comment(writer, $"The encoded value of a NUL-terminated string: from 1 to {fragment.MaxEncodedValueName}, or zero where the string is invalid.");
		writer.Line($"static inline {fragment.ValueKeyword} {cstrName}(const char *text)");
		writer.OpenBlock();
		writer.Line($"return {fragment.EncodeName}(text, strlen(text));");
		writer.CloseBlock();
		writer.Line();
	}

	void EmitDecode(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;
		string cstrName = fragment.Name("DecodeCstr");

		writer.Line("/*");
		writer.Line("   Writes the string a value stands for, which is in canonical form, with no terminator.");
		writer.Line($"   {fragment.MaxLengthName} bytes always suffice. length, which may be NULL, receives how many");
		writer.Line("   were written, or zero where none were.");
		writer.Line();
		writer.Line("   Returns false where the value is not one this naxp produces, or the destination is too");
		writer.Line("   short, in which case nothing is written.");
		writer.Line("*/");
		writer.Line($"static inline bool {fragment.DecodeName}({fragment.ValueKeyword} value, char *destination, size_t capacity, size_t *length)");
		writer.OpenBlock();
		writer.Line($"char buffer[{fragment.BufferSize}] = {{ 0 }};");
		writer.Line("int written;");
		writer.Line();
		writer.Line($"if (value < {fragment.ValueOne} || value > {fragment.MaxEncodedValueName})");
		writer.OpenBlock();
		writer.Line("if (length != NULL) { *length = 0; }");
		writer.Line();
		writer.Line("return false;");
		writer.CloseBlock();
		writer.Line();
		writer.Line($"written = {fragment.DecodeCoreName}({fragment.DecodeCoreArgument}, buffer);");
		writer.Line();
		writer.Line("if ((size_t)written > capacity)");
		writer.OpenBlock();
		writer.Line("if (length != NULL) { *length = 0; }");
		writer.Line();
		writer.Line("return false;");
		writer.CloseBlock();
		writer.Line();
		writer.Line("memcpy(destination, buffer, (size_t)written);");
		writer.Line();
		writer.Line("if (length != NULL) { *length = (size_t)written; }");
		writer.Line();
		writer.Line("return true;");
		writer.CloseBlock();
		writer.Line();

		writer.Line("/*");
		writer.Line("   Writes the string a value stands for, which is in canonical form, as a NUL-terminated");
		writer.Line($"   string. {fragment.MaxLengthName} + 1 bytes always suffice.");
		writer.Line();
		writer.Line("   Returns false where the value is not one this naxp produces, or the destination is too");
		writer.Line("   short, in which case nothing is written.");
		writer.Line("*/");
		writer.Line($"static inline bool {cstrName}({fragment.ValueKeyword} value, char *destination, size_t capacity)");
		writer.OpenBlock();
		writer.Line("size_t length;");
		writer.Line();
		writer.Line($"if (capacity == 0 || !{fragment.DecodeName}(value, destination, capacity - 1, &length)) {{ return false; }}");
		writer.Line();
		writer.Line("destination[length] = '\\0';");
		writer.Line();
		writer.Line("return true;");
		writer.CloseBlock();
		writer.Line();
	}

	void EmitCanonicalForm(Fragment fragment)
	{
		CodeWriter writer = fragment.Writer;
		string cstrName = fragment.Name("CanonicalFormCstr");

		writer.Line("/*");
		writer.Line("   Writes the canonical form of text, which is the text decoding its encoded value gives back,");
		writer.Line($"   with no terminator. {fragment.MaxLengthName} bytes always suffice. length, which may be NULL,");
		writer.Line("   receives how many were written, or zero where none were.");
		writer.Line();
		writer.Line("   Returns false where the text is invalid, or the destination is too short, in which case");
		writer.Line("   nothing is written.");
		writer.Line("*/");
		writer.Line($"static inline bool {fragment.CanonicalFormName}(const char *text, size_t text_length, char *destination, size_t capacity, size_t *length)");
		writer.OpenBlock();
		writer.Line($"char buffer[{fragment.BufferSize}] = {{ 0 }};");
		writer.Line($"int written = {fragment.CanonicaliseName}(text, text_length, buffer);");
		writer.Line();
		writer.Line("if (written < 0 || (size_t)written > capacity)");
		writer.OpenBlock();
		writer.Line("if (length != NULL) { *length = 0; }");
		writer.Line();
		writer.Line("return false;");
		writer.CloseBlock();
		writer.Line();
		writer.Line("memcpy(destination, buffer, (size_t)written);");
		writer.Line();
		writer.Line("if (length != NULL) { *length = (size_t)written; }");
		writer.Line();
		writer.Line("return true;");
		writer.CloseBlock();
		writer.Line();

		writer.Line("/*");
		writer.Line("   Writes the canonical form of a NUL-terminated string as a NUL-terminated string.");
		writer.Line($"   {fragment.MaxLengthName} + 1 bytes always suffice.");
		writer.Line();
		writer.Line("   Returns false where the string is invalid, or the destination is too short, in which case");
		writer.Line("   nothing is written.");
		writer.Line("*/");
		writer.Line($"static inline bool {cstrName}(const char *text, char *destination, size_t capacity)");
		writer.OpenBlock();
		writer.Line("size_t length;");
		writer.Line();
		writer.Line($"if (capacity == 0 || !{fragment.CanonicalFormName}(text, strlen(text), destination, capacity - 1, &length)) {{ return false; }}");
		writer.Line();
		writer.Line("destination[length] = '\\0';");
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
			writer.Line($"if (!{fragment.AcceptsName}(text, text_length)) {{ return -1; }}");
			writer.Line();
			writer.Line("memcpy(canonical, text, text_length);");
			writer.Line();
			writer.Line("return (int)text_length;");
			writer.CloseBlock();
			writer.Line();

			return;
		}

		fragment.DeclareRegister("{ 0 }");
		writer.Line("int length = 0;");
		writer.Line("int state = 0;");
		writer.Line();
		writer.Line("for (size_t i = 0; i < text_length; ++i)");
		writer.OpenBlock();
		fragment.KeepCharacter("text[i]");
		writer.Line($"state = {fragment.CanonicalStepName}(state, (unsigned char)text[i], {fragment.StepArguments("&length")});");
		writer.Line();
		writer.Line("if (state < 0) { return -1; }");
		writer.CloseBlock();
		writer.Line();
		writer.Line($"return {fragment.FinishCanonicalName}(state, {fragment.FinishArguments()});");
		writer.CloseBlock();
		writer.Line();
	}
	#endregion
}
