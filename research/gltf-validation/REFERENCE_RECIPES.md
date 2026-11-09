# Reproducible source-reference capture recipes

`reference-recipes.json` contains one row for every **337 actual variants of
150 models**, keyed by the unchanged `Model/variant` corpus ID. It joins the
current Run result through `recipeId`. It does not approve appearance: the user
reviews the source image/video beside actual engine output, and source, recipe,
producer or capture changes invalidate that review.

All 150 primary pinned source previews were viewed in five contact sheets;
all 15 animated previews were additionally viewed as phase strips. There are
13 GIFs and two animated WebPs. Inspection aids are reproducible local files in
`Temp/gltf-validation/reference-review/`, not substituted expected renders.
`reference-recipes.schema.json` defines the consumer shape, and
`../../engine/Tools/Gltf/scripts/test_reference_recipes.py` checks completeness, source hashes and numerical
validity. `../../engine/Tools/Gltf/scripts/build_reference_recipes.py` regenerates the stable inventory.

The document declares its local corpus root; reference paths and source paths
are relative to that root. Reference URLs use the exact pinned Git revision,
not `main`. Animated GIF/WebP references remain `kind="image"` with explicit
format/frame timing so a browser can display their actual original animation.
Available media has a SHA256; truly unavailable media must use an explicit
`kind="unavailable"` and reason rather than a guessed file/hash.

Camera vectors are **glTF metres**, up is a direction, vertical FOV is in
**degrees**, and orthographic height is in metres. The driver performs the
single agreed cyclic basis/unit conversion and explicitly selects vertical FOV.
Presentation specifies background, environment, directional lights, exposure
and resolution. Authored camera values are labeled separately from the inferred
choice of which camera might have produced the preview. No undocumented HDRI,
renderer, exposure or camera choice is labeled exact.

The camera recipes use model-specific inspected classes: flat diagnostic
grids, shallow grids, product views, source-authored cameras, a Fox side view,
an elevated frontal morph cube, the proper OrientationTest hemisphere, and an
interior Sponza view. Generic node-rest bounds remain approximate for deformation;
they are never an exact-reference claim. SimpleInstancing reuses its existing
independent all-125-instance bounds, and BoxAnimated includes the top box's
authored upward travel. Unsupported scene/camera selection remains explicit;
framing does not authorize repairing or silently selecting a missing default.

Static reference images request one pose even when the asset has optional
animations. Animated references request actual APNG/video sequences with explicit
sample times and frame delays, not four stills. When usable GIF timing exists,
its delays and total presentation duration are retained. The single-cycle
correspondence for AnimatedMorphCube, BoxAnimated and RiggedSimple is an explicit
inference, including any comparison-specific playback rate; it does not change
the engine's authored animation semantics or prove real-time cadence. Missing
timing uses a clearly inferred 30-fps authored-duration plan.

Some reference media contains behavior outside a single source clip:

- InterpolationTest visibly animates nine disjoint clips simultaneously. A
  single-clip driver must report that recipe as only partially applied.
- SimpleSkin's GIF later switches to wireframe presentation; the glTF animation
  does not encode that material/debug toggle.
- CesiumMan's GIF appears to orbit the model. Its undocumented camera motion
  is not claimed reproduced by a fixed view.

These limits are carried into each applicable recipe. All current rows remain
`matchStatus="needs-tuning"`; inspecting upstream art is not user approval of
the rendered result. Independent glTF spec/numerical tests remain separate.

Coordinate inventory changes with the validation coordinator: a current Run
binds the whole recipe SHA256. Do not rewrite the JSON during that run. At the
next safe boundary, overwrite the same canonical file and invalidate stale
reviews; do not create date/revision variants or resurrect historical captures.
