# Supplemental normal material/image handoff

Initial CPU/GPU C# sources received only an explicit System.Numerics.Vector2
alias correction from submission SHA
`edb24209bc034fc68967ac429b9b54535b4c9e08934f4105bc56122a702752af`.
Coordinator confirmed no model C# was selected/executing before the alias
correction. No initial C# or GPU execution has been reported. The graph contribution has
been refrozen to implement Encoding owner's exact absent-normal contract.

`surface_normal_candidate.shdrgrph`: **81 provided nodes**, SHA256
`1cfa3b27287eda715af3d316fc3cc67efd187fcf106c2da2a473156e78ab566e`.
`GltfHasNormalTexture` selects literal (0,0,1) when zero, avoiding an RGBA8
128/255 flat-normal approximation. Set it exactly to one only for a present
normal role. `GltfNormalScale` multiplies decoded XY before normalization.
The graph preserves stored texture components and compensates the provided
TransformNormal legacy Y inversion locally. No global shader change.

The graph owns the **single** complete mapped-normal backface flip using
`GltfDoubleSided`; Alpha owner must remove its post-transform flip when this
branch joins, otherwise two flips cancel. Material `GeometricNormal` remains
authored +N in the provided Material.Init path. Actual backface shading remains
pending; neither culling alone nor graph arithmetic proves full lighting parity.
Encoding currently retains its source adapter double-sided/normal rejection
until the actual provided graph/GPU gates pass.

## New independent fixture

`asymmetric-normal-uv-fixture.gltf` contains eight literal quads: four distinct
RGB texels × W=±1. Normal scale .5, UV1 selected by KHR_texture_transform,
offset (0,1), scale (1,-1). UV1 is intentionally V-mirrored before that map;
UV0 deliberately chooses the same incorrect texel on every cell. Nearest,
clamp sampling avoids filter-boundary ambiguity.

`asymmetric-baked-normal-reference.gltf` contains the same positions with the
eight independently computed mapped normal directions baked into NORMAL and
an orthogonal tangent. Its material has **no normal texture**. The JSON oracle
records literal RGB8 and front/back world directions, computed without any
production parser, evaluator or image. At least one cell differs by the listed
maximum component distance for each independent deliberately wrong behavior:

| Wrong behavior | Direction difference |
|---|---:|
| Green inversion | .4697602072 |
| V inversion | .3808112138 |
| Both green and V inversion | .2075459605 |
| Ignored tangent W | .4697602072 |
| Omitted normal scale | .2461987662 |

Thus a V flip cannot hide a green flip. `../../../../../engine/Tools/Gltf/direct-gpu/models/NormalTangentMirrorTest/test_normal_fixture.py` verifies the
baked raw float values, exact role properties and these independent controls.

## Actual native image probes prepared, unrun

- `NormalTangentMirrorImageProbe.Run(root,scene,camera,compute,wait,capture)`:
  16 image cases (4 actual forms × front/back × identity/reflected placement).
  Raw-source reference triangles use Graphics.Draw and the same borrowed
  source material; wrong-W control must visibly fail. This isolates frame and
  material binding. It **cannot** prove a material equation shared by both.
- `NormalTangentMirrorFixtureProbe.Run(root,scene,camera,compute,wait,capture)`:
  4 material image cases (source/compiled × front/back). Actual textured normal
  quads are compared to the independent baked-normal/no-normal-texture model;
  wrong-V transform must visibly fail. This detects normal transfer, green,
  scale, handedness and UV selection against different expected source data.

Both use the coordinator's native normal-debug camera presentation at 384²,
empty-before, positive and deleted-empty controls. No native Model/Mesh. Every
actual instance must have a completed current GPU revision and bound GPU index
readback. Source material rejection is intentionally fatal; no fallback shader
or diagnostic material can make the material gate pass. Images are unexecuted,
and normal-debug behavior itself must be verified by the independent controls.

Enforced initial thresholds: silhouette IoU≥.995, RGB MAE≤.002, max RGB error≤.03,
visible reference intersection>1024 pixels; incorrect frame/UV control≥16
pixels exceeding .02 and MAE≥correct+.00005. These are explicit prepared
thresholds, not measurements or inferred passes. A failure requires triage of
the capture/format/material path and must not trigger arbitrary widening.

Final lit/horizon model appearance, material culling, Model.Load integration,
public lifecycle and performance remain separate pending gates.

Executed lightweight evidence: Python source4/4, graph9/9, fixture5/5 (**18/18**).
Prepared/unrun: C# CPU24 + checker10, actual GPU readback16, images20. The
coordinator alone compiles/installs/runs/captures; user play state is preserved.
