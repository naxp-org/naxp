// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "parser.hpp"

#include "ascii_char_set.hpp"
#include "ast.hpp"
#include "fault.hpp"
#include "folding.hpp"
#include "naxp_message.hpp"
#include "padding.hpp"

#include <array>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace logmu::detail
{
	namespace
	{
		/// Returned by `peek` past the end of the pattern. Safe as a sentinel because
		/// `try_check_pattern_characters` has already ruled out any pattern containing a
		/// character outside whitespace and U+0021 to U+007E.
		constexpr char end_of_text = '\0';

		/// The most digits an interval count may have.
		constexpr int max_interval_count_digits = 2;

		/// The most digits a decimal range bound may have.
		constexpr int max_bound_digits = 15;

		bool is_whitespace(char c) noexcept
		{
			return c == ' ' || c == '\t' || c == '\r' || c == '\n';
		}

		bool is_digit(char c) noexcept
		{
			return c >= '0' && c <= '9';
		}

		bool is_reserved_char(char c) noexcept
		{
			switch (c)
			{
				case '!': case '#': case '(': case ')': case ',': case '-': case '?':
				case '[': case '\\': case ']': case '{': case '|': case '}':
				// The five regex metacharacters naxp reserves without giving them a meaning,
				// so that a regex habit gets a message naming what naxp offers instead rather
				// than a pattern that silently means something else.
				case '*': case '+': case '.': case '^': case '$':
					return true;

				default:
					return false;
			}
		}

		bool is_bare_char(char c) noexcept
		{
			return c >= '\x21' && c <= '\x7E' && !is_reserved_char(c);
		}

		bool is_start_of_element(char c) noexcept
		{
			return c == '\\' || c == '[' || c == '#' || c == '(' || is_bare_char(c);
		}

		/// A character as a message names it.
		///
		/// The quotes belong here rather than in the message, so that 'a space' is not quoted as
		/// though it were a character. A message using this must therefore not quote its
		/// argument.
		std::string describe_char(char c)
		{
			switch (c)
			{
				case ' ': return "a space";
				case '\t': return "a tab";
				case '\r': return "a carriage return";
				case '\n': return "a line feed";
				default: return std::string("'") + c + "'";
			}
		}

		/// A character as it is written inside a character set, for a message telling somebody
		/// what to write.
		///
		/// Naming a character and writing one are different jobs, which is why this is not
		/// `describe_char`: that says 'a space', and a space is the one thing nobody can type.
		///
		/// Only three kinds of character reach here. A space arrives as `\s`, since bare
		/// whitespace inside a set is skipped and a backslash before whitespace is invalid; a
		/// reserved character arrives escaped and has to go back escaped; and everything else is
		/// bare and stands for itself.
		std::string pattern_for_char(char c)
		{
			if (c == ' ')
			{
				return "\\s";
			}

			if (is_reserved_char(c))
			{
				return std::string("\\") + c;
			}

			return std::string(1, c);
		}

		std::string hex_digits(std::uint32_t value, int at_least)
		{
			static constexpr char digits[] = "0123456789ABCDEF";
			std::string text;

			for (; value != 0 || static_cast<int>(text.size()) < at_least; value >>= 4)
			{
				text.insert(text.begin(), digits[value & 0xF]);
			}

			return text;
		}

		/// Names a byte the pattern may not hold, which is by definition one that cannot be
		/// shown.
		///
		/// Where the byte starts a well-formed UTF-8 sequence the code point is named and the
		/// whole sequence is the span, so that the fault says `U+00A3` for a pound sign rather
		/// than naming its first byte. Otherwise the byte is named as itself.
		///
		/// @param text The pattern.
		/// @param at The offending byte.
		/// @param length How many bytes the fault spans, which is the sequence length or one.
		std::string code_point_as_text(std::string_view text, std::size_t at, std::size_t& length)
		{
			const auto byte = [text](std::size_t i) { return static_cast<unsigned char>(text[i]); };
			const unsigned char lead = byte(at);
			std::size_t sequence = 0;
			std::uint32_t code_point = 0;

			if (lead >= 0xC2 && lead <= 0xDF)
			{
				sequence = 2;
				code_point = lead & 0x1F;
			}
			else if (lead >= 0xE0 && lead <= 0xEF)
			{
				sequence = 3;
				code_point = lead & 0x0F;
			}
			else if (lead >= 0xF0 && lead <= 0xF4)
			{
				sequence = 4;
				code_point = lead & 0x07;
			}

			bool well_formed = sequence != 0 && at + sequence <= text.size();

			for (std::size_t i = 1; well_formed && i < sequence; ++i)
			{
				const unsigned char continuation = byte(at + i);

				if ((continuation & 0xC0) != 0x80)
				{
					well_formed = false;
				}
				else
				{
					code_point = (code_point << 6) | (continuation & 0x3F);
				}
			}

			if (well_formed)
			{
				// Overlong forms, surrogates and values past U+10FFFF are not well-formed either.
				const std::uint32_t lowest = sequence == 2 ? 0x80 : sequence == 3 ? 0x800 : 0x10000;

				well_formed = code_point >= lowest && code_point <= 0x10FFFF && (code_point < 0xD800 || code_point > 0xDFFF);
			}

			if (!well_formed)
			{
				length = 1;

				return "byte 0x" + hex_digits(lead, 2);
			}

			length = sequence;

			return "U+" + hex_digits(code_point, 4);
		}

		/// A recursive descent parser over one pattern.
		///
		/// Every production returns the node it built, or null having set `error`. The
		/// helpers that build nothing return whether they succeeded, on the same terms.
		class parser
		{
		public:
			explicit parser(std::string_view text) noexcept
				: text(text)
			{
			}

			bool try_parse_naxp(ast_ptr& tree, std::optional<fault>& fault_found)
			{
				tree = nullptr;

				if (!this->try_check_pattern_characters())
				{
					fault_found = std::move(this->error);

					return false;
				}

				this->skip_whitespace();

				ast_ptr expression = this->try_parse_expr();

				if (expression == nullptr)
				{
					fault_found = std::move(this->error);

					return false;
				}

				this->skip_whitespace();

				if (this->pos != this->text.size())
				{
					// A ')' that closes a group is consumed by try_parse_base, so one still
					// standing here closes nothing. Calling it a reserved character and offering
					// the escape is true and is almost never what was meant.
					fault_found = this->peek() == ')'
						? fault(naxp_message::group_not_opened, this->pos, 1)
						: this->unexpected_character();

					return false;
				}

				tree = std::move(expression);
				fault_found.reset();

				return true;
			}

		private:
			// Productions

			/// `expr ::= seq ( "|" seq )*`
			ast_ptr try_parse_expr()
			{
				const std::size_t start = this->pos;

				ast_ptr first = this->try_parse_seq();

				if (first == nullptr)
				{
					return nullptr;
				}

				std::vector<ast_ptr> alternatives;

				this->skip_whitespace();

				while (this->peek() == '|')
				{
					this->advance();
					this->skip_whitespace();

					ast_ptr next = this->try_parse_seq();

					if (next == nullptr)
					{
						return nullptr;
					}

					if (alternatives.empty())
					{
						alternatives.push_back(first);
					}

					alternatives.push_back(std::move(next));

					this->skip_whitespace();
				}

				return alternatives.empty()
					? first
					: std::make_shared<ast_alternation>(std::move(alternatives), start);
			}

			/// `seq ::= element+ | element* case_fold expr`
			///
			/// A case fold is the loosest operator: it runs from where it is written to the end
			/// of the enclosing group, across any `|`, the way a regex flag does. So a fold ends
			/// the sequence it is written in, taking the rest of the expression as its operand.
			/// Where one fold is written inside another the outer governs, so a run of folds is
			/// the first of them and the rest are consumed and dropped.
			ast_ptr try_parse_seq()
			{
				const std::size_t start = this->pos;

				ast_ptr first;
				std::vector<ast_ptr> elements;

				while (true)
				{
					this->skip_whitespace();

					const std::size_t fold_start = this->pos;
					bool has_fold = false;
					bool fold_to_upper = false;

					if (!this->try_parse_fold(has_fold, fold_to_upper))
					{
						return nullptr;
					}

					if (has_fold)
					{
						ast_ptr tail = this->try_parse_expr();

						if (tail == nullptr)
						{
							return nullptr;
						}

						ast_ptr rest = folding::apply(tail, fold_to_upper);
						rest->pattern_offset = fold_start;

						if (first == nullptr)
						{
							return rest;
						}

						if (elements.empty())
						{
							elements.push_back(first);
						}

						elements.push_back(std::move(rest));
						break;
					}

					if (!is_start_of_element(this->peek()))
					{
						break;
					}

					ast_ptr element = this->try_parse_element();

					if (element == nullptr)
					{
						return nullptr;
					}

					if (first == nullptr)
					{
						first = std::move(element);
					}
					else
					{
						if (elements.empty())
						{
							elements.push_back(first);
						}

						elements.push_back(std::move(element));
					}
				}

				if (first == nullptr)
				{
					this->error = this->no_element_here();

					return nullptr;
				}

				return elements.empty()
					? first
					: std::make_shared<ast_sequence>(std::move(elements), start);
			}

			/// `element ::= operand quantifier? text_unification?`
			ast_ptr try_parse_element()
			{
				const std::size_t start = this->pos;

				ast_ptr node = this->try_parse_base();

				if (node == nullptr)
				{
					return nullptr;
				}

				bool has_quantifier = false;
				bool has_optional = false;

				this->skip_whitespace();

				if (this->peek() == '?')
				{
					this->advance();
					node = std::make_shared<ast_optional>(std::move(node), start);
					has_quantifier = true;
					has_optional = true;
				}
				else if (this->peek() == '{')
				{
					node = this->try_parse_interval(std::move(node), start);

					if (node == nullptr)
					{
						return nullptr;
					}

					has_quantifier = true;
				}

				this->skip_whitespace();

				if (has_quantifier && (this->peek() == '?' || this->peek() == '{'))
				{
					this->error = fault(naxp_message::quantifier_repeated, this->pos, 1);

					return nullptr;
				}

				if (this->peek() == '!')
				{
					node = this->try_parse_unified(std::move(node), start, has_optional);

					if (node == nullptr)
					{
						return nullptr;
					}
				}

				return node;
			}

			/// `case_fold ::= "\C" | "\c"`, as many as are written.
			///
			/// The fold is consumed here and expanded by `folding` once the expression it binds
			/// to has been parsed, so no later stage sees one. A run of folds is the first of
			/// them; see `try_parse_seq`.
			///
			/// @param has_fold Whether a fold was written.
			/// @param to_upper Whether upper case is canonical, which is `\C`.
			/// @returns Whether the fold, if there was one, is well placed.
			bool try_parse_fold(bool& has_fold, bool& to_upper)
			{
				has_fold = false;
				to_upper = false;

				char letter = end_of_text;

				// Where one fold is written directly on another the outer governs, as it does
				// over a fold anywhere within its extent, so a run of folds is the first of them
				// and the rest are consumed and dropped.
				while (this->try_peek_fold(letter))
				{
					this->advance();
					this->advance();

					if (!has_fold)
					{
						has_fold = true;
						to_upper = letter == 'C';
					}

					this->skip_whitespace();
				}

				return true;
			}

			/// Whether a fold starts here, without consuming it.
			///
			/// @param letter The fold letter, `C` or `c`.
			bool try_peek_fold(char& letter) const noexcept
			{
				letter = end_of_text;

				if (this->peek() != '\\')
				{
					return false;
				}

				const char next = this->pos + 1 < this->text.size() ? this->text[this->pos + 1] : end_of_text;

				if (next != 'C' && next != 'c')
				{
					return false;
				}

				letter = next;

				return true;
			}

			/// `base ::= char_set | digits_range | "(" expr? ")"`
			ast_ptr try_parse_base()
			{
				const std::size_t start = this->pos;
				const char c = this->peek();

				if (c == '(')
				{
					this->advance();
					this->skip_whitespace();

					if (this->peek() == ')')
					{
						this->advance();

						return std::make_shared<ast_empty>(start);
					}

					ast_ptr inner = this->try_parse_expr();

					if (inner == nullptr)
					{
						return nullptr;
					}

					this->skip_whitespace();

					if (this->peek() != ')')
					{
						this->error = fault(naxp_message::group_not_closed, start, 1);

						return nullptr;
					}

					this->advance();
					inner->pattern_offset = start;

					return inner;
				}

				if (c == '#')
				{
					return this->try_parse_decimal_range();
				}

				if (c == '[')
				{
					ascii_char_set bracket_set;

					if (!this->try_parse_bracket_set(bracket_set))
					{
						return nullptr;
					}

					return std::make_shared<ast_chars>(bracket_set, start);
				}

				ascii_char_set atom_set;
				char literal_char = end_of_text;
				bool is_block_escape = false;

				if (!this->try_parse_char_atom(atom_set, literal_char, is_block_escape))
				{
					return nullptr;
				}

				return std::make_shared<ast_chars>(atom_set, start);
			}

			/// `unified ::= "!" element | "!!" | "!?"`
			///
			/// @param subject The element the `!` binds to.
			/// @param start The offset at which that element starts.
			/// @param subject_is_optional Whether the subject already carries a `?`.
			ast_ptr try_parse_unified(ast_ptr subject, std::size_t start, bool subject_is_optional)
			{
				const std::size_t bang_offset = this->pos;
				this->advance();

				// No whitespace is skipped here: '!!' and '!?' are single tokens.
				const char next = this->peek();

				if (next == '!' || next == '?')
				{
					this->advance();

					if (subject_is_optional)
					{
						this->error = fault(
							next == '!' ? naxp_message::reproduced_after_optional : naxp_message::dropped_after_optional,
							bang_offset,
							2);

						return nullptr;
					}

					// The expansions are structural: x!! is x?!(x), and x!? is x?!().
					ast_ptr optional_subject = std::make_shared<ast_optional>(subject, start);
					ast_ptr rendering = next == '!'
						? subject
						: ast_ptr(std::make_shared<ast_empty>(this->pos));
					const unified_form form = next == '!' ? unified_form::reproduced : unified_form::dropped;

					return std::make_shared<ast_unified>(std::move(optional_subject), std::move(rendering), form, start);
				}

				if (is_whitespace(next))
				{
					const std::size_t whitespace_offset = this->pos;
					std::size_t lookahead = this->pos;

					while (lookahead < this->text.size() && is_whitespace(this->text[lookahead]))
					{
						++lookahead;
					}

					const char after_whitespace = lookahead < this->text.size() ? this->text[lookahead] : end_of_text;

					if (after_whitespace == '!' || after_whitespace == '?')
					{
						this->error = fault(
							after_whitespace == '!' ? naxp_message::reproduced_split : naxp_message::dropped_split,
							whitespace_offset,
							lookahead - whitespace_offset);

						return nullptr;
					}
				}

				this->skip_whitespace();

				// A fold would run to the end of the group, and a fold with anything to do
				// expands to a '!', which W2 refuses inside a rendering; so there is nothing a
				// fold could usefully mean here, and it is refused as syntax with a message that
				// says what to write instead.
				{
					char letter = end_of_text;

					if (this->try_peek_fold(letter))
					{
						this->error = fault(naxp_message::fold_begins_rendering, this->pos, 2);

						return nullptr;
					}
				}

				if (!is_start_of_element(this->peek()))
				{
					this->error = fault(naxp_message::rendering_missing, bang_offset, 1);

					return nullptr;
				}

				ast_ptr written_rendering = this->try_parse_element();

				if (written_rendering == nullptr)
				{
					return nullptr;
				}

				return std::make_shared<ast_unified>(std::move(subject), std::move(written_rendering), unified_form::written, start);
			}

			/// `interval ::= "{" digits ( "," digits )? "}"`
			ast_ptr try_parse_interval(ast_ptr child, std::size_t start)
			{
				const std::size_t brace_offset = this->pos;
				this->advance();
				this->skip_whitespace();

				int min_count = 0;

				if (!this->try_parse_interval_count(min_count))
				{
					return nullptr;
				}

				this->skip_whitespace();

				int max_count = min_count;

				if (this->peek() == '-')
				{
					this->error = fault(naxp_message::interval_hyphen, this->pos, 1);

					return nullptr;
				}

				if (this->peek() == ',')
				{
					this->advance();
					this->skip_whitespace();

					if (!is_digit(this->peek()))
					{
						this->error = fault(naxp_message::interval_unbounded, this->pos, 1);

						return nullptr;
					}

					if (!this->try_parse_interval_count(max_count))
					{
						return nullptr;
					}

					this->skip_whitespace();
				}

				if (this->peek() != '}')
				{
					this->error = fault(naxp_message::interval_not_closed, brace_offset, 1);

					return nullptr;
				}

				this->advance();

				if (min_count > max_count)
				{
					this->error = fault(naxp_message::interval_counts_out_of_order, brace_offset, this->pos - brace_offset);

					return nullptr;
				}

				if (max_count == 0)
				{
					this->error = fault(naxp_message::interval_count_zero, brace_offset, this->pos - brace_offset);

					return nullptr;
				}

				return std::make_shared<ast_interval>(std::move(child), min_count, max_count, start);
			}

			bool try_parse_interval_count(int& count)
			{
				count = 0;
				const std::size_t start = this->pos;

				if (!is_digit(this->peek()))
				{
					this->error = fault(naxp_message::interval_count_not_digits, this->pos, 1);

					return false;
				}

				int digit_count = 0;

				while (is_digit(this->peek()))
				{
					if (digit_count < max_interval_count_digits)
					{
						count = (count * 10) + (this->peek() - '0');
					}

					++digit_count;
					this->advance();
				}

				if (!this->try_check_digit_run_not_split(naxp_message::interval_count_split))
				{
					return false;
				}

				if (digit_count > max_interval_count_digits)
				{
					this->error = fault(naxp_message::interval_count_too_long, start, this->pos - start);

					return false;
				}

				return true;
			}

			/// What reading one bound of a decimal range found.
			struct bound
			{
				/// The value.
				std::uint64_t value = 0;

				/// The digits it was written with.
				int digit_count = 0;

				/// Whether it was written with a leading zero.
				bool has_leading_zero = false;

				/// Whether any digit carries a mark.
				bool has_marks = false;

				/// One entry per digit, nul where that digit carries no mark.
				std::array<char, max_bound_digits> marks{};

				/// Where each digit sits in the pattern, so a fault on one can name it rather
				/// than the whole range. A digit and its mark are one token, so these are not
				/// evenly spaced.
				std::array<std::size_t, max_bound_digits> digit_offsets{};
			};

			/// `digits_range ::= "#[" low_bound "-" digits "]"`
			ast_ptr try_parse_decimal_range()
			{
				const std::size_t start = this->pos;
				this->advance();

				// '#[' is one token, so no whitespace is skipped between the two characters.
				if (this->peek() != '[')
				{
					this->error = is_whitespace(this->peek())
						? fault(naxp_message::hash_split_from_bracket, this->pos, 1)
						: fault(naxp_message::hash_without_bracket, start, 1);

					return nullptr;
				}

				this->advance();
				this->skip_whitespace();

				// Leading zeros in the lower bound are the point of it: they set a minimum
				// width, and a mark on one says that the width it sets is optional on input.
				bound low;

				if (!this->try_parse_bound(true, low))
				{
					return nullptr;
				}

				this->skip_whitespace();

				if (this->peek() != '-')
				{
					this->error = fault(naxp_message::decimal_range_bounds_separator, this->pos, 1);

					return nullptr;
				}

				this->advance();
				this->skip_whitespace();

				bound high;

				if (!this->try_parse_bound(false, high))
				{
					return nullptr;
				}

				this->skip_whitespace();

				if (this->peek() != ']')
				{
					this->error = fault(naxp_message::decimal_range_not_closed, start, 1);

					return nullptr;
				}

				this->advance();

				if (low.digit_count > high.digit_count)
				{
					this->error = fault(naxp_message::lower_bound_wider_than_upper, start, this->pos - start);

					return nullptr;
				}

				if (high.digit_count > low.digit_count && high.has_leading_zero)
				{
					this->error = fault(naxp_message::upper_bound_leading_zeros, start, this->pos - start);

					return nullptr;
				}

				if (low.value > high.value)
				{
					this->error = fault(naxp_message::lower_bound_exceeds_upper, start, this->pos - start);

					return nullptr;
				}

				// A mark stands for a padding zero, so it may sit only in front of the digits of
				// the value. Which positions those are is known once the bound has been read.
				if (low.has_marks)
				{
					const int at = marked_inside_value(low);

					// The zero and the mark after it, which is the thing that is wrong. Naming
					// the whole range would leave 'this zero' pointing at nothing.
					if (at >= 0)
					{
						this->error = fault(
							naxp_message::decimal_range_mark_not_padding,
							low.digit_offsets[static_cast<std::size_t>(at)],
							2);

						return nullptr;
					}
				}

				return low.has_marks
					? padding::expand(low.value, low.digit_count, high.value, high.digit_count, low.marks, start)
					: std::make_shared<ast_decimal_range>(low.value, low.digit_count, high.value, high.digit_count, start);
			}

			/// Whether any mark falls on a digit of the value rather than on the padding in
			/// front of it.
			///
			/// @returns The position of the first mark that is out of place, or -1 where none is.
			static int marked_inside_value(const bound& low) noexcept
			{
				int significant = 1;

				for (std::uint64_t rest = low.value; rest >= 10; rest /= 10)
				{
					++significant;
				}

				for (int position = low.digit_count - significant; position < low.digit_count; ++position)
				{
					if (position >= 0 && low.marks[static_cast<std::size_t>(position)] != '\0')
					{
						return position;
					}
				}

				return -1;
			}

			/// Reads one bound of a decimal range.
			///
			/// @param allow_marks Whether a padding mark may follow a digit, which is so for the
			///     lower bound alone.
			/// @param result What was read.
			/// @returns Whether the bound parsed.
			bool try_parse_bound(bool allow_marks, bound& result)
			{
				result = bound();

				const std::size_t start = this->pos;

				if (!is_digit(this->peek()))
				{
					this->error = fault(naxp_message::decimal_range_bound_not_digits, this->pos, 1);

					return false;
				}

				const char first_digit = this->peek();

				while (is_digit(this->peek()))
				{
					const char digit = this->peek();

					if (result.digit_count < max_bound_digits)
					{
						result.digit_offsets[static_cast<std::size_t>(result.digit_count)] = this->pos;
						result.value = (result.value * 10) + static_cast<std::uint64_t>(digit - '0');
					}

					++result.digit_count;
					this->advance();

					// A mark is part of the digit's token, so nothing is skipped between the two.
					if (this->peek() != '!' && this->peek() != '?')
					{
						continue;
					}

					if (!allow_marks)
					{
						this->error = fault(naxp_message::decimal_range_mark_on_upper_bound, this->pos, 1);

						return false;
					}

					if (digit != '0')
					{
						this->error = fault(naxp_message::decimal_range_mark_on_non_zero, this->pos, 1);

						return false;
					}

					if (result.digit_count <= max_bound_digits)
					{
						result.has_marks = true;
						result.marks[static_cast<std::size_t>(result.digit_count - 1)] = this->peek();
					}

					this->advance();
				}

				if (allow_marks && !this->try_check_mark_not_split())
				{
					return false;
				}

				if (!this->try_check_digit_run_not_split(naxp_message::decimal_range_bound_split))
				{
					return false;
				}

				if (result.digit_count > max_bound_digits)
				{
					this->error = fault(naxp_message::decimal_range_bound_too_long, start, this->pos - start);

					return false;
				}

				result.has_leading_zero = result.digit_count > 1 && first_digit == '0';

				return true;
			}

			/// `char_set ::= ... | "[" set_item+ "]"`
			bool try_parse_bracket_set(ascii_char_set& set)
			{
				set = ascii_char_set();

				const std::size_t start = this->pos;
				this->advance();

				ascii_char_set result;
				int item_count = 0;

				while (true)
				{
					this->skip_whitespace();

					if (this->peek() == ']')
					{
						this->advance();
						break;
					}

					if (this->peek() == end_of_text)
					{
						this->error = fault(naxp_message::character_set_not_closed, start, 1);

						return false;
					}

					ascii_char_set item_set;
					char item_char = end_of_text;
					bool item_is_block_escape = false;

					if (!this->try_parse_char_atom(item_set, item_char, item_is_block_escape))
					{
						return false;
					}

					++item_count;

					if (item_is_block_escape)
					{
						result = result | item_set;
						continue;
					}

					this->skip_whitespace();

					if (this->peek() != '-')
					{
						result = result | item_set;
						continue;
					}

					const std::size_t hyphen_offset = this->pos;
					this->advance();
					this->skip_whitespace();

					ascii_char_set upper_set;
					char upper_char = end_of_text;
					bool upper_is_block_escape = false;

					if (!this->try_parse_char_atom(upper_set, upper_char, upper_is_block_escape))
					{
						return false;
					}

					if (upper_is_block_escape)
					{
						this->error = fault(naxp_message::range_upper_bound_is_block_escape, hyphen_offset, 1);

						return false;
					}

					if (upper_char < item_char)
					{
						this->error = fault(
							naxp_message::range_reversed,
							pattern_for_char(upper_char) + "-" + pattern_for_char(item_char),
							hyphen_offset,
							1);

						return false;
					}

					result = result | ascii_char_set::character_range(item_char, upper_char);
				}

				if (item_count == 0)
				{
					this->error = fault(naxp_message::character_set_empty, start, this->pos - start);

					return false;
				}

				set = result;

				return true;
			}

			/// Reads one bare character, escape or block escape.
			///
			/// @param set The characters it denotes.
			/// @param literal_char The single character it denotes, meaningful only when
			///     `is_block_escape` is false. Only a literal character may bound a range.
			/// @param is_block_escape Whether it was one of `\9`, `\A`, `\a` or `\X`.
			/// @returns Whether an atom was read.
			bool try_parse_char_atom(ascii_char_set& set, char& literal_char, bool& is_block_escape)
			{
				set = ascii_char_set();
				literal_char = end_of_text;
				is_block_escape = false;

				const char c = this->peek();

				if (c == '\\')
				{
					const std::size_t backslash_offset = this->pos;
					this->advance();

					const char escaped = this->peek();

					if (is_whitespace(escaped))
					{
						this->error = fault(naxp_message::backslash_before_whitespace, this->pos, 1);

						return false;
					}

					if (escaped == end_of_text)
					{
						this->error = fault(naxp_message::backslash_without_escape, backslash_offset, 1);

						return false;
					}

					this->advance();

					switch (escaped)
					{
						case 's':
							literal_char = ' ';
							set = ascii_char_set::single_character(' ');
							return true;

						case '9':
							set = all_digits;
							is_block_escape = true;
							return true;

						case 'A':
							set = all_upper_case_letters;
							is_block_escape = true;
							return true;

						case 'a':
							set = all_lower_case_letters;
							is_block_escape = true;
							return true;

						case 'X':
							set = all_digits_and_upper_case_letters;
							is_block_escape = true;
							return true;

						case 'x':
							set = all_digits_and_lower_case_letters;
							is_block_escape = true;
							return true;

						case 'C':
						case 'c':
							// A fold before an element is taken in try_parse_element, so one
							// reaching here is inside a character set, where it means nothing.
							this->error = fault(naxp_message::fold_in_character_set, std::string(1, escaped), backslash_offset, 2);
							return false;

						default:
							break;
					}

					if (is_reserved_char(escaped))
					{
						literal_char = escaped;
						set = ascii_char_set::single_character(escaped);

						return true;
					}

					this->error = undefined_escape_error(escaped, backslash_offset);

					return false;
				}

				if (is_bare_char(c))
				{
					this->advance();
					literal_char = c;
					set = ascii_char_set::single_character(c);

					return true;
				}

				this->error = this->unexpected_character();

				return false;
			}

			// Pattern scanning

			bool try_check_pattern_characters()
			{
				for (std::size_t i = 0; i < this->text.size(); ++i)
				{
					const char c = this->text[i];

					if (is_whitespace(c) || (c >= '\x21' && c <= '\x7E'))
					{
						continue;
					}

					std::size_t length = 1;
					std::string name = code_point_as_text(this->text, i, length);

					this->error = fault(naxp_message::character_not_allowed, std::move(name), i, length);

					return false;
				}

				return true;
			}

			char peek() const noexcept
			{
				return this->pos < this->text.size() ? this->text[this->pos] : end_of_text;
			}

			void advance() noexcept
			{
				++this->pos;
			}

			void skip_whitespace() noexcept
			{
				while (this->pos < this->text.size() && is_whitespace(this->text[this->pos]))
				{
					++this->pos;
				}
			}

			/// Refuses whitespace between a decimal range bound's digit and a padding mark on it.
			///
			/// Called where the digit run has ended, which is where a mark separated from its
			/// digit leaves the parser: whitespace is not a digit, so the run stops in front of
			/// it.
			bool try_check_mark_not_split()
			{
				if (is_whitespace(this->peek()))
				{
					const std::size_t whitespace_offset = this->pos;
					std::size_t lookahead = this->pos;

					while (lookahead < this->text.size() && is_whitespace(this->text[lookahead]))
					{
						++lookahead;
					}

					const char after_whitespace = lookahead < this->text.size() ? this->text[lookahead] : end_of_text;

					if (after_whitespace == '!' || after_whitespace == '?')
					{
						this->error = fault(naxp_message::decimal_range_mark_split, whitespace_offset, lookahead - whitespace_offset);

						return false;
					}
				}

				return true;
			}

			/// Rules out whitespace that splits a run of digits, which whitespace between tokens
			/// does not. Called immediately after the run has been read.
			///
			/// @param message Which fault to give, since the two callers word it differently.
			bool try_check_digit_run_not_split(naxp_message message)
			{
				if (is_whitespace(this->peek()))
				{
					const std::size_t whitespace_offset = this->pos;
					std::size_t lookahead = this->pos;

					while (lookahead < this->text.size() && is_whitespace(this->text[lookahead]))
					{
						++lookahead;
					}

					if (lookahead < this->text.size() && is_digit(this->text[lookahead]))
					{
						this->error = fault(message, whitespace_offset, lookahead - whitespace_offset);

						return false;
					}
				}

				return true;
			}

			// Diagnostics

			/// The fault for a position at which an element was required and none begins.
			fault no_element_here() const
			{
				const char c = this->peek();

				if (c == end_of_text)
				{
					return fault(naxp_message::element_required, this->pos, 0);
				}

				if (c == '|' || c == ')')
				{
					return fault(naxp_message::alternative_empty, this->pos, 1);
				}

				if (c == '!')
				{
					return fault(naxp_message::unified_without_element, this->pos, 1);
				}

				return this->unexpected_character();
			}

			/// The fault for a character that cannot appear where it stands.
			fault unexpected_character() const
			{
				const char c = this->peek();

				if (c == end_of_text)
				{
					return fault(naxp_message::naxp_incomplete, this->pos, 0);
				}

				if (c == '*' || c == '+')
				{
					return fault(naxp_message::repetition_unbounded, std::string(1, c), this->pos, 1);
				}

				if (c == '.')
				{
					return fault(naxp_message::any_character, this->pos, 1);
				}

				if (c == '^' || c == '$')
				{
					return fault(naxp_message::anchor, std::string(1, c), this->pos, 1);
				}

				return is_reserved_char(c)
					? fault(naxp_message::reserved_character_here, std::string(1, c), this->pos, 1)
					: fault(naxp_message::character_here, describe_char(c), this->pos, 1);
			}

			/// The fault for a backslash followed by something that is not an escape.
			///
			/// The span covers the backslash and what follows it, which is two characters.
			static fault undefined_escape_error(char escaped, std::size_t backslash_offset)
			{
				return fault(naxp_message::escape_undefined, std::string(1, escaped), backslash_offset, 2);
			}

			std::string_view text;
			std::size_t pos = 0;
			std::optional<fault> error;
		};
	}

	bool try_parse(std::string_view pattern, ast_ptr& tree, std::optional<fault>& error)
	{
		return parser(pattern).try_parse_naxp(tree, error);
	}
}
