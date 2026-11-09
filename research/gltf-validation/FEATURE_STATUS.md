# glTF implementation and test status

## Status on 2026-11-08

Code moved out of `research/` on 2026-11-08. The glTF tooling now lives in `engine/Tools/Gltf/`. The move map and the rules for old paths are in [CODE_LOCATIONS](CODE_LOCATIONS.md). Docs, receipts, manifests and fixtures stay here. The move changed no engine code.

Checked after the move:

- Pure glTF suite (`engine/Tests/Sandbox.Test.Gltf`): 886 cases, 883 pass, 3 fail. The three failures are texture-transform rotation tests. They come from the uncommitted edit described in item 1 below.
- Python suites in `engine/Tools/Gltf/scripts`: 14 of 15 modules pass. `test_visual_comparison_identity` fails, and it failed before the move. The capture-diagnostics suite passes.
- Node UI tests pass `node --check`. Their browser runs were not repeated.
- `run_editor_probes.py` finds its probes, guard, corpus manifest and transport script in the new layout. It was not run against an editor.
- No editor or native job was started.

Open items, most urgent first:

1. **Texture-transform rotation sign.** `engine/Sandbox.Gltf/GltfDocument.Materials.cs`, `TransformUv`, has an uncommitted sign flip. Its comment says it matches the Khronos Sample Renderer. The committed code, the committed tests and the glTF spec's counter-clockwise rotation all use the other sign. The Sample Renderer claim is not verified. Pick the convention, then revert or fix the edit, and re-run the three tests and the arrow-direction captures.
2. **Engine build output.** Earlier today the managed build compiled the uncommitted glTF changes with 0 errors and 0 warnings. It needed `DOTNET_ROOT=/home/damian/.dotnet`, because the CodeGen apphost does not find the .NET 10.0.13 runtime under `/usr/share/dotnet`. That build overwrote 17 DLLs in `game/bin/managed/`, and the previous copies were not backed up. Rebuild through SboxBuild before the next editor run.
3. **Missing receipts.** At least 13 receipt files cited here are not on disk. One example is `Temp/gltf-validation/editor-corpus/333d63a4057a44bfb7e85eab84aed97c/receipt.json`. `Temp/` is gitignored, so these may exist only on the machine that produced them. Find them, or mark the claims unverified.
4. **Conflicting case counts.** This document cites 732 passed and 28 failed for the actual-editor suite. `engine/Tests/Sandbox.Test.Gltf/README.md` cites 486 cases. The pure suite now has 886. Pick one dated source and cite it everywhere.
5. **Camera import.** `engine/Sandbox.Engine/Resources/Model/Gltf/Cameras/` is empty. The candidate `GltfCameraImport.cs` and its patches are under `engine/Tools/Gltf/direct-gpu/models/Cameras/draft/`. Nothing is merged. The execution queue records a private Engine build that passed. The merge is pending review.
6. **Probe shaders in shipped content.** `game/core/shaders/gltf_geometry_probe.shader` and `gltf_sampler_probe.shader`, with their compiled `_c` files, ship in `game/core`. They came in with commit `8967e1eea`. Move them out of shipped content.
7. **License notice.** `engine/Sandbox.Gltf/ThirdPartyNotices.md` does not list MikkTSpace. Its folder `engine/Sandbox.Gltf/ThirdParty/MikkTSpaceSharp/` has its own `LICENSE.txt`. Add it to the main notice.
8. **Codec safety.** The Draco, meshopt, KTX and Basis wrappers make no untrusted-input safety claim. No audit has been done.
9. **Old paths in records.** Receipts and frozen requests keep their pre-move paths. Some source hashes changed because path strings in 254 moved files were edited. A frozen selection that names a helper under `research/gltf-validation/direct-gpu/` no longer dispatches. Re-freeze it against the new path. See [CODE_LOCATIONS](CODE_LOCATIONS.md).
10. **Harness projects.** The three `.csproj` harnesses have not been built since the move. `OpticalReaderHarness` referenced `../engine/Sandbox.Gltf/` before the move, so that reference was already broken.
11. **Repository state.** Nothing is committed from this work. `git status` lists 336 entries across `engine/` and `research/`, many unrelated to glTF. Split the glTF work into reviewable commits before opening a PR.

## Current receipt-backed evidence (2026-11-07)

The canonical owner-private publication is v32 at commit
`62e6a61f1b2f7c4457956f2832a61888b08f8859`, with 84 captured forms,
55 visual groups and 371 comparison states. It replaces two existing review
rows with the aligned MandarinOrange source capture and opacity overlay, and
the native 1380×1302 NormalTangentTest canonical capture. Both are pending
user review. Live checks verified the two image hashes, the overlay control
and same-tab comparison links. The other 369 rows, saved decisions and earlier
evidence remain in the comparison history.
[Live publication receipt](../../Temp/gltf-validation/comparison-site-publication.json) ·
[Feedback action ledger](FEEDBACK_ACTIONS.md).

Recent receipts remain producer-scoped. BrainStem/SparseAccessor/TextureTransform/SimpleInstancing evidence keeps its producer identity in the linked receipts. MetalRoughSpheres AO evidence and the later unverified Normal-stage boundary are described in the scoped update below; neither implies broad parity or visual approval.

The canonical receipts are linked below and in the relevant feature rows:
[BrainStem animation receipt](../../Temp/gltf-validation/direct-gpu/BrainStem/canonical-animation-5832-run/probe-01-editor_direct_gpu_tests/receipt-01.json),
[SimpleSparseAccessor result](direct-gpu/models/SimpleSparseAccessor/result.json),
[TextureTransform owner result](../../Temp/gltf-validation/direct-gpu/texture-background-audit/TextureTransformTest-feedback-result.json),
[SimpleInstancing source capture](../../Temp/gltf-validation/direct-gpu/SimpleInstancing/review-camera-lit/summary.json) and [canonical compiled capture](../../Temp/gltf-validation/direct-gpu/SimpleInstancing/review-camera-compiled-final/summary.json),
and [publication receipt](../../Temp/gltf-validation/comparison-site-publication.json).

MandarinOrange also has a bounded public `KHR_materials_diffuse_transmission`
technical pass: source + canonical compiled GPU cases and one roughness
compatibility case passed **3/3** on the named installed producer ([receipt](../../Temp/gltf-validation/direct-gpu/mandarin-diffuse-named-public3/probe-01-editor_direct_gpu_tests/receipt-01.json),
SHA `40e408ee…`; [model result](direct-gpu/models/MandarinOrange/result.json)).
This is an out-of-selected-ten feature follow-up; new-capture human appearance
approval remains pending and this does not establish broader extension or
corpus conformance.

The older 337-row run (22 pass, 315 blocked) and exception inventory remain
distinct producer-specific evidence; they are not the latest publication snapshot
or a current complete-corpus run. Check the
[impact note](direct-gpu/CURRENT_IMPACT.md) before reusing older claims.

Retained producer-scoped exception snapshot:
[EXCEPTIONS_AND_GAPS.md](EXCEPTIONS_AND_GAPS.md). It records an earlier
6 November corpus producer (83 captures, 117 failed variants, 137 blocked),
11 recorded `GltfException` messages, and source versus installed support. Its
machine-readable companion retains affected cases and stack traces. Later
model-specific passes supersede only their same-scope earlier failures; this
snapshot is historical and is not the current publication or a current corpus count.

Source/evidence audit: **2026-11-07**. Scope remains complete glTF 2.0 core,
the ten selected extension names in [coverage.json](coverage.json), and the
agreed s&box model integration. This is a feature checklist, not a claim of
conformance. Category ownership is in [WORKSTREAMS.md](WORKSTREAMS.md).

**Source code, written tests, CPU passes and rendered proofs have different
scopes. No complete core/corpus/vmdl/editor/performance parity is claimed.**

Current scoped deltas: [TextureTransform](../../Temp/gltf-validation/direct-gpu/texture-background-audit/TextureTransformTest-feedback-result.json)
now has corrected source/canonical captures (2/2), with SHA
`bfe892260eea8c668570d24909136a2f8fc0c6a554dffb88d8ef4ca9b13e678c`; six
contrast and six arrow-direction checks pass, but user approval is pending. The
SimpleInstancing source and canonical capture now share the corrected hash above;
its user appearance decision is pending. NormalTangent evidence remains ambiguous:
current GPU output matches independent literal and Mikk references, so no global
normal/tangent correction is accepted. MetalRoughSpheres AO stage Engine MVID `87f823c3-45fc-4903-a136-2b62d86ee251` / Gltf MVID `b083757d-e24c-4b5f-8e04-dd3861b36960` passed scoped source + canonical public renders, authored AO controls and the lease test. Its selected HDR field has 18 untouched/NaN face-readback probes; EV pixel response and visual approval remain unproven. A later Normal stage is installed but render-unverified, so do not attribute the AO pass to it or claim editor readiness. The old warm-frame estimate of 5.4 s was five pixels different from the
later fully resident frame: the fully resident frame is 6.649 s, and the 2 s
target is unmet. See [selected batch results](direct-gpu/texture-benchmark/SELECTED_BATCH_RESULTS.md).


## Current remaining-feature checklist

The public route works for a defined slice: descriptor-driven `Model.Load` and
ordinary/skinned rendering; OPAQUE source-lit base-color/MR/normal/AO; MASK;
required unlit plus the tested mixed legacy opaque-lit route; selected public
material variants; and representative skin/morph/codec assets. Evidence is
distributed across [UnlitTest](direct-gpu/models/UnlitTest/result.json),
[MaterialsVariantsShoe](direct-gpu/models/MaterialsVariantsShoe/result.json),
[BrainStem](direct-gpu/models/BrainStem/result.json),
[MeshoptCubeTest](direct-gpu/models/MeshoptCubeTest/result.json),
[ABeautifulGame](direct-gpu/models/ABeautifulGame/result.json) and
[MandarinOrange](direct-gpu/models/MandarinOrange/result.json). Receipts keep
their own producers; no single run proves this entire list or corpus-wide support.

**Implemented but lacking broad GPU/corpus proof:** every integer/normalized,
sparse, interleaved, matrix-padding and quantized accessor layout; all primitive
modes; all material texture roles, UV sets/transforms and sampler combinations;
all STEP/LINEAR/CUBICSPLINE channels, morph+skin combinations and real-time
playback; every applicable model/form; and cross-platform runtime behavior.
Focused CPU/data tests and representative GPU assets do not close these matrices.

**Still missing or not installed:** lit BLEND cross-instance ordering and the
transparent manual-override/lifecycle prototype; complete native camera import;
public punctual-light lifecycle and point/spot support; native asset-reference
ownership, publication, auto-embedding and `.meta`-driven GPU compilation; full
vmdl/model-editor/physics/LOD/bodygroup/attachment parity; Windows acceptance;
and matched load/memory/package-performance acceptance across target models and
platforms. Camera and directional
light candidates are source/built-stage work, not installed public acceptance.
Portable `.glb_c` dependency ownership and publication remain design/integration
work; shared external textures are acceptable when references resolve. See
[core details](#gltf-20-core), [selected extensions](#selected-extensions),
[s&box integration requirements](#additional-agreed-sbox-work), and
[COMPILED_PIPELINE_DESIGN.md](COMPILED_PIPELINE_DESIGN.md).

## Validation status legend and explicit no-start gaps

Use these labels when asking whether a behavior has “started validation”:

- **Unstarted:** no implementation, fixture, source oracle, or test identified for that behavior.
- **Written/unrun:** test or fixture exists, but no matching execution receipt.
- **CPU/data-only:** reader, plan, numerical, or mock test ran; this does not prove native/GPU behavior.
- **GPU/native scoped:** an actual route ran for the exact forms/producer named; it does not imply broader conformance.

**Audit result, bounded to glTF 2.0 core and the ten names in
[coverage.json](coverage.json):** no entire core feature row or selected extension
was found with zero validation artifacts. The core rows point to reader/pure
cases or model fixtures, and each selected-extension row lists CPU/plan tests,
model-specific evidence, or both. The actual gaps are mostly unrun or missing
positive runtime paths, not whole areas that nobody has examined.

| Positive behavior with no current accepted runtime path | What validation exists | What remains unstarted or open | Evidence |
|---|---|---|---|
| `EXT_mesh_gpu_instancing` applied to a skinned mesh node | A valid fixture and negative loader-policy tests verify explicit rejection without partial ownership changes | Positive skinned-instancing rendering is unimplemented and has no passing GPU fixture; the negative rejection is not a positive spec pass | [`SimpleInstancingLoaderPolicyTests.cs`](../../engine/Tools/Gltf/direct-gpu/models/SimpleInstancing/SimpleInstancingLoaderPolicyTests.cs), [`GltfGpuBackend.cs`](../../engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuBackend.cs) |
| `KHR_lights_punctual` native point/spot photometry and public instance lifetime | Reader/plan CPU cases cover point, spot and directional definitions, defaults and units; fake-instance lifecycle tests cover ownership; the lamp source fixture is point-light focused | Positive public native/GPU photometry, lifecycle and image checks are not accepted; point/spot native behavior is not established | [`GltfCoreSceneConformanceTests.cs`](../../engine/Tests/Sandbox.Test.Gltf/GltfCoreSceneConformanceTests.cs), [`GltfPunctualLightPlanTests.cs`](../../engine/Tests/Sandbox.Test.Unit/Resources/GltfPunctualLightPlanTests.cs), [`GltfPunctualLightInstancesTests.cs`](../../engine/Tests/Sandbox.Test.Unit/Resources/GltfPunctualLightInstancesTests.cs), [`LightsPunctualLamp/ACCEPTANCE.md`](direct-gpu/models/LightsPunctualLamp/ACCEPTANCE.md) |
| glTF-managed asset references, publication/auto-embedding, and `.meta`-driven GPU compile settings | Compiler/package dependency tests and a design document exist | Native asset-reference ownership/discovery, publisher wiring and `.meta` import settings have no accepted end-to-end glTF test; these are s&box integration requirements, not glTF 2.0 core clauses | [COMPILED_PIPELINE_DESIGN.md](COMPILED_PIPELINE_DESIGN.md); [s&box integration requirements](#additional-agreed-sbox-work) |

Some highlighted gaps are **CPU-only**, not unvalidated: for example, sparse
zero-base materialization has fixture/test code in
[`MorphPrimitivesLayoutFixture.cs`](../../engine/Tools/Gltf/direct-gpu/models/MorphPrimitivesTest/MorphPrimitivesLayoutFixture.cs) and a separate synthetic actual-GPU proof passed **8/8** ([receipt](../../Temp/gltf-validation/direct-gpu/SimpleSparseAccessor/implicit-zero-run-20261107-010637/probe-01-editor_direct_gpu_tests/receipt-01.json), SHA `a844402e…`). The public SimpleSparseAccessor GPU sample uses a populated base, so broader no-base forms, mutation/disposal, wider bounds and visual review remain open. Likewise camera readers and light plans have tests; public component import/render remains a separate gate.

The feature checklist covers the ten selected extension names. A bounded
registry-name and maturity inventory, including evidence gaps, is in
[`EXTENSION_COVERAGE.md`](EXTENSION_COVERAGE.md); it does not enumerate vendor
or archived registry sections or claim support from a name/test hit.

## Current architecture and accepted evidence

Engine and Gltf MVIDs are retained with each receipt instead of generalized
across the whole validation set. Earlier stage `6543fdeea62146c681a7732eb4a16585`
and its engine/shader identities remain valid only for their own receipts; do
not attribute older c5, 7eb or 8fc evidence to another producer. The AO receipts are scoped to MetalRoughSpheres; the subsequently installed Normal
stage has no render verification. See the
[impact note](direct-gpu/CURRENT_IMPACT.md) and
[execution evidence](direct-gpu/EXECUTION.md).

`Model.Load` now prepares a managed CPU resource; ordinary `ModelRenderer` and
`SkinnedModelRenderer` acquire C# GPU-buffer instances lazily and submit through
real `SceneCustomObject`s. The new glTF route creates no native Model, Mesh,
ModelBuilder or valid native IModel placeholder. Existing vmdl rendering keeps
its native branch. Native GPU allocation/drawing, stock materials/shaders,
texture codecs and camera/light/presentation services remain useful services.

Source bufferViews/accessors determine raw component formats, normalization,
offsets, strides and sparse/materialized streams. Compute consumes explicit
descriptors; output packing has a runtime vertex declaration. It is not a fixed
`GltfVertex` input layout. The named compiled `gltf_deform_cs.shader` is used;
public model loading does not compile an anonymous shader. Full-affine model
matrices are retained; public component transforms follow existing engine TRS
semantics, and unsupported lossy public-bone decomposition is not hidden.

| Actual evidence | Result and practical limit |
|---|---|
| Pure reader/codec project, c5c0 | **816/816 cases and 3/3 separate native codec preflights pass**. Receipt `direct-gpu/c5c0-current-pure-816/probe-01-editor_gltf_current_tests/receipt-01.json`, SHA256 `e8f0cbc404fe8303b54d03a24ba5b4d69956680f5c9560ccffdc9906d83c43f2`. CPU/data/codec proof, not rendered or complete specification parity; retain its producer. |
| Public Model/resource/ordinary-component integration, earlier fee5 | Corrected **25 public cases +3 native-handle controls pass**, plus **6 async +10 lifecycle**; receipts `6f10ea3e…` and `917974e1…`. Native handle predicates were corrected only after bare-custom/managed/vmdl controls; original reds retained. This earlier producer is not automatically a c5 regression run. |
| Public skinned SimpleSkin, c5c0 | **57 public cases +3 controls pass**; four source/compiled reference poses, current GPU vertices/indices, IoU 1/RGB error 0 and clean deletion controls. Receipts `e9a3bc6a…` and `2c7a40a3…`. The first receipt's surrounding 65-case batch includes one unrelated failed texture probe; no claim that all 65 passed. Authored skeleton/sequence/ownership scopes are tested; graph/physics/full-rig parity is not. |
| Public AnimatedMorphCube, c5c0 | **25 reader +12 public cases pass**, then **18 extended public clock/manual-morph cases pass**. Six source/compiled authored-key stills/readbacks pass (`66ac4a30…`); a real source-form two-loop cadence check passes (`165598de…`): 848 observed ticks, clip duration 4.199997425 s, maximum phase error 2.3849e-7 s. The containing cadence/material batch has 9 passes and 16 failures across 25 cases; only the cadence subtest is green. No all-model cadence or smooth-movie/performance claim. |
| Public textured BoxTextured, c5c0 | **All six glTF/Embedded/Binary source and derived-compiled forms pass**, with actual GPU positions/normals/UVs/indices, settled source texture texels and matched shared-material images, including flipped-V negatives. Initial two forms and eight checker controls remain in `46d4c955…`; the remaining four forms and repeat controls passed in `c97866b3…`. Aggregate manifest SHA256 `62debf0d52775a3c6c5fe3c1129c0a6433cd569f394649494422cab1cce33860`. This is not all texture formats/roles/samplers. |
| Public OrientationTest, earlier fee5 | **13 GPU rows and seven fresh/reference/reversed/deleted captures pass**, source camera mapped without reflection, foreground/colored-region IoU 1. Receipt `e4bbd94e…`; maximum position error 1.52588e-5 inches and normal error 1.19e-7. Same-material geometry/binding proof, not independent PBR fidelity or the full 48-case layout/placement/camera matrix. |
| Source-lit material route and sampler diagnostics, 8fc/corrective | Source-lit OPAQUE checks pass on exercised source/compiled normal/AO cases (`cc763d92…`, `19aedfc4…`, `aa7ca61d…`). Sampler Attribute generation/compatibility **16/16 pass** (`13fc2b72…`); readiness-controlled diagnostics **102/102 GPU readbacks pass** (`c97866b3…`). Separate MetalRoughSpheres source/canonical AO and authored-AO controls passed with clean leases; HDR field texel/EV behavior and visual approval remain unverified. A later Normal stage has no render verification; do not attribute these scoped AO passes to it. Whole-model role/sampler integration remains incomplete; preserve earlier failed default-texture/transfer probes as scoped failures. See `direct-gpu/models/MetalRoughSpheres/EVIDENCE.md` and the current snapshot above. |
| Other core/extension model work | Independent source oracles, CPU/descriptor plans and focused helpers exist for primitive modes, interleaved/sparse data, instancing, material features and additional rigs. Each requires its own actual scope/receipt; do not turn the source inventory or older native captures into current own-buffer passes. |

Artifact paths in this checklist are relative to `Temp/gltf-validation/` unless
otherwise qualified. Current capture manifests are under
`direct-gpu/public-model-smokes/` and `direct-gpu/public-captures/`. Earlier
native-model evidence is preserved in the historical section below. The
comparison website must label producer/backend/scope. Its current owner-private
Current publication counts and feedback-sync status are recorded once in the
timestamped snapshot above. The current owner-private publication and saved
reviews do not establish corpus-wide or model-wide approval. See the
[comparison site](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site) and
[publication notes](COMPARISON_SITE.md); older stage descriptions there are
historical and do not override producer identities above.

Legend:

- **U**: focused CPU/reader/managed tests exist; consult the actual suite receipt.
- **E**: actual-editor probe or integration test exists; written is not executed.
- **P**: historical actual-editor proof; not proof of the current own-buffer path.
- **Missing/pending**: required acceptance is absent or not yet executed/passing.

## glTF 2.0 core

| Feature | Current source implementation | Tests written | Still required |
|---|---|---|---|
| JSON `.gltf`, asset version/minVersion and metadata | Reader implemented; current public Model.Load route is managed | U: Container, ReaderRobustness, Compiler; current pure 816 passes; E: selected public source forms | All applicable valid assets, editor metadata preservation and packaged logical identity |
| GLB header, JSON/BIN chunks, ordering and padding | Reader and lossless GLB writer implemented | U: Container, ValidationEdge, Compiler in pure 816; E: selected public compiled forms | All variants and unknown-chunk editing policy; lossless packaging is not GPU optimization |
| External buffers and data URIs, including percent encoding | Reader and confined mounted resolver implemented | U: Accessor, ValidationEdge, ReaderRobustness in pure 816; E: selected source/compiled public fixtures | Full mounted/build dependencies, relocation, invalidation and compiled-only source identities |
| BufferView buffer/offset/length/target/stride | Source-described owned upload storage and descriptors feed C# GPU buffers | U: Accessor/UploadLayout/UploadData in pure 816; E: 40 Orientation CPU cases; public Box/Skin/Cube GPU readback | Full dynamic layout/value/render matrix, including all adversarial fixture forms |
| Accessor component types, vector/scalar counts and normalization | Reader plus descriptor-driven component decoding; no fixed input vertex struct | U: Accessor, Primitive, UploadLayout/UploadData in pure 816; E: selected actual GPU rows | All integer/normalized layouts and semantics on GPU; source/compiled and negative controls |
| Accessor relative/absolute offsets, alignment, bounds and matrix column padding | Reader validation and descriptor offsets implemented | U: current pure 816; E: 40 Orientation cases include interleaved source/compiled CPU fixtures | Actual GPU matrix/unaligned-view-origin/poison-padding fixture coverage; CPU success is separate |
| Sparse accessors and zero-initialized accessors without a base view | Materialized into detached effective-value streams with source provenance | U: UploadData/MorphUploadData and accessor suites in pure 816; E: synthetic no-base zero initialization actual GPU **8/8** ([receipt](../../Temp/gltf-validation/direct-gpu/SimpleSparseAccessor/implicit-zero-run-20261107-010637/probe-01-editor_direct_gpu_tests/receipt-01.json), SHA `a844402e…`); SimpleSparseAccessor source + canonical compiled sample GPU/readback **2/2**, result receipt SHA `66c75af8…` | The sample uses a populated base accessor; synthetic proof is bounded to its tested cases. Broader no-base forms, mutation/disposal, wider bounds and visual review remain open |
| Dynamic separate/interleaved attribute streams | Implemented C# source arenas, per-stream descriptors and runtime output vertex declarations | U: UploadLayout/UploadData/MorphUploadData; E: raw triangle and selected public GPU rows | Full separate/interleaved accessor matrix on real GPU; 48-case Orientation capture matrix remains pending |
| Indexed and nonindexed geometry; unsigned 8/16/32-bit indices | Descriptor plans preserve source indices; topology expansion and output indexing are explicit | U: Accessor, Primitive, Geometry; E: bound GPU index readback and raw-index corruption negative control | All index widths/nonindexed/topology combinations through public loading and rendering |
| All seven primitive modes | Reader/list expansion and direct-GPU mode planning implemented. Installed shaders apply the light-independent path to default point/line materials without normals | U: Geometry/DrawData in pure 816; E: MeshPrimitiveModes focused public source/canonical cases **6/6**, Engine `362ddfea…`, receipt `86ca3055…`; checks cover light independence, mixed slots, variants, overrides and source texture roles. Earlier mode 0–6 readbacks remain recorded | User review of the 128×128 reference-sized capture, broader winding/material combinations and full corpus coverage |
| Meshes, multiple primitives and per-primitive material slots | Shared immutable primitives and node instances; source-ordered used material slots separate from dedup | U: Primitive, StaticSceneAssembly; E: public Orientation13 primitives/seven slots on fee5 | Current broader multi-primitive materials, instances, variants and deformation acceptance |
| POSITION attributes, coordinate basis and metre-to-engine-unit conversion | Direct GPU basis maps glTF (x,y,z) to engine (z,x,y)/.0254; placement applied once | U: Geometry/StaticSceneAssembly; E: current public Box/Skin/Cube readback; fee5 Orientation camera proof | Remaining samples, full transforms/bounds and independent numeric/image matrix; no reflected basis workaround |
| NORMAL and TANGENT attributes; tangent handedness | Dynamic normal/tangent output and deformation paths implemented | U: DrawData/deformation tests; E: selected public GPU tangent/readback checks and NormalTangentTest public forms | NormalTangent visual cause remains ambiguous: current GPU output matches independent literal and Mikk references; no global tangent-space fix is justified. Broad transformed/deformed tangent-format and appearance proof remains open |
| Missing normals/tangents | Triangle flat-normal generation and fallback tangent source paths exist | U: stock-frame/primitive tests; model-local missing-normal/tangent probes written | Actual mode-by-mode GPU behavior and UV-dependent normal-map tangents; no full acceptance |
| All TEXCOORD sets and texture-specific set selection | Reader/uploads retain sets; runtime declarations are dynamic; provided-material candidate exposes UV0/UV1 | U: Primitive, DrawData, material UV plans; E: BoxTextured source UV GPU readback | All per-role set/transform combinations and sets beyond candidate limits; complete default material integration |
| COLOR_0 RGB/RGBA and normalized integer colors | Dynamic color streams replace the old native loader nonwhite-color rejection path | U: Primitive/DrawData; model-specific color layout/oracle tests written | Actual stock-shader color multiplication, linear interpretation and alpha across formats; no blanket color pass |
| Application attributes and names/extras | Reader and upload provenance retain source/application semantics; unknown data preserved conservatively | U: Primitive, CompilerSafety, UploadLayout and MorphUploadData | Safe compile/edit/reimport reference preservation and explicitly requested custom GPU bindings |
| Scenes, selected default scene, multiple scenes and no default | Public resource accepts valid no-default-scene assets; explicit scene selection, empty/geometry-free resources, identity and initial reimport have current MandarinOrange evidence | U: Scene/StaticSceneAssembly in pure 816; E: MandarinOrange result: public source/compiled scene material+GPU 2/2 and reimport 6/6 | Broader scene authoring, editor selection and reimport cases; do not claim every valid scene workflow |
| Node hierarchy, matrix/TRS transforms, repeated mesh instances | Reader hierarchy plus shared GPU primitive/node instances; public model metadata is separate | U: Scene/HierarchyCache/StaticSceneAssembly; E: Orientation repeated-node data and public placement checks | All hierarchy/deformation/rendered bounds/lifecycle cases; linked/editable scene hierarchy is still pending |
| Negative/nonuniform transforms, normals, winding and bounds | Full-affine raw glTF/GPU matrices; component placement follows existing InterpolatedWorld semantics | U: scene/rig tests; E: selected public instance placement and source normal/readback cases | Reflected/sheared/singular real-GPU matrix; inherited public Transform shear is rejected rather than silently decomposed |
| Zero/singular transforms | Reader/GPU matrix paths retain finite/degenerate geometry; public bone Transform fallback `9db…` is applied in the worktree but not installed | U: current pure 816; E: c5 tiny public Transform diagnostic has 12 failures across 19 cases | Install and validate the fallback against retained tiny finite TRS reds; prove singular surviving normals/winding/culling on GPU |
| Skin joints, skeleton roots, inverse binds and missing-IBM identity defaults | Managed authored skeleton/animation metadata and ordinary SkinnedModelRenderer adapter installed | U: SceneFeature/Deformation/SkinningPalette; E: c5 public SimpleSkin 57 cases plus three controls pass; four actual source/compiled poses | Broader rigs, skeleton APIs/bone objects, inverse-bind edge cases and full native-gameplay parity; tiny public TRS still red |
| Joint/weight formats and multiple influence sets | Descriptor-driven inputs and all-set rig/shader loops replace native per-mesh decoder selection | U: Primitive/Deformation/AnimatedDeformation and rig plans; E: SimpleSkin selected GPU poses | Actual all-format/>4-influence GPU fixtures and samples; old native decoder limitations are historical, not proof of this path |
| Full-affine skinning, nonjoint ancestors and instance placement | Direct GPU skin matrices stay affine; public bone Transform cannot encode arbitrary shear | U: SkinningPalette/rig tests; E: SimpleSkin public pose and placement/ownership controls | Independent shear/nonjoint-ancestor/multiple-skin GPU matrix, bounds/culling, and full public bone behavior |
| Morph POSITION, NORMAL and TANGENT deltas | Detached morph streams and dynamic compute deformation implemented; public manual morph API installed | U: MorphUploadData/PoseMorphOverride in pure 816; E: upstream MorphPrimitivesTest is POSITION-only/no-skin; a separate synthetic source + canonical compiled fixture passes public GPU/readback **4/4**, checking nonzero P/N/T deltas and tangent W on every vertex (receipt SHA `fc864156…`; [bounded fixture report](direct-gpu/models/MorphPrimitivesTest/COMBINED_STATUS.md)) | This is one synthetic fixture, not broader upstream/model coverage. Arbitrary target counts, all influence combinations, other models, cadence and complete shader/material behavior remain open |
| Node/mesh morph weights, defaults and morph-before-skin order | Reader and instance poses retain defaults; signed manual overrides are isolated from sequence poses | U: MorphConsistency/AnimatedDeformation; E: synthetic combined source + one canonical compiled public `Model.Load`/`SkinnedModelRenderer` fixture tests authored defaults and signed weights, with exact GPU per-vertex readbacks and wrong-order controls; max errors P `1.71e-5` in, N `1.46e-7`, T `1.37e-7` (4/4; [receipt](../../Temp/gltf-validation/direct-gpu/combined-morph-skin-02/probe-01-editor_direct_gpu_tests/receipt-01.json)) | The original Khronos MorphPrimitivesTest has POSITION targets only and no skin. Broader target counts, all influence combinations, corpus coverage, playback cadence and full instance/override acceptance remain open |
| Animation TRS and morph-weight channels, arbitrary key times | Exact reader evaluator plus managed public sequence adapter and signed shared clock installed | U: Animation/AnimationChannel/current pure 816; E: 60 public SimpleSkin/control cases; Cube public and real two-loop cadence | All sample clips/channel combinations, runtime lifecycle/graph behavior and full GPU/corpus acceptance |
| STEP, LINEAR and CUBICSPLINE; quaternion interpolation | CPU samplers and signed public clock implemented; no fixed-rate approximation approved | U: Animation/AnimationChannel in pure 816; model-local independent reference tests | Current public GPU samples at keys/STEP neighbors/intermediate times across interpolation modes; measured tolerances |
| Core default material and base-color factor | Stock complex.shader/cache plus installed conditional source-lit material route for the tested OPAQUE subset; no generic authored-material/PBR parity claim | U: Material/cache plans; E: BoxTextured, NormalTangentTest and ABeautifulGame public source/compiled captures | Complete factor/color/role behavior, independent PBR reference and full-model material parity |
| Metallic/roughness factors and packed texture channels | Default stock adapter packs B/G into native texture resources; source-texture candidate preserves separate factors | U: Material/texture/compiler plans; historical native factor checks; current resource preparation checks | Current direct-GPU rendered factors/channels and independent material oracle; candidate binding failures remain |
| Base-color, metallic-roughness, normal, occlusion and emissive textures | Source-lit OPAQUE checks pass for exercised roles; emissive preparation is accepted. MetalRoughSpheres source/canonical AO and authored-AO controls pass for a bounded scope; HDR field texel response and user approval remain pending | U: Material/Image/role plans; E: MandarinOrange and NormalTangentTest public captures; ABeautifulGame material-role/GPU checks | All five roles together, AO/HDR behavior across broader assets/materials, texture scale/strength, independent sampler/factor behavior and emissive pixels across public assets |
| OPAQUE, MASK, BLEND and alphaCutoff | Installed source-lit BLEND and per-draw native sorting units pass the exercised public paths | U: Material/MaterialEdge; E: Alpha public loading/rendering and cross-instance cases **8/8** across `87d4292b…` and corrected standalone-canonical cases `9e022751…`. Native lifecycle/compatibility cases **7/7** across `1a905a3b…` and the corrected explicit-visibility-capability case. Initial fixture failures are retained. Corrected source presentation **1/1**, `7806f9f4…` | User visual review. Callback-enabled contiguous fallback ordering, manual transform/color overrides, first unprepared raw overrides, in-place material reclassification and performance remain open |
| Double-sided materials and back-face shading | Public NormalTangentTest source/compiled forms pass on 7eb; corrected test-owned signed/lit controls pass, but same-defective-oracle backface comparisons are withdrawn | U: Material; E: NormalTangentTest current result and candidate receipts | Generalized independent front/back normal, lighting, texture and deformation controls across actual public material routes |
| Texture sources: external URI, data URI and image bufferView | Encoded sources and native texture resources implemented; all six public Box source/derived-compiled forms pass | U: Image/ArtifactDependencies; E: BoxTextured external/Embedded/Binary forms, GPU UVs/indices and exact settled source texels | Broader image-role/source combinations and compiled-only dependency relocation/packaging |
| PNG/JPEG decoding, alpha and valid image precision | Production `GltfTextureCompiler` supports bounded linear PNG16 decoding and metallic/roughness G/B packing; source + canonical compiled public `Model.Load`, actual material realization and bilinear GPU samples pass. This is not appearance or arbitrary-size acceptance | U: byte/precision plans; E: installed Engine MVID `e66560df-8eba-4c30-998a-a0296a91e4af`, actual producer **61/61**, focused follow-up **5/5**, and permanent integration regression **1/1**; [producer result](direct-gpu/models/TextureEncodingTest/precision/result.json), [precision notes](direct-gpu/models/TextureEncodingTest/precision/README.md), [61-case receipt](../../Temp/gltf-validation/direct-gpu/TextureEncodingTest/png16-production-run-20261107-020700/probe-01-editor_direct_gpu_tests/receipt-01.json) (`4202567c…`), [5-case receipt](../../Temp/gltf-validation/direct-gpu/TextureEncodingTest/png16-followup-run-20261107-020708/probe-01-editor_direct_gpu_tests/receipt-01.json) (`69fce954…`), [permanent integration receipt](../../Temp/gltf-validation/direct-gpu/TextureEncodingTest/png16-integration-run-20261107-021643/probe-01-editor_direct_gpu_tests/receipt-01.json) (`91eba668…`) | Default working-set budget is 256 MiB: 2048² estimates 437,256,192 B without mips / 526,734,656 B with mips, plus encoded bytes, so representative 2K PNG16 is rejected at default. Public import budget setting and broad-size allocation/rendering remain open. 16-bit sRGB base-color/emissive is rejected as non-core (glTF core specifies 8-bit sRGB); no visual approval or full PNG/JPEG precision matrix. PLTE-after-tRNS follow-up is prepared but unrun |
| sRGB versus linear interpretation by texture role | Role-aware resource conversion and selected source/GPU transfer checks are installed; not a complete role/shader matrix | U: Material/encoding plans; E: ABeautifulGame Basis resource checks and rendered source sample | Independent rendered transfer/factor tests for every material role and complete authored appearance |
| Sampler wrap S/T and all min/mag/mip choices | Reader/mapper and sampler Attribute path exist; diagnostic generation/readback is verified, but not every path is wired/proven in full-model rendering | U: sampler plans; E: 16 generation/compatibility cases and 102 readiness-controlled diagnostic readbacks | Whole-model sampler combinations across roles/layouts and independent visual acceptance |
| Perspective and orthographic cameras, aspect/FOV and clipping | Reader metadata plus source import candidate in `engine/Sandbox.Engine/Resources/Model/Gltf/Cameras/GltfCameraImport.cs`; candidate not installed as accepted public importer | U: SceneFeature/CameraReader; E: capture cameras are test-owned native cameras | Build/install public component import, units/projection/activation and authoring/reload acceptance |
| Required/optional extension handling and standard fallbacks | Reader capability gate and five advertised loader extension names remain explicit | U: Container/declaration/fallback tests in pure 816 | End-to-end selected-extension acceptance; required unsupported rejection and optional fallback must remain distinct |


## Selected extensions

These are the selected names, not a promise to implement every extension in the
registry. Other extensions require a separate scope/maturity review.

| Extension | Current source | Tests written | Missing engine acceptance |
|---|---|---|---|
| `KHR_texture_transform` | Reader transforms/texCoord override and per-role UV candidate planning; default dynamic integration incomplete | U: pure material/declaration and UV-plan tests; model-specific oracle/checkers | All per-role GPU/default material bindings, source/compiled instances and variants |
| `KHR_materials_unlit` | Installed named-shader public route supports four UnlitTest source/compiled forms; targeted e12 mixed route supports unlit plus legacy-compatible opaque lit materials | U: Material/MaterialEdge; UnlitTest 12/12 public cases and 34 feature measurements; e12 mixed follow-up receipt `267dcdfc…` | Broader texture/alpha/backface combinations and material mixtures; not complete selected-extension or lit-material conformance |
| `KHR_materials_variants` | Public per-instance selection/rendering exists for MaterialsVariantsShoe source and derived-compiled forms | U: Material/variant state; Shoe receipts `79d143f0…`, `cd947b03…`, current compiled acceptance `d1e4ee81…` | Full GPU variant coverage across assets, overrides/lifecycle/reimport, resource ownership/packaging and model-wide appearance parity |
| `KHR_mesh_quantization` | Reader/loader policy plus descriptor-driven source decoding and GPU formats | U: Primitive/MorphConsistency/declaration in pure 816; E: quantized BrainStem meshopt and MeshoptCubeTest source/compiled GPU evidence | Every selected normalized/integer format and accessor combination on GPU, all applicable variants, Windows and complete corpus acceptance |
| `KHR_draco_mesh_compression` | Descriptor-driven codec/accessor path passes BrainStem Draco source/compiled GPU forms; ABeautifulGame combined BasisU+Draco source passes public load/render | U: Draco/DracoAccessor/DracoPrimitiveData; BrainStem public receipt `5ac14449…`; ABeautifulGame compressed-source receipt `251cfbfe…` | All applicable corpus assets, wider layouts/deformation, portable dependency packaging and Windows decoder proof |
| `EXT_meshopt_compression` | Codec/filter/fallback path passes BrainStem EXT source/compiled GPU forms | U: Meshopt/MeshoptIntegration/CompilerSafety/UploadData; BrainStem public/meshopt receipts `73bd9af2…`, `d9cee9cc…` | All filter/layout combinations across applicable assets, portability and Windows coverage |
| `KHR_meshopt_compression` | KHR meshopt decoder passes BrainStem source/compiled and MeshoptCubeTest fallback/compressed source plus canonical compiled GPU checks | U: Meshopt/MeshoptIntegration/CompilerSafety; BrainStem receipt `73bd9af2…`; MeshoptCubeTest final result `45cf33f9…` | Broader asset/layout coverage, Release Candidate compatibility review on upgrade, Windows and performance |
| `KHR_texture_basisu` | Public ABeautifulGame required BasisU+Draco source loads/renders; representative native Basis texture/readback paths exist | U: Ktx/Image/BasisFallback; ABeautifulGame `251cfbfe…` and role/mip evidence | Other image/material roles, shared-package portability, measured performance and Windows/corpus coverage; earlier initial ORM residency failure was later resolved after bounded wait |
| `KHR_lights_punctual` | Reader/light plans, native directional prototype and isolated finite point-light candidates | U: SceneFeature/light-plan; E: cookie identity shader **1/1** `f7cac5c…`, finite photometry **1/1** `38e6cccb…`, source/canonical selected-plan ownership **2/2** `a83b9528…`. Candidate shader/factory scope is recorded in PointLightIntensityTest acceptance | Production shader routing, public light binding, shadow behavior, unbounded range, directional/spot photometry and authored model appearance |
| `EXT_mesh_gpu_instancing` | Static public 125-instance sample and source/compiled forms pass geometry/GPU readback and ordering checks; skinned nodes remain explicitly unsupported | U: Instancing and model-local core/plan/backend tests; E: `direct-gpu/models/SimpleInstancing/ACCEPTANCE.md` | Broader forms/material/shadow/selection cases, skinned instancing and measured hardware/performance claims |

Public receipts now exercise `KHR_materials_unlit`, `KHR_materials_variants`,
`KHR_draco_mesh_compression`, `KHR_texture_basisu`, `KHR_meshopt_compression`
and `EXT_meshopt_compression` on named models, in addition to selected
quantization and texture-transform paths. This is model-scoped evidence, not
support across all applicable variants. Reader recognition alone does not prove
public loading, rendering, fallback behavior or cross-platform conformance.


## Additional agreed s&box work

These are product requirements beyond glTF core. Existing vmdl tests do not
count as glTF acceptance without exercising glTF-backed assets.

| Requirement | Tests already written | Still required |
|---|---|---|
| Existing Model.Load/LoadAsync APIs and stable resource identity | E: fee5 25 public cases plus three controls and six async/ten lifecycle cases pass; c5 public Skin/Cube/Box proofs above | Full managed API parity, GUID/hash cold loads, compiled-only logical `.gltf`, aliases/collisions and all samples |
| Valid portable optimized `.glb_c` | U: Compiler/CompilerSafety/CompilerOrphanBinary/ArtifactDependencies in pure 816; selected source-derived public forms pass | Portable package ownership/dependency contract, registration/invalidation, logical identity, collisions and compiled-only resolution; preserve declared resolvable external dependencies |
| Generated dependency publication and auto-embedding | Publisher tests exist; data/compiler package dependency tests pass in pure 816; design in [COMPILED_PIPELINE_DESIGN.md](COMPILED_PIPELINE_DESIGN.md) | Native asset-reference ownership, dependency discovery/publication, optional auto-embed and generic publisher integration are not implemented/accepted |
| `.meta`-driven GPU compilation | Source-adjacent metadata mechanism and inspector precedent documented in [COMPILED_PIPELINE_DESIGN.md](COMPILED_PIPELINE_DESIGN.md) | No glTF GPU-preparation `.meta` setting/ownership/invalidation path is implemented or accepted |
| Runtime material/texture sharing and overrides | U/E: shared leases/cache, rollback/provenance controls; public source-ordered material slots and whole/slot hooks | Per-instance/per-variant precedence/lifecycle, reimport generations, graphs and default material completeness; transparent override/lifecycle prototype remains uninstalled |
| Animation graph references | Managed skinned adapter explicitly rejects native graph/parameter operations; no positive glTF graph acceptance | Packaged required graph references and ordinary runtime integration; graph editing remains deferred |
| Collision shapes/meshes, mass/surfaces, joints/ragdolls/hitboxes | No glTF-specific runtime parity acceptance identified | Standard-extension review, authoring/storage/runtime mapping and equivalent-vmdl physics tests |
| LODs, bodygroups, attachments and project-defined game data | Core managed models provide legitimate empty bodygroup metadata; authored feature parity remains unimplemented | Established API integration, essential standard/s&box encoding and authoring/runtime/roundtrip tests |
| New model-only editor, mesh selection and modifier/component-style authoring | Design direction documented; no complete acceptance suite | All model-only parity controls, preview/select/modifiers, undo/save/reopen/source-control/dependency tests |
| Direct source `.gltf`/`.glb` editing and safe reimport | Design settled; no complete editing/reimport acceptance suite | Extension/extras safety, source-material edits/links, automatic reload/conflict UI; `.meta` import settings only |
| Standalone authored export and DCC plugins | Deferred by user | Keep outside first milestone |
| Every applicable Khronos model/format variant | Pinned corpus/data-reader/roundtrip inventory; selected actual public captures only | All applicable in-engine variants with independent numeric/visual evidence, exclusion/asset-defect ledger; no corpus parity pass |
| Load time, process/GPU memory and package size versus vmdl | No matched completed acceptance benchmark | Selected warm diagnostic: 5.39957 s frame is five pixels different from later fully resident 6.64883 s; target under 2 s remains unmet. Agree matched thresholds on static/animated assets; gate optimized `.glb_c`, measure raw loading separately |
| Linux then Windows | Scoped Linux native-editor proofs only | Complete feature matrix on both; Windows evidence and cross-platform compiled portability remain pending |


## Source/test pointers and remaining critical path

- Current pure inventory and exact source/case hashes:
  [47-class proposal](direct-gpu/models/OrientationTest/PURE_SUITE_CURRENT_PROPOSAL.json),
  [inventory audit](direct-gpu/models/OrientationTest/PURE_SUITE_AUDIT.md),
  [current runner](../../engine/Tools/Gltf/editor/editor_gltf_current_tests.csx). The historical
  [764-case manifest](gltf-test-manifest.json) and [runner](../../engine/Tools/Gltf/editor/editor_gltf_unit_tests.csx)
  remain unchanged; their earlier green counts are not attributed to new producers.
- Managed public route:
  [GltfModelLoader](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfModelLoader.cs),
  [managed resource](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfManagedModelResource.cs),
  [direct GPU backend](../../engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuBackend.cs),
  [rig](../../engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuRig.cs),
  [ordinary component](../../engine/Sandbox.Engine/Scene/Components/Render/ModelRenderer.Gltf.cs),
  [skinned component](../../engine/Sandbox.Engine/Scene/Components/Render/SkinnedModelRenderer.Gltf.cs).
- Source layout contracts:
  [upload descriptions](../../engine/Sandbox.Gltf/GltfDocument.UploadLayout.cs),
  [owned upload data](../../engine/Sandbox.Gltf/GltfDocument.UploadData.cs),
  [morph upload data](../../engine/Sandbox.Gltf/GltfDocument.MorphUpload.cs),
  [compute shader](../../game/core/shaders/gltf_deform_cs.shader).
- Model-local tests, independent references, execution holds and scoped receipts:
  [worker context](direct-gpu/WORKER_CONTEXT.md), [execution ledger](direct-gpu/EXECUTION.md),
  [public Model integration](direct-gpu/MODEL_LOAD_INTEGRATION.md),
  [engine unit tests](../../engine/Tests/Sandbox.Test.Unit/Resources),
  [integration tests](../../engine/Tests/Sandbox.Test.Integration).

The critical path is wider actual public-model sample/variant coverage, complete
material/UV/sampling/alpha behavior, extreme layout/transform/deformation cases,
and compiled source identity/portable GPU preparation. Physics/graphs/authored
model features, the model-only editor, performance parity and Windows remain
required. Passing the current CPU suite or a few public captures cannot close
those gates.

## Historical native-model stages and evidence

The following stage logs and native ABI audit describe the earlier model/mesh
construction prototypes as of 5 November 2026. They are preserved for regression
history. Their words “current”, “installed” and “blocker” refer to those earlier
producers. The native per-mesh skin-decoder constraint does **not** block the new
C# descriptor-driven GPU-buffer path, and none of these captures proves that
path. The earlier negative/failure evidence has not been discarded.

The latest accepted captured producer is stage
`8824bf8bc7a342adb5ffc21dac2d850c`: Engine `65853c81`, Gltf `39c031d5`,
Renderer `541ba5a5`. It supports the scoped managed rigid/skeletal motion proofs
below. Stage B `6428bbe4ba694714848a1df233e6e4cd` has now been installed after a
normal idle/no-unsaved shutdown: Engine `016c390a`, unchanged Gltf `39c031d5`,
Renderer `707bdcc0`. Its morph/transaction changes await actual editor validation.
Installation manifests, selected output hashes and backups are in each stage.

Combined stage C `a6eb5e90bd714ab2a6385c957cf7ba9e` is installed in the
visible physical editor: Engine `7a669eb5`, Gltf `6e93bfb4`, Renderer `a5150560`.
Current actual checks: **120/120 CPU**, **14/14 sampled animation forms**
(34 clips/544 GPU frames), **20/20 independent signed/static position+normal morph
fixtures**, and **733/745 pure reader tests** (12failed,0blocked). The full native
API set remains36/51; logical morph4, JSON bulk atomicity1 and authored skeleton10
are positive failures. Four generic ModelRenderer rest rows show IoU0; three
Skinned controls pass and one has an IModel-null lifetime failure under investigation.
Current OrientationTest Binary source/compiled capture2/2; glTF source/compiled
fail native construction. The previous paired4/4 camera proof is historical.
The skin-format rerun also failed procedural allocation before any rows, so the
older12-case GPU format proof is not promoted to C evidence. A bounded cache
lifetime fix plus data/scene reader validation fixes are being prepared as D.

| Actual evidence | Result and practical limit |
|---|---|
| Frozen pure glTF project, loaded Gltf `39c031d5` | **704/732 passed**, 28 failed, no blocked cases. 16 animation/skinning, six data and six scene failures. `editor-gltf-unit-tests/648e65f201594457afa07ac9439cc44b/receipt.json`. Separate reader fixes and 745-case source inventory are frozen but unbuilt/unrun. |
| CPU adapter/clock/rig/layout tests against Engine `65853c81` | **99/99 passed**. CPU staging/mapping only, not GPU/native API parity. `animation-stage-expanded-tests/probe-01-editor_model_adapter_tests/receipt-01.json`. |
| Actual native component/lifetime/cache tests against Engine `65853c81` | **7/9 passed**, including four component playback rows, two forced-GC direct SceneModel lifetime rows and cache dispatch. Two positive morph-loading rows reject. Corrected test-only native replacement binding oracle preserved earlier exact glTF ownership/disposal assertions. `animation-stage-lifetime-unorm16/probe-01-editor_animation_component_tests/receipt-01.json`. |
| Actual animated Model.Load + normal component ticks | **12/14 forms passed**, six models, source and compiled: AnimatedTriangle, SimpleSkin, RiggedSimple, Fox, CesiumMan, InterpolationTest. 32 clips and 512 sampled GPU frames; two independent instances, seek/reset/finish/loop controls and representative STEP boundaries. Managed capture only; full interpolation/reference appearance conformance unverified. AnimatedMorphCube source/compiled reject in this producer. `animation-stage-expanded-tests/probe-03-editor_animation_load/receipt-01.json`. |
| Actual posed raw GPU layout diagnostic | **12 four-influence cases passed** with IoU1: both U8/U16 joint indices × UNORM8/FLOAT32/UNORM16 weights × rest/posed. Exact fractional values retained, compute features0/1/2 observed. Six-influence rows remain red. This is procedural installed raw upload, separate from glTF loader acceptance. `animation-stage-lifetime-unorm16/probe-02-editor_native_skin_pose/receipt-01.json`. |
| Actual stock compute shader compilation/reload | Compile passed; actual shader reload callback revision0→1 and feature selections observed. Material central callback test is separate. `animation-stage-first-proof/probe-01-editor_skinning_shader_compile/receipt-01.json`. |
| Actual OrientationTest camera regression | **4/4 forms captured**, two presets each: source-negative-X yields cyan, source-positive yields red, with green/blue both. Proper basis unchanged; upstream extrinsics/light are not calibrated. `orientation-paired-managed-camera-658/probe-01-editor_orientation_cameras/receipt-01.json`. |
| Historical actual static/MR/codec checks on Engine `585541bd` | Adapter65/65; settled BoxTextured4/4; metallic/roughness4/4; Draco/KHR+EXT meshopt8/8 forms captured. Not rerun on the current producer; full appearance/performance not proved. Receipts under `mr-codec-installed-*`. |
| Historical bounded Khronos inventory on Engine `00923901` | All337 rows completed:312 applicable,25 required-extension exclusions;70 variants/140 source+compiled forms loaded+captured,111 failed,131 valid larger variants awaiting resource admission. `editor-corpus/c10e9530a23249bdaa2ed886e28e089c/receipt.json`. |
| Actual loader sampler baseline on Engine `00923901` | **12/20 passed**; clamp/mirror/nearest remain red. The72-row follow-up isolates fixed shader sampler behavior but does not fix stock shaders. `editor-model-sampling/ee21c2e1708942d4bfabf1d1edc4e4e4/receipt.json`. |

All artifact paths are relative to `Temp/gltf-validation/`. The comparison site
publishes32 actual sampled motion clips plus eight paired camera images;64 older
images are historical. See [COMPARISON_SITE.md](COMPARISON_SITE.md).

Full authored-skeleton API parity, generic ModelRenderer authored rest, logical
morph names/manual controls, >4 influences, nonzero tangent morphs and flat-normal
morph rebuilding remain separate positive acceptance work. The default-native
per-mesh weight decoder selector is a documented blocker below. Source-only
coordinate/rest/API tests do not turn these into passing evidence.


The managed capture route then completed the same 16-variant batch: six variants
across Triangle, TriangleWithoutIndices and BoxInterleaved loaded and captured
in both forms (**12 reliable form captures**), with every visibility and removal
control passing. The other ten variants retained their actual loader failures.
Receipt: `Temp/gltf-validation/editor-corpus/51b63abfd0f5492cb088c7b302330664/receipt.json`.

Current loader/capture milestone: the installed Engine `00923901`, Gltf
`39c031d5` and SceneRenderer `b861ac04` loaded and captured **14 of 16 variants**
in both original and derived compiled forms (**28 controlled forms / 56 images /
eight model families**). Only the two nonwhite vertex-color variants failed in
this batch. All visibility and deletion controls passed; full appearance stays
unverified. Receipt: `Temp/gltf-validation/editor-corpus/333d63a4057a44bfb7e85eab84aed97c/receipt.json`.

A separate actual-loader analytic sampling test passed **12/20** cases, with
CLAMP_TO_EDGE, MIRRORED_REPEAT and NEAREST magnification failing. These are real
core rendering gaps. The stock sampler declaration/binding is under
investigation; the managed capture route alone cannot identify the native draw
path's root cause. Receipt:
`Temp/gltf-validation/editor-model-sampling/ee21c2e1708942d4bfabf1d1edc4e4e4/receipt.json`.

### Historical default native renderer blocker

Source/ABI audit, 2026-11-05: the new weight-format decoder selection is in
`Sandbox.SceneRenderer`. It does not establish correct FLOAT or UNORM16 skinning
in the normal native renderer (`r_managed_scene=false`). A positive managed
offscreen result must remain labeled with its renderer. The native camera
diagnostic failed its initial empty control, so its posed GPU behavior is still
unproved. Do not enable the experimental managed renderer globally as a glTF
workaround or convert exact weights to packed UNORM8.

The available managed/native boundary supplies these capabilities:

| Available API | What it establishes | Missing capability |
|---|---|---|
| `RenderTools.DispatchModelMeshSkinning`, generated in `engine/Sandbox.Engine/Interop.Engine.cs`; wrapper in `engine/Sandbox.SceneRenderer/Gpu/RenderContext.cs` | Explicit managed dispatch accepts an `IMaterialMode`, native model mesh, transform buffer and cache destinations. The managed renderer can select the stock compute material's `F_SKIN_WEIGHT_FORMAT` for each mesh. | It does not change the native scene system's own compute scheduling or material selection. |
| `RenderTools.GetModelMeshSkinning` and `GetModelMeshSkinningBones` | Native vertex/weight/bone counts, bone remapping and inverse-bind readback can be checked after construction. | No exposed post-build vertex attribute format, byte offset or stride readback. |
| `CSceneAnimatableObject.SetWorldSpaceRenderBoneTransform` and ordinary material overrides | Existing native scene models accept TRS render-bone poses and draw-material overrides. | No exposed per-mesh skinning compute-material/decoder override, full-affine render-palette setter, or per-instance external vertex-cache injection. |
| `SceneObject` before/after object-render callbacks | Managed code can run around an object draw. | No contract exposes the native skinning batch setup or makes a draw-material attribute select its compute decoder. |

`GltfSkinningMeshFormats` therefore records the **uploaded** raw vertex layout,
not a native post-build layout readback. It validates the uploaded byte range,
stride/alignment and supported formats, then checks native unique mesh names and
vertex counts. Independent posed GPU fixtures remain necessary to prove that
native construction preserved that upload contract. Registered mismatches and
invalidated registrations fail explicitly; existing unregistered vmdl models
retain their established default decoder.

The smallest native change is to read the weight `ColorFormat` while building
each mesh's compute-skinning work, select the corresponding stock shader mode
(`UNORM8`, `FLOAT32`, `UNORM16`), and include that mode in the native compute batch
compatibility key. The shader branches already being staged can be reused. The
native implementation is outside this managed repository; this is a concrete
native integration requirement, not a claim that its GPU path has been tested.

The identity render-palette bones in the initial glTF adapter are also separate
from authored skeleton parity: they do not establish source joint names,
hierarchy, bind-pose APIs, attachments, physics, native graph playback or bone
object behavior. Those require the separately planned source master skeleton
and per-skin inverse-bind representation.
