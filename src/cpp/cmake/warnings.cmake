# The warning set every naxp target builds with. Warnings are errors, so that a build that
# passes on one compiler has nothing waiting for the next.
#
# A parameter or local may share a member's name. Members are always written `this->name`, so
# the shadowing is harmless and a constructor can read `: children(children)`; GCC's
# -Wshadow=local and MSVC without C4458 warn about every other kind of shadowing.
function(naxp_set_warnings target)
	if(MSVC)
		target_compile_options(${target} PRIVATE /W4 /WX /permissive- /utf-8 /wd4458)
	else()
		target_compile_options(${target} PRIVATE
			-Wall
			-Wextra
			-Wpedantic
			-Werror
			-Wconversion
			-Wsign-conversion
			-Wshadow=local
			$<$<COMPILE_LANGUAGE:CXX>:-Wold-style-cast>)
	endif()
endfunction()
