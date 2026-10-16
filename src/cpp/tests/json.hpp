// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The JSON the tests read: the conformance data and the standard naxps.
//
// The reader is written here rather than taken from a library because the build downloads
// nothing, and the files use only objects, arrays, strings, booleans and a few numbers: about a
// hundred lines covers it. It is not a general JSON parser and does not claim to be.

#ifndef NAXP_TESTS_JSON_HPP
#define NAXP_TESTS_JSON_HPP

#include <cstddef>
#include <map>
#include <stdexcept>
#include <string>
#include <variant>
#include <vector>

namespace logmu::testing
{
	/// A parsed JSON value. Numbers are kept as their text, since the data writes every
	/// large number as a string in any case.
	struct json
	{
		using object = std::map<std::string, json>;
		using array = std::vector<json>;

		std::variant<std::nullptr_t, bool, std::string, object, array> value;

		const json& operator[](const std::string& key) const
		{
			const object& members = std::get<object>(this->value);
			const auto found = members.find(key);

			if (found == members.end())
			{
				throw std::runtime_error("The JSON has no member '" + key + "'.");
			}

			return found->second;
		}

		bool has(const std::string& key) const
		{
			return std::get<object>(this->value).count(key) != 0;
		}

		const std::string& text() const
		{
			return std::get<std::string>(this->value);
		}

		const array& items() const
		{
			return std::get<array>(this->value);
		}

		bool truth() const
		{
			return std::get<bool>(this->value);
		}
	};

	/// Reads JSON text.
	json read_json(std::string text);

	/// Reads a JSON file, throwing where it cannot be read.
	json read_json_file(const std::string& name);
}

#endif
