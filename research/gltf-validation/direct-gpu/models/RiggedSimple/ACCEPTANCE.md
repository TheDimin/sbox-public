# RiggedSimple direct GPU acceptance

Dedicated model owner: `gpu_rigged_simple`. Runtime backend owner: `gpu_fox`.
The `gpu_validation` worker alone runs editor operations, shader compilation,
heavy builds and tests under the resource guard. No result below is a pass until
an actual editor receipt identifies the loaded producer and executed sources.

Pinned corpus: Khronos glTF-Sample-Assets
`90d7ede14c7e280af263824604b427a1ca02cb66`, model `Models/RiggedSimple`.
License: CC-BY-4.0, Cesium (2017), as recorded in the model's `metadata.json`
and `LICENSE.md`. Source/license files remain in the external pinned checkout.

## Audited source

- Five nodes. `Z_UP`, `Armature` and `Bone` retain authored matrices. The
  skinned `Cylinder` mesh is node 2. `Bone.001` (node 4) has animated TRS.
- One mesh, one triangle primitive, 160 accessor vertices, 564 U16 indices.
- Skin 0 has joints `[3,4]`, two inverse bind matrices and one influence set.
  Joint indices are U16 VEC4, stride 8; weights are FLOAT VEC4, stride 16.
- POSITION and NORMAL are FLOAT VEC3, stride 12, sharing one bufferView with
  accessor-relative offsets 1920 and 0. The runtime must retain these facts
  rather than reinterpret the view as an interleaved or fixed struct.
- One unnamed animation. Three LINEAR samplers animate node 4 translation,
  rotation and scale. Each has 50 keys, from `0.04166661947965622` to
  `2.083333015441895` seconds. Times before the first key clamp to its value;
  the clip must not be retimed to move that key to zero.
- No morph targets, UV sets or textures. Material 0 has a green base factor,
  metallic 0 and default roughness 1. Stock s&box shaders are the intended
  material path; diagnostic material substitution must be recorded explicitly.
- Applicable variants: glTF, glTF-Embedded, glTF-Binary and glTF-Draco; each
  original and compiled form needs an acceptance row. Draco now has a written variant-local numeric suite backed by a separately
  integrated official WASM decoder, with shared codec ancestry explicitly
  documented. Its input authoring and tests remain unrun; its coverage must not
  disappear or be counted as a pass from decoder declarations.

## Written first: CPU/data expectations

Compile `../../../../../engine/Tools/Gltf/direct-gpu/models/RiggedSimple/RiggedSimpleReference.cs`, `../../../../../engine/Tools/Gltf/direct-gpu/models/RiggedSimple/RiggedSimpleLayoutFixture.cs` and
`../../../../../engine/Tools/Gltf/direct-gpu/models/RiggedSimple/RiggedSimpleModelTests.cs` as test sources
against the actually loaded glTF producer and MSTest. Do not compile producer
sources into the test helper. Set
`Sandbox.Gltf.DirectGpu.Tests.RiggedSimpleModelTests.CheckoutRoot` to the engine
checkout and run all six methods, including eight explicit DataRows: **12 cases**.
The first ten cases have a historical pass receipt below; the two layout cases
remain queued against the current source freeze.

The independent reference reads JSON/accessor bytes itself, composes the complete
hierarchy, samples LINEAR channels with the framework quaternion implementation,
accumulates all present weight sets and computes inverse-bind-before-joint skin
transforms. It never calls the producer's decoder, animation sampler, pose,
palette or coordinate converter. Unsupported fixture features fail closed.

The cases establish:

1. Fixture shape and timing remain as audited.
2. The oracle has nonzero visible motion and rejects reversed palette order
   and double application of the skinned mesh-node world transform.
3. Six uncompressed original/compiled forms match all five world matrices and
   twelve selected vertices at rest, all 50 keys, 49 midpoints, time zero and
   samples before/after the authored range: 103 samples per form.
4. The actual upload reader preserves the shared storage, separate accessor
   offsets, actual strides and component formats.
5. Independent reference instances retain their poses; full affine placement
   including shear/reflection and literal basis/units conversion apply once.

The numerical check allows 32 float32 relative epsilon units with a minimum
absolute scale of 1. This bounds independent float arithmetic only. It does not
approve resampling, clamping authored values or a visual/GPU approximation.
Failures retain their exact expected/actual values and bound. GPU tolerances
must be reported separately with measured error distributions.

## Actual GPU gates still required

Use the shared `CONTRACT.md` interface. The instance receives `SetPose` from the
same immutable evaluated pose used for every associated deformation input;
reading source metadata is not GPU acceptance. No `Model`, `Mesh`, `IModel`,
model builder or native morph controller may be constructed along this path.

- First run the shared raw-triangle draw/readback and empty/deletion controls.
  Use the accepted native camera path: Normal visualization, normal
  postprocessing, an owned stock light, explicit matching CustomSize and target
  size. The failed Albedo capture routes are unsuitable for this proof.
- For every original and compiled form, draw authored rest and the animation
  at all keys and between keys. Compare GPU output positions/normals against
  the independent reference, following each snapshot's source-vertex map and
  actual output fields/offsets/stride. Check every output vertex, not only
  the twelve CPU spot checks.
- Numeric and rendered-image checks are separate rows. Require positive
  geometry, literal-reference silhouette agreement, normal error evidence and
  zero residual geometry after deletion on fresh targets.
- Apply an ordinary translation, a nonuniform scale and a full affine
  shear/reflection placement. The output is world-space and the scene object's
  transform must remain identity. Preserve matrix-authored source ancestors.
- Put two instances in different poses and placements; updating one must not
  mutate the other's output, palette or clock. Destroy one and continue
  rendering the other; final deletion must produce an empty target.
- Run repeated create/dispose and creation-failure rollback, checking owned
  GPU buffers/material leases and callback removal. Disposing a live asset
  with remaining instances must follow the shared contract.
- Capture smooth actual GPU animation at a requested 60 Hz source-time cadence,
  recording every actual timestamp and frame duration, plus total clip time.
  No synthetic frames, curve-rate changes or discarded elapsed time. A
  separate wall-clock run proves normal playback and clock independence;
  a deterministic seek movie does not prove real-time performance.
- Material appearance review uses the model's green material through the
  intended stock material path. Diagnostic white/normal views are not a
  substitute for the final appearance result.

## GPU probe entry point

After the shared backend has compiled and passed its raw-triangle gate, compile
`../../../../../engine/Tools/Gltf/direct-gpu/models/RiggedSimple/RiggedSimpleGpuChecks.cs` and `../../../../../engine/Tools/Gltf/direct-gpu/models/RiggedSimple/RiggedSimpleGpuProbe.cs` with the reference and
shared backend. Call `RiggedSimpleGpuProbe.Run(document, reference, sceneWorld,
borrowedComputeMaterial, boundedActualFrameWait, optionalMaterialResolver)` for each
source/compiled form. The callback must await real editor/native render frames,
not directly invoke the instance draw callback. The probe requires matching pose
and submitted revisions, nonzero draw calls and no callback errors before every
readback. It measures all output vertices at 103 source samples, full-affine
placements, independent instance retention and surviving-instance behavior after
disposal. It returns 108 measured rows; these have `measured-not-accepted` status.
The coordinator must still establish numerical acceptance, independent raster
controls, material appearance, final deletion and smooth animation media.

The shader material is borrowed from the coordinator and remains alive until all
assets are disposed. The engine globally caches `Material.FromShader`; a model
asset must not destroy that borrowed cached entry.

Two additional model-derived layout cases are written and queued: a padded
64-byte interleaved view at a nonzero buffer origin, offsets POSITION 0, NORMAL
16, WEIGHTS 28 and JOINTS 48, preserving every source attribute and animation
value. Original and compiled forms each check all 160 vertices at four poses.
These are synthetic layout controls, not Khronos corpus variants. The latest CPU
source now declares 12 cases (the historical 10-case receipt below remains valid
for its recorded source hash); the two new cases are not yet executed.

## Draco variant-local reference

The shared official decoder author is
`models/CesiumMan/author_draco_reference.cjs`, frozen SHA-256
`23fa4616c7819bdfe62c56b7d10111e586259431c2ecffbb712829b2a9c2481c`.
The coordinator alone runs its bounded invocation with source-relative
`Models/RiggedSimple/glTF-Draco/RiggedSimple.gltf`; the output belongs under
`Temp/gltf-validation/direct-gpu/RiggedSimple/draco-reference.json`.
The helper uses official Draco 1.5.7 JS/WASM at commit
`8786740086a9f4d83f44aa83badfbea4dce7a1b5`, retaining its Apache-2.0 license.

`../../../../../engine/Tools/Gltf/direct-gpu/models/RiggedSimple/RiggedSimpleReference.Draco.cs` validates corpus/source/buffer/compressed-view
and decoded-attribute hashes, decoder identity, actual component formats/counts
and source accessor normalization. It feeds that variant's decoded bytes and
triangle order into the existing independent C# hierarchy/skin math, retaining
this variant's animation and inverse-bind source bytes. It does not assume
uncompressed point correspondence, invoke the producer decoder or rewrite an
artist's asset. No geometry value, weight or joint ID is clamped or normalized.

`../../../../../engine/Tools/Gltf/direct-gpu/models/RiggedSimple/RiggedSimpleDracoTests.cs` declares **8 cases** (two ordinary methods and three
DataTestMethods with two rows each). Configure it with checkout and decoded
oracle paths, and compile it with both reference partials. The checks bind source
and decoder provenance, compare every raw attribute and triangle index for source
and compiled forms, check every vertex at eight rest/key/between/clamp poses,
and validate decoded weights/joint IDs without repairing them. The input author
is a separate integration of the same upstream Draco codec algorithm family;
**shared codec ancestry remains**, so this is not a wholly independent decoder.
Input authoring itself proves no engine/GPU pass. These eight cases are queued.
The combined current CPU inventory is 12 core/layout + 8 Draco = **20 cases**.

## Current evidence

The 10 CPU/data cases **passed in the actual s&box editor** on 5 November
2026, within a 74/74-case combined model suite. Receipt:
`Temp/gltf-validation/direct-gpu/core-model-tests-alias-fixed/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `1a7570f0167fa928fe7927b959f9bb8b2c2a15a0d36d8126d45697c656f98e94`.
Loaded Gltf MVID `b064c360-8818-4f8c-8963-27d894571c5f`; Engine MVID
`04b86c2e-4406-422e-a466-b61ec13f1f86`. The executed CPU test source hash was
`4d7edb3505ba573bda2bbe4217183ebebc1b8707658fdcca101e761c00b71f92`,
reference hash `ddc1ba9d131340de860305e00b14aa2d8f6b18040d235d991c5249f714d1b8fe`.

The GPU checker/probe and new Draco suite remain source-only. Direct GPU
execution, Draco input authoring/tests, final visual review and high-cadence
media remain pending. Older native-model captures are historical and cannot satisfy
these gates. The current compiler packages portable GLB losslessly; these tests
do not prove GPU optimization or the final load/memory target against vmdl.
