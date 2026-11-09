# Clearcoat and anisotropy: Sample Viewer compared with s&box (2026-11-09)

## Scope

| Model (glTF-Binary) | Extension in file | Required? |
|---|---|---|
| AnisotropyStrengthTest | `KHR_materials_anisotropy` (strength 0 to 1) | No |
| AnisotropyRotationTest | `KHR_materials_anisotropy` (strength 0.5, rotation) | No |
| AnisotropyDiscTest | `KHR_materials_anisotropy` (anisotropyTexture, strength 1) | No |
| ClearCoatTest | `KHR_materials_clearcoat` (factor, roughness, texture, coat normal) | No |
| ClearcoatWicker | `KHR_materials_clearcoat` (factor 1, roughness 0.1, coat normal map) | No |

- **Reference:** `Temp/gltf-reference/rendered/<case>/reference.png`. It is rendered by `@khronosgroup/gltf-viewer` 1.1.0 with the Cannon Exterior HDRI, IBL only, exposure 1, Khronos PBR Neutral tone map, and the recipe camera.
- **s&box capture:** `Temp/gltf-validation/current/assets/<case>/capture.png`, captured 2026-11-09 12:51 to 12:54. This is the **old presentation**: one directional light at intensity 2, the engine default ambient light, no IBL, and a flat gray background.
- **Sheets:** each sheet in this folder shows three panels: reference, capture, and |difference| × 4.
- **Metrics:** `metrics.json`. The tool is `engine/Tools/Gltf/scripts/diff_reference_capture.py`. The reference is downscaled to the capture size before the comparison.

s&box loads all five models, because both extensions are optional in these files. s&box ignores the extension data and renders the core metal/rough material only.

## Measured differences (foreground)

| Case | MAE (sRGB 0..1) | Linear luma, capture / reference | Clipped pixels, ref → s&box | Colour saturation, ref → s&box |
|---|---|---|---|---|
| AnisotropyStrengthTest | 0.268 | 0.52 | 0.0% → 5.2% | 0.022 → 0.000 |
| AnisotropyRotationTest | 0.313 | 0.43 | 0.0% → 6.3% | 0.022 → 0.000 |
| AnisotropyDiscTest | 0.239 | 1.42 | 0.0% → **61.4%** | 0.063 → 0.000 |
| ClearCoatTest | 0.096 | 1.14 | 0.2% → 1.9% | 0.083 → 0.039 |
| ClearcoatWicker | 0.114 | 1.26 | 0.0% → 0.2% | 0.088 → 0.067 |

The foreground mask includes the background areas that differ: gray in s&box, the HDRI in the viewer. Because of that, the silhouette IoU values in `metrics.json` are low and are not meaningful.

## What the sheets show

1. **Metals are black, with one hot spot.** All anisotropy spheres and plates in s&box are black, with one round, centred highlight. The reference shows the sky and the ground reflected, stretched by the anisotropy. Cause: these materials are metallic (metallicFactor 1). Without an environment, a metal reflects only the single directional light. **No IBL is the largest error in all five models.**
2. **Rough metals saturate to white.** In AnisotropyDiscTest every tile with roughness 0.1 to 0.8 is pure white (61% of pixels clipped), and the roughness 0 tile is black. The light hits the plates almost head-on at intensity 2. With the Linear tone map, everything above 1.0 clips, so the roughness and anisotropy detail is lost. In the reference, the tone mapper keeps the brushed-disc pattern visible up to roughness 0.4.
3. **No anisotropic stretching.** Every s&box highlight is round, and strength, rotation and the anisotropy texture all have no effect. The highlights grow only with roughness (AnisotropyStrengthTest columns). Rows that should differ, such as strength 0 against 1, or rotation against no rotation, are identical.
4. **No clear-coat layer.** In ClearCoatTest the "Coating Only" column is almost black in s&box. The reference shows a sharp reflective coat over a dark base. In the "Coated" column, s&box looks the same as "Base layer", so the coat roughness, the partial-coat texture and the coat normal map rows show nothing. ClearcoatWicker misses the glossy coat over the rough weave. Its base also looks flatter and brighter: luma is 1.26× the reference and contrast (luma std) is 0.075 against 0.107, because only direct light and ambient light the crevices.
5. **Background and exposure.** The viewer shows the blurred HDRI. s&box shows flat gray. This is not a material error, but it dominates the pixel difference outside the objects.

## Recommended work, in order of visual gain per effort

### 1. IBL with the reference HDRI (in progress, separate task)
The "s&box HDRI lighting to match Sample Viewer" task already loads Cannon_Exterior as a `SceneCubemap`, removes the added directional light, and locks exposure (see `sboxPresentation` in `reference-render-settings.json`). This alone should fix items 1, 2 and 5 for the metal spheres and plates. Recapture these five cases after it lands and run `diff_reference_capture.py` again before you start on items 2 and 3 below. Expected effect: luma ratio near 1.0 and clipping near 0%.

### 2. Tone mapping that does not clip
s&box has no Khronos PBR Neutral. Linear clips at 1.0, so at exposure 1 the IBL highlights on smooth metals will still clip. Options, in order of preference:
- Add Khronos PBR Neutral as a tone-map mode. It is about 15 lines of shader: a start-compression point of 0.8 − 0.04, desaturation, and an offset of 0.04 below 0.08. The reference formula is in `glTF-Sample-Renderer` `source/Renderer/shaders/tonemapping.glsl` (`toneMap_KhronosPbrNeutral`). This is the only exact match.
- Keep Linear in the corpus, and record that differences in highlights above 1 are expected.

### 3. `KHR_materials_clearcoat` (unlocks ClearCoatTest, ClearcoatWicker, CarConcept, ClearCoatCarPaint and others)
Reader: parse `clearcoatFactor`, `clearcoatTexture` (R), `clearcoatRoughnessFactor`, `clearcoatRoughnessTexture` (G), and `clearcoatNormalTexture` (with scale) into `GltfMaterial`. Register the extension in `GltfModelLoader.SupportedExtensions` only when the shader consumes it.

Shader: add a second specular lobe on top of the existing glTF stock material:
- coat normal: the coat normal map if one is present, else the geometric normal. The coat does **not** use the base normal map.
- coat specular = GGX(coatNormal, coatRoughness) with F0 = 0.04, plus prefiltered IBL sampled at coatRoughness along the coat normal.
- `color = base * (1 - coatFactor * F_coat(NdotV)) + coatFactor * coatSpecular`, with F_coat = Schlick(0.04, coat NdotV).
- The coat needs the IBL from item 1. Under direct light only, it shows as a single small highlight. That is the "Coating Only" column now, so do not judge the coat before item 1 is done.
- Texture routes: the existing five-UV material plan (`GltfMaterialUvPlan`, `GltfMaterialTextureEncodingPlan`) needs three new roles: ClearcoatFactor, ClearcoatRoughness, ClearcoatNormal. KHR_texture_transform applies to them too.

### 4. `KHR_materials_anisotropy` (unlocks the three anisotropy tests and AnisotropyBarnLamp)
Reader: parse `anisotropyStrength`, `anisotropyRotation` and `anisotropyTexture`. In the texture, RG is the direction in [-1,1] tangent space and B scales strength.

Shader: the anisotropy direction needs a tangent frame. All three test files contain TANGENT. Where a file has none, generate MikkTSpace tangents, as the normal-map path already requires.
- Direction `t = normalize(TBN * vec3(rotate(dirRG, rotation), 0))`, bitangent `b = cross(N, t)`. Strength = factor × texture B.
- Direct light: anisotropic GGX with `alphaT = mix(alpha, 1, strength²)` and `alphaB = alpha`, and the matching anisotropic visibility term, as in the Sample Viewer's `BRDF_specularGGXAnisotropy` (`brdf.glsl`).
- IBL: the Sample Viewer bends the normal toward the anisotropy direction before it computes the reflection: `bentNormal = normalize(mix(anisotropicNormal, n, bendFactorPow4))`, then `reflect(-v, bentNormal)`. Copy `getIBLRadianceAnisotropy` from `ibl.glsl` (line 119). This bent-normal trick is what makes the stretched sky reflections in the reference spheres. Without IBL (item 1), anisotropy cannot show at all.
- AnisotropyDiscTest also checks that the direction texture is read **linear**, not sRGB. Mark the texture role non-sRGB.

### 5. Blurred background (low priority)
The viewer blurs the background HDRI, and s&box shows the sharp cubemap as the sky. For comparison only, render the sky from a high mip of the same cubemap. This does not change object pixels.

## Acceptance for this set
After each step, recapture these five cases and run:
```
python3 engine/Tools/Gltf/scripts/diff_reference_capture.py research/gltf-validation/material-diff-<date> \
  AnisotropyStrengthTest/glTF-Binary ClearCoatTest/glTF-Binary ClearcoatWicker/glTF-Binary \
  AnisotropyDiscTest/glTF-Binary AnisotropyRotationTest/glTF-Binary
```
Targets (proposed, not yet agreed with the reviewer):
- foreground MAE below 0.05
- linear luma ratio from 0.9 to 1.1
- clipped pixels below 1%
- in the anisotropy tests, rows that differ in strength or rotation visibly differ in the capture

A human review on the compare site is still the final decision.

## Update: after HDRI, Khronos PBR Neutral, clearcoat and anisotropy (same day, 15:20)

Captures: `Temp/gltf-validation/exp-coat`. Sheets: `research/gltf-validation/material-diff-2026-11-09-after/`.

| Case | MAE before → after | Linear luma ratio before → after | s&box clipped before → after |
|---|---|---|---|
| AnisotropyStrengthTest | 0.268 → 0.134 | 0.52 → 0.70 | 5.2% → 0% |
| AnisotropyRotationTest | 0.313 → 0.090 | 0.43 → 0.81 | 6.3% → 0% |
| AnisotropyDiscTest | 0.239 → 0.215 | 1.42 → 0.51 | 61.4% → 0% |
| ClearCoatTest | 0.096 → 0.149 | 1.14 → 1.53 | 1.9% → 0% |
| ClearcoatWicker | 0.114 → 0.154 | 1.26 → 1.60 | 0.2% → 0% |
| CompareMetallic (control) | n/a → 0.108 | n/a → 0.74 | n/a → 0% |

What the sheets show:
- **Clearcoat works.** The "Coating Only" column now shows the sharp reflective coat over a dark base, as in the reference. The partial-coat texture, the coat roughness and the coat normal-map rows all show their effect. The higher MAE and luma for the two clearcoat cases come from the background. s&box shows the sharp, bright sky; the viewer shows a blurred, darker background. The objects themselves are close.
- **Anisotropy works.** Highlights stretch, strength 0 against 1 differs, and the rotation and direction-texture tiles show the brushed pattern.
- **Remaining gap 1: rough metals are too dark** (luma 0.5 to 0.8). s&box prefilters the cubemap differently (GGXCubeMapBlur, 256 px, BC6H) from the viewer's GGX importance sampling, and the rough mips lose energy. Next: measure the luma ratio per roughness column on MetalRoughSpheres and set `iblGain` or the cube size from that, rather than by eye.
- **Remaining gap 2: background.** Render the visible sky from a blurred mip of the same cubemap, to match the viewer's `blurEnvironmentMap`. This removes most of the remaining pixel difference outside the objects.
