# UnlitTest source material integration, 2026-11-06

Owner: `/root/model_unlit`; shared producer/install owner: `/root/today_integration`.

The pinned sample requires `KHR_materials_unlit` and contains two untextured opaque
materials, exact linear factors `(1, .217637640824031, 0, 1)` and
`(0, .217637640824031, 1, 1)`. All normals and faces must have uniform respective
colors. Actual upstream forms are glTF and binary glTF; each also has a portable
compiled representation. Derived fixtures are explicitly additional controls.

## Current evidence

Loaded producer for these receipts: Engine
`b5b66ab1-06f5-4aee-b816-92b6b197666e`, Gltf
`88f89cfc-81b4-4744-9888-ec746058a6de`, editor PID 155513/start 1006069.
These identities are historical evidence, not permission to reuse that process.

`today-unlit-baseline-01`: 57 assertion cases, 49 pass, 8 fail, zero incomplete.
The four public `Model.Load` cases reject required unlit. Four real stock
preparation cases reject unlit. Other core, verifier and installed plan checks
pass. Receipt SHA-256:
`9d167f9c484cebefa0d5b622d13adfa897f4d6eefd38803a46497b0cd669e47f`.

`today-unlit-source-01`: one orchestration passes, including four real model forms
and two derived normalized-U8 color forms through the installed source adapter
and explicit projected material path. Independent literal geometry and linear
color references, actual GPU readbacks, responsive lit control, two light setups,
wrong encoded factor negative, winding, depth and deleted-scene controls pass.
The default public path is unchanged by that proof.

`today-unlit-source-02`: base orchestration passes again. Feature orchestration
passes 14 source/compiled rows through single-sided backface controls, then fails
real double-sided backface rendering: requested no-cull yields an empty image.
The candidate declared `F_RENDER_BACKFACES` through common features but did not
connect it to any actual cull state. This is an actual shader defect.

`today-unlit-source-04` is terminal **PASS**: one orchestration, 34 actual
source/compiled feature measurements, two stock-vmat override/restore controls,
and 20 actual GPU pose/shear/mirror/instance/disposal measurements. Receipt:
`865eba71ca26bad5e76184d9c68ff2ca9cc9c36237083f4cd257c2e21c2791dc`.
GPU maximum position component error is 7.6293945e-6 inches and maximum normal
error is 8.940697e-8. Adversarial offcenter nearest/bilinear and outside
clamp/repeat samplers, legal contiguous TEXCOORD0..9 selecting9 with transform,
linear texture/factor/COLOR_0 products, alpha coverage, front/back culling, and
restoration all passed. Private widgets/scenes were destroyed; cleanup errors
are empty. The prior third-run fixture failure was an invalid TEXCOORD_9-only
fixture and was corrected to contiguous sets without changing producer code.

The model-local corrected shader explicitly sets
`RenderState(CullMode, F_RENDER_BACKFACES ? NONE : BACK)` after common pixel code.
Frozen third-run request adds texture decode/factor/COLOR_0, nearest sampler,
TEXCOORD_9 plus transform, stock-vmat override/restore and 20 actual GPU
pose/full-affine/mirror/independent-instance/disposal measurements.
The host guard refused the initial third-run admission at 4 GiB workload budget:
24,916,733,952 bytes actual + 4,294,967,296 workload = 29,211,701,248 bytes;
stale cleanup found no records or reclaim. No third run was dispatched then.

## Bounded shared integration proposal

Ship the exact terminal-tested model-local
`../../../../../engine/Tools/Gltf/direct-gpu/models/UnlitTest/gltf_unlit_five_uv.shader` as a stable core shader resource. Do not compile a
shader, graph or factor texture at runtime. Retain a distinct template for each
actual native alpha permutation; feature reflection is insufficient without the
pixel controls and native classification checks.

Use the existing `GltfGpuAsset.ConfigureProjectedSourceMaterials` path and
`GltfUnlitSourceMaterialAdapter` for selected unlit materials, with actual proven
capabilities only. Existing adapter already binds exact linear factor, alpha
cutoff, texture sampler attribute, canonical UV projection and row transforms,
cache leases and sidedness; it checks retained uniforms/native alpha flags.
Retain unmodified raw descriptor plans for ordinary vmat overrides and restore
the projected source route when overrides clear. No native Model/Mesh is needed.

Template contract:

- `F_ALPHA_TEST`: 1 only for MASK; `F_TRANSLUCENT`: 1 only for BLEND;
  `F_RENDER_BACKFACES`: 1 only for doubleSided.
- Linear FLOAT4 `g_vGltfBaseColor`; scalar `g_flGltfHasBaseColorTexture`;
  MASK cutoff `g_flGltfAlphaCutoff`.
- Base texture `g_tGltfBaseColorTexture`, sRGB RGB decode and linear alpha;
  material sampler attribute `GltfBaseColorSampler`.
- Scalar `g_flGltfBaseColorUvSlot` and FLOAT3 row uniforms
  `g_vGltfBaseColorUvRowU/V` implement canonical base-color UV transforms.
- Position/normal/color and projected UV0/UV1/UV4/UV5/UV6 runtime declarations.
  Input layout still comes from actual accessors and bufferViews.
- OPAQUE ignores product alpha; MASK keeps equality and uses arbitrary legal
  cutoff; BLEND uses straight alpha. Source factor*texture*linear COLOR_0.
- Depth test GREATER_EQUAL; depth writes enabled for opaque/mask, disabled blend;
  culling driven by actual material feature, no lighting term in RGB output.

The editor later exited normally during the integration owner's separate named
shader compile, about115 seconds after this worker's terminal04 receipt. No
main-window or process-close command was issued by this worker. No native tests
may reuse that process; a freshly verified producer/editor marker is required.

Public `GltfModelLoader.SupportedExtensions` must stay unchanged until source
realization and shader installation are coherent. Then add unlit and dispatch
managed resource creation to the proven projected route, preserving existing
lit/native/vmdl behavior. Rerun public source/compiled loading, actual source
captures, stock vmat overrides and disposal against the new installed marker.
A source helper, compile-only pass, optional-fallback load or candidate image
cannot substitute for that public gate.

Reference camera is authored for this validation: 192x192 orthographic target,
position `(200,0,0)` engine inches, yaw 180, orthographic height220, near1/far1000,
black background, exposure disabled and native tonemap disabled. The upstream
screenshot has a different presentation and is human comparison context.
Windows, editor parity and vmdl load/memory performance remain global gates.

## Installed terminal result

Integration staged the exact corrected source as the named core unlit shader and
added capability-based public selection, alpha-mode templates and scene-aware
reachable-material dispatch. Stage01459 Enginea7a46583/Gltf88f89cfc passed the
worker's changed public gates:12/12 cases,4 actual model source/compiled forms,
34 actual public feature measurements, zero failures/incomplete/cleanup errors.
Receipt SHA256
`5569df0a667cb9b8201bf9bc08ff44a9e8a35351f81e0be0f84cff72d1066902`.
Current bounds and limitations are in `result.json`; the four current bright
captures share SHA256
`16722bdf295200076ea526531d3cf71ef0d5fe934513644596ad465e294552fc`.

Mixed reachable lit/unlit scenes deliberately reject in this increment. Six
actual dispatch tests verify unused-unlit lit-only scenes, explicit and no-default
selection, empty scenes, mixed rejection and recovery. Mixed transformed-UV and
variant packet realization remains pending; do not promote these negative tests
into full extension conformance. The public source adapter has been accepted
independently of the old legacy adapter, whose historical direct unlit preparation
failures have not been rewritten.

## Mixed packet implementation accepted on e12

The e12follow-up supersedes the previous increment's blanket mixed rejection for
legacy-supported opaque lit inputs. Named unlit source packets coexist with
existing legacy-lit shader/materials, per-material baked input buffers and raw
unmodified override plans. The worker's mixed02orchestration passed1/1 with8actual
captures (2publicdefault and6explicit-reader variant poses),2raw stock override/
restores,4unchanged lit baselines,0RGBcomparisonerror and0UVerror. Independent B
bytes/variant and deletion controls pass. Receipt SHA256
`267dcdfc54e223f807aad3db8e2477b18a43baca0eaed71500be6e085067d26b`.
Two changed active scene assertions additionally pass2/2. Public material-variant
support is not inferred from explicit-reader poses. Each current/follow-up receipt
and capture retains its exact producer/hash in the stable `result.json`.
