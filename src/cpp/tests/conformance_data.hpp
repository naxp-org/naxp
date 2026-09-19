// Copyright (c) Tim Gordon.
// This file is licensed to you under the Apache Licence, Version 2.0. See the LICENSE file.

// The conformance data, read from the JSON the generator writes.
//
// The reader is written here rather than taken from a library because the build downloads
// nothing, and the data uses only objects, arrays, strings, booleans and a few numbers: about a
// hundred lines covers it. It is not a general JSON parser and does not claim to be.

#ifndef NAXP_TESTS_CONFORMANCE_DATA_HPP
#define NAXP_TESTS_CONFORMANCE_DATA_HPP

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace logmu::testing
{
	struct conformance_value
	{
		std::string in;
		std::uint64_t out;
		std::string canon;
	};

	struct conformance_case
	{
		std::string naxp;
		std::string note;
		std::uint64_t max_encoded_value;
		std::uint64_t accepted_count;
		bool complete;
		std::vector<conformance_value> values;
		std::vector<std::string> invalid;
	};

	struct conformance_invalid_naxp
	{
		std::string naxp;
		std::string rule;
		std::string note;

		/// The fault's code and span, which the fuzz data records and the conformance data does
		/// not; empty where absent.
		std::string code;
		std::size_t offset = 0;
		std::size_t length = 0;
	};

	/// Two naxps and how the reference implementation compares them. Fuzz data only.
	struct conformance_pair
	{
		std::string a;
		std::string b;
		bool decided;
		std::string accepted_text;
		std::string encoding;
		std::string printed_text;
		std::uint64_t first_divergent_value;
	};

	struct conformance_data
	{
		std::string naxp_version;
		int test_data_version;
		std::vector<conformance_case> cases;
		std::vector<conformance_invalid_naxp> invalid_naxps;
		std::vector<conformance_pair> pairs;

		/// The file that was read.
		std::string file;

		/// Reads the data, once: the file named by the NAXP_CONFORMANCE_FILE environment
		/// variable where it is set, which is how the fuzz output is run through the same
		/// tests, and otherwise the conformance data the build points at.
		static const conformance_data& load();
	};
}

#endif
