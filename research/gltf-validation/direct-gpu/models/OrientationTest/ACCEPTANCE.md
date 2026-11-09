# OrientationTest direct GPU acceptance

Status: tests and independent oracle prepared before the direct GPU backend.
Actual GPU readback, images and `Model.Load` managed-backend integration are
unproven until corresponding coordinator receipts exist. Python oracle checks
are not engine or renderer coverage. Historical native-Model captures do not
pass this backend's gates.

This model tests authored node rotation and translation. Red/green/blue arrows
use quaternion rotations; cyan/magenta/yellow arrows use authored matrices.
Each must point at its same-color target whose transform is baked into geometry.
The upstream description and failure illustration are in the pinned model's
README. The screenshot supplies visual context; its undocumented light/exposure
does not establish a pixel oracle.

## Source and expected-value ownership

- Read the existing pinned `Temp/gltf-reference/glTF-Sample-Assets` checkout;
  do not fetch a moving branch. Its revision and per-file hashes belong in the
  coordinator receipt. Preserve upstream copyright and license attribution.
- Include all three on-disk core variants: `glTF`, `glTF-Embedded` and
  `glTF-Binary`, each as original source and an actual `GltfCompiler.Compile`
  generated GLB form. This yields six source/compiled forms.
- `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/source_oracle.py` independently decodes the sample and computes column-vector
  TRS/matrix world positions and inverse-transpose normals. It imports no
  Sandbox code, rendering output, production basis or pose helper. Its source
  hashes identify input containers; its geometry fingerprint normalizes only
  JSON numeric spelling and signed zero, with no numerical rounding.
- `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/test_source_oracle.py` tests that reference, including deliberately transposed
  quaternion/matrix negative controls. A correction to its fingerprint test
  does not represent a fix to the engine.
- `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/layout_fixture.py` produces a diagnostic storage mutation of this same model.
  POSITION/NORMAL share stride-32 views at accessor offsets 4/20, with poison
  padding and nonzero view offsets; U8/U16 indices retain authored values at
  accessor offset four. `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/test_layout_fixture.py` proves decoded equivalence and
  sensitivity to packed-layout assumptions. It is labeled synthetic, never a
  new Khronos variant. Generated copies remain under `Temp` with attribution.

## Actual loaded-producer tests

The coordinator compiles `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/OrientationCoreTests.cs` against the editor's actually
loaded glTF producer, then calls:

```csharp
Sandbox.Gltf.DirectGpu.Validation.OrientationCoreTests.RunAll(
    checkoutRoot, sourceOracleJsonPath);
Sandbox.Gltf.DirectGpu.Validation.OrientationCoreTests.RunFixture(
    generatedInterleavedSourcePath, interleavedOracleJsonPath);
```

Expected count: **30 upstream cases**, five assertion groups for each of the six
forms, plus **10 synthetic interleaved cases** (source and compiled):

1. Default scene, all 13 authored nodes/meshes, seven materials, material slot
   bindings and absence of animation/skinning are preserved.
2. `MaterializePrimitiveUpload` respects each accessor's component type, count,
   view-relative offset, view stride, owned view length and exact source bytes;
   both U8 and U16 indices are exercised. Shared source views retain one owned
   snapshot, and reported owned-byte accounting matches distinct storage.
3. `EvaluatePose` and `GetWorldTransform` preserve authored matrix/quaternion
   orientation and match independent world positions, including every vertex.
4. Static assembly matches independently calculated engine positions, normals,
   positive-determinant triangle winding and primitive/bounds presence.
5. Detached owned upload storage remains valid after document disposal.

These tests prove producer data/math only. They allocate no renderer resources
and must not be counted as direct GPU or final material parity.

## Own-buffer readback and rendered-image gates

Shared API is `../../CONTRACT.md`. The coordinator compiles model test files with
the frozen shared backend as a collectible test helper; no producer replacement
is compiled into the helper. `OrientationGpuChecks.ValidateCurrent` requires a
completed current-pose submission, successful callback, and real `Readback()`.
Every output uses its declared field offsets and runtime stride, plus
`SourceVertices` when the backend expands corners. It compares all positions,
normals, topology and winding to the independently generated reference. Report
the maximum errors even on a failed row; do not loosen tolerances after a failure
without investigating the cause. Initial numerical gates are 0.001 engine inch
per position component and 0.00001 per normal component, recorded explicitly.

`OrientationReferenceScene` draws literal expected triangles via the declared
diagnostic `Vertex` layout and `Graphics.Draw`. It never calls glTF decode,
production deformation, native `Model`, `Mesh` or model builders. It borrows the
same materials used by the actual path, so comparisons isolate geometry,
front-face order and normal orientation. This does not independently validate
stock material conversion. Record an explicit material resolver/override when
used; default-material acceptance remains a separate row.

`OrientationGpuProbe.RunSelected` supplies the complete model-local matrix using
the coordinator's private scene/camera, compiled compute material, actual-frame
wait and capture delegates. Its unfiltered inventory is 48 cases with seven
fresh captures per case. It targets the reviewed lazy backend metadata APIs and
remains uncompiled/unrun until that extraction is installed. It borrows the
asset's real source-material resources for the independent geometry reference.
The runner fits its camera from independent expected bounds and records source
camera signs; it does not guess asset units or change the production basis.

Run one baseline source form first, then the complete matrix:

- Six upstream source/compiled forms.
- Two explicit source camera directions, `(1,.65,1)` and `(-1,.65,1)`, mapped to
  engine directions `(1,1,.65)` and `(1,-1,.65)`. Source up `(0,1,0)` maps to
  engine up `(0,0,1)`. The first camera corresponds to the upstream preview's
  positive red/green/blue faces. The second intentionally shows the other side.
- Three full-affine instance placements: identity; source-X reflection
  (engine-Y scale -1); and a translated nonsingular shear. Placement goes through
  the instance's matrix API without TRS decomposition. Cameras target the placed
  model center while keeping their declared directions.

This is **36 actual GPU cases / 468 primitive readback rows**, followed by the
same two-camera/three-placement matrix for the synthetic interleaved fixture's
source and compiled forms (**12 additional cases / 156 rows**). None are excluded
merely because a backend feature fails. Each case independently requires:

1. Native default presentation: normal debug mode, normal postprocessing, an
   owned light and exact camera/target size, following the accepted controls.
   Require existing native scene rendering, without changing the global route.
2. A fresh magenta-initialized target becomes wholly black before creating the
   asset. Magenta retention, stale geometry and blank positive draws fail.
3. Create a glTF GPU asset/instance and capture visible geometry; require zero
   native Model/Mesh creation, actual C# owned buffers, runtime vertex declaration,
   explicit compute-to-vertex-read barrier and raw buffer draw submission.
4. Read back that current pose/placement and run `OrientationGpuChecks` against
   the independent JSON, identifying every node/mesh/primitive row.
5. Dispose the actual instance, process deletion normally and capture a fresh
   black target; its callback count must stay unchanged after disposal.
6. Render `OrientationReferenceScene` alone with borrowed matching materials.
   Compare actual/reference foreground and per-color regions (IoU > 0.97),
   recording RGB error separately. Require visible coverage above 64 pixels.
7. Delete that reference and require another fresh black target. Then run its
   `reverseWinding:true` reference as an intentional negative control; it must
   differ detectably (>30 pixels), followed by deletion and another black target.
8. Capture camera/viewport/clear state, source and helper hashes, shader identity,
   producer MVIDs, exact source/compiled form, placement, GPU readback errors,
   active callback counts, images/hashes and cleanup errors in the receipt.

The cyclic basis `(x,y,z) -> (z,x,y)` has determinant +1. Position units change
from metres to inches exactly once. A mismatched camera does not justify changing
this basis or silently flipping glTF handedness. Reflected placement requires
corresponding front-face correction; normals use inverse transpose.

## Remaining integration scope

The prototype's successful helper draw would establish only the declared GPU
buffer path and this model's cases. `Model.Load`, render components, cache/lifetime,
stock material completeness, `.glb_c` GPU optimization/performance and the rest
of the corpus still require their own positive acceptance tests. No Model/Mesh
placeholder or legacy rendering fallback can stand in for managed resource
backend integration.

`PUBLIC_API_INTEGRATION.md` inventories the audited public-resource/component
touchpoints. `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/OrientationPublicModelTests.cs` adds positive original `Model.Load`
and `ModelRenderer` acceptance, queued after the raw GPU triangle. Existing
native-backed glTF behavior must fail the explicit managed-backend assertions.
Compiled-only resolution of original `.gltf` identities and same-basename source
collisions remain explicit required mapping tests, pending the actual audited
AssetSystem/publication interface. A direct `.glb_c` test cannot pass them.
