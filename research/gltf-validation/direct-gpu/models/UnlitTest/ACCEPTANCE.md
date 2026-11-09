# UnlitTest acceptance

Owner: `/root/gpu_unlit`. Coordinator: `/root/gpu_validation`. Shared backend:
`/root/gpu_fox`. Only this model directory is owned by this worker. Shared material
implementation needs a separate explicit file handoff after the raw triangle gate.

## Source and independent expectations

Khronos glTF-Sample-Assets commit `90d7ede14c7e280af263824604b427a1ca02cb66`.
Actual upstream forms are `glTF/UnlitTest.gltf` with `UnlitTest.bin`, and
`glTF-Binary/UnlitTest.glb`. There is no upstream embedded-JSON variant.

| Input | SHA-256 |
|---|---|
| `glTF/UnlitTest.gltf` | `b611ca0892cb1a0118e92b72ff52b3afed3e4903c15378daa72c4c252a0334e1` |
| `glTF/UnlitTest.bin` | `a6a81ec57560242f29ee2c2a94f452a923b5548bf256b6529b0c07dea3f5c907` |
| `glTF-Binary/UnlitTest.glb` | `e07b68c6fd9fbf73d372c610a681b89d6b013bdd1a506e46a411df82210a2481` |

Model/data attribution: Ed Mackey, Analytical Graphics, Inc. (2019), CC-BY-4.0;
retain upstream `LICENSE.md` and `metadata.json` when redistributing inputs.
The literal reference records all 96 source positions, 96 normals and 132 U16
indices without calling `Sandbox.Gltf`. Two nodes translate the same shape by
`-1.2` and `+1.2` source meters on X and bind distinct Orange/Blue materials.
World engine bounds are `(-39.37008,-86.61417,-39.37008)` to
`(39.37008,86.61417,39.37008)` inches under `(x,y,z) -> (z,x,y)/.0254`.

**Both** materials are unlit. There is no authored lit control. Both forms require
`KHR_materials_unlit`; unsupported required loading must reject it. Optional
fallback checks explicitly derive a fixture by removing `extensionsRequired` and
label it separately. The exact linear factors are `(1,.217637640824031,0,1)` and
`(0,.217637640824031,1,1)`. Do not replace their green factor with `127/255` or the
exact sRGB decode of that value. The README's approximate color names are not a
numeric oracle.

The [pinned Khronos extension](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/extensions/2.0/Khronos/KHR_materials_unlit/README.md)
requires the base-color product without lighting; alpha behavior and sidedness
still apply. The sample has no textures, vertex colors, animation, skin or morphs.

## Frozen callable inventory

- `Sandbox.Gltf.DirectGpu.UnlitTestValidation.UnlitTestCoreTests.RunAll(string checkoutRoot)`:
  **39** static result rows: three independent/reference controls plus nine cases
  for each of four forms (each actual source and each derived compiled GLB).
- `Sandbox.Gltf.DirectGpu.UnlitTestValidation.UnlitTestGpuVerifierTests.RunAll(string checkoutRoot)`:
  **10** synthetic checker-control rows. These prove verifier sensitivity, not a
  GPU execution. Controls cover arbitrary runtime field order, wrong position
  view, NaN, normal axis, winding, source mapping, color/UV defaults, missing and
  duplicate node identities.
- `Sandbox.Gltf.DirectGpu.UnlitTestValidation.UnlitTestGpuProbe.Run(...)`:
  **20 planned actual GPU measurements**, five per form: rest, translation,
  full-affine shear, mirrored full affine, and surviving another instance's
  disposal. Concurrent B buffers must stay byte-identical while A is updated.
  Coordinator supplies the current real `SceneWorld`, loaded compiled compute
  `Material`, actual rendered-frame waiter, explicit position/normal thresholds,
  and optional explicitly labeled diagnostic material resolver.
- `UnlitTestGpuChecks.Measure/RequireWithin`: completed actual vertex and index
  readback, every vertex, both nodes, inverse-transpose normals, independent
  triangle winding, exact generated white colors/zero UVs, dynamic field layout.
- `UnlitTestGpuChecks.ReferenceTriangles(int node, Matrix4x4 placement)`: independent
  literal triangle reference for native drawing; creates no Model or Mesh and
  must never become actual producer input.
- `UnlitTestImageChecks.Measure/RequireWithin`: separate source-material image
  gate. No rendered image result has been collected.

Proposed numeric GPU acceptance is maximum component position error `0.0002`
inches and Euclidean unit-normal error `0.00001`, including the fixed affine
placements. Generated color/UV defaults and topology match exactly. The probe
requires the coordinator to pass explicit thresholds; unreviewed measurements
remain pending. The pure CPU node-transform comparison uses `0.000001` meters.

## Source-material image gate

Keep camera, dimensions, source placement, exposure and postprocessing fixed.
Create independent constant-color references using the literal triangles, with
Orange and Blue identified independently. Both eroded interior masks need at
least 512 distinct pixels, at least three pixels away from silhouette edges.
Masks cannot come from the actual output being tested. Record the reference
shader/material identity and how exact linear factors reach its color output.

Capture default source materials with two substantially different scene-light
setups, and the same two setups with an explicitly derived lit negative control.
Both actual source images must agree with the constant-color reference and each
other. The lit control must show a measurable light response for each material;
otherwise the lighting-invariance setup is invalid. Propose mean RGB difference
`<=1` byte/channel, maximum `<=3`, and lit-control mean change `>=8` bytes/channel,
subject to review of the native capture transfer function. The image checker
requires explicit thresholds and preserves the measured values.

Use the common empty-before, positive and deleted-empty-after controls and
matched source/compiled captures. A normal debug-mode geometry image cannot
satisfy this source-material gate. The upstream screenshot has unmatched
camera/exposure and is review context only.

## Observed and pending evidence

On 2026-11-05 the lightweight `python3 .../audit_reference.py` audit passed four
cases: input hashes, complete literal geometry versus both source forms,
material/node bindings, and basis/unit/color negative controls (0.003 seconds).
This is independent reference/provenance evidence only.

No C# helper compilation, production-parser execution, GPU submission/readback or
image capture has been performed by this model worker. At audit time the shared
stock adapter explicitly rejected `material.Unlit`. Therefore four forward
`default_stock_material_preparation_accepts_required_unlit` rows are expected to
fail until a real source-material implementation is staged. Do not convert the
known failure into an expected pass or use a diagnostic resolver to satisfy it.

Public `Model.Load`, model-editor behavior, Windows and vmdl load/memory parity
remain separate global completion gates. No wall-clock/animation gate applies
to this static sample.

### Actual coordinator execution

The original frozen 49-case UnlitTest subset has now executed against loaded
Engine `04b86c2e-4406-422e-a466-b61ec13f1f86` and Gltf
`1de213d3-1748-43d1-9a16-92a13bc9277d`: **45 passed, four failed, zero incomplete**.
The enclosing combined material receipt is
`Temp/gltf-validation/direct-gpu/material-core-first-186/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `0da83949e97504dcc493158bf7f912d928b288827ebf3b5f8653ff35c74d59d2`.
Its whole-batch totals are 186 planned / 180 passed / six failed; those are not
the UnlitTest totals.

All four UnlitTest failures are the forward default-stock-unlit preparation rows,
one per actual/compiled form, with the stock adapter's explicit unsupported-unlit
exception. The other 35 core/staging and ten synthetic-verifier rows passed.
This closes those CPU/checker rows only, not GPU/material image acceptance.

The newly assigned CPU implementation lives in
`engine/Sandbox.Engine/Resources/Model/Gltf/Materials/GltfUnlitMaterialPlan.cs`,
with 33 focused managed test cases in `GltfUnlitMaterialPlanTests.cs`. It preserves
the authored source separately from per-primitive context, consumes only the
canonical BaseColor UV/encoding role, and makes native template limitations
explicit. The combined `fee5` engine build completed successfully; those 33
tests, the additional four installed-plan model rows, and candidate rendering
remain unexecuted. The default adapter is unchanged. Production support cannot
be inferred from this isolated CPU implementation or from the build result.

### 2026-11-06 installed public acceptance

The actual required-unlit rejection was reproduced in the 57-case baseline:
49 passed / 8 failed / 0 incomplete. Four public `Model.Load` cases rejected
`KHR_materials_unlit`; four old direct legacy-stock preparation rows rejected
unlit. Baseline receipt SHA256
`9d167f9c484cebefa0d5b622d13adfa897f4d6eefd38803a46497b0cd669e47f`.

The final **installed named shader and public API** run passed **12/12 cases**,
zero failed/incomplete/cleanup errors, on stage01459 Engine
`a7a46583-fd03-4f19-8e65-63cfa5a020c8` and Gltf
`88f89cfc-81b4-4744-9888-ec746058a6de`. Receipt:
`Temp/gltf-validation/direct-gpu/today-unlit-public-01/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA256 `5569df0a667cb9b8201bf9bc08ff44a9e8a35351f81e0be0f84cff72d1066902`.
These are 4 public loading cases, 6 scene/dispatch/explicit mixed-limit cases,
1 four-form public rendering orchestration and 1 34-measurement public feature
orchestration. The 34 measurements are supplemental fixtures, not 34 upstream
model variants. Both upstream formats and each portable compiled form load with
required unlit and render through ordinary `ModelRenderer`, with no native Model
placeholder and the named `shaders/gltf_unlit_five_uv.shader` resource.

All four true forms have two independent interior masks of 3664 pixels each,
exactly zero reference RGB error and zero light-change error. The ordinary lit
negative responds by 162.086/142.123 mean RGB bytes. Actual GPU readback covers
all 192 vertices and 264 indices: maximum position component error
1.9073486e-6 inches, normal error 8.4293696e-8. Bound indices report actual GPU
readback. Public stock-vmat override/restoration, disable/re-enable, deletion and
resource identity controls pass. Source material colors stay separate from
white vertex defaults. The current public capture was visually inspected and
shows uniform orange/blue surfaces.

The source-adapter candidate additionally passed 20 affine/mirror/independent-
instance/disposal GPU measurements on the earlier producer. Those remain labeled
prior evidence and were not counted as new-stage public measurements. Its actual
shader culling defect was fixed by material-controlled CullMode; an invalid
TEXCOORD_9-only test fixture was corrected to legal contiguous sets. Historical
failures are preserved in their original receipts. Full provenance, current
captures and exact hashes are in this folder's stable `result.json`.

**Bounded limitation:** public scenes with reachable mixed lit and unlit materials
still reject explicitly before resource publication. Unused unlit does not change
a lit selected scene. Thus UnlitTest itself passes, while full extension-wide
mixed-material support remains pending. The prepared mixed/variant UV fixture is
unrun. The old direct legacy stock adapter still rejects unlit; the proven public
route uses the separate source-material adapter. Windows, editor parity, vmdl
performance and final user source-versus-engine appearance approval remain global
gates. No animation applies to this static model.

### e12 mixed-material follow-up supersedes blanket mixed rejection

The shared packet integration now keeps each legacy lit material's source UV
bake/input buffers together with its selected material while preserving projected
unlit packets. The worker's changed mixed-only gate passed **1/1** on installed
Engine `dbdf65e7-d6b5-4587-b777-843a09d62f96` (e12), Gltf unchanged. Receipt:
`Temp/gltf-validation/direct-gpu/today-unlit-mixed-02/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA256 `267dcdfc54e223f807aad3db8e2477b18a43baca0eaed71500be6e085067d26b`.

This verifies two supplemental public default source/compiled mixed forms and
six separately explicit-reader variant poses (`null -> 0 -> null`), two raw stock
vmat override/restoration controls, four preexisting standalone lit-only
baselines, independent instance bytes/variant state and deletion. All twenty
image comparisons are exactly zero RGB error; maximum literal position error is
7.6293945e-6 inches and maximum baked/raw UV error is zero. The independent source
UV=.25 becomes .75 for the default transformed lit material, .25 for its alternate,
and stays raw .25 under a stock override. The source test's first attempt used an
unsupported node-visibility test control; it was corrected by capturing independent
references before instances exist, without changing producer or thresholds.

The two formerly negative active scene assertions were updated to positive
independent lit/unlit slots and rerun narrowly: **2/2**, zero failures/incomplete/
cleanup errors, on e12. Earlier01459negative receipts retain their original
historical scope. No unchanged12case family was rerun solely for the new build.

The blanket mixed rejection limitation is therefore superseded for the legacy-
supported opaque lit subset. Public `KHR_materials_variants` remains unsupported;
those six poses use an explicitly selected reader capability and establish only
the underlying material-packet behavior. Existing normal/AO/emissive/non-opaque
legacy lit limitations, full format/editor/Windows/performance gates and user
appearance approval remain separate. Primary UnlitTest captures keep their actual
01459producer identity; new mixed controls keep e12provenance in `result.json`.
