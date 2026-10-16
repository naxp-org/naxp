// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

#include "json.hpp"

#include <cctype>
#include <cstddef>
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>

namespace logmu::testing
{
	namespace
	{
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
					throw std::runtime_error("The JSON has trailing content.");
				}

				return result;
			}

		private:
			json read_value()
			{
				this->skip_whitespace();

				if (this->pos >= this->text.size())
				{
					throw std::runtime_error("The JSON ends early.");
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
						throw std::runtime_error("The JSON has an unterminated string.");
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
								throw std::runtime_error("The JSON holds a non-ASCII escape.");
							}

							result.push_back(static_cast<char>(code));
							break;
						}

						default:
							throw std::runtime_error("The JSON has an unknown escape.");
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
					throw std::runtime_error("The JSON has an unexpected character.");
				}

				return this->text.substr(start, this->pos - start);
			}

			void expect(const char* literal)
			{
				const std::string_view expected(literal);

				if (this->text.compare(this->pos, expected.size(), expected) != 0)
				{
					throw std::runtime_error("The JSON does not read as JSON where '" + std::string(expected) + "' was expected.");
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
	}

	json read_json(std::string text)
	{
		return json_reader(std::move(text)).read();
	}

	json read_json_file(const std::string& name)
	{
		std::ifstream file(name, std::ios::binary);

		if (!file)
		{
			throw std::runtime_error("The JSON could not be read from " + name + ".");
		}

		std::ostringstream buffer;
		buffer << file.rdbuf();

		return read_json(buffer.str());
	}
}
