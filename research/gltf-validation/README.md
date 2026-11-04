# glTF sample validation scaffold

Rendering acceptance runs in the real s&box editor, using its virtual display when
requested. The user chose editor-only validation on 2026-11-04; do not substitute
a standalone `TestAppSystem` render or manually pump engine frames for this proof.
Reuse an existing authorized editor and confirm its project with `editor_status`.
Use [editor_render.csx](editor_render.csx) through the discovered `run_csharp`
tool. It creates its own `SceneRenderingWidget`, waits for real editor frames,
and writes PNGs plus a scoped receipt under `Temp/gltf-validation/editor-render`.
It checks the pinned Triangle's source and packaged geometry, both windings and
camera sides, single/double-sided stock materials, shared material leases and
empty-frame controls. This is one geometry fixture, not full corpus parity.

[editor_texture.csx](editor_texture.csx) compiles and packages the pinned
BoxTextured sample inside the editor, compiles its PNG into a native RGBA texture,
and renders through the shared material cache and stock complex shader. It checks
all native texture texels, source/package render agreement, an untextured control,
and black initial/disabled frames. Its receipt and PNGs are under
`Temp/gltf-validation/editor-texture`. This covers a static base-color fixture,
not complete PBR, sampling, mip or material conformance.

[editor_cache.csx](editor_cache.csx) checks the actual registered
`GltfMaterialSceneSystem` in two private editor scenes, an asset lease that
outlives both, isolated edits, rejection after disposal and final material-wrapper
invalidation. It writes `Temp/gltf-validation/editor-cache/receipt.json` and does
not clear the shared cache. GPU release timing and game-context teardown remain
separate requirements.

The preview does not edit the open project scene. Confirm editor/compile state
and read new console output after running it. Engine and glTF module identities,
input and image hashes, frames, failures and the precise tested scope belong in
the receipt. Wait for the preview widget's actual `OnPreFrame` callbacks before
and after render submission, rather than only counting global application-loop
iterations. The receipt records callback counts, visibility, widget/camera sizes
and frame numbers. Readback in the editor's active view batch must follow the
render submission's completion.

The current Triangle geometry, winding and sharing cells pass, but deleting the
last visible model and rendering a fresh empty scene can retain the prior image.
Keep this baseline failing. An opt-in `SBOX_GLTF_CAMERA_CLEAR=1` diagnostic adds a
color-only command to the two private cameras at `AfterDepthPrepass`; it passes
the same strict empty-view controls. This is evidence for a managed clear-path
fix, not a production fix or permission to mark full parity complete. Default
and diagnostic captures use separate `editor-render` and
`editor-render-camera-clear` output directories. The process-local diagnostic
flag must be restored after use.

The `SBOX_GLTF_CAMERA_CLEAR=noop` control registers an action-only command at the
same camera stage, records execution on both cameras, and writes
`editor-render-camera-noop`. It still fails the two empty-view checks by 1,275
pixels in the current editor. Merely registering a custom command therefore does
not explain the explicit-clear diagnostic's success. Preserve this failing
control when investigating the renderer; no global pipeline change is included.

[editor_morph.csx](editor_morph.csx) constructs a pinned, self-contained glTF with
one animated position target, builds stock-shader native morphs and independent
CPU-evaluated reference models, and captures 0/+1/−1 weights. It records actual
controller names, requested and native-reported weights, preview updates and
strict empty controls. The default path currently fails for empty images and
negative weights. With the private camera-clear diagnostic enabled, 0/+1 match
exactly; −1 is clamped to zero by the public managed setter. The separate
`SBOX_GLTF_SIGNED_MORPH_OVERRIDE=1` diagnostic calls the existing generated native
setter without that clamp: readback reports −1 but the image still matches zero.
That rules out fixing only the managed clamp as a sufficient solution.

`SBOX_GLTF_PAIRED_MORPH=1` instead creates positive and negative delta targets and
drives both through the public API, explicitly zeroing the inactive partner. With
the private clear diagnostic, this passed all five 0/±1/±2 reference comparisons
with zero differing pixels and the +1→−1→0 transition on a single scene model.
The ±2 references use separately pinned static-weight glTF variants. The receipt
is under `editor-morph-camera-clear-paired`; other diagnostic combinations have
distinct output directories. This proves a position-morph construction option,
not production loader integration, normal/tangent morphs, combined targets,
skinning, memory efficiency or full deformation conformance. Pairing duplicates
target data and controllers; quantify that cost before selecting the compiled
representation. The baseline remains failing.

[editor_material_schema.csx](editor_material_schema.csx) reads the actual loaded
stock complex shader's 159 variables and 20 combos in the current editor and
writes `editor-material-schema/receipt.json`. It does not mutate the shader or
material. Metadata supplies candidates for subsequent controlled render tests;
it does not prove material equations, packing, per-texture UV/sampler behavior or
color space. The cache's scalar isolation check uses the exposed
`g_flRoughnessScaleFactor` and establishes parameter readback, not shading fidelity.

These editor observations used Engine module ID
`aa9884de-2db1-4752-aac1-d13ad9ea8d36` and glTF module ID
`b2fb1c46-6cba-4256-b064-6d56daae5acb`. Re-run against rebuilt modules; retain the
module IDs and diagnostic flags in every receipt. No standalone graphics host
was used for these observations, and the project's open scene was not modified.

### Capture renderer and scene assembly

`CameraComponent.RenderToTexture` uses the native camera pipeline. The widget's
ordinary view may use the managed renderer when `r_managed_scene` is enabled.
Record this distinction: running inside the editor alone does not establish
coverage of both pipelines. The current editor's viewport flag is false.

`editor_render.csx` additionally accepts `SBOX_GLTF_CAPTURE_RENDERER=managed` to
call the already-registered `SceneBridge.RenderToTexture` for its private camera
and target after the normal preview callbacks. It does not change the global
viewport flag. In this path the complete Triangle/winding/sharing matrix and
deleted/fresh empty controls passed without the color-clear diagnostic. The
receipt is `editor-render-managed/receipt.json`. The native stale-frame control
remains a separate failure.

[editor_scene.csx](editor_scene.csx) compiles the exact new
`GltfStaticSceneAssembler` source and its self-contained MSTest source inside the
editor, in a collectible helper assembly. It records both source hashes, the
framework hash and helper module ID, and checks that the loaded glTF assembly
matches disk. This validates the new code without replacing loaded engine DLLs;
it does not establish normal `Model.Load` integration or a rebuilt engine.

All five scene-assembly tests passed: default scene selection, multiple meshes
and primitives, repeated instances, nested transforms, nonuniform mirrored
normal/tangent transforms and winding, bounds, UV preservation, and explicit
rejection of presently unsupported deformation. A four-primitive rendered
fixture matched independent native reference geometry with zero differing
pixels; omitting each primitive changed 80 pixels. Khronos Box source and compiled
geometry matched over 2,304 covered pixels. These native captures used the
explicit color-clear diagnostic and wrote `editor-scene-camera-clear`.
The same tests and geometry comparisons also passed through the explicit managed
capture route without the clear diagnostic, under `editor-scene-managed`.
Skinning, morphs, animated selected nodes, instancing and singular transforms
remain unsupported by this static assembler, and remain requirements of the
full implementation.

### Material and lighting prerequisites

[editor_material_channels.csx](editor_material_channels.csx) compiles eight
controlled PNG channel fixtures into native linear texture resources, verifies
their actual RGBA readback and measures stock complex debug output. Under the
tested settings, normal-texture blue values 32/224 produced roughness output
32/224, while its other channels did not change roughness. This identifies a
packing candidate; it does not prove factor equations or independent UV/sampler
support. Its metalness control was unresponsive, so the overall receipt remains
failed at `editor-material-channels-camera-clear`.

[editor_metalness_lighting.csx](editor_metalness_lighting.csx) adds direct-light
controls and an optional short `SBOX_GLTF_LIGHTING_CONTROLS_ONLY=1` mode. Its
`SBOX_GLTF_CAPTURE_RENDERER=managed` option keeps both capture paths separate.
The first tests found that the stock vmdl sphere lights correctly, while both
the glTF triangle and an independently built native triangle with a zero tangent
produce zero world normals and no lighting. Supplying an explicit unit tangent
restores the expected +X normal and front-light response over all 1,225 covered
pixels; a light behind the triangle contributes zero. This isolates a runtime
mesh vertex-frame prerequisite. Preserve the failed original controls while
fixing primitive staging; do not infer metalness behavior from an unlit mesh.

[editor_vertex_frame.csx](editor_vertex_frame.csx) compiles the exact primitive
builder source and its new MSTest regression source inside the editor. The
`SBOX_GLTF_EXPECT_VERTEX_FRAME_RED=1` run against the original source recorded
three failures for missing tangent frames and one passing authored-tangent
preservation case. These failures remain in `editor-vertex-frame-red`.

The builder now supplies a stable orthonormal tangent when glTF omits one,
including after generating flat face normals. Authored tangents and their signs
remain unchanged. This fixes the stock complex shader's flat-normal lighting;
UV-dependent MikkTSpace generation for normal maps is still required separately.
All four regression tests passed against the updated exact source. Its managed
render test then matched independently authored reference geometry in albedo,
world normals, no-light, front-light and back-light diffuse/specular views for
both source and compiled Triangle. Front lighting affected all 1,225 visible
pixels in each lighting view; the no-light, back-light and empty controls were
black. The comparison counts pixels exceeding 0.03 in any RGB channel and allows
at most five such pixels; all source/reference and compiled/reference counts
were zero. Evidence is in `editor-vertex-frame-managed`.
The same four tests and lighting comparisons also passed through the native
capture path with the scoped clear diagnostic, in
`editor-vertex-frame-camera-clear`.

Those source-compilation proofs alone do not establish deployment into ordinary
engine call sites. The subsequent rebuilt-engine results below do exercise
`Model.Load`; material coverage and full corpus appearance remain incomplete.

### Rebuilt-engine resource loading

`GltfModelLoader` now handles explicit `.gltf`, `.glb` and `.glb_c` paths through
`Model.Load` and `Model.LoadAsync`, using the mounted filesystem and the static
scene assembler. It creates one model containing all selected node/primitive
instances, with transformed bounds, stock complex materials and shared leases.
Opaque base-color RGB factors are supported; authored alpha is ignored for
OPAQUE and retained in the source. Distinct effective colors get distinct
cached materials. Default metallic/roughness inputs map to the experimentally
verified stock parameters. Nondefault factors, textures, vertex colors,
nonopaque/unlit/emissive materials and deformation remain unsupported by this
initial adapter and report errors. This is not complete core support.

The first actual API test failed on the old engine because native loading
expected `.vmdl` (`editor-model-load/667675e6611a4a9692c605f12f8c3e3b`). A later
regression caught cold canonical `.glb` references failing when only `.glb_c`
was shipped (`editor-model-load/f1179af85c1949c08610bf0fd9dcc05d`). The loader now
resolves that compiled file while preserving the logical `.glb` identity.
The separate nonwhite-factor positive test also failed before RGB support was
added. These are real API failures, not assertions against an invented stub.

[editor_model_load.csx](editor_model_load.csx) mounts private fixtures, verifies
their bytes and exercises the actual resource API. `SBOX_GLTF_MODEL_CASE=all`
passed all 13 current cases with 42 loads on the rebuilt engine. It covers both
untouched Khronos Triangle variants, source GLB, direct compiled-file loading,
cold compiled-reference loading without source siblings, multi-node geometry,
RGB factors, distinct material slots and opaque alpha, optional-extension
warnings, required-extension rejection, missing dependencies and URI resolution.
Repeated, equivalent-path and async loads share a model with a stable path/name;
the temporary procedural alias and native GUID are not published as its managed
identity. GUID-only cold loading is not established. Current matrix evidence:
`editor-model-load/4283e26f40c042f8bf87750e100a5cbe/receipt.json`.

With `SBOX_GLTF_MODEL_RENDER=1` and `SBOX_GLTF_CAPTURE_RENDERER=managed`, the loaded
models passed independent stock-material references in albedo, world normals,
no-light, front-light and back-light specular views. All comparisons had zero
pixels exceeding the 0.03 RGB tolerance; no-light, back-light and empty controls
were black. Triangle coverage was 1,225 pixels. The two-color scene covered 320
pixels and omitting each of its four reference primitives changed 80 pixels.
The original embedded variant was tested without an external Triangle buffer.
Receipts beneath `Temp/gltf-validation/editor-model-load-render-managed`:

| Case | Receipt directory |
| --- | --- |
| Cold compiled reference | `25109013245e4fe58dfdeedc0b078246` |
| RGB factor | `594bed55b34a4416bc58e6d3da041a98` |
| Two materials and repeated instances | `b1713ffeb36243639d6aa407a913642f` |
| Original Khronos Triangle | `475cd3bf0b3641cba6aa0ae7c66ccccc` |
| Original embedded Triangle | `c6e93595558d4ab980504c7f31817dde` |
| Direct `.glb_c`, no source sibling | `b23e4b48d9524f9c9d35ce6aaf93fc83` |

The loaded two-color scene also passed the same references and omission controls
through the native capture pipeline with its scoped color-clear diagnostic:
`editor-model-load-render-camera-clear/6d7c219285e545c5bef76147d95d9951`.

[editor_model_lifetime.csx](editor_model_lifetime.csx) additionally proved that
two distinct loaded models share a material, explicit destruction decrements its
lease count 2→1→0, the surviving model remains valid, the last wrapper becomes
invalid, and repeated destruction is harmless. A separate no-inline weak-model
test allowed the normal 5–10 second native-cache retention to expire; after
about 9.4 seconds the model was collected, its last lease released and its
material invalidated. The existing vmdl sphere remained valid. Evidence:
`editor-model-lifetime/ace3a902502543e985d0553fc8520233` on the current modules
(about 9.6 seconds and seven collections). The earlier build's independent run
is retained at `editor-model-lifetime/74b8f2a5f43a4a3ebbf48954a54f919f`.
This establishes managed ownership/finalizer behavior, not GPU deallocation
timing or measured runtime-memory parity.

`SboxBuild build-managed-stage --project Sandbox.Gltf Sandbox.Engine` builds
selected projects serially into a new ignored `Temp` directory, records baseline
dependency and source/output hashes, and leaves the live managed deployment
untouched. It updates shared build intermediates and does not automatically
deploy, restart or validate an editor. The current successful stage is
`staged-build/80092d916c4a414baf5dd011d55ab779`; after a coordinated restart its
Engine module `57b9fe73-4ec9-46fe-bdac-bfc019e3b260` and glTF module
`6fa5ef0a-784e-4311-b6dd-9b6375716f88` were confirmed in the virtual editor.
All current render receipts above use those modules.

The `.glb_c` fixtures remain valid packaging-only GLBs, not GPU-optimized
resources. Source `.gltf` references in compiled-only builds still need an agreed
compiler/asset-identity mapping; no filename collision or automatic build/hot
reload policy is claimed here. Native texture integration, complete materials,
deformation, selected extensions, editor workflows, Windows and the matched
vmdl load/memory benchmark remain requirements of the full goal.

### Next loader regression: textured models

The next API regression is the untouched pinned Khronos BoxTextured asset.
The existing texture preview above bypasses `Model.Load`, which still rejects
its base-color texture and nondefault metallic factor. Its material is opaque,
white-tinted, metallic zero and roughness one, with untransformed UV0. Its
sampler explicitly requests linear magnification, `NEAREST_MIPMAP_LINEAR`
minification and repeat addressing. Merely binding its image at the base mip
cannot establish support for that sampler or full sample parity.

[editor_model_texture_load.csx](editor_model_texture_load.csx) is prepared for
this regression but has not run. It independently calls the actual resource
APIs for the original external, embedded and binary assets plus a labeled
derived compiled-only GLB, retaining every failed case. Success checks geometry,
material parameters, complete base-mip texel readback and cache identity; its
receipt explicitly leaves rendering, mip/sampler behavior and sample parity
unproven. It must first record the actual current loader rejection in the
editor. The editor and installed outputs are temporarily reserved for foliage
validation; this test has not been substituted with a standalone graphics host.

Source inspection confirms that `ReadImage` already supplies external,
data-URI and buffer-view image bytes through the same interface. The existing
`GltfTextureCompiler` writes a native texture resource without a procedural GPU
texture, but currently emits only one uncompressed RGBA8 mip and rejects
16-bit PNG and KTX2. The stock shader's observed `g_tColor` binding is usable for
the next controlled experiment; per-texture sampler attributes, minification,
color-space multiplication and arbitrary UV mappings are not established.
The deprecated generic material includes are not authoritative source for the
installed compiled complex shader.

Before promoting this to textured-loader support, verify precompiled resource
lookup and packaging, all requested sampler behavior, missing-dependency errors,
and two models sharing a texture through independent destruction. Keep authored
PNG/JPEG information intact. Generated textures need content/settings/version
identity and explicit ownership; unloading one model must not dispose another
model's shared `Texture` wrapper. Shipped `.glb_c` must load its precompiled
dependencies rather than rebuild them or resolve against an unshipped source
tree. The source mount root remains the containment boundary; legitimate shared
external textures must not be rejected merely for living above a model folder.

No new extension or texture packaging convention is selected by this audit.
The [glTF extension registry](https://github.com/KhronosGroup/glTF/blob/main/extensions/README.md)
and [MSFT_texture_dds specification](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Vendor/MSFT_texture_dds/README.md)
were checked on 2026-11-05: DDS support defines a different image representation,
not an existing mapping to native s&box `.vtex_c`. Investigate the established
resource compiler's child dependencies before introducing custom metadata.
These are pending integration checks, not a newly proven native blocker.

### Skeleton and animation integration checks

The current `Model.Load` adapter still rejects skinned, morphed and animated
selected nodes. Existing CPU readers/evaluation do not establish native model
loading or playback. [editor_animation.csx](editor_animation.csx) is a prepared,
unrun editor check of analytic translation/rotation/scale expectations for
STEP, LINEAR and CUBICSPLINE, including keys, intermediate times, immediate
STEP-boundary neighbors, hierarchy and a skinned vertex. Its arithmetic epsilon
is a CPU sanity check, not an approved native animation approximation tolerance.

Native `ModelBuilder.AddAnimation` takes a frame rate and `AnimationBuilder`
stores frames in skeleton order. `DisableInterpolation` applies to the whole
clip. These APIs alone do not prove preservation of arbitrary glTF key times,
mixed interpolation channels or cubic curves. Measure native seek/playback at
fractional times before choosing any sampling policy. Also investigate exact
managed evaluation followed by bone upload through the normal model-update
callback; fixed-rate baking is not assumed to be the only implementation path.

The skeleton path must separately prove inverse-bind matrices, parent-relative
bones, rest transforms, weighted GPU deformation and additional influence sets
against independently evaluated geometry. Rest scale/shear, native influence
limits and graph interaction remain open integration checks. Treat an exposed
API limitation as a question to investigate, not a proven native blocker.

[editor_skinning.csx](editor_skinning.csx) prepares source/compiled two-joint
fixtures with inverse-bind offsets, hierarchy and normalized weight sets. Its
private native construction uses independent bind/posed reference geometry,
actual preview callbacks and empty-frame controls, with an optional managed
capture route. The separate `Model.Load` observation cannot pass merely because
the direct native construction does. More than four nonzero influences, rest
scale/shear and loaded-model deformation remain unproven. The probe is unrun.

[editor_animation_playback.csx](editor_animation_playback.csx) prepares native
fixed-frame seek measurements and an actual load of the pinned embedded
AnimatedTriangle. It records fractional and boundary-adjacent times, local/world
bones and mixed-channel reference errors. This is a measurement probe with no
approved approximation tolerance, and remains unrun.

### Core geometry regression checks

[editor_primitive_modes.csx](editor_primitive_modes.csx) prepares 42 cases across
seven primitive modes, source/cold compiled-only storage, and three authored or
missing normal/tangent combinations. It requires independent native topology,
visible omission controls, metadata and image comparisons, and empty frames.
Individual mode/format/direction environment selections allow bounded runs.
For points/lines without normals it does not invent a normal direction; the
specification's recommended unlit material behavior needs separate verification.
This probe is unrun; its generated GLBs do not prove optimized GPU layout.

The static-scene unit tests now include positive all-zero and rank-two node
scales, replacing an incorrect expectation that all singular transforms should
be rejected. Zero scale is valid in the
[core transformation rules](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#transformations).
The production assembler still rejects these cases. Record the actual failing
editor run before implementing the correction. The revised `editor_scene.csx`
can compile the worktree helper even when that helper already exists in the
loaded module: it renames three type identifiers and records both original and
compiled source hashes. The algorithm and tests otherwise remain unchanged;
this does not substitute for rebuilding and validating the production engine.

[editor_quantization.csx](editor_quantization.csx) prepares two actual-load
checks for a pinned synthetic `KHR_mesh_quantization` triangle with strided
SHORT positions, normalized BYTE directions and node dequantization scale.
The source and cold compiled-only cases verify independent native geometry
readback, bounds, material and cache identity. Both are unrun. The current
loader advertises no extension capability and rejects this required extension
before rendering; that is an integration gap, not evidence of a native GPU
limitation. The probe does not claim rendering, full extension conformance or
approved release tolerances.

This tool inventories every model variant in the pinned [Khronos sample assets repository](https://github.com/KhronosGroup/glTF-Sample-Assets) and aggregates independently produced evidence. The pin is [90d7ede14c7e280af263824604b427a1ca02cb66](https://github.com/KhronosGroup/glTF-Sample-Assets/commit/90d7ede14c7e280af263824604b427a1ca02cb66), a dated upstream commit, not a claim about current HEAD. It is recorded in `coverage.json`. No model assets are included in this repository.

This is scaffolding, **not an s&box adapter, glTF conformance validator, reference evaluator, image comparison or performance benchmark**. It checks enough of a `.glb` to read its JSON chunk, but does not validate the rest of the GLB or glTF specification. Artifact hashes establish file integrity, not that a claimed adapter ran or that its assertions are correct. Consequently even a well-formed, hash-matching evidence record reports `adapter-integration-pending` and cannot pass. No engine capability is established by running this tool alone.

## Corpus and licenses

Obtain the upstream repository and its complete Git LFS content through the project's approved network workflow, then check out the exact manifest commit. The tool does not download or alter it. A missing repository, wrong commit, local changes under `Models` or `LICENSES`, missing indexed assets, LFS pointer instead of an indexed asset or referenced buffer/image, unsafe paths and malformed asset JSON all prevent either pass. Do not run against a partial or modified corpus. Git status does not verify ignored files or changes outside these paths; CI should use a fresh, trusted checkout.

Preserve each model's upstream README, metadata, attribution and applicable license, including any relevant files under `LICENSES`, when obtaining, retaining, distributing or publishing results using its assets. License terms vary by model; repository code licensing is not a blanket license for model content. The report records available per-model provenance files; it does not interpret licenses. A model without either README or metadata is an inventory error. Review licenses and any asset defects individually.

## Run

```sh
python3 -B research/gltf-validation/validate.py --corpus /path/to/glTF-Sample-Assets > report.json
python3 -B research/gltf-validation/validate.py --corpus /path/to/glTF-Sample-Assets --evidence /path/to/evidence.json > report.json
python3 -B research/gltf-validation/validate.py --coverage-status > pending-integrations.json
python3 -B -m unittest discover -s research/gltf-validation -p 'test_*.py' -v
```

Exit status is 0 only for an `applicable_full_pass`, 1 for an incomplete or failed applicable assessment, and 2 for a fatal setup error. With no adapters or evidence, status 1 is expected. `--coverage-status` needs no corpus and emits the full machine-readable list of pending integration categories and acceptance coverage, including the Linux and managed-first renderer targets. The corpus report contains every indexed variant, actual declared `extensionsUsed` and `extensionsRequired`, external buffer/image paths, provenance paths, scope and separate proof status for parser, numeric, render and compiled output. Performance is reported separately. Index tags are recorded, not used to determine core status. Changing the selected extensions requires review and an explicit manifest change. Duplicate JSON keys are rejected. An unindexed `.gltf` or `.glb` in a conventional `Models/<model>/glTF*` variant folder is a fatal inventory discrepancy requiring investigation; it cannot disappear silently from coverage.

The report has two distinct outcomes. Once real integrations exist, `applicable_full_pass` will require at least one applicable case; every core-only variant and every variant using only selected extensions must have all four proofs, and every coverage item and the global benchmark must pass. **Any inventory error blocks it**, including errors in variants whose scope could not be established. Variants using at least one unselected extension are listed as excluded with the extension names and are never counted as supported; these exclusions alone do not prevent the applicable goal from passing. A mixed selected/unselected variant is excluded as a whole, so it cannot prove support for its selected extensions; the manifest requires separate evidence for each selected extension. `corpus_full_pass` additionally requires no excluded variants. Do not remove excluded variants from the index to make either result green. At present both outcomes are always false: no adapters are integrated.

`KHR_meshopt_compression` is explicitly pinned to Khronos glTF commit [3e9dfff91d52cd79fa0169e8453b7fdaca663fd8](https://github.com/KhronosGroup/glTF/commit/3e9dfff91d52cd79fa0169e8453b7fdaca663fd8); its [specification](https://github.com/KhronosGroup/glTF/blob/3e9dfff91d52cd79fa0169e8453b7fdaca663fd8/extensions/2.0/Khronos/KHR_meshopt_compression/README.md) is a release candidate, not a ratified specification. This is a separate pin from the asset corpus. Reassess maturity, compatibility and changes before advancing it.

## Evidence contract

The optional evidence file is JSON. `corpus_commit` must exactly match the pin. `cases` maps each `model/variant` ID from the report to four independent proofs: `parser`, `numeric`, `render` and `compiled`. `coverage` maps **every** `required_coverage` item in the manifest to its evidence. `performance` is a separate global proof. Each proof requires:

```json
{
  "result": "pass",
  "fallback": false,
  "adapter": {"name": "adapter-name", "version": "version-or-revision"},
  "artifacts": [{"path": "relative/path/to/evidence", "sha256": "64-lowercase-hex-characters"}]
}
```

Artifact paths are relative to the evidence file's directory and must remain within it. Render proofs also require nonempty `reviewer`, `reference` and `conditions` strings. Performance proofs also require `hardware`, `thresholds` and `metrics` strings. Real artifacts must contain the results and provenance that substantiate these assertions. A failed, skipped, missing or fallback proof cannot pass. Invalid or mismatched hashes cannot pass. Even correctly shaped records with matching hashes remain pending: adding a reviewed executable adapter integration, binding its exact version and inputs to the run, validating its output and defining trustworthy visual-review and benchmark provenance are prerequisites for replacing the explicit gate. Merely registering adapter names, versions or hashes from the evidence file is insufficient. Tests mock completed adapter results solely to exercise future aggregation; they do not validate engine behavior.

The current gate requires all four case proofs even where an asset has no visible geometry or animated values. Define case-specific applicability through a reviewed extension of this contract before relaxing it; absent results are not assumed inapplicable. Coverage IDs are placeholders for independently reviewed concrete fixtures and acceptance criteria, not claims that those tests exist. In particular, thresholds for animations and performance remain to be agreed after measurement.

## Integration still needed

- An official glTF validator adapter and an s&box import/load adapter reporting reproducible errors; rejection of unsupported required extensions and optional fallbacks need dedicated negative tests.
- Reference numerical evaluation at keys and between keys, including STEP boundaries, skinning, morphs and transformations, with measured and approved tolerances.
- Captures and human visual review against a documented reference renderer under matched conditions; preserve image, renderer versions and review records.
- Actual portable `.glb_c` generation, structural validation, dependency packaging, source and compiled runtime behavior, plus editor, reimport and vmdl parity fixtures.
- Matched vmdl and `.glb_c` static and animated benchmarks: load time, process memory, available GPU-memory evidence and package size, with agreed thresholds and hardware/run details.
- Linux execution first, then Windows validation before the full goal is complete, as requested. Keep separate platform evidence, a machine-readable mapping from coverage IDs to concrete tests, pinned tool versions, CI artifacts and a reviewed process for documented upstream defects and scope exclusions.
