# BoxVertexColors direct GPU acceptance

This worker owns only this folder. Shared backend/material changes are owned by
`/root/gpu_fox`; `/root/gpu_validation` owns every build, shader compilation,
editor call and native capture. No native Model/Mesh is created by these helpers.

## Frozen source and independent expected data

The corpus is glTF-Sample-Assets commit
`90d7ede14c7e280af263824604b427a1ca02cb66`. Actual upstream variants are `glTF`,
`glTF-Binary`, and `glTF-Embedded`. All three and their derived portable GLBs are
required. The source license is CC0-1.0, artist Marco Hutter. Source data and
license metadata remain in the pinned reference checkout.

`../../../../../engine/Tools/Gltf/direct-gpu/models/BoxVertexColors/author_reference.py` uses bounded Python stdlib JSON/GLB/accessor decoding and
does not import the production reader/evaluator/compiler. `reference.json`
contains all 24 literal positions, normals, RGB colors and 36 U16 indices.
`source-audit.json` records hashes and exact accessor/view representation.
The default scene has one node, one primitive, no explicit material, no skin,
no animation, no UVs and no extensions.

POSITION, NORMAL and COLOR_0 share one 864-byte view at source-buffer offset 72.
They use accessor-relative offsets 0, 288 and 576 with stride 12. COLOR_0 is
FLOAT VEC3 and RGB alpha is one. Source RGB happens to equal POSITION exactly.
The unmodified sample alone therefore cannot detect reading POSITION in place
of COLOR_0. Seven labeled fixtures break that correlation:

- FLOAT3 and FLOAT4, UNORM8 RGB/RGBA, UNORM16 RGB/RGBA.
- A FLOAT4 COLOR_0 clamp fixture with negative and greater-than-one values.

Every fixture retains literal cube geometry, uses a nonzero color accessor
offset and padded stride, has an independent COLOR_1 FLOAT4 sentinel
`[-.25,.5,1.25,.75]`, and carries linear baseColorFactor `[.5,.25,.75,.5]`.
`fixtures.json` hashes each self-contained source and records exact independently
decoded expectations. Compiled fixtures are produced by the installed compiler
and checked against these same independent values, not against each other.

## Standards and shared defects

The [pinned published glTF 2.0.1 specification](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#meshes)
defines COLOR_n as linear RGB/RGBA multipliers, permits FLOAT or normalized
unsigned byte/short, assumes alpha one for RGB, and requires COLOR_0 clamping.
Extra numbered color sets remain individually described and preserved; this
worker does not apply COLOR_0's clamp rule to COLOR_1. Base color combines the
linear vertex color, linear factor and decoded base-color texture. Alpha remains
linear. OPAQUE material behavior ignores the resulting alpha for visibility;
the output buffer still preserves the authored alpha.

Two shared defects were reported before changing shared files:

1. The deformation shader's unconditional color `saturate` clamps COLOR_1 too.
   Its condition must include semantic index zero. The actual GPU fixture must
   retain the COLOR_1 sentinel while clamping COLOR_0.
2. The default complex shader input path lacks COLOR0 and initializes vertex
   color from instance tint. Buffer readback can pass while the cube renders
   white. The separate shipped `vertex_color.shader` applies an sRGB decode to
   its color input, which changes fractional linear glTF colors. A source-color
   material path needs an actual raw linear COLOR0 shader input/multiplier.

`../../../../../engine/Tools/Gltf/direct-gpu/models/BoxVertexColors/box_vertex_color_reference.shader` is research source outside watched content.
It is an independent PBR reference: the standard vertex-processing result is
multiplied by raw linear COLOR0 without gamma conversion. It is not the shipped
implementation or an override that can make the source-material gate pass.

## Inventory and execution state

Executed locally: independent Python oracle controls **5/5 passed**. The initial
test-first run failed because the reference module did not exist; that red start
is retained in the chat. No engine build, shader compilation or editor action
was performed by this worker.

Prepared C# assertions, currently unexecuted:

| Entry | Rows | Scope |
| --- | ---: | --- |
| `BoxVertexColorsCoreTests.RunAll(checkout)` | 64 | Owned source descriptors/bytes, runtime staging plans, source/compiled packages, independent oracle controls |
| `BoxVertexColorsColorLayoutTests.RunAll(checkout)` | 16 | Invalid signed/unnormalized/integer/dimension/set-name source and compiled import rejection |
| `BoxVertexColorsGpuVerifierTests.RunAll(checkout)` | 10 | Synthetic checker controls for layout order, offset alias, missing/gamma-decoded/factor-baked colors, RGB alpha, COLOR_1 clamp, NaN, index/source maps |
| `BoxVertexColorsImageVerifierTests.RunAll(checkout)` | 8 | Synthetic image-checker controls, including empty/missing references |

Namespace: `Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation`. GPU helpers have
internal class visibility because the production backend is internal; public
static entry methods remain available to the coordinator's reflection runner.

Prepared actual execution, currently unexecuted:

- `BoxVertexColorsGpuProbe.Run`: **38** measurements: rest for all 20 forms,
  plus affine, reflection and independent-instance/disposal controls for each
  of the six real sample forms. Bound indices must come from GPU readback.
  Color error allowance is two float32 ulps at unit scale, with COLOR_0 clamp,
  COLOR_1 preservation, RGB alpha one and no material-factor baking enforced.
- `BoxVertexColorsImageProbe.Run`: **20** actual source-material images against
  independent literal FLOAT4-colored triangle images. It requires fresh empty,
  positive reference, repeated stable reference, deleted-empty, white-color
  negative, fractional-gamma negative for derived fixtures, actual source,
  and deleted-empty controls. The camera must see enough of the cube for at
  least 1024 foreground pixels and at least 256 changed negative-control pixels.
  Supply an independently compiled reference shader and explicit reviewed IoU,
  mean RGB and maximum RGB thresholds. The actual asset uses default material
  resolution, with no diagnostic resolver. Readback alone cannot pass this gate.

Image reference geometry uses a fixed explicitly declared *reference output*
layout with FLOAT4 color. No Color32 quantization or producer geometry is used.
The source material's parameter resources are copied, then its shader is replaced
only for the expected image. This isolates actual vertex-color shader consumption.
It does **not** independently certify PBR lighting, texture sampling, or full
factor/texture material fidelity. Those remain TextureTransform/Unlit/encoding
workstream gates. Source/compiled agreement is not an independent expected image.

The coordinator must retain source/producer/shader identities, actual per-form
assertions, capture controls, supplied thresholds and artifact hashes. A failed
first case or compiler abort does not execute the rest of the planned inventory.
Public Model.Load, `.glb_c` resource publication, Windows and matched compiled-vmdl
load/memory benchmarks remain root-level integration gates.

## Native vertex-color material regression, stage 901

The coordinator executed `VertexColorMaterialBindingProbe.Run` and
`BoxVertexColorsBlackDiagnostic.RunRegression` against Engine MVID
`881eb8b1-3500-40d7-98eb-25f21758fc6c`: **3/3 cases passed**, with no cleanup
errors. Receipt: `Temp/gltf-validation/direct-gpu/model-tests/b3f29947755d4ce5976d5e8c7c1426e0/receipt.json`.
This is a separate, narrow regression and does not execute the inventories above.

In the public `glTF/source` model, native readback matched all 24 positions,
normals, COLOR_0 values and bound-index topology; all reported errors were zero.
The original private 192-pixel baseline was solid black on its 13,718 model
pixels. The fresh baseline with the stage 901 adapter and revised shader is
multicolored; all 13,718 model pixels changed by more than four RGB byte values.
The fresh image is
`Temp/gltf-validation/direct-gpu/black-box-diagnostic/174c2ba45df24c05a746da0c33870dd3/public_baseline.png`
(SHA-256 `1ef1e1171282ce0f5f09be69070ab20132cb3dc4100c81432d20e3af670df424`).
The original comparison is
`Temp/gltf-validation/direct-gpu/black-box-diagnostic/31593d590c704f7fa747f22a862fb47e/public_baseline.png`
(SHA-256 `f763cfe9951b520e5b8ff8aa10c794cbac71fee5e0a389654395c686499dc0c3`).
The authored default metallic factor remains one; the metallic-zero images are
diagnostic controls, not substitutes for the public source-material result.

The implicit white `g_tColor2` binding matched an explicit white control byte
for byte. Source roughness-zero and roughness-one debug frames differed on
13,718 pixels and matched the stock complex shader's respective endpoints.
A base-color PNG with nonwhite factors and a metallic/roughness PNG produced
expected native packed-channel readback; retained material/texture lease and
default-off native sphere vmdl controls also passed. The private camera recorded
exposure bounds 1 and 2; an effective locked exposure was not established.

These checks do not prove independently referenced PBR color parity, filtering
or sampler fidelity, all COLOR_0 encodings, other material modes, or parity on
other graphics devices. The fresh 512-pixel corpus capture and user review are
separate pending gates.

## Model-owned test run, stage d0

The isolated model-worker run against Engine MVID
`1380fa97-c2ab-4935-bb84-41c5fb5510ff` completed **99/103** cases with clean
cleanup. Its four native material-binding and rendered regression cases passed,
including the synthetic-white fallback with a nondefault metallic-roughness
sampler and rejection of incompatible authored samplers. The failing cases are
the source and compiled forms of `COLOR_1` without `COLOR_0` and `COLOR_00`.
The reader's primitive path checks indexed semantic continuity and spelling,
but the upload-description path did not; the stage d0 result is retained as a
failure, not counted as acceptance.

Receipt:
`Temp/gltf-validation/direct-gpu/model-workers/boxvertexcolors-686ea85e9ba64302bdd7abc58dec7f85/tests-transport/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA-256 `de0c5b898ffc8d644b3505c7ad1a36b0600fa46295ead3a881dbc7ad05d5f065`).
A source correction shares the existing validation across both reader paths;
the integration owner built and installed that correction before the next run.

## Corrected model-owned run

The corrected Engine (`ad6a78a7e4f2664fe1f74b1567dde6d15d1519ad78dbd1e21efbb21ebfa2ebd1`)
and Gltf (`b86cb72e0c0e0f3c23af45bd115f68760655d1754d663f41df62a45e136f9e8b`)
completed **103/103** model-worker checks with no cleanup errors. This includes
the four formerly failing semantic cases, native material binding and sampler
fallback checks, a rendered vertex-color/roughness regression, and preparation
of a compiled input. Receipt:
`Temp/gltf-validation/direct-gpu/model-workers/boxvertexcolors-34f86e2c6b4a49dba62d3c262f406fb8/tests-transport/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA-256 `1df876459087f5eb2af5e795eb71a04efdef6a21020964f66551872f635c75a0`).

Isolated public `Model.Load` captures of the source glTF and the compiled
`.glb_c` both passed at 512×512 using the existing recipe. Both images have
SHA-256 `b8dcf31161c0d88200bdd2376dd75b176512f16e6b8be707b6fea4b3657759e6`:

- `Temp/gltf-validation/direct-gpu/model-workers/boxvertexcolors-34f86e2c6b4a49dba62d3c262f406fb8/captures/source/capture.png`
- `Temp/gltf-validation/direct-gpu/model-workers/boxvertexcolors-34f86e2c6b4a49dba62d3c262f406fb8/captures/compiled/capture.png`

The prior canonical capture was black on all 97,511 model pixels; the new
source image is visibly multicolored and all those pixels differ by more than
four RGB byte values. The requested exposure is not a verified lock. These
captures do not establish full PBR or sampler parity; user visual approval and
the publisher's later canonical Run remain pending. Capture/cleanup summary:
`Temp/gltf-validation/direct-gpu/model-workers/boxvertexcolors-34f86e2c6b4a49dba62d3c262f406fb8/captures-summary.json`
(SHA-256 `6b5f2394199acd6adbd4f6595ab5f3082c93d9a58323049bb6cd71390f443ff9`).
