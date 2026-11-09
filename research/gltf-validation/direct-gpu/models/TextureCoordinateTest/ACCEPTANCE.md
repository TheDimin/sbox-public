# TextureCoordinateTest acceptance

Owner: `/root/gpu_texture_coordinates`. Coordinator: `/root/gpu_validation`.
Shared UV plan: `/root/gpu_texture_transform`; shared backend: `/root/gpu_fox`.

Pinned corpus revision: `90d7ede14c7e280af263824604b427a1ca02cb66`.
Upstream actually supplies glTF, glTF-Embedded and glTF-Binary. This folder
adds a lossless compiled GLB for each source, plus a clearly identified derived
layout/selection fixture in source and compiled form. No upstream variant is invented.
Original files and source licenses stay intact. Model content is CC0-1.0;
metadata/license documents are CC-BY-4.0 as recorded in the upstream license.

The original model has five static four-vertex quads, all with authored FLOAT
POSITION/NORMAL streams and U8 indices. Four quads have FLOAT VEC2 TEXCOORD_0;
the back plane has no UV stream or texture. Every accessor offset is zero;
all UV strides are 8 and all UV normalization flags are false. View origins
are nonzero for attributes. Each textured material has only baseColorTexture,
effective texCoord 0, identity transform, sRGB color data, and the default repeat
sampler with unspecified filters. The four colored quadrants occupy distinct
source UV rectangles and image text explicitly distinguishes top from bottom.
The [published glTF 2.0.1 specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html)
is the baseline; [KHR_texture_transform](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_texture_transform/README.md)
defines the tested scale, counterclockwise rotation, offset order and per-texture
coordinate override. The original sample itself does not use that extension.

`../../../../../engine/Tools/Gltf/direct-gpu/models/TextureCoordinateTest/author_reference.py` independently reads all source forms using Python
`json`/`struct` and Pillow, verifies embedded/external image identity, and freezes
every accessor, source view origin/length, accessor offset, stride, component
format/normalization, value, node/mesh/material link and index in
`source-audit.json`. It also records 12 black label pixels with white vertical-
flip controls and nearby white same-row samples. The metadata is an oracle,
not a claim that the producer or GPU passed.

The derived fixture preserves every original byte and accessor, appending a
distinct TEXCOORD_1 to each textured quad: normalized U16 VEC2, view origin
after the original buffer and a 16-byte poison prefix, accessor offset 4,
stride 12, poison padding and mirrored U. Base color selects texCoord 1.
UV0 remains unchanged. Source and compiled fixture retain these layouts.
This exposes wrong view/accessor offsets, FLOAT-only loads, missing normalization,
tight-packed stride assumptions, always-UV0 material selection and global UV bake.
It is synthetic coverage, separately identified from upstream content.

Prepared C# inventory:

* `TextureCoordinateCoreTests.RunAll(checkoutRoot)`: 40 explicit result rows,
  five groups for each of eight source/compiled forms. Full source descriptor
  and value comparisons, immutable GPU staging bytes/runtime declarations,
  material/texture/image/default sampler selection, static geometry, retained
  upload ownership, portable packaging and original-file immutability.
* `TextureCoordinateGpuVerifierTests`: five MSTest methods. Poisoned arbitrary
  output offsets/stride and all UV sets pass; incorrect UV1, overwritten UV0,
  reversed indices, CPU index metadata, nonfinite coordinates and missing nodes
  are rejected. Synthetic arrays prove checker sensitivity only.
* `TextureCoordinateUvPlanTests.RunAll(checkoutRoot)`: 24 explicit result rows
  against the shared production `Sandbox.GltfMaterialUvPlan`. All forms retain
  the selected stream object, source bytes and per-role identity/transform;
  default variant lookup agrees, UV-free back plane needs no descriptor, missing
  selected set rejects without mutating previous plans. A distinct-role fixture
  independently checks scale, then counterclockwise rotation, then offset.
* `TextureCoordinateGpuProbe.MeasureAll`: 24 actual GPU measurements, three
  placements (identity, affine shear, reflection) for eight forms. Each checks
  all five quads, 20 vertices, 30 actual bound GPU indices, all raw UV sets,
  positions, inverse-transpose normals and reflected winding. The original
  forms have four UV streams total; the derived forms have eight. No baked
  UV representation is silently accepted.
* `TextureCoordinateImageProbe.MeasureAll`: 16 model/form/placement image rows.
  Native fresh empty, literal positive, post-reference deletion, V-flip positive,
  post-control deletion, actual GPU and post-actual deletion controls; same
  camera/presentation and stable static reference required. The reference draws
  independent source triangles with borrowed actual materials, so RGB agreement
  alone proves geometry/UV binding. It does not prove material/sampler correctness.
  Separate independent source-PNG label probes map 12 asymmetric black/white
  landmarks through authored UV barycentrics and the verified capture camera.
  The same landmarks must reject the V-flipped image. The capture callback must
  provide top-left pixels and metadata `camera` and `presentation`; the projection
  callback must use that exact camera. Every textured quad must span at least
  220 pixels to resolve the source label strokes (recommend >=1024 square target).

Proposed numeric thresholds for coordinator review are max component position
error 0.00002 inches, normal vector error 0.000001, raw UV component error
0.0000001, exact source topology and actual GPU index provenance. They must be
passed explicitly to `RequireWithin`. Image/label thresholds must be reviewed
against the initial measurement and passed explicitly to `RequireAccepted`;
the measurement status remains `measured-not-accepted` until then. No tolerance
has been widened to hide a failure. All 12 original/derived selected-UV label
probes and a useful number of V-flip rejections are required.

Executed locally: four independent Python oracle sensitivity tests passed.
The initial label search used a location without an asymmetric black pixel and
failed one test; the bounded candidates were corrected to actual source labels.
That authoring failure was not a renderer result. A revised run passed 4/4.
No engine build, editor, shader compilation or C# execution was started by this
worker. C# parser/staging cases, actual GPU readback, actual native images and
independent landmark acceptance are pending the coordinator's receipts.

Public Model.Load integration, Windows, material filtering/color-space parity,
portable optimized-resource performance and matched compiled-vmdl benchmarks
remain broader separate gates. The static sample has no animation cadence gate.
