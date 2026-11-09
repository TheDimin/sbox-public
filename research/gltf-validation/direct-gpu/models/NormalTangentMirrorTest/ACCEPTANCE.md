# NormalTangentMirrorTest acceptance

Pinned source: Khronos glTF-Sample-Assets commit
`90d7ede14c7e280af263824604b427a1ca02cb66`. Actual upstream variants are glTF
and glTF-Binary. Each also has a derived portable compiled-GLB test form.
Artist Ed Mackey, Analytical Graphics, Inc., 2018; CC-BY-4.0. Numeric reference
data derives from that model. Preserve upstream `LICENSE.md` and `metadata.json`.

One identity node, one primitive, 2,770 vertices, 15,720 uint16 indices, authored
FLOAT normal/tangent/UV0 streams. Tangent W is +1 on 2,690 vertices and -1 on
80. There are no source extensions, skins, animations, transformed UVs, or
missing-tangent primitives. Tangent generation is **inapplicable to this actual
asset**, not implemented/proven by this model. NormalTangentTest remains the
corpus model for generation. Derived scale/UV-transform controls are explicitly
named fixtures, not invented upstream variants.

The actual material is double-sided and uses base color, normal, occlusion and
metallic-roughness maps. Normal uses linear RGB, UV0, default scale 1. The
current stock adapter rejects normal/occlusion; diagnostic override rendering
must not become source-material acceptance.

## Independent oracle and callable tests

`../../../../../engine/Tools/Gltf/direct-gpu/models/NormalTangentMirrorTest/author_reference.py` reads each raw source accessor by bufferView offset,
accessor offset, stride and component type, and confirms both variants match.
It imports no production parser or evaluator. `reference.json` pins all six
input/dependency hashes. The source exporter has maximum normal squared-length
error `8.879864006061666e-05` and normal/tangent dot `1.1335942421197842e-05`;
recorded input imprecision is normalized/orthogonalized in the independent
double cofactor oracle, not hidden by larger GPU tolerances.

- `NormalTangentMirrorTests.RunAll(root)`: 24 CPU/data/staging cases, including
  all raw values, W populations, source descriptors, role/UV selection,
  portable dependencies and source/compiled derived scale/UV1 transform.
- `NormalTangentMirrorCheckerTests.RunAll(root)`: 10 synthetic checker controls.
  They reject lost W, flipped T, wrong N, flipped UV origin, wrong winding,
  wrong mapping, NaN and wrong units. Synthetic bytes are not GPU evidence.
- `NormalTangentMirrorGpuProbe.Run(root, world, compute, frameWait, resolver)`:
  16 actual GPU measurements: 4 forms × identity/full affine/reflected affine/
  recovered identity. Validates every position, normal, tangent, bitangent,
  UV and bound GPU index. Source-map entries remain CPU provenance metadata.
  Position threshold .001 inch; normal/tangent .00002/component; bitangent
  .00004/component; exact UV and W. These bounds cover float32 arithmetic while
  mutation controls exceed them substantially. Actual frame execution pending.

Helper classes are internal, with public reflection entry methods. Coordinator
owns compilation and execution; no model worker editor/shader/build operations.

## Material implementation contribution

Published glTF 2.0.1 baseline:
[mesh frame, material normal and double-sided requirements](https://raw.githubusercontent.com/KhronosGroup/glTF/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc).
Use supplied tangent XYZ/W; B=cross(N,T)*W. Decode linear sampled RGB as
2RGB-1, multiply XY by normal scale, normalize, and reverse the complete mapped
normal for double-sided backface lighting. Alpha is ignored.

`../../../../../engine/Tools/Gltf/direct-gpu/models/NormalTangentMirrorTest/author_normal_graph.py` augments the adapter owner's provided Surface/Lit graph
using existing graph nodes only, outputting `surface_normal_candidate.shdrgrph`.
Normal role uses the shared `GltfNormalTexture`, `GltfNormalSampler`,
`GltfNormalUv1`, `GltfNormalUvRowU/V` family and new `GltfNormalScale` and
`GltfDoubleSided` factors. Default texture RGB is (.5,.5,1). The candidate
supports UV0/UV1 only; other selected sets must remain explicit pending cases.

The engine's `common/utils/normal.hlsl:TransformNormal` negates tangent-space Y,
while `vr_common_vs_code.fxc` constructs B=cross(N,T)*W. The graph compensates
with `[scale,-scale,1]` before the provided Result.Normal transform. This is an
adapter convention correction, not source PNG/channel alteration. Backfaces
multiply the entire tangent-space vector by -1 before transformation. Graph
generation through the actual registry/compiler, exact source review,
compilation and GPU execution remain **pending**. Adapter owner must bind
double-sided/cull behavior and all normal role resources before selecting it.

## Required actual GPU/image controls (pending)

1. Coordinator captures native empty-before, positive, deleted-empty-after at
   identical physical-camera/dimensions/background. Never native Model/Mesh.
2. Render the source model from front and back using the actual adapter; verify
   the geometry columns and normal-map columns show matched level-horizon
   reflections. A merely nonempty image is insufficient. Upstream screenshots
   illustrate intended behavior but are not camera/light-matched pixel oracles.
3. A literal planar fixture uses N=+Z, T=+X, W=±1 and a constant linear normal
   RGB=(.75,.25,1), scale=.5. For W=-1 its world mapped direction is
   `(0.25,0.25,1)/sqrt(1.125)`, and the back face is its negative. A numeric
   GPU sample/render target must compare to these literal values. Controls
   deliberately flip green, ignore W, omit XY scale, apply sRGB and reverse
   only N; each must fail the same acceptance function.
4. Independently baked UV/reference texture samples validate rotated/nonuniform
   normal UV transforms and UV1 selection; transform data alone is not sampling.
5. Final source/compiled captures and compiled dependency closure, public
   Model.Load route and lifecycle/performance parity remain separate gates.

Current executed evidence: four lightweight Python source/oracle tests pass.
All C# tests, actual GPU frames and rendered/material gates are unrun at initial
handoff. Existing global receipts do not cover these new files.
