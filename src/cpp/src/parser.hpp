// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_PARSER_HPP
#define NAXP_PARSER_HPP

#include "ast.hpp"
#include "fault.hpp"

#include <optional>
#include <string_view>

namespace logmu::detail
{
	/// Parses a naxp, checking syntax and W4.
	///
	/// The parser reports W4 as well as syntax, because the constraints on interval counts and
	/// decimal range bounds are decided at the point the tokens are read and nowhere else. W1
	/// and W2 need the finished tree and live in the well-formedness check; W3 and W5 need the
	/// state map.
	///
	/// It carries error productions for syntax that is plausibly wrong rather than merely
	/// invalid, so that the message names the mistake: a comma in an interval, an unbounded
	/// interval, a bare `x!`, the hex escape naxp does not have, and whitespace splitting a
	/// token.
	///
	/// The pattern is bytes. A byte outside whitespace and U+0021 to U+007E is a fault, named by
	/// the code point it starts where the bytes are well-formed UTF-8 and by the byte where they
	/// are not; offsets and lengths are in bytes throughout.
	///
	/// @param pattern The pattern of the naxp.
	/// @param tree The tree, or null if the pattern was invalid.
	/// @param error The fault, or nothing if the pattern parsed.
	/// @returns Whether the pattern parsed.
	bool try_parse(std::string_view pattern, ast_ptr& tree, std::optional<fault>& error);
}

#endif
