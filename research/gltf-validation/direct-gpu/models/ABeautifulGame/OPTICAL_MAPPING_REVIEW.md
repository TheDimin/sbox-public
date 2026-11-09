# Optical source-material integration candidate for review

## Evidence and scope

`KHR_materials_transmission` and `KHR_materials_volume` are Complete and Ratified in the Khronos extension registry ([transmission](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_transmission), [volume](https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_materials_volume)). This is a proposed format path, not a claim of implemented support. The public loader must continue reporting unsupported required extensions and using the defined core fallback for optional unsupported extensions until all routes below pass. The existing glass native test establishes access to the framebuffer-copy service only; its stock material is not the proposed glTF mapping.

## Reader and material contract

The isolated `optical-reader-isolated-proposal.patch` adds typed transmission and volume parameters and includes both optional images in the material-image snapshot, but does not modify public capability sets. A reader supplied an explicit capability set parses transmission factor in [0,1] with linear R texture; volume thickness in mesh units times linear G texture; attenuation distance strictly positive with absent value represented by +infinity; attenuation color in linear [0,1]. Wrong types, ranges, undeclared selected extensions and excluded unlit/specular-glossiness are rejected. Core base/normal/MR/AO/emissive roles remain unchanged. `KHR_materials_ior` is not newly advertised: absent or unsupported optional IOR uses 1.5; required unsupported IOR fails. Optical plus supported diffuse-transmission, BLEND and MASK need dedicated compositing and role-count controls before broad support; no silent downgrade of selected required data.

The existing 5-role source shader needs a generated 7-role version for independent transmission and thickness UV selections and samplers. Each role uses the source texture's sampler, mip requirement, UV selector and KHR texture transform; both data roles use linear transfer. Texture snapshot retention, decoded image/mip caches and material cache keys must include the new role descriptors, sampler and UV affine rows. Avoid packing one role into another or changing sample images. A separate >7-role combination including diffuse transmission is unresolved; it must not truncate streams or silently select a different material.

## Native shading proposal

Use an opt-in stock-derived source shader with `S_TRANSLUCENT` and `bWantsFBCopyTexture`, relying on Alpha's tested final sorting/copy route only when available. Actual alpha coverage and physical transmission are distinct; a core OPAQUE material with transmission is sent through the optical translucent path, but the surface is still fully covered. Keep source base color, normals, metallic/roughness, AO, emission and native direct+IBL specular lighting. Split the native standard combiner's diffuse and specular terms, attenuate diffuse by `(1 - transmission)` while preserving the metallic specular and dielectric reflection, then add framebuffer transmission weighted by `transmission * (1-metallic) * (1-dielectricFresnel)` and authored base color. This is a screen-space raster approximation of the extension's transmission lobe, subject to independent validation; no arbitrary 1.05 glass refraction.

For a thin wall (`thickness=0`) sample the unrefracted framebuffer coordinate. For nonzero volume use `refract(-V,N,1/1.5)` at the entrance and project the estimated ray exit into the copied framebuffer. The Khronos sample renderer estimates the ray from refracted unit direction, authored thickness and model-axis scales ([implementation](https://github.com/KhronosGroup/glTF-Sample-Renderer/blob/main/source/Renderer/shaders/punctual.glsl)). The prototype instead proposes direction-sensitive affine scaling: invert the actual rigid node/placement linear transform, then calculate world ray length as mesh-space thickness divided by the length of the transformed world-unit direction. This accounts for rotation and nonuniform scale without an arbitrary glass strength. Mesh-space to world scale must come from the actual current node/placement transform, not fixed meters-to-inches alone. The existing GPU rig uses System.Numerics row-vector convention; pack inverse columns `(M11,M21,M31)`, `(M12,M22,M32)`, `(M13,M23,M33)` into the shader dot-product rows, so its world-to-mesh direction agrees with `Vector3.TransformNormal`. The existing Basis conversion is already in the rig matrix and must not be applied again. It must be per draw, updated on pose/placement and tested with rotated/nonuniform transforms and skin/morph before broader capability claims; no trustworthy scale implies fallback/rejection, not fabricated scale. Convert world path and attenuationDistance consistently (engine inches vs glTF world meters). Infinite attenuation distance yields exactly one; finite attenuation is `pow(attenuationColor, path / distance)`, with zero path producing exactly one and zero-color channels handled without NaN. Roughness controls transmission mip/blur and must be verified against actual copy mip availability, not assumed. Rendering outside viewport, perspective/orthographic projections, depth and backface behavior need explicit limits. Stock/native directional/IBL terms, masks, depth, tools, fog and emission must not be bypassed by an untested custom compositor.

## Independent admission controls

- Reader: defaults, actual ABeautifulGame fields in all forms, required-supported vs required-unsupported, optional malformed fallback, selected malformed rejection, excluded material models, per-role UV transforms/sampler and transfer. Unit-only parsing is not public renderer support.
- Shader and framebuffer: synthetic authored untextured OPAQUE slab over two independently colored opaque landmarks. Test transmission 0 and 1, preserve dielectric specular highlight; metallic 1 blocks transmission; test base color, normal and roughness independently. Probe actual bound framebuffer copy and target pixels with strong negative controls. Transparent ordering and overlapping surfaces are separate tests; one screen-space opaque copy need not prove multi-layer transparency.
- Volume: literal directional/refraction oracle at IOR 1.5; zero thickness must equal thin wall; nonzero thickness must shift background according to projected ray, with scale/rotation tests; negative IOR and screen-coordinate controls. Infinite attenuation with color 0.8 must stay neutral; finite distance at zero/one/two world distance must give 1/c/c². Test channels at 0 and 1, and independent thickness/transmission linear channels with competing R/G texels and independent UV/samplers. Distinguish finite physical path and image-space projected shift.
- Public: actual ABeautifulGame source and exactly one canonical compiled resource, original bytes intact; both authored pawn materials and actual source texture role bindings; compare settled reference framing and exact IBL inputs. Capture is review only, no numerical user-acceptance claim. A correct optional fallback remains a separate public case; do not declare required support based only on a synthetic or material-override test.

## November 7 continuation

The repository's authenticated review snapshot recorded the rejection of
`ABeautifulGame/glTF::reframed-ibl-candidate` at
`2026-11-07T03:18:44.880Z` with the note “The marbles on the pawns are not
transparant”. Exact capture fingerprint
`ee30bb0a1a7b7158f6e9a5620f796ca2000c863ea5ae0a749f46c587f71b3212`.
The imported snapshot was site version 31, observed at
`2026-11-07T03:43:23Z`. This work uses that imported record and does not
perform another authenticated sync. Earlier camera, lighting and material
feedback remains attached to its own captures.

- The original model checkout is still at `90d7ede14c7e280af263824604b427a1ca02cb66`.
  The guarded preflight runner rehashed the pinned asset dependency inventory.
- Nine independent Python checks passed, including the three authored/default
  controls and six numerical optical controls.
- Native anonymous shader preflight passed 1/1 on e3add. The f069 shader
  compiled as an anonymous native shader and exposed transmission and thickness
  role parameters, numeric controls, translucency and the framebuffer-copy flag.
  Receipt `Temp/gltf-validation/direct-gpu/beautiful-optical-shader-preflight-e3add-01/probe-01-editor_direct_gpu_tests/receipt-01.json`,
  SHA-256 `8356450b756b814faf34cb4cdce7560908d554440db6d88df78ecdc52d808f0f`.
- The revised e3add thin-surface test observes the copy from the actual
  translucent sort child's callback. It checks the installed automatic
  copy request without assigning the copy flag by hand and uses the
  installed stock-derived source shader as the zero-transmission reference.
- `../../../../../engine/Tools/Gltf/direct-gpu/models/ABeautifulGame/PublicPawnOpticsTests.cs` prepares an ordinary public source/canonical pair
  with the previously reviewed 800×620 framing and fixed ACES +2 EV / IBL.
  Its separate fallback fixture removes only the two optical extensions.
  The render assertions sample the interiors of visible black and white pawns.
  This probe requires a coherent named shader and public material producer.
- The first e3add thin request reached the runtime helper compile and stopped
  with two missing Fox test dependencies inherited from the shared
  `NativeCapture` file. Receipt
  `Temp/gltf-validation/direct-gpu/beautiful-optical-thin-e3add-01/probe-01-editor_direct_gpu_tests/receipt-01.json`,
  SHA-256 `29f7c9f25155b507abca78024ce41aa8fa082fd8e9f583635bc07097f87c1262`.
- The dependency-complete request compiled and ran. The automatic copy was
  exposed by the actual translucent sort child in 40 callbacks and reported
  nine mip levels. Strength zero under direct lighting matched the installed
  native source material byte for byte. Opaque landmark mean signal was
  0.65217. In the dark control, strength zero, strength one and metal one
  produced identical PNGs and zero interior signal. The positive optical pixel
  assertion failed. Receipt
  `Temp/gltf-validation/direct-gpu/beautiful-optical-thin-e3add-02/probe-01-editor_direct_gpu_tests/receipt-01.json`,
  SHA-256 `458b078019688cc95cbd94fa8e32a25b2422197a24d10355f30fe22da81d909d`.
  The texture handle and mip count alone do not establish usable color.
- The model-local copy diagnostic is compiled as a helper against e3add, with
  execution pending a shared slot. It renders the same landmarks with
  postprocessing enabled and disabled, and isolates lobe factors, screen UV,
  copy scaling, the candidate sampler, explicit dimensions-based sampling and
  a direct texel load.
- An isolated build of the current Sandbox.Gltf sources with the frozen reader
  patch passed 20/20 in the C# harness. Roslyn compiled the scratch copy in
  `/tmp/gltf-optical-reader-csc-yANUoI`; DLL SHA-256
  `2202bf67905e6624efe613636350b2fe1bf4b50eb6259cac9937ff4c6529efbd`.
  Source file line endings required applying the patch with
  `apply.ignoreWhitespace=change` in the scratch directory. The reader build
  used the actual package bindings for compilation, made no global edits,
  and did not install a new producer.
