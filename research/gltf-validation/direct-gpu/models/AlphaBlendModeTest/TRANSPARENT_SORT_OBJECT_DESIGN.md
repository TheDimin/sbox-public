# Transparent draw objects: review design and acceptance inventory

Status: design only. No implementation, no public BLEND capability claim. The
current single-object sort fixes only per-view ordering *inside* one instance.
It cannot interleave another object's primitive between two draws of that instance.

## Evidence and boundary

On Engine `5a07f228-dff8-4d6b-b4ef-890b32a80299`, canonical compiled derived
fixtures rendered far red and near green in one managed instance and middle blue
in another, all alpha .5. Independent isolated foreground references produced
an alpha-over expectation `[.13371658,.50863075,.25868797]`; the actual was
`[.25854492,.50878906,.13366699]`, maximum channel error `.12502098`. Empty and
per-reference deletion captures were black; retired native scene objects were
invalid and draw counters stopped. Receipt `alpha-sorted-order-03/.../receipt-01.json`
SHA-256 `31a9f2daae75b3e153958d0fb7b7b6f8811e5bd625119d0defe84d9446b3c499`.
This is a simple nonintersecting counterexample, not a demand for a general
solution to intersecting transparency. Original sample lighting is not the oracle.

`SceneCustomObject` supplies one native scene object, one AABB and one callback;
there is no exposed per-draw native sort-key API. Managed `MeshRenderFeature`
also sorts a custom object as one AABB unit. Native custom-object callback
observations and this actual pixel failure support exposing separate units for
nonintersecting translucent draws. Native nearest-AABB distance is an imperfect
heuristic for overlapping or intersecting bounds; scoped acceptance must not be
expanded into general order-independent transparency.

## Candidate approach

Give each effective translucent draw a native custom object with that draw's
posed world AABB and a callback that submits only that draw. Keep the existing
primary scene object as the public `ModelRenderer.SceneObject` and as the opaque,
MASK, ancillary and manual-render owner. Do not present an inert placeholder:
its behavior and public identity are part of the renderer contract. Native scene
sorting can then interleave these draw objects with each other and with ordinary
transparent scene objects. Same-distance ties remain native order and are not a
claim of depth-correct interleaving. For equal or intersecting depth ranges,
document the renderer's bounded heuristic.

A variant or override transition can move a draw between opaque/MASK and BLEND.
Create/reclassify objects outside rendering before native view collection. Publish
a complete replacement vector transactionally only after material and object
creation succeeds; on failure destroy candidates and retain old effective draws,
flags, callbacks and state. Per-draw objects must not change document ownership,
per-instance pose/output/palette/index resources or native-model-free boundary.
Pose application calculates each bound and publishes it along with the same pose
revision; sorting and callback execution must remain view-local and serialized
with disposal. Callback errors remain visible and a partially submitted pose must
not report complete.

State cannot be copied once and assumed stable. Native `AddChild` is only observed
for transform inheritance; it does not propagate all state. A deliberate engine
contract is needed for changes to primary `RenderingEnabled`, `ColorTint`, flags,
render layers, `Tags`, `CallbackTarget`/picking, owner component, shadow settings,
`ExecuteBefore`/`ExecuteAfter`, and renderer options. Attributes can be mutated
through a retained `RenderAttributes` reference without a new `SceneObject`
property notification; either implement a versioned mutation signal and pre-view
synchronization or prove an equivalent shared native attribute binding. Do not
mirror from inside the draw callback after native culling or view classification.
Manual `Renderer.RenderSceneObject` / `Graphics.Render(primary, ...)` needs a
deliberate all-draw path with its transform, tint and material override applied
exactly once; it cannot silently omit child draws or duplicate opaque draws.
Preserve `SceneObject` primary identity and callback expectations through reload.

An alternative scene-wide transparent aggregator is insufficient as a complete
solution: its one native sort unit still cannot interleave correctly with ordinary
transparent scene objects, and it would need independent culling, layers, tags,
picking and lifetime management. A per-model list with a per-view sort is the
existing bounded interim, not a cross-instance solution.

## Minimal ownership before implementation

Agree exact ownership for `GltfGpuBackend`/instance lifecycle, `SceneObject` state
notification and render-attribute versioning, `ModelRenderer`/`Renderer` public
state and manual-render behavior, and integration/build/shaders. Keep these
coordinated; do not replace native renderer, construct a native Model/Mesh, or
fork a second glTF backend. No code change is approved by this document alone.

## Required independent controls

1. Canonical compiled, three-plane red/blue/green test above, plus reversed node
   and object creation orders, positive isolated reference colors, black empty
   and between-reference deletion frames, valid-handle/callback retirement, and
   independently computed alpha-over. Test both camera sides with positive
   references and a nonidentity instance placement; verify the observed ordering
   changes when the camera moves.
2. Replace the middle blue glTF plane with an ordinary engine transparent object
   using an independently calibrated straight-alpha material. Exercise both
   creation orders, view changes and bounds; an aggregator which only interleaves
   glTF objects must fail this control.
3. Mixed OPAQUE/MASK/BLEND: depth and MASK cutoff equality/zero and discarded
   fragments stay correct, actual flags/pass selection match effective materials,
   no duplicates, and shadow policy is explicitly checked. Test whole and slot
   overrides, clear and variants changing class both directions, two instances
   sharing one asset, and in-place feature mutation policy.
4. While the renderer is enabled and before view collection, mutate primary
   tint, retained attributes including texture/sampler, tags, all relevant render
   and layer flags, enable, transform and visibility; verify every actual draw,
   sorting participation and outputs. Test changes across simultaneous cameras.
   State propagation must not rely on one screenshot or a late callback copy.
5. Picking and hit callback return the same owner/component and correct geometry
   for each draw; renderer/scene object APIs retain their documented identity.
   Invoke manual `Graphics.Render` through the ordinary renderer with transform,
   color and stock material overrides; assert exactly-once per-draw submission
   and correct observable pixels.
6. Two instances with independent overrides and poses, one disposed while the
   other renders; repeated create/dispose; disposal during queued callbacks;
   whole-model disabled/reenabled, empty and node-hidden cases. Assert no calls
   after disposal, invalidated native objects and all leases released once.
7. Inject failure at every allocation/publication and staged reload step. Keep
   the old primary, children, flags, attributes and material generation live on
   rollback; no leaked native objects, stale callbacks, half-published revisions,
   lost overrides, or double disposal. Retest successful hot reload and public
   `Model.Load`/ordinary `ModelRenderer`, not only research helper construction.
8. Capture actual views under fixed camera/exposure with independent black/white
   transfer checks. Record draw-object count, callback/view/order, effective
   native flags, bounds, GPU output and fixture/source/compiled/shader identities.
   Compare images only to defined numeric/geometry oracles; user owns final
   appearance review against unknown reference lighting.
9. Measure scene object count, CPU pose/bounds update, view-list/sort cost and
   frame/GPU effect in a representative named workload with matching before/after
   camera and content. A two-draw microtiming or the number of objects is not a
   performance acceptance. Defer broad claims until tested on other GPU/OS targets.

## Default manual-override route: concrete follow-up

The prototype explicitly rejects a direct `Graphics.Render(primary, material:)`
when a projected instance has never prepared its raw override output. This is an
interim failure, not API parity or an acceptable final default. Eagerly adding a
second full raw output buffer to every projected instance avoids the failure but
adds unmeasured memory to unrelated opaque models.

A candidate default is one instance-owned output allocation per draw sized to the
maximum **actual descriptor-derived byte extent** among the raw route and all
reachable projected variant routes. Keep separately derived raw/projected input
and output descriptors and vertex declarations; initialize the small raw route
metadata/layout before publishing the instance, without allocating a second full
output. At submission bind the selected route's actual stride and declaration.
The one physical buffer has a single resource-state history and a last-computed
key containing pose revision plus route/packet identity. Switching any of those
invalidates the key and dispatches compute again, followed by the normal explicit
write-to-vertex barrier. Do not treat two revision fields as independently valid
when they point at one overwritten buffer. No source accessor stride, offset,
normalization or semantic packing may be inferred from a fixed `GltfVertex`.

Before adopting this: prove every reachable variant's required capacity and
integer overflow limits, including flat normals, generated tangents, morph/skin,
sparse and unusual accessor layouts; verify one buffer's byte-address/vertex
usage and transitions in actual GPU readback; preserve raw and projected layouts,
index winding and user overrides. Exercise alternating direct manual overrides,
variants, two views, pose changes and disposal/failure injection while another
instance shares the immutable asset. Measure real per-instance maximum memory,
compute switching and load time on a named model against the existing lazy path.
A narrower shader-specialized layout may be a deliberate `.meta` opt-in only;
the automatic/default route must continue accepting existing override APIs.
