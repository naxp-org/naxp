// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "conformance_data.hpp"
#include "json.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <string>
#include <utility>
#include <vector>

#ifndef NAXP_CONFORMANCE_FILE
#error "NAXP_CONFORMANCE_FILE must be defined by the build."
#endif

namespace logmu::testing
{
	namespace
	{
		std::uint64_t as_uint64(const std::string& digits)
		{
			return std::stoull(digits);
		}

		std::string chosen_file()
		{
			// getenv is the portable spelling; MSVC warns that it is not the safe one, and the
			// value is read once and copied at once.
#ifdef _MSC_VER
#pragma warning(push)
#pragma warning(disable : 4996)
#endif
			const char* overridden = std::getenv("NAXP_CONFORMANCE_FILE");
#ifdef _MSC_VER
#pragma warning(pop)
#endif

			return overridden != nullptr && *overridden != '\0' ? std::string(overridden) : std::string(NAXP_CONFORMANCE_FILE);
		}

		conformance_data read_file()
		{
			const std::string name = chosen_file();
			const json root = read_json_file(name);
			conformance_data data;
			data.file = name;

			data.naxp_version = root["naxpVersion"].text();
			data.test_data_version = std::stoi(root["testDataVersion"].text());

			for (const json& item : root["cases"].items())
			{
				conformance_case entry;
				entry.naxp = item["naxp"].text();
				entry.note = item.has("note") ? item["note"].text() : std::string();
				entry.max_encoded_value = as_uint64(item["maxEncodedValue"].text());
				entry.accepted_count = as_uint64(item["acceptedCount"].text());
				entry.complete = item["complete"].truth();

				for (const json& value : item["values"].items())
				{
					entry.values.push_back(conformance_value{value["in"].text(), as_uint64(value["out"].text()), value["canon"].text()});
				}

				for (const json& invalid : item["invalid"].items())
				{
					entry.invalid.push_back(invalid.text());
				}

				data.cases.push_back(std::move(entry));
			}

			for (const json& item : root["invalidNaxps"].items())
			{
				conformance_invalid_naxp entry;
				entry.naxp = item["naxp"].text();
				entry.rule = item["rule"].text();
				entry.note = item.has("note") ? item["note"].text() : std::string();

				if (item.has("code"))
				{
					entry.code = item["code"].text();
					entry.offset = static_cast<std::size_t>(std::stoull(item["offset"].text()));
					entry.length = static_cast<std::size_t>(std::stoull(item["length"].text()));
				}

				data.invalid_naxps.push_back(std::move(entry));
			}

			if (root.has("pairs"))
			{
				for (const json& item : root["pairs"].items())
				{
					data.pairs.push_back(conformance_pair{
						item["a"].text(),
						item["b"].text(),
						item["decided"].truth(),
						item["acceptedText"].text(),
						item["encoding"].text(),
						item["canonicalForms"].text(),
						as_uint64(item["firstDivergentValue"].text())});
				}
			}

			return data;
		}
	}

	const conformance_data& conformance_data::load()
	{
		static const conformance_data data = read_file();

		return data;
	}
}
