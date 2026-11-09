# Pure glTF suite inventory audit — 6 November 2026

This is a static source inventory and execution proposal, not a test run. No
shared manifest, runner, test source or producer was changed. The live sampler
and Cube validation queue retains priority; the coordinator owns execution.

## Exact delta

The version 7 manifest contains **44 classes, 432 methods, 432 DataRows and 764
expanded cases**. All 44 current files are byte-for-byte identical to its frozen
`f9fd58602bbf47a6ac1bfa51f0d07845/pure-test-source-snapshot`. There are no removed
files or method/count changes among them. The current project adds these files:

| New source | Methods | DataRows | Cases | Required API |
|---|---:|---:|---:|---|
| `GltfMorphUploadDataTests.cs` | 10 | 16 | 23 | `GltfDocument.MaterializeMorphTargetsUpload` |
| `GltfPoseMorphOverrideTests.cs` | 17 | 12 | 25 | `GltfPose.WithMorphWeights` and pose provenance |
| `GltfPoseOwnershipTests.cs` | 4 | 0 | 4 | `GltfDocument.ValidatePoseOwnership` |
| Additive cohort | **31** | **28** | **52** | Public reader APIs only |
| Current complete pure project | **463** | **460** | **816** | **47 classes** |

The inventory counts each non-parameterized test once and each explicit DataRow
once. All method names, DataRow source text, line locations and SHA256 values
are in `PURE_SUITE_INVENTORY.json`. Counts were checked against every legacy
suite entry; the coordinator must independently enforce reflection-discovered
counts before actual execution.

The three new classes use in-memory fixtures and public `Sandbox.Gltf` APIs.
They have no external source-test dependencies, embedded resources, native
codec requirements, GPU allocation, shader compilation or native Model/Mesh
construction. The zero-allocation ownership check is CPU behavior, not a load
time or memory-parity benchmark. The override tests deliberately use reflection
so a missing API can remain a real red rather than requiring a substitute
implementation.

The three relevant API source files match the `c5c0fe3da02d46efaca6decd71760d42`
producer input manifest exactly. Test sources themselves are not producer build
inputs; their new frozen helper hashes still require separate recording. No
static source match is presented as a completed runtime result.

## Why the old gate rejects current execution

`engine/Tools/Gltf/scripts/run_editor_tests.py` reads the worktree and enforces exactly the version 7
44-file inventory. It correctly rejects the three additional files before
execution. Updating only its total would not fix its frozen version/schema and
per-suite checks.

`engine/Tools/Gltf/editor/editor_gltf_unit_tests.csx` currently reads the historical 44-file snapshot,
whose inventory remains valid. It also requires that snapshot's producer hash
`3cc86c2490bc835bcf1932b50ead41ea6ea5e94a46b1212d8ea8a33b8641faa0`.
That differs from the inspected current disk glTF hash
`a6744ebb672015f4f32568e8c88c193ba26a0259246376fed261b129a812af9e`.
It therefore cannot establish a c5c0 regression result without a new reviewed
freeze. Keep the historical manifest, identity checks and receipts intact.

The model assertion runner already admits the first two new reader paths but
does not admit `GltfPoseOwnershipTests.cs`. The managing agent approved adding
that exact third path; the coordinator applies it. Moving or copying tests to
another allowed directory would evade the admission policy and is not proposed.

## Smallest coherent execution proposal

1. Freeze and execute `PURE_SUITE_ADDITIONS_PROPOSAL.json`: **3 classes / 52
   cases**, with the actual loaded c5c0 glTF assembly and test framework hashes.
   Admit only the exact ownership test path in addition to the two existing
   paths. No build, editor restart, native codec preflight or GPU work is needed
   merely to run this cohort on the existing editor.
2. After the current capture/material jobs, freeze the full
   `PURE_SUITE_CURRENT_PROPOSAL.json`: **47 classes / 816 cases**. Preserve the
   old pure runner's BCL + loaded glTF + MSTest metadata reference policy;
   importing unrelated Engine/global vector types can change name binding in
   the unchanged pure test sources. Use a new reviewed selection/snapshot and
   producer identity rather than weakening the historical runner's pin.
3. Record the three codec preflights separately from the 816 project cases.
   **117 cases** belong to suites declaring native codec requirements; **699**
   do not. A failed decoder preflight produces an explicit blocked/incomplete
   cohort, not exclusions or successful fallback coverage.

The full suite retains the existing Draco, meshopt and KTX requirements and
four embedded KTX fixtures, with their hashes in the proposal. The parser
project pins Evergine Draco `2025.10.14.20`, MeshOptimizer `2026.10.1.20` and KTX
`2026.6.15.8`; native assets must match the actual runtime RID. These are codec
libraries, not a requirement to construct native engine models. Compiler-safety
and meshopt-integration also compile their existing `GltfMeshoptTests.cs` source
dependency. None of those dependencies applies to the new 52-case cohort.

The separately inventoried **25 `Gltf*.cs` Engine unit files / 321 static cases**
are not part of the 764 or 816 pure project. They reference internal Engine
types and require their own friend-assembly/harness/dependency review. Model
research helpers, native integration tests, corpus validation, GPU/image checks
and editor tests likewise remain separate. Adding unrelated counts together
would not establish complete glTF coverage.

## Existing public-path evidence stays scoped

The fee5 producer's corrected 25 public Model/resource/component cases plus
three handle controls passed, as did its six async and ten lifecycle cases.
The public Orientation capture also passed 13 GPU rows and seven image controls.
Those receipts establish their declared public-path and same-material geometry
checks. They neither execute this new pure inventory nor prove every feature
against a later producer. See `EVIDENCE.md` for the retained failures, exact
producer identities and limits.
