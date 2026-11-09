# Plan: transmission, volume, IOR, dispersion and clearcoat (2026-11-09)

## Goal
Make these models load and look like the Sample Viewer reference renders:

| Model | Extensions used (all optional unless marked) |
|---|---|
| ClearCoatTest, ClearcoatWicker | `KHR_materials_clearcoat` |
| TransmissionTest, ChronographWatch | `KHR_materials_transmission` |
| CommercialRefrigerator | `KHR_materials_transmission` (**required**), `KHR_materials_clearcoat` |
| DragonAttenuation | `KHR_materials_transmission` + `KHR_materials_volume` (thickness texture, attenuation colour and distance) |
| DispersionTest, DragonDispersion | `KHR_materials_transmission` + `KHR_materials_volume` + `KHR_materials_ior` + `KHR_materials_dispersion` |

Order matters: volume, IOR and dispersion change only how transmitted light bends and is absorbed. Each one needs transmission first.

## Status of clearcoat (this session)
Implemented, not yet verified on screen:
- **Reader:** `GltfClearcoat` (`GltfContracts.cs`, `GltfDocument.ClearcoatAnisotropy.cs`). The coat normal map has its own scale. Coat images are retained in `GltfDocument.MaterialImages.cs`.
- **Roles:** `Clearcoat` (R), `ClearcoatRoughness` (G) and `ClearcoatNormal` (RGB), all linear, in the UV and encoding plans.
- **Adapter:** factors and texture flags in `GltfStockMaterialAdapter`. Feature `F_GLTF_CLEARCOAT` in the material key and template.
- **Shader** (`gltf_surface_five_uv.shader`): a second lighting pass with the coat normal, coat roughness, F0 0.04 and no diffuse. It reuses the stock `ComputeDirectLighting` and `CalculateIndirectLighting`, and composites `base * (1 - coat * Fcoat) + coat * coatSpecular`.
- `KHR_materials_clearcoat` is in `GltfModelLoader.SupportedExtensions`.
- Open: verify against the reference with `diff_reference_capture.py` after the shader compiles. The diffuse-transmission shader has no coat lobe. A material with both drops the coat. No corpus model needs both.

## Step 1: `KHR_materials_transmission` (largest gain; unlocks 3 required-extension cases)
**Mechanism (Khronos):** transmissive surfaces show the opaque scene behind them. The renderer renders opaque objects first, copies the colour buffer with mips, then samples that copy for transmissive pixels. The mip level is set by roughness: `lod = log2(width) * applyIorToRoughness(roughness, ior)`. The transmitted colour is multiplied by base colour and replaces the diffuse lobe by `transmission` (texture R × factor). Specular reflection stays.

**s&box already has the parts:**
- `glass.shader` samples `g_tFrameBufferCopyTexture` (attribute `FrameBufferCopyTexture`) with `SampleLevel( ..., sqrt(roughness) * 7 mips )`.
- It reconstructs the refraction point from depth (`RecoverWorldPosFromProjectedDepthAndRay`). That is the same "refraction in screen space" idea as the Khronos renderer.

**Work:**
1. **Reader:** `GltfTransmission { Factor, Texture }` and role `Transmission` (R, linear). Retain the image in `MaterialImages.cs`.
2. **Shader:** new feature `F_GLTF_TRANSMISSION` in `gltf_surface_five_uv.shader`.
   - The material must render in the translucent pass after opaques, so the framebuffer copy exists. Use `F_TRANSLUCENT` render state with premultiplied output, as `glass.shader` does. Check which render-state or attribute requests the framebuffer copy in `glass.shader` and copy that exactly.
   - Thin-walled (no volume): the refraction ray is the view ray, which means no offset.
   - Colour: `mix(diffuse, transmitted * baseColor, transmission) + specular`. The specular uses the normal Fresnel. Do not run the surface's diffuse term on the transmitted part.
3. **Adapter:** factor, texture flag, feature key and template key. Add `"KHR_materials_transmission"` to `SupportedExtensions` only when step 2 renders correctly.
4. **Known limit to accept:** a screen-space copy cannot show transmissive objects behind other transmissive objects. The Khronos renderer has the same limit; TransmissionOrderTest shows it.

**Test set:** TransmissionTest, TransmissionRoughnessTest, TransmissionThinwallTestGrid, CompareTransmission, ChronographWatch, CommercialRefrigerator.

## Step 2: `KHR_materials_volume` (DragonAttenuation)
**Mechanism:** the surface is the boundary of a solid. The refracted ray travels `thickness` (factor × texture G, in model units, scaled by the node scale) through the volume, and light is absorbed by Beer's law: `T = attenuationColor ^ (distance / attenuationDistance)`.

**Work:**
1. **Reader:** `GltfVolume { ThicknessFactor, ThicknessTexture (G), AttenuationDistance (default +inf), AttenuationColor }` and role `Thickness`.
2. **Shader:** refract the view ray with IOR (default 1.5). Offset the sample point by `refract(-v, n, 1/ior) * thickness * modelScale`, project it to screen, and sample the framebuffer copy there, instead of the thin-walled path.
3. Multiply by the Beer's-law transmittance. The model scale comes from the object-to-world matrix length. Pass it per draw, or compute it from the instance transform in the vertex shader.

**Test set:** DragonAttenuation, AttenuationTest, CompareVolume, MosquitoInAmber.

## Step 3: `KHR_materials_ior` (small)
- **Reader:** `Ior`, default 1.5. A value of 0 means infinite IOR (allowed).
- **Shader:** `F0 = ((ior - 1) / (ior + 1))^2` replaces the 0.04 dielectric F0 in the metal/rough conversion. Use the same IOR in the volume refraction (step 2) and in the transmission roughness scale.
- This also changes opaque dielectrics, so CompareIor and IORTestGrid test it without transmission.

## Step 4: `KHR_materials_dispersion` (DispersionTest, DragonDispersion)
**Mechanism (Khronos):** the IOR differs per colour channel. `halfSpread = (ior - 1) * 0.025 * dispersion`, and the R, G and B IORs are `ior - halfSpread`, `ior`, `ior + halfSpread`. The renderer refracts and samples the framebuffer copy three times, once per channel.

**Work:** the reader reads `Dispersion`. In the volume path, the shader refracts three rays and takes `.r`, `.g` and `.b` from the three samples. It applies only with volume (thickness > 0), as in the spec.

**Test set:** DispersionTest, DragonDispersion, CompareDispersion.

## Shared prerequisites
- **IBL and tone map:** the HDRI lighting (Cannon Exterior cubemap) and the Khronos PBR Neutral tone map are in place in `reference-recipes.json`. Glass is judged mostly by reflection and refraction of the environment. Without IBL these comparisons mean nothing.
- **Sky in the framebuffer copy:** the visible sky must render before the copy, or transmitted pixels show the clear colour.
- **Verification loop for every step:**
  ```
  gltf_corpus_run "model=<test set> out=Temp/gltf-validation/exp-<step>"
  python3 engine/Tools/Gltf/scripts/diff_reference_capture.py research/gltf-validation/material-diff-<date> <cases>
  ```
  Look at the sheets, then put the user verdict on the compare site.

## Estimated effort (my estimate, medium confidence)
| Step | Effort |
|---|---|
| Clearcoat verification and fixes | 0.5 day |
| Transmission (thin-walled) | 1 to 2 days. The render pass and framebuffer-copy hookup is the risk. |
| Volume | 1 day |
| IOR | 0.25 day |
| Dispersion | 0.5 day |
