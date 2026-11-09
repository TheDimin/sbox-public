# MaterialsVariantsShoe evidence

## Executed original freeze

The coordinator executed the original 59-case submission
`829bb11dd8e047f37d24c7c238101b22dd7c44df8bd57e2771e691c68ae16f21`.
The receipt was independently read back and its SHA256 verified here:

`Temp/gltf-validation/direct-gpu/material-core-first-186/probe-01-editor_direct_gpu_tests/receipt-01.json`

SHA256: `0da83949e97504dcc493158bf7f912d928b288827ebf3b5f8653ff35c74d59d2`.

| Shoe suite | Passed | Failed | Scope |
| --- | ---: | ---: | --- |
| MaterialsVariantsShoeCoreTests | 40 | 0 | Source/compiler/reader CPU data |
| MaterialsVariantsShoePlanTests | 12 | 0 | CPU GPU-staging plans |
| MaterialsVariantsShoeGpuChecks verifier controls | 5 | 0 | Synthetic checker sensitivity |
| MaterialsVariantsShoeGpuProbe contract | 0 | 2 | Actual missing runtime API |
| Total | 57 | 2 | No actual Shoe GPU readback/image gate |

The actual failures were absent public `void GltfGpuInstance.SetVariant(int?)`
and absent readonly `int? ActiveVariant`. These are preserved as red evidence.
The encompassing batch contained 186 cases / 180 passes / 6 failures across
multiple models; its totals must not be relabeled as Shoe totals. The batch
completed without compiler error, incomplete case or cleanup error.

Loaded producer identities:

- Engine MVID `04b86c2e-4406-422e-a466-b61ec13f1f86`.
- Gltf MVID `1de213d3-1748-43d1-9a16-92a13bc9277d`.
- Helper MVID `4a873d08-280e-43f7-aad1-ab1e6f09331a`.
- Request SHA256 `227783a5589ef1ebdc6062230aff48125121585f3d06973dd7eebaf2dcd76aa5`.

The original eight model source/oracle hashes remain unchanged. Earlier frozen
acceptance/design documents retain their preparation-time unexecuted status;
this file records the later receipt without rewriting that source freeze.

## Isolated implementation, not yet runtime acceptance

Root reviewed the new isolated pure variant-state implementation and Fox agreed
its generic lease/state interface. The combined producer stage freezes:

- `engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfMaterialVariantState.cs`:
  `f0d23cc3ad283eed71f6cbe7fdc91c7a8b2e76eec8abca6096f9fbe2c6151546`.
- `engine/Tests/Sandbox.Test.Unit/Resources/GltfMaterialVariantStateTests.cs`:
  `e1460ed044a6d5cbde4f203ec6e82a024c848ac168e9cb8c853fd580ad9fe251`.
- `implementation-submission.json`:
  `f1c2fb182273f6ff803428584b42bf61f500f5c9327aa73dfbdcda58a6920d8f`.

The 27 focused unit cases were written first and remain unexecuted pending the
actual new Engine stage. They cover mapping/fallback, complete row preflight,
owned/borrowed lifetime, rollback and cleanup errors, creation failure, shared
resource validation per primitive, thread/reentrancy, snapshots, repeated
switches, default-null identity, empty/global-index validation, source UV/pose
preservation and borrowed document lifetime. No replacement Engine source was
compiled into a helper to simulate an installed change.

Fox owns the shared backend wiring. This pure state component does not itself
provide the missing public prototype methods, submitted material identity, shader
or texture sampling, or source-material image acceptance. A complete outer draw
snapshot must atomically bind the material selection with each primitive's
consumed-role UV plan and normal-map basis context. Index-cached material
resources must not retain a foreign primitive's source UV stream references.

## Native override precedence remains queued

The single native component matrix uses cached `Model.Cube`, two stock material
copies and the accepted NativeCapture route. It has ten matrix rows, seventeen
captures and seven synthetic classifier checks. Current separate request:

`native-precedence-submission.json` SHA256
`6190da9f25ab0fbc05e01ed773a78b4a33132a7500f8c2a1ec3235e49988737f`.

The request preserves the previous submission/dependency identities. The reviewed
capture helper update to `35cf8663b61fe62dde41a3f5c157e555889ac0b8122e59d591349cbd64139161`
adds orthographic/clip/light metadata without changing the accepted route. Its
probe source remains `506d8564c05fb4b521355c00cc636b6c2d3be688652c3c27e1a1278e2c48ef08`.
No native winner is claimed before execution; tagged/alternate-group behavior
is outside this bounded cube matrix. Whole/slot public-picker policy must wait
for the actual native receipt.

## Remaining gates

Actual SetVariant/ActiveVariant draw wiring and red-to-green evidence; the 27
focused production unit cases; owned native material release/rollback; matching
per-slot material/UV/basis publication; source normal and occlusion support;
actual Shoe GPU readback; default/all-variant/reset/two-instance images with
fresh controls; public Model.Load/vmdl behavior; optimized portable cache and
Windows/model-editor parity all remain distinct gates. Animation cadence is not
applicable to this static asset.

This worker has not run a build, editor, shader compilation or native capture.
