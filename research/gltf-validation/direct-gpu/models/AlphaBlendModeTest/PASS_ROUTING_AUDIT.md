# Bounded native alpha routing audit

Status: actual native callback baseline executed on c5c0; receipt
`ce272234dc5e25f16d77b38d2cade0677994131f8da9bc0e04ec8421ea64adeb`.
One of two MSTests passed. Native callback eight rows passed; material routing
three rows passed and five failed. All 24 PNG controls are retained. Reverse-X
sort callback order was observed, but both positive images were empty: visible
reverse-camera acceptance remains unproven. Parent-child observation showed the
child stayed enabled, untinted and not excluded while its parent changed.

Fox applied the root-reviewed classification/per-draw compute/no-op/empty patch
to backend SHA `86ac3f13464ac0e323eb12f32c64ede56618630957a416c4ced5a741a9461fd7`.
It is next-stage source, not installed acceptance. Transparent sorting was removed
from that patch and remains unresolved, both within and across models. The four
MSTest followup request adds actual mixed animated GPU rows, no-op overrides and
empty readiness. This worker did not edit shared Engine source.

## Authoritative existing behavior

- `SceneCustomObject.RenderInternal` invokes the callback in `Graphics.Scope`
  created from the actual native `ManagedRenderSetup_t`.
- `Graphics.LayerType` exposes Opaque, DepthPrepass, Translucent and Shadow.
  `Graphics.NativeViewIdentity` exposes camera/view IDs where available;
  `Graphics.ModeFor(material)` returns the actual native layer-selected material
  mode identity. `Graphics.DrawShaderMode` is intentionally default in native
  views, so it cannot name their selected shader mode.
- `Sandbox.SceneRenderer/Native/StandardPipeline.cs` creates native Translucent
  Forward with `NeedsFullSort`, `SceneLayerType.Translucent`, required
  `IsTranslucent`, and excluded `ExcludeGameLayer`. This is native pipeline setup,
  not evidence from the alternative managed scene renderer.
- `SceneLayerType.Translucent` documents back-to-front object ordering. The
  managed renderer's parity implementation uses distance to object AABB, but
  that implementation is corroborating source only, not the native oracle.
- The tested c5c0 GltfGpuInstance creates one opaque-only SceneCustomObject and draws
  every primitive in every callback; global SubmittedRevision guards all compute.
  This cannot route mixed material passes correctly.
- `Material.Flags.IsTranslucent` and `.IsAlphaTest` query actual native material
  attributes, as the engine's own RenderMesh classification does. F_TRANSLUCENT
  or source JSON alone is insufficient for an explicit override/candidate.

The repo's SceneRenderer README links `docs/managed/scene-renderer.md`, which is
absent in this checkout. This audit therefore uses the actual checked-in code and
requires native runtime evidence instead of assuming missing documentation.

## Actual native probe

`AlphaNativePassProbe.Run(scene,camera,opaqueMaterial,blendMaterial,wait,capture)`
contains eight cases. Coordinator supplies a verified native private scene,
actual frame waits, captures and borrowed opaque/translucent materials. The blend
material must report actual native `translucent != 0`; both controls must visibly
render the literal white quad under the supplied setup. No Model/Mesh is created.
The helper draws literal vertices via the existing Graphics.Draw path.

Cases: opaque-only callbacks; translucent-only callbacks; four sorted-object
cases (two camera sides × two creation orders); child state inheritance
observation; both-flags callback routing. Captures include fresh empty and deleted
empty controls, bounded callback records, layer/mode/native view/frame identity,
actual camera position and distance to each object's independently assigned AABB.
Application.FrameCount is ulong. Any callback draw exception is retained.

The two separated native objects have equal-size finite AABBs centered at engine
X=-20 and X=20; cameras X=±200 must reverse the back-to-front callback order
independently of creation order. The child case records actual flags, enable,
tint and callback behavior without declaring an inheritance policy in advance.
The both-flags case must observe both native opaque and translucent callbacks.
These are pass/sort observations, not alpha-pixel correctness or glTF production
integration. Shader/material source alpha still needs the independent pixel gates.

## Minimal implementation contingent on passing probe

Keep one stable primary SceneCustomObject and all existing component ownership.
At realization and explicit material/override selection, classify each effective
material using actual native `Flags.IsTranslucent`. Union its classification into
primary `IsOpaque` / `IsTranslucent`. OPAQUE and MASK both use opaque classification;
MASK stays on its actual alpha-test material for depth/shadow draws.

In the callback, classify/filter by **actual layer** before draw submission:

- Opaque and DepthPrepass: only nontranslucent draws.
- Translucent: only translucent draws, stably sorted far-to-near for this actual
  camera/view using per-draw transformed bounds.
- Shadow and ancillary effect/overlay layers: explicitly audited policy, not an
  accidental else branch. MASK must retain its cutoff path. BLEND shadows are
  not proof of alpha-over and should not silently become opaque silhouettes.

Cache per-draw bounds while ApplyPose already evaluates each draw's palette and
morph state; do not decode attributes or read GPU buffers to sort each frame.
The stable tie key should be original draw ordinal. Same scene is rendered from
multiple views: do not keep one mutable global ordering that assumes a main camera.

Add `Draw.ComputedRevision`, initially -1. Compute/barriers execute when that
specific draw's computed revision differs from pose revision. Mark computation
successful after its write-to-read barrier; do not reuse global SubmittedRevision.
Keep `Draw.SubmittedRevision` as successful DrawIndexed evidence only. Aggregate
instance SubmittedRevision becomes current only after all required draws have
actually submitted at the current revision. Readback must preserve current strict
meaning when one class/pass was culled, missing or failed. Compute success alone
cannot become draw success. A failed material bind must not hide missing rows.

The instance remains one external native sort unit. Internal layer sorting solves
controlled nonintersecting layers within this model; it does not claim arbitrary
intersecting transparency or perfect interleaving with other transparent models.
Add a simple two-model interleaving test; if that scoped case fails, revisit
per-primitive native objects with explicit renderer-state propagation.

## Override classification and ownership constraints

Refresh classification after whole override, slot override, clear/reapply and
material variant selection; use the **effective** material, not the original
source alphaMode. Do this outside rendering before native view collection. Preserve
current whole-versus-slot precedence and invalid-material transaction behavior.
A diagnostic OPAQUE material on an authored BLEND primitive must render opaque;
a native BLEND override on an authored OPAQUE primitive must enter translucent.
Restoring defaults must restore original classification. Two instances sharing an
asset must not change each other's flags or override choices.

Material.Flags queries live native values. Material.SetFeature calls native
ReloadStaticCombos, but the current Material API has no observed per-material
change event/version consumed by custom glTF instances; in-place feature changes
and hot reload without a new override command require explicit additional update
handling. Do not silently call those supported merely because explicit assignment
is correct. Actual material schema/flag observations must be recorded for the
candidate and each override used in the tests.

Per-primitive child objects are not the initial implementation because normal
ModelRenderer/Renderer state is primary-owned: direct Tint setter, Attributes
reference, tags, Component/picking, RenderOptions, shadow transitions, rendering
enable, render layer, callback ExecuteBefore/After, manual RenderSceneObject and
replacement rollback all need a deliberate multi-object contract. AddChild's
native flags 0x02 document transform inheritance, not general state propagation.
The public primary must not become an unused placeholder without these semantics.

## Exact production scope after evidence

First requested file: `engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuBackend.cs`
(Fox owner). Add per-draw classification/bounds/computed revision, primary flag
union, actual-pass filtering, stable view-local transparent draw order, and
explicit override refresh. Reuse existing GltfGpuRig.Bounds; no new disconnected
policy type, no native Model/Mesh, no generated interop edit. If evidence requires
changes beyond this file, request a fresh exact handoff before editing.

Keep current fee5 installation and shared producer freezes untouched. A bounded
unapplied patch is prepared only after the actual eight-case callback receipt.

## Self-contained execution selection

`native-execution-request.json` packages the two existing probes as **two MSTest
methods**, preserving their 16 named nested results in `Measurements`. Configure
`AlphaNativePassExecutionTests` with checkout, fresh evidence directory and exact
deployed compute shader path. It creates only its own private native scene, visible
preview widget, point light and material copies. The stock white template stays
borrowed. F_TRANSLUCENT 0/1 copies must report actual native flags; feature reload
refusal is a real setup failure, not a reason to fabricate a blend material.
The private preview and owned copies are retired on the normal success/failure
path; no user scene selection or editor process is changed. Actual counts must
remain two executed test methods plus 16 nested observations, never 16 MSTests.

## Concrete external sort-unit limitation

`AlphaCrossModelOracle` supplies two derived actual glTF inputs with three
parallel, nonintersecting planes. Model A contains far red and near green; model B
contains middle blue. Each has alpha .5. Their RGB comes from unit emission with
zero base RGB, in a black unlit environment, to avoid an assumed PBR radiance.
On black the independent far-red / middle-blue / near-green result is
`(.125,.5,.25)`. A whole-model A-before-B ordering instead produces
`(.125,.25,.5)`; B-before-A produces `(.25,.5,.125)`. Neither external unit ordering
can match the pixel oracle. This is not an assertion that glTF mandates perfect
sorting for arbitrary geometry: it is a concrete simple case that any expanded
cross-model support claim must disclose or solve. Native callback evidence and
first bounded single-primary implementation remain prerequisites as agreed.

## MASK depth shader review boundary

The provided `ShaderTemplate` declares Forward and Depth modes and shares the
same MainPs/material output body. The owned MASK delta performs exact cutoff
clipping before `ShadingModelStandard::Shade`, so the written source also places
that clip before `DepthNormals::Output`. This is source-path evidence only.
`common/shadingmodel.hlsl` subsequently runs `AdjustAlphaToCoverage`, including
an epsilon clip that would wrongly discard alpha zero at cutoff zero if the
original opacity were passed through. The delta therefore sets surviving opacity
to one. The helper warns that derivatives after discard can be undefined; its
native `AdjustOpacityForAlphaToCoverage` implementation is not checked in here.
Actual native conservative depth variants and constant-full coverage must be
verified with the equality/zero-cutoff/depth fixtures. Generated text or a forward
color-only image cannot close this gate. No global common-shader edit is implied.

## Metric evidence still required

The checked-in managed `MeshRenderFeature.TranslucentSortDistance` documents the
native convention as nearest-AABB distance, truncated to hundredths, with stable
world-index tie order. Our actual separated-axis cases cannot distinguish that
metric from signed view depth or center distance. A future native off-axis probe
will place parallel planes at (20,160,0) and (0,0,0), half-size30, viewed from
(200,0,0) with orthographic height400. Both are in view; nearest-AABB radial order
and signed view-depth order disagree. The result must be measured before adopting
that metric as a native-matching policy. It cannot itself prove general transparent
pixel correctness when geometric depth ranges overlap.
