# glTF tests-first implementation

Current validation direction: use the running s&box editor and its actual preview
renderer, visibly on the user's physical desktop, for rendering acceptance. The
user returned to the PC and explicitly ended virtual-display use. See the
[editor validation workflow](../../../research/gltf-validation/README.md) and its
real-frame capture scripts. The earlier windowless test host produced stale
captures and is not a substitute for this evidence. Keep its failures separate
from supported or unsupported glTF features; do not change mesh winding to fit
those captures. The standalone test commands below describe the existing test
projects, not permission to resume that validation route in this conversation.

This MSTest project exercises `Sandbox.Gltf` without initializing s&box engine
interop. Container/accessor reading, geometry, materials, scenes, animation and
lossless GLB packaging now have managed implementations. Meshopt uses the pinned
native codec supplied by Evergine's .NET binding; it needs a native payload but
does not need the engine. The latest actual-editor run executed 486 cases:
483 passed, three singular-transform cases failed, and none were blocked or
unexecuted. Positive native Draco, Basis/KTX and meshopt preflights passed.
Additional conformance tests are being written and are not yet executed; the
current count is not complete specification or rendering coverage. The reviewed
singular-transform fix is newer than the loaded producer and still needs
build/restart validation. The full evidence is recorded in the validation docs.
Tests assert useful results or specified errors; they
do not contain unconditional failure placeholders or skipped acceptance gates.

## Run

From the repository root, use the existing build tool's new project selector:

```sh
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- test --project Sandbox.Test.Gltf
```

Follow the current AGENTS.md resource policy: inspect host guard status and run
expensive work through the guard. Historical task requests do not override the
newly supplied launch, memory and swap rules. Do not interrupt the user's live
editor/play session to run tests. The selector builds
only the selected test project and its references, with one MSBuild worker.
The ordinary unfiltered SboxBuild test command also includes this project.

## Coverage and priority

| Priority | Tests | What they check |
| --- | --- | --- |
| 1 | Container, accessor and validation edges | JSON/GLB structure and version, extension declarations, external and embedded buffers, offsets, strides, padding, normalization, sparse data and bounded reads |
| 2 | Primitive and geometry | Attribute layouts and references, index widths/ranges, all core topology modes, nonindexed geometry, coordinate/unit conversion and negative transforms |
| 3 | Scene, animation, channel and deformation | Hierarchies, TRS/matrices, interpolation, independent poses, skin/inverse-bind order, multiple influences and morph-before-skin evaluation |
| 4 | Material, image and scene features | PBR defaults, color-space roles, samplers, UV transforms, variants/fallbacks, image source selection, cameras, lights and instancing |
| 5 | Codec goldens and compiler | Independently known decoded bytes, malformed streams, valid portable GLB packaging, deterministic lossless round trips, dependencies and unknown extension preservation |

The written tests preceded their corresponding implementation. The first build
found raw-string fixture compilation errors. The expanded meshopt run then failed
six cases because copied fixture arrays differed from the pinned upstream
goldens; these now reuse the verified vectors. Continue to
fix the highest-priority failing behavior and rerun. Do not weaken
assertions to make missing implementation appear supported.

The pose-aware vertex evaluator combines animated morph weights, inverse binds,
joint hierarchies and multiple influence sets in world coordinates; focused tests
also compare source and packaged GLB at several animation times. Morph target
counts must agree across every primitive in a mesh. Empty target objects can
still retain unchanged base attributes. These are position-evaluation checks;
animated normal/tangent and GPU agreement remain required.

The current scene/animation helpers decode arrays and rebuild state per call.
They establish a testable correctness path, not the final cached runtime path.
The compiler currently performs lossless GLB packaging; GPU layouts, native
resource integration and optimized `.glb_c` loading are still required. It
preserves buffer identities when merging would invalidate opaque references or
metadata, and rejects unknown GLB chunks instead of discarding them. Required
meshopt fallback placeholders survive packaging; a placeholder at buffer zero
needs real BIN storage to satisfy the GLB container rules. This can increase size.
External core images become immutable content-addressed sidecars and the model
is published atomically after those images. Unknown extension dependencies still
need an explicit audit. This is not the final optimized representation.

These tests distinguish isolated decoder fixtures from complete valid assets.
For example, a uint32 decoder test uses large integer values without allocating
billions of mesh vertices. That is not evidence of a validator-clean model.
The official validator and corpus adapters must separately assess complete assets.

## Required proof beyond unit tests

The [sample validation harness](../../../research/gltf-validation/README.md) pins
the Khronos sample revision and reports missing evidence as a failing assessment.
Its self-tests exercise the harness, not engine rendering. Submitted JSON and
artifact hashes alone cannot establish parity.

Before completion, implement and execute the remaining concrete tests/adapters:

- Draco and KTX2/Basis decoding/transcoding goldens, meshopt glTF integration,
  version/filter coverage, and extension fallback/rejection tests. Metadata source
  selection tests do not prove a compression codec works.
- CPU/GPU agreement for animation, morphs, skinning, normals/tangents and all
  primitive modes; independent reference evaluation and approved tolerances.
- Managed buffer allocation/upload/draw integration, bounds/culling, instances,
  depth, shadows, opaque/masked/blended passes, alpha behavior, normal mapping,
  UV/sampling/channel/color-space correctness, lighting and motion behavior.
- Resource lifetime, repeated load/unload, failure cleanup, variant/override
  isolation and unchanged legacy vmdl gameplay-facing behavior.
- Every applicable pinned sample/variant rendered in-engine and visually reviewed
  against a recorded reference; missing fixtures must be added for uncovered core
  and selected-extension requirements. Exclusions and upstream defects stay visible.
- Portable `.glb_c` GPU preparation and packaged dependency tests, invalidation on
  change, raw-source loading, and matched vmdl load/memory/package benchmarks.
- Audited model-editor/ModelDoc parity, undo/save/reopen, selection, reimport
  conflicts, modifiers and physics/animation runtime behavior. See the
  [recorded editor direction](../../../research/gltf-model-editor-direction.md).

Validate Linux first and Windows before declaring the full goal complete. Runtime
numerical and benchmark thresholds still require measurement and agreement. Standards revision pinning,
shader/material integration and native compatibility must be resolved explicitly;
passing this project cannot substitute for any of those checks.

## Execution status

The successful Linux command is shown above; its current console artifact is
`Temp/gltf-validation/unit-latest.log` (ignored by Git). Compilation reports zero
warnings and errors, and the last completed run reports 373 passed tests. This does not establish
complete image-format support, full GPU rendering, Khronos sample parity, optimized `.glb_c` layouts
or the vmdl performance target. The pinned checkout is available at
`Temp/gltf-reference/glTF-Sample-Assets`. Its 150 models contain 337 indexed
variants. The managed data-reader scan passes all 185 in-scope cases, including
147 core-only variants; 152 excluded-scope variants receive separate fallback
or failure diagnostics. The report is `Temp/gltf-validation/readers.json`.

The managed container round-trip now passes 185/185 required cases after fixing
meshopt fallback buffer handling and external image packaging. Reload resolves
only against the written package. `Temp/gltf-validation/compiled.json` records
artifact and dependency hashes. The [independent Khronos validator adapter](../../../research/gltf-validation/VALIDATOR.md)
passes source and compiled checks for all 147 core-only variants. Across all 185
required variants, 164 pass and 21 remain incomplete because the pinned validator
does not implement Draco, meshopt or GPU instancing checks. No required variant
reports a validation or dependency I/O error. These earlier reports do not prove
image transcoding, rendering or GPU layout optimization. The standalone KTX2
decoder now exercises pinned independent ETC1S and UASTC color, alpha and mip
fixtures; it is not yet a full `KHR_texture_basisu` conformance or rendering test.
The updated corpus scanner performs a bounded KTX2 RGBA8 decode per asset and
reports that separately. The six indexed BasisU variants are excluded for other
out-of-scope extensions; they do not establish applicable extension coverage.

The internal engine `GltfGeometryBuffer` uploads decoded geometry through typed
C# `GpuBuffer` objects and submits indexed draws. Fifteen focused pure engine
tests pass for its upload layout, per-role UV mapping and sampler descriptors.
Two dedicated GPU probe shaders compile. Opt-in integration tests cover the
pinned Triangle source/container path and sampler pixels; their native execution
status must be checked separately in `Temp/gltf-validation/gpu-integration.log`.
The first Vulkan run renders the Triangle through C# buffers in all four source/
container cases. All eight independent sampler filter modes and wrap/mirror/clamp
match the probe's reference values. A zero maximum LOD forces magnification on
Vulkan, so it is not equivalent to removing mipmaps when min/mag filters differ.
The single-mip texture probe verifies independent min/mag without this clamp.
A separate experiment showed `RenderAttributes.Set(texture, mip: 0)` did not
restrict sampled mip selection; do not assume it provides a base-only view.
The stock-shader route is now preferred, and a native model primitive bridge is
being tested. These staging paths have no complete scene hierarchy, skin/morph GPU
execution, resource registration or editor integration yet. Pretransformed UVs
make its buffers material-dependent; do not share them across incompatible
variants or overrides.

## Shared material-cache prototype

`GltfMaterialCache` shares a configured copy of a caller-loaded stock material.
Its descriptor snapshots the template identity and explicit revision, shader
features, scalar/vector parameters, texture identities and dirty versions, and
sampling state. Structural equality resolves hash collisions. Equivalent
descriptors share one material; an edited descriptor acquires a separate entry
without mutating the template or existing users.

An asset can hold a lease without a scene. `GltfMaterialSceneSystem` owns scene
leases against the same cache, so destroying one scene cannot release resources
still used by another scene or asset. Releasing the final lease invalidates the
procedural material wrapper and queues native handle release through the existing
engine path. Clearing the cache retires entries while outstanding leases remain
valid. This is a main-thread API and cannot allocate during a graphics scope.
It binds existing textures; it does not create procedural textures.

Run the focused bookkeeping and native lifecycle tests through SboxBuild:

```sh
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- test --project Sandbox.Test.Unit --filter FullyQualifiedName~GltfMaterialCacheTests
env SBOX_TEST_GRAPHICS=1 dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- test --project Sandbox.Test.Integration --filter FullyQualifiedName~GltfMaterialCacheNativeTests
```

The focused cache suite passes 10/10 unit tests. The native lifecycle test passes
with both Vulkan and the default empty renderer, including two scenes sharing
with an asset owner, isolated parameter edits, and rejection after scene disposal.
The tests first reproduced cleanup, recursive-creation, conflicting-binding and
disposed-scene failures before their fixes. Logs are in
`Temp/gltf-validation/material-cache{,-red,-headless}.log` and
`material-cache-native-red.log`; the Vulkan green case is also recorded in
`gpu-integration.log`. That broader graphics run still has two failing stock-model
pixel tests: the diagnostic observes prior-model pixels even in renderer-disabled
captures. Neither that suite nor complete glTF rendering parity is green.

This is an internal process-scoped prototype. Full model-loader ownership,
game-context shutdown, automatic source/template invalidation, variant and override
precedence, and performance measurements remain to be integrated. Callers must
treat shared material instances as immutable: the public `Material` API does not
enforce that rule. Descriptor revision changes prevent reuse of stale entries;
they do not automatically update a material already held by a caller.

## CPU image-to-native-texture slice

The internal `GltfTextureCompiler` takes decoded-container `GltfImage` bytes and
writes a native resource with an uncompressed RGBA8888 DATA block and streaming
pixels. PNG/JPEG decoding happens in a bounded CPU bitmap, before any GPU texture
exists. It follows the established `VTexWriter`/`ResourceWriter` path and preserves
straight alpha and all decoded bytes. Native texture loading uses the normal
`.vtex` resource lookup. There is no call to procedural texture creation in the
compiler. Six focused native integration cases pass with Vulkan, including exact
GPU readback in both color roles; JPEG source decoding has its own quantization
tolerance, while resource writing must preserve the decoded bytes exactly.

The slice has one mip level. Mip generation, 16-bit PNG preservation, Basis GPU
formats, shader sampling/color-space verification, content-addressed dependency
packaging and integration into model import/build/runtime loading remain pending.
Unsupported precision is rejected explicitly. The decoded-byte budget bounds the
RGBA bitmap, not the sum of all temporary/output allocations or native codec use.
The command and current log are:

```sh
env SBOX_TEST_GRAPHICS=1 dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- test --project Sandbox.Test.Integration --filter FullyQualifiedName~GltfCompiledTextureTests
```

`Temp/gltf-validation/compiled-texture.log` records the green run; the earlier
`compiled-texture-red.log` records the missing implementation failures.

Draco sparse overrides, shared accessor context between compressed and ordinary
primitives, and direct strip index-accessor reconstruction remain incomplete.
The native dependency also has release-review limitations recorded in
`engine/Sandbox.Gltf/ThirdPartyNotices.md`. Keep these acceptance gaps visible;
the current corpus run does not establish complete extension conformance.
