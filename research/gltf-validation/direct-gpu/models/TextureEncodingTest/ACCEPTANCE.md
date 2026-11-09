# TextureEncodingTest validation

The assigned source is Khronos glTF-Sample-Assets commit
`90d7ede14c7e280af263824604b427a1ca02cb66`, CC0-1.0 (lexaknyazev, 2017).
The actual upstream variants are **glTF** and **glTF-Binary**. There is no
glTF-Embedded variant. The source audit pins both inputs, their ten external
dependencies and the exact image bytes embedded in the GLB. The external form
also receives a content-addressed portable compiled package; the GLB compiled
form remains self-contained. Packaging agreement proves dependency closure and
byte preservation, not color correctness.

The normative baseline is published [glTF 2.0.1](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html),
commit `8e798b02d254cea97659a333cfcb20875b62bdd4`, sections 3.8.3, 3.9.2,
3.9.3 and 3.9.4. Color-space image metadata is ignored. Base-color and emissive
RGB use sRGB; metallic/roughness use linear G/B; alpha is linear and stored
without premultiplication. Image rows begin at the upper left. OPAQUE ignores
alpha coverage, while MASK and BLEND consume it according to their policies.

`TextureEncodingCoreTests.RunAll(checkoutRoot)` declares **28 result rows**:
four reference/native serialization controls, plus six rows for each of the
four forms (two upstream variants × source/portable compiled).

The independent color oracle is the literal texel `[0,136,0,255]` or
`[0,136,255,255]`, and the sRGB equation gives green
`0.24620132670783548`. Metallic/roughness green remains `136/255`.
The six 1×1 RGB PNGs keep these values while their `gAMA` (0.2) or `iCCP`
metadata changes. The BoxTextured owner's bounded PNG oracle is reused for
these images and the literal RGB/RGBA fixtures. A separate bounded decoder
handles the actual labels' 4-bit indexed pixels and transparency. It is not a
production decoder or an unbounded general PNG implementation.

The native-resource comparisons check every decoded base texel, format, mip
count and linear/sRGB resource flag. The 2×2 literal fixture exposes vertical
flips, RGB/RGBA expansion, alpha premultiplication and loss of RGB under zero
alpha. The black/white mip control expects `[188,188,188,128]` for sRGB and
`[128,128,128,128]` for linear. The material checks compare factor-only colors
and packed float32 roughness against literal expected numbers, without using
another material render as an independent proof.

`TextureEncodingGpuSamplingTests` declares one separate in-engine test with
**64 completed numeric samples**: 48 sample PNG/role/form combinations and
16 asymmetric literal fixture samples. It compiles two anonymous diagnostic
shaders, uses actual native texture leases and `Graphics.Blit` raw vertices,
flushes GPU completion, and reads a 1×1 RGBA32F render target. No Model or Mesh
is created. Fresh magenta clear controls bracket sampling. The actual sampled
green color/data separation must exceed 0.28; RGB absolute error is bounded at
0.001, below one normalized byte and over 280 times smaller than the wrong
transfer gap. Linear alpha error is bounded at 0.000001. This proves diagnostic
sampling only when executed successfully; source-material/scene parity remains
a different gate. It uses no camera, lighting, exposure or postprocess oracle.

The approved new production file
`engine/Sandbox.Engine/Resources/Model/Gltf/Materials/GltfMaterialTextureEncodingPlan.cs`
provides metadata-only per-role image/sampler/encoding/channel/alpha planning.
It consumes the UV owner's canonical `GltfMaterialTextureRole`. Its focused
unit fixture has **17 cases**, including shared-image color/data roles,
contradiction rejection, no image I/O, selected-role laziness for unlit,
sampler omission/filter preservation and Basis extension/fallback selection.
The Unlit owner consumes this plan; existing texture-compiler/stock-adapter
wiring remains owned by the shared material integration workstream.

Executed evidence: the bounded Python source audit succeeded and produced
`source-audit.json`. The coordinator's actual Engine D run passed all **28
native-serialization rows**. The GPU test failed during anonymous shader
compilation, before any sample measurement, because the native parser did not
find its inline vertex-program block. The failed receipt remains retained;
[EVIDENCE.md](EVIDENCE.md) identifies it. A narrow multiline block correction is
submitted for a separate rerun. All 17 production-plan unit cases, actual GPU
sampling, rendered material comparison and public `Model.Load` remain pending.
This worker did not build, launch, drive or mutate an editor, compile shaders or
run a heavyweight job.

The existing stock adapter rejects emissive and MASK materials, including this
sample's emissive row and label materials. Metadata planning and successful
base-color/metallic-roughness serialization cannot close that limitation.
GPU acceptance needs independent numeric sRGB/linear sample readback or a
matched independent numeric image, explicit negative transfer/alpha/origin
controls and the shared empty-before/positive/deleted-empty controls. Same
material images and source-versus-compiled images alone are insufficient.
