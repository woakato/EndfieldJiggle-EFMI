cmake_minimum_required(VERSION 3.20)
project(EndfieldJiggleInputGuard LANGUAGES C CXX)
if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "The input guard requires Windows x64.")
endif()

include(FetchContent)
FetchContent_Declare(minhook
    GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git
    GIT_TAG c3fcafdc10146beb5919319d0683e44e3c30d537)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "Build dependencies statically" FORCE)
FetchContent_MakeAvailable(minhook)
set_property(TARGET minhook PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_library(input_guard SHARED InputGuard.cpp)
set_target_properties(input_guard PROPERTIES OUTPUT_NAME EndfieldJiggleInputGuard)
target_link_libraries(input_guard PRIVATE minhook user32)

add_executable(input_guard_companion Companion.cpp)
set_target_properties(input_guard_companion PROPERTIES OUTPUT_NAME EndfieldJiggleInputGuard)
target_link_libraries(input_guard_companion PRIVATE user32)

add_executable(input_guard_tests ../../tests/input_guard_host.cpp InputGuard.cpp)
add_executable(input_companion_fixture ../../tests/input_companion_fixture.cpp)
add_library(input_cursor_probe SHARED ../../tests/input_cursor_probe.cpp)
target_link_libraries(input_cursor_probe PRIVATE user32)
add_dependencies(input_guard_tests input_cursor_probe)
target_compile_definitions(input_guard_tests PRIVATE EJ_INPUT_GUARD_TESTING)
target_include_directories(input_guard_tests PRIVATE .)
target_link_libraries(input_guard_tests PRIVATE minhook user32)

foreach(target input_guard input_guard_companion input_guard_tests input_cursor_probe input_companion_fixture)
    target_compile_features(${target} PRIVATE cxx_std_17)
    target_compile_definitions(${target} PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
    set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endforeach()
enable_testing()
add_test(NAME input_guard_tests COMMAND input_guard_tests)
