# GPU companion diagnostic acceptance

This is a bounded experiment, not a `Model.Load` implementation or component
parity claim. Production deformation files remain frozen. Only the new research
script and diagnostic compute shader may change for this experiment; execution,
shader compilation and captures belong to the validation coordinator.

## Required observable result

With the normal native scene renderer enabled (`r_managed_scene=false`), a
`SceneCustomObject` draws a stock-material triangle from a GPU buffer written by
compute. The compute shader consumes glTF-accessor-derived byte offsets,
strides, component types and normalization, plus a small joint palette. It must
not infer layout from stride, numeric values or a fixed C# vertex structure.

Each accepted row must provide:

1. A fresh render target and genuinely empty native scene control before drawing.
2. Actual compute dispatch, a compute-write to vertex-read barrier, and a native
   custom-object callback that draws using an explicit runtime vertex layout.
3. A separately constructed static reference using independently evaluated
   fixture positions, not the production glTF evaluator or a helper load path.
4. Visible reference and actual coverage, silhouette intersection/union above
   0.97, and a fresh empty control after deletion. Blank actual/reference images
   never pass.
5. Source/accessor metadata, compute shader source and compiled identity,
   actual native render route, producer MVIDs, target sizes and capture hashes.

Native camera color clearing previously failed its empty control. If explicit
camera command-list clearing is necessary, apply the identical declared clear
to every test/control view, retain the original failure, and label the route.
Never change the global renderer, omit empty controls or substitute a managed
offscreen capture for native evidence.

## Initial cases

- Source FLOAT positions in a strided view with a nonzero accessor offset;
  normals in a separately strided view; index and weight fields in another view.
- U8 and U16 joint accessors, paired with UNORM8, exact FLOAT `.3/.7`, and UNORM16
  `19660/65535` plus `45875/65535` weights.
- Identity and translated/rotated joint poses; a second instance has an
  independent output/palette buffer. Updating one instance must leave the other
  unchanged.
- Vary runtime output attribute order/offsets so no GPU position/normal address
  is an implicit constant. Output layout is an explicit engine-facing conversion
  contract, distinct from original glTF buffer views.

CPU work may build fixtures, stage raw bytes, upload joint palettes and evaluate
independent reference geometry. CPU-deformed actual geometry, readback followed
by CPU upload, FLOAT-to-UNORM conversion, dropped influences and fake animation
metadata are prohibited.

## Integration gates after this diagnostic

A passing draw does not supply normal model behavior. A production proposal must
separately prove no double draw, ordinary `Model`/`SceneModel` and component
identity, per-instance lifetime/unload/replacement, bounds and culling, editor
picking/selection, shadow/depth passes, LOD/bodygroups, asset/component material
overrides, and deformation motion history. Source skeleton APIs, native graphs,
physics and manual morph-override composition remain separate work.

No broad companion implementation or default pipeline change is authorized by
this diagnostic result alone.
