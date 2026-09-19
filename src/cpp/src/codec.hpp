// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_CODEC_HPP
#define NAXP_CODEC_HPP

#include "state_map.hpp"

#include <cstdint>
#include <string>
#include <string_view>

/// The rank of a string within a machine's language, and the string at a rank.
namespace logmu::detail::codec
{
	/// The encoded value of a string, which is zero exactly when the machine's language does
	/// not hold it.
	std::uint64_t encode(const state_map& map, std::string_view text) noexcept;

	/// The string an encoded value stands for.
	///
	/// @param map The machine.
	/// @param value The value, from 1 to the machine's string count.
	/// @param text Where the string goes. Untouched where the value is out of range.
	/// @returns Whether the value is one the machine can produce.
	bool try_decode(const state_map& map, std::uint64_t value, std::string& text);
}

#endif
