# MaterialsVariantsShoe acceptance

Owner: `/root/gpu_material_variants`. Execution coordinator: `/root/gpu_validation`.
Shared renderer owner: `/root/gpu_fox`. Only this model folder is owned here.

## Audited input

Khronos Sample Assets revision `90d7ede14c7e280af263824604b427a1ca02cb66`
contains exactly `glTF/MaterialsVariantsShoe.gltf` and
`glTF-Binary/MaterialsVariantsShoe.glb`. Both use optional
`KHR_materials_variants`, with no required extensions. There is one primitive,
13,540 vertices, 68,100 U32 indices, three materials, and three named variants
(`midnight`, `beach`, `street`). There are no skins or animations. Normal and
position accessors are FLOAT VEC3 in distinct tightly packed views; UV is FLOAT
VEC2. Explicit byteStride does not make these views multiattribute interleaved
data. POSITION is accessor 1; NORMAL is accessor 0. Root node 2 supplies the
uniform scale and translation through nodes 1 and 0.

Asset credit: Shopify, 2021, CC-BY-4.0. Upstream `LICENSE.md` SHA256:
`bb2ac1f41c9aef71cc58f6d8832db4ea553c758a2438416b837ec97fd5a7d0b5`.
No asset binary or image is duplicated into this folder.

`../../../../../engine/Tools/Gltf/direct-gpu/models/MaterialsVariantsShoe/author_reference.py` independently reads JSON/GLB and decodes raw little-endian
source accessors using Python stdlib. `reference.json` pins all source and
dependency hashes, all geometry-view hashes, all encoded JPEG identities, twelve
source/world/engine-space vertex samples, normalized world normals, and bounds.
No Sandbox evaluator or renderer output generated its expected values. Both
source containers have independently confirmed identical geometry and images.
The upstream screenshot lacks matched camera/light/exposure and is context only.

## Standard material behavior

The [ratified KHR_materials_variants specification](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_variants/README.md)
defines at most one active variant per asset instance. A mapped variant applies
its mapped material. An unmapped variant, absent primitive extension, or no
active variant uses that primitive's ordinary glTF material/default. A variant
index can map to any material index, and one mapping can list several variants.
Variant names are labels; neither names nor variant indices are material IDs.

Reader `GltfDocument.GetMaterialForVariant` already implements the selected
resolution path. The inspected direct GPU backend only resolves the authored
default into an asset-shared `Primitive.Material`; it has no instance variant
selector. A metadata/data pass is not evidence that materials switch in a draw.
Proposed prototype contract, pending root review and backend implementation:
`GltfGpuInstance.SetVariant(int?)` and readonly `int? ActiveVariant`. Material
resources/leases remain asset-owned; selection state is instance-owned. `null`
restores ordinary primitive behavior. Existing vmdl material-group and component
override behavior must retain their existing contracts; no shared/native files
are changed by this worker.

## Frozen callable cases

All listed C# cases are authored and unexecuted until the coordinator records a
receipt against actual loaded producers. Source forms are original glTF and
original GLB, each with a derived `GltfCompiler.Compile` GLB, four forms total.
The derived packaging form is not a new upstream variant or proof of optimized
portable `.glb_c`; the compiler currently preserves external image URIs in the
glTF-derived GLB, and the fixture supplies the pinned resolver.

- `MaterialsVariantsShoeCoreTests.RunAll(checkoutRoot)`: **40 cases**, CPU/data.
  Seven per form (28) plus 12 focused controls. Checks full geometry hashes and
  descriptors, independent raw/world samples, exact image payloads/material role
  links, default/reset/mappings, optional fallback, unsupported required reject,
  exact owned upload budget and disposal. Derived fixtures shuffle mappings,
  group variants, omit a mapping/default material/primitive extension, and reject
  duplicate or invalid mapping/request indices. These fixtures are not upstream
  variants. PBR role checks cover source sRGB/linear intent, not decoded JPEG
  accuracy, stock sampling or final material appearance.
- `MaterialsVariantsShoePlanTests.RunAll(checkoutRoot)`: **12 cases**, CPU staging
  plan, three per form. Requires shared `GltfGpuBackend.cs` plus dependencies.
  Checks actual stream descriptors and bytes, U32 topology order, source vertex
  mapping, runtime output declaration, generated tangent/color inputs and
  authored default. No GPU resource is constructed by these cases.
- `MaterialsVariantsShoeGpuChecks.RunVerifierControls(checkoutRoot)`: **5 cases**,
  synthetic checker controls only. Positive deliberately reorders fields/stride;
  negatives corrupt position offset, V orientation, winding and placement.
  Synthetic output must never be cited as actual GPU evidence.
- `MaterialsVariantsShoeGpuProbe.RunContract()`: **2 cases**, reflection contract.
  Missing SetVariant/ActiveVariant is a readable red result, without preventing
  other helpers from compiling. These cases currently describe a known pending
  API; do not mark them inapplicable or hide them with a skip.

Types are in `Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation`.
`RunAll`/`RunVerifierControls` string argument is the explicit engine checkout.
No setup property, native model, editor process or shader job is needed for
these pure case entries. Compile them against the actual loaded `Sandbox.Gltf`
and engine; do not embed replacement producer source. Expected authored total:
**59 cases**, of which two are known pending API contracts.

## Actual GPU and image gates

After the raw triangle gate, root's interface review and material selector
implementation, coordinator can invoke
`MaterialsVariantsShoeGpuProbe.Run(checkoutRoot, formName, world,
computeMaterial, waitForActualFrames, materialResolver = null)` once per form.
It requires real subsequent frame submissions and creates two shared-asset
instances with independent placement/selection. It tests several simultaneous
variant combinations, reset to null, unaffected output geometry, translated
placement and survival after disposing the second instance. The four returned
rows per form are state/geometry records, not four visual passes. A successful
ActiveVariant property alone does not establish which material was bound.

`MaterialsVariantsShoeGpuChecks.AssertSnapshot(snapshot, formOracle, tx, ty, tz)`
requires completed actual readback, declared output fields, every original index,
source-vertex provenance and twelve independent position/normal/UV values.
Only identity linear placement plus optional translation is covered here.
Proposed enforced thresholds, awaiting coordinator review: position 0.00002
inches per component, normalized normal 0.00002, UV 0.0000003. No tolerance has
been increased based on an observed failure. Full-affine generic backend tests
remain separate.

Pending material acceptance requires actual default/midnight/beach/street/null
captures in matched presentation, two instances holding different variants,
verified selected material bindings or independent image/sampling expectations,
and empty-before/positive/deleted-empty controls. An all-white diagnostic
resolver can establish geometry only. Full stock JPEG decode, linear ORM and
normal interpretation, repeat/filter/mip semantics, and final PBR fidelity need
their own measured acceptance. There is no animation/wall-clock gate for this
static model. Public `Model.Load`, existing vmdl behavior, optimized portable
cache, model editor and Windows gates remain with the coordinating workstream.

## Evidence status

Executed here: lightweight stdlib source-oracle authoring and pin audit only.
No C# case was executed, and no build, editor, shader compilation, GPU readback,
material switch, capture, public loading or vmdl regression was run by this
worker. Frozen hashes/callable entries are supplied to the coordinator separately.
