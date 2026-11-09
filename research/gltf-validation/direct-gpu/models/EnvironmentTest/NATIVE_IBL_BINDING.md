# Authored image-based lighting boundary

`EXT_lights_image_based` is **not** in `corpus-case-manifest.json:selected_extensions`.
The pinned EnvironmentTest `glTF-IBL` form uses it optionally. Existing core fallback
is separate from extension consumption; nothing in this folder enables registration.
The specification calls it multi-vendor, rather than a ratified Khronos extension.

The current native stock route cannot directly preserve both authored SH9 and the
authored roughness range:

- `SceneCubemap.RenderDirty` recomputes radiance and normalization SH. The generated
  `CEnvMapSceneObject` binding exposes those calculation methods and one float4
  `m_vNormalizationSH`; it exposes no nine-RGB authored irradiance setter.
- `common/lightbinner.hlsl:BinnedEnvMap` carries no SH9 coefficients. Its float4
  normalization field is explicitly unused; the spare attributes are not an ABI
  contract for adding 27 floats.
- `vr_environment_map.fxc:29-31` samples `level * GetDimensions().nLevels`. The
  five supplied source mips therefore use `5*r` with hardware clamping, whereas
  authored levels cover `0..4` evenly. This cannot be corrected by adding a
  SceneCubemap property or by regenerating source mips without changing semantics.
- `vr_lighting.fxc:867-872` samples the same specular cube at level 1 for diffuse
  lighting. It does not evaluate the source's nine irradiance coefficients.
- No `CubeMapWrapper` type was found in this checkout's managed/shader source or
  installed managed XML surface. This is an audit result, not a claim about an
  unavailable native implementation.

The smallest managed alternative keeps the existing GPU-buffer draw, source
material inputs and native BRDF helpers. A dedicated glTF source-IBL shader branch
binds the original authored cube, nine RGB coefficient uniforms, intensity and
world-to-environment rotation. It evaluates SH9 for diffuse, dividing authored
irradiance by pi once, and samples specular at `linearRoughness*(mipCount-1)`.
It must replace the corresponding indirect terms rather than add a second copy
of the private comparison environment. Direct light, normal maps, material AO and
material factors stay in the existing stock source-material path.

`gltf_source_ibl_candidate.hlsl` is an **uncompiled, unwired** model-local helper
for that branch. It changes no shared shader, renderer, access list or native
interop. The selected extension and default capability stay off. A real route
still needs typed document validation, source-image decoding through image and
bufferView references, immutable generation ownership, selected-scene binding,
source/compiled parity and explicit scene-environment lifetime policy. Copying
a decoded fixture into a private scene does not satisfy any of those gates.

The extractor preserves all five authored mip levels and original image bytes;
it does not regenerate GGX mips or derive an HDR panorama. Its native fixture
candidate recomputes native diffuse SH and therefore remains diagnostic. The
current source form retains the Adobe Stock license from its pinned model tree.

Independent acceptance for a future branch includes: coefficient-only constant
irradiance, signed first-order direction, nonzero second-order cross terms,
intensity zero and proportionality, 90-degree rotation, distinct mip colors at
roughness 0/.25/.5/.75/1, and an explicit `mipCount` versus `mipCount-1` negative
control. GPU cube samples must precede rendered controls. Actual extension
consumption and final source/one-canonical-compiled visual comparison remain
required before advertising support.

Primary references: [extension specification](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_lights_image_based),
[Babylon loader](https://github.com/BabylonJS/Babylon.js/blob/master/packages/dev/loaders/src/glTF/2.0/Extensions/EXT_lights_image_based.pure.ts),
[SH coefficient order, signs and Lambertian conversion](https://github.com/BabylonJS/Babylon.js/blob/master/packages/dev/core/src/Maths/sphericalPolynomial.pure.ts),
[RGBD expansion](https://github.com/BabylonJS/Babylon.js/blob/master/packages/dev/core/src/Shaders/ShadersInclude/helperFunctions.fx).
