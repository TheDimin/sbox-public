# TextureSettingsTest sampler acceptance

Pinned corpus revision: `90d7ede14c7e280af263824604b427a1ca02cb66`.
Only actual upstream `glTF`, `glTF-Binary`, and `glTF-Embedded` variants are used;
each has a separately derived compiled form. The reference verifies all seven
input/dependency SHA-256 hashes before decoding. The source's CC-BY-4.0 license
remains in the upstream model directory; no texture is copied here.

The five authored samplers all use minification `NEAREST_MIPMAP_LINEAR` (9986)
and magnification `LINEAR` (9729). Their independent S/T pairs are repeat/repeat,
repeat/clamp, clamp/repeat, repeat/mirror, and mirror/repeat. Nine textures share
three images; material 7 is double-sided, material 8 explicitly single-sided.
The six sampler test meshes deliberately use negative T or S greater than one.
The first source triangle is in each test column; all twelve incorrect wrap
choices produce a visibly different result with the pinned image and UVs.

The normative baseline is the published [glTF 2.0.1 sampling specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#samplers),
registry commit `8e798b02d254cea97659a333cfcb20875b62bdd4`. Independent minification,
magnification, mip selection and U/V addressing are distinct requirements.
Base-color filtering operates on decoded linear RGB. An absent filter is a
caller policy, not an invented required glTF default. Non-mip minification
requires a one-level texture/view; the known Vulkan MaxLod=0 behavior is not a
substitute when minification and magnification differ.

## Frozen gates and limits

- `TextureSettingsTests`: 21 CPU cases. Six source/compiled forms each validate
  materials/sampler/image bytes and all geometry/index/UV values. Six independent
  real test-column controls reject each incorrect wrap mode, two image controls
  distinguish nearest/linear, and one literal edge test verifies negative and
  mirrored texel addressing. Full geometry uses an independent direct binary
  decoder, never producer output as expected data.
- `SamplerAttributeGenerationTests.RunAll`: 12 active-editor graph cases,
  including both VS/PS stages, unchanged omitted-attribute declarations, named
  binding generation/serialization, same-binding deduplication, distinct-binding
  separation and malformed-name rejection. Empty graphs call actual supplied
  `ResultTexture` and `GenerateGlobals`, without creating texture nodes or
  requesting native texture/shader compilation. Initial missing-property results
  must be retained before changing provided graph code.
- `TextureSettingsSamplerGpuTests`: one asynchronous diagnostic case with 102
  float-target sample readbacks. It uses the pinned authored PNGs through the
  actual native texture compiler/cache, independent PNG decoding and first-mip
  arithmetic. All twelve min/mag pairs, all nine independent wrap pairs, and
  twelve actual source-triangle wrong-wrap controls are checked. Two sampler
  roles deliberately receive conflicting states; switching roles must select
  the corresponding expected texel and switching back must restore it.
  Maximum RGBA error is .04; actual-source negative controls differ by >.1.
  Source image edge witnesses enforce sensitivity before any draw. The anonymous
  shader hash is explicitly supplied by the coordinator. Material attributes
  are explicitly merged for this candidate; that is an integration requirement,
  not evidence that the present raw backend does it.

All execution is owned by the validation coordinator. Authoring and the small
independent Python/Pillow sensitivity cross-check are not engine test passes.
Sampler pixel diagnostics do not establish provided Surface shader integration,
whole-model image parity, model double-sided appearance, native vmdl regression,
public `Model.Load`, Windows behavior, or performance. Those remain separate
acceptance gates after producer integration.

## Production integration boundary

Use provided ShaderGraph generation with an optional generic sampler attribute
name on each sampler declaration. The graph compiler does not know glTF names.
The model adapter supplies exact `GltfSamplerMapping.State` descriptions through
material sampler attributes; the raw backend merges those attributes for each
draw under a fresh graphics scope. Never leave material A's sampler in material
B's draw. There is no scalar descriptor-index assumption and no runtime graph
compilation in `Model.Load`.

Suggested adapter bindings, one per canonical UV/encoding role:

| Role | Sampler attribute |
|---|---|
| BaseColor | `GltfBaseColorSampler` |
| MetallicRoughness | `GltfMetallicRoughnessSampler` |
| Normal | `GltfNormalSampler` |
| Occlusion | `GltfOcclusionSampler` |
| Emissive | `GltfEmissiveSampler` |

A shared source image may have different samplers/UV transforms/color transfer
across roles. Keep the direct source ORM channels and factors rather than
creating two float32 repacked texture chains. Standard/Unlit surfaces use raw
linear COLOR0 (default white), decoded base texture and linear base factor once.

## Coordinator result, 5 November 2026

Receipt `7a21a72f339b1f20e16732200b986e909039b2a58dc25d87f0e72082e09ba765`
ran all 21 TextureSettings CPU cases successfully. Its 12 graph prerequisite
rows failed in the helper's ambiguous TypeLibrary reflection lookup before
feature checks. The corrected helper selects the non-generic two-argument
lookup explicitly (SHA-256
`7c9c9aec404f3a3875e4aae934500e0e0149c8e3346e7ba539062b0c502998c5`).
The corrected baseline has not executed: the coordinator's next preflight found
the user in play mode and deferred it. No shared graph source has been changed,
no play state has been stopped, and no sampler pixel pass is claimed.

The independent author-side check also confirms sensitivity of the GPU filter
witnesses on `CheckAndX.png` (512×512). At UV
`(.34228515625, .7646484375)`, nearest versus linear magnification differs by
`.2328804575` in the largest linear RGB channel. At
`(.28955078125, .7529296875)`, base versus first-mip nearest sampling differs by
`.6597914650`; therefore a `.25` LOD blend differs from mip-nearest by
`.1649478662`. These exceed the `.04` pixel tolerance without relying on a
source-independent checkerboard. They remain oracle-authoring evidence until
actual GPU readbacks are recorded.

## Corrected prerequisite and prepared implementation, 6 November 2026

The corrected generation baseline executed on the coordinator's new idle
producer. Receipt
`cf229f85cd7b0e48fc6edcd8ac8d45e01ecb56153247f90f907072701df81b75`
contains two successful unchanged-legacy cases and ten actual failures because
the active sampler has no optional `Attribute` property. This releases the
feature implementation prerequisite; the earlier helper failure remains retained.

The approved unwatched patch is `sampler-attribute.patch`, SHA-256
`669461292907c170961ad7f0a9491cd9294eae7a1167c25bfd6058d0ea993555`.
Its adjacent JSON manifest records exact before/after identities and BOM/CRLF
preservation. The coordinator alone applies it at a guarded idle boundary.
`git apply --check` succeeded without modifying either watched file. Null and
empty names preserve the old serialized graph and emitted declaration. Nonempty
names must be ASCII identifiers; validation precedes graph registration and
shader emission. Binding names participate in existing sampler equality/dedup.

Four separately frozen compatibility MSTests additionally cover omitted/empty
JSON and byte-identical shader source, whitespace-independent omission checks,
case-sensitive identity, underscore/digit names, and rejection without partial
sampler/texture registration. They supplement the unchanged twelve baseline
cases. Application, addon hotload, all sixteen followup cases and the actual
102-readback sampler gate remain pending coordinator execution.

## Applied sampler extension and retained GPU failure

The coordinator applied the approved patch under the resource guard. Active
sampler assembly MVID `81834be5-03d7-4d82-9b89-fe189665a884` exposes `Attribute`;
all twelve generation and four compatibility cases passed, receipt
`13fc2b7232e0ae062307790b1cd75ad32793a9863db7d4324ef823881ce2d3f8`.
This is actual graph generation/serialization evidence, not a sampler pixel pass.

Receipt `454761de8fe6263661da49a84989385a14f6d991a98c279071a998b4732b5716`
reached seventeen sampler observations: the first sixteen base-only resource
samples passed; the first full-mip resource failed at min=9984, mag=9728, lod=-1,
UV `(.34228515625,.7646484375)`. Expected white was observed as
`(.3515625,.76953125,.3515625,1)`, max error `.6484375`. The test and tolerance
remain unchanged. Independently decoded source mip1 contains `(160,227,160,255)`
at that coordinate, whose linear RGB matches the failed observation within
hardware rounding. This narrows the question to a level/view/residency shift;
it does not by itself establish its cause.

`TextureSettingsMipDiagnosticTests` validates every serialized mip byte, then
compares native full-mip and base-only resources with explicit static GPU upload
of the same independent mip chain. The diagnostic reads shader dimensions,
explicit Load(mip0/mip1), SampleLevel(0), and the original SampleGrad request
before and after bounded real-frame/MarkUsed observations. CPU-side native
GetPixels is deferred until the final phase so it cannot force residency during
that differential. A pass still requires the full 512×512, ten-level view and
the original authored white pixel; coarse fallback never satisfies the gate.

## Actual GPU readiness differential

Receipt `165598de63db1add007c7ddedb19b5d4ecb7c95501302d5c117e68255ac20f7f`
proves the full native byte chain matches the independent PNG/mip oracle. The
initial native GPU view was 256×256 with nine levels at actual frames 0, 2 and 5.
Explicit Load(0), SampleLevel(0) and the original SampleGrad all returned source
mip1. After MarkUsed(512) and real frames, frame 17 (about 383 ms) exposed
512×512 with ten levels; those three operations then returned the authored
white mip0 texel. At frame64, native GetPixels also returned exact expected
encoded mip0 and mip1 texels. Explicit static upload of the identical chain
exposed the full view immediately.

The separate fresh scoped-preload control failed: both default and immediate
force-preloaded views were 128×128/eight levels on the same application frame.
This retained failure means the scoped preload flag is not an immediate GPU
readiness guarantee. It is not evidence of corrupted source bytes or a reason
to alter the expected sampler output.

The sampling harness therefore adds an explicit test precondition: request the
actual source dimensions with MarkUsed, and require exact GPU GetDimensions for
every native texture on two distinct real frames, under a ten-second deadline.
Every observation records per-texture expected/actual dimensions and levels,
frame counts and elapsed time, including on timeout. Original shader, 102
numeric sampling requests, independent oracles and `.04` thresholds stay
unchanged. Readiness timeout remains a failure. Production streaming policy,
streaming latency and load/memory performance are separate from sampler
correctness; this test does not disable streaming globally.

## Actual sampler realization accepted, 6 November 2026

Receipt `c97866b304711f7c8da2f8e64b0ca37c2f588434251bf05fb90e6823a0e14d2c`
executed the corrected readiness-precondition harness successfully: **102 of
102 actual GPU sampling requests passed**, with maximum RGBA channel error
`.0026040524` under the unchanged `.04` limit. The sampler candidate shader is
still `e9c8f6057c1135d2ed6256c0a23cadb457d63863b96e18c9ca78cf8c59499ece`.
All numeric oracles and source witnesses remain unchanged.

The 118 measurement rows are 102 sampling observations, fifteen per-texture
GPU-readiness observations, and one declared sampling plan. Both full-chain
textures initially exposed 256×256/nine levels. Complete 512×512/ten-level
views were first observed at elapsed frame9 (about92ms) and confirmed on
elapsed frame12 (about118ms). The base-only texture exposed512×512/one level
throughout. This is bounded readiness evidence for these source textures,
not a general streaming latency benchmark or production policy change.

Together with the 21 actual-model CPU cases and sixteen active provided-graph
generation/compatibility cases, this establishes the optional typed sampler
attribute route: all twelve min/mag combinations, independent nine S/T wrap
pairs, the real TextureSettings source test-triangle negative controls and
separate sampler-role bindings operate correctly after actual GPU residency is
verified. It does **not** establish stock-complex default sampler overrides,
the complete provided Surface material, whole-model pixels/backface visibility,
public `Model.Load`, Windows, or load/memory performance. Those gates remain
separate. The original failure454761 and failed immediate-preload control165598
remain retained as evidence; neither was relabeled a pass.

`../../../../../engine/Tools/Gltf/direct-gpu/models/TextureSettingsTest/TextureSettingsGpuChecks.cs` is an authored, currently unexecuted helper for a
future whole-model capture. It checks the source's ten nodes,144 vertices and
216 actual GPU index values, literal root/basis transforms, normal directions,
and unmodified out-of-range UVs. It reports errors without inventing a final
numeric acceptance threshold or claiming image parity.
