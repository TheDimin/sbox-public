# glTF editor: properties and extensions

Status: research and recommendation, 2026-11-10. "Editor" columns describe the
glTF editor in this branch; "Runtime" is what the s&box loader renders
(`GltfModelLoader.SupportedExtensions` and the material adapters).

## How to show a property

Each glTF property falls into one of four tiers. The tier decides the UI.

| Tier | When | UI |
|---|---|---|
| Edit | The user authors it and the runtime uses it | A property group (facet) on the element, or the scene transform for nodes |
| Preview | Its meaning is visual or temporal | A viewport preview chosen by selection (mesh alone, variant, animation, joint weights) |
| Info | Useful to know, edited elsewhere (exporter, compression tool) | A read-only line in the inspector |
| Managed | Plumbing that follows from other edits | Hidden; the editor keeps it valid and de-duplicated |

Rules that follow from this:

- A property the runtime ignores is still editable when it is valid glTF, but its
  group shows it is not rendered (for example `MSFT_lod`, `KHR_materials_ior`).
- Buffers, buffer views and accessors are Managed. They have no meaning to a user;
  the editor only adds them (preview spheres, imported data) and never shows them.
- Images and samplers are Managed: the texture inspector picks the image and the
  sampling, and the editor shares identical samplers and images (done).
- Geometry encodings (Draco, meshopt, quantization) are Info. Re-encoding belongs
  to an import or export step, not to property editing.

## Core glTF

| Element | Property | Tier | Editor now |
|---|---|---|---|
| asset | version, generator, copyright | Info (copyright: Edit) | not shown; recommend a file-level inspector |
| scene | name, root nodes | Edit (name), Preview | done (name); roots follow the hierarchy |
| node | name, translation/rotation/scale/matrix | Edit | done, through GameObjects and the scene transform |
| node | mesh, skin, camera | Edit | mesh shown with a jump button; skin and camera not shown |
| node | weights (morph defaults) | Edit | done (Morph Weights, only when the mesh has targets) |
| mesh | name, primitive materials | Edit | done, with sphere renders |
| mesh | primitive attributes, mode, indices | Info | not shown; recommend vertex/triangle counts and attribute list |
| mesh | targets, weights | Edit + Preview | not shown; recommend sliders that drive the mesh preview |
| material | core PBR, alpha, double sided, emissive, texture slots | Edit | done |
| texture | image, sampler | Edit (Managed elements) | done |
| camera | type, perspective, orthographic | Edit | perspective and orthographic groups; `type` is not kept in sync with the group yet |
| animation | name, channels (TRS) | Edit | selecting it imports the channels into Movie Maker as LocalPosition/Rotation/Scale tracks on the node GameObjects; edits are written back as LINEAR samplers (30 Hz) on save or deselect |
| animation | morph weight channels, original interpolation | Info | weights are kept but not editable; edited TRS channels become LINEAR |
| skin | joints, skeleton, inverseBindMatrices | Preview + Info | joints are bone GameObjects (default bone gizmo and icons, toolbar toggles for armature and names); weight heat map while a joint is selected; "Add Socket" adds an empty child node |
| extras | any | Edit (advanced) | kept but not shown; recommend a raw JSON card |

## Extensions

| Extension | Runtime | Tier | Editor now | Recommendation |
|---|---|---|---|---|
| KHR_materials_unlit | yes | Edit | shader picker | done |
| KHR_materials_clearcoat | yes | Edit | group with textures | done |
| KHR_materials_anisotropy | yes | Edit | group with texture | done |
| KHR_materials_diffuse_transmission | yes | Edit | shader picker + group | done |
| KHR_materials_emissive_strength | yes | Managed | part of the HDR emissive color: a color above 1 is saved as factor plus strength | done |
| KHR_materials_variants | yes | Edit + Preview | variants under meshes, per-primitive material per variant | add create/rename/delete variant |
| KHR_texture_transform | yes | Edit | kept, not shown | add offset, rotation, scale and UV override to each texture slot |
| KHR_texture_basisu, EXT_texture_webp | yes | Info / Edit | kept, not shown | show the alternative image source on the texture; allow choosing it |
| KHR_node_visibility | yes | Edit | the GameObject enabled state | done |
| KHR_lights_punctual | data only (not instanced) | Edit | Light group | show a light gizmo; node light reference |
| EXT_mesh_gpu_instancing | yes | Info | not shown | instance count on the node |
| KHR_mesh_quantization, KHR_draco_mesh_compression, KHR/EXT_meshopt_compression | yes | Info | not shown | show "compressed with ..." on the mesh |
| KHR_materials_ior, specular, sheen, transmission, volume, iridescence, dispersion | no | Edit (not rendered) | only IOR has a group | add groups marked "not rendered"; the shader picker already refuses shaders that cannot render present groups |
| KHR_materials_pbrSpecularGlossiness | no (archived) | Info | not shown | offer conversion to metallic-roughness |
| MSFT_lod | no | Edit (not rendered) | Level of Detail group | keep; runtime support is a separate decision |
| KHR_animation_pointer | no | Info | not shown | list the animated pointers |

## Recommended next steps

1. Texture transform on each slot (common in exported files, runtime supports it).
2. Mesh info and morph target sliders with a live preview.
3. Animation channel list and an asset/file inspector (copyright, generator).
4. Variant management (create, rename, delete) and camera `type` sync.
5. "Not rendered" groups for the remaining material extensions.
