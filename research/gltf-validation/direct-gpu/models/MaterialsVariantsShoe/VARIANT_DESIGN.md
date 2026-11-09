# Bounded variant selection proposal

This document and `../../../../../engine/Tools/Gltf/direct-gpu/models/MaterialsVariantsShoe/MaterialsVariantsShoeVariantTransactions.cs` are additive
preparation. They do not modify the original eight-file / 59-case freeze in
`submission.json`. No shared backend source is owned or changed here. Execution
belongs to `/root/gpu_validation`; shared edits require root's handoff after an
actual missing-API failure. The two existing reflection cases remain explicit
pending failures, not exclusions.

## Source audit and limits

The original pin and all eight frozen file hashes were revalidated after the
restart. The reader's `GltfDocument.GetMaterialForVariant` already supplies the
required primitive mapping, grouped variant, null and unmapped fallback behavior.
The current direct GPU asset resolves only `Primitive.Plan.Material` during
creation, stores `Primitive.Material` on the shared asset, and binds that same
value in every instance's draw. `SetVariant` and `ActiveVariant` remain absent.

The existing material lease services are suitable building blocks, not completed
variant integration. `GltfStockMaterialAdapter.Acquire` leases material and
texture resources together, retains owner context, and rolls back acquisitions
on failure. `GltfMaterialLease.Dispose` is idempotent and verifies the thread
before releasing. The cache requires shared materials to remain immutable;
acquire/release is disallowed during rendering. The GPU asset already refuses
disposal while instances are live and owns its acquired material leases.

All three authored Shoe materials have normal and occlusion textures, which
`GltfStockMaterialAdapter.Prepare` currently rejects. Selection tests using
explicit borrowed diagnostic materials cannot turn this into a source material
fidelity pass. JPEG decode, normal/ORM application, actual samplers, source PBR
images and texture transforms are separate gates.

## Proposed minimal state and transaction

Keep public prototype `void SetVariant(int?)` and readonly `int? ActiveVariant`
on `GltfGpuInstance`. Variant selection is asset-wide for one instance, while
material resources are asset-owned and immutable. Stable draw slots remain
node/mesh/primitive identities, independent of variant names and material IDs.
This prototype does not yet define a public ModelRenderer variant/group alias.

For each request on the safe main thread, outside graphics:

1. Validate the requested index and resolve **all** selected primitive material
   indices before any material acquisition or visible-state change. Use the
   reader's selected-extension behavior; an invalid supported variant fails.
2. Deduplicate resolved indices. Reuse already acquired asset entries. Stage
   newly needed entries and their owned leases in a private transaction. An
   explicit resolver returns borrowed materials, which this transaction never
   destroys. A missing/invalid resource fails; do not substitute white silently.
3. Validate each staged material before publication. On failure release every
   newly owned lease, attempt every cleanup even if one fails, retain the cause,
   and leave the previous active index, selection vector and existing cache
   unchanged. No partially resolved row may become drawable.
4. After successful preflight publish one immutable selection vector and its
   revision. A draw captures this vector once for all rows. Publish the active
   variant and vector as one state, not separate mutable per-draw assignments.
5. Keep successful material cache entries until asset disposal. Cache identity
   is material index/default within this asset, backed by the existing descriptor
   cache. Repeated switches acquire nothing. Storage is bounded by distinct
   reachable source materials plus an implicit default, not switch count or
   instance count. Preserve reader/resource budgets for large valid assets;
   do not invent a smaller conformance-excluding cap.

The proposed policy is lazy acquisition of previously unselected variants. This
avoids making an unused expensive/unsupported material a default-load failure.
The new runtime test intentionally specifies rollback of staged cache entries;
root must approve that policy before implementation or execution.

Changing only a material selection must not advance the pose clock, alter
`PoseRevision`, rerun deformation, rewrite source UV/vertex/index buffers, change
placement, or mutate shared material parameters. Any material-role UV set and
transform belong to its immutable material binding, not a globally baked source
UV buffer. The TextureTransform owner is preparing that separate binding design.
Repeated selection may be an early no-op; draw metadata must still identify the
actual vector/revision used. Full sampler/material appearance remains separate.

If the extension is not supported/advertised, ordinary reader core fallback is
already covered. The precise API behavior of selecting an unadvertised variant
is not established here and must be reviewed before exposing a public picker.
Likewise resource reload/generation replacement and recovery from externally
invalidated borrowed materials need their existing lifetime contract, not a
silent fallback or mutation of a shared resource.

## Tests prepared before shared implementation

`MaterialsVariantsShoeVariantTransactions.RunFixtureCases(checkoutRoot)` has four
CPU cases for a derived three-slot legal glTF fixture. Its independent matrix is:

| Active variant | Mesh 0 | Mesh 1 | Mesh 2 without mappings |
| --- | --- | --- | --- |
| null | 0 | 0 | 0 |
| 0 | 1 | 2 | 0 |
| 1, valid but unmapped | 0 | 0 | 0 |
| 2 | 2 | 1 | 0 |

`RunVerifierControls()` has six synthetic observation-checker cases: a valid
three-slot vector, wrong source material index, wrong actual material identity,
mixed committed revisions, stale frame, and reader-resolution metadata offered
as draw proof. These are checker sensitivity only.

`RunBorrowedRuntime(checkoutRoot, world, computeMaterial, diagnostics,
waitForActualFrames, observeCompletedBindings)` has eight sequential runtime
checks: shared default acquisition; injected failure while resolving the second
new material; complete retry; repeated cache reuse; another instance's different
combination; unmapped/null fallback; invalid-index atomicity; and borrowed-material
survival through instance/asset disposal. The fault is injected after the first
new material resolves, so a row-by-row commit is observable as a wrong binding.
It checks geometry and pose revision remain unchanged.

The observer is a coordinator-supplied, audited diagnostic boundary, not a new
promised backend API. It must report the exact material object/index passed to
the completed draw's `SetRenderState`, frame and committed selection revision.
Reconstructing its rows from `ActiveVariant` or `GetMaterialForVariant` is
explicitly rejected. Real texture sampling and pixel parity are not established
by draw-submission metadata. No observer currently exists, so this runtime probe
is prepared and unrun, pending interface review and actual implementation.

Owned stock-lease rollback/release still needs a separate test seam that records
actual asset cache acquisitions and final releases. Required checks: two
instances do not multiply asset leases; a failed second acquisition releases the
first staged owned lease; disposing one instance preserves another; live-instance
asset disposal refuses without partial cleanup; final disposal releases each
entry once; a failure in one release still attempts remaining releases. These
requirements are not satisfied merely by borrowed-material survival or existing
generic cache unit tests. Root can assign the narrow owned-lease seam with the
shared implementation after observing the current red.

## Existing override precedence must be measured

The read-only source audit establishes routing and call order only:

- `ModelRenderer.HasMaterialGroups` disables group selection while global
  `MaterialOverride` is non-null; `UpdateObject` sends group or global override.
- `ApplyMaterialOverrides` submits tagged entries before indexed entries.
  The global setter calls `UpdateObject` alone, while other mutation/lifecycle
  routes replay saved entries. Last-managed-write inference is therefore unsafe.
- `SceneObject` delegates global/tag/group/index writes to separate native APIs;
  their native winner rule is not present in this repository.
- `MaterialAccessor.GetOriginal` and `GetOverride` report original/saved state,
  not the final bound material. Existing RendererTests verify property routing
  and validity, not combined override images.
- `ClearMaterialOverrides` clears global/tag/native state but retains the
  MaterialAccessor dictionary, which a later lifecycle replay can reapply.

Relevant source: `ModelRenderer.cs` (HasMaterialGroups, UpdateObject,
OnSceneObjectCreated), `ModelRenderer.Materials.cs` (setters and replay),
`MaterialAccessor.cs`, `SceneObject.cs` (native delegation), and
`engine/Tests/Sandbox.Test.Integration/Scene/Components/RendererTests.cs`.

Root assigned exactly one bounded executable native matrix to this worker:
`NativeMaterialOverridePrecedenceProbe.Run(checkoutRoot)`. It reuses the accepted
`GltfGpuTriangleGateTests.NativeCapture` route with Normal visualization,
postprocessing, an owned light, exact fresh target dimensions, and real frames.
It uses cached native `Model.Cube`, exactly two owned stock material copies, and
ordinary ModelRenderer creation/setters/EditorTick/disable-enable/deletion.
It never invokes direct SceneObject setters to manufacture a precedence result,
creates no Model/Mesh, and leaves cached vmdl/original material unchanged.

Three single-setting image controls establish default, whole-A and slot-B.
Ten rows measure both setter orders, clear-slot/clear-whole/clear-all, reset
alone, and ordinary component disable-enable replay. Fresh empty-before,
deleted-empty controls and a restored default component are mandatory. Winners
are classified from actual pixels against the controls, not predeclared from
source order. Camera mismatch, black/mixed/ambiguous images fail. Prewritten
thresholds: foreground mask IoU >= .995, winner mean RGB error <= .02 and next
candidate error margin >= .03. Seven synthetic classifier controls cover
valid winners, mixed image, wrong camera, black target and wrong extent.

The single-slot cube does not establish tagged-user-attribute or authored
alternate-group precedence. Its original material attributes are not mutated or
invented. Those remain explicit follow-up native matrices if the public adapter
needs them. Orientation's owner will not duplicate this probe; Fox's new picker
must wait for the actual native receipt before adopting a whole/slot rule.

## Current evidence

All additions here are source preparation and unexecuted C# tests. The original
59 cases remain frozen and queued. No editor call, build, shader job, native
capture, material switch, owned-lease lifecycle or override winner was executed
by this worker. The earlier successful shared raw triangle/index-negative
receipts prove their own scope and do not pass these model/material gates.
