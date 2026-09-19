// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The C face over the C++ library. No exception crosses it: every function that can allocate
// catches whatever escapes and answers with the failure value its contract names.

#include "naxp/naxp.h"
#include "naxp/naxp.hpp"

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <new>
#include <optional>
#include <string>
#include <string_view>
#include <utility>

/// The opaque handle is the C++ class itself, which is a cheap-to-copy pointer to the
/// compilation and so costs one allocation to hand out.
struct naxp
{
	explicit naxp(logmu::naxp parsed)
		: value(std::move(parsed))
	{
	}

	logmu::naxp value;
};

struct naxp_fault
{
	explicit naxp_fault(logmu::naxp_fault parts)
		: value(std::move(parts))
	{
	}

	logmu::naxp_fault value;
};

namespace
{
	/// Writes text into a caller's buffer on the C contract: nothing at all where it does not
	/// fit, the length where it does.
	bool write_out(std::string_view text, char* destination, size_t capacity, size_t* length) noexcept
	{
		if (text.size() > capacity)
		{
			if (length != nullptr)
			{
				*length = 0;
			}

			return false;
		}

		if (!text.empty())
		{
			std::memcpy(destination, text.data(), text.size());
		}

		if (length != nullptr)
		{
			*length = text.size();
		}

		return true;
	}
}

extern "C"
{
	naxp *naxp_parse(const char *pattern, size_t pattern_length, naxp_fault **fault)
	{
		if (fault != nullptr)
		{
			*fault = nullptr;
		}

		try
		{
			logmu::naxp_fault found;
			std::optional<logmu::naxp> parsed = logmu::naxp::try_parse(std::string_view(pattern, pattern_length), &found);

			if (parsed.has_value())
			{
				return new naxp(std::move(*parsed));
			}

			if (fault != nullptr)
			{
				*fault = new naxp_fault(std::move(found));
			}

			return nullptr;
		}
		catch (...)
		{
			return nullptr;
		}
	}

	void naxp_free(naxp *expression)
	{
		delete expression;
	}

	const char *naxp_fault_code(const naxp_fault *fault)
	{
		return fault->value.code.c_str();
	}

	const char *naxp_fault_message(const naxp_fault *fault)
	{
		return fault->value.message.c_str();
	}

	size_t naxp_fault_offset(const naxp_fault *fault)
	{
		return fault->value.offset;
	}

	size_t naxp_fault_length(const naxp_fault *fault)
	{
		return fault->value.length;
	}

	void naxp_fault_free(naxp_fault *fault)
	{
		delete fault;
	}

	const char *naxp_pattern(const naxp *expression, size_t *length)
	{
		const std::string_view pattern = expression->value.pattern();

		if (length != nullptr)
		{
			*length = pattern.size();
		}

		// The view is over the compilation's own std::string, which is NUL-terminated.
		return pattern.data();
	}

	uint64_t naxp_max_encoded_value(const naxp *expression)
	{
		return expression->value.max_encoded_value();
	}

	size_t naxp_max_length(const naxp *expression)
	{
		return expression->value.max_length();
	}

	bool naxp_accepts(const naxp *expression, const char *text, size_t text_length)
	{
		return expression->value.accepts(std::string_view(text, text_length));
	}

	uint64_t naxp_encode(const naxp *expression, const char *text, size_t text_length)
	{
		try
		{
			return expression->value.encode(std::string_view(text, text_length));
		}
		catch (...)
		{
			return 0;
		}
	}

	bool naxp_decode(const naxp *expression, uint64_t encoded_value, char *destination, size_t capacity, size_t *length)
	{
		try
		{
			std::string text;

			if (!expression->value.try_decode(encoded_value, text))
			{
				if (length != nullptr)
				{
					*length = 0;
				}

				return false;
			}

			return write_out(text, destination, capacity, length);
		}
		catch (...)
		{
			if (length != nullptr)
			{
				*length = 0;
			}

			return false;
		}
	}

	bool naxp_compare(const naxp *a, const naxp *b, naxp_comparison *comparison)
	{
		comparison->accepted_text = NAXP_INCOMPARABLE;
		comparison->encoding = NAXP_INCOMPARABLE;
		comparison->printed_text = NAXP_INCOMPARABLE;

		try
		{
			logmu::naxp_comparison found;

			if (!logmu::naxp::try_compare(a->value, b->value, found))
			{
				return false;
			}

			// The two enums share their values, which the tests check.
			comparison->accepted_text = static_cast<naxp_set_relationship>(found.accepted_text);
			comparison->encoding = static_cast<naxp_set_relationship>(found.encoding);
			comparison->printed_text = static_cast<naxp_set_relationship>(found.printed_text);

			return true;
		}
		catch (...)
		{
			return false;
		}
	}

	uint64_t naxp_first_divergent_value(const naxp *a, const naxp *b)
	{
		try
		{
			return logmu::naxp::first_divergent_value(a->value, b->value);
		}
		catch (...)
		{
			return 0;
		}
	}

	char *naxp_emit(
		const naxp *expression,
		naxp_output_language language,
		const char *prefix,
		naxp_value_type value_type,
		const char *initial_indent,
		const char *new_line,
		const char *indent,
		size_t *length)
	{
		if (length != nullptr)
		{
			*length = 0;
		}

		try
		{
			const std::string fragment = expression->value.emit(
				static_cast<logmu::output_language>(language),
				prefix == nullptr ? std::string_view() : std::string_view(prefix),
				static_cast<logmu::naxp_value_type>(value_type),
				initial_indent == nullptr ? std::string_view("") : std::string_view(initial_indent),
				new_line == nullptr ? std::string_view("\n") : std::string_view(new_line),
				indent == nullptr ? std::string_view("\t") : std::string_view(indent));

			char *copy = new char[fragment.size() + 1];
			std::memcpy(copy, fragment.c_str(), fragment.size() + 1);

			if (length != nullptr)
			{
				*length = fragment.size();
			}

			return copy;
		}
		catch (...)
		{
			return nullptr;
		}
	}

	void naxp_string_free(char *text)
	{
		delete[] text;
	}

	bool naxp_canonical_form(const naxp *expression, const char *text, size_t text_length, char *destination, size_t capacity, size_t *length)
	{
		try
		{
			const std::optional<std::string> canonical = expression->value.canonical_form(std::string_view(text, text_length));

			if (!canonical.has_value())
			{
				if (length != nullptr)
				{
					*length = 0;
				}

				return false;
			}

			return write_out(*canonical, destination, capacity, length);
		}
		catch (...)
		{
			if (length != nullptr)
			{
				*length = 0;
			}

			return false;
		}
	}
}
