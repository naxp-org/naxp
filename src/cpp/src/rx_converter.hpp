// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_RX_CONVERTER_HPP
#define NAXP_RX_CONVERTER_HPP

#include "ast.hpp"
#include "rx.hpp"

/// Turns a parsed naxp into the algebra the state map is built over.
///
/// This is step 1 of the specification's procedure. The canonicalisation table there has three
/// rows, `x!y` to `y`, `x!!` to `x` and `x!?` to `()`, but the parser already expanded the two
/// abbreviations into the general form, so all three collapse to taking the rendering.
///
/// Decimal ranges are expanded here, because a bound of fifteen digits expands to about fifteen
/// alternatives and costs nothing. Intervals are not, because their counts multiply when nested.
namespace logmu::detail::rx_converter
{
	/// @param node The tree.
	/// @param factory The factory to build with.
	/// @param is_canonical Which of a naxp's two languages the expression is for: the canonical
	///     language C, which is L with each unified element replaced by its rendering and which
	///     the encoding ranks over, or the accepted language L, the strings the naxp matches.
	const rx* convert(const ast& node, rx_factory& factory, bool is_canonical);
}

#endif
