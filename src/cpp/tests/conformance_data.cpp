// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "conformance_data.hpp"

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <fstream>
#include <map>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <variant>
#include <vector>

#ifndef NAXP_CONFORMANCE_FILE
#error "NAXP_CONFORMANCE_FILE must be defined by the build."
#endif

namespace logmu::testing
{
	namespace
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
					throw std::runtime_error("The conformance data has no member '" + key + "'.");
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

		class json_reader
		{
		public:
			explicit json_reader(std::string text)
				: text(std::move(text))
			{
			}

			json read()
			{
				json result = this->read_value();
				this->skip_whitespace();

				if (this->pos != this->text.size())
				{
					throw std::runtime_error("The conformance data has trailing content.");
				}

				return result;
			}

		private:
			json read_value()
			{
				this->skip_whitespace();

				if (this->pos >= this->text.size())
				{
					throw std::runtime_error("The conformance data ends early.");
				}

				const char c = this->text[this->pos];

				switch (c)
				{
					case '{': return this->read_object();
					case '[': return this->read_array();
					case '"': return json{this->read_string()};
					case 't': this->expect("true"); return json{true};
					case 'f': this->expect("false"); return json{false};
					case 'n': this->expect("null"); return json{nullptr};
					default: return json{this->read_number()};
				}
			}

			json read_object()
			{
				json::object members;
				++this->pos;
				this->skip_whitespace();

				if (this->peek() == '}')
				{
					++this->pos;

					return json{std::move(members)};
				}

				while (true)
				{
					this->skip_whitespace();
					std::string key = this->read_string();
					this->skip_whitespace();
					this->expect(":");
					members.emplace(std::move(key), this->read_value());
					this->skip_whitespace();

					if (this->peek() == ',')
					{
						++this->pos;
						continue;
					}

					this->expect("}");

					return json{std::move(members)};
				}
			}

			json read_array()
			{
				json::array items;
				++this->pos;
				this->skip_whitespace();

				if (this->peek() == ']')
				{
					++this->pos;

					return json{std::move(items)};
				}

				while (true)
				{
					items.push_back(this->read_value());
					this->skip_whitespace();

					if (this->peek() == ',')
					{
						++this->pos;
						continue;
					}

					this->expect("]");

					return json{std::move(items)};
				}
			}

			std::string read_string()
			{
				this->expect("\"");
				std::string result;

				while (true)
				{
					if (this->pos >= this->text.size())
					{
						throw std::runtime_error("The conformance data has an unterminated string.");
					}

					const char c = this->text[this->pos++];

					if (c == '"')
					{
						return result;
					}

					if (c != '\\')
					{
						result.push_back(c);
						continue;
					}

					const char escaped = this->text[this->pos++];

					switch (escaped)
					{
						case '"': result.push_back('"'); break;
						case '\\': result.push_back('\\'); break;
						case '/': result.push_back('/'); break;
						case 'b': result.push_back('\b'); break;
						case 'f': result.push_back('\f'); break;
						case 'n': result.push_back('\n'); break;
						case 'r': result.push_back('\r'); break;
						case 't': result.push_back('\t'); break;
						case 'u':
						{
							// The data is ASCII, so only the basic plane below U+0080 arrives
							// this way; anything else would be a defect in the data.
							const unsigned long code = std::stoul(this->text.substr(this->pos, 4), nullptr, 16);
							this->pos += 4;

							if (code >= 0x80)
							{
								throw std::runtime_error("The conformance data holds a non-ASCII escape.");
							}

							result.push_back(static_cast<char>(code));
							break;
						}

						default:
							throw std::runtime_error("The conformance data has an unknown escape.");
					}
				}
			}

			std::string read_number()
			{
				const std::size_t start = this->pos;

				while (this->pos < this->text.size() && (std::isdigit(static_cast<unsigned char>(this->text[this->pos])) || this->text[this->pos] == '-' || this->text[this->pos] == '.' || this->text[this->pos] == 'e' || this->text[this->pos] == 'E' || this->text[this->pos] == '+'))
				{
					++this->pos;
				}

				if (this->pos == start)
				{
					throw std::runtime_error("The conformance data has an unexpected character.");
				}

				return this->text.substr(start, this->pos - start);
			}

			void expect(const char* literal)
			{
				const std::string_view expected(literal);

				if (this->text.compare(this->pos, expected.size(), expected) != 0)
				{
					throw std::runtime_error("The conformance data does not read as JSON where '" + std::string(expected) + "' was expected.");
				}

				this->pos += expected.size();
			}

			char peek() const noexcept
			{
				return this->pos < this->text.size() ? this->text[this->pos] : '\0';
			}

			void skip_whitespace() noexcept
			{
				while (this->pos < this->text.size() && (this->text[this->pos] == ' ' || this->text[this->pos] == '\t' || this->text[this->pos] == '\r' || this->text[this->pos] == '\n'))
				{
					++this->pos;
				}
			}

			std::string text;
			std::size_t pos = 0;
		};

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
			std::ifstream file(name, std::ios::binary);

			if (!file)
			{
				throw std::runtime_error("The conformance data could not be read from " + name + ".");
			}

			std::ostringstream buffer;
			buffer << file.rdbuf();

			const json root = json_reader(buffer.str()).read();
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
						item["printedText"].text(),
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
