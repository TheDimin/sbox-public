# MeshPrimitiveModes validation freeze

Owner: `/root/gpu_mesh_primitive_modes`. Coordinator: `/root/gpu_validation`.
Shared backend: `/root/gpu_fox`. No shared producer, renderer, shader, global
manifest or execution artifacts are owned by this worker.

## Audited actual source

Khronos glTF-Sample-Assets commit `90d7ede14c7e280af263824604b427a1ca02cb66`.
The actual upstream forms are **glTF** and **glTF-Embedded**, each containing
seven indexed primitives, one per mode 0–6. There is no upstream GLB,
nonindexed, UINT8 or UINT32 variant. Everything is CC0-1.0; source license and
credit remain in `Models/MeshPrimitiveModes/LICENSE.md` and `README.md`.

| Input | Bytes | SHA-256 |
| --- | ---: | --- |
| glTF/MeshPrimitiveModes.gltf | 4059 | `2a8b3209c25e2fca5105539136a05f42d170767f0eb16148cd3825758e5ee977` |
| glTF/buffer.bin | 216 | `4369522e5929bed650242fea069c687aa2ea1230caa61c378610b2154c1048d7` |
| glTF-Embedded/MeshPrimitiveModes.gltf | 4374 | `9c6dd4c66dd5f4f15c052224b5c1446947ada7f935c521137520dbebbc7f2312` |

Seven independent mesh nodes use the same seven FLOAT VEC3 positions in a
regular hexagon, with authored translations. POSITION view origin is 132,
accessor offset 0, stride 12. UINT16 indices share a 130-byte view at origin 0;
their accessor offsets are 0, 14, 38, 52, 66, 102 and 114.

| Source mode | Authored index count | Draw type | Expanded draw indices | Geometric primitives |
| --- | ---: | --- | ---: | ---: |
| 0 POINTS | 7 | Points | 7 | 7 points |
| 1 LINES | 12 | Lines | 12 | 6 lines |
| 2 LINE_LOOP | 7 | Lines | 14 | 7 lines including 6→0 closure |
| 3 LINE_STRIP | 7 | Lines | 12 | 6 lines without closure |
| 4 TRIANGLES | 18 | Triangles | 18 | 6 triangles |
| 5 TRIANGLE_STRIP | 6 | Triangles | 12 | 4 triangles with alternating winding |
| 6 TRIANGLE_FAN | 8 | Triangles | 18 | 6 triangles |

The oracle uses literal source/index/translation arrays. It never calls a
producer evaluator or topology helper to obtain expected data. The supplemental
fixtures retain every topology and oriented geometric primitive, using UINT8,
UINT16, UINT32 or nonindexed accessors. Nonindexed positions are reordered into
the **authored index sequence**, preserving the actual point/line/triangle
classes and geometry. They are explicitly derived fixtures, not invented
upstream variants. Poisoned position padding has accessor offset 8, stride 28
and separately computed nonzero view origins; index accessor offset is 4.

## Callable suites and exact inventory

All types use namespace `Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation`.
`submission.json` contains complete file hashes and runner suite definitions.

| Suite | Entry | Cases | Scope |
| --- | --- | ---: | --- |
| `MeshPrimitiveModesCoreTests` | `RunAll(checkoutRoot)` | 84 | CPU/data; every mode in all 12 forms |
| `MeshPrimitiveModesPlanTests` | `RunAll(checkoutRoot)` | 84 | Pure staging plan; every mode in all 12 forms |
| `MeshPrimitiveModesGpuChecks` | `RunVerifierControls(checkoutRoot)` | 17 | Manufactured readback verifier sensitivity only |
| `MeshPrimitiveModesImageChecks` | `RunVerifierControls()` | 4 | Manufactured pixel verifier sensitivity only |

**189 C# cases are authored and unexecuted.** Each of the 84-case suites is
7 modes × 12 forms. The forms are `glTF/source`, `glTF/compiled_glb`,
`glTF-Embedded/source`, `glTF-Embedded/compiled_glb`, and
`derived/index-5121`, `derived/index-5123`, `derived/index-5125`,
`derived/nonindexed`, each with `/source` and `/compiled_glb` suffixes.
`GltfCompiler.Compile` supplies a lossless packaged GLB read by `GltfReader`.
These names do not claim an optimized compiled `.glb_c` implementation.

The source/data tests check every raw position and index, authored world
position, basis conversion, descriptor offset/stride/component format,
generated-vs-authored index provenance, loop/strip/fan topology, exact owned
byte budget and upload survival after document disposal. Plan tests inspect
all input arena bytes, runtime output fields, point/line preservation and
triangle corner expansion. The 17 readback verifier controls include all seven
modes, deliberately padded runtime output, corrupt indices, CPU index origin,
wrong runtime POSITION offset and missing primitive. The four image controls
reject empty agreement, nondiscriminating negative controls and dropped points.
Manufactured readbacks/images are never submitted as GPU/render evidence.

## Actual GPU and rendered geometry requests

`MeshPrimitiveModesGpuProbe.RunWholeSceneNumeric(checkoutRoot, formName,
world, computeMaterial, waitForActualFrames, materialResolver = null)` runs
the complete authored default scene and returns seven numeric measurements.
One call per form gives 84 primitive measurements, separate from the C# suite
totals. It checks exact seven-node provenance, actual bound-index readback,
all declared output positions and flat triangle normals. `SourceVertices`
remains CPU provenance metadata; it is not called GPU source-map readback.

`RunNumeric(checkoutRoot, formName, mode, world, computeMaterial,
waitForActualFrames, materialResolver = null)` is bounded to one selected
mode and returns three rows: identity placement, reflected affine/sheared
placement, and continued operation after disposing the other instance. The
second instance's actual GPU bytes must remain unchanged. The selected-scene
diagnostic retains all source meshes/buffers, selecting the original node only.
It is reported separately from the untouched default-scene probe.

`RunImage(checkoutRoot, formName, mode, world, computeMaterial, drawMaterial,
waitForActualFrames, capture)` is bounded to one mode/form. The coordinator
supplies a matched native camera and `Func<string, Task<Color[]>> capture`
that saves PNGs, dimensions, camera/target/material state and hashes. Required
order: fresh empty → independent literal positive → deleted empty → literal
topology negative → deleted empty → direct GPU positive → deleted empty.
The same borrowed diagnostic material is used on both paths. Reference
`Graphics.Draw` submits **actual Points, Lines or Triangles**, and never
constructs Model/Mesh or substitutes points/lines with triangles, quads or
sprites. The independent reference does not use any producer path.

Set a 192×192 orthographic native target, camera at `(+200, nodeX/0.0254,
nodeY/0.0254)` looking along -X, orthographic height 120 inches, black clear,
Normal diagnostic mode, normal postprocessing and the accepted native capture
route. Use a single-sided diagnostic material. The coordinator may reuse the
existing triangle capture helper with position/target/height arguments. This
framing shows the complete selected hexagon, including every point. Save
empty/positive/negative/deletion captures with identical camera state.

Numeric measurements remain `measured-not-accepted`. Enforced proposed bounds
are `32 * float-epsilon * 256 = 0.0009765625` inches per position component
(these known placements remain below 256 inches) and `32 * float-epsilon`
per generated triangle-normal component. These are finite-float arithmetic
bounds, subject to coordinator review before accepting execution. Points/lines
have **no authored NORMAL**: their fallback direction is not a source
conformance assertion. Geometry images enforce positive actual/reference
coverage, a negative topology that changes pixels, no unexpected magenta and
**exact foreground-mask equality**. RGB error is measured separately. A failed
mask stays failed until investigated; no unreviewed tolerance widening.

The point negative keeps only vertex 0; line negatives collapse each segment;
triangle negatives reverse winding. These controls retain the primitive class.
The bounds do not establish material fidelity or antialiasing equivalence.
No native point/line blocker is proven: the backend currently submits Points
and Lines, but actual native shader/draw/capture execution is pending. Only
an observed failed native request and its retained artifacts can establish a
blocker; no unsupported classification is inferred from pending work.

The [pinned glTF 2.0.1 specification](https://raw.githubusercontent.com/KhronosGroup/glTF/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc)
defines mode topology and recommends one-pixel points/lines and unlit
base-color-plus-emissive behavior for no-normal points/lines. The Normal-debug
geometry probe does not validate that material recommendation. Default-source
material appearance, point/line width/style and lit/unlit handling need separate
native evidence; PrimitiveModeNormalsTest has a different owner.

## Executed evidence and outstanding gates

`python3 .../MeshPrimitiveModes/test_source_oracle.py`: **8/8 passed**, using
only Python stdlib in 0.002 s. This verifies pinned three-file hashes, actual
format inventory, embedded/external data identity, all index offsets/types,
every FLOAT position, translations/bounds, loop/strip/fan sensitivity and wrong
origin/stride/index-offset detection. `git diff --check` found no whitespace
errors. No C# build, shader compilation, editor launch/call or capture was run
by this owner.

Pending: all 189 C# cases, full-scene actual GPU readbacks, per-mode matched
images and deletion controls, negative live bound-index mutation where useful,
default material fidelity, public `Model.Load`, existing vmdl regression,
portable optimized `.glb_c`, Windows and model-editor parity. There is no
animation in this sample; wall-clock animation does not apply to this model.
