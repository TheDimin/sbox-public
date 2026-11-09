# Khronos extension registry and local validation inventory

Audit snapshot: 2026-11-07. Registry source: KhronosGroup/glTF
`extensions/README.md` at commit [`81762cc328b160eafaa0d577dd547a998401ade9`](https://github.com/KhronosGroup/glTF/blob/81762cc328b160eafaa0d577dd547a998401ade9/extensions/README.md),
the latest registry update visible at audit time (2026-09-03). The same official
registry defines status stages; names and maturity below refer to that snapshot.
The sample-assets checkout pinned in `coverage.json` is not the spec-repository
revision. No local clone of KhronosGroup/glTF was present.

This inventory answers whether a registry name has a corresponding validation
lead, not whether the engine supports it. A name in code, schema, a sample, or a
negative test is not positive extension support. **GPU scoped** means an actual
named producer exercised the feature slice; it does not mean general
conformance. **Reader/CPU only** means data/plan/source checks exist without
accepted positive public GPU proof. **Unstarted** means this bounded audit found
no extension-specific implementation, fixture, test or owner record; it is not
a claim that generic core validation is absent. Entries outside the selected
ten are not automatically deferred: separately active user requests and the
full physics-parity goal are marked explicitly below.

## Ratified Khronos extensions

| Registry name | Scope | Local evidence level | Evidence boundary |
|---|---|---|---|
| `KHR_animation_pointer` | Separately active | Isolated candidate/CPU scoped; public GPU unrun | AnimatedColorsCube isolated parser candidate completed 18 assertions (terminal observation, isolated build only). It preserves rejection of required extension support; no installed public pointer-animation GPU proof. See [isolated observation](direct-gpu/models/AnimatedColorsCube/isolated-parser-observation.json) and [candidate tests](../../engine/Tools/Gltf/direct-gpu/models/AnimatedColorsCube/PartialPointerTests.cs). |
| `KHR_draco_mesh_compression` | Selected | GPU scoped | BrainStem and ABeautifulGame public source/compiled forms; see [FEATURE_STATUS](FEATURE_STATUS.md#selected-extensions). |
| `KHR_gaussian_splatting` | Not in selected extension set | Unstarted | No extension-specific local fixture or owner found in this audit. |
| `KHR_interactivity` | Not in selected extension set | Unstarted | No extension-specific local fixture or owner found. |
| `KHR_lights_punctual` | Selected | Reader/CPU only plus prototype | Scene/light reader and plans exist; a directional factory prototype is not public lifecycle/render acceptance. Point/spot public behavior remains open. |
| `KHR_materials_anisotropy` | Not in selected extension set | Reader/source only | Model/source audit exists; texture-channel mapping and actual public binding/render proof remain open. |
| `KHR_materials_clearcoat` | Not in selected extension set | Reader/source only | Source/numeric optical fixture exists; public glTF binding and rendered layer proof remain open. |
| `KHR_materials_dispersion` | Not in selected extension set | Reader/source only | Optical source/numeric cases exist; no accepted native rendered feature proof. |
| `KHR_materials_emissive_strength` | Not in selected extension set | Reader/CPU only | Decoder/unit coverage exists; HDR emission realization and actual public GPU pixels remain unproved. |
| `KHR_materials_ior` | Not in selected extension set | Reader/source only | Optical source/numeric cases exist; no accepted native rendered feature proof. |
| `KHR_materials_iridescence` | Not in selected extension set | Unstarted | No extension-specific local validation lead identified in this bounded audit. |
| `KHR_materials_sheen` | Not in selected extension set | Unstarted | No extension-specific validation proof identified; a material visual comment is not an implementation test. |
| `KHR_materials_specular` | Not in selected extension set | Unstarted | No extension-specific validation proof identified; shared environment work does not prove the material extension. |
| `KHR_materials_transmission` | Separately active (optical mapping work) | Reader/source proposal only; unrun | ABeautifulGame has reader and shader mapping proposals under review, not accepted executed mapping or public render proof. See [mapping review](direct-gpu/models/ABeautifulGame/OPTICAL_MAPPING_REVIEW.md) and [proposal](direct-gpu/models/ABeautifulGame/OPTICAL_PROPOSAL.md). |
| `KHR_materials_unlit` | Selected | GPU scoped | UnlitTest named-shader source/compiled and bounded mixed-material receipts; not complete material conformance. |
| `KHR_materials_variants` | Selected | GPU scoped | MaterialsVariantsShoe source and canonical compiled selection/render proof; broader asset/lifecycle coverage remains open. |
| `KHR_materials_volume` | Separately active (optical mapping work) | Reader/source proposal only; unrun | ABeautifulGame has reader and shader mapping proposals under review, not accepted executed mapping or public render proof. See [mapping review](direct-gpu/models/ABeautifulGame/OPTICAL_MAPPING_REVIEW.md) and [proposal](direct-gpu/models/ABeautifulGame/OPTICAL_PROPOSAL.md). |
| `KHR_mesh_quantization` | Selected | GPU scoped | Quantized BrainStem/Meshopt public proofs; not every accessor/format combination. |
| `KHR_meshopt_compression` | Selected; Release Candidate at the separate pinned spec revision in `coverage.json` | GPU scoped | BrainStem source/compiled and MeshoptCubeTest compressed/fallback plus canonical compiled GPU checks; broader assets/layouts remain open. This name is absent from the registry index snapshot above, so the pinned maturity is not represented as current registry status. |
| `KHR_node_hoverability` | Not in selected extension set | Unstarted | No extension-specific local fixture or owner found. |
| `KHR_node_selectability` | Not in selected extension set | Unstarted | No extension-specific local fixture or owner found. |
| `KHR_node_visibility` | Not in selected extension set | Reader/CPU only | Reader/scene-node tests exist; no selected-scope public GPU acceptance was identified. |
| `KHR_texture_basisu` | Selected | GPU scoped | ABeautifulGame required BasisU+Draco source and native texture/resource evidence; broader roles/portability remain open. |
| `KHR_texture_transform` | Selected | GPU scoped | Corrected TextureTransform source/canonical public captures pass scoped contrast/direction checks; broader role/sampler matrix remains open. |
| `KHR_xmp_json_ld` | Not in selected extension set | Unstarted | No extension-specific local fixture or owner found. |
| `EXT_mesh_gpu_instancing` | Selected | GPU scoped | Static 125-instance source/compiled geometry and ordering checks; skinned nodes remain unsupported. |
| `EXT_meshopt_compression` | Selected | GPU scoped | BrainStem EXT source/compiled and related codec/layout proofs; wider filter/layout/Windows matrix remains open. |
| `EXT_texture_webp` | Not in selected extension set | Reader/CPU only | Image-source selection and fallback tests exist; no accepted public WebP GPU/render proof identified. |

## In-progress Khronos and multi-vendor extensions/projects

| Registry name | Registry maturity | Scope | Local evidence level |
|---|---|---|---|
| `KHR_accessor_float64` | Review Draft | Not in selected extension set | Unstarted: no extension-specific fixture or owner found; ordinary accessor validation is not Float64 support. |
| `KHR_audio_graph` | Proposal | Not in selected extension set | Unstarted in this audit. |
| `KHR_collision_shapes` | Review Draft | Separately active (full physics parity goal) | Extension-specific validation is unstarted in this audit; full physics parity remains a requested integration goal. |
| `KHR_materials_diffuse_transmission` | Release Candidate | Separately active, user-requested | GPU scoped: MandarinOrange named installed public3 passed **3/3** (source, canonical compiled, and compatibility); new-capture human appearance approval is pending. See [result](direct-gpu/models/MandarinOrange/result.json). |
| `KHR_materials_subsurface` | Initial Draft | Not in selected extension set | Unstarted in this audit. |
| `KHR_physics_rigid_bodies` | Review Draft | Separately active (full physics parity goal) | Extension-specific validation is unstarted in this audit; full physics parity remains a requested integration goal. |
| `KHR_texture_procedurals` | Initial Draft | Not in selected extension set | Unstarted in this audit. |
| `EXT_texture_procedurals_mx_1_39` | Review Draft | Not in selected extension set | Unstarted in this audit. |
| `KHR_texture_video` | Initial Draft | Not in selected extension set | Unstarted in this audit. |
| `EXT_lights_ies` | Multi-vendor | Not in selected extension set | Unstarted in this audit. |
| `EXT_lights_image_based` | Multi-vendor | Not in selected extension set | Unstarted in this audit. |
| `EXT_mesh_manifold` | Multi-vendor | Not in selected extension set | Unstarted in this audit. |

## Reading this inventory

The exact ten selected names and their current scoped proofs are maintained in
[`coverage.json`](coverage.json) and the [selected-extension table](FEATURE_STATUS.md#selected-extensions).
Diffuse transmission, optical mapping and physics-parity work are separately
active and do not expand that ten-name set. Active reader/shader proposals are
not executed feature proof.
The optical source/numeric tests are documented in
[`direct-gpu/models/MaterialOptics/EXTENSION_CAPABILITIES.md`](direct-gpu/models/MaterialOptics/EXTENSION_CAPABILITIES.md).
Extension name recognition and fallback rejection are not positive implementation.

This pass inventories the Ratified, In-progress and Multi-vendor sections of the
official registry snapshot. It does not enumerate the separate Vendor or
Archived sections, audit every third-party extension, or certify that the
bounded local search found every generic test. See Khronos' [registry status
definitions](https://github.com/KhronosGroup/glTF/blob/81762cc328b160eafaa0d577dd547a998401ade9/extensions/README.md#specification-status-explained).
