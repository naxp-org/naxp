// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "naxp_message.hpp"

#include "naxp_limits.hpp"

#include <array>
#include <cstddef>
#include <string>
#include <string_view>

namespace logmu::detail
{
	namespace
	{
		struct message_entry
		{
			std::string_view code;
			std::string_view format;
		};

		/// The digits of a limit, so that the table below can be built at compile time.
		template <int Value>
		constexpr auto digits_of()
		{
			static_assert(Value >= 0 && Value < 1'000'000);

			std::array<char, 7> text{};
			int length = 0;

			for (int rest = Value; rest > 0 || length == 0; rest /= 10)
			{
				text[static_cast<std::size_t>(length++)] = static_cast<char>('0' + (rest % 10));
			}

			for (int i = 0; i < length / 2; ++i)
			{
				const char swapped = text[static_cast<std::size_t>(i)];
				text[static_cast<std::size_t>(i)] = text[static_cast<std::size_t>(length - 1 - i)];
				text[static_cast<std::size_t>(length - 1 - i)] = swapped;
			}

			return text;
		}

		constexpr auto max_states_text = digits_of<limits::max_states>();
		constexpr auto max_canonical_states_text = digits_of<limits::max_canonical_states>();
		constexpr auto max_generated_length_text = digits_of<limits::max_string_length>();

		/// A message holding a limit, spliced at run time on first use rather than written
		/// out, so the figure is stated once.
		std::string with_limit(std::string_view before, const std::array<char, 7>& limit, std::string_view after)
		{
			return std::string(before) + std::string(limit.data()) + std::string(after);
		}

		/// One entry per member, in the same order, which the tests check against the enum. A
		/// message holding `{0}` takes an argument; `format_message` substitutes every
		/// occurrence, so the braces in messages such as `'A{2,5}'` need no escaping.
		constexpr std::array<message_entry, static_cast<std::size_t>(naxp_message::count)> entries{{
			{"NAXP1001", "An atom may take only one quantifier. To repeat something already quantified, group it first, as in '(A?){2}'."},
			{"NAXP1002", "The counts of an interval are separated by ',', not by a hyphen. Write 'A{2,5}'."},
			{"NAXP1003", "There is no unbounded interval, because a naxp must have a finite count of values. Write both counts, as in 'A{2,5}'."},
			{"NAXP1004", "This interval is not closed. Add a '}'."},
			{"NAXP1005", "An interval count must be a run of one to two digits."},
			{"NAXP1006", "The digits of an interval count cannot be separated by whitespace."},
			{"NAXP1007", "The first count of an interval cannot exceed the second."},
			{"NAXP1008", "An interval count may have at most two digits. The cap bounds the expansion an implementation must carry out before it can judge a naxp on any other ground."},
			{"NAXP1009", "This group is not closed. Add a ')'."},
			{"NAXP1010", "'!!' carries its own '?', so it cannot follow one. Write 'x!(x)' instead."},
			{"NAXP1011", "'!?' carries its own '?', so it cannot follow one. Write 'x!()' instead."},
			{"NAXP1012", "'!!' is one token, so whitespace may not split it."},
			{"NAXP1013", "'!?' is one token, so whitespace may not split it."},
			{"NAXP1014", "A '!' must be followed by its rendering. Write 'x!y', 'x!!' or 'x!?'; there is no bare 'x!'."},
			{"NAXP1015", "There should be no whitespace between '#' and '[' in a decimal range."},
			{"NAXP1016", "A '#' introduces a decimal range and must be followed by '['. To match a hash write '\\#'."},
			{"NAXP1017", "The bounds of a decimal range are separated by '-'. Write '#[0-105]'."},
			{"NAXP1018", "This decimal range is not closed. Add a ']'."},
			{"NAXP1019", "A decimal range bound must be a run of one to fifteen digits."},
			{"NAXP1020", "The digits of a decimal range bound cannot be separated by whitespace."},
			{"NAXP1021", "The lower bound of a decimal range may not have more digits than the upper bound."},
			{"NAXP1022", "Where the upper bound of a decimal range has more digits than the lower, it may not have leading zeros."},
			{"NAXP1023", "The lower bound of a decimal range may not exceed the upper bound."},
			{"NAXP1024", "A decimal range bound may have at most fifteen digits, which is what a 53 bit mantissa holds exactly."},
			{"NAXP1025", "This character set is not closed. Add a ']'."},
			{"NAXP1026", "A range in a character set runs between two single characters, so its upper bound cannot be a block escape."},
			{"NAXP1027", "A range in a character set must be written lowest first. Write '{0}'."},
			{"NAXP1028", "A character set must contain at least one character, so '[]' is not legal."},
			{"NAXP1029", "A '\\' cannot be followed by whitespace. To match a space write '\\s'."},
			{"NAXP1030", "A '\\' must be followed by an escape letter or a reserved character."},
			{"NAXP1031", "'\\{0}' is not an escape. A backslash may be followed by one of the letters 's', '9', 'A', 'a', 'X', 'x', 'C' and 'c', or by a reserved character."},
			{"NAXP1032", "{0} cannot appear in the pattern of a naxp, which may hold whitespace and the printable ASCII characters U+0021 to U+007E."},
			{"NAXP1033", "An element is required here, but the naxp ends."},
			{"NAXP1034", "An alternative must contain at least one element. To admit the empty string write '()'."},
			{"NAXP1035", "A '!' must follow its left operand, the subject it unifies. To match an exclamation mark write '\\!'."},
			{"NAXP1036", "The naxp ends before it is complete."},
			{"NAXP1037", "'{0}' is reserved and cannot appear here. To match it write '\\{0}'."},
			{"NAXP1038", "{0} cannot appear here."},
			{"NAXP1039", "A '!' may not nest, so neither the subject nor the rendering may contain another '!'."},
			{"NAXP1040", "The subject of a '!!' must generate exactly one string, since '!!' reproduces it."},
			{"NAXP1041", "The rendering of a '!' must generate exactly one string, or there would be no basis on which to choose between them."},
			{"NAXP1042", "This element cannot be deleted, because its subject does not generate the empty string. Make the subject optional."},
			{"NAXP1043", "The rendering '{0}' is not one of the strings its subject generates, so reconstituted text would not encode again."},
			{"NAXP1044", "Text unification must be single valued, but this naxp gives one string more than one canonical form, so it would have more than one value."},
			{"NAXP1045", "Text unification must be single valued, but '{0}' has more than one canonical form under this naxp, so it would have more than one value."},
			{"NAXP1046", "This naxp has more than 18 446 744 073 709 551 615 encoded values, which is more than W5 allows."},
			// The five budget messages carry a limit and are completed in format_message.
			{"NAXP1047", "This element generates a string longer than {limit} characters, which W6 does not allow."},
			{"NAXP1048", "This naxp needs more than {limit} states, which W6 does not allow."},
			{"NAXP1049", "This naxp needs more than {limit} states to canonicalise, which W6 does not allow."},
			{"NAXP1050", "Deciding whether unification is single valued for this naxp needs more than {limit} pair states, which W6 does not allow."},
			{"NAXP1051", "Deciding whether unification is single valued for this naxp needs an intermediate string longer than {limit} characters, which W6 does not allow."},
			{"NAXP1052", "'\\{0}' is a case fold and applies to a whole element, so it cannot appear inside a character set. Write it before the set instead."},
			{"NAXP1053", "The '!' or '?' must immediately follow the preceding zero without any separating whitespace."},
			{"NAXP1054", "Only a zero may be marked with '!' or '?'."},
			{"NAXP1055", "Only zeros at the front of a number may be marked with '!' or '?'. The lower bound must always end with a digit without a '!' or a '?'."},
			{"NAXP1056", "Only the lower bound of a decimal range may be marked with '!' or '?'."},
			{"NAXP1057", "There is no group for this ')' to close. To match a parenthesis write '\\)'."},
			{"NAXP1058", "A case fold cannot begin the rendering of a '!'. Write the rendering in the case you want, or case fold the whole text unification."},
			{"NAXP1059", "'{0}' is reserved. naxp has no unbounded repetition, so write an interval such as '{1,9}'. To match the character write '\\{0}'."},
			{"NAXP1060", "'.' is reserved. Write the character set you mean, such as '\\X'. To match a full stop write '\\.'."},
			{"NAXP1061", "'{0}' is reserved. A naxp matches the whole text, so there are no anchors. To match the character write '\\{0}'."},
			{"NAXP1062", "A fixed count of zero matches only the empty string. Write '()' instead."},
		}};

		/// The limit a budget message states, or null for every other message.
		const std::array<char, 7>* limit_of(naxp_message message) noexcept
		{
			switch (message)
			{
				case naxp_message::element_too_long: return &max_generated_length_text;
				case naxp_message::too_many_states: return &max_states_text;
				case naxp_message::too_many_canonical_states: return &max_canonical_states_text;
				case naxp_message::too_many_pair_states: return &max_states_text;
				case naxp_message::pair_output_abandoned: return &max_generated_length_text;
				default: return nullptr;
			}
		}

		std::string replace_all(std::string_view format, std::string_view placeholder, std::string_view replacement)
		{
			std::string result;
			std::size_t from = 0;

			for (std::size_t at = format.find(placeholder); at != std::string_view::npos; at = format.find(placeholder, from))
			{
				result.append(format, from, at - from);
				result.append(replacement);
				from = at + placeholder.size();
			}

			result.append(format, from, std::string_view::npos);

			return result;
		}
	}

	std::string_view message_code(naxp_message message) noexcept
	{
		return entries[static_cast<std::size_t>(message)].code;
	}

	std::string format_message(naxp_message message, std::string_view argument)
	{
		const std::string_view format = entries[static_cast<std::size_t>(message)].format;

		if (const std::array<char, 7>* limit = limit_of(message); limit != nullptr)
		{
			const std::size_t at = format.find("{limit}");

			return with_limit(format.substr(0, at), *limit, format.substr(at + 7));
		}

		return argument.empty() ? std::string(format) : replace_all(format, "{0}", argument);
	}
}
