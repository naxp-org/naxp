// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "codec.hpp"

#include "state_map.hpp"

#include <cstdint>
#include <string>
#include <string_view>

namespace logmu::detail::codec
{
	std::uint64_t encode(const state_map& map, std::string_view text) noexcept
	{
		const state* current = map.start;
		std::uint64_t total = 0;

		for (const char c : text)
		{
			std::uint64_t skipped = 0;
			const state* next = nullptr;

			for (const transition& arc : current->transitions)
			{
				const std::uint64_t count = arc.next->string_count;

				if (arc.set.contains(c))
				{
					total += skipped + (count * static_cast<std::uint64_t>(arc.set.index_of(c)));
					next = arc.next;
					break;
				}

				// An empty set is the end of text transition, which stands for one value.
				skipped += count * (arc.set.is_empty() ? 1 : static_cast<std::uint64_t>(arc.set.count()));
			}

			if (next == nullptr)
			{
				return 0;
			}

			current = next;
		}

		return current->accepts_end_of_text() ? total + 1 : 0;
	}

	int decode(const state_map& map, std::uint64_t value, char* destination)
	{
		// Zero is reserved for invalid text, so it decodes to nothing.
		if (value == 0 || value > map.string_count())
		{
			return -1;
		}

		const state* current = map.start;
		std::uint64_t remaining = value;
		int length = 0;

		while (!current->is_terminal())
		{
			const state* next = nullptr;

			for (const transition& arc : current->transitions)
			{
				if (arc.set.is_empty())
				{
					if (remaining == 1)
					{
						return length;
					}

					remaining -= 1;
					continue;
				}

				const std::uint64_t per_character = arc.next->string_count;
				const std::uint64_t block = static_cast<std::uint64_t>(arc.set.count()) * per_character;

				if (remaining <= block)
				{
					destination[length++] = arc.set.character_at(static_cast<int>((remaining - 1) / per_character));
					remaining = ((remaining - 1) % per_character) + 1;
					next = arc.next;
					break;
				}

				remaining -= block;
			}

			// The value was checked against the count of the start state, and each step leaves
			// it within the count of the state it moves to, so this cannot be reached.
			if (next == nullptr)
			{
				return -1;
			}

			current = next;
		}

		return length;
	}
}
