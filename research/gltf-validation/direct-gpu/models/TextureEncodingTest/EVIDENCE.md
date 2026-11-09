# TextureEncodingTest execution evidence

The coordinator's terminal run
`Temp/gltf-validation/direct-gpu/texture-transfer-instancing-69/probe-01-editor_direct_gpu_tests/receipt-01.json`
has SHA-256
`ca0c3292debc880375790d87e546d9a1c9a6999cd9ad2584d21960bd9e0612db`.
It executed 69 total cases: the TextureEncoding Core28 and unrelated
SimpleInstancing Core40 passed, while the TextureEncoding GPU test failed.
There were zero incomplete cases and zero cleanup errors.

Actual producer identity was Engine D MVID
`04b86c2e-4406-422e-a466-b61ec13f1f86`, Gltf MVID
`1de213d3-1748-43d1-9a16-92a13bc9277d`, helper MVID
`eda1fc88-c00d-46fc-b284-55b689fdee2d`. The tested GPU helper source hash was
`3fdf4d043099bd71529d34232462ff07750e4b0c789c066c2647dc2964c3e49d`.

The GPU failure was `ShaderCompileException` for `anonymous/6.shader`:
`Program VFX_PROGRAM_VS not found`. It originated in the native shader
compilation route (`GameShaders.CompileInCache`, line 219). No texture sample
measurements were produced. This is a shader fixture grammar failure, not a
measured texture-transfer failure or pass.

After the coordinator released that freeze, only the diagnostic shader's
top-level HEADER/VS block layout was expanded to multiline native grammar.
The texture sampling expressions, resource compile roles, literal expected
values, sampler policy and acceptance tolerances were preserved. The helper
also records requested/compiled shader source hashes in the runner's existing
`Measurements` collection, so a compiler failure retains requested source
identity without presenting it as completed GPU output. The original receipt
and its failed helper hash remain unchanged. Later executed corrections and
their separate failures are recorded below.

Core28 success establishes encoded source/package identity, independent PNG
texels/components/alpha/origin, native resource pixel payloads/flags and the
existing base-color/float32-repacked metallic-roughness adapter behavior. It
does not establish direct source ORM, GPU color sampling, emissive/MASK source
materials, a managed public `Model.Load` draw, or final image fidelity.

## Installed c5c0 source preparation and cache execution

Actual c5c0 Engine MVID is `2c054bba-c0fa-43b5-a3a2-39a34ff7577f`,
Gltf MVID `c869761c-5b80-462d-982a-bc3a688e57b6`. The four actual
`SourceOrmResourceTests` rows passed in
`c5c0-skin-fixed-and-texture-65/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA-256 `e9a3bc6ad826740b93c7a29696e6abcd9c323de967aa55840095728bc2f7579d`).
These call the loaded `PrepareSource`, preserve exact source RGBA8 components,
distinguish color/data transfer flags, deduplicate matching role resources,
retain independent samplers and mip demand, and leave factors unbaked.
They are resource/payload evidence, not final material pixels.

The three cache fault-injection cases failed against fee5 because original
errors hid cleanup failures, then all three passed against c5c0. The red receipt
is `fee5-cache-failure-red-3/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA-256 `09d6ac599e15dddea81b0ef1b13024c25f1a846f222fee8f44424d9a40cfe914`);
the green receipt is
`c5c0-public-feature-first-114/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA-256 `2a00ccf3fb6374ce901d4029c614fbaf45e509dbe06cd6013a7a9fd2a02f768d`).

## Executed transfer precision failure

After the first grammar correction, the actual compiler reported `PS_INPUT`
undefined in the pixel stage. That separate compile failure remains in the
`e9a3bc6a...` receipt above. Moving the diagnostic stage interfaces into COMMON
then allowed actual shader compilation and sampling, helper source SHA-256
`12a35625d7ea8aae64f12afe4612bc270092f5ed3c93fb4d77736efb621e7312`.

`c5c0-public-cadence-material-diagnostics-25/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA-256 `165598de63db1add007c7ddedb19b5d4ecb7c95501302d5c117e68255ac20f7f`)
retains 58 transfer events/measurements: two shader events and 56 sample rows.
All 48 pinned model/form/role samples passed their numeric checks. The linear
RGB fixture samples also passed. The test then failed at RGB fixture pixel 3,
sRGB blue: encoded byte 201 gives analytic `0.5840784178911641`, whereas the
actual readback was `0.5859375`, beyond the unchanged `0.001` RGB bound. The
RGBA fixture's 8 alpha/origin samples remain unexecuted after that failure.

The dyadic values suggest limited precision somewhere along the conversion or
capture path, but this is an inference. `TextureEncodingPrecisionTests` is a
new, unrun diagnostic with literal float GPU-buffer output, explicit float32
EOTF, hardware SampleLevel/Load, actual native runtime descriptors and matching
RGBA32F clear/pixel captures. It records every row before asserting and retains
the original transfer bound. No tolerance or texture producer changed.

## Provided shader and default-handle evidence

The actual active graph/compiler Tools MVID
`81834be5-03d7-4d82-9b89-fe189665a884` generated the 55-node provided Lit graph.
The manifest SHA-256 is
`026afe68daaba1dadd5f983e59170f5bf37de9c9e08c3ebef75f7be1b2735f84`,
and the reviewed source SHA-256 is
`d988509c2c958680cec9d1334bd16869e39e0bbcd38ed12a4021c9c08c586366`.
Actual anonymous compilation and Material.Create succeeded. The two original
native-template cases then failed at `GetTexture` before any texel check in
receipt SHA-256
`98e4f877ab989a58d1579b2636c27a137883c6c074e003046a443f9442d459c0`.

The retained diagnostic version also failed both original assertions in the
`165598de...` receipt. Its six preceding measurements show HasParameter true
but GetTexture null for both `g_tGltfBaseColorTexture` and
`GltfBaseColorTexture`, on the template and its acquired material. The provided
white.vmat positive control exposes a valid 4x4 `g_tColor` texture. This
distinguishes parameter visibility but does not prove that shader default texels
are absent at draw time. No fallback texture was created to satisfy lookup.

The separately generated Unlit literal reference is source SHA-256
`bf7fb12b62ed825c71e5ae1f7b88ed97dd6f52d587c73c9b6123be563ecd96c8`,
manifest SHA-256
`fd5691d47789f4060320f828afb4656d8155422e69d6f69a086e102984f54034`.
Its reviewed actual vertex shader passes raw COLOR0, and its pixel shader
returns literal vertex RGB/alpha one without texture, factor or lighting.
`SourceOrmEmissionGpuTests` was initially prepared against that independent
program: omitted emissive texture plus three exact pinned PNG profiles, each
source and portable. It samples the actual Lit template through AcquireSource
and raw GPU triangles, with native empty/deleted and wrong-transfer controls.
All eight comparisons precede parity assertions. This pending gate does not
waive the GetTexture failures or advertise default/backend support. Its later
actual execution is recorded below.

The first combined emission/precision attempt stopped during helper compilation:
`c5c0-source-emission-and-precision-2/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `6b34634c84da37c51f24754853b7183806b74e1cca76fcba06217c7ec325fd47`.
There were zero executed cases and two incomplete cases. The precision helper
had four CS0103 errors because its `ResourceState` references lacked the
`Sandbox.Rendering` namespace import. There were no reported errors in the
emission helper. Adding that import is the only source correction; shader text,
thresholds, expected values and producers are unchanged. This compile-only
attempt establishes no GPU precision or material failure. The corrected
precision source SHA-256 is
`fe703f64ca1abe836afb40805f853182b1db464aeaeaee559d0e1dae7eefe23e`.

## Actual emission and precision differential

The corrected two-case run completed with two failures, zero incomplete cases
and zero cleanup errors:
`c5c0-source-emission-and-precision-fixed-2/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `abd55378afb65e4263454cf010a286927bdfa04561ca91c31ba8fbc206b38712`.
Its Engine/Gltf identity remains c5c0; helper MVID is
`513bb3de-5236-450c-8f69-86ad032dd6b5`.

All eight emission comparisons were retained. The six explicit texture cases
(plain/gAMA/iCCP, source/portable) have zero RGB difference and coverage IoU one
against the independent Unlit literal reference. Both omitted-texture cases
fail: expected interior RGB `(137,188,225)`, actual `(99,0,165)`, mean error
`95.33333333333333` bytes, maximum `188`, IoU one. The actual triangle is purple,
not empty or a target-clear sentinel. Empty-before/deleted-empty have zero
foreground; the wrong encoded-space reference differs by mean `17`, maximum
`51` bytes. The immutable image directory is
`Temp/gltf-validation/direct-gpu/c5c0-source-emission-proof-01` and its original
configuration SHA-256 remains
`52ba40fc5039dd518fbb76e204b8bfd992ca0be2125680c790d2213049514d49`.

The precision case retained all eight numeric rows and actual runtime texture
descriptors. Runtime target format is RGBA32323232F. Literal compute-buffer,
clear and raster output retain the supplied float32 values; raw linear samples
and explicit shader EOTF pass `1e-6`. Native sRGB SampleLevel and Load already
return blue `0.5859375` directly into the float buffer, matching the raster
result. Manual EOTF returns `0.5840786` versus analytic `0.5840784178911641`.
This differential rules out the color target/readback as the source of that
specific error. The original `.001` mathematical test stays recorded as failed;
API precision guarantees are being audited separately before classifying it as
a rendering defect. No compensating decode pipeline or tolerance change exists.

The missing-role error is separate and normative: glTF omitted base-color and
emissive textures multiply by one; omitted metallic/roughness G/B are also one.
After root review and a coordinator-cleared source window, exact patch SHA-256
`fb2d2b2fff93ac6fd51600b3811a8fe8a41c4c4d0c7e2e5411443717fa7be9bc`
was applied. Explicit source AcquireSource freezes four exact0/1 presence
scalars into its existing cache/readback path. The provided graph's dynamic
Branch selects neutral float4 one or the sampled texel before channels/factors;
SplitVector retains all four selected components. No default GPU textures or
static variants are allocated. Default Prepare/Acquire/backend routing is
unchanged. New graph SHA-256 is
`1f2860b53fe1c3a377515d915e19bcf12b872a12ae94ec5fa84e9a6436fcbb0e`
(67 nodes), adapter SHA-256
`642886c50bb08a1b4727c560a57d15e9b6eb46af173bb48d9aa619112a3e80e4`.
At that source freeze the correction was not installed or GPU-proven. The
following stage records the later installation and generation; actual image
acceptance still needs a fresh hashed configuration/output directory.

## Stage485 installed correction and actual generation review

Installed Engine MVID is `4181538e-896c-467c-966f-c8fcbf4f3282`, Gltf MVID
`df511f06-1c72-424c-a957-16a3fda208f6`. All four focused presence/cache-key
cases passed in
`stage485-targeted-92/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `f7aa221b0116150b48bd3b6cff4424b3516e4550b7094ffdfb26147c5d2dd2ef`.

The active graph/compiler changed to Tools MVID
`d7a9163f-8542-45c9-b8d3-b453802fd8f5`; old81834 provenance remains historical.
The actual 67-node generation passed, receipt
`stage485-source-generation-1/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `abcbc1722727983eef05b241c03442640d508e1d0221137051ad8ff538e11c96`.
Its generation manifest SHA-256 is
`47fc2227dd3cbf0afe88b3aabdf11cc81acef642d0548e225bc3d6bbf807af3d`,
emitted source SHA-256
`6648ccfa4486ef7ad79add3157661398ef7bb3f94e65a6403970ca33dae6bf45`.

Review of that exact emitted HLSL confirms four float4 conditional values:
BaseColor local12, Emissive local32, MetallicRoughness local49 and Occlusion
local66 choose sampled RGBA when the corresponding presence scalar is greater
than zero, otherwise float4 one. Base/Emissive use selected XYZ before factors;
roughness uses selected Y, metallic Z, occlusion X. Typed sampler attributes,
role transfer flags, independent affine UV selection, opaque alpha and the
provided Forward/Depth/Standard route are preserved. Sampling appears before
the value selection, so this is not a promise to skip the texture fetch. It
does avoid an arithmetic lerp of an unselected NaN into the neutral value.

The reviewed source is ready for bounded anonymous compilation. Native
presence observations and the unchanged eight emission image comparisons are
still pending. Their independent Unlit reference must first be regenerated
under actual current Tools d7a9163f; the old81834 manifest must not be accepted
by bypassing the producer identity guard. Original config52ba and proof01 image
directory remain immutable.

The next actual two-case receipt,
`stage485-presence-and-literal-2/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `e493fcdc57f219f5dd313142bf11b95bacb68a31ab8ffc767a7ce32e4880e4fa`,
passed both cases. Native Surface6648 anonymous compilation succeeded and all
twelve presence mask/form observations retained exact0/1 values, native cache
sharing and live-peer lifetime. The independent literal was actually generated
again under current Tools d7a9163f, manifest SHA-256
`f8afe8ba7bb00cc7c0e635053f9eadb912cec92e47bbe872b6595df6b241e8a8`.
Its shader bytes remain identical to reviewed bf7fb12b; no producer identity
guard was bypassed.

The unchanged emission helper698f is ready with new configuration SHA-256
`58c63a78dd8bdb6aa747300626ab3c82c15e11aac5ce7c399c699b4072ffe9f5`,
which pins current manifests and unique fresh output
`stage485-source-emission-proof-a7d007ba82714d149a89351113160960`.
The request SHA-256 is
`fd601c03922b994a489a2c171afd97b00955f98432ad31fbbf92746c6e0d0c70`.
All eight image comparisons and thresholds are unchanged. The coordinator did
not select/admit this run before root requested a closure audit with no new
tests/probes/builds. Its status is READY/UNRUN, and the fresh output directory
remains absent. The last actual emission result is still abd553: six explicit
texture comparisons exact and two omitted-role failures on the old pre-fix
producer. Corrected-source image acceptance remains pending.
