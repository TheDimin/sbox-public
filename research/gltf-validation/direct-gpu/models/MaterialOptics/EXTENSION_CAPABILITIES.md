# Material extension capability audit

This is a source audit, not a renderer or visual acceptance result. The public
loader's current `GltfModelLoader.SupportedExtensions` does not register the
extensions below. Required use therefore remains an error; unsupported optional
use retains the authored core material. A successful load is not evidence that
an optional extension rendered. The sample checkout is pinned at
`90d7ede14c7e280af263824604b427a1ca02cb66`.

| Extension | Available candidate | What remains before public support |
|---|---|---|
| `KHR_materials_unlit` | Reader flag and CPU plan for base colour, alpha, vertex colour and sidedness | Stock adapter rejects unlit. A lighting-independent realized path must bind all applicable inputs and prove alpha/pass/sidedness and GPU pixels. |
| `KHR_materials_variants` | Primitive mapping reader and transactional per-instance state candidate | Integrate selection into actual draw/material lifetime, expose usable instance selection, and verify two independent instances and reset. |
| `KHR_materials_emissive_strength` | Separate decoder returns an effective scalar only when the reader is configured for it; defaults and unsupported optional fallback are one | Core emission realization, linear texture multiplication, unclamped HDR intensity and actual GPU rendering remain unproved. Do not register solely for decoding. |
| `KHR_materials_anisotropy` | Stock common BRDF source contains an anisotropic function; this is not an extension binding | Direction in tangent space, rotation, RG direction and B strength texture channels, independent texture sampling, and shader integration require proof. User feedback for `AnisotropyDiscTest` reports a missing texture. |
| `KHR_materials_clearcoat` | No public glTF binding | Separate layer with factor, roughness and normal texture roles requires mapping and rendered validation. |
| `KHR_materials_transmission`, `KHR_materials_volume`, `KHR_materials_ior`, `KHR_materials_dispersion` | Independent pinned-source and numerical checks in `../../../../../engine/Tools/Gltf/direct-gpu/models/MaterialOptics/OpticsSourceTests.cs` | Physical transmission is distinct from alpha coverage. Texture channels, thickness, attenuation, IOR-dependent reflectance and wavelength-dependent refraction require a validated realization. |

The stock `glass.shader` is a potentially useful building block, not a faithful
implementation of the optical extensions: its source uses a fixed `0.04` in
the reflection calculation, forces metalness to zero, samples a screen-space
framebuffer and exposes a refraction-strength control (UI range 1 to 1.1). It
does not establish the glTF volume thickness/attenuation or dispersion
contracts. A shader function or a visually plausible glass sample alone cannot
justify marking the extensions supported.

The existing optics fixture has seven independent source/numeric cases; its
`CheckDiagnosticValues` accepts values from an implementation under test and
checks transmission R, thickness G, IOR reflectance, attenuation distances and
dispersion. It is not a renderer test by itself. The existing
`AnisotropyBarnLampTests` explicitly checks that optional anisotropy, clearcoat,
emissive strength, transmission and volume remain separately reported core
fallbacks.

Primary specifications (all marked complete and ratified):
[unlit](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_unlit/README.md),
[variants](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_variants/README.md),
[emissive strength](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_emissive_strength/README.md),
[anisotropy](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_anisotropy/README.md),
[clearcoat](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_clearcoat/README.md),
[transmission](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_transmission/README.md), and
[volume](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_volume/README.md).
