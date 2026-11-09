# Authored glTF skeleton and API representation

Source audit: 2026-11-05. This records the accepted representation direction and
available-interface limits. It does not authorize a sampling approximation or
claim that the initial render-palette implementation provides skeleton parity.

## Current adapter and its limits

`GltfModelRig` creates `gltf_skin_{skin}_joint_{joint}` bones separately for each
skin, with no parents and identity rest transforms. A source joint shared by two
skins therefore becomes two native bones. The evaluated matrices written into
native render bones are `inverseBind * jointWorld`, rather than joint transforms.
These are useful palette proxies, not the authored skeleton.

The distinction affects established APIs:

| Consumer | Current source path | Missing semantics |
|---|---|---|
| `Model.Bones`, names, parents and rest transforms | `Model.Skeleton.cs` delegates to native model bones | Source names, ancestry, shared joint identity and authored rest transforms |
| Final bone world transforms | `GltfModelRig.ApplyPose` writes palette transforms after native finish | Joint world frames must exclude inverse binds |
| Bone objects and parent-space queries | `SkinnedModelRenderer` builds `Model.Bones` and reads native parent-space bones | Exact source animation must populate that hierarchy |
| Pre-physics animation queries | `SceneModel.GetWorldSpaceAnimationTransform` reads native animation state | Current exact glTF evaluation is not published into that state |
| Procedural bones and physics overrides | Native parent/physics overrides run before the final glTF palette write | Overrides must compose with the exact authored animation instead of being overwritten |
| Attachments and hitboxes | Native definitions bind to bone names | Definitions must target the source skeleton and follow its final joint frames |
| Physics and ragdolls | `PhysicsBodyBuilder.BoneName` and `BindPose`; renderer physics follows skeleton parents | Correct source names, rest poses, ancestry and animation/physics ordering |
| Animation graphs | Native graph references and parameters already exist | Exact managed glTF clips are absent from native animation groups; publishing names does not connect graph playback |

Relevant implementation: [rig](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfModelRig.cs),
[model bones](../../engine/Sandbox.Engine/Resources/Model/Model.Skeleton.cs),
[bone objects](../../engine/Sandbox.Engine/Scene/Components/Render/SkinnedModelRenderer.Bones.cs),
[procedural update](../../engine/Sandbox.Engine/Scene/Components/Render/SkinnedModelRenderer.cs),
[attachments](../../engine/Sandbox.Engine/Resources/Model/Model.Attachments.cs), and
[physics bones](../../engine/Sandbox.Engine/Scene/Components/Render/SkinnedModelRenderer.PhysicsBones.cs).

## Smallest correct representation

Maintain one master skeleton identified by source node index. Include source
joints and the non-joint ancestors necessary to preserve their transforms. A
shared node has one public bone, regardless of how many skins reference it.
Preserve unique source names and parent links. Missing or ambiguous names need
deterministic native identifiers while retaining source names and node IDs;
string lookup alone cannot distinguish duplicate source names.

Keep each skin's ordered joint references and inverse-bind matrices separately.
In row-vector notation, let `W_node(t)` be the final joint world matrix before
model placement, and `I_skin,j` its skin-specific inverse bind. The render palette
is `I_skin,j * W_node(t) * placement`. The joint APIs expose `W_node(t) * placement`.
An inverse bind is not a replacement for the source rest transform, and different
skins must not force different public identities for the same node.

The managed renderer's per-mesh affine palette can carry these independent
inverse binds. Native render-bone mappings must still be read back and matched;
native ordering and mesh packing must not be assumed.

Animation must publish the exact source pose before the consumers that need it.
Physics and procedural overrides then produce final source joint transforms,
from which skin palettes and attachments are derived. The exact native update
boundary needs focused runtime tests; changing callback order without those
tests is insufficient.

A native authored skeleton for the unit-scale TRS subset is useful progress.
Scaled and sheared cases remain positive core acceptance requirements. A broad
managed facade overriding every bone, attachment and physics API would be much
larger and has not been selected.

## Proven limits of the exposed interfaces

These are limits of the available ABI, not proof that the closed native engine
cannot implement the capabilities.

* [The bone builder](../../engine/Sandbox.Engine/Resources/Model/Builder/ModelBuilder.Skeleton.cs)
  passes name, parent name, position, rotation and bounds to `AddRunTimeBone`.
  It has no scale or full rest-matrix argument. Runtime parent-space setters
  accept `Transform`, but do not change the model's authored bind-pose data.
* `RenderTools.GetModelMeshSkinningBones` reads native inverse binds and master
  mappings. No corresponding per-mesh inverse-bind writer was found in the
  exposed managed interop. One derived inverse bind per master joint cannot
  represent arbitrary distinct skins sharing that joint.
* [Native animation construction](../../engine/Sandbox.Engine/Resources/Model/Builder/ModelBuilder.Animation.cs)
  supplies a uniform frame rate and arrays of frame transforms. It exposes no
  arbitrary-key curve source or exact managed sampler callback for native
  animation graphs. Unapproved resampling remains excluded.
* Existing bone APIs return `Transform`. Inherited shear cannot be represented
  exactly by a position, quaternion and scale, even though the managed GPU path
  can carry a full `Matrix3x4`.
* The current FLOAT weight decoder selection applies to explicit managed
  skinning dispatch. No per-mesh compute-material/weight-format selector or
  external vertex-cache injection was found for the native/default renderer.
  Its parity remains a separate requirement.

Potential narrow native additions are a complete rest-transform bone builder,
per-mesh inverse binds or final palette input, and an exact external animation
source usable by native graphs. These require a decision and native work; none
is silently substituted by proxy semantics.

## Positive acceptance fixtures

[GltfAuthoredSkeletonTests](../../engine/Tests/Sandbox.Test.Integration/Resources/GltfAuthoredSkeletonTests.cs)
contains five cases, each for source `.gltf` and compiled `.glb_c`:

1. Source joint names, ancestry and rest transforms through `Model` APIs.
2. Bone objects and source animation transforms through real components and
   normal `Scene.EditorTick` updates.
3. One source-joint identity across two skins with different inverse binds,
   preserving their distinct runtime palettes without dictating mesh packing.
4. Non-unit authored rest scales through model and runtime bone APIs.
5. Inherited shear in the final runtime palette, checked against independent
   analytic point transforms.

These are positive expectations, not tests that pass when loading throws. They
are expected to remain red against the proxy skeleton until the representation
is implemented. Their runtime results must be reported separately from the
initial animation motion captures. Palette state readback is not GPU appearance
proof. Attachments, graph-driven clips, physics/ragdolls, procedural composition,
and duplicate/missing joint-name policies need their own later positive fixtures.
