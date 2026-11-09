# InterpolationTest direct GPU validation

Status: 40/40 numeric cases passed inside the actual editor against installed D
`Sandbox.Gltf` MVID `b064c360-8818-4f8c-8963-27d894571c5f` and engine MVID
`04b86c2e-4406-422e-a466-b61ec13f1f86`. Source hashes were verified against the
executed receipt. Nine player cases and all GPU/image/wall-clock gates are pending.
No direct GPU, rendered, wall-clock or Model.Load parity claim follows from this
numeric result.
The validation coordinator owns execution and receipts; model owners do not start
builds, shader compilers or editors independently.

After the next Codex restart, the source freezes and every model input hash were
revalidated against the actual pinned checkout. The shared raw GPU triangle gate
is now proved independently of this model: receipt
`Temp/gltf-validation/direct-gpu/raw-triangle-multiline-modes/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `884bb3276c4fd27a8c47ba4597848e284d23248d4f452ebfa6ec3cb7b2d93a01`.
The bound-index corruption negative control also passed in receipt
`Temp/gltf-validation/direct-gpu/raw-triangle-index-negative-frame-type/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `920299bdcd1fce5f2d4329aae560c6fc31ce0340f943578f3299d3c0226a5850`.
Both terminal receipts were inspected. Earlier failed/compile-only triangle
attempts remain retained and do not replace these passing receipts. These gates
permit model execution; they do not prove InterpolationTest rendering.

Pinned source: Khronos glTF-Sample-Assets
`90d7ede14c7e280af263824604b427a1ca02cb66`, `Models/InterpolationTest`.
Inputs are checked against `corpus-case-manifest.json`; the sample license is CC0.
The normative interpolation reference is published glTF 2.0.1, commit `8e798b02`,
[animation samplers and Appendix C](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#appendix-c-animation-sampler-interpolation-modes).

The actual sample has ten root nodes, nine independently posed copies of the same
cube primitive and one static textured plane, no skins or morph targets, and nine
two-second animations. Each animation has five keys at 0, .5, 1, 1.5 and 2 seconds.
Scale, rotation and translation each have STEP, LINEAR and CUBICSPLINE clips. Cube
POSITION/NORMAL share stride 24 at offsets 0/12. The plane uses stride 32 with UVs
at offset 24. Its U8 index accessor begins 36 bytes into a shared index view.

`../../../../../engine/Tools/Gltf/direct-gpu/models/InterpolationTest/InterpolationReference.cs` independently decodes the hashed text source with its
actual bufferView/accessor offsets and strides. It does not invoke the production
reader, animation sampler, pose evaluator or geometry helpers. Cubic channels are
converted to Bezier control points and evaluated with de Casteljau subdivision;
TRS matrices are assembled independently in double precision. The decoder's FLOAT
and nonsparse restrictions describe this pinned oracle, not engine support limits.

`InterpolationConformanceTests` contains 40 executable MSTest cases: nine clips
times four forms (text source, binary source and a valid compiled GLB from each),
plus four inventory/analytic controls. Every clip/form checks 37 times: each key,
its adjacent representable FLOAT values, five interior fractions per segment and
both out-of-range clamps. It compares channel components, all ten node world
matrices, unaffected defaults and four source vertex anchors. Analytic controls
distinguish STEP boundaries, duration-scaled cubic tangents, normalized cubic
quaternions and LINEAR short-arc behavior from CUBICSPLINE authored signs.
`Measurements` records numerical maxima. The eight-float-epsilon scaled ceiling
is a diagnostic budget for these arithmetic operations; it does not approve curve
resampling, GPU tolerances or approximate animation paths.

`InterpolationPlayerTests` adds nine cases through the actual shared pure player,
one per authored clip. They test paused construction/selection, complete consumption
of .75 and .625-second updates, precise double seek state, one callback per update,
independent instances and immutable pose snapshots, endpoint clamping, multi-loop
advancement, playback speed and return to authored rest. These are controlled clock
tests and remain separate from actual wall-clock editor playback.

The shared coordinator should compile the two numeric files against the loaded
production `Sandbox.Gltf` and actual MSTest, invoke
`InterpolationConformanceTests.Configure(checkoutRoot)`, then run 40 numeric cases.
Link the frozen `GltfGpuAnimationPlayer.cs` and `../../../../../engine/Tools/Gltf/direct-gpu/models/InterpolationTest/InterpolationPlayerTests.cs` for
the additional nine player cases. Do not compile replacement reader/evaluator
sources into a test helper. Record all source hashes, the actual producer MVID,
compile diagnostics, each case result and numerical measurements.

The remaining direct GPU gates are:

- The shared raw-buffer triangle/control gate passes first, with zero native
  Model/Mesh/IModel/ModelBuilder/native controller construction.
- Each of the four input forms produces all ten node/primitive outputs. Runtime
  field declarations, byte extents and `SourceVertices` mappings are checked;
  no output struct or input packing is assumed.
- Index arrays must come from the actual bound index buffer and explicitly carry
  `IndexDataOrigin="gpu-readback"`. `SourceVertices` remains CPU provenance,
  rather than a claimed GPU map readback. Model-local negative tests reject
  absent/CPU index provenance; their meaningful red/green execution is pending.
- At every key and representative between-key times, call `SetPose` explicitly,
  render a completed current revision, then pass actual output snapshots to
  `InterpolationGpuChecks.Measure`. It measures all vertices, independent full
  affine positions/normals, source triangle topology and per-node errors.
  `RequireWithin` requires explicitly reviewed numerical tolerances; measurements
  alone have status `measured-not-accepted`.
- The scale clips legitimately collapse to scale (0,0,0) at .5 and 1.5 seconds.
  Their positions must remain finite and collapse correctly; normal inverse
  transpose is undefined only for those singular outputs. Finite deterministic
  emitted normals are still required. Other nodes and the static plane continue
  to render. Near-zero but nonsingular scales do not waive normal comparisons.
- Include identity, nonuniform, reflected and sheared instance placement and two
  independently animated instances sharing one immutable asset. Draw/readback
  must not advance either clock. Repeated disposal and failure rollback are
  shared-backend acceptance requirements, not bypassed by this sample.
- Use the proved native presentation route: normal visualization, normal
  postprocessing, an owned light and exact camera custom dimensions. Record
  fresh empty-before, positive geometry and deleted-empty-after controls. The
  earlier Albedo route failed and cannot supply accepted captures.
- Compare actual images to independently assembled `ReferenceTriangles` from
  this source oracle. Literal `Graphics.Draw` reference geometry is diagnostic
  only, never the implementation's mesh data. Use the same declared diagnostic
  material for geometry/normal comparisons and state when it replaces source
  materials. The stock material/texture appearance gate is separate and includes
  the plane's texture; a gray diagnostic pass is not appearance parity.
- Capture high-cadence authored-time movies with actual captured states and
  accurate per-frame durations. Never interpolate or duplicate frames to imply
  unobserved motion. Keep deterministic seek media distinct from wall-clock proof.
- For wall-clock proof, record actual monotonic elapsed time, supplied delta,
  observed instance clock, pose/draw revisions and callback errors over several
  complete cycles, including a measured long frame. All supplied elapsed time
  must reach the clock; requested-time agreement alone is insufficient.
- Prove the shipped Model.Load managed resource/backend route separately. A
  successful research helper does not establish API integration or performance
  parity with compiled vmdl.

Current generated test/source artifacts are intentionally model-scoped. Shared
execution receipts belong to `direct-gpu/EXECUTION.md` and the coordinator's Temp
run folders. The older native-model movies remain historical evidence only.

Executed numeric receipt (74 cases including other models, 40 InterpolationTest):
`Temp/gltf-validation/direct-gpu/core-model-tests-alias-fixed/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `1a7570f0167fa928fe7927b959f9bb8b2c2a15a0d36d8126d45697c656f98e94`.
This receipt bounds errors by the assertions' ceiling but does not retain the
`Measurements` list's exact maxima. The coordinator has been asked to collect
that property on the next necessary run; exact measured maxima remain unavailable.
