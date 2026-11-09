# Current glTF exceptions and unsupported features

Audit: 2026-11-06T07:20:56.798957+00:00. Latest retained recording finished **2026-11-06T13:09:55.668452+07:00** (2026-11-06T06:09:55.668452+00:00).

**Scope:** current 337-variant corpus record, including carried-forward rows from its resumed sweep, plus inspected engine logs after that record. This is not a new render run. No earlier exception-only checkpoint was found; this report establishes one.

**117 failed variants / 54 models / 11 distinct GltfException messages.** The snapshot also has 83 captures and 137 blocked variants. **0 additional GltfException log entries found after the recording** in inspected logs. Logs can omit caught exceptions, and other engine copies are outside this audit.

Exact source record: `Temp/gltf-validation/current/run.json`, SHA256 `c507b897511c6b2110ce4dc04f6a2284df73d51637b038b959091349dbf703f2`. Installed Engine/Gltf/SceneRenderer binaries matched this recorded producer at audit time. New worktree candidates are not installed proof.

All affected case IDs and complete captured stack traces, producer hashes, inspected log file hashes, and extension inventories are in [gltf-exceptions-current.json](gltf-exceptions-current.json). The load and render fields repeat the same failures; they are counted once per case, not twice. First-error counts are not the number of models that would be fixed by supporting that feature.

## Exceptions in the current recording

| ID | Exact message after `Sandbox.Gltf.GltfException:` | Variants | Example |
|---|---|---:|---|
| E01 | The stock glTF model material adapter does not yet support normal, occlusion or emissive textures. | 57 | ABeautifulGame/glTF |
| E02 | The stock glTF model material adapter does not yet support unlit, emissive or non-opaque materials. | 37 | BoomBox/glTF |
| E03 | Unsupported required glTF extension 'KHR_lights_punctual'. | 4 | DirectionalLight/glTF |
| E04 | Unsupported required glTF extension 'KHR_materials_unlit'. | 4 | AnimationPointerUVs/glTF |
| E05 | Unsupported required glTF extension 'KHR_texture_basisu'. | 4 | ABeautifulGame/glTF-Binary-KTX-ETC1S-Draco |
| E06 | Unsupported required glTF extension 'KHR_materials_transmission'. | 3 | ChronographWatch/glTF-KTX-BasisU |
| E07 | Unsupported required glTF extension 'EXT_texture_webp'. | 2 | CarConcept/glTF-WEBP |
| E08 | Unsupported required glTF extension 'KHR_materials_clearcoat'. | 2 | ClearCoatCarPaint/glTF |
| E09 | Unsupported required glTF extension 'KHR_node_visibility'. | 2 | CubeVisibility/glTF |
| E10 | The asset has no default scene to assemble. | 1 | MandarinOrange/glTF |
| E11 | Unsupported required glTF extension 'KHR_materials_iridescence'. | 1 | IridescenceSuzanne/glTF-Binary |

The no-default-scene failure is an engine loading-policy gap for a valid asset, not evidence of an upstream defect. BrainStem and MetalRoughSpheresNoTextures each caused an observed native SIGSEGV; those crashes and the remaining unattempted/blocked variants are not GltfException feature counts. No malformed-asset root cause is established by these 11 groups.

## Feature choices: missing or partial end-to-end support

Choose an ID or feature name for delegation. Existing parser/tests/experimental candidates do not count as shipped model-rendering support.

| ID | Feature | Gap and existing work |
|---|---|---|
| C01 | Normal textures and normal scale | Rejected by active stock material adapter; UV-correct generated tangents are also needed for ABeautifulGame. Core material reader/tests exist; rendering integration missing. |
| C02 | Occlusion textures and strength | Rejected by active stock material adapter. Candidate role planning exists, not the active validated path. |
| C03 | Emissive factor and emissive textures | Nonzero emission and emissive maps rejected by active adapter. Core reader exists; shader/material integration missing. |
| C04 | Alpha MASK and alphaCutoff | Active adapter requires OPAQUE. Alpha fixtures/probes exist; no complete public rendering support. |
| C05 | Alpha BLEND, pass/depth/sorting behaviour | Active adapter requires OPAQUE. Independent pass/sorting probes do not establish current public support. |
| C06 | Correct linear vertex-colour multiplication and alpha | Existing default path fails BoxVertexColors. Opt-in stock-shader/per-primitive candidate and tests prepared, not installed or validated. |
| C07 | UV-dependent generated tangents for normal maps | Fallback tangent direction is not UV-derived MikkTSpace; mirrored seams need per-corner handling. Independent oracle/dependency proposal prepared; generator not integrated. |
| C08 | Independent texture-role UV sets and transforms | Default adapter does not complete per-role UV selection/transform mapping; KHR_texture_transform advertisement is not full material integration. Reader/upload and candidate UV paths exist; default end-to-end mapping incomplete. |
| C09 | Independent per-texture samplers | Active adapter rejects differing base-colour vs metallic/roughness samplers because shader shares a binding. Sampler diagnostic tests pass in a separate path; whole default-material semantics not proven. |
| C10 | Valid assets without a default scene / geometry-free resources | MandarinOrange fails the default-scene policy; Model.Load also rejects a default scene with no renderable geometry. Valid-format acceptance must be separated from a request to import/render a default scene; not an upstream defect finding. |
| C11 | Imported glTF cameras | Capture cameras do not establish full authored perspective/orthographic component import. Camera reader exists; full component workflow and activation integration incomplete. |
| C12 | Concurrent independent animation clips in public renderer | Public renderer selects one sequence; InterpolationTest capture applies one of nine clips. Managed composition source and tests prepared but uncompiled; public integration absent. This is a comparison/workflow requirement, not a claim that glTF requires every clip to play concurrently. |
| C13 | EXT_mesh_gpu_instancing on skinned nodes | Explicitly rejected by direct-GPU backend. Static instancing path exists; do not describe all instancing as unsupported. |

## Extensions present in the pinned corpus but not supported by the public loader

The counts below describe declarations across **all 337 variants**, including unattempted ones; they are not observed failure counts. Optional extensions can load using a core fallback and still look wrong. Metadata extensions are not rendering features. Some entries are outside the initially selected ten-extension scope and require your scope choice.

| ID | Extension | Feature | Used / required variants |
|---|---|---|---:|
| X01 | `EXT_lights_image_based` | Authored image-based environment lighting | 1 / 0 |
| X02 | `EXT_texture_webp` | WebP image sources | 5 / 5 |
| X03 | `KHR_animation_pointer` | Animation of material, UV and other pointer-targeted properties | 10 / 0 |
| X04 | `KHR_lights_punctual` | Authored directional, point and spot lights | 23 / 12 |
| X05 | `KHR_materials_anisotropy` | Directional highlights and anisotropy direction/strength textures | 15 / 0 |
| X06 | `KHR_materials_clearcoat` | Separate clearcoat layer and its inputs | 30 / 2 |
| X07 | `KHR_materials_diffuse_transmission` | Diffuse transmission through thin surfaces | 11 / 0 |
| X08 | `KHR_materials_dispersion` | Wavelength-dependent refraction | 8 / 0 |
| X09 | `KHR_materials_emissive_strength` | Emissive intensity beyond core factors | 14 / 2 |
| X10 | `KHR_materials_ior` | Configurable index of refraction and dielectric reflectance | 33 / 0 |
| X11 | `KHR_materials_iridescence` | Thin-film angle-dependent colour | 22 / 1 |
| X12 | `KHR_materials_pbrSpecularGlossiness` | Legacy specular/glossiness material workflow | 2 / 2 |
| X13 | `KHR_materials_retroreflection` | Retroreflective material response | 1 / 0 |
| X14 | `KHR_materials_sheen` | Fabric sheen layer | 19 / 4 |
| X15 | `KHR_materials_specular` | Dielectric specular strength and colour | 19 / 4 |
| X16 | `KHR_materials_transmission` | Physical transmission, distinct from alpha blending | 76 / 6 |
| X17 | `KHR_materials_unlit` | Lighting-independent materials | 10 / 6 |
| X18 | `KHR_materials_variants` | Per-instance material-variant selection | 21 / 0 |
| X19 | `KHR_materials_volume` | Thickness, attenuation colour and distance | 55 / 2 |
| X20 | `KHR_materials_volume_scatter` | Volume scattering | 2 / 0 |
| X21 | `KHR_node_visibility` | Authored/animated node visibility | 6 / 4 |
| X22 | `KHR_texture_basisu` | KTX2/Basis texture loading through the public model path | 6 / 6 |
| X23 | `KHR_xmp` | XMP metadata interpretation (not a rendering feature) | 2 / 0 |
| X24 | `KHR_xmp_json_ld` | JSON-LD XMP metadata interpretation (not a rendering feature) | 4 / 0 |

Current source loader allow-list: `EXT_mesh_gpu_instancing`, `EXT_meshopt_compression`, `KHR_draco_mesh_compression`, `KHR_mesh_quantization`, `KHR_meshopt_compression`, `KHR_texture_transform`. An allow-list entry is not proof of every shader binding, asset or platform. Other registry extensions not represented in this pinned corpus are not exhaustively catalogued here.

## Product and validation work separate from format exceptions

| ID | Feature or acceptance gate | Remaining work |
|---|---|---|
| P01 | Real HDR image-based lighting in the comparison scene | Fixture and opt-in SceneCubemap capture candidate prepared; native import, reflections and exposure controls unverified. Separate from EXT_lights_image_based file import. |
| P02 | Complete selected animation movies, camera framing and cadence | Camera/dense-sampling recipes prepared; fresh captures not made. These are evidence/presentation gaps, not all engine animation failures. |
| P03 | Animation graph references and parameters | Managed glTF component explicitly rejects native graph operations. |
| P04 | Model physics parity | Collision authoring, mass/surfaces, joints, ragdolls and hitboxes lack full glTF implementation/acceptance. |
| P05 | Model LODs/bodygroups/attachments/game data | Authored vmdl-parity integration remains incomplete. |
| P06 | Complete model editor and safe source reimport | Asset discovery/drop/inspector/reimport candidate exists but is unbuilt; full model-only editing, conflict handling, variants and metadata preservation remain incomplete. |
| P07 | GPU-ready portable compiled GLB and build packaging | Lossless GLB compiler exists; GPU optimization, complete dependency publication and cold-load parity not complete. |
| P08 | Performance parity and Windows acceptance | No completed matched vmdl performance gate or full Windows validation; these are acceptance gaps, not blanket missing core decoding. |

Core skinning, morphing, STEP/LINEAR/CUBICSPLINE evaluation, accessor/bufferView decoding, primitive modes, PNG/JPEG basics, Draco and meshopt have implementation and partial evidence. Do not list them as wholly unsupported merely because full corpus coverage is missing. Remaining cross-format/deformation/precision/backface/sampler tests remain acceptance work. A thicker point/line preview is a requested presentation improvement, not a requirement to alter glTF primitive topology.

## Per-message affected cases

### E01 — The stock glTF model material adapter does not yet support normal, occlusion or emissive textures.

`ABeautifulGame/glTF`, `ABeautifulGame/glTF-Binary`, `AlphaBlendModeTest/glTF`, `AlphaBlendModeTest/glTF-Binary`, `AnisotropyBarnLamp/glTF`, `AnisotropyBarnLamp/glTF-Binary`, `AnisotropyRotationTest/glTF`, `AnisotropyRotationTest/glTF-Binary`, `AntiqueCamera/glTF`, `AntiqueCamera/glTF-Binary`, `Avocado/glTF`, `Avocado/glTF-Binary`, `Avocado/glTF-Draco`, `Avocado/glTF-Quantized`, `BarramundiFish/glTF`, `BarramundiFish/glTF-Binary`, `BarramundiFish/glTF-Draco`, `Box With Spaces/glTF`, `CarConcept/glTF`, `CarConcept/glTF-Binary`, `CarConcept/glTF-JPG`, `CarbonFibre/glTF`, `CarbonFibre/glTF-Binary`, `ChairDamaskPurplegold/glTF`, `ChairDamaskPurplegold/glTF-Binary`, `ChronographWatch/glTF`, `ChronographWatch/glTF-Binary`, `ClearcoatWicker/glTF`, `ClearcoatWicker/glTF-Binary`, `CompareAmbientOcclusion/glTF`, `CompareAmbientOcclusion/glTF-Binary`, `CompareAnisotropy/glTF`, `CompareAnisotropy/glTF-Binary`, `CompareNormal/glTF`, `CompareNormal/glTF-Binary`, `CompareSheen/glTF`, `CompareSheen/glTF-Binary`, `Corset/glTF`, `Corset/glTF-Binary`, `Corset/glTF-Draco`, `DiffuseTransmissionTeacup/glTF`, `DiffuseTransmissionTeacup/glTF-Binary`, `FlightHelmet/glTF`, `GlamVelvetSofa/glTF`, `GlamVelvetSofa/glTF-Binary`, `GlassBrokenWindow/glTF`, `GlassBrokenWindow/glTF-Binary`, `GlassHurricaneCandleHolder/glTF`, `GlassHurricaneCandleHolder/glTF-Binary`, `IridescenceAbalone/glTF`, `IridescenceAbalone/glTF-Binary`, `IridescenceLamp/glTF`, `IridescenceLamp/glTF-Binary`, `IridescentDishWithOlives/glTF`, `IridescentDishWithOlives/glTF-Binary`, `MaterialsVariantsShoe/glTF`, `MaterialsVariantsShoe/glTF-Binary`

### E02 — The stock glTF model material adapter does not yet support unlit, emissive or non-opaque materials.

`BoomBox/glTF`, `BoomBox/glTF-Binary`, `BoomBox/glTF-Draco`, `BoomBoxWithAxes/glTF`, `ClearCoatTest/glTF`, `ClearCoatTest/glTF-Binary`, `CompareAlphaCoverage/glTF`, `CompareAlphaCoverage/glTF-Binary`, `CompareBaseColor/glTF`, `CompareBaseColor/glTF-Binary`, `CompareEmissiveStrength/glTF`, `CompareEmissiveStrength/glTF-Binary`, `CompareIor/glTF`, `CompareIor/glTF-Binary`, `CompareTransmission/glTF`, `CompareTransmission/glTF-Binary`, `CompareVolume/glTF`, `CompareVolume/glTF-Binary`, `DamagedHelmet/glTF`, `DamagedHelmet/glTF-Binary`, `DamagedHelmet/glTF-Embedded`, `DiffuseTransmissionPlant/glTF`, `DiffuseTransmissionPlant/glTF-Binary`, `EmissiveStrengthTest/glTF`, `EmissiveStrengthTest/glTF-Binary`, `GlassVaseFlowers/glTF`, `GlassVaseFlowers/glTF-Binary`, `IridescenceDielectricSpheres/glTF`, `IridescenceMetallicSpheres/glTF`, `Lantern/glTF`, `Lantern/glTF-Binary`, `Lantern/glTF-Draco`, `Lantern/glTF-Quantized`, `LightsPunctualLamp/glTF`, `LightsPunctualLamp/glTF-Binary`, `MeshoptCubeTest/glTF`, `MeshoptCubeTest/glTF-Meshopt`

### E03 — Unsupported required glTF extension 'KHR_lights_punctual'.

`DirectionalLight/glTF`, `DirectionalLight/glTF-Binary`, `LightVisibility/glTF`, `LightVisibility/glTF-Binary`

### E04 — Unsupported required glTF extension 'KHR_materials_unlit'.

`AnimationPointerUVs/glTF`, `AnimationPointerUVs/glTF-Binary`, `DiffuseTransmissionTest/glTF`, `DiffuseTransmissionTest/glTF-Binary`

### E05 — Unsupported required glTF extension 'KHR_texture_basisu'.

`ABeautifulGame/glTF-Binary-KTX-ETC1S-Draco`, `AnisotropyBarnLamp/glTF-KTX-BasisU`, `CarConcept/glTF-KTX-BasisU-Draco`, `FlightHelmet/glTF-KTX-BasisU`

### E06 — Unsupported required glTF extension 'KHR_materials_transmission'.

`ChronographWatch/glTF-KTX-BasisU`, `CommercialRefrigerator/glTF`, `CommercialRefrigerator/glTF-Binary`

### E07 — Unsupported required glTF extension 'EXT_texture_webp'.

`CarConcept/glTF-WEBP`, `ChronographWatch/glTF-WEBP`

### E08 — Unsupported required glTF extension 'KHR_materials_clearcoat'.

`ClearCoatCarPaint/glTF`, `ClearCoatCarPaint/glTF-Binary`

### E09 — Unsupported required glTF extension 'KHR_node_visibility'.

`CubeVisibility/glTF`, `CubeVisibility/glTF-Binary`

### E10 — The asset has no default scene to assemble.

`MandarinOrange/glTF`

### E11 — Unsupported required glTF extension 'KHR_materials_iridescence'.

`IridescenceSuzanne/glTF-Binary`

