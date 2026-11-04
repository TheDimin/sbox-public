# glTF tests-first implementation

This MSTest project exercises `Sandbox.Gltf` without initializing s&box engine
interop. Container/accessor reading, geometry, materials, scenes, animation and
lossless GLB packaging now have managed implementations. Meshopt uses the pinned
native codec supplied by Evergine's .NET binding; it needs a native payload but
does not need the engine. The Linux suite builds without warnings and currently
passes 313 test cases, including native Draco decoding, meshopt buffer-view integration and accessor regressions.
Tests assert useful results or specified errors; they
do not contain unconditional failure placeholders or skipped acceptance gates.

## Run

From the repository root, use the existing build tool's new project selector:

```sh
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- test --project Sandbox.Test.Gltf
```

Follow the active session's resource policy. The user explicitly disabled the
memory guard for this glTF task on 2026-11-04 after closing the editors; the swap
refresh was cancelled and no swap settings were changed. This override applies
to this task and does not alter the shared guard or other projects. The selector builds
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
goldens; these now reuse the verified vectors and all 313 cases pass. Continue to
fix the highest-priority failing behavior and rerun. Do not weaken
assertions to make missing implementation appear supported.

The current scene/animation helpers decode arrays and rebuild state per call.
They establish a testable correctness path, not the final cached runtime path.
The compiler currently performs lossless GLB packaging; GPU layouts, native
resource integration and optimized `.glb_c` loading are still required. It
conservatively rejects buffer merging when opaque references or metadata could
be invalidated, and rejects unknown GLB chunks instead of discarding them.

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

Linux is the active goal's completion platform. Runtime numerical and benchmark
thresholds still require measurement and agreement. Standards revision pinning,
shader/material integration and native compatibility must be resolved explicitly;
passing this project cannot substitute for any of those checks.

## Execution status

The successful Linux command is shown above; its current console artifact is
`Temp/gltf-validation/unit-latest.log` (ignored by Git). Compilation reports zero
warnings and errors, and the run reports 313 passed tests. This does not establish
image decoding, GPU rendering, Khronos sample parity, optimized `.glb_c` layouts
or the vmdl performance target. The pinned checkout is available at
`Temp/gltf-reference/glTF-Sample-Assets`. Its 150 models contain 337 indexed
variants. The managed data-reader scan passes all 185 in-scope cases, including
147 core-only variants; 152 excluded-scope variants receive separate fallback
or failure diagnostics. The report is `Temp/gltf-validation/readers.json`.

The first managed container round-trip run passes 181/185 required cases; the
four failures involve meshopt fallback buffer packaging. Its report is
`Temp/gltf-validation/compiled.json`. Neither data-reader completion nor a managed
round-trip proves independent glTF validity, image transcoding, rendering or GPU
layout optimization. BasisU remains encoded-source-only in this adapter.

Draco sparse overrides, shared accessor context between compressed and ordinary
primitives, and direct strip index-accessor reconstruction remain incomplete.
The native dependency also has release-review limitations recorded in
`engine/Sandbox.Gltf/ThirdPartyNotices.md`. Keep these acceptance gaps visible;
the current corpus run does not establish complete extension conformance.
