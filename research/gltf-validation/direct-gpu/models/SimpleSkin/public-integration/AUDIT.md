# SimpleSkin public skinned integration

Prepared 2026-11-05; implementation updated 2026-11-06. The coordinator's fee5
binary/source snapshot preserves the pre-adapter producer. Root subsequently
authorized production source edits while requiring the original frozen 41-case
baseline to run against fee5 before any later producer installation. The adapter
and metadata source now exist; `production-submission.json` records their hashes.
They have not been built or executed by this worker.

The pinned corpus checkout is `90d7ede14c7e280af263824604b427a1ca02cb66`.
SimpleSkin has two real source variants: glTF and glTF-Embedded. Each is also
tested as a derived portable `.glb_c`. There is no upstream glTF-Binary variant.
The authored geometry has ten vertices, 24 indices, two joints at nodes 1 and 2,
joint hierarchy 1 → 2, child translation `(0,1,0)` metres, and one unnamed
rotation clip with twelve keys ending at 5.5 seconds. It has no morph targets,
animation graph, material, texture, or authored normals.

## Frozen requirements

`SimpleSkinPublicTests.RunResources(checkout)` declares 12 cases: each of the
four forms independently checks authored skeleton metadata, animation/graph
metadata, and empty morph metadata. `RunComponents(checkout)` declares 29
cases: seven per form plus the existing native skinned-vmdl control. Component
cases require the real direct GPU instance, public exact-key bone transforms,
seek/rate controls, independent instances, queued-update cancellation on native
replacement and disable, and disable/re-enable/destruction lifetime.

These are positive requirements. Rejection by the temporary `Model` setter
guard is a failure, not a pass or a supported final behavior. An exception in a
native metadata call is retained as a failure. No assertions silently fall back
to an alternate loader or native Model/Mesh construction.

Tests compile as a helper against the installed Engine using its existing test
friend assembly. Expected joint pivots and hierarchy are literal source facts;
expected angles use the existing independently transcribed rounded quaternion
oracle, not the producer evaluator. Vector tolerance is 0.0002 engine units and
unit-vector components. This includes float conversion error but detects wrong
pivots, inverse-bind identity exposed as a joint, wrong axis, and wrong phase.

These tests do not prove raw GPU output, images, continuous animation cadence,
performance, authored morph handling, general affine bone API expressibility,
native animation graphs, attachments, or Windows. Those remain distinct gates.

## Audited pre-adapter failures

- `ModelRenderer.Model` deliberately rejects managed models assigned to
  `SkinnedModelRenderer` before mutation.
- `SkinnedModelRenderer.OnEnabled` constructs `SceneModel` unconditionally.
- `SequenceAccessor.CurrentSequence` only sees `SceneModel.CurrentSequence`;
  duration/time/name-list properties then dereference that native-only object.
- `Model.BoneCount`, skeleton transforms, `AnimGraph`, and morph construction
  dereference the native model handle. Managed animation metadata is not assigned.
- The existing `GltfModelRig` is the historical native palette adapter. Its
  identity bind bones and inverse-bind palettes cannot serve as the new authored
  public joint hierarchy. It must not be reused to construct a native model.
- `SceneAnimationSystem.ProcessRenderer` calls `AnimationUpdate` in parallel.
  `GltfGpuInstance.SetPose` asserts the main thread and immediately uploads
  palette/weight/index buffers; direct worker invocation is invalid.

## Implemented boundary awaiting validation

The skinned adapter is in a new `SkinnedModelRenderer.Gltf.cs` plus narrow
branches in the existing `.cs`, `.Sequence.cs`, and `.Bones.cs` partials. Keep
the old native `SceneModel` path. Override the existing protected virtual
`CreateSceneObject`: managed models call the base method, vmdl returns its
existing `SceneModel`. The ordinary renderer owner removes the temporary
setter rejection was removed under the revised source-edit authorization.

Use an `AnimationSequence` subclass so the existing public `SequenceAccessor`
and JSON property contract remain the same. Reuse `GltfPlaybackClock` for all
finite signed delta/rate, duration, explicit seek, wrap and finished semantics.
Each component owns its own clock, selected clip, evaluated pose and scheduling
revision. The resource owns immutable clip and skeleton identity data only.

For scheduling, parallel `AnimationUpdate` records the pending elapsed
delta and preview state under the component lock. Main-thread `FinishUpdate`
evaluates the document and publishes `SetPose`. This avoids assuming glTF reader
caches are safe for concurrent lazy access. Public explicit seeks already occur
on the main thread and evaluate immediately. Disabled/replaced instances clear
pending work; a resource/instance generation token is checked before flush.
Do not pass a queued `GltfPose` from a destroyed generation into a replacement.

`GltfManagedModelResource` retains `GltfModelAnimation` as read metadata with
its existing clip naming/timing policy. Do not assign it to legacy
`Model.GltfAnimation`: that property's disposal currently destroys its document
when the public Model owner dies, which is too early for retained render
instances. Instead, the asset final-release callback disposes the resource's
animation metadata/document after the last instance lease. The resource
constructor's prepare-failure path must leave document disposal with its caller.

New `GltfManagedModelMetadata` maps authored source nodes to stable public
bones, deduplicate joints shared by skins, preserve required ancestor nodes and
hierarchy, and expose engine-basis rest transforms. Palette inverse-bind matrices
are separate data; they must not replace actual joint transforms. Native public
`Transform` cannot express arbitrary shear: full affine poses remain exact in
the GPU path, while conversion policy for that public API must be explicit.

Manual morph wiring was added only after the Cube owner's twelve public cases
were written. It consumes the Cube owner's immutable `GltfPose.WithMorphWeights`
API, preserving signed finite logical values and instance isolation. Explicit
zero-duration overrides and clears publish immediately; clear returns to the
current animation. The native wrapper's default fade duration is 0.05 seconds.
Managed timed transitions currently use a linear curve in scene time; equivalence
to the native interpolation curve remains unmeasured and is not a parity claim.

Native graph evaluation, bone merging, procedural overrides and bone velocities
remain unsupported. Sequence blending explicitly rejects. Public `Transform`
queries strictly reject inherited shear that cannot be represented by TRS;
the GPU pose retains its full matrix. These are remaining parity gaps, not
completed features.

## Actual baseline and next helper revision

The coordinator archived and ran the original `../../../../../../engine/Tools/Gltf/direct-gpu/models/SimpleSkin/public-integration/SimpleSkinPublicTests.cs` at SHA256
`32a1c6810b2aa01a891803eb814056fc14fed2f81859ee7bd17913413bd73b36` for
the fee5 baseline. All 41 cases executed: one existing-vmdl control passed and
40 managed requirements failed (12 missing metadata branches, 28 temporary
skinned setter rejections). There were no compilation, incomplete, or cleanup
errors. Receipt SHA256 is
`6185f21254a4ded01c94824fa7d2915523f395ef3af69021d31679478da0068d`, at
`Temp/gltf-validation/direct-gpu/fee5-simple-skin-public-red-41/probe-01-editor_direct_gpu_tests/receipt-01.json`.

After that actual baseline, `../../../../../../engine/Tools/Gltf/direct-gpu/models/SimpleSkin/public-integration/SimpleSkinPublicTests.cs` was revised in place to
SHA256 `35855b8b20d6fdf24ae8dc80d5e7b4c9814824fcd862933b50441c77aade9c97`:
12 resource cases, 41 component cases and four explicit inherited-shear API
limitation cases. It adds interval accumulation, absolute-seek interleaving and
zero-delta preview restoration requirements.

Its worker joins yield the main thread and are bounded to two seconds. A timed
out worker makes the suite fail/stop, leaving remaining declared cases incomplete.
The exact task, private scene and mount remain owned in the original helper's
registry. Cleanup must call `CompletePendingWorkerCleanupAsync` on that original
loaded assembly, identified by the MVID in the error; a newly compiled helper's
static registry cannot recover it. Cleanup never disposes a running worker's
fixture and reports outstanding work.

The first installed adapter run executed all 57 cases: 53 passed and four failed
only on the native-handle null-wrapper assertion. Receipt SHA256
`2a00ccf3fb6374ce901d4029c614fbaf45e509dbe06cd6013a7a9fd2a02f768d`
remains intact. Three fresh matched controls then passed, receipt
`66ac4a3006173a0b9359591738cad7e85a264071cadf0ef1593d43f037a80315`:
both a bare custom object and the actual public skinned glTF object return a
nonnull wrapper with an invalid strong handle and a null public Model; the
native vmdl control has a valid strong handle and nonnull public Model.

Only after those controls, the assertion was corrected to require a null wrapper
or invalid strong handle. Every nonnull wrapper is still released, and the
managed resource's null native handle and the scene object's null public Model
remain required. The revised helper SHA256 is
`ff499cebfbc055c85c1a0c74a5bcb697f0cb21889a4938d8eb6688ea42b4aeff`.
All 57 requirements plus the same three controls passed on the actual installed
adapter (60/60), receipt
`e9a3bc6ad826740b93c7a29696e6abcd9c323de967aa55840095728bc2f7579d`.
No compilation, incomplete, or cleanup errors occurred. These API results do
not substitute for GPU/image or wall-clock proof.

## Degenerate and tiny TRS conversion follow-up

`../../../../../../engine/Tools/Gltf/direct-gpu/models/SimpleSkin/public-integration/SimpleSkinTransformConversionTests.cs` adds eighteen literal matrix requirements:
ordinary rotated TRS, each single zero axis, each pair of zero axes, all-zero
scale, four reflection patterns, four tiny-scale patterns, and two genuine shear
negative controls. `RunLegacy()` calls the existing installed converter directly;
`RunPublicAdapter()` checks the new adapter with the same inputs. The actual
legacy run passed 14/18; only the four tiny-TRS cases rejected. All zero-axis,
all-zero, reflection and genuine-shear controls passed. Receipt
`cf229f85cd7b0e48fc6edcd8ac8d45e01ecb56153247f90f907072701df81b75`
is retained. A rejected zero/tiny TRS is recorded as
a conversion limitation, not proof that the public type cannot represent it.
Reconstruction compares each row relative to its own magnitude, including exact
zero rows; an absolute epsilon floor cannot hide tiny-scale collapse. The legacy
converter's measured eight-machine-epsilon guard is unchanged.

`TINY_TRANSFORM_FOLLOWUP.md` describes the source-based diagnosis, nineteen new
regression/diagnostic cases and an unapplied normalization-fallback patch. The
proposal preserves the measured reconstruction guard and source GPU affine data.

## Public GPU/image gate

`SimpleSkinPublicCapture` reuses the existing public test fixture/mount, the
original independent position/triangle oracle, the original GPU checker and the
coordinator's `NativeCapture` framework. It loads actual source glTF or actual
compiled GLB through `Model.Load`, creates an ordinary `SkinnedModelRenderer`,
and changes pose only through `Sequence.Time` at 0 and 1 seconds. Each form has
eight native captures with empty/reference/deletion controls and two actual GPU
readbacks. This is deterministic seek evidence, not playback-rate evidence.

The old checker received only an approved `public` to `internal` class modifier
for canonical internal backend accessibility; numeric/oracle code is unchanged.
Old SHA256 `4c7f781b4856643bc91239fe0adb453840763b34a27cb9e4dbdd2af4ebdafa55`
and its historical receipts remain valid. New SHA256 is
`c19d1463292064a13c81325faa0ea3e851f2ca8fe6288ba01567fcb1588ac109`.
Capture source SHA256 is
`3d27a1a15b9686895111dccece679ae436c6bf5f12065a969b345c21a73de443`.
Root approved position error `1e-4` inches, normal error `1e-5`, IoU 1, and zero
mean/maximum RGB error. Both actual source and compiled forms passed in receipt
`2c7a40a3a203e78c1183045c57f314c9f61de408d7356e1209ffbcfb437dd5a5`
at `Temp/gltf-validation/direct-gpu/c5c0-public-skin-capture-2/probe-01-editor_direct_gpu_tests/receipt-01.json`.
All four actual poses matched the independent images exactly (IoU 1, zero RGB
error); each form changed 4,898 pixels between its two actual poses, matching the
independent references. Maximum GPU position error was `3.8146973e-6` inches,
normal error zero, topology and finite-value gates passed. All sixteen native
captures, including empty/deletion controls, are retained in
`Temp/gltf-validation/direct-gpu/public-model-smokes/SimpleSkin/latest-evidence.json`.
Both actual source-pose images were also visually inspected by this worker.
Installed Engine MVID was `2c054bba-c0fa-43b5-a3a2-39a34ff7577f`, Gltf
`c869761c-5b80-462d-982a-bc3a688e57b6`. This is actual public deterministic
animation/rendering evidence; wall-clock playback, Windows and performance remain
separate gates.

## Ownership

- This worker: this folder; conditionally the new metadata/skinned partial and
  listed narrow partial-file branches after baseline/release.
- Orientation: managed resource/base ordinary renderer; explicit resource file
  handoff and setter-guard removal are coordinated before writing.
- Fox: canonical GPU backend/rig; no modifications proposed here.
- Validation coordinator: all builds, installs, editor work, actual receipts.

Existing SimpleSkin research files and evidence remain frozen and unchanged.
