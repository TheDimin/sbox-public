# SimpleInstancing current validation

Owned scope is this folder. Corpus revision is `90d7ede14c7e280af263824604b427a1ca02cb66`.
The source files are CC0-1.0 (Marco Hutter, 2023), as recorded in the pinned
[upstream metadata](../../../../../Temp/gltf-reference/glTF-Sample-Assets/Models/SimpleInstancing/metadata.json).
No upstream file is changed or invented.

## Actual source and independent oracle

The actual forms are `glTF/SimpleInstancing.gltf` plus its 5,720-byte external
BIN, and `glTF-Binary/SimpleInstancing.glb`. Both are independently pinned by
SHA-256. The upstream GLB JSON equals source JSON after removing the buffer URI,
and its BIN equals the external BIN exactly. Each also runs through the real
`GltfCompiler.Compile` for its derived portable valid GLB (`compiled_glb_c`).

`../../../../../engine/Tools/Gltf/direct-gpu/models/SimpleInstancing/author_reference.py` uses only Python JSON/struct/math. It decodes every raw
accessor and freezes its expected values and complete view hashes in
`reference.json`. The C# oracle implements quaternion rotation and affine
normal transforms directly, without the producer's evaluators or quaternion
matrix constructor. It checks every one of the 3,000 positions and 3,000 normals
per runtime placement, retaining source vertex correspondence and bound indices.

One node with default identity transform uses one shared cube primitive: 24
vertices, 36 UINT indices, POSITION/NORMAL FLOAT VEC3, no authored materials,
textures, skins or animation. All accessor offsets are zero and normalization is
false. Buffer-view offsets are 0/144/432/720/2220/4220; tight strides are
4/12/12/12/16/12. TRANSLATION/ROTATION/SCALE accessors contain 125 elements.

Instance index is `25*x + 5*y + z`; translation is `2.5*(x,y,z)`, scale is
`1+(x,y,z)/4`, each coordinate 0..4. Raw quaternions are authoritative. The last
is `(0.5,0.5,0.5,0.5)`, which rotates 120 degrees and maps source vertex `(1,0,0)`
to `(10,12,10)` after scale/translation. The README's 90-degree final-angle
description is inaccurate. Independent analytic rounded-quaternion checks cover
all 125 instances. Full-affine shear is necessary to distinguish forward normal
transforms from inverse transpose: the source face normals alone cannot expose
that fault under each instance's rotation and axis scale.

## Runnable inventory

All names below use namespace `Sandbox.Gltf.DirectGpu.SimpleInstancingValidation`.

| Entry | Rows/states | Scope |
|---|---:|---|
| `SimpleInstancingCoreTests.RunAll(checkoutRoot)` | 40 | CPU/data: 7 per four forms, plus 12 legal/negative/default/capability fixtures |
| `SimpleInstancingPlanTests.RunAll(checkoutRoot)` | 12 | Shared base primitive staging, 3 per four forms |
| `SimpleInstancingExpansionTests.RunAll(checkoutRoot)` | 29 | Production instancing CPU plan: ordered rows, provenance, defaults, valid affine composition, malformed/fallback and interleaved SHORT fixtures |
| `SimpleInstancingBackendTests.RunAll(checkoutRoot)` | 12 | Actual Engine CPU asset preparation: identities, shared primitive, no device allocations, all-instance bounds |
| `SimpleInstancingRigLocalTests.RunAll(checkoutRoot)` | 12 | Actual Rig local matrices, tiny/reflected/zero winding, invalid affine rejection |
| `SimpleInstancingAffineReference.RunOracleControls()` | 12 | Independent double position/normal/tangent/handedness oracle controls only |
| `SimpleInstancingGpuChecks.RunContract()` | 1 | Instance identity API must exist; absent API is failed/pending |
| `SimpleInstancingGpuChecks.RunVerifierControls(checkoutRoot)` | 10 | Synthetic checker sensitivity only, never GPU evidence |
| `SimpleInstancingLoaderPolicyTests.RunAll(checkoutRoot)` | 14 | Required/optional selection, fallback/rejection and explicitly unsupported skin combination policy |
| `SimpleInstancingPublicTests.RunResources(checkoutRoot)` | 8 | Four actual source/compiled forms: public metadata, cache, bounds, material and cold reload |
| `SimpleInstancingRenderConcurrencyTests.RunAll()` | 4 | Ungated corruption control, per-instance exclusion, retirement and exception release |
| `SimpleInstancingGpuProbe.Run(checkoutRoot, world, computeMaterial, waitForActualFrames, materialResolver=null)` | 20 | Five actual-readback states per four forms |
| `SimpleInstancingAffineProbe.Run(checkoutRoot, world, computeMaterial, waitForActualFrames, materialResolver=null)` | 6 | Explicit synthetic authored-tangent fixture: regular/reflected/tiny/singular instances, three placements, source plus derived compiled fixture |
| `SimpleInstancingPublicTests.RunRenderer(...)` | 4 | Public Model.Load/ModelRenderer, named default compute, all125 readbacks and component deletion |

The GPU states are rest A, sheared B, mirrored A, unchanged B after A changes,
and restored A after B disposal. They require exactly 125 distinct `InstanceIndex`
values 0..124 and no ordinary base mesh, all vertices/normals, actual bound index
readback, mirrored winding, single immutable primitive/input/material resources,
and independent mutable output/placement resources. No probe requires a specific
number of authored-instance SceneObjects or a particular batching strategy.

Negative controls reject dropped/extra/duplicate instances, wrong output field
offset, ignored instancing, missing/double placement, forward normal under shear,
wrong winding and CPU-submission index metadata. Defaults omit each TRS semantic,
and a custom count-only attribute checks all-default identity transforms. Legal
signed normalized BYTE/SHORT rotation fixtures supplement the actual FLOAT data;
they are explicitly synthetic, not claimed upstream variants.

Original-sample GPU checks retain absolute bounds of 0.0005 engine inches per
position component and 0.000002 per normal component. The synthetic affine probe
retains 0.0005 inches, 0.000003 normal/tangent and 0.000001 handedness limits.
No threshold was changed to obtain the results below.

## Extension audit (primary sources, 2026-11-05)

[Pinned current extension text](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/extensions/2.0/Vendor/EXT_mesh_gpu_instancing/README.md)
(blob `22c33de18c9329d1316747eb6d0a7e01e064e9c6`) labels the extension complete
and ratified. At the [core baseline repository pin](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/extensions/2.0/Vendor/EXT_mesh_gpu_instancing/README.md),
its text (blob `d8900b0c21c8f7b6d28878bba72f720de1015178`) was experimental.
The GitHub connector verified both exact revisions after web cache misses.
Neither has a separate extension version field; both target glTF 2.0.

Its TRS accessors are optional, share one count, and apply before node world
transform. FLOAT VEC3 translation/scale and FLOAT or signed normalized VEC4
rotation are valid. An instanced node suppresses its ordinary mesh draw. The
specification recommends listing the extension as required; this actual sample
lists it only in `extensionsUsed`. Unsupported optional reporting/fallback is a
separate CPU case and never selected-instancing acceptance. Default material
factors follow the [core material specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#materials):
white base, metallic/roughness 1, opaque and single-sided. Source material image
fidelity remains pending. Neither audited revision forbids mesh nodes that also
have a skin. That combination is valid but unimplemented here; temporary
rejection remains a required FAILED/PENDING acceptance case, never an exclusion.
See `extension-audit.json` for exact source identities and capability gates.

## Current executed evidence

The model worker executed the following under the shared editor lock and host
resource guard on the coherent installed Engine `ad6a78a7e4f2664fe1f74b1567dde6d15d1519ad78dbd1e21efbb21ebfa2ebd1`
(MVID `b5b66ab1-06f5-4aee-b816-92b6b197666e`) and Gltf
`b86cb72e0c0e0f3c23af45bd115f68760655d1754d663f41df62a45e136f9e8b`
(MVID `88f89cfc-81b4-4744-9888-ec746058a6de`).

- **154/154 CPU/control/resource rows passed**, zero failed/incomplete/cleanup.
  [Receipt](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/SimpleInstancing/coherent-units-154/probe-01-editor_direct_gpu_tests/receipt-01.json), SHA-256 `6f233a4584ba92fdae3073c81c54e098922cfbf6d3e3f6b41bdd991d299adccd`.
- **30/30 GPU states passed**: original20, synthetic affine6, public4. The runner
  reports14/14 assertion cases:11 repeated checker controls plus3 GPU wrappers;
  these11 controls are already included in the154 inventory, not new unique rows.
  [Receipt](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/SimpleInstancing/coherent-gpu-30/probe-01-editor_direct_gpu_tests/receipt-01.json), SHA-256 `cbd4458b849110ff08a05a3ada7279fb713c28f7bec1fbfae8b95672f9bd9136`.
- Every original/public state checked125 source IDs,3,000 positions/normals and
  4,500 actual bound indices. Original20 maximum component errors were
  `0.00010870577523292013` inches / `1.7860918744005971e-7` normal. Public4 maxima
  were `0.00003914153057849035` inches / `1.1780402431327275e-7` normal, through
  ordinary Model.Load/ModelRenderer and `gltf_stock_sampler.shader` materials.
- Synthetic affine6 covered144 positions,96 invertible normal/tangent frames
  and48 singular finite-output frames per state. Maxima: `0.00016520248664164683`
  inches, `7.031433846282908e-8` normal, `9.62831764317329e-8` tangent, zero W error.
- The ordered native suffix **RiggedFigure/glTF → RiggedSimple/glTF →
  SimpleInstancing/glTF** completed24/42/1 frames with confirmed cleanup after
  every case. SimpleInstancing submitted125 meshes,3,250 draws, revision2=2 and
  zero callback errors. [Capture](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/SimpleInstancing/coherent-original-suffix-native/cases/SimpleInstancing/glTF/capture.png).
  The final console audit initially failed before dispatch because the client
  validator rejected a numeric limit; one read-only audit using its default
  limit completed successfully without repeating captures.
  [Assessment](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/SimpleInstancing/coherent-original-suffix-audit/assessment.json).

Final read-only health check passed: editor idle, no unsaved scene changes, zero
compile errors and no matching Error-level console entries.
[Health status](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/SimpleInstancing/coherent-final-health/status-after.json).

Fresh public native captures exist for all four real source/compiled forms in
the GPU receipt's `existing_public_renderer_result` / capture measurements.
Empty-before and deleted-empty coverage controls passed for every form. Their
independent orthographic fit is numeric diagnostic presentation, distinct from
the512px recipe-based source capture above. The worker inspected that source
image: the lattice is visible and uncropped; final visual approval is the user's.

`ReadInstances` remains the sole production instance decoder; original accessor
provenance is retained separately from CPU-converted row matrices. The per-instance
submission/retirement gate fixes the proven concurrent attribute mutation race.
The ordered suffix did not reproduce the native SIGSEGV; exact attribution of
the intermittent fault and the full original batch remain narrower pending
integration questions. See `CRASH_FIX.md` for that distinction.

Remaining separate gates are user visual/material parity, hardware instancing
and raw instance-stream GPU upload efficiency, performance/load-memory/platform
comparisons, and the valid skin+instancing combination. Rejection-policy tests
do not turn that combination into supported rendering. This static sample has
no animation cadence claim; source/compiled agreement alone is not an oracle.
