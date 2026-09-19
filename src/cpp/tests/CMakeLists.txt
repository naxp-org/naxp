set(naxp_test_sources
	check.cpp
	ascii_char_set_tests.cpp
	c_header_tests.c
	comparison_tests.cpp
	conformance_data.cpp
	conformance_tests.cpp
	emitter_tests.cpp
	naxp_message_tests.cpp
	naxp_tests.cpp
	parser_tests.cpp
	public_headers_tests.cpp
	state_map_tests.cpp)

# The conformance data lives at the repository root, one file per version of the language.
set(naxp_conformance_file ${CMAKE_CURRENT_SOURCE_DIR}/../../../conformance/naxp-v0.10.json)

# The tests reach the private headers, which nothing else may.
function(naxp_set_test_properties target)
	target_include_directories(${target} PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/../src)

	set_target_properties(${target} PROPERTIES
		CXX_STANDARD 17
		CXX_STANDARD_REQUIRED ON
		CXX_EXTENSIONS OFF
		C_STANDARD 11
		C_STANDARD_REQUIRED ON
		C_EXTENSIONS OFF)

	naxp_set_warnings(${target})

	target_compile_definitions(${target} PRIVATE NAXP_CONFORMANCE_FILE="${naxp_conformance_file}")
endfunction()

add_executable(naxp_tests ${naxp_test_sources})
target_link_libraries(naxp_tests PRIVATE naxp::naxp)
naxp_set_test_properties(naxp_tests)

add_test(NAME naxp_tests COMMAND naxp_tests)

# The same tests over the amalgamation in place of the library, so that every build proves the
# single file is the library. One further test includes the flat headers as a consumer would.
if(TARGET naxp_amalgamation)
	add_executable(naxp_amalgamation_tests
		${naxp_test_sources}
		amalgamation_tests.cpp
		${NAXP_AMALGAMATION_DIR}/naxp.cpp)

	add_dependencies(naxp_amalgamation_tests naxp_amalgamation)
	naxp_set_test_properties(naxp_amalgamation_tests)

	target_include_directories(naxp_amalgamation_tests PRIVATE
		${CMAKE_CURRENT_SOURCE_DIR}/../include
		${NAXP_AMALGAMATION_DIR})

	target_compile_definitions(naxp_amalgamation_tests PRIVATE NAXP_PROJECT_VERSION="${PROJECT_VERSION}")

	add_test(NAME naxp_amalgamation_tests COMMAND naxp_amalgamation_tests)
endif()

# Differential testing against the JavaScript implementation. The generator writes random naxps
# and what that implementation makes of them in the conformance data's shape, and the same
# conformance tests then run over it. Needs Node; where there is none the test is left out.
if(NAXP_NODE)
	set(NAXP_FUZZ_SEED 1 CACHE STRING "The seed the fuzz generator runs with")
	set(NAXP_FUZZ_COUNT 2000 CACHE STRING "How many naxps the fuzz generator writes")
	set(fuzz_file ${CMAKE_CURRENT_BINARY_DIR}/fuzz-${NAXP_FUZZ_SEED}.json)

	add_test(NAME naxp_fuzz_generate
		COMMAND ${NAXP_NODE} ${CMAKE_CURRENT_SOURCE_DIR}/../../../conformance/fuzz/generate.js
			--seed ${NAXP_FUZZ_SEED} --count ${NAXP_FUZZ_COUNT} --out ${fuzz_file})

	add_test(NAME naxp_fuzz COMMAND naxp_tests conformance)

	set_tests_properties(naxp_fuzz_generate PROPERTIES FIXTURES_SETUP fuzz_data)
	set_tests_properties(naxp_fuzz PROPERTIES
		FIXTURES_REQUIRED fuzz_data
		ENVIRONMENT "NAXP_CONFORMANCE_FILE=${fuzz_file}")

	# The emitters, held byte-identical to the JavaScript implementation's: the generator emits
	# every conformance naxp in every language through that implementation, and the test emits
	# the same through this one and compares.
	set(emit_file ${CMAKE_CURRENT_BINARY_DIR}/emit-expected.txt)

	add_test(NAME naxp_emit_generate
		COMMAND ${NAXP_NODE} ${CMAKE_CURRENT_SOURCE_DIR}/../../../conformance/emit/generate.js
			--data ${naxp_conformance_file} --out ${emit_file})

	add_test(NAME naxp_emit COMMAND naxp_tests emitted_fragments_match)

	set_tests_properties(naxp_emit_generate PROPERTIES FIXTURES_SETUP emit_data)
	set_tests_properties(naxp_emit PROPERTIES
		FIXTURES_REQUIRED emit_data
		ENVIRONMENT "NAXP_EMIT_EXPECTED=${emit_file}")
endif()
