# TextureLinearInterpolationTest

Pinned Khronos corpus: `90d7ede14c7e280af263824604b427a1ca02cb66`.
Chosen source: original `glTF-Binary/TextureLinearInterpolationTest.glb`, SHA256
`5a7d79bb138f9f732002a988298b5ba44f614064330e94f6ea8c17e38b998440`.
One canonical derived `.glb_c` is generated under the model's Temp evidence path.
The external glTF is audited for source provenance only, not retained as another
executed form. Original model, PNG, reference and license bytes remain unchanged.
Author: lexaknyazev; license: CC0-1.0 (upstream LICENSE.md).

The upstream intent is linear interpolation **after sRGB decoding**. The left
sphere has linear emissive factor `(0,.5,0)`; the right uses a 2×1 black/green PNG,
factor one, and all 205 UVs at `(.5,0)`. Both filters explicitly select LINEAR,
without mip filtering. Both address axes default to REPEAT. A MASK, double-sided
label plane is also part of the unchanged model. No camera or light is authored.

The independent expected green is `(decode(0)+decode(1))/2 = .5`. Filtering first
and then decoding gives `.21404114048223255`, a `.2859588595` difference. Nearest
filtering gives an endpoint, differing by `.5`. These are separately observed
wrong-sampling controls, never replacement production shaders.

`TextureLinearReference` independently reads the pinned GLB JSON and BIN chunk,
including accessor/view offsets and strides. Three core cases check both forms'
material/texture/sampler provenance, all constant source UVs, image bytes and the
analytic negative-control separation. Two native cases use real public
`Model.Load` and ordinary `ModelRenderer`, unchanged default source materials,
zero native IModel placeholders, actual bound GPU position/UV/index readback,
original native texture pixels, a one-level 2×1 GPU view, and final native images.

The numeric shader samples the actual public material texture through the real
material's `GltfEmissiveSampler` binding. Its default sampler is deliberately
POINT/CLAMP, so missing attribute binding cannot satisfy the LINEAR/REPEAT gate.
Magnification and minification footprints plus positive/negative repeated U
coordinates must produce green .5 within .002. Its explicit wrong-point and
wrong-transfer branches must remain distinct. No source, texture, UV or
production shader quality is changed to satisfy these checks.

Whole-model capture uses inferred front orthographic framing and compares the
two authored green witnesses (mean green difference ≤2/255). This is a same-view
semantic image check, not exact reproduction of the upstream renderer's unknown
camera/light/exposure. Empty-before and renderer-deletion-after controls must
remain empty; destroyed callbacks must stop. GPU positions must be within
.00003 inches of independent source arithmetic; UV and source topology are exact.

Existing TextureSettings sampler-matrix receipt `c97866b3…` (102 requests, all
min/mag and independent wrap pairs) and TextureEncoding transfer evidence retain
their original producer and scope. They are not replayed for this unchanged
feature. This model adds distinct-endpoint interpolation through the actual
public emissive material. Mip filter combinations are inapplicable to its explicit
non-mip sampler, and unrelated broader material/conformance claims are excluded.

Execution: three core cases passed in mixed receipt `49f41422…`; its two public
diagnostic shader-format failures remain retained. Corrected public-only receipt
`7d2a89a3c6adfb949580ea42f4079dc6bcef25500a05c2bac3d032cf2b491897`
passed both source and canonical forms on installed Engine MVID
`5a07f228-dff8-4d6b-b4ef-890b32a80299`. No unchanged core or sampler matrix was
replayed. The earlier helper compile failure `fcae6fc0…` also remains recorded.

All twelve positive float samples returned exactly `(0,.5,0,1)`; the deliberately
incorrect filter-before-decode sample returned green `.21404819` and the point
control returned `1`. Each actual GPU view was 2×1, one level. Original native
texture pixels and 1,926 submitted indices were exact; 416 submitted vertices
had maximum position error `.0000076293945` inches and UV error zero. Both sphere
image means were `.7372549` encoded green. Empty/deleted captures had no coverage,
and deleted callbacks stopped. Actual default materials used the named
`gltf_surface_five_uv.shader`; no source or production material edit was needed.

Both captures were inspected against upstream's correct/incorrect references.
Labels render and the two green spheres match. Source and canonical capture
bytes are identical (SHA256 `cb42ac81…`), so `media/current.png` is one new visual
comparison shared across both forms. The canonical artifact is byte-identical
to this already portable original GLB. `result.json`, exact receipt copies and
`comparison-handoff.json` preserve scope, hashes, license and producer identity.
Linux evidence does not establish Windows behavior, performance, full API/editor
parity, exact upstream presentation, or final user visual approval.
