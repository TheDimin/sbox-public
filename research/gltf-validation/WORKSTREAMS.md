# glTF workstream ownership

> Paths updated 2026-11-08. The code cited here moved to `engine/Tools/Gltf/` (see [CODE_LOCATIONS](CODE_LOCATIONS.md)). The coverage claims below are unchanged, and no new acceptance was run.

## Canonical source and compiled coverage matrix

**Plan for the reduced corpus run; not a new acceptance result.** The pinned
manifest remains 337 variants / 150 models: 312 applicable rows across 140
models, 25 required-extension exclusions; 10 models have no applicable row.
For each applicable model, run one chosen upstream source and exactly one
canonical derived `.glb_c`, compiled from that chosen source. Keep an additional
source form only when it exercises behavior that the chosen source or shared
fixtures do not cover: accessor component/stride/sparse layout, quantization, a
required codec or fallback, material semantics, or authored animation. Keep
authored material variant names and animation poses as within-asset assertions;
they do not multiply compiled artifacts. Preserve all old receipts, captures and
review decisions. Rows omitted by this plan are not counted as newly passed.

Centralize generic JSON/external-buffer, GLB chunk, embedded-buffer, data-URI,
and compiled-only/alias/dependency-mount checks in shared fixtures. Existing
harness starting point is [editor_model_load.csx](../../engine/Tools/Gltf/editor/editor_model_load.csx)
(`source-gltf`, `source-glb`, `khronos-triangle-embedded`,
`compiled-only.glb_c` / `direct-only.glb_c` selections); this plan does not
claim every central case has a fresh passing receipt. Keep independent layout
or codec cases when they differ semantically even if extension names overlap.
Compare accessor types, normalization, offsets, strides, sparse overlays,
quantization, fallback bytes and codec filters before omitting a source row.

| Model | Chosen source → one canonical `.glb_c` | Additional source-only cases retained | Omitted variants and covering proof | Existing proof / open gaps |
| --- | --- | --- | --- | --- |
| `NormalTangentTest` | `glTF` → `.glb_c` derived from `glTF` | Keep the `glTF` source for the asymmetric normal map, generated tangents, UV transform/handedness and front/back controls. | Omit upstream `glTF-Binary` and its separate derived package from future model runs; shared GLB/container fixtures own this encoding. Do not claim new binary-form rendering from this omission. | `result.json`: four existing public source/compiled forms passed on stage 7eb; binary source receipt `cc78d1c6…`, canonical `.glb_c` from glTF receipt `aa7ca61d…` with external image dependencies mounted. Valid test-owned candidate evidence is corrected front/back and lit controls (`1a75ed9f…`, with corrected back-lit follow-up `df47904b…`). Withdrawn same-defective-oracle comparisons `72a09eea…` and `58eee1b2…` are not signed-normal acceptance. Human visual approval and full PBR remain pending. |
| `MaterialsVariantsShoe` | `glTF` → `.glb_c` derived from `glTF` with external image dependencies mounted | Keep the source asset for public variant selection and render sequence; exercise `midnight`, `beach`, `street`, default reset, and per-instance behavior within that asset. | Omit `glTF-Binary` source and its separate derived compile from future model runs; same authored variant behavior is exercised and shared GLB/container fixtures cover encoding. One compiled package covers the chosen source. | Stage 7eb public receipt `79d143f0…` and compiled retry `cd947b03…`; source selection/render and rollback passed, compiled retry passed with mounted JPEGs. The 69-pixel compiled reset comparison remains above the 64-pixel threshold (`443f5a60…`; later repeat `232dad77…`), so reset parity and compiled-only packaging remain gaps. |
| `MeshoptCubeTest` | `glTF-Meshopt` → `.glb_c` derived from the compressed source | Retain `glTF` source as the real optional fallback baseline and `glTF-Meshopt` source as required KHR meshopt. The compressed form has 60 compressed views across ATTRIBUTES/TRIANGLES/INDICES and NONE, EXPONENTIAL, OCTAHEDRAL, COLOR and QUATERNION filters; fallback bytes/layout differ. | Omit a second `.glb_c` from fallback `glTF`; generic compiled/alias coverage is centralized. Do not omit either source: the fallback source proves fallback bytes and compressed source proves required decoder/filter behavior. | Independent fallback/GPU layout report `e7608000…` and triangle classification `58ec8d8f…`; installed 7eb MASK controls passed 4/4. Preserve older public preparation failure `3ee6292d…` as historical. The current blocker is rotated missing-tangent fallback: candidate `5f02…` passed isolated old/new GPU and authored/generated/skin regression controls, but the named-shader install and canonical retest remain pending. Playback/canonical preview also remain open. |
| `BrainStem` | `glTF-Meshopt-EXT` → `.glb_c` derived from that source, to cover EXT meshopt in the one canonical package | Keep `glTF` source as the uncompressed core baseline, `glTF-Meshopt` source for KHR meshopt, and `glTF-Draco` source for Draco. Codec variants have distinct primitive/attribute streams and use variant-local geometry/pose oracles. | Omit `glTF-Embedded` and `glTF-Binary` sources plus their derived packages; shared container fixtures own these encodings. Omit derived packages for the other feature sources: one canonical package per model. | `result.json` records all 12 prior source/compiled forms passing on stage 8fc. Public GPU receipts: core `dca71f2e…` plus source-first `675dfbf0…`, meshopt `73bd9af2…`, Draco `5ac14449…`; separate meshopt format checks `d9cee9cc…`. Newer source-first capture is diagnostic framing; animation/reference visual approval, Windows and measured performance remain pending. |
| `ABeautifulGame` | `glTF` → one canonical `.glb_c` derived from `glTF` | Retain `glTF` source as ordinary external-image/material-role baseline and `glTF-Binary-KTX-ETC1S-Draco` source for the unique required BasisU+Draco combination. Do not split authored pawn materials into extra packages. | Omit ordinary `glTF-Binary` source and its separate derived package; generic GLB/container coverage is centralized. Keep compressed source because it is the only variant combining both selected codecs. Under the one-compiled-package policy, the compressed source does not require another `.glb_c` unless a separately justified shared-fixture gap emerges. | Ordinary source/derived-compiled public forms and capture passed on stage 7eb (`7cccd06e…`); combined compressed public source passed (`251cfbfe…`). Earlier required-BasisU rejection remains historical. Basis metadata/resource checks 37/37; GPU mip-residency check was 2/3 (`1d9026a…`), with ORM initial residency later resolved after bounded wait. Camera/lighting are inferred and visual approval remains pending. |

This is a focused selection for the five active model workstreams, not a
completed 140-model selection. Apply the same manifest feature/layout audit to
the remaining applicable models before reducing their rows. Remaining coverage
decisions: assign each selected codec's compiled-path proof to shared fixtures
or representative models while preserving one canonical `.glb_c` per model;
centralize integer/normalized/sparse/interleaved layouts without losing
model-specific combinations; and define the portable compiled plus declared
dependency contract. Shared external textures are allowed when their asset
references resolve. Do not claim these gaps closed by this table. See
[COMPILED_PIPELINE_DESIGN.md](COMPILED_PIPELINE_DESIGN.md) for package
constraints.


## Current execution checkpoint — 2026-11-05

Root coordinates/reviews only. `validation_coordinator` owns serialized builds,
editor lifecycle, test execution, captures and publication. Animation/skinning
workers own scoped source changes and receive actual failures. Older history
below does not describe the live PID.

Current visible physical editor: PID219180/start2085386, guard parent219153,
persistent launch session69684. VallArk/minimal is idle with no unsaved work;
loaded Engine `7a669eb5`, Gltf `6e93bfb4`, Renderer `a5150560`. Global managed
rendering remains false. Controlled captures explicitly use the managed bridge.

Coherent C stage `a6eb5e90bd714ab2a6385c957cf7ba9e` includes only the bounded
per-row TRS roundoff guard/original-matrix bounds and five parser validation
files relative to B. All2092 producer inputs and18 test sources were frozen.
Actual results: CPU120/120; pure733/745 (12failed,0blocked); full animation
14/14 forms,34clips544actualGPUframes; signed/static morph20/20 position+normal
references. Native API36/51 has15positive failures (logical morph4, JSON bulk
atomicity1, authored skeleton10). Generic ModelRenderer authored rest4rows fail;
Skinned3rows pass and one has IModel-null. Default-native renderer remains a
separate documented format-selection blocker.

Current OrientationTest glTF two forms fail native construction; Binary two forms
capture with the normal source-positive corpus preset. Earlier paired4/4 camera
and8/8 independent axis/culling results remain accurately producer-labeled.
A skin-format rerun failed native procedural construction before any GPU rows.
Model.Destroy's native resource cache eviction is under test-first investigation;
no global cache clearing or rerun-until-pass workaround was applied.

Next coherent D source: two category owners froze six-data/twelve-scene-reader
regressions and bounded validation fixes (expected pure764total). The Model
resource-lifetime regression/fix must freeze before D build. Preserve C receipts;
install only after all-input snapshot, normal idle/no-unsaved shutdown and guard
admission, then rerun affected/full suites against actual new loaded modules.

Private website source `089467e08ee67ca288de468e08b3661dfeb2aa50` deployed
successfully with34current sampled-motion clips and two current camera images,
104historical records,733/745headline and current load failures retained.
All current camera vectors are source-positive; earlier negative-X vectors stay
historical. Browser QA remains declined. See COMPARISON_SITE.md.


## Coordination rules

1. Root approves exact write ownership before implementation. Shared loader,
   descriptor, compiler, scene and material files are not implicitly available
   to every category owner. Proposed ownership in an audit is not write approval.
2. Owners write meaningful regression/acceptance tests before fixes. Root reviews
   the result, executes the appropriate actual-s&box tests, and returns concrete
   failures to the owner. Source inspection and written tests are not a pass.
3. Workers do not build, execute suites, use the editor/CLI, compile shaders,
   deploy binaries, restart processes or commit unless root assigns that action.
   Expensive work remains centrally serialized through the resource guard.
4. BufferViews and accessors determine layouts. No fixed `GltfVertex` or
   `StockVertex` assumption satisfies the dynamic upload requirement. Any
   necessary materialization/conversion has an explicit resulting descriptor.
5. Keep original vmdl behavior, unrelated dirty files and user sessions intact.
   Standards and existing engine workflows are audited before custom schemas.
6. Follow [FEATURE_STATUS.md](FEATURE_STATUS.md) for the full scope and missing
   tests. Update evidence and implementation status independently.

## Current test execution status

Earlier, an editor was verified through `editor_status` as project `vallark`,
scene `minimal`, not playing, no unsaved changes, compilation idle/successful.
That PID is now stale. No editor launch or restart was performed by the harness
owner; the earlier identity observation cannot authorize attaching to another PID.

The first bounded runner attempt on 2026-11-05 at 06:41:40 UTC was **refused
before dispatch** by `codex-resource-guard run --budget-gib 2`: swap free was
1,468,002,304 of 8,589,930,496 bytes, below the required 25%. Its bounded stale
metadata cleanup found nothing removable and the recheck also refused. No
process was terminated. This is an admission result, not a failed or passing
test run. The full updated suite and new native paths remain unrun.

The swap refusal is historical. On resuming, root reports a guard snapshot with
no editors, low RAM use and full swap, and is coordinating rebuilt Gltf, Engine
and SceneRenderer modules plus a guarded physical-desktop editor launch. The
user is back at the PC and requests the actual visible desktop editor. Root
must verify the new PID/start identity, project, visible window and editor
responsiveness before dispatch. The harness owner does not perform that launch
or any test execution.

Root subsequently dispatched the all-suite script in the actual editor. That
attempt failed while `run_csharp` compiled its method body, before any test or
codec preflight executed: `mcp-script.cs(324,76)` reported CS1061 for
`Dictionary<string,bool>.GetValueOrDefault`. No runner receipt was produced.
Root retained `coordinator-all-20261105/reply-005.json`, the final console reply,
and a clean CLI exit. The harness owner replaced that namespace-dependent
extension with an explicit `TryGetValue` condition and froze the corrected
source. Root's second actual run compiled the runner, then failed while compiling
test sources with 16 CS9007 interpolated-raw-literal errors. The negative receipt
records zero executed cases and 485 partial cases; no codec preflight ran. The
data and material owners are correcting their test literals without changing
case inventory. The orchestrator initially obscured that receipt with a
`uuid.UUID(None)` TypeError; its verification now reports the runner's phase,
error and diagnostics before requiring identities from successful later phases.
It retains the complete negative receipt and all pass requirements. Root's next
485-case run reached the complete phase: **366 passed, 2 singular-transform
tests failed, and 117 were blocked by three missing native codec preflights**.
The receipt is `coordinator-all-syntax-20261105/editor-receipt.json`. This is not
a full-suite pass. Codec deployment and singular-transform fixes are owned by
their respective categories. A newly frozen third singular-transform regression
raises the next inventory to 486; the current loaded producer cannot prove the
unbuilt scene fix until root rebuilds/restarts it.

### Latest root-owned runtime evidence

Earlier evidence used physical-desktop editor PID **22925**, start identity
**142176**. Root now verified physical-desktop PID **89621**, start identity
**689782**, on the same Survive project with loaded Engine/Gltf/SceneRenderer
identities beginning `802a970d` / `72ebb3cb` / `59dbbdd0`. Scene `Lost` has unsaved
changes, is not playing, and compilation is idle with zero errors. Root is
dispatching the frozen 732-case suite while preserving that unsaved scene.
No harness owner stops play mode, changes the active scene, restarts the editor
or interrupts the user. Prior receipts retain their original process identities.

| Check | Actual evidence reported by root | Remaining limit |
|---|---|---|
| Expanded 732-case suite | **701 passed, 31 failed, 0 blocked, 0 partial**; receipt `editor-gltf-unit-tests/88f170b047bb4c4d80ee920b19e6e635/receipt.json` | User entered play by the outer postcheck. Animation/data/scene failures require review; singular producer fix remains unbuilt. |
| Native codec preflights | Draco, KTX and meshopt all passed on this host | This proves sampled decoder paths, not extension rendering/integration. |
| CPU/discovery probes | Stock material schema query completed; animation's 9 CPU reference cases passed; palette and model-lifetime probes passed | These scopes remain distinct from loaded-model animation and GPU deformation. |
| First render batch | Source and compiled captures matched, but deleted-object control retained 1275 stale pixels; overall result false | Capture-control failure prevents a rendering acceptance claim. The batch stopped before primitive modes when user play became active. |
| New conformance tests | 246 additional data/material/animation/scene cases executed in the expanded suite | Manifest v5 has 44 classes/416 methods/409 rows/732 cases; historical receipts stay immutable. |
| First bounded corpus run | 16 static variants attempted in both forms; six variants loaded in both forms (12 loads), ten blocked by material/color support | All 12 capture controls failed due to stale empty-view pixels. Saved images are diagnostics; receipt `editor-corpus/e853b953459245b5ab42e22c686a7b10/receipt.json`. |
| Managed corpus captures | Six variants across Triangle, TriangleWithoutIndices and BoxInterleaved loaded and captured in both forms (12 forms), with all sphere/empty/deletion controls passing | Narrow managed-renderer captures only; no native-path or Khronos appearance pass. Receipt `editor-corpus/51b63abfd0f5492cb088c7b302330664/receipt.json`. |
| Revised loader build | Gltf, Engine and SceneRenderer staged successfully with zero warnings/errors in `staged-build/c1ff5bb2ba5046a28363f52a61e2c781` | No installation or editor restart yet; raw upload, base-color resources, mips and arbitrary factors need actual-editor validation. |

Latest user priority: implement the actual `Model.Load` path, then expand the
website's sample evidence. Active source ownership is `/root/model_load_materials`
(loader/material adapter/native texture-resource integration),
`/root/dynamic_geometry` (runtime-described primitive upload), and
`/root/engine_workflows` (capture-control diagnosis and corpus harness). Root
reviews, stages builds and executes all editor validation; broad negative-test
cleanup waits behind this integration work.

The [337-row corpus inventory](corpus-case-manifest.json) and
[metadata builder](../../engine/Tools/Gltf/scripts/build_corpus_manifest.py) are frozen alongside the new,
unrun [actual-editor corpus harness](../../engine/Tools/Gltf/editor/editor_corpus.csx). Metadata-only authoring
verified 337 indexed = 337 disk variants, 150 models, 1890 hashed files and a
clean pinned Models/LICENSES checkout. All ten selected extension names have
applicable sample rows; sample presence is not acceptance. The 312 applicable
rows retain optional outside-scope core fallback. Only 25 rows with required
extensions outside scope are excluded, and no upstream defect is silently
waived. Immutable material/node-binding tables are hash-shared without dropping
the 10000 NodePerformance contracts. Independent source bounds exist for 224
rows; full native geometry, material/texture/alpha, posed animation/deformation
and matched appearance oracles remain unverified. Seven non-corpus coverage
requirements (authoring/reimport, model parity, packaging, managed visual review,
performance and both platforms) still require dedicated acceptance evidence.

## Central validation plan

This is a source-reviewed execution inventory, not execution evidence. Manifest
version 5 now matches all 44 top-level pure test sources: **416 methods, 409
DataRows and 732 expanded cases**. The latest UploadData source has 18 methods,
5 DataRows and 21 cases, including finite-FLOAT regressions and one native
meshopt case. The runner preserves exact `string`/`int`/`uint`/`byte`/`bool`/`float`
DataRow types and records their CLR names. Root alone executes the following
work, serialized through the resource guard, and sends failures to the owner.

The [CLI orchestrator](../../engine/Tools/Gltf/scripts/run_editor_tests.py) has been used for root's attempts. Its
`discover` mode saves live instructions/schemas and exits without dispatching
scripts. Root reviews its `discovery.json` and explicitly supplies that file's
SHA-256 to `run`; run mode rechecks the live schemas and frozen inputs before
using the actual new session exchange directory. The helper retains complete
protocol replies and the receipt. The physical-window and loaded Engine/
SceneRenderer identity gate remains root's responsibility.

The new [probe batch executor](../../engine/Tools/Gltf/scripts/run_editor_probes.py) is source ready and unrun.
Root supplies an explicit ordered list of existing probe filenames; it reuses
the reviewed live-schema attachment without changing script bytes or editor
environment. It preserves legacy fixed-output subtrees before a rerun (bounded
to 8192 files/512 MiB per batch), keeps full replies and safe receipt copies,
and records per-probe console/status snapshots. It stops if the human enters
play mode or compilation starts. Human scene edits are recorded without being
treated as probe failures. CPU and rendering batches should be chosen separately;
the executor does not infer common acceptance from heterogeneous return fields.

1. Verify the physical desktop editor and loaded/disk Engine/Gltf/SceneRenderer
   identities. Read the console before testing and retain a final console check.
   Do not use the stale PID, a virtual display or an unguarded launch.
2. Run the unchanged [pure-suite runner](../../engine/Tools/Gltf/editor/editor_gltf_unit_tests.csx) with an
   explicit same-session request whose `suite` is `all`. Use the exact persistent
   `--connect` protocol in the [runner instructions](README.md#run-selection-within-one-attached-cli-connection).
   Hash the canonical manifest and runner, copy runner bytes unchanged into the
   session exchange directory, write/hash the four-field request there, and
   invoke it before closing that connection. Verify request ID/path/hash/values,
   `selection_source == "request"`, both canonical source hashes, 732 expected
   cases, zero failed/blocked/partial cases, and separate positive Draco/KTX/
   meshopt preflight results. Treat a missing native decoder as blocked coverage.
3. Run loaded-model/metadata probes before rendering, then establish independent
   native visibility and empty-frame controls before accepting pixel comparisons.
   Run private-scene probes sequentially; inspect cleanup errors and console
   output after each coherent group. Retain the complete response, receipt,
   fixture/source/module hashes and captures for every run, including failures.
4. Run the supplementary CPU/source and native capability probes separately from
   actual `Model.Load` acceptance. A compiled worktree helper or a directly built
   native model does not establish the behavior of the installed glTF loader.
   Record exact failures rather than replacing them with a passing substitute.
5. Report coverage gaps after the existing probes. These 25 scripts plus the
   pure suite do not cover every implemented engine test, every feature in
   FEATURE_STATUS, the full pinned Khronos corpus, Windows, or matched vmdl
   performance. Full completion still requires their independent acceptance.

The scripts below all write beneath `Temp/gltf-validation`. `private preview`
means task-owned editor scenes, preview widgets, cameras/lights, render targets
and temporary resources that their cleanup code is expected to destroy. `mount`
means a task-owned redirect/mounted filesystem that is expected to be unmounted;
it can still affect the process while the probe runs. No inspected script edits
the user's active scene. Verify that observation with before/after editor status.
Several older scripts use fixed output directories; preserve their receipts and
captures before rerunning them.

| Probe | Required inputs / producer | Live effects and evidence boundary |
|---|---|---|
| [editor_gltf_unit_tests.csx](../../engine/Tools/Gltf/editor/editor_gltf_unit_tests.csx) | Final manifest, pure source tree/KTX fixtures, existing Release MSTest DLL, current loaded Gltf, native Draco/meshopt/KTX codecs | Compiles tests only in a collectible context; CPU/codec evidence; no scene. Explicit request `all` selects 732 cases. |
| [editor_corpus.csx](../../engine/Tools/Gltf/editor/editor_corpus.csx) | Pinned 337-row corpus manifest/files/license refs and shared immutable contracts; actual loader/compiler | Unique private mounts/copies; one owned model/form at a time with explicit Model.Destroy; original and derived packaging loads, partial independent bounds/material checks, no heavy captures. Missing full oracles remain unverified; valid large assets are admission pending, not excluded. Authored, unrun. |
| [editor_image_conformance.csx](../../engine/Tools/Gltf/editor/editor_image_conformance.csx) | 21 generated image fixtures, four source forms, two resource roles | 168 authored native resource cases, including 32 positive PNG16 low-bit checks; not Model.Load, mip/sampler or rendered transfer acceptance. Unrun. |
| [editor_material_schema.csx](../../engine/Tools/Gltf/editor/editor_material_schema.csx) | Stock white material/shader; schema and native properties metadata APIs | Loads shared stock resource; writes raw schema and hashes; no scene or pixels. Discovery only. |
| [editor_cache.csx](../../engine/Tools/Gltf/editor/editor_cache.csx) | Loaded glTF material cache/descriptor and stock material | Two private scenes, owned material/template copies and leases; checks process cache ownership; fixed output; no GPU release timing. |
| [editor_model_load.csx](../../engine/Tools/Gltf/editor/editor_model_load.csx) | Pinned Triangle external/embedded files, current public loader, redirect filesystem | Private mount and owned models; optional private rendering. Default selects only `source-gltf`; full matrix needs editor-inherited `SBOX_GLTF_MODEL_CASE=all`, rendering needs `SBOX_GLTF_MODEL_RENDER=1`. |
| [editor_model_lifetime.csx](../../engine/Tools/Gltf/editor/editor_model_lifetime.csx) | Pinned Triangle, loaded public loader and material ownership/cache APIs | Private mount/models, explicit destruction and bounded forced GC/finalizers; process-wide GC effect; no user scene. |
| [editor_model_texture_load.csx](../../engine/Tools/Gltf/editor/editor_model_texture_load.csx) | Pinned BoxTextured external/embedded/binary assets, public loader and native texture compiler/readback | Private mount/models and base-mip readback; defaults to all four forms; no rendering or sampler/mip fidelity proof. |
| [editor_material_acceptance.csx](../../engine/Tools/Gltf/editor/editor_material_acceptance.csx) | Pinned Triangle, synthetic material definitions, current loader/compiler/native material metadata | Private mount/models; actual source and packaging-only GLB loads; no preview/pixels or per-instance variant proof. |
| [editor_quantization.csx](../../engine/Tools/Gltf/editor/editor_quantization.csx) | Synthetic required KHR_mesh_quantization fixture, loaded loader policy and native geometry readback | Private mount/models; defaults to both source/compiled forms; metadata/geometry, no rendering. |
| [editor_render.csx](../../engine/Tools/Gltf/editor/editor_render.csx) | Pinned Triangle, loaded primitive builder/material cache/stock shader; optional loaded managed capture bridge | Two private preview scenes and capture callbacks, owned targets/models/material leases; independent winding/empty/fresh-target controls; fixed output. Establish visibility first. |
| [editor_raw_vertex_buffer.csx](../../engine/Tools/Gltf/editor/editor_raw_vertex_buffer.csx) | Current loaded raw Mesh buffer API, stock shader, collectible IVT bridge | Private preview, owned geometry/targets and comparison captures; runtime stride/offset/normalized-color evidence; integer shader values and native rollback/lifetime remain unproven. Root observed a preview timeout; that is not regression proof. |
| [editor_primitive_modes.csx](../../engine/Tools/Gltf/editor/editor_primitive_modes.csx) | Synthetic geometry, actual loader, native direct reference models, stock shader/capture | Private mount/preview and captures; default 7 modes × 2 forms × 3 normal/tangent states = 42 cases; reference visibility required. Packaging-only compiled fixtures. |
| [editor_texture.csx](../../engine/Tools/Gltf/editor/editor_texture.csx) | Pinned BoxTextured+PNG, native texture compiler, loaded primitive builder/cache and stock shader | Temporary compiled texture/redirect, private preview and target; fixed output; opaque base-color helper capability, not textured `Model.Load` or PBR/sampler conformance. |
| [editor_animation.csx](../../engine/Tools/Gltf/editor/editor_animation.csx) | Synthetic exact STEP/LINEAR/CUBICSPLINE data, loaded Gltf reader/evaluator/compiler | CPU pose/vertex checks and packaged parity; no scene/native playback. |
| [editor_animation_playback.csx](../../engine/Tools/Gltf/editor/editor_animation_playback.csx) | Native skeleton/animation builders, stock material, pinned embedded AnimatedTriangle | Owned native SceneWorld/model, fixed-frame seeks and private mount for separate actual-load observation; no user scene; measurement, no approved approximation tolerance. |
| [editor_direct_skinning_palette.csx](../../engine/Tools/Gltf/editor/editor_direct_skinning_palette.csx) | Current loaded Engine/SceneRenderer direct palette API; DirectSkinningPaletteTests/TestScenes source and existing MSTest DLL | Compiles tests only under friend assembly identity; private CPU renderer fixtures; no real frame pump, pixels or loaded glTF deformation. |
| [editor_skinning_palette.csx](../../engine/Tools/Gltf/editor/editor_skinning_palette.csx) | Worktree GltfSkinningPalette helper/test source and existing MSTest DLL | Compiles renamed producer helper plus tests; CPU source diagnostic, not installed producer/GPU acceptance; no scene. |
| [editor_skinning_load.csx](../../engine/Tools/Gltf/editor/editor_skinning_load.csx) | Synthetic two-skin/six-influence fixture; loaded actual loader/Gltf/compiler and renderer skin readback | Private mount/models; independent CPU/native metadata observations. **Source compile blocker:** `int startingFrame = Application.FrameCount` narrows ulong to int. Overall `passed` is deliberately false; inspect detailed fields. |
| [editor_skinning.csx](../../engine/Tools/Gltf/editor/editor_skinning.csx) | Synthetic two-joint fixture, native skeleton/mesh construction, stock material, current renderer skin readback | Private mount/preview, render callbacks and owned resources; direct-native silhouettes and separate loader result. **Source compile blocker:** `int startFrame = Application.FrameCount` narrows ulong to int. |
| [editor_morph.csx](../../engine/Tools/Gltf/editor/editor_morph.csx) | Pinned synthetic position-morph fixtures, native morph/controller APIs and stock material | Private preview/callbacks/models/targets, fixed output; signed-weight runtime capability, not loader, normal/tangent morph or combined skinning proof. |
| [editor_scene.csx](../../engine/Tools/Gltf/editor/editor_scene.csx) | Worktree StaticScene helper/tests, existing MSTest DLL, pinned Box, stock material/capture | Compiles renamed producer source; private preview/models/targets; source scene/transform diagnostic, not loaded scene integration. |
| [editor_vertex_frame.csx](../../engine/Tools/Gltf/editor/editor_vertex_frame.csx) | Worktree primitive builder+StockVertexFrameTests, Unit Release MSTest DLL, pinned Triangle, stock shader | Compiles producer source, private light/preview/resources, fixed output; stock normal/tangent and lighting diagnostic, not installed loader acceptance. |
| [editor_material_channels.csx](../../engine/Tools/Gltf/editor/editor_material_channels.csx) | Pinned Triangle, stock complex material/compiler/debug-output support | Private preview, temporary textures/materials and targets, fixed output; exploratory scalar/channel sensitivity, no glTF material parity. |
| [editor_metalness_lighting.csx](../../engine/Tools/Gltf/editor/editor_metalness_lighting.csx) | Pinned Triangle, stock complex shader/direct-light/debug outputs; optional managed bridge | Private preview/light/resources and fixed output; exploratory light/metalness controls, no glTF PBR parity. |

Probe selection/capture flags other than the pure runner's request file are read
from the existing editor's environment. Setting them on a new CLI does not change
the editor. Record inherited settings; default-only execution cannot stand in
for an unselected matrix. Do not mutate global environment/CWD to switch cases.
Keep camera-clear, signed-morph override and similar diagnostic controls explicit
in evidence; a diagnostic control is not acceptance of the ordinary path.

The two skinning frame-counter type defects are source-review findings, not
observed compiler output. They require their owner's correction before dispatch.
All probe execution results for this coordinated rebuild remain pending until
root records actual receipts.

## Current installed loader validation

Execution ownership is delegated to `validation_coordinator`. Root coordinates
and reviews; animation and skinning agents author source without building or
operating the editor. Only one expensive job runs at a time through the resource
guard. Reuse the physical desktop editor; preserve human play and unsaved work.

The user-approved normal restart installed
`staged-build/7d4b93b19b594b63ad5229ebc8b6c5ee/installation.json`, with verified
selected output hashes and backups. Current physical editor: PID **173962**,
start **1380335**, guard parent **173935**, VallArk/Survive project. Persistent
launch session **9645** must remain alive. Loaded modules are Engine
`585541bd-f925-44de-9cfd-2d37343600b2`, Gltf
`39c031d5-72d0-4b52-8b24-80d390419dfe`, SceneRenderer
`bcb58c44-ef25-404d-859a-aae0c8193937`.

| Gate | Current evidence |
|---|---|
| Pure reader/evaluator inventory | 704/732 passed, 28 failed on unchanged Gltf `39c031d5`; no blocked/unexecuted cases. |
| Installed adapter CPU contracts | 65/65 passed against Engine `585541bd`; additional animation/rig tests await the next combined build. |
| Actual BoxTextured | Four forms load and match exact settled mip-0 pixels; first cold immediate resident mip is smaller, then reaches full resolution after three actual frames. |
| Actual metallic/roughness | Four source/compiled original/explicit-factor cases pass independent one-million-pixel G/B packing and final-mip checks. RGBA32F intermediate resource cost is not a performance pass. |
| Actual compressed geometry | Box/Duck Draco plus DragonAttenuation KHR/EXT meshopt: eight source/compiled forms load and capture with managed-route controls. Complete appearance remains unverified. |
| Full bounded corpus, historical Engine `00923901` | All 337 rows completed; 312 applicable = 70 both-form loaded/captured variants, 111 actual failures, 131 larger valid pending admission; 25 required-extension exclusions. Not a complete current-producer rerun. |
| Stock texture sampling | Historical actual Model.Load oracle 12/20; clamp/mirror/nearest failures retained. Extended 72-row sampler binding diagnostic shows fixed samplers ignore overrides; annotated object attributes/merging work. No stock shader sampler fix. |
| Actual animated loading baseline | Current static producer rejects all 14 source/compiled cases across AnimatedTriangle, SimpleSkin, RiggedSimple, Fox, CesiumMan, InterpolationTest and AnimatedMorphCube. No animated load/render success yet. |
| GPU skin formats | Installed raw upload diagnostic: U8/U16 indices with UNORM8 weights match independent posed raster oracle exactly; FLOAT4 .3/.7 and six influences fail. This is not Model.Load acceptance. |
| Skeleton and gameplay API parity | New rig/sequence source and focused tests are being integrated. Native bone metadata, original skeleton names/hierarchy, graph workflow and full vmdl API parity remain distinct gates. |
| Morphs | CPU support and positive loader expectations exist; production loaded-model hooks/rendered acceptance remain red or unrun. Never substitute a helper morph render for a loaded-model pass. |

The next build is intentionally held until both owners freeze the exact FLOAT
weight fetch, model/scene lifetime, transactional sequence selection and rig
format metadata. Then stage Gltf/Engine/SceneRenderer, restart normally through
the guard, verify loaded module identities, run adapter/native component tests,
repeat the independent posed GPU oracle, and capture actual component playback.

`../../engine/Tools/Gltf/editor/editor_animation_load.csx` now defines two real instances sharing one loaded
model, deterministic normal Scene.EditorTick playback, clocks before/after GPU
capture, finish/loop/seek checks, source sampler/key provenance, representative
STEP boundary captures, and fresh empty/sphere/deletion controls. It does not
claim independent full animation interpolation or appearance conformance.
`../../engine/Tools/Gltf/editor/editor_animation_component_tests.csx` runs exact source native component
regressions without substituting producer implementation. Keep forced-GC direct
SceneModel lifetime tests separate from visible GPU motion acceptance.

After actual motion proof, publish hash/time/clip/source/module-provenanced
animation media on the existing private comparison site. Leave a separate
interactive preview with a selected playable clip and existing controls when
possible; never modify the user's active scene for validation. The current site
still contains the earlier 56 controlled images across eight models.
