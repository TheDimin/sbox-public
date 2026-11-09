# Basis texture audit — 5 November 2026

The existing codec is reused. `engine/Sandbox.Gltf/GltfKtx.cs` calls the pinned
KTX native library to decode ETC1S or UASTC into owned straight-alpha RGBA32
levels. It already checks source/header/mip bounds and native result metadata,
then destroys the native texture. This work adds no decoder or native binding.

The actual missing path found by source inspection is
`GltfTextureCompiler.OpenImage`: it rejects every MIME except PNG/JPEG. The old
`GltfModelLoader.ReadDocument` capability list also omits `KHR_texture_basisu`.
Stock material preparation additionally rejects this model's normal, occlusion
and emissive inputs. These are managed integration gaps, not evidence of a
native decoder/API blocker. Only the shared owners change those files.

`GltfBasisTexturePlan` now supplies a real CPU resource path: it owns original
encoded bytes, DFD and KVD; selects ETC1S/BasisLZ or UASTC/(none or Zstd) from
actual metadata; selects the existing RGBA32 decoder target deterministically;
and emits native RGBA8888 VTEX bytes using existing resource writers. No OS,
GPU descriptor index, sampler handle or runtime capability result is serialized
into the portable source. Block-compressed transcode/upload optimization remains
unimplemented and is not described as a native limitation.

The plan preserves authored levels in source order. Mipmap sampling fills only
missing tail levels, using the established linear-light color reducer; alpha and
non-color channels stay linear. Base-only sampling consumes only base level,
while the descriptor retains all authored source levels. The allocation check
includes source copies, native encoded/expanded/transcoded storage, managed mip
levels and serialization copies. It is an admission estimate, not a process
memory cap.

## Version, platform and license evidence

The installed package is `Evergine.Bindings.KTX` **2026.6.15.8**, already referenced
by `Sandbox.Gltf.csproj`. Local nuspec repository commit:
`85fbb29ff878bf949cef470bb8660002d6c8901a` in
[EvergineTeam/KTX.Net](https://github.com/EvergineTeam/KTX.Net).
NuGet SHA-256: `93018d0c29675ec91b322cbf47b2dc8435de981944fd9143c4bf2b042ff09cfe`.
The existing `KtxThirdPartyNotices.md` records the binding MIT license and pinned
KTX-Software commit `1f63a3ee42bd46f05168850f1a8a8d33e8a92528`, with upstream
Apache/BSD/MIT/Zlib dependency notices. The nuspec has no license expression or
bundled license file; the exact optional native build configuration and complete
redistribution attribution remain review gates. No new package is introduced.

Read-only inspection of local payloads found:

| RID | Payload evidence | Execution status |
| --- | --- | --- |
| linux-x64 | `ktx.so`, 3,186,520 bytes; SHA-256 `df45203c9e002b73250a5a5eade0fd7e485427b56239dd82ce3b48ff6b647c8f`; required create/transcode/mip/data exports present | New plan not executed |
| win-x64 | `ktx.dll`, 2,080,256 bytes; SHA-256 `ab55776f7c2936d2881baaffe8f49249c54aa9f44abf4fa99957b4ea083767cd` | Not executed |
| linux-arm64, osx-arm64, Android, iOS, browser-wasm | Package payloads exist | Not executed; no compatibility claim |

`readelf` on linux-x64 reports dependencies on libstdc++, libm, libgcc_s and libc;
its highest required versions are GLIBC **2.34** and GLIBCXX **3.4.32**. Payload
presence is not loading success. The current csproj copies exact requested-RID
native content into build/publish output and rejects missing requested-RID
assets; stage/publish execution remains the coordinator's gate. No macOS x64 or
Windows arm64 payload was present.

## Source format requirements and historical data

Primary references are the [ratified Basis extension](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_texture_basisu/README.md)
and [KTX 2.0.4 container specification](https://registry.khronos.org/KTX/specs/2.0/ktxspec.v2.html).
The plan rejects nonstandard orientation or swizzle, incompatible source codec /
supercompression pairs, a role/transfer mismatch, premultiplied alpha, unsupported
texture dimensionality, malformed metadata and oversized level ranges before
native decoding.

All four pinned lamp textures are 2048×2048 UASTC+Zstandard, one authored level,
`KTXorientation=rd`. Base color has BT709+sRGB DFD. Normal, ORM and optional
anisotropy maps have **BT709+linear** DFD. The current extension requests
unspecified primaries for non-color data. These historical sample bytes are
retained exactly and exposed as `HasNonColorPrimaries`; no primary conversion is
applied to numerical data channels. This compatibility policy is explicit and
must not be presented as strict metadata conformance. Transfer mismatches still
fail. The complete authored DFD/KVD hashes are in `source-audit.json`.
