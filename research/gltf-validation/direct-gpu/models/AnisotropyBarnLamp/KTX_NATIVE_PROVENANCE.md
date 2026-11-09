# KTX native dependency: provenance and replacement recipe

This is a read-only audit, not a legal conclusion or a validation of a new
binary. The current Basis public-loader capability remains gated. No native
build, binary replacement or platform execution was performed here.

## Evidence and remaining provenance gap

The local NuGet `Evergine.Bindings.KTX` 2026.6.15.8 package has SHA-256
`93018d0c29675ec91b322cbf47b2dc8435de981944fd9143c4bf2b042ff09cfe`.
Its nuspec identifies [binding commit `85fbb29`](https://github.com/EvergineTeam/KTX.NET/tree/85fbb29ff878bf949cef470bb8660002d6c8901a),
with no package license expression or license file. The package's four desktop
payloads match binary Git blobs in that *same commit*, established with
`git hash-object` on each local file and the GitHub recursive tree:

| RID | Package native SHA-256 | Matching Git blob ID |
| --- | --- | --- |
| linux-x64 | `df45203c9e002b73250a5a5eade0fd7e485427b56239dd82ce3b48ff6b647c8f` | `be50ff99f0fe24fb3f4b1184bc4d79059d7e114e` |
| linux-arm64 | `a8b8bc38fa4fc10916e168eb818e77f58b3765d0ae0ccfe4d92d5acaf5bbbd44` | `9d85d5143a86c7176657c4188558b39b2963ac84` |
| win-x64 | `ab55776f7c2936d2881baaffe8f49249c54aa9f44abf4fa99957b4ea083767cd` | `bc47e5ce01d8292f176796dacb32de8917fe70cf` |
| osx-arm64 | `d7d87fd86043a9f9cc7c924fc5c5865aaeb736685f6e981a6be43bdc95856eee` | `8fec16159b10d288eccc1065984526b3a4ce0c2c` |

The binding commit's [gitlink](https://api.github.com/repos/EvergineTeam/KTX.NET/contents/KTX-Software?ref=85fbb29ff878bf949cef470bb8660002d6c8901a)
points at [KTX-Software `1f63a3ee42bd46f05168850f1a8a8d33e8a92528`](https://github.com/KhronosGroup/KTX-Software/commit/1f63a3ee42bd46f05168850f1a8a8d33e8a92528).
Its [native build script](https://github.com/EvergineTeam/KTX.NET/blob/85fbb29ff878bf949cef470bb8660002d6c8901a/build_native_libs.py)
does not override ETC unpack for Linux or Windows, while that candidate KTX
revision [defaults it ON](https://github.com/KhronosGroup/KTX-Software/blob/1f63a3ee42bd46f05168850f1a8a8d33e8a92528/CMakeLists.txt).
The README says the NuGet workflow does not itself compile native libraries.
Neither the package nor matching Git blobs establish which source tree,
compiler, flags or patches produced those binaries. This is evidence of a
candidate source and recipe, not a native build attestation.

Both Linux binaries export `decompressBlockETC2` and `_ktxUnpackETC`; linux-x64
also contains the `etcdec.cxx` string and Basis Universal and `ZSTD_*` symbols.
In the candidate source, `_ktxUnpackETC` and the references to the ETC block
functions are conditional on `SUPPORT_SOFTWARE_ETC_UNPACK`; `etcdec.cxx` is
added when `KTX_FEATURE_ETC_UNPACK` is enabled. These observations strongly
indicate the optional decoder is built into the Linux binaries, but do not
identify the exact compiled source revision or establish the rights applicable
to those binary copies. Windows and macOS decoder inclusion remains unverified.
The [pinned KTX license](https://github.com/KhronosGroup/KTX-Software/blob/1f63a3ee42bd46f05168850f1a8a8d33e8a92528/LICENSE.md)
identifies the decoder as subject to an [Ericsson agreement](https://github.com/KhronosGroup/KTX-Software/blob/1f63a3ee42bd46f05168850f1a8a8d33e8a92528/external/etcdec/etcdec.cxx).
The [binding notice](../../../../../engine/Sandbox.Gltf/KtxThirdPartyNotices.md)
now flags this evidence rather than presenting the binary as wholly Apache/MIT.

Linux x64 ELF build ID is `39b8aa2dca7ae4b17da51fc1a13d9f0d5e89726b`;
its direct dependencies are `libstdc++.so.6`, `libm.so.6`, `libgcc_s.so.1`,
and `libc.so.6`; symbol requirements reach GLIBC_2.34 and GLIBCXX_3.4.32.
Linux arm64 build ID is `7fa9fd7fef7810b32fa969c06325cc6c05ee1636`, with the
same four plus `ld-linux-aarch64.so.1` and GLIBC_2.34 requirements. Windows
x64 imports `KERNEL32.dll`, `MSVCP140.dll`, `VCRUNTIME140.dll`,
`VCRUNTIME140_1.dll` and Universal CRT API-set DLLs. These are binary imports,
not demonstrated deployment compatibility. No Windows-arm64 or macOS-x64
payload is present in this package.

## Candidate minimal source build

The glTF path in `GltfKtx.DecodeRgba32` needs KTX2 creation, Basis ETC1S and
UASTC transcoding to RGBA32, level/data access and destruction, including
UASTC + Zstandard inputs. It does **not** call GL upload or `_ktxUnpackETC`.
The optional Ericsson ETC1/ETC2 *pixel decoder* is separate from Basis ETC1S
transcoding. At the candidate pin, the `ktx_read` common source list includes
`basis_transcode.cpp`, `basisu_transcoder.cpp` and `zstd.c` unconditionally;
`etcdec.cxx` is conditionally added only for `KTX_FEATURE_ETC_UNPACK`.
This is a justified build candidate, not proof that the resulting library
exports the complete binding ABI or decodes every required fixture.
The pinned Basis vendored-tree metadata identifies upstream commit
`1f4b564ae0b8ec1e67f21f058e53112d0405a25a`; this describes that source tree,
not the unknown source of the packaged binaries.

The commands below are a **not-yet-executed recipe**, to be run in an isolated,
controlled build environment after resource admission. Preserve the fetched
commit/tree, recursive dependency identities, toolchain image digest, compiler,
CMake/Ninja versions, SDK/CRT version, full configure log and `CMakeCache.txt`,
compile commands, linker map and output hashes. The exact Linux baseline and
Windows toolchain are not yet selected; until then this is a repeatable process
outline, not a verified bit-for-bit reproducible build. Inspect this exact
pinned CMake cache and abort on unknown/unused options or a dirty checkout.

On Linux, after acquiring the pinned source into an empty isolated work area:

```sh
git clone https://github.com/KhronosGroup/KTX-Software.git ktx-source
git -C ktx-source checkout --detach 1f63a3ee42bd46f05168850f1a8a8d33e8a92528
git -C ktx-source submodule update --init --recursive
git -C ktx-source status --porcelain
git -C ktx-source submodule status --recursive
export SOURCE_DATE_EPOCH=1781261884
cmake -S ktx-source -B ktx-build -G Ninja \
  -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON \
  -DKTX_FEATURE_KTX2=ON -DKTX_FEATURE_KTX1=OFF \
  -DKTX_FEATURE_ETC_UNPACK=OFF -DKTX_FEATURE_GL_UPLOAD=OFF -DKTX_FEATURE_VK_UPLOAD=OFF \
  -DKTX_FEATURE_TESTS=OFF -DKTX_FEATURE_TOOLS=OFF -DKTX_FEATURE_LOADTEST_APPS=OFF \
  -DKTX_FEATURE_DOC=OFF -DKTX_FEATURE_JNI=OFF -DKTX_FEATURE_PY=OFF \
  -DCMAKE_EXPORT_COMPILE_COMMANDS=ON
cmake --build ktx-build --target ktx_read --parallel 2
```

On Windows use the same detached source and options with a documented Visual
Studio C++ toolchain and SDK, configure with `cmake -S ktx-source -B ktx-build
-A x64` plus the same `-D` options, then `cmake --build ktx-build --config
Release --target ktx_read --parallel 2`. Set `SOURCE_DATE_EPOCH=1781261884`
in the build environment and record the actual linker and timestamp behavior.
**Windows link risk:** the pinned `lib/internalexport.def` unconditionally
names KTX1 and write-related symbols while the `ktx_read` target disables write
and the candidate disables KTX1. Verify the actual MSVC link; if it cannot link,
inspect the pinned build and use a separately reviewed minimal `.def` patch or
the less-minimal full `ktx` target with KTX1 enabled and ETC unpack disabled.
Record the patch or wider linked-object/license inventory and rerun every ABI
and decoding check. Do not assume either fallback has already built.
The current binding imports library name `ktx`, whereas this candidate target
is `ktx_read`: **do not silently rename or replace package files**. First
confirm required exported names and ABI, then design an explicit application
resolver/packaging route and exercise it with the real managed binding. Turning
off KTX1/GL/Vulkan means this candidate is only for the direct glTF path, not a
drop-in replacement for other binding consumers.

For each platform, require all imports actually used by `GltfKtx` to resolve,
verify no Ericsson `decompressBlockETC*` implementation or `etcdec.cxx`
object appears in linker maps/artifacts, and inspect exports, runtime library
imports and minimum platform versions. Test ETC1S, UASTC, UASTC+Zstd, alpha,
authored mips, malformed data and allocation limits against independent fixture
expectations, then verify Linux and Windows cold loading and public rendered
texture cases. Test artifacts and source inventories must reflect the actual
loaded binary hash. Disabling ETC unpack must not be accepted from flags alone.

## License inventory for the candidate source

The candidate source has KTX core under Apache-2.0, the
[Basis transcoder](https://github.com/KhronosGroup/KTX-Software/blob/1f63a3ee42bd46f05168850f1a8a8d33e8a92528/external/basisu/transcoder/basisu_transcoder.cpp)
under Apache-2.0, and its bundled [Zstandard license](https://github.com/KhronosGroup/KTX-Software/blob/1f63a3ee42bd46f05168850f1a8a8d33e8a92528/external/basisu/zstd/LICENSE)
under BSD terms. KTX also contains other components and notices, including
ASTC and DFD utility sources; exact linked objects and their license/notice
obligations must be inventoried from each actual Linux/Windows build, including
static libraries and runtime distributions. Use the pinned `LICENSE.md`,
`NOTICE.md`, `LICENSES/`, `REUSE.toml` and actual compile/link manifests;
source-tree listings alone cannot certify what was distributed. The binding
itself remains MIT as recorded in `KtxThirdPartyNotices.md`. This inventory is
a starting point, not an exhaustive distribution clearance.
