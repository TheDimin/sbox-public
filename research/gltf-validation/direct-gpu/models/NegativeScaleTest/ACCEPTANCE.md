# NegativeScaleTest acceptance

Owned model folder only; shared producer ownership remains `/root/gpu_fox`; only
`/root/gpu_validation` builds, stages, drives editors or compiles shaders.

The pinned Khronos source commit is `90d7ede14c7e280af263824604b427a1ca02cb66`.
Both actual upstream forms exist: `glTF/NegativeScaleTest.gltf` with one external
BIN and two PNGs, and `glTF-Binary/NegativeScaleTest.glb`. Each must execute as
source and portable compiled GLB (`.glb_c` packaging). Attribution: Ed Mackey,
Analytical Graphics, Inc., 2023, CC-BY-4.0; upstream LICENSE.md remains authoritative.
The oracle records complete input hashes and every raw accessor value.

The actual scene has 14 nodes, 11 mesh-node draws, 8 meshes, 22 accessors, 3958
instanced position elements and 7724 triangles. Global negative determinants
occur at nodes 4, 6, 8 and 11. Parent reflections cancel the local reflections of
nodes 9 and 12; local scale signs alone are an invalid winding rule. Materials
0–2 are single-sided; sphere materials 3–5 are authored double-sided. Never
force double-sided rendering to conceal incorrect winding.

`../../../../../engine/Tools/Gltf/direct-gpu/models/NegativeScaleTest/test_source_oracle.py` was written before the authoring implementation and
passed 8/8 controls. The stdlib raw decoder and independent column-vector TRS
hierarchy create `reference.json`; no production evaluator, GPU code or generated
capture supplies expected values. The independently measured engine bounds are
(-19.6850381968, -203.215512704, -175.336214501) to
(19.6850381968, 203.215512704, 175.336214501) inches. The image camera fits every
independent transformed oracle vertex; it never trusts producer bounds.

The additional `affine-tangent-fixture.gltf` is explicitly synthetic, not an
upstream variant. Individually valid local TRS nodes produce full-affine shear
through nonuniform parent scale and child rotation. One authored tangent and
oblique normal distinguish forward-normal transform from inverse transpose.
The scale animation keys are negative, rank-two exact zero, rank-zero exact zero,
and negative recovery. Both source and compiled forms execute. There is no TRS
decomposition of the resulting world matrix.

## Frozen callable suites

- `NegativeScaleCoreTests.RunAll(checkoutRoot)`: 38 CPU/data cases, actual
  descriptors/raw values, world hierarchy, source positions, authored culling,
  owned upload and compiled geometry/image self-containment, plus synthetic keys.
- `NegativeScalePlanTests.RunAll(checkoutRoot)`: 22 staging-plan groups, complete
  raw GPU arena components, explicit runtime output fields, full-affine palette
  positions and global winding. Includes tiny invertible negative placement
  `diag(-1e-20,1e-20,1e-20)` to detect float determinant underflow.
- `NegativeScaleCheckerTests.RunAll(checkoutRoot)`: 11 manufactured observation
  controls. Position, normal, tangent W, actual bound index winding, source map
  and nonfinite singular fallback corruption must be rejected. These are checker
  sensitivity cases, not actual GPU evidence.
- `NegativeScaleGpuProbe.Run(checkoutRoot,world,compute,waitForActualFrames,resolver)`:
  60 actual GPU readback measurements. Four actual forms each exercise identity,
  affine, reflected affine, tiny uniform mirrored placement, rank-two placement zero, rank-zero placement zero,
  recovery and an unchanged independent instance B. Two synthetic forms each
  exercise four source animation keys under identity and mirrored placement, plus
  initial/recovered nonsingular keys under tiny uniform mirrored and tiny
  anisotropic positive/negative placements. Those cases require defined normals
  and tangent W consistently across source and placement reflection stages.
  Dispose B while A remains valid, and require B draw callbacks stop.
- `NegativeScaleImageProbe.Run(checkoutRoot,scene,camera,compute,waitForActualFrames,capture)`:
  12 image cases, four actual forms × three placements. Each requires seven fresh
  captures: empty, actual, deleted-empty, independent literal source-material
  reference, deleted-empty, incorrect single-sided winding, deleted-empty.
  Uses the source material map borrowed from production asset metadata APIs.

All callable types are under `Sandbox.Gltf.DirectGpu.NegativeScaleValidation`.
The staged production backend is internal and must be consumed through the
coordinator's actual `Sandbox.Test.Unit` friend helper. Do not compile backend
producer source into a replacement helper. Image calls require the agreed
`MaterialSlotSourceIndices`/`GetMaterials()` asset metadata extraction.

## Numeric and image gates

Positions use full affine source hierarchy, `(x,y,z) → (z,x,y)` and meters to
inches exactly once, followed by full-affine placement exactly once. GPU maximum
position error is at most .001 inch; defined inverse-transpose normals and
transformed orthogonalized authored tangent errors are at most .00002 component.
Authored tangent W must match source W times global determinant sign times
placement determinant sign exactly. Actual indices must have origin
`gpu-readback` and match determinant-dependent expected topology. Output is read
through declared field offsets/stride/component type, never an assumed struct.

An exact singular transform has no unique inverse-transpose normal or tangent
handedness oracle. Such vertices must still have finite, correct collapsed
positions and a finite, unit, orthogonal normal/tangent fallback with signed W.
Their undefined directions are counted separately and never admitted as defined
normal successes. Recovery must restore the complete nonsingular oracle. Tiny
nonzero scales remain invertible and must not be classified as singular merely
because a float determinant underflows.

Images require nonempty actual/reference foreground (>128 pixels), IoU >= .99,
mean RGB error <= .003, pixels differing by >.03 RGB <= max(12, 1.5% of reference
foreground), and >=30 changed pixels when only single-sided winding is reversed.
Maximum RGB error is reported separately. These predeclared gates remain strict;
failed captures are retained. They prove geometry, source material binding,
normals and front-face behavior on shared source material resources. Independent
PBR/texture adapter fidelity, wall-clock playback, performance, Windows and public
Model.Load integration remain separate gates. No native Model/Mesh is created by
these helpers. Existing vmdl behavior is untouched.

## Current execution state

Only the independent Python oracle controls have executed (8/8 pass). C# suites,
actual GPU measurements and native image comparisons are unexecuted until the
coordinator records the loaded producer identity and frozen request. There are
71 prepared C# CPU/staging/checker groups, 60 GPU measurements and 12 image cases;
none of those prepared rows are reported as executed passes.

Source audit found float `GetDeterminant()` underflow erased reflection signs in
`GltfGpuRig.ReverseWinding`. Canonical source now independently normalizes the
linear matrices in double precision, compares signs without multiplying their
magnitudes, and preserves exact zeros. The staging tests call that actual
implementation. Loaded-producer regression execution remains a coordinator gate.

Follow-up review found the shader's single global-max normalization remains
insufficient for strongly anisotropic but invertible transforms. For example,
`diag(-1e-30,1e-30,1)` has negative double determinant about -1e-60, but its float
shader determinant is zero. An oblique normal can flip and tangent W becomes +1
instead of -1. The initial/recovered synthetic cases under tiny anisotropic
positive and negative placement explicitly distinguish sign composition. Global
or per-row scaling alone may not cure near-parallel rows produced by a sheared
source world and anisotropic placement. Shared owner has this reproducer; the
original shader is still a pending GPU failure, never an accepted normal pass.

Canonical source review otherwise confirms the intended order: source world
matrix times the positive cyclic meter-to-inch basis in the palette, one full
affine placement in the shader, inverse transpose of the combined linear matrix
for normals, forward tangent transform and orthogonalization, and source tangent
W times the combined determinant sign. Exact zero scale states reset winding;
recovery reinstates the source-dependent expected bound indices.
