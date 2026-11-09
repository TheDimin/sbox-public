# Lamp texture allocation audit — production integration

The shared backend owner handed this worker sole ownership of
`GltfTextureCompiler.cs` for KTX MIME dispatch and preservation of authored
metallic/roughness mips. The compiler now routes general RGBA color/data texture
compilation through `GltfBasisTexturePlan` for KTX input. No existing stock
material adapter or loader capability declaration was changed by this worker.

The canonical `GltfMaterialTextureEncodingPlan.Binding.IsSrgb` determines
whether an input is color or numerical data. Its raster ICC/gamma-ignore policy
does not waive KTX DFD format validation. `GltfMaterialUvPlan` remains the owner
of source UV selection and affine transforms. Valid Basis `rd` orientation needs
no source UV flip. The source texture, sampler and transfer role remain distinct
inputs; this worker introduces no role enumeration or alternate material graph.

## Legacy float adapter cost

For one 2048×2048 ORM image with a complete 12-level pyramid:

| Allocation represented by the current budget formula | Bytes | MiB |
| --- | ---: | ---: |
| One RGBA32F mip chain, 5,592,405 pixels ×16 | 89,478,480 | 85.333313 |
| Retained first native resource payload | 89,478,480 | 85.333313 |
| Current native streaming chain | 89,478,480 | 85.333313 |
| ResourceWriter temporary stream, charged as exact chain size | 89,478,480 | 85.333313 |
| ResourceWriter final copy payload | 89,478,480 | 85.333313 |
| One base-level RGBA32F working image | 67,108,864 | 64 |
| Existing decoder/base allowance, 8 bytes/base pixel | 33,554,432 | 32 |
| Existing overhead allowance | 1,048,576 | 1 |
| **Existing estimated peak, four chain-sized copies plus working/allowance** | **459,625,792** | **438.333313** |

The first table row defines chain size; it is not an additional fifth chain in
the total. These are source-derived accounting values, not measured process
peaks. The source is `GltfTextureCompiler.ValidateMetallicRoughnessBudget`.
`CompileMetallicRoughnessPixels` deliberately produces roughness and metalness
sequentially, retaining the first completed resource while making the second.
`CompileLinearFloatTexture` owns the chain and the current packed float pixels.

`ResourceWriter.SetStreamingData` retains the supplied array without copying.
`ResourceWriter.ToArray` allocates a `ByteStream`, writes the streaming bytes,
and calls `buffer.ToArray()` to copy them into the final resource. `ByteStream`
uses `ArrayPool<byte>.Shared.Rent`; the returned capacity may exceed the requested
size. With a 68-byte resource header and the normal current .NET power-of-two
pool bucket, an 89,478,548-byte request uses a 134,217,728-byte (128 MiB) buffer.
Substituting that stream size, preserving the existing 32 MiB decoder allowance
and adding both retained/final 68-byte headers yields **504,365,176 bytes
(481.000114 MiB)**. This bucket result is an allocation estimate, not a runtime
measurement or guaranteed pool-capacity maximum. Returned pooled storage may
also remain resident independently of these live references.

The new KTX legacy-ORM route additionally charges both original/owned encoded
source copies plus DFD/KVD before using the existing float-adapter budget. It
checks the float preparation limit before native decoding. The default
**256 MiB remains unchanged and rejects this lamp's ORM float repack**. The same
legacy limit also applies to its PNG ORM variant; the sample remains applicable
and pending, not excluded or passed.

## Production source-channel route

The provided material shader should consume authored ORM channels directly:
roughness from G, metallic from B, occlusion from R, with their separate source
factors, UV role selection and samplers. The unchanged general compiler API
`CompileWithMipmaps(image, binding.IsSrgb, budget)` now accepts the KTX source and
emits an RGBA8 native texture chain with **22,369,620 bytes (21.333332 MiB)**.
Two legacy RGBA32F output chains retain **178,956,960 bytes (170.666626 MiB)** of
texture payload before native upload. This size comparison does not establish
measured GPU memory, load time or final quality.

Authored KTX metallic/roughness mips, when using the compatibility adapter, are
packed independently at their source dimensions. Only a missing tail is reduced
in float32 from the final authored level. Reducing to RGBA8 first would quantize
sub-byte factors and discard source precision. The new independent tests use
contradictory authored mip colors and sub-byte factors to reject those mistakes.

No decoded-byte limit was increased, no codec library was added, and no heavy
build, native/editor execution or performance measurement was run by this worker.
