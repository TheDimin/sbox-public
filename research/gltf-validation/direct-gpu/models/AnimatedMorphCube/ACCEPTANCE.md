# AnimatedMorphCube direct GPU acceptance

Status: public `Model.Load` plus `SkinnedModelRenderer` now draws and reads back
three authored keys in the original `glTF` source and its actual compiled GLB.
The path uses C# owned GPU buffers and the installed named compute shader.
The original-source public path has also completed two monotonic playback loops
and a verified 252-frame lossless APNG. Bounded original-source movie numerical
limits passed. Matched reference appearance and the other actual upstream GPU
format matrices remain pending.
The earlier native-Model animation receipts remain historical evidence only.

The actual physical-editor helper rerun against loaded `Sandbox.Gltf`
`1de213d3-1748-43d1-9a16-92a13bc9277d` passed all 23 reader-upload, ten shared clock
and three morph-binding cases. Five separately owned backend groups made that
focused run 41/41. The receipt is
`Temp/gltf-validation/direct-gpu/morph-reader-fixture-corrected/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA256 `c422cb799669645e319ae7def6b145d43fef738fe8ddf629f88614201f7535de`).
The loaded reader producer snapshot was `494227079c…`; the working copy's later
`602c3a49d8…` change corrects a comment only. The initial 35/36 failure receipt is
retained: one test wrongly assumed source buffers resolve lazily, but the document
constructor eagerly resolves them. The corrected test separates source resolution
from the upload budget without changing strict budget/unique-storage checks.
These are source/CPU test results and do not demonstrate a rendered GPU model.

The eight tiny media codec fixture tests passed separately; they exercise actual
rational APNG timing/CRC and decoded pixel identity, not engine rendering. The
record is `Temp/gltf-validation/direct-gpu/reference-generation/morph-media-codec-tests.json`
(SHA256 `a27840ef6f4440765cca0b1d76399294e5efa6111fd647f3bbdab9d5f35c3919`).
The shared writer now derives every duration from recorded source timestamps
instead of rounding to one integer millisecond interval. Actual model captures
are recorded below; synthetic codec fixtures are never published as model evidence.

The reference corpus is Khronos glTF-Sample-Assets revision
`90d7ede14c7e280af263824604b427a1ca02cb66`. Cover original `glTF`, `glTF-Binary`
and `glTF-Quantized`, plus the actual portable `.glb_c` generated from each source.
Keep the sample's license and full dependency hashes in the existing corpus harness.

## Independently established expectations

- The original source has 24 vertices, 36 indices, two morph targets and a
  single `Square` LINEAR weights clip. It has 127 keys, with last input
  `4.199997425079346` seconds. Authored key spacing is approximately 30 Hz;
  interpolation remains continuous between these keys.
- The original and binary variants provide position, normal and tangent target
  streams. The quantized variant provides signed target formats and a different
  leaf/parent transform hierarchy. Each variant must use its own raw descriptors
  and exact hierarchy. Source tangents are VEC4 while tangent deltas are VEC3;
  handedness must not receive a morph delta.
- Source `(x,y,z)` meters map to engine `(z,x,y)/0.0254` inches once. Delta blending
  occurs before node/skin matrices; transformed normals use inverse transpose.
  Signed weights are not clamped to `[0,1]` or normalized.
- The old published 16-state movie with 262 ms frame delays presents approximately
  3.82 frames/second. That movie does not prove smooth playback or incorrect clip
  interpolation. The old preview's capped elapsed time also drops real time
  after stalls. The new shared player retains all elapsed time in a double clock;
  rendering callbacks never advance it.

The original source and binary source have unit tangents `(1,0,0)` even on their
`±X` normal faces. The retained source diagnostic established the first failure
at original `glTF`, time zero, vertex 8, weights `[0,0]`: local normal `(-1,0,0)`,
base tangent `(1,0,0,1)`, both target tangent deltas zero, transformed tangent
`(-100,0,0)` and projected tangent exactly zero. This is an undefined authored
tangent frame at rest, with no animation or decoder failure. The diagnostic is
`Temp/gltf-validation/direct-gpu/reference-generation/morph-tangent-diagnostic.json`
(SHA256 `aff9d9f444f3ca0fc2ddfd197ca08feeebb97b85185cf8534afc46d97563bab2`).
The failed full-author artifact and preceding reference JSON remain retained.

The source is not excluded: core specifies unit tangent XYZ, signed W and
`cross(N,T)*W` bitangents without an explicit orthogonality requirement. Only an
exactly undefined per-vertex direction lacks an independent XYZ expectation.
The corrected source oracle records that diagnosis and preserves handedness;
the GPU gate still requires finite, unit, perpendicular fallback output and exact
W there. All other directions, positions, normals, topology and clocks retain
their checks. A near-parallel but nonzero direction is not waived. Source-oracle
regressions and checker sensitivity tests cover this distinction.

The corrected author `4c7a31b928…` executed successfully and produced reference
JSON SHA256 `d18dd9c6f5acf94b5c29001c6501bf8d5249fcc52214a91b3e0283ee2bcd65b0`:
three source variants, each with 253 key/interior reference samples and 252 planned
movie times. The five source-oracle regressions passed. Against the actual loaded
reader, all nine model-specific source/CPU rows and all eight synthetic verifier
controls passed in
`Temp/gltf-validation/direct-gpu/cube-core-verifier-fox-reference-first/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA256 `9a156ed2f68675a0b4e19249ff59b738ced317ed6e6262685d67b0920e3eafbf`).
That combined receipt is 18/20 overall because two separately owned Fox oracle
controls remained red; it is not an all-green suite. These 17 Cube rows do not
demonstrate Cube GPU drawing, rendered comparison or a captured 60 Hz movie.

## Executed original-source GPU smoke

`AnimatedMorphCubeThreeAuthoredKeysUseCurrentGpuOutputs` passed in
`Temp/gltf-validation/direct-gpu/three-model-smokes-source-fitted/probe-01-editor_direct_gpu_tests/receipt-01.json`
(SHA256 `79e57fbdfd955db7333b4294601beb8af7921cbe81a5cdfa404e8445c06c96e6`).
It used the independent fitted bounds, stock-adapter source material, actual GPU
vertex/index readbacks and fresh empty-before/deleted-empty controls. All 24
vertices and 36 indices retained their source mapping. The authored start, key 63
and key 126 produced maximum position errors of `3.8146973e-6`, `3.8146973e-6`
and `7.6293945e-6` inches; normal error stayed below `6.15e-8`. All 16 defined
tangent directions had zero measured direction error; eight explicitly undefined
directions retained finite/unit/orthogonal output and exact handedness. Actual
start-to-middle captures differed at 7,510 pixels.

These are measured errors, not approval of the full numerical/image matrix. The
combined receipt is 14/26 overall due to twelve separately owned Fox core-oracle
failures, while the three model smoke methods passed. Do not label the whole
receipt green. The Cube stills have visually clipped white interiors under the
recorded high-intensity lights; the coordinator was asked to calibrate lighting
in-engine before claiming material appearance or capturing the dense movie.

## Shared signed clock implementation awaiting combined stage

The expanded 20-case player suite first executed against the old research player:
10 passed and 10 failed for finite signed rates/deltas/seeks and unnecessary
zero-delta/rate pose evaluation. The implementation now delegates to one internal
Engine `GltfPlaybackClock`, also used by the existing sequence implementation.
Its 26 pure cases cover signed arithmetic, seeks that clamp even in loop mode,
directional finish, zero movement, overflow rollback, independent clocks, warm
allocation-free advancement and precision at positive/negative loop boundaries.
An exact reverse correction that rounds to Duration retains the existing second-
modulo zero-endpoint policy; small positive phases avoid adding a large period.

The combined Engine stage is installed and the normal public editor clock controls
described below passed. The separate 26-kernel/20-player suite still needs its
own execution receipt. Source frame zero reuses the selected pose,
then each nonzero advancement evaluates one shared pose. This source update does
not retroactively change the prior executed helper results.

The callable `AnimatedMorphCubeMovieProbe.Capture` samples all 252 actual planned
states plus a separately recorded, unencoded endpoint. Its explicit numeric gate
adapter emits the shared APNG writer schema; it does not invent frame pixels or
claim wallclock/API verification. `AnimatedMorphCubeWallClockProbe.RunVisible`
observes an existing visible widget with monotonic time at a declared signed rate
for multiple loops and records actual final GPU submission. The completed public
capture and cadence executions are recorded below.

## Public morph and normal editor clock integration

The new reader `GltfPose.WithMorphWeights` creates an immutable same-owner pose
snapshot with validated per-node weight replacements. Supplied arrays are copied;
signed finite weights remain unbounded, other nodes and world matrices retain
their values, and source defaults remain unchanged. Its 25 focused cases first
executed as real missing-API reds against fee5, alongside twelve real public
morph reds. The retained receipt SHA256 is
`cf229f85cd7b0e48fc6edcd8ac8d45e01ecb56153247f90f907072701df81b75`.
All 25 reader and twelve public morph cases then passed against actual installed
Engine `2c054bba-c0fa-43b5-a3a2-39a34ff7577f` and Gltf
`c869761c-5b80-462d-982a-bc3a688e57b6` in receipt
`2a00ccf3fb6374ce901d4029c614fbaf45e509dbe06cd6013a7a9fd2a02f768d`.
This statement covers these 37 cases, not the unrelated rows in that 114-case run.

`AnimatedMorphCubePublicMorphTests.Run(checkout,referencePath)` includes twelve
positive manual-morph rows within its eighteen-row suite: six cases in the
original `glTF` source and its actual compiled GLB. They load through `Model.Load`, create the ordinary
`SkinnedModelRenderer`, and use its existing sequence and `Morphs` APIs. They
require two logical target names, signed zero-fade overrides, clearing back to
the current animation, independent instances, atomic rejection of nonfinite
weights and retaining one target while another is cleared. Read-only diagnostics
check that the component's immutable pose is the same object supplied to its
actual GPU instance, so a dictionary-only setter cannot pass. These cases do not
by themselves prove GPU submission or rendered pixels. Default timed morph fades
and their native curve comparison remain separate gates.

The same public suite was then extended to eighteen rows. Six new controls run
the ordinary `Scene.EditorTick`: full `0.75 + 0.625` seconds, signed reverse
multiloop phase, and a zero-rate frame that advances the real animation stage
without replacing its pose. The expected weights come from independent source
keys. All eighteen passed in receipt
`98e4f877ab989a58d1579b2636c27a137883c6c074e003046a443f9442d459c0`.
Two separately owned template-material tests were red in that 20-case run; the
whole run is not labeled green. This is editor-stage/pose evidence, not real time.

Public source and compiled three-key GPU capture then passed in receipt
`66ac4a3006173a0b9359591738cad7e85a264071cadf0ef1593d43f037a80315`.
The retained manifest is
`Temp/gltf-validation/direct-gpu/public-model-smokes/AnimatedMorphCube/latest-evidence.json`
(SHA256 `967dbd6ea9f9e8ecde4e1f6484f1dddd2c0e82e01b7824643a35cc67e9380b02`).
All six states have actual current-revision vertex/index readbacks. Maximum
position error was `7.6293945e-6` inches, normal error `6.143906e-8`, and defined
tangent direction/handedness errors zero. Each form changed 13,590 pixels between
start and middle; capture coverages were `0 / 9778 / 7134 / 9778 / 0`, including
fresh empty/deletion controls. The actual named compute shader was nonanonymous;
native Renderer MVID was `c22b305b-109d-4166-a212-a7d4bc301e40` and compiled shader
SHA256 `f15a8f245dd6d9f0293ee895c54a5f46a1b41eb094140153f1a5f7539178e5fc`.
The source captures show shaded faces and clear deformation using the stock
complex material. Matched independent images/material appearance and explicit
numerical approval remain separate gates.

`RunCapturedCadence` passed actual monotonic public preview playback.
It uses the same public fixture and native capture controls, configures normal
editor animation, and calls `Scene.EditorTick` from complete Stopwatch callback
gaps. After initial reset it does not set sequence time or use a helper player.
It records input float conversion, public clock ULPs and double arithmetic bounds.
There were 848 observations over `8.407102248` seconds, covering two authored
loops at rate one. Maximum actual callback gap was `0.032611475` seconds and
maximum phase error `2.3848583907692955e-7` seconds. The final actual GPU revision
was `854`, matching its submitted revision; draw count was `2580`. The retained
manifest is `Temp/gltf-validation/direct-gpu/public-model-smokes/AnimatedMorphCube/latest-cadence-evidence.json`
(SHA256 `3620384aa2b8f4e8286e2a4b8975d327b0973f372cfe6e6b9fbe0ba216d4d7fa`),
from receipt SHA256 `165598de63db1add007c7ddedb19b5d4ecb7c95501302d5c117e68255ac20f7f`.
Sixteen separately owned material/tiny-scale controls were red in that run;
this does not label the whole run green. This is real public clock/submission
proof, not measured presentation FPS or a wall-clock video. No deliberate live
stall was injected; the separate real-editor unit controls exercised `0.75` and
`0.625` second scene deltas without capping them.

`RunCapturedMovie` reuses the same public fixture, native capture controls
and shared recording implementation. It prescribes normal `Scene.EditorTick`
deltas for 252 real captured states and a separate endpoint, preserving the
independent source oracle. Each captured actual vertex/index buffer is retained
with format/stride metadata and hashes; the CPU source-vertex map is labeled as
CPU provenance. Public and diagnostic-player recordings have distinct route
flags. Explicit numerical limits and lossless container verification are still
required before publication. Before dense capture, the coordinator compared
owned point-light intensities `4`, `1` and `0.25` over the same three authored keys.
All nine states passed with unchanged cameras and GPU bytes. Intensity `4` had no
pixels with all RGB channels at least `254`, and p99 brightness `242` at start and
`176` at middle; the unchanged point-only intensity `4` was selected. The receipt
SHA256 is `df4e6ab96677f0c6100dc86059725d6a265a49332c1db5fa6cb65f1f4429bd91`.
No common capture framework, shader or producer source changed for calibration.

## Executed original-source public movie

Actual capture passed in receipt
`79acddca38896af76cecc65e3b099a02f80cfacad51d99c7cc05cb7e48aa5530`.
It retained 252 scheduled states plus a separate unencoded endpoint: 253 actual
PNG files and 506 actual GPU vertex/index files. Position error peaked at
`3.8146973e-5` inches and normal error at `3.8462608e-7`; all tangent direction,
length, normal-dot and handedness errors were zero. Coverage ranged from 6,137
to 9,778 pixels. Maximum public-clock error was `4.4504801444134046e-7` seconds,
within its recorded float-transport bound `6.952669266979683e-7` seconds.
The 24.46 seconds spent collecting images is distinct from source playback time.

Root explicitly approved position `1e-4` inches and normal/tangent direction,
length and normal-dot `1e-5` for this bounded original-source public transport;
handedness remains exact. `ToWriterCase` passed those limits and rechecked all
retained GPU files; every captured PNG was also rehashed. Its acceptance receipt
is `679297d0f082837e231f64ab5be74fa88dacfca424825ce2bbd0def617d3f4ff`.
These limits do not replace another model's or source-format matrix's gates.

The unchanged shared writer produced 252 encoded frames from 252 actual input
frames. The lossless APNG duration is `4.199997424974802` seconds against authored
`4.199997425079346`, with maximum cumulative hold-time error
`1.0454415689022545e-10` seconds. The first delay is `1/60`; the final partial delay
is `755/45307`. All composited pixels, PNG CRCs and rational timing were verified.

The portable browser animation is
`Temp/gltf-validation/direct-gpu/public-model-smokes/AnimatedMorphCube/22cc980bc9bc40459e3448d38f1f1d20/public-apng/AnimatedMorphCube_glTF_source_clip0.png`
(526,397 bytes; SHA256 `82aef7f4781b11ac3725f2535e2025e3f7ea830a956a209a2a63aa199d067509`).
Its exact original first-frame poster is `poster-2d04429b8ffddb0a75d05f86e4daf5916638a50250c7a43a9d7b6c3e3a652252.png`
in the same directory. The manifest SHA256 is
`deca30a39c332d96f1ce503e90ca7ea724d9a06f3e01707021a42022c5e4a26a`;
its accepted wrapper receipt SHA256 is
`2db4451217b7f1cb0465417e20106f31f37bdfe550b434ae5197767fa277f0ff`.
The full recording SHA256 is
`12dc7c16c7bbb22a54b3483d15b1ef41167e159dcb5588ed5010457cd565276c`.

This is a deterministic public `Model.Load`/`SkinnedModelRenderer` movie using
ordinary prescribed `Scene.EditorTick` deltas. It is not relabeled as real-time
video or a presentation-FPS benchmark. The completed two-loop monotonic run is
separate evidence. Unknown seek/reset, simultaneous live instance and final
material-reference comparisons are not claimed as movie passes. The upstream
binary/quantized GPU matrices and complete compiled-movie matrix remain pending.

`AnimatedMorphCubePublicGpuProbe` separately prepares ten public-command GPU
observations and three independent-instance observations. Its expected data come
from nine explicit raw-source manual samples per actual source variant, including
weights `-2,-1,0,1,2`, partial overrides at distinct authored keys, and signed
values `-0.5` and `2.5`. The source author preserves the preceding authored and
movie sections. Seven new controls are prepared to execute first against the
preserved old author without those samples, then against the new source before
reference regeneration. No new reference JSON or GPU result has been generated
by the model worker. The probe checks actual submitted revisions and vertex/index
bytes; its separate `RequireWithin` method requires explicit reviewed limits.

## Preparation and pure checks

The coordinator generates the source oracle without calling Sandbox.Gltf or the
renderer:

```sh
python3 engine/Tools/Gltf/direct-gpu/models/AnimatedMorphCube/author_reference.py \
  --sample-root Temp/gltf-reference/glTF-Sample-Assets \
  --output Temp/gltf-validation/direct-gpu/AnimatedMorphCube-reference.json
```

The author checks the actual Git HEAD. It decodes each source's views/accessors,
normalization, sparse data and transforms independently, records hashes, all
authored keys and midpoint expectations, and a 60 Hz requested-time schedule.
The APNG duration plan uses bounded rational numerator/denominator pairs for each
actual captured frame, including the partial final interval. Its total must remain
within 0.1 microsecond of the authored duration. It produces reference data only.

`AnimatedMorphCubeCoreTests.RunAll(sampleRoot,referencePath)` checks all three
source variants' exact raw upload view hashes, count/format/normalization/offset/
stride/provenance, independent key/interior poses, and the high-cadence clock
schedule. This is CPU/source evidence, separate from GPU and wallclock proof.

The 23 `GltfMorphUploadDataTests` cases cover shared interleaved views, source vs
owned offsets, sparse/interleaved/implicit-zero copies, alias deduplication,
preflight budgets, post-document lifetime, signed raw quantized formats, omitted
semantics, nonfinite components vs unused padding, and invalid data. Optional
TEXCOORD/COLOR/custom semantics are preserved as descriptors without an assertion
that the renderer supports them. The ten shared player cases and three morph
staging cases cover the uncapped clock, independent immutable poses, endpoint/
loop/pause/seek behavior, and signed weights.

Format rules are based on the published
[glTF 2.0.1 morph section](https://github.com/KhronosGroup/glTF/blob/8e798b02/specification/2.0/Specification.adoc#morph-targets)
and [primitive schema](https://github.com/KhronosGroup/glTF/blob/8e798b02/specification/2.0/schema/mesh.primitive.schema.json).
The schema permits empty target objects; the target array itself must be nonempty.
Omitted target semantics retain their base values. Quantized target formats follow
the selected ratified `KHR_mesh_quantization` specification; extension maturity and
version auditing remain part of the broader extension acceptance.

## GPU and final-image proof

1. Revalidate the physical editor/project/MVID and guard. First pass the shared
   raw-triangle controls with fresh empty-before, positive geometry and deleted-
   empty-after. Use the accepted native presentation route: explicit camera size,
   Normal debug mode, normal postprocessing and an owned stock light. The failed
   Albedo capture routes cannot certify this model.
2. Build the shared asset using real `GltfDocument` source upload data. Draw through
   C# owned GPU buffers, explicit compute/write-to-vertex-read barriers, runtime
   vertex declarations and SceneCustomObject presentation. Construct no native
   Model, Mesh, model builder or native morph controllers.
3. For each source and compiled form, compare completed GPU readbacks at rest,
   every authored key, between keys and a dense playback schedule. Check position,
   normals, original vertex mapping and winding-preserving topology against the
   independent source oracle. Tangent and handedness correctness are separate
   required rows; position-only success is insufficient.
4. `AnimatedMorphCubeGpuChecks.Measure` reports numerical errors without selecting
   tolerances. Establish tolerances from measured precision; do not waive a
   discrepancy to make a pass. Its reference draw helper uses literal CPU oracle
   triangles only for a separate diagnostic draw, never as actual model input.
   Record default stock material rendering separately from diagnostic material
   comparisons. A successful readback does not prove a correct final image.
5. Exercise two instances at different phases, nonuniform placement, signed weights
   `-2,-1,0,1,2`, explicit reset/first update, repeated load/unload, shared asset
   lifetime and independent instance overrides. Verify empty/deletion controls
   after each capture family and after disposing instances/assets.
6. Capture every actual requested movie state at the planned 60 Hz source times;
   encode its corresponding recorded delay. No image interpolation, repeated
   fabricated states, or changing curve/key rate. Keep source time, actual GPU
   pose/readback identity, frame pixels, assembly MVID and shader hashes together.
   A deterministically sought movie is labeled as such and is not a performance
   or wallclock test.
7. Separately run ordinary visible-editor playback driven by monotonic elapsed
   wall time with no seek/capture-clock substitution. Compare elapsed wall time
   and observed instance clocks over multiple loops and an intentionally measured
   stall; verify independent instances. Record actual rendered frame cadence.
   Smooth presentation and authored-seconds speed are distinct acceptance rows.
8. Prove this path through the existing Model.Load API's explicit managed backend
   and normal model component behavior. A research helper-only loader is not the
   shipped API implementation. Keep legacy vmdl behavior unchanged and verify it.

The validation coordinator owns all execution, guard admission, builds, real
shader compilation, physical editor work, captures and receipts. Model workers
prepare source/tests and review failures without launching competing workloads.
