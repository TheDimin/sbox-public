# MeshoptCubeTest current audit

Inspected 6 November 2026. Pinned sample checkout HEAD is
`90d7ede14c7e280af263824604b427a1ca02cb66`. The actual manifest and directory
contain `glTF` and `glTF-Meshopt`, both `.gltf`, and no upstream binary variant.
Each source is also tested through its derived compiled representation. The
existing oracle and codec/layout evidence are reused without replaying their
old inventory solely for a newer build.

Source JSON SHA256:

- glTF: `8721150e3409425acf83aa21986e55880360ab084c17e96409c25fac53477f72`.
- glTF-Meshopt: `b5947609f3d8aba58de3d43101df3b635ffaaab5849431f8518af6a98a040433`.

The optional source requires KHR_mesh_quantization and provides a real fallback
buffer. The compressed-only source additionally requires KHR_meshopt_compression
and has a URI-less fallback placeholder. Their compressed data and descriptors
exercise 60 compressed views: 44 ATTRIBUTES (33 v0/a0 and 11 v1/a1), 12 TRIANGLES
(e1), and 4 INDICES (d1). Filters are NONE 40, EXPONENTIAL 6, OCTAHEDRAL 6, COLOR 6,
and QUATERNION 2. The public loader explicitly selects KHR meshopt and
quantization. The document reader validates extension-specific headers and does
not silently fall back after selecting malformed compressed data.

[KHR_meshopt_compression primary specification](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_meshopt_compression/README.md)
was rechecked and still says Release Candidate. It defines ATTRIBUTES v0/v1,
TRIANGLES/INDICES, all five filters, and parent-view decoded-length constraints.
[KHR_mesh_quantization](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_mesh_quantization/README.md)
is ratified; its normalized signed byte/short normals must be normalized after
normal-matrix transformation. KHR's maturity is an explicit compatibility limit,
not a claim of ratification or a reason to silently remove required metadata.
No extension policy/spec pin was changed.

The project package reference and cached nuspec agree on
Evergine.Bindings.MeshOptimizer `2026.10.1.20`; the nuspec identifies binding
source commit `2d5fe80c599106a869570096fad6a1441b6853d0`. The versioned upstream
[v1.3 header](https://github.com/zeux/meshoptimizer/blob/v1.3/src/meshoptimizer.h)
was rechecked and declares 1030. The raw binding.yml at that binding revision
could not be fetched, so the exact native upstream source correspondence is not
independently remote-verified in this audit. No codec/package update was made.

The public tests use 35 independently placed node primitives, 640 vertices and
960 indices from the Python-authored explicit fallback oracle. This tests signed
normal normalization, unsigned colors, interleaved offsets/strides, U16/U32
indices, UVs, and six rotation-key/midpoint/clamp times. Public resources must
have null native IModel and the ordinary ModelRenderer must own the actual
GltfGpuInstance/SceneCustomObject. A separately drawn literal scene uses the
same actual source materials to isolate geometry/attribute/material bindings;
that comparison does not establish independent sampler/PBR correctness.

The native capture recipe is a full-grid orthographic view at engine position
(1600,250,370), looking toward (0,250,370), height 1100 inches, near/far 1/3000,
192x192, black background, Normal camera mode, postprocessing enabled, the
fixture point light plus an owned camera-facing directional light (white*2).
This is inferred readable framing, not an exact upstream camera reconstruction.
The actual upstream screenshot was visually inspected; its 5x5 cubes and ten
labels establish visual context only. Final appearance approval belongs to the
user. Wall-clock animation cadence, Windows and matched vmdl performance remain
separate gates.

## Measured index-oracle correction, 7eb

Actual public source GPU readback was retained as
`Temp/gltf-validation/direct-gpu/MeshoptCubeTest/public/c1399fd8841146ed956c54be29e5ba1f/actual_gpu_rest.json`,
SHA256 `e7608000fbda673db461b0ae088d98250ed8bac4c0ca3add3d56266978e4e8df`.
The original exact-order assertion remained red in diagnostic receipt
`aa36983366ec2aeb2847b37aded9347ecd538b95f3cfa0f44981e869850abeb6`.
Independent stdlib Python parsing of the raw GPU byte arrays and declared output
fields checked all 640 vertices, colors, UVs, normals and tangent frames against
the pinned fallback data and literal rest transforms. Maximum position error was
0.00003556379181191005 inches; normal/UV error was zero, color error
0.00000005983838846645995, and tangent length/dot error zero.

The 320 actual GPU triangles classified as 248 exact, 72 cyclic, zero reversed,
zero changed. The first difference at node20, triangle ordinal1 is fallback
`(0,3,2)` versus actual `(2,0,3)`. The independent report retains every changed
ordinal and both triples; report SHA256
`58ec8d8f9e00943bca928b0597697914e38ae5e91ad4dbc4e971f47590cd4b05`.
The corrected checker accepts only `(a,b,c)`, `(b,c,a)`, `(c,a,b)` at that same
triangle ordinal. It never sorts indices/faces, changes source-vertex mapping,
changes production decoding/layout, or widens numerical/image thresholds.
Actual bound GPU reversed-winding and changed-index negatives are both required
and restored independently before subsequent checks.

## Canonical completion

The final model result uses exactly one canonical compiled asset from
`glTF-Meshopt`: `.glb_c` SHA256
`e820e3a683d872e728a9a7429ad5ed7f97adfa7c9da55b96a82b2de22329d60a`,
48,572bytes, ordinary conservative GLB2 compiler, required compression retained.
Its package contains ten PNG dependencies and upstream attribution, no source
`.gltf` or external geometry `.bin`. Distinct optional/required source behavior
and earlier receipts are retained without repeated variant execution.

Current actual canonical pose/index-negative and public playback tests both
pass. Five poses `.375,1,1.625,2,2.25` preserve all640 vertices/960indices. The
independent raw-GPU report maximums are position3.556379181191005e-05in,
normal5.960465181958564e-08, color5.983838846645995e-08, UV0, tangent length0,
and tangent-normal dot0. Real reversed-winding and changed-index negatives are
rejected and restored. Scene deletion stops callbacks and clears all pixels.

Ordinary `SkinnedModelRenderer` playback observes643 widget callbacks over
4.2016328seconds/2.1008164 authored loops. Maximum phase error6.70043944772658e-08s
stays inside explicit float entry/output and double arithmetic bounds. Full
callback gaps are passed to normal `Scene.EditorTick`; no per-frame seek or
helper player drives the clock. A separate paused public midpoint seek changes
2511 actual pixels. These are clock/final-pose observations, not visual FPS or
continuous per-frame independent geometry acceptance.

The canonical1024px oblique preview at engine(1600,1000,900), looking toward
(0,250,370), orthographic height1100, near/far1/4000, black background, native
Normal/postprocessing mode and owned camera-facing white*2 directional light
was visually inspected. All25 cubes/ten labels are present. The upstream image
uses unknown perspective/light/exposure; this recipe is inferred. Final visual
approval and independent PBR matching remain with the user. result.json carries
exact capture, package, source, producer and receipt hashes.
