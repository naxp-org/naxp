// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#ifndef NAXP_FAULT_HPP
#define NAXP_FAULT_HPP

#include "naxp_message.hpp"

#include <cstddef>
#include <string>
#include <string_view>
#include <utility>

namespace logmu::detail
{
	/// A fault: which message, where in the pattern, and what the message needs to say it.
	///
	/// The text is not held. A fault names a `naxp_message` and, where that message
	/// interpolates something, supplies one string; the words are looked up only when somebody
	/// asks for them. So nothing between the place a fault is found and the public surface
	/// handles prose.
	///
	/// An offset and a length of zero together mean the whole naxp, which is what most faults
	/// want and none of them have to say. Only the parser knows a position, and only the public
	/// surface knows how long the pattern is, so the substitution happens there. Every fault that
	/// does name a position uses a length of at least one, or it would read as this.
	struct fault
	{
		/// Constructs a fault.
		///
		/// @param message Which fault this is.
		/// @param argument What the message interpolates, or empty where it takes nothing.
		/// @param offset Where the fault starts, or zero for the naxp as a whole.
		/// @param length How much is at fault, or zero for the naxp as a whole.
		explicit fault(naxp_message message, std::string argument = {}, std::size_t offset = 0, std::size_t length = 0)
			: message(message)
			, argument(std::move(argument))
			, offset(offset)
			, length(length)
		{
		}

		/// Constructs a fault at a position, for the common case of no argument.
		fault(naxp_message message, std::size_t offset, std::size_t length)
			: fault(message, std::string(), offset, length)
		{
		}

		/// Whether this fault belongs to the naxp as a whole rather than to a place in it.
		bool is_whole_naxp() const noexcept
		{
			return this->offset == 0 && this->length == 0;
		}

		/// The stable identifier for this fault, such as `NAXP1002`.
		std::string_view code() const noexcept
		{
			return message_code(this->message);
		}

		/// What is wrong, and where practical what to write instead.
		std::string text() const
		{
			return format_message(this->message, this->argument);
		}

		/// The code, the span where there is one, and the text, on one line.
		std::string to_string() const
		{
			std::string result(this->code());

			if (!this->is_whole_naxp())
			{
				result += " at " + std::to_string(this->offset) + ".." + std::to_string(this->offset + this->length);
			}

			result += ": ";
			result += this->text();

			return result;
		}

		/// Which fault this is.
		naxp_message message;

		/// What the message interpolates, or empty.
		std::string argument;

		/// Where the fault starts. Zero with a zero length means the whole naxp.
		std::size_t offset;

		/// How much is at fault. Zero with a zero offset means the whole naxp.
		std::size_t length;
	};
}

#endif
