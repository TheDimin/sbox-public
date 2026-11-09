# Material and texture acceptance inventory

This is a test inventory, not a claim that the material pipeline is complete.
The source/compiled reader tests establish preservation of data; GPU behavior,
editor workflows, platform packaging, and performance need independent evidence.
The portable `.glb_c` fixture used below is a packaging round trip, not proof of
GPU-optimized layout or build-pipeline integration.

## Existing reader and compiler coverage

- `GltfMaterialTests` checks glTF defaults, a combined MASK material, all five
  texture roles and transfer metadata, one transform, unlit, and variant mapping.
  `GltfMaterialEdgeTests` checks several malformed inputs and optional fallbacks.
  `GltfTextureTransformDeclarationTests` exercises declaration and fallback.
- `GltfImageTests`, `GltfReaderRobustnessTests`, and `GltfBasisFallbackTests`
  check encoded image ownership, source selection, errors and fallback. `GltfKtxTests`
  adds independent fixture transcoding, mip and alpha examples; it does not prove
  shader sampling or published runtime resource integration.
- `GltfDrawDataTests` covers independent UV sets, VEC3 vertex colors, and defaults.
  `GltfSamplerMappingTests` tests managed sampler descriptor mapping, not stock
  shader bindings or graphics output.
- `GltfCoreMaterialConformanceTests` adds shared-image role-local transform,
  color-space and sampler combinations; all six core minification modes with
  independent magnification/wrap axes; normalized VEC4 vertex alpha with MASK
  parameters; and out-of-range scalar/vector factors. Some cases compare source
  and compiled containers. These added tests are **authored, not yet run**.

The packed metallic-roughness image uses roughness from G and metallic from B;
occlusion uses R, base-color and emissive RGB use sRGB transfer, base-color alpha
is linear, and normal values are linear tangent-space data. Metadata alone cannot
establish those shader channel operations.

## Latest bounded editor baseline

`../../engine/Tools/Gltf/editor/editor_material_acceptance.csx` makes isolated mounted source and `.glb_c`
fixtures and calls the actual `Model.Load` path. The recorded run at
`Temp/gltf-validation/editor-material-acceptance/871a509bcf4f4ebe87beba60667ce5ee/receipt.json`
reported 6 of 26 loads passing: base-color, OPAQUE alpha and double-sided for
both forms passed their tested native parameter assertions. The other 20 were
rejected by the then-loaded loader. The receipt captures the loaded assembly
identities; it is a point-in-time integration baseline, not a result for later
worktree changes. It does not render or verify per-instance variant behavior.

## Required actual engine and editor tests still open

For each applicable case use the actual public loader and stock shader path,
source and packaged form, with independently validated fixtures and a reference
render. Record loaded assembly identities, fixture hashes, captures, control
images, backend, platform, and cleanup. A diagnostic native parameter is useful
but does not by itself satisfy a pixel or material-behavior check.

- **Factors and alpha:** nonwhite base color; nondefault metallic and roughness;
  emissive factor; OPAQUE ignores alpha; MASK around cutoff on both sides of the
  threshold; BLEND preserves partial alpha and its visual compositing. Include
  VEC3 and VEC4 vertex colors, factor/texture/vertex multiplication, and
  double-sided front/back lighting and normal orientation.
- **Texture channels and color:** all five roles independently and combined;
  vary individual RGBA channels with controls to establish G/B/R routing,
  sRGB versus linear interpretation and linear alpha. Exercise normal scale,
  occlusion strength, emissive factor, and a single image used in both sRGB and
  linear roles without incorrectly sharing their runtime representation.
- **Coordinates and sampling:** at least two genuinely different UV sets;
  per-role KHR_texture_transform scale, rotation, offset and texCoord override;
  repeat, mirrored repeat and clamp on both axes; both magnification filters,
  every minification filter and actual mip levels. Check explicit and unspecified
  samplers separately, including base-level-only behavior and source/compiled parity.
- **Core image forms:** PNG and JPEG in external URI, data URI and bufferView;
  RGB/RGBA and alpha where applicable, supported bit depths, orientation, repeated
  loads and unloads. Verify dependency packaging and byte/pixel integrity without
  fallback to the original source tree.
- **Selected extensions:** unlit must preserve applicable base-color/alpha and
  ignore lighting as specified. Variants must retain authored defaults and switch
  one standard active variant per model instance across multiple material slots;
  two simultaneous instances must remain independent and resetting a variant must
  restore defaults. Exercise supported, required-unsupported and optional-fallback
  declarations. Basis/KTX needs actual ETC1S and UASTC loads, alpha/mip/color-space
  fidelity and platform resource checks in addition to codec tests.
- **Overrides and editing:** verify per-instance and per-slot/variant overrides,
  removal restores the underlying authored material, unaffected instances stay
  unchanged, and vmdl behavior remains unchanged. The precedence rule still needs
  to follow and be checked against established native behavior. Cover authoring,
  save/reopen, undo/redo, geometry reimport with preserved material assignments,
  external edits and conflicts, and shared-material workflows once specified.
- **Corpora and targets:** each applicable core-only and selected-extension
  Khronos model and variant needs a separately identified actual load and visual
  result. Record upstream defects and exclusions without treating them as passes.
  Run Linux first and Windows before declaring cross-platform completion. Measure
  optimized `.glb_c` against equivalent compiled vmdl under the agreed protocol.
