// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

using System.IO;
using System.Text;

namespace LogMu;

/// <summary>
/// Writes source text a line at a time, indenting each line to the current block depth.
/// </summary>
/// <remarks>
/// Written for the language emitters, which all build text the same way. Block and indent
/// syntax are options so one writer serves both brace languages and indentation languages: the
/// defaults on <see cref="Emitter"/> suit C# and its relatives, while a Python emitter would
/// pass four spaces and no block lines, ending each block's header with a colon before
/// <see cref="OpenBlock"/>. The derived classes supply the target: <see cref="CodeWriterSB"/>
/// appends to a <see cref="StringBuilder"/> and <see cref="CodeWriterTW"/> writes to a
/// <see cref="TextWriter"/>, so the emitters serve both through identical code.
/// </remarks>
abstract class CodeWriter
{
	readonly string initialIndent;
	readonly string indent;
	readonly string? blockOpen;
	readonly string? blockClose;
	int depth;

	/// <summary>
	/// Constructs a writer for one language's block syntax.
	/// </summary>
	/// <param name="initialIndent">
	/// What every non-empty line starts with, ahead of the depth indentation, so a fragment can
	/// sit inside an already-indented wrapper.
	/// </param>
	/// <param name="indent">What one level of indentation is written as.</param>
	/// <param name="blockOpen">
	/// The line <see cref="OpenBlock"/> writes before indenting, or <see langword="null"/> for
	/// none, where a language opens a block by indentation alone.
	/// </param>
	/// <param name="blockClose">
	/// The line <see cref="CloseBlock"/> writes after outdenting, or <see langword="null"/> for
	/// none.
	/// </param>
	protected CodeWriter(string initialIndent, string indent, string? blockOpen, string? blockClose)
	{
		this.initialIndent = initialIndent;
		this.indent = indent;
		this.blockOpen = blockOpen;
		this.blockClose = blockClose;
	}

	/// <summary>Writes an empty line, with no indentation.</summary>
	public void Line() => this.EndLine();

	/// <summary>Writes one line at the current indentation.</summary>
	/// <param name="text">The line, without a terminator.</param>
	public void Line(string text)
	{
		this.Append(this.initialIndent);

		for (int i = 0; i < this.depth; ++i) { this.Append(this.indent); }

		this.Append(text);
		this.EndLine();
	}

	/// <summary>Opens a block: writes the block opening line, where the language has one, and indents.</summary>
	public void OpenBlock()
	{
		if (this.blockOpen is not null) { this.Line(this.blockOpen); }

		++this.depth;
	}

	/// <summary>Closes a block: outdents and writes the block closing line, where the language has one.</summary>
	public void CloseBlock()
	{
		--this.depth;

		if (this.blockClose is not null) { this.Line(this.blockClose); }
	}

	/// <summary>Indents by one level, for constructs that are not blocks.</summary>
	public void Indent() => ++this.depth;

	/// <summary>Undoes one <see cref="Indent"/>.</summary>
	public void Outdent() => --this.depth;

	/// <summary>Writes text within the current line.</summary>
	protected abstract void Append(string text);

	/// <summary>Terminates the current line.</summary>
	protected abstract void EndLine();
}

/// <summary>A <see cref="CodeWriter"/> that appends to a <see cref="StringBuilder"/>.</summary>
sealed class CodeWriterSB : CodeWriter
{
	readonly StringBuilder builder;

	public CodeWriterSB(StringBuilder builder, string initialIndent, string indent, string? blockOpen, string? blockClose)
		: base(initialIndent, indent, blockOpen, blockClose)
	{
		this.builder = builder;
	}

	protected override void Append(string text) => this.builder.Append(text);

	protected override void EndLine() => this.builder.AppendLine();
}

/// <summary>A <see cref="CodeWriter"/> that writes to a <see cref="TextWriter"/>.</summary>
sealed class CodeWriterTW : CodeWriter
{
	readonly TextWriter writer;

	public CodeWriterTW(TextWriter writer, string initialIndent, string indent, string? blockOpen, string? blockClose)
		: base(initialIndent, indent, blockOpen, blockClose)
	{
		this.writer = writer;
	}

	protected override void Append(string text) => this.writer.Write(text);

	protected override void EndLine() => this.writer.WriteLine();
}
