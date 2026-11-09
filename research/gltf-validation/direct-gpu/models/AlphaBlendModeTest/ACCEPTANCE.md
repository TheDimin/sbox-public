# AlphaBlendModeTest acceptance

Owner: `/root/gpu_alpha_blend`. Sole runtime executor: `/root/gpu_validation`.
No shared producer or watched shader file was changed by this worker.

## Source audit and completed authoring

Pinned Khronos commit `90d7ede14c7e280af263824604b427a1ca02cb66` is verified.
Actual variants are **glTF** and **glTF-Binary**, each with a derived compiled-GLB
form. No Embedded/Draco/quantized variant is invented. `source-audit.json` records
independent Python JSON/PIL image observations and complete input hashes; its
success is source inspection, not engine/GPU execution. Model/image attribution:
Ed Mackey / Analytical Graphics, Inc., CC-BY-4.0 (preserve upstream LICENSE.md).

The sample has six materials, nine primitives and five parallel alpha panels.
The five panel meshes have four FLOAT positions/normals/UVs and no COLOR_0.
They share AlphaBlendLabels.png (1024² RGBA); RGB `(223,229,71)` stays constant
through the selected interior ramp texels, while alpha is `45,87,128,169,210`.
The bed also needs normal and occlusion textures; alpha support alone cannot
make the full original model pass. `AlphaControlFixtures` adds explicitly derived
COLOR_0, cutoff-equality, depth, layered-over and sidedness fixtures.

The source audit initially searched too narrow an image subregion for an alpha0
nonzero-RGB witness; that authoring assertion failed. Expanding the bounded scan
to the actual full PNG found the witness recorded in source-audit.json. This was
an oracle-authoring correction, not an engine failure or rendering pass.

## Frozen CPU and generation requests

`core-validation-request.json`: 44 actual producer CPU cases, public static
`AlphaBlendModeCoreTests.RunAll(checkoutRoot)`. Tests source material/geometry/UV
literals, pinned PNG preservation, parser defaults/legal/illegal cutoffs, linear
factor preservation, and actual default adapter Prepare acceptance. The 24 Prepare
rows are support gates, not rendered alpha tests. The inspected current adapter
rejects four nonopaque materials plus the bed per form: 20 anticipated reds must
be retained as actual results when executed, never changed to expected-pass tests.

`candidate-generation-request.json`: actual active provided GraphCompiler,
one static result, six generated candidates (three modes × single/double sided).
Requires the sampler owner's actual `Sampler.Attribute` producer. The graph
recipe imports the Encoding owner's Surface graph family; it does not fork the
engine template. No `Shader.CompileAsync`, installation or capability upgrade
occurs during generation. Actual generated source and producer identities are
hashed before the coordinator reviews/compiles the candidates.

Alpha factor binds to `g_flGltfAlphaFactor` (linear parsed factor W); RGB uses
shared `g_vGltfBaseColor`. Source texture A is not sRGB-decoded. COLOR_0 RGB and
A are clamped through provided graph Saturate nodes; absent/RGB vertex alpha
is backend-supplied one. RGB is never premultiplied.

MASK adds `clip(m.Opacity - cutoff)` then sets `m.Opacity=1` before the provided
coverage helper. This preserves equality and alpha0/cutoff0, which the stock
near-zero discard alone would violate. Cutoff has no schema upper bound. BLEND
uses straight source-alpha/one-minus-source-alpha, tests depth and disables writes
in this candidate. That is a candidate strategy, not a normative requirement.
The candidate's full world-normal backface reversal is temporary: when the normal
owner's `GltfDoubleSided` graph branch joins, remove this extra reversal to avoid
flipping twice. `m.GeometricNormal` handling remains a separate pending lighting
question and must be proved with asymmetric backface controls.

## Real source-image probe

`AlphaBlendModeSourceProbe.Run` is public static on an **internal** helper type,
so it may use the actual internal GPU backend without exposing its types publicly.
The coordinator supplies its private native scene/camera, real frame waits, compute
material, actual float-linear RGBA captures, and independently constant opaque
reference material. That reference must be a separately reviewed shader/material
with constant source-decoded RGB `sRGB^-1((223,229,71)/255)`, metallic0, roughness.8,
normal+Z and opacity1, on the helper's literal panel geometry. It must not share
the candidate alpha evaluator or source alpha texture. Caller retains leases.

The probe creates actual GltfGpuAssets/instances and renders source-isolated
panels and their compiled form. Isolation removes the bed/decal/arrow scene nodes
only; source panel accessors/materials/image bytes remain unchanged. This is
reported as **derived isolated authored panels**, not a full source render. An
explicit candidate resolver is reported separately from the default source path.

Controls are empty black/white and half-gray transfer calibration, positive
independent reference, deleted reference, source against both backgrounds and
deleted source. The exact camera is 1600×600, orthographic 3m high, looking from
source +Z; directional light is uniform and shadows disabled. Capture must
use linear float readback or a separately verified inverse transfer, with fixed
exposure and tonemap disabled. PNG byte fractions do not meet this requirement.
The half-gray empty control detects an unaccounted display transfer. Images and
raw sample receipts remain the coordinator's responsibility. A missing reference,
unchanged source, nonfinite value, wrong extent or nonpositive reference fails.

`AlphaBlendModeImageChecks` measures 25 actual ramp samples against independent
reference/background frames. For each channel the expected linear value is
`reference*coverage + background*(1-coverage)`; background difference separately
measures coverage without depending on the lit foreground. Caller supplies
reviewed tolerances below .05; none has yet been approved or measured. Pixel-center
projection/filtering near the .5 cutoff must be inspected in the actual capture;
a failure cannot be erased by raising tolerance or dropping that row.

## Supplemental actual-render gates

`AlphaControlFixtures.All()` produces 11 source-authored fixtures:

- Two separated BLEND layers with reversed node order: near red alpha.5 over far
  green alpha.25 over blue gives linear `(0.5,0.125,0.375)`. This numeric setup
  requires independently calibrated unit foreground colors. It tests this simple
  nonintersecting case, not perfect sorting for arbitrary intersecting geometry.
- Five MASK depth fixtures: below/equal/above cutoff, alpha0/cutoff0 and cutoff1.25.
  Near MASK precedes farther opaque blue; discarded fragments expose blue and
  accepted fragments fully occlude it.
- Two COLOR_0 fixtures: bilinear 2×1 PNG alpha0/alpha1 at UV(.5,.5), factorA.375,
  RGB versus RGBA vertex alpha.25 gives coverage .1875 versus .046875.
- Single/double-sided fixtures captured from both source-Z signs with matched
  lighting and independently reversed-normal reference. No-cull and correct
  backface lighting are distinct assertions; merely seeing a backface is not enough.

The current backend creates one opaque-only SceneCustomObject; the backend owner
confirmed actual BLEND pass routing is not implemented. Future shared changes
must preserve pose/buffers/lifetime and ordinary renderer scene-object semantics.
No disconnected alpha policy class or fake metadata capability was added.

## Remaining evidence

All C# helper execution, actual graph generation, shader compilation, actual GPU
alpha readback, source images, supplemental fixtures, public Model.Load integration,
full-model bed material fidelity, Windows and performance gates remain **unrun**
until the coordinator supplies receipts. Do not claim rendered conformance from
the source audit, parser tests, shader text or material metadata.

Primary normative basis: published glTF 2.0.1 at registry commit
`8e798b02d254cea97659a333cfcb20875b62bdd4`,
[alpha coverage and double-sided](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#alpha-coverage).
BLEND depth-write/sort policy is implementation guidance; the required comparison
is the resulting over composition in the scoped test scene.
