cmake_minimum_required(VERSION 3.20)
project(GtaMountReader LANGUAGES C CXX)
include(FetchContent)
FetchContent_Declare(ragekit GIT_REPOSITORY https://github.com/RatPrez/ragekit.git
    GIT_TAG 9d4110ef0fce900787aaeccb52b67fa5402ff41b)
FetchContent_MakeAvailable(ragekit)
add_library(gta_mount_reader SHARED reader.cpp)
target_compile_features(gta_mount_reader PRIVATE cxx_std_20)
target_link_libraries(gta_mount_reader PRIVATE ragekit)

target_include_directories(gta_mount_reader PRIVATE ${ragekit_SOURCE_DIR}/src ${ragekit_SOURCE_DIR}/third_party/miniz)
target_compile_definitions(gta_mount_reader PRIVATE MINIZ_NO_ARCHIVE_APIS MINIZ_NO_STDIO MINIZ_NO_ZLIB_COMPAT_NAMES)

configure_file(${ragekit_SOURCE_DIR}/LICENSE ${CMAKE_CURRENT_BINARY_DIR}/ragekit.txt COPYONLY)
configure_file(${ragekit_SOURCE_DIR}/third_party/miniz/LICENSE ${CMAKE_CURRENT_BINARY_DIR}/miniz.txt COPYONLY)
configure_file(${ragekit_SOURCE_DIR}/third_party/clipper2/LICENSE ${CMAKE_CURRENT_BINARY_DIR}/clipper2.txt COPYONLY)
