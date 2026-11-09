# MeshoptCubeTest independent validation

Current public-integration evidence is in [result.json](result.json) and the
6 November [source/standards audit](CURRENT_AUDIT.md). The original prepared
suite inventory below is historical; it is not a current execution claim.
The 8fc non-opaque rejection remains recorded. On 7eb the authored MASK route,
cutoff/settings and all four forms' rest GPU/image assertions completed before a
later helper pose-ownership error; those are partial assertions under a failed
wrapper, not four passing tests. The corrected owned-generation midpoint then
exposed an actual missing-tangent orthogonality defect. The installed correction
now passes two targeted canonical tests: five owned-resource poses with actual
GPU index negatives, and two-loop ordinary public playback with1024px source
material previews. Independent posed GPU tangent length/dot errors are zero.
Execution uses one canonical `glTF-Meshopt/compiled_glb_c` input. Historical rest
comparisons were not replayed. Final appearance approval remains with the user.

Corpus pin: `90d7ede14c7e280af263824604b427a1ca02cb66`. Upstream source is
CC0-1.0, authored by Arseny Kapoulkine (2025); retain upstream metadata/README.
The only upstream variants are **glTF** and **glTF-Meshopt**. Each has source
plus a derived portable valid GLB packaging form, called `compiled_glb_c` by the
probe. These are four forms, not an invented upstream binary variant.

`../../../../../engine/Tools/Gltf/direct-gpu/models/MeshoptCubeTest/author_reference.py` reads actual `glTF/MeshoptCubeTestFallback.bin` with Python
`struct` to author all 109 accessor values and 99 complete-view hashes. It uses
no Sandbox reader/evaluator, codec library or generated rendering. This is the
upstream explicit fallback, with independent literal geometry/normalization and
animation-key checks. Both variants have identical layouts and codec bytes.
All expected inputs and dependencies have length/SHA256 checks in `reference.json`.

The 35 root nodes comprise ten textured labels and 25 cubes: 640 vertices,
960 triangle-list indices (320 triangles). Sixty views exercise ATTRIBUTES v0
and v1 (`a0`/`a1`), TRIANGLES (`e1`), INDICES (`d1`), and NONE, OCTAHEDRAL,
EXPONENTIAL, COLOR and QUATERNION filters. Layouts include interleaved 20-byte
records with POSITION at 0, signed normalized byte NORMAL at 12 and unsigned
normalized byte COLOR at 16; separate signed/unsigned byte/short streams; and
U16/U32 index streams. No inferred float vertex layout is accepted.

Five animated cubes have normalized signed-short quaternion outputs, literal
times `[0,1,2]` and rotations `[identity,90-degrees-about-Y,identity]`. The
independent posed oracle uses analytic Y rotation, then authored scale and
translation and exactly one `(z,x,y)/.0254` engine conversion. Seek samples are
0, .375, 1, 1.625, 2 and 2.25 seconds (the last tests clamping).

The optional variant requires only KHR_mesh_quantization, and must remain
usable with the real fallback when meshopt is unsupported. The compressed-only
variant additionally requires KHR_meshopt_compression, has a URI-less fallback
placeholder, and must reject missing support clearly. A bad selected compressed
header must reject rather than silently selecting fallback. These policies have
separate assertions for source and portable forms.

## Codec maintenance audit, 5 November 2026

The project references Evergine.Bindings.MeshOptimizer `2026.10.1.20`. Its local
nuspec identifies binding source revision `2d5fe80c599106a869570096fad6a1441b6853d0`.
The actual cached nupkg SHA256 is
`840a3ea678f652ff04e98ce05d654b810112241572e9daa1a16eb35607b8a2e5`, matching
the existing third-party notice. The [primary NuGet package page](https://www.nuget.org/packages/Evergine.Bindings.MeshOptimizer)
currently lists this as latest and documents native Linux x64/arm64, Windows
x64/arm64 and macOS arm64 support. The [upstream releases](https://github.com/zeux/meshoptimizer/releases)
currently mark v1.3 latest; its [versioned header](https://github.com/zeux/meshoptimizer/blob/v1.3/src/meshoptimizer.h)
declares version 1030. This matches the repo notice's bundled-upstream version.
Fetching the binding's pinned `binding.yml` failed in this session; that remote
source-file verification remains unverified rather than silently inferred.

The [primary KHR extension specification](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_meshopt_compression/README.md)
still reports Release Candidate and describes both v0/v1 and all five filters.
No extension-policy or package upgrade is made by this model worker. The repo's
existing specification pin remains authoritative until separately advanced.

## Frozen callable suites and execution gates

Namespace: `Sandbox.Gltf.DirectGpu.MeshoptCubeValidation`.

- `MeshoptCubeCoreTests.RunAll(checkoutRoot)`: **35** CPU/codec/data cases.
- `MeshoptCubePlanTests.RunAll(checkoutRoot)`: **12** staging-only cases.
- `MeshoptCubeGpuChecks.RunVerifierControls(checkoutRoot)`: **9** synthetic
  checker controls, including output declaration padding, rotation phase,
  color normalization, UV orientation, actual index winding and origin.
- `MeshoptCubeImageChecks.RunVerifierControls()`: **7** synthetic image
  controls. These deliberately manufactured inputs prove checker sensitivity.
- `MeshoptCubeGpuProbe.Run(checkoutRoot, formName, world, computeMaterial,
  waitForActualFrames, materialResolver = null)`: actual GPU work, **four form
  invocations**. Each invocation returns nine geometry/state rows plus one
  explicit pending material/cadence row; do not count that row as a pass.
- `MeshoptCubeImageChecks.AssertMatched(...)`: separate source-material image
  acceptance. Requires fresh empty-before, independent expected,
  empty-after-reference, actual, and deleted-empty-after frames with identical
  camera/presentation/dimensions. All 35 independently placed regions must show
  positive geometry. It enforces per-region IoU >= .98, mean RGB byte error
  <= 1 and maximum RGB byte error <= 8; empty controls allow at most one byte
  of presentation noise. These thresholds must be reviewed against the actual
  controlled capture before a material-fidelity pass is claimed.

GPU readbacks check every vertex's position, inverse-transpose normalized normal,
normalized vertex color, UV and generated tangent frame; all bound index values
must originate from actual `GpuBuffer.GetData` (`IndexDataOrigin=gpu-readback`).
The CPU SourceVertices map is expressly metadata. Position tolerance is .0002
inches, accounting for float output at source positions around 800 engine units;
normal tolerance 2e-6, UV/color 2e-7. Tangents must be unit, perpendicular and
positive-handed; no arbitrary tangent-generation algorithm is treated as oracle.
Another instance stays at its own time/placement while A animates, and A must
survive B disposal. Probe execution creates zero native Model/Mesh objects.

Source materials are ten distinct PNG MASK labels with cutoff .001, double-sided
linear-filter/clamp UV0 sRGB base color, metallic 0 and roughness 1; cubes use
the glTF default material and authored vertex color where present. CPU metadata
and GPU color/UV readback do not establish final material pixels. A diagnostic
material resolver cannot satisfy source-material acceptance. The upstream
screenshot has no matched camera and cannot be the expected image.

## Actual evidence and remaining gates

Executed locally: **5/5** lightweight Python independent oracle authoring tests.
Reference JSON authoring completed, 233998 bytes, SHA256
`bffc5439add7da08959b2a01ca93e748b274a3d3e26ce64f0d1b7b448d402219`.

Pending: compilation and execution of **63** C# CPU/staging/synthetic-control
cases on the coordinator's actually loaded producers; four actual GPU probes;
independently authored matched material pixels/capture controls; wall-clock
animation cadence; repeated load/unload/failure rollback; public Model.Load;
Windows; and matched compiled-vmdl loading/memory comparison. No build, shader,
editor, native decoder execution or GPU job was run by this worker. No shared
producer or backend file was modified. A missing heavy-job admission becomes
pending evidence, never an exclusion or pass.
