# glTF sample validation scaffold

## Where things live

- Docs, JSON manifests, receipts, captures, reference recipes and fixtures: this folder.
- Editor probes (`editor_*.csx`), Python and Node runners, and the `Run` driver: [`engine/Tools/Gltf/`](../../engine/Tools/Gltf/README.md).
- Per-model code: `engine/Tools/Gltf/direct-gpu/`. Per-model requests, results and docs: `direct-gpu/models/<Model>/` in this folder.
- Move history and rules for old paths: [CODE_LOCATIONS](CODE_LOCATIONS.md).

Status as of 2026-11-08 is in [FEATURE_STATUS](FEATURE_STATUS.md), section "Status on 2026-11-08".


The current corpus entry point is [Run](../../engine/Tools/Gltf/scripts/Run), using the public model APIs in
[editor_corpus.csx](../../engine/Tools/Gltf/editor/editor_corpus.csx). Its stock-resource proof recursively pins
resource dependencies and audited shader parents before reusing captures.
The selected `vertex_color.shader` compile took 29.575062905 seconds and produced
`8f4ecfe974cbd25d5cc2319f6a44fb760e47e49e573ca303cb32a94535756f46`.
Its compiled FEATURE metadata retains Forward, delegated Depth and delegated
ToolsShadingComplexity; both delegated shader hashes are unchanged. Container,
program bounds, preserved GUID and dependency identity were checked. This proves
the compiled resource closure, not shader appearance or runtime mode correctness;
new captures and visual review remain required. The editor observations below
describe their original, narrower fixtures.

Rendering acceptance runs in the real s&box editor, using its virtual display when
requested. The user chose editor-only validation on 2026-11-04; do not substitute
a standalone `TestAppSystem` render or manually pump engine frames for this proof.
Reuse an existing authorized editor and confirm its project with `editor_status`.
Use [editor_render.csx](../../engine/Tools/Gltf/editor/editor_render.csx) through the discovered `run_csharp`
tool. It creates its own `SceneRenderingWidget`, waits for real editor frames,
and writes PNGs plus a scoped receipt under `Temp/gltf-validation/editor-render`.
It checks the pinned Triangle's source and packaged geometry, both windings and
camera sides, single/double-sided stock materials, shared material leases and
empty-frame controls. This is one geometry fixture, not full corpus parity.

[editor_texture.csx](../../engine/Tools/Gltf/editor/editor_texture.csx) compiles and packages the pinned
BoxTextured sample inside the editor, compiles its PNG into a native RGBA texture,
and renders through the shared material cache and stock complex shader. It checks
all native texture texels, source/package render agreement, an untextured control,
and black initial/disabled frames. Its receipt and PNGs are under
`Temp/gltf-validation/editor-texture`. This covers a static base-color fixture,
not complete PBR, sampling, mip or material conformance.

[editor_cache.csx](../../engine/Tools/Gltf/editor/editor_cache.csx) checks the actual registered
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

[editor_morph.csx](../../engine/Tools/Gltf/editor/editor_morph.csx) constructs a pinned, self-contained glTF with
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

[editor_material_schema.csx](../../engine/Tools/Gltf/editor/editor_material_schema.csx) reads the actual loaded
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

`../../engine/Tools/Gltf/editor/editor_render.csx` additionally accepts `SBOX_GLTF_CAPTURE_RENDERER=managed` to
call the already-registered `SceneBridge.RenderToTexture` for its private camera
and target after the normal preview callbacks. It does not change the global
viewport flag. In this path the complete Triangle/winding/sharing matrix and
deleted/fresh empty controls passed without the color-clear diagnostic. The
receipt is `editor-render-managed/receipt.json`. The native stale-frame control
remains a separate failure.

[editor_scene.csx](../../engine/Tools/Gltf/editor/editor_scene.csx) compiles the exact new
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

[editor_material_channels.csx](../../engine/Tools/Gltf/editor/editor_material_channels.csx) compiles eight
controlled PNG channel fixtures into native linear texture resources, verifies
their actual RGBA readback and measures stock complex debug output. Under the
tested settings, normal-texture blue values 32/224 produced roughness output
32/224, while its other channels did not change roughness. This identifies a
packing candidate; it does not prove factor equations or independent UV/sampler
support. Its metalness control was unresponsive, so the overall receipt remains
failed at `editor-material-channels-camera-clear`.

[editor_metalness_lighting.csx](../../engine/Tools/Gltf/editor/editor_metalness_lighting.csx) adds direct-light
controls and an optional short `SBOX_GLTF_LIGHTING_CONTROLS_ONLY=1` mode. Its
`SBOX_GLTF_CAPTURE_RENDERER=managed` option keeps both capture paths separate.
The first tests found that the stock vmdl sphere lights correctly, while both
the glTF triangle and an independently built native triangle with a zero tangent
produce zero world normals and no lighting. Supplying an explicit unit tangent
restores the expected +X normal and front-light response over all 1,225 covered
pixels; a light behind the triangle contributes zero. This isolates a runtime
mesh vertex-frame prerequisite. Preserve the failed original controls while
fixing primitive staging; do not infer metalness behavior from an unlit mesh.

[editor_vertex_frame.csx](../../engine/Tools/Gltf/editor/editor_vertex_frame.csx) compiles the exact primitive
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

[editor_model_load.csx](../../engine/Tools/Gltf/editor/editor_model_load.csx) mounts private fixtures, verifies
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

[editor_model_lifetime.csx](../../engine/Tools/Gltf/editor/editor_model_lifetime.csx) additionally proved that
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

[editor_model_texture_load.csx](../../engine/Tools/Gltf/editor/editor_model_texture_load.csx) is prepared for
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

Current integration status:

| Layer | Implemented | Evidence still required |
|---|---|---|
| glTF data and evaluation | Joint hierarchy, inverse binds, multiple influence sets, morphs, STEP/LINEAR/CUBICSPLINE channels and poses | Run the queued focused tests inside the editor against the intended module versions |
| Full-matrix palette staging | Immutable skin definition, coordinate conversion and reusable instance destinations | Run the new analytic palette tests; these do not prove GPU deformation |
| Managed renderer | Instance-local direct palette upload hook | Build/restart after the shared-editor handoff, then verify transform-buffer behavior and actual stock-shader deformation |
| Model and component integration | Static untextured `Model.Load` is editor-proven | Skinned/animated `Model.Load`, normal component playback, posed bounds and animation graph references are not integrated |

The next rig milestone is an actual loaded, two-joint animated model using the
ordinary component path, with independent bind/posed geometry comparisons and
two instances at different animation times. Before accepting that milestone,
also establish native bone/weight metadata and interpolation behavior; do not
silently truncate influence sets or substitute fixed-rate baking for the glTF
sampler. Existing vmdl behavior remains a regression requirement.

The current `Model.Load` adapter still rejects skinned, morphed and animated
selected nodes. Existing CPU readers/evaluation do not establish native model
loading or playback. [editor_animation.csx](../../engine/Tools/Gltf/editor/editor_animation.csx) is a prepared,
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

[editor_skinning.csx](../../engine/Tools/Gltf/editor/editor_skinning.csx) prepares source/compiled two-joint
fixtures with inverse-bind offsets, hierarchy and normalized weight sets. Its
private native construction uses independent bind/posed reference geometry,
actual preview callbacks and empty-frame controls, with an optional managed
capture route. The separate `Model.Load` observation cannot pass merely because
the direct native construction does. More than four nonzero influences, rest
scale/shear and loaded-model deformation remain unproven. The probe is unrun.

The probe now also records the fixture's original joint order and inverse binds,
the built model's master-bone identities/parents, and each native mesh's render
palette mapping, inverse binds, blend count and vertex count. It uses the
existing renderer readback helper only after checking its loaded/disk module
identity. Unavailable metadata is reported explicitly; matching names or counts
does not establish a glTF-to-native mapping. These additions remain unrun.

The adapter must map each native render bone through its master bone to the
corresponding joint entry of that mesh instance's glTF skin. A vertex `JOINTS_n`
value indexes that skin's joint list. Different skins can use the same glTF node
with different joint order or inverse binds, and the same source mesh can be
instanced with different skins. Nonjoint ancestors are included in the glTF pose
hierarchy. Native vertex-index interpretation and generated master/render-bone
order still need actual editor readback before packing can rely on them.

[editor_animation_playback.csx](../../engine/Tools/Gltf/editor/editor_animation_playback.csx) prepares native
fixed-frame seek measurements and an actual load of the pinned embedded
AnimatedTriangle. It records fractional and boundary-adjacent times, local/world
bones and mixed-channel reference errors. This is a measurement probe with no
approved approximation tolerance, and remains unrun.

`GltfSkinningPalette` now stages full matrices in original skin-joint order from
an immutable snapshot of joints and inverse binds. It changes coordinate basis
after multiplying inverse bind by the joint scene transform, then applies an
optional engine-space object placement once. Its reusable destination API
validates before writing and does not require an array allocation per update.
This helper has not been connected to a renderer or `Model.Load`.

[editor_skinning_palette.csx](../../engine/Tools/Gltf/editor/editor_skinning_palette.csx) is the unrun editor
runner for its new unit tests. It compiles the worktree helper/test sources with
one renamed type identifier and records original/compiled hashes and module
identities. Cases cover valid TRS hierarchies producing shear, independent
analytic point transforms, inverse-bind/joint order, ignored skinned mesh-node
transforms, noncommuting object placement, source/compiled parity, immutable
snapshots and safe destination reuse/failures. These are CPU staging checks,
not GPU deformation or native playback proof.

The managed renderer already has full `Matrix3x4` palette storage. Its existing
scene bridge first obtains `Transform` bone values, and `WriteBones` then
multiplies native inverse binds. A glTF full-matrix bridge must preserve affine
data through that boundary and avoid applying the inverse binds a second time.
Keep instance palettes out of shared mesh/material state and preserve legacy
model behavior. Native draw metadata, influence packing, bounds, shadows,
multiview updates and native-camera rendering still need independent proof;
the reflected managed capture route alone cannot establish them.

The unbuilt managed renderer now has an internal per-instance, per-model-mesh
direct palette hook. It copies final world-space matrices in native render-bone
order, validates count and finite components, and sends them to the transform
buffer without another inverse-bind or object-transform multiply. Explicit
overrides survive material/bodygroup draw variants sharing the exact native
model and skin metadata. Incompatible mesh replacement or removal clears them;
clearing an override restores the latest legacy bone pose. No glTF loader or
scene-mirror provider supplies these matrices yet.

`DirectSkinningPaletteTests` covers CPU entries for one/four weight layouts,
independent instances, shared-skin draw variants, invalid input, fallback and
transform-buffer generations. It does not advance `Application.FrameCount` or
claim GPU submission timing. The optional shadow-slot observation is not shadow
coverage. The hook must be submitted before frame preparation; native compute
metadata/weight precision and actual GPU deformation remain unproven. The
mirror's visibility-to-null transition will need provider reapplication on show,
along with posed bounds and ordinary component/playback integration. These
source changes and tests are unbuilt and unrun.

[editor_direct_skinning_palette.csx](../../engine/Tools/Gltf/editor/editor_direct_skinning_palette.csx) queues
those tests against the actual loaded Engine/SceneRenderer modules. It checks
disk/loaded module identities, reports a missing producer API explicitly, and
compiles only the test and existing fixture source in the established friend
test-assembly identity. It runs DataRows separately, leaves unit-test mode
unchanged and verifies that synchronous tests do not advance the editor frame
counter. Rebuilding and restarting the renderer is still required before its
new implementation can be tested; source compilation of tests does not replace
that step. This runner is also unrun.

### Core geometry regression checks

The complete feature-level implementation/test audit is in
[FEATURE_STATUS.md](FEATURE_STATUS.md). The fixed `GltfGeometryBuffer.GltfVertex`
and `GltfModelPrimitiveBuilder.StockVertex` paths do not meet the requirement for
source-driven GPU layout. The new `DescribePrimitiveUpload` API records authored
views, offsets, strides, component types/counts and normalization, plus explicit
sparse/zero-base/codec materialization and source/decoded topology. Its tests
cover distinct streams, interleaving, padded matrix columns, primitive-scoped
Draco data, implicit indices and valid view-relative alignment. This is unrun
metadata staging; the fixed renderer paths remain in use and are not replaced.
Actual dynamic native bindings or vertex pulling, owned byte lifetimes, sparse
materialization and stock-shader compatibility remain implementation work.

[editor_primitive_modes.csx](../../engine/Tools/Gltf/editor/editor_primitive_modes.csx) prepares 42 cases across
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
editor run before implementing the correction. The revised `../../engine/Tools/Gltf/editor/editor_scene.csx`
can compile the worktree helper even when that helper already exists in the
loaded module: it renames three type identifiers and records both original and
compiled source hashes. The algorithm and tests otherwise remain unchanged;
this does not substitute for rebuilding and validating the production engine.

[editor_quantization.csx](../../engine/Tools/Gltf/editor/editor_quantization.csx) prepares two actual-load
checks for a pinned synthetic `KHR_mesh_quantization` triangle with strided
SHORT positions, normalized BYTE directions and node dequantization scale.
The source and cold compiled-only cases verify independent native geometry
readback, bounds, material and cache identity. Both are unrun. The current
source loader now advertises `KHR_mesh_quantization` and uses the existing
accessor decoder before float-buffer staging. It still advertises no shader or
codec extensions. `GltfModelDocumentTests` exercises this actual loader policy
for source and compiled input, independent positions/directions/bounds, native
buffer staging without allocation, undeclared quantization rejection, and
unknown required/optional extension policy. The producer change and five test
cases are unbuilt/unrun; rebuild and restart before the actual-load probes can
exercise this policy. The probe does not claim rendering, full extension
conformance or approved release tolerances.

The reader's primitive validator now requires both quantization capability and
the asset's `extensionsRequired` declaration before accepting extension-only
vertex layouts. Previously the capability alone enabled those layouts even in
undeclared assets. New `GltfQuantizationDeclarationTests` cover POSITION,
NORMAL, TANGENT and TEXCOORD declarations, unsupported required usage, and core
FLOAT/unsigned-normalized UV inputs. The fix follows the ratified
[KHR_mesh_quantization specification](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_mesh_quantization/README.md).
The separate loader-policy change above enables decoding for `Model.Load`;
these declaration tests remain unrun.

[editor_gltf_unit_tests.csx](../../engine/Tools/Gltf/editor/editor_gltf_unit_tests.csx) queues those tests against
the loaded glTF module, without compiling production source. Manifest version 5
inventories 44 source classes, 416 methods, 409 DataRows and 732 expanded cases,
including the revised UploadData/UploadLayout suites and the raw Draco primitive
suite. These are source counts, not
execution results or a claim of complete specification coverage.

An optional `gltf-unit-request.json` in the current connection's
`exchangeDirectory` selects any manifest suite alias, or `all` for the complete
`Sandbox.Test.Gltf` project. A valid request takes precedence over the editor's
`SBOX_GLTF_UNIT_SUITE` environment variable. Only an absent request retains that
environment selection, or the `quantization-declarations` default when the
editor has no nonblank environment value. Setting an environment variable on a
new CLI process does not change the existing editor's environment. The runner
changes neither process environment nor CWD.

The request must be UTF-8 JSON, between 1 and 4096 bytes, with exactly these four
properties, no duplicate properties, integer version `1`, a 32-character
lowercase hexadecimal request ID, an exact suite alias, and the lowercase
SHA-256 of the manifest bytes used for this run:

```json
{"version":1,"request_id":"0123456789abcdef0123456789abcdef","suite":"all","manifest_sha256":"<64 lowercase hexadecimal characters>"}
```

A malformed or stale request fails the run; it cannot fall back to environment
or default selection. The receipt records `selection_source` (`request`,
`environment` or `default`), `request_path`, `request_sha256`, `request_id` and
`request_values`. Empty or oversized inputs are rejected before hashing;
invalid UTF-8 or JSON has a hash but no parsed values. The manifest, runner,
test sources and fixtures keep their independent hashes.

The runner checks source-file inventory and fixed class/method/row/case totals,
supports both TestMethod and DataTestMethod, and compiles selected test sources
plus their declared source dependencies. DataRow arguments support exact CLR
`string`, `int`, `uint`, `byte`, `bool` and `float` values; argument types must match the
method parameters without conversion. Receipts preserve `argument_types` beside
the JSON argument values, including blocked cases and codec preflights. It embeds the actual checked-in KTX
fixtures and records their hashes with test, manifest and framework hashes.
The helper references the glTF library, BCL and MSTest, matching the pure test
project, and checks loaded/disk producer identity. It executes cases sequentially,
yields between synchronous calls, honors cancellation and checks the editor
frame counter without writing it or changing process CWD/unit-test mode.

Known-good decoder tests run before any dependent codec suite. A failed
preflight blocks its dependent cases; malformed-input tests cannot pass merely
because a missing decoder also throws. Preflight results and module observations
are separate from project-case totals. They prove only their sampled paths;
the remaining codec cases still determine other modes and formats. The revised
Draco preflight is `NativeBoxDecodeSuppliesRawFloatAttributesAndDecodedTopology`
from the raw primitive suite. Its other cache-seeded cases exercise adapter and
representation behavior; they do not establish native decoding of those formats.
The UploadData suite requires the positive meshopt preflight. Root's first
actual-editor attempt failed before test execution because the method-body
wrapper did not import the namespace containing `GetValueOrDefault`. The
runner now uses `TryGetValue` explicitly. A second actual run compiled the
runner but its test-source compilation reported 16 CS9007 raw-literal errors;
the receipt records zero executed cases and 485 partial cases. Owners are
correcting those literals. The orchestrator retains the negative receipt and
reports its phase/error/diagnostics before validating identities that are absent
when compilation fails. A subsequent 485-case run completed with 366 passed,
two singular-transform failures and 117 blocked cases from three unavailable
native codecs; that is not a complete pass. The new singular-transform
regression raised the next inventory to 486. Root later recorded 483 passed and
three singular-transform failures with no blocked/partial cases, and all three
codec preflights passed. Manifest v5 now adds 246 authored, unrun core
data/material/animation/scene cases for 732 total. A complete project pass remains separate from Khronos
corpus rendering, native component/editor behavior and performance acceptance.
An installed module missing a newly added API must be rebuilt/restarted before
that suite can compile; compiling test sources never substitutes for that step.

### Run selection within one attached CLI connection

[run_editor_tests.py](../../engine/Tools/Gltf/scripts/run_editor_tests.py) implements the following protocol with
two explicit modes. Root runs each entire foreground command through the
resource guard after verifying the physical desktop editor. First use
`python3 engine/Tools/Gltf/scripts/run_editor_tests.py discover --pid PID --project /absolute/project.sbproj
--output /absolute/new-discovery-directory`. Inspect its `instructions.txt`,
`schemas.json`, full JSON replies and `discovery.json`. It dispatches no scripts
or tests. Then use `python3 engine/Tools/Gltf/scripts/run_editor_tests.py run --pid PID --project
/absolute/project.sbproj --output /absolute/new-run-directory
--confirmed-discovery /absolute/new-discovery-directory/discovery.json
--confirmed-sha256 HASH`, substituting the discovery file's printed SHA-256.
Run mode repeats discovery and rejects changed schemas or frozen inputs before
dispatching the unchanged runner with a same-session `all` request. It retains
the full receipt, checks the 732-case result and records console output. It
never launches or kills an editor. Transport failures produce incomplete/error
artifacts and a cooperative disconnect; a normal disconnect follows the final
response. Root's attempts have exercised the protocol; complete-suite acceptance
remains unverified.

[run_editor_probes.py](../../engine/Tools/Gltf/scripts/run_editor_probes.py) reuses that reviewed connection for
an explicit root-provided list, for example
`python3 engine/Tools/Gltf/scripts/run_editor_probes.py --pid PID --project
/absolute/project.sbproj --output /absolute/new-probe-directory
--confirmed-discovery /absolute/discovery.json --confirmed-sha256 HASH
editor_animation.csx editor_skinning_palette.csx`. Root guards the whole command
and chooses CPU and rendering batches separately. Full replies, per-probe
console/status snapshots and safe returned receipt copies stay in the artifact
directory; stdout is concise progress only. Existing fixed-output evidence is
archived before overwrite, within an 8192-file/512-MiB batch bound. The executor
does not interpret a common `passed` field or claim feature acceptance. It
records human scene changes and stops rather than changing play/compile state.
Optional `--model-request /absolute/request.json` accepts only the existing
six-field model-probe schema with `selection: "all"` and `render: true`, and
copies it unchanged to the actual session. It accepts no arbitrary editor code.
The batch executor remains unrun by its author.

First satisfy the resource guard and existing-editor identity checks. Use the
installed CLI's `--connect <verified-pid>` protocol mode and keep its stdin open
until the final response arrives. A new connection has a new exchange directory.
`--call` initializes and immediately invokes its one tool, so it does not provide
an opportunity to prepare that connection's request file. An earlier `--call`
connection's exchange directory cannot select a later run.

The exact sequence on one process is:

1. Start `/absolute/path/to/game/bin/managed/sbox-cli --connect <verified-pid>
   --request-timeout 900` with UTF-8 stdin/stdout pipes and stderr inherited or
   drained separately. Send one JSON object per line, without a BOM. Flush after
   each message; match responses by ID and ignore notifications. Treat any
   JSON-RPC `error`, including an error with null ID, as fatal.
2. Send initialization and wait for response ID 1. Read its instructions and
   require `_meta.pid`, `_meta.kind == "editor"` and the resolved `_meta.project`
   to match the editor already verified for this task. Retain the returned
   `_meta.exchangeDirectory` as `exchange`.

   ```json
   {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"gltf-validation","version":"1"}}}
   ```

3. Send the initialized notification, then list entry points and wait for ID 2:

   ```json
   {"jsonrpc":"2.0","method":"notifications/initialized"}
   {"jsonrpc":"2.0","id":2,"method":"tools/list"}
   ```

4. Call `editor_status` and confirm its project and responsiveness. Discover
   `run_csharp` with `search_tools`, checking its current argument schema. Wait
   for each matching response and reject `result.isError == true`.

   ```json
   {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"editor_status","arguments":{}}}
   {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"search_tools","arguments":{"query":"run_csharp"}}}
   ```

5. Read the canonical manifest and runner bytes from this checkout. Hash both.
   Create the four-field request using the manifest hash and a fresh
   `uuid.uuid4().hex` request ID. Write it as UTF-8 without BOM to
   `exchange/gltf-unit-request.json`. Copy the runner bytes unchanged to
   `exchange/editor_gltf_unit_tests.csx`; verify that copy's hash equals the
   canonical runner hash. Record the request bytes' hash before invoking. Do not
   insert a selection assignment or otherwise rewrite the script body.
6. Substitute the actual absolute path in this request and wait for response
   ID 5 before closing stdin:

   ```json
   {"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"run_csharp","arguments":{"path":"<exchange>/editor_gltf_unit_tests.csx"}}}
   ```

7. Retain the complete response and read the returned receipt. Require the
   requested suite, `selection_source == "request"`, request ID, exact request
   path/hash/values, manifest hash and original runner hash to match the inputs
   recorded above. A complete project pass additionally requires
   `manifest_version == 5`, `all_project_cases_passed == true`, 732 expected and
   passed cases, and zero failed, blocked or partial cases. Verify the three
   positive codec preflights separately. Close stdin only after the last
   response, then wait for this attached CLI to exit; the editor stays open.

These instructions follow the installed CLI's
`skills/sbox-cli/references/protocol.md` and `run-csharp.md`. They prepare no new
editor, change no global environment/CWD, and have not been executed for the
version 4 runner.

### Request regression matrix for the central editor test run

All cases below remain **unrun**. Root runs them using the connection sequence
above, replacing only the request bytes between fully completed calls. For
every rejected request require `passed == false`,
`all_project_cases_passed == false`, zero executed/preflight cases, an actionable
error, and a receipt. Verify the request hash for every nonempty input no larger
than 4096 bytes, including invalid JSON/UTF-8. Rejection never falls back.

| Request case | Required observation |
|---|---|
| Request absent | Source is `environment` if the existing editor already has a nonblank selection; otherwise `default` and `quantization-declarations`. Do not set editor environment to arrange this case. |
| Valid `accessor` request | Source is `request`, request/hash/values match, and exactly 15 accessor cases are selected. |
| Valid `all` request | Source is `request`, all 732 manifest cases are selected, and positive Draco, meshopt and KTX preflights precede dependent cases. |
| Valid request padded with JSON whitespace to exactly 4096 bytes | Request accepted; hash includes the padding. |
| Empty input; 4097-byte input | Rejected as empty/oversized; no request hash or parsed values. |
| Invalid UTF-8; BOM; malformed JSON; trailing second JSON value | Rejected during request parsing; no parsed values. |
| `null`, array or scalar root; JSON deeper than four levels | Rejected before suite execution. |
| Each missing property; extra property; duplicate property | Exact schema/uniqueness rejection. |
| Version `0`, `2`, `1.0`, string `"1"`, boolean or null | Integer version 1 required. |
| ID wrong length, uppercase or nonhex; nonstring ID | Rejected with request source and no suite execution. |
| Manifest hash wrong length, uppercase, nonhex, nonstring or stale | Rejected before suite execution; compare the supplied and observed manifest hashes in the receipt. |
| Empty, whitespace, unknown, oversized or nonstring suite | Rejected; an unknown alias must not select the default suite. |

`GltfTextureTransformDeclarationTests` now checks that a selected
`KHR_texture_transform` is listed in `extensionsUsed`, while preserving valid
used-only transforms and optional unsupported fallback. Expected UVs independently
pin scale, rotation and offset order. This is local reader validation following
the core [extension declaration rules](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#specifying-extensions),
not whole-document validation or rendered UV proof. The new fix/tests are unrun.

The material cache now ignores a sampler value in rendering-equivalence identity
only when it has no bound sampler parameter, matching the actual material
creation path. Bound sampler values/names and texture identity/revision remain
distinct. New tests check equal hashes, one shared creation and final-lease
release; the existing collision test now binds its sampler explicitly. These
tests and the cache change remain unbuilt/unrun. This does not authorize ignoring
glTF sampler semantics or establish stock-shader sampler consumption.

`GltfBasisFallbackTests` covers optional Basis image selection versus core image
fallback and required-extension rejection. The reader now rejects a Basis-only
texture unless `KHR_texture_basisu` is required, as specified by its ratified
[fallback rules](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_texture_basisu/README.md#using-without-a-fallback).
These are metadata checks, not native Basis decoding or loader support; the
new tests and fix are unrun.

`GltfSamplerMapping` now converts explicit glTF min/mag/mip and S/T wrap choices
to a `SamplerState`, with caller-selected policy for omitted filters. Tests
cover all twelve filter pairs, wrapping, parsed input and invalid values. It is
not connected to the loader or stock shader, and remains unbuilt/unrun. Modes
without mip filtering require a single-mip texture or view; a sampler descriptor
alone does not meet that requirement. In particular, setting Vulkan maximum LOD
to zero can lose the requested independent min/mag behavior.

The revised [editor_material_schema.csx](../../engine/Tools/Gltf/editor/editor_material_schema.csx) preserves
the native shader-properties JSON as well as the typed variables/combos schema,
and reports mounted source/compiled filenames. Each run has a unique receipt
and raw-metadata hash. This query loads the stock material but does not create
samplers, choose permutations or render; the new version is unrun. Additional
JSON fields or mounted filenames alone cannot establish independent per-texture
sampling support.

### Raw compiled-GLB asset pipeline audit

The lossless compiler now preserves an input BIN chunk that no core buffer
references, including a present empty chunk. It keeps that exact chunk and
embeds all core buffers as data URIs on this path, retaining buffer indices and
avoiding new external geometry dependencies. Seven new tests cover byte
preservation, coexistence, resolver-free reads, repeat compilation and packaging
budget boundaries. These changes/tests are unbuilt/unrun; they do not establish
GPU layout optimization or the meaning of arbitrary opaque URI references.

The managed `Editor.Asset` abstraction can override compilation and compiled
file paths. The publisher packages bytes at that path rather than requiring a
native resource header in managed code. A raw valid `.glb_c` primary output is
therefore a plausible managed route; it has not been demonstrated end to end.
Current ordinary registration/change callbacks create `NativeAsset`, and the
only other registry insertion path found handles mounted assets. A first-class
managed glTF asset needs lifecycle and registry integration, not merely an
attribute declaring its extension.

The next editor probe must establish stable IDs and aliases, source/dependency
watching, reimport, compilation state, output ownership/deletion, and distinct
ownership for same-stem `.gltf`/`.glb` sources. Verify actual packaged GLB magic
and dependencies. The publisher source now collects additional game files
after compilation for direct and referenced assets, preserving successful
additional-file-only assets and existing exclusions. Regression tests cover
generated dependencies, deduplication and compile failure/pending states.
This change/tests remain unbuilt/unrun; missing additional files can still be
skipped, and source/reference discovery still occurs before compilation. Probe
the native compiler's `SetExtension` naming/framing separately using a throwaway
fixture. These source observations do not prove a native blocker or choose a
custom extension, filename convention or final compiler architecture.

This tool inventories every model variant in the pinned [Khronos sample assets repository](https://github.com/KhronosGroup/glTF-Sample-Assets) and aggregates independently produced evidence. The pin is [90d7ede14c7e280af263824604b427a1ca02cb66](https://github.com/KhronosGroup/glTF-Sample-Assets/commit/90d7ede14c7e280af263824604b427a1ca02cb66), a dated upstream commit, not a claim about current HEAD. It is recorded in `coverage.json`. No model assets are included in this repository.

This is scaffolding, **not an s&box adapter, glTF conformance validator, reference evaluator, image comparison or performance benchmark**. It checks enough of a `.glb` to read its JSON chunk, but does not validate the rest of the GLB or glTF specification. Artifact hashes establish file integrity, not that a claimed adapter ran or that its assertions are correct. Consequently even a well-formed, hash-matching evidence record reports `adapter-integration-pending` and cannot pass. No engine capability is established by running this tool alone.

## Actual-editor corpus tests

[corpus-case-manifest.json](corpus-case-manifest.json) inventories **150 models
and all 337 variants** at the pinned commit. Independent authoring checks found
337 indexed assets and exactly the same 337 on disk, a clean Models/LICENSES
checkout, and 1890 distinct hashed source/dependency/provenance files. Rebuild
this metadata with `python3 engine/Tools/Gltf/scripts/build_corpus_manifest.py
--corpus Temp/gltf-reference/glTF-Sample-Assets`. The stdlib builder performs no
engine or rendering validation. It bounds independent accessor reads to 2 MiB
and position decoding to 100000 elements; unavailable/large oracles remain
explicit coverage holes.

There are **312 applicable rows and 25 exclusions for required extensions
outside the selected scope**. Optional outside-scope extensions remain
applicable core-fallback cases. A valid asset with no default scene stays
applicable and records selection-needed import behavior. Unsupported point/line,
camera, skin, morph or animation behavior is never an exclusion. The reviewed
upstream-defect ledger is currently empty; no sample has been repaired or
discarded. Each row keeps its source form, extension declarations, source and
dependency SHA-256s, README/metadata/legal references and upstream screenshot
reference. Full immutable material tables and node→mesh→primitive→material
bindings are shared by SHA-256 across identical source forms; all 10000
NodePerformance material contracts and their distinct values are retained.

[editor_corpus.csx](../../engine/Tools/Gltf/editor/editor_corpus.csx) is an **authored, unrun** existing-editor
test. It rechecks the pin, index/disk set and shared table hashes, mounts unique
private copies, and attempts actual `Model.Load` for each applicable original
source and a packaging-only `.glb_c` derivative. A compiler failure cannot hide
the separate original-source attempt. Original assets are unchanged. It
destroys each uniquely owned model explicitly, unmounts/removes its redirects,
and removes only that form's owned input copies before advancing; it retains no
array of loaded models. Main-thread yields occur between forms/assets. Human
play mode stops further loads, and no active scene, environment or CWD is changed.

Every row receives a stable per-run case receipt, including exclusions,
resource-admission pending cases and work not run after interruption. Default
per-asset admission is a conservative 256 MiB estimate and 64 MiB individual
file limit. Estimates are not hard memory caps. Larger valid assets remain in
scope with `resource_admission_pending`; root must choose a suitably guarded
isolated run. Optional `exchangeDirectory/gltf-corpus-request.json` has exactly
these fields (an empty `case_ids` selects all 337 rows):

```json
{"version":1,"request_id":"<32 lowercase hex>","manifest_sha256":"<manifest SHA-256>","case_ids":[],"max_estimated_bytes":268435456,"max_file_bytes":67108864,"keep_inputs":false}
```

Load, geometry, material, animation and render appearance are separate gates.
Independent stdlib-decoded transformed bounds are available for 224 corpus
rows, subject to their recorded limits. Declared triangle counts are diagnostic;
degenerate pruning or Draco strip topology can make native counts differ.
Native base-factor/backface parameter checks are partial material observations.
Source-versus-derived agreement is supplementary and cannot act as an
independent oracle. The upstream screenshots lack matched camera/light/exposure
contracts and are review context only. There are no heavy captures by default.
Full vertex/attribute/posed readback, PBR/texture/alpha behavior, native animation
and matched appearance oracles remain explicitly unverified where not attached;
**loading every row cannot produce `applicable_full_pass`**. The manifest's
feature matrix reports sample presence independently from acceptance and lists
requirements needing dedicated feature tests.

## Corpus and licenses

Obtain the upstream repository and its complete Git LFS content through the project's approved network workflow, then check out the exact manifest commit. The tool does not download or alter it. A missing repository, wrong commit, local changes under `Models` or `LICENSES`, missing indexed assets, LFS pointer instead of an indexed asset or referenced buffer/image, unsafe paths and malformed asset JSON all prevent either pass. Do not run against a partial or modified corpus. Git status does not verify ignored files or changes outside these paths; CI should use a fresh, trusted checkout.

Preserve each model's upstream README, metadata, attribution and applicable license, including any relevant files under `LICENSES`, when obtaining, retaining, distributing or publishing results using its assets. License terms vary by model; repository code licensing is not a blanket license for model content. The report records available per-model provenance files; it does not interpret licenses. A model without either README or metadata is an inventory error. Review licenses and any asset defects individually.

## Run

This section describes the older offline `../../engine/Tools/Gltf/scripts/validate.py` evidence aggregator. Its
legacy any-unselected-extension filter is stricter than the latest required-only
exclusion policy in `corpus-case-manifest.json`; do not use its applicable-case
count as the new editor harness's scope. Its adapters also remain pending. The
337-row editor inventory above is authoritative for the newly requested corpus
tests, including optional outside-scope core fallback.

```sh
python3 -B engine/Tools/Gltf/scripts/validate.py --corpus /path/to/glTF-Sample-Assets > report.json
python3 -B engine/Tools/Gltf/scripts/validate.py --corpus /path/to/glTF-Sample-Assets --evidence /path/to/evidence.json > report.json
python3 -B engine/Tools/Gltf/scripts/validate.py --coverage-status > pending-integrations.json
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
