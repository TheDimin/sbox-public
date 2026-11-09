# BoxTextured direct GPU validation

Status: tests and reference artifacts prepared; no BoxTextured test, GPU draw,
material image comparison or benchmark has been executed by this model owner.
The validation coordinator owns all compilation and editor execution. Prior
native `Model.Load` BoxTextured captures are historical evidence, not proof of
the new buffer renderer.

## Pinned inputs and license

Corpus: KhronosGroup/glTF-Sample-Assets at
`90d7ede14c7e280af263824604b427a1ca02cb66`, model `Models/BoxTextured`.
The model metadata identifies CC-BY-4.0 plus the Cesium logo/trademark notice;
keep `LICENSE.md`, `metadata.json` and the referenced legal-mark notice with the
validation corpus and preserve attribution in published comparisons.

Applicable upstream variants: `glTF`, `glTF-Embedded`, `glTF-Binary`, each original
and compiled with the actual `GltfCompiler` into valid GLB. No variant is excluded.
All three variants have the same authored geometry and root matrix; this is
checked from their individual source bytes, not assumed from the variant names.

| Input | SHA-256 |
| --- | --- |
| glTF/BoxTextured.gltf | `1e9003a4a2a8822ff60da529357bd8e4dec4a59b1a479017993e7e2ad5fcebef` |
| glTF-Embedded/BoxTextured.gltf | `879feb174dbf48e9e0cb22d2402ab35ff8519e13f0c5a100676788712dc3c8c7` |
| glTF-Binary/BoxTextured.glb | `b510eca2e2ef33f62f9ed57d6e7ce2d10ebb2bdebc4a8e59d347719ba81abdf4` |
| glTF/BoxTextured0.bin | `2e8c0483fa6665c686ec345f89dcbb2a694a587442584d09f8a83a59633327bc` |
| glTF/CesiumLogoFlat.png | `9c22b05c5b136d03c5621a8765e50a8322be6c35b9de53e9fe22685840d7f469` |

The source has one mesh/primitive, two nodes, 24 vertices, 36 U16 indices and no
animation/skin/morph targets. POSITION and NORMAL share a 576-byte bufferView at
stride 12, but POSITION begins at accessor offset 288 and NORMAL at zero. UVs use
another view with stride 8. Their S coordinates span 0..6; silently clamping them
to 0..1 would destroy intentional repeat behavior. Indices are in a view at
buffer offset 768 with accessor-relative offset zero.

The texture is an 8-bit indexed-color 256×256 PNG. The sampler explicitly asks for
`NEAREST_MIPMAP_LINEAR` minification (9986), `LINEAR` magnification (9729), and
REPEAT on both axes. The material has metallic factor zero and core default
roughness one, opaque alpha, one sRGB base-color texture and TEXCOORD_0.

## Tests written before renderer work

`BoxTexturedCoreTests.RunAll(checkoutRoot)` returns exactly **50** case records:
eight independent-oracle checks and seven groups across six source/compiled forms.
It runs in the coordinator's `Sandbox.Test.Unit` helper against the loaded real
`Sandbox.Gltf` and `Sandbox.Engine` assemblies. It does not compile substitute
producer types and does not allocate a native Model or Mesh.

| Group | Cases | Assertions |
| --- | ---: | --- |
| Independent reference controls | 8 | Wrong POSITION offset, asymmetric PNG origin/marker, repeat versus clamp, linear-light versus encoded mip average, RGB/RGBA PNG filter byte width, CRC corruption, linear bilinear filtering and clamp |
| Source descriptors | 6 | Shared view identity, exact offsets/strides, raw FLOAT formats, U16 indices and relative origins |
| Source values | 6 | All 24 positions, normals and UVs; all 36 indices, compared with an independent raw accessor reader |
| World placement | 6 | Literal authored root rotation, all vertex world positions, meter-to-inch cyclic basis and expected cube bounds |
| Material/image/sampler declarations | 6 | Core defaults, source links, explicit independent min/mag/mip and wrapping descriptors, original image bytes |
| Native texture resource bytes | 6 | Independent palette PNG decode versus complete generated RGBA8888 mip chain, linear-light mip average, sRGB/resource flags, material factors |
| Portable packaging | 6 | Valid GLB framing, geometry embedding, exact image bytes, explicit packaged relative dependencies, missing dependency errors, zero external I/O for embedded variants |
| Detached upload lifetime | 6 | Owned source bytes and shared storage remain valid after document disposal |

Portable compiled GLB is not synonymous with standalone export. The accepted
workflow allows separately packaged relative texture resources. `glTF` may retain
`CesiumLogoFlat.png` as an explicit packaged dependency; the embedded/Binary
variants must not acquire external image I/O. A missing required texture must
identify its relative URI. Arbitrary network dependencies are not accepted as a
portable package. Standalone export is outside this first milestone.

`PngTexelReference` is an independent test oracle using PNG chunks/CRC, zlib and
row filters. It does not use the production Skia importer. It supports bounded,
noninterlaced 8-bit RGB/RGBA/indexed-color PNG only; other precision/layouts are
explicitly unsupported by this oracle and are not exclusions from the engine's
full core requirements. TextureTransformTest can consume this frozen helper for
its own independent image expectations.

## Actual GPU and default material gates

`BoxTexturedGpuChecks.MeasureAll(checkoutRoot, world, borrowedComputeMaterial,
awaitCompletedFrame)` prepares **18 measured rows**: six forms times authored
rest, full affine shear, and reflected placement. It deliberately passes no
material resolver to `GltfGpuAsset.Create`; an untextured diagnostic override
would not validate this model's material workflow.

For every row require:

- A completed current pose revision, actual successful native draw callback,
  no callback errors, and source-derived input descriptors.
- Actual GPU position/normal/UV readback through the returned dynamic output
  fields; no fixed output stride or offsets in the checker.
- Actual GPU index readback, source topology and reflected winding correctness.
- Independent expected geometry, source UVs unchanged, full affine placement
  applied once, and finite outputs.
- The actual default material uses the stock complex shader; its color texture
  becomes resident through normal usage/real frames, and all 65,536 RGBA texels
  equal the independent PNG decode.
- Preserve the immediate residency observation and any initial failure. A later
  settled readback does not establish zero load overhead or compiled-vmdl parity.
- Correct metallic feature/factor and roughness scale/resource mapping.

Measurements do not pass numerical acceptance until explicitly reviewed
thresholds are supplied. Texture pixel readback proves resource decoding and
residency; it does not prove the shader samples sRGB, wraps or filters correctly.

`BoxTexturedImageChecks.ReferenceDraw` supplies independently decoded literal
`Graphics.Draw<Vertex>` geometry with the same borrowed actual material. The
coordinator's existing camera/target fixture must perform fresh empty-before,
positive-reference, deleted-reference-empty, actual-buffer-draw, and
deleted-actual-empty controls. Use the already proven Normal presentation with
normal postprocessing, owned light and exact camera target size. Retain failed
capture routes; never inject clears or force double-sided rendering to pass.

Compare geometry/UV images against the independent reference. A deliberate V
flip must produce a clearly different asymmetric logo. Record matched camera
state, actual dimensions, frame/time, material identity, loaded producer/helper
MVIDs, source hashes, shader identity and control results. A same-material
reference can share a sampler bug, so this is explicitly a geometry/UV binding
gate, not full material fidelity.

## Independent shader sampling gate still required

The inherited stock adapter records requested sampler states, but earlier native
sampling probes demonstrated that stock fixed sampler bindings may ignore them.
This remains a real pending acceptance item. No descriptor match, exact texture
bytes or same-material image comparison waives it.

For an independent final-image test, use CPU texture lookup expectations and
literal small stock constant-color patches at the same world positions/normals
under the same light/camera/exposure. Check asymmetric blue/green/white logo
landmarks and repeat tiles, using magnification or measured texture derivatives
so the expected filter/LOD is explicit. The actual model keeps its default
textured stock material. Keep reference-material construction, fresh empty
controls and patch interiors observable. `PngTexelReference.BilinearLinearRgba`
provides top-left-origin, sRGB-before-filter RGB/linear-alpha expectations for
base-level magnification, with explicit clamp or repeat addressing.

Minification 9986 additionally requires a measured nearest-within-mip/linear-mip
test; a magnification-only landmark check cannot establish it. Renderer lighting
differences may be reviewed visually, but incorrect core texture sampling is not
accepted as an artistic renderer difference.

## Remaining goal gates

- Actual execution and reviewed evidence for all rows above.
- Public `Model.Load` and ordinary `ModelRenderer` dispatch to the managed buffer
  backend without native Model/Mesh placeholders. The research helper alone does
  not meet that requirement.
- Real scene instance/lifetime/override behavior, safe reimport/editor acceptance,
  and unchanged vmdl behavior.
- Valid optimized portable `.glb_c`, dependency packaging, and matched load-time,
  process/GPU memory and package-size measurements against compiled vmdl.
- Linux visual validation, then Windows validation before full goal completion.

No first-milestone pass or native blocker is declared by this source preparation.
