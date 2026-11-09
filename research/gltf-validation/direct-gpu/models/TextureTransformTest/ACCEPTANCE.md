# TextureTransformTest acceptance

Status: the stage 901c legacy public UV correction passed its 18-case CPU and
native regression. It proves transformed UV transport and matched-material
image sensitivity for the pinned source and derived compiled form. It does
not establish independent sampler fidelity or resolve the user's visual
rejection: the small native diagnostic image still shows repeated arrows and
the first arrow appears closer to the red marker than the green marker in the
pinned reference. The cause is unproven. A canonical matched capture and user
review, plus independent clamp/filter sampling evidence, remain required.

## Stage 901c public UV regression — 18 cases passed

Receipt `Temp/gltf-validation/direct-gpu/model-tests/615012a1018f41879b30c85adc24633b/receipt.json`,
SHA-256 `fa94049fa9818d1405233a19c13cf23401423e2881ff16909d8102f21919745f`,
reports 18 passed, zero failed or incomplete, and no cleanup errors. The loaded
Engine MVID was `881eb8b1-3500-40d7-98eb-25f21758fc6c`, and glTF MVID was
`c99cbed5-9108-4caa-a331-6b2a1f589bb2`. Request SHA-256 was
`74e028bf9cc9bb97bc965148926a03dc2361c9e39ff0b9fea198ab0e2d680e23`.
The earlier helper compilation failure remains preserved separately; it ran no
cases and was corrected by the subsequent test-fixture-only change.

The 14 synthetic CPU cases cover offsets, positive and negative rotation,
negative scales, texCoord override and optional-extension fallback, interleaved
normalized and sparse input, incompatible role-map rejection, shared descriptor
routes, morph boundaries and identity-map preservation. Two pinned CPU cases
exercise the original `.gltf` and its derived compiled GLB. Two native cases
use public `Model.Load` and `ModelRenderer`, with actual GPU buffer readback,
identity/reference and no-transform controls, override and restoration, and
fresh empty/deleted captures.

Both forms returned 12 primitive rows / 72 output vertices, finite values and
correct winding. The maximum transformed-UV error was `6.4333164e-8`; raw
override UV error was zero, and clearing the override restored the transformed
result. The deliberately raw negative differed by `0.77628505` UV units. The
position maximum was `4.2649613e-6` engine inches and normal error was zero.
The actual 192×192 images matched the independently constructed transformed-UV
reference exactly; the no-transform control differed in 9,182 pixels with mean
absolute RGB error `0.0939362`. Both routes use stock materials and sampler
bindings, so this is not independent sampler or colour-transfer proof.

The source and compiled default PNGs share SHA-256
`e9c4f79f22e44ad0d2fd1bc207c469d48a8c35ad47c09cb9b961d586058f066c`; their
missing-transform controls share
`bbe4f4e98a2a707987f6b5c1de1986b446b0d1f1bda14a74cb638b71bee586a0`. The
source capture is
`Temp/gltf-validation/direct-gpu/public-model-smokes/TextureTransformTest/4089ee9d571a454c98946a48e818b906/TextureTransform_public_source_default.png`;
the compiled capture is
`Temp/gltf-validation/direct-gpu/public-model-smokes/TextureTransformTest/0c00c2fd0e414efbb997dfaaba86c954/TextureTransform_public_compiled_default.png`.
These diagnostic captures do not replace the user's canonical review image.

## Prior CPU/data/resource evidence

Inspected evidence: `Temp/gltf-validation/direct-gpu/material-core-first-186/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA-256 `0da83949e97504dcc493158bf7f912d928b288827ebf3b5f8653ff35c74d59d2`.
The selected rows are 17 core + nine texture-resource + two coordinate-selection
cases, all passing with the exact frozen source hashes. Loaded Engine MVID was
`04b86c2e-4406-422e-a466-b61ec13f1f86`; Gltf MVID was
`1de213d3-1748-43d1-9a16-92a13bc9277d`. The combined 186-case batch was **not**
globally green: six other model cases failed. These 28 rows do not prove GPU UV
mapping or correct native sampler binding.

## Inputs and version policy

The corpus is `glTF-Sample-Assets` revision `90d7ede14c7e280af263824604b427a1ca02cb66`. This model supplies **one** upstream container variant, `glTF/TextureTransformTest.gltf`, plus one external 136-byte buffer and five PNGs. Test both that source and its losslessly compiled valid GLB. Do not invent an upstream binary/embedded variant or count TextureTransformMultiTest as this model.

The model has 12 node instances, nine mesh/material bindings and six primary demonstration quads. Its remaining node instances are correctness/error markers. Every primitive has four POSITION/TEXCOORD vertices and six UNSIGNED_INT indices; no NORMAL, animation or skin is authored. Its two UV accessors contain a unit quad and a half-size quad. BufferView origins are 0, 48, 80 and 112 bytes; upload offsets must remain relative to their returned owned storage. Missing normals require generated flat shading.

All seven input hashes are fixed in `TextureTransformReference.Inputs`; the corpus manifest revision is also checked. Model content is CC0-1.0; metadata/license documentation is CC-BY-4.0, as the pinned model LICENSE.md states. Keep the fetched corpus and its license files out of the engine source payload.

The reviewed primary extension sources are the [published-baseline README at glTF revision 8e798b02](https://github.com/KhronosGroup/glTF/blob/8e798b02/extensions/2.0/Khronos/KHR_texture_transform/README.md) and its [schema](https://github.com/KhronosGroup/glTF/blob/8e798b02/extensions/2.0/Khronos/KHR_texture_transform/schema/KHR_texture_transform.textureInfo.schema.json). This revision marks the extension Complete; the [currently reviewed upstream README](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_texture_transform) marks it ratified. There is no extension-specific numeric version field. Earlier tests followed its GLSL example; the rotation convention chosen after the actual sample capture is documented below and differs from that example.

## CPU/data checks — 17 cases passed

`TextureTransformCoreTests` has 11 methods and 12 explicit DataRows, yielding 17 cases. The coordinator compiles it and `../../../../../engine/Tools/Gltf/direct-gpu/models/TextureTransformTest/TextureTransformReference.cs` against the actually loaded producer and sets static `CheckoutRoot`.

- All material offsets, scales and rotations match independent double-precision affine arithmetic on vertices and off-grid UV probes, in source and compiled forms.
- All 12 node world positions match literal independent parent/scale arithmetic.
- Source streams retain formats, actual view boundaries and U32 topology. Compiled packaging embeds geometry, preserves independently managed relative PNG dependencies and exact resolved PNG bytes, reports a missing texture by name and retains the extension. A portable GLB does not require standalone export; standalone export remains deferred.
- Source texture roles preserve sRGB tagging, clamp addressing and LINEAR minification/magnification without mipmapping. Reading these properties does not prove shader sampling.
- Optional unsupported transforms have their defined core fallback reported separately. A required unsupported transform is rejected by name. Neither fallback is a selected-extension render pass.
- A derivative of this model uses both UV sets and distinct base-color/MR/normal/AO/emissive transforms. This tests per-texture storage, texCoord override precedence, normal scale and AO strength; it does not claim that all those shader features already work.
- Negative scales/unbounded offsets remain valid. Independent negative controls distinguish wrong transform order, rotation sign and vertical-origin flipping.

Expected namespace/type: `Sandbox.Gltf.DirectGpu.TextureTransformTestValidation.TextureTransformCoreTests`.

After the restart, `TextureTransformCoordinateSelectionTests` added a separate
two-case source/compiled freeze without changing those original 17 cases. Both
cases passed in the receipt above. The suite
checks the actual distinct bytes of the per-role fixture's half-size UV0 and
unit-size UV1, then demonstrates that always selecting UV0 or one global UV1
produces independently distinguishable wrong material coordinates. These are
CPU/data sensitivity checks; GPU and image selection still require execution.

The [material/UV boundary](MATERIAL_UV_DESIGN.md) records the audited stock and
ShaderGraph limits, approved five-role UV plan, current implementation ownership
and remaining source integration. Source approval does not constitute runtime
capability evidence.

## GPU readback checks — unrun

`TextureTransformGpuChecks.MeasureCurrent` requires a real current submitted revision, positive draw calls and no callback errors. It reads fields using the returned runtime offsets/stride/components and maps flat corners through SourceVertices. It compares all 12 node primitives against independent positions, normals, UVs and winding, including full affine placement/reflection. Numerical limits must be supplied after measured error review; obtaining an error value alone does not pass acceptance.

The coordinator must explicitly declare whether output UV0 is raw source data or a baked base-color mapping. A raw-source UV pass proves transport only. An implementation that retains raw coordinates and applies the extension in native shader/material properties is valid; this test must not force baking. Either implementation still needs final rendered-image proof that the material uses the intended per-texture mapping.

Four `TextureTransformGpuVerifierTests` exercise the checker with synthetic diagnostic bytes: poisoned padding/nonstandard field offsets, incorrect UV/mapping declarations, NaN positions/reversed winding, and missing nodes/incorrect normals. These are verifier unit tests, not GPU samples.

## Image transform checks — six prepared rows, unrun

`TextureTransformImageProbe.MeasureAll(checkoutRoot, world, borrowedComputeMaterial, captureCompletedNativeFrame, outputUvRepresentation)` covers two source/compiled forms and identity, affine shear and reflected placement. It creates the actual asset with its **default** stock-material path; no resolver override substitutes for those actual materials.

The reference uses literal `Graphics.Draw<Vertex>` triangles and independently transformed UVs. It borrows separately acquired identity-mapping source-derived materials while their reference asset leases remain alive, so an actual shader applying the authored affine map cannot transform these reference coordinates twice. Material realization calls `GetMaterials()` without creating a reference GPU geometry instance. It constructs no Model or Mesh. For each row the capture sequence is fresh empty, reference, identical static reference repeat, reference deleted/empty, actual, actual deleted/empty. Identity also adds a deliberately omitted-transform reference, which must visibly differ. Callback counters must stop after deletion. Captures must come from the previously validated Normal/native-postprocessing/owned-light/exact-camera-size route; record actual dimensions, camera/mode/light/producer/shader hashes and monotonic timestamps in the receipt. The capture delegate must return on the engine main thread after a completed submission and verified texture residency.

Rows report RGB foreground IoU and RGB difference measurements. A black textured pixel is not geometrically empty, so this metric is not labeled a true geometry silhouette. Final appearance requires visual review and the applicable independent geometry controls. Repeat instability invalidates quantitative comparison; diagnose the capture route instead of widening tolerances.

**Sharing materials between actual/reference isolates transform and geometry agreement, but does not independently validate sampler behavior.** Both paths could share the same wrong native sampler. An image-comparison success cannot erase the known stock sampler failures.

## Independent sampler/material gates — pending

The main two source images use CLAMP_TO_EDGE, LINEAR and no mipmaps. Their gray borders and transformed coordinates outside [0,1] deliberately distinguish clamp from wrap. UV.png is RGBA8 256×256; Arrow.png is RGBA8 128×128. The three marker images are RGB8 128×128. No vertical flip should be inferred from the engine's world coordinate conversion.

Coordinate with the BoxTextured owner's independently decoded PNG/bilinear reference; reuse its frozen source rather than another duplicate decoder. Filter decoded linear RGB, preserve linear alpha, and evaluate clamp-to-edge texel centers. Constant-color stock reference patches under identical placement/normal/lighting can isolate expected sampled texels from shader-lighting transfer. Final protocol and actual evidence remain pending. The existing CLAMP/MIRROR/NEAREST stock failures remain open until this independent gate passes.

`TextureTransformTextureResourceTests` adds nine checks using that Box-owned PNG decoder and native-resource header reader; all nine passed in the inspected receipt above. All five source images serialized to exact straight RGBA bytes/top-left rows, both primary stock-material textures preserved one mip and requested clamp/linear descriptors, and both source images independently distinguished outside-UV clamp from wrap. Descriptor/resource success remains separate from actual shader behavior. Setup static `CheckoutRoot` as for the core suite and include the frozen Box reference/PNG helper sources.

`TextureTransformSamplingReference.Build` prepares 96 independently filtered material/UV landmarks, including outside-unit-square and wrong-wrap sensitivity tags. Its values are linear RGB/alpha before lighting, exposure and tone mapping. Comparing those values directly with final display RGB would be invalid; the constant-color transfer controls must establish that mapping first.

Before stage 901c, the direct GPU shader forwarded raw UVs and the stock adapter
had no per-texture mapping state. The new legacy path now bakes a compatible
base-colour/metallic-roughness mapping into a separate UV0 route and preserves
raw coordinates for material overrides; see the public regression above. The
older native-model primitive builder remains historical evidence only. Full
independent role mappings, independent sampler acceptance, canonical user review,
compiled load/memory parity and Windows proof remain separate requirements.

## Approved production UV plan — implemented source, 18 tests pending

The new `engine/Sandbox.Engine/Resources/Model/Gltf/Materials/GltfMaterialUvPlan.cs`
contains one shared internal five-role enum, immutable per-role bindings to the
exact `GltfUploadStream`, authored UV properties and declared affine rows. It
never bakes source coordinates, canonicalizes their formats, resolves an image or
allocates a native/GPU resource. `CreateForRoles` supports base-color-only unlit
consumption without validating ignored texture-role UVs. `CreateForVariant`
returns a completed new plan, preserving prior plans and shared source bytes.

`engine/Tests/Sandbox.Test.Unit/Resources/GltfMaterialUvPlanTests.cs` was written
before this implementation and adds 18 focused cases: independent roles,
normalized/interleaved offsets, signed quantized and sparse UV descriptors,
affine ordering/negative scales, omitted/ignored roles, role/index errors,
source lifetime/immutability and variant fallback/failure preparation. They must
run against an actually staged new Engine assembly; copying producer source into
a helper is not evidence.

Detached upload data has no document-owner token. The factory's caller must
enforce the owning geometry's document/mesh/primitive identity and exact source
stream references. The planned backend integration must reject a same-shaped
foreign upload before committing selection. Matching accessor IDs alone is not
an ownership check. A UV plan is per primitive/material pair, so it cannot be
stored as the only value of a material-index-only shared cache. The variant
owner and Fox are coordinating an outer immutable draw snapshot that commits
native material resources and per-slot UV projections together.

The corrected image reference borrows separately prepared identity-mapping
source-derived materials and independently bakes UVs, avoiding a double transform
when the actual shader applies its affine rows. This correction remains unrun.
Feeding raw UVs through the same possibly wrong transform shader cannot provide
an independent transform oracle. The independent PNG/sampling gate remains open.

## Identity-reference and native binding prerequisites — unrun

`TextureTransformIdentityMaterialReference.CreateDocument` builds a model-local
derivative of the pinned source solely for acquiring reference materials. It
removes the six base-color affine extensions and their unused declaration while
retaining source factors, texture/sampler references and dependencies. The literal
reference geometry supplies independently baked UVs. It does not alter the
artist's source or substitute an identity mapping into the actual asset.

Four `TextureTransformIdentityMaterialTests` cases cover source and derived GLB:
exact image bytes, material factors, geometry and sampler declarations survive;
the reference material mapping is identity; off-grid landmarks detect missing
and double-applied transforms. They are reader/fixture prerequisites, not native
rendering evidence. The coordinator released the unrun probe correction, which
now calls `GetMaterials()` before borrowing lazily realized reference materials.
GPU checker/probe classes are internal with public callable entry methods so
they can consume the extracted backend's internal types without widening the
engine's public API.

The literal reference also explicitly merges each material's typed attributes
and native view attributes before `Graphics.Draw`, matching the raw backend's
material-before-view ordering. `Graphics.DrawInternal` does not perform that
material-attribute merge itself. This correction is required to carry named
per-role sampler states into the reference draw and remains unrun; it does not
make a shared-sampler image an independent sampling oracle.

Eight `TextureTransformMaterialBindingTests` cases target the explicit provided
Surface/Lit candidate after its identified template is compiled and the actual
Engine is staged. Set static `CheckoutRoot` and `BorrowedTemplate`, or call
`RunAll(checkoutRoot, borrowedTemplate)` for the same eight assertion labels. The
coordinator supplies the actual reviewed anonymous compiled Material; this model
does not compile it or invent a filename lookup for it. These tests check native
affine-row/selector readback against independent model
expectations, UV1 versus UV0 material identity, resource sharing for equal maps,
separation of the six affine maps from identity references, lease retention and
rejection of a mismatched plan without changing a live material. Readable native
properties do not prove that a shader consumes them. The candidate is separate
from the default material path until its actual GPU gate passes and Fox selects
it deliberately; canonical default-path visual and independent sampling
acceptance remain open.

## Public sampler regression baseline

The stage `901c53b657a44bc7874626b58a0b2fa0` public `Model.Load` sampler
probe used a literal 2x2 red/green/blue/yellow PNG and separate in-range
controls. Both source glTF and derived compiled GLB rendered the in-range
red/green U and red/blue V controls correctly and passed repeat controls. Clamp
at 1.25 and mirror at 1.25 and 1.75 produced repeat samples on both axes: six
failures per form, each with maximum RGB error 1. The two captures are identical
(SHA-256 `ea1e851dff8e4a1af5d3428c6e1b1d50e0720194ddccbb804ba30cb2d40c9c20`);
receipt `Temp/gltf-validation/direct-gpu/model-tests/286c73c061614a969bb4c79150b7cf7f/receipt.json`
records complete execution and no cleanup errors. The literal PNG checksum and
pixel data were independently checked. This establishes a public wrap defect;
point samples do not establish mip, linear-filter, or material-lighting parity.

A glTF-specific stock-lighting wrapper and default-material route are staged in
source to expose named material samplers. They are not compiled or accepted by
this baseline. The route needs its matching shader and must pass the same source
and compiled public grid, TextureTransform image review, and colored/uncolored,
metallic-roughness, double-sided, depth, override and stock-material controls.

## Rotation convention discrepancy

The stage d0 canonical image passed the separate wrap controls but the rotation
arrow pointed to the red opposite-rotation marker; the combined-transform arrow
also missed the green marker. The pinned source's UVs put (0,0) at the top left.
The [Khronos Sample Renderer at revision 93d0a94da6728ba36090f2526f212e11c26183aa](https://github.com/KhronosGroup/glTF-Sample-Renderer/blob/93d0a94da6728ba36090f2526f212e11c26183aa/source/gltf/material.js#L116-L155)
constructs its column-major rotation with opposite sine signs to the published
extension README GLSL example. Its [array conversion](https://github.com/KhronosGroup/glTF-Sample-Renderer/blob/93d0a94da6728ba36090f2526f212e11c26183aa/source/gltf/utils.js),
[matrix upload](https://github.com/KhronosGroup/glTF-Sample-Renderer/blob/93d0a94da6728ba36090f2526f212e11c26183aa/source/Renderer/shader.js),
and [shader multiplication](https://github.com/KhronosGroup/glTF-Sample-Renderer/blob/93d0a94da6728ba36090f2526f212e11c26183aa/source/Renderer/shaders/textures.glsl)
confirm the effective sign. The upstream mismatch remains discussed in
[Khronos issue 1563](https://github.com/KhronosGroup/glTF/issues/1563).

This implementation chooses the Sample Renderer's general convention, without
asset-specific handling, to match the sample and user feedback. It does not
claim conformance to the opposing literal README GLSL example. The independent
positive/negative quarter-turn, nonuniform-scale and offset landmark tests retain
the opposite-sign matrix as an explicit negative control. Source change and
new-stage runtime acceptance must be recorded separately from the d0 baseline.

## Coherent-stage validation

On the coherent 9b9f stage (Engine SHA-256
`ad6a78a7e4f2664fe1f74b1567dde6d15d1519ad78dbd1e21efbb21ebfa2ebd1`,
Gltf SHA-256 `b86cb72e0c0e0f3c23af45bd115f68760655d1754d663f41df62a45e136f9e8b`),
the frozen request completed 92/92 cases with no failed, incomplete or cleanup
errors: `Temp/gltf-validation/direct-gpu/model-tests/253d14da76954d68b5865b32506efbe9/receipt.json`
(SHA-256 `563a7729aef8aff03997c23a2303ac178874daaf04926d0b3d11044fb8e1f934`).
The initial expanded request failed to compile three old auxiliary fixture raw
strings; its 0/92 receipt is retained at
`Temp/gltf-validation/direct-gpu/model-tests/1ccd026128d045d780c13d2bf1d05a6a/receipt.json`.
The successful request used corrected fixture delimiters.

In both original source and derived compiled forms, the actual public renderer
matched independent UV positions within `6.44e-8`, and native overrides read
raw UVs with zero observed error. Separate literal-image point-sampler grids
passed repeat, clamp, mirror and differing S/T modes with zero observed color
error on both axes. The suite also passed signed rotation and nonuniform scale,
retained interleaved/normalized/sparse streams, generated tangent route, flat
front/back and depth, and a stock vmdl positive/deletion control. These checks
are bounded; they do not establish all filtering, mapped-normal, five-role
material, lighting or full glTF conformance.

The isolated 512x375 source capture is
`Temp/gltf-validation/direct-gpu/coherent9b9f-texture-transform-canonical/case/capture.png`
(SHA-256 `f4977fb2663c9474417372a46ccfb0e2eb8c9e478f23b0762716a9ecd7e1f325`).
The authored-marker image check passed all three bottom quads: nearest black
arrow pixels were 18.21, 12.79 and 11.89 pixels from the green-marker centers.
The rotation arrow was 87.60 pixels from red and 50.81 from yellow; the scale
arrow was 59.40 pixels from yellow. The checker and its image-space thresholds
are recorded in `../../../../../engine/Tools/Gltf/direct-gpu/models/TextureTransformTest/check_arrow_markers.py` and `arrow-check.json`. The source and
compiled public-renderer captures also appear in the suite receipt. User visual
approval remains pending.
