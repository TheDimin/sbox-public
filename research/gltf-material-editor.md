# glTF material editor (MaterialDoc replacement)

Status: research and proposed design, 2026-11-09. Nothing here is implemented
except where marked. Decisions for the owner are at the end.

## Goal

Selecting a glTF material in the glTF editor gives a MaterialDoc-class editor
in the Unity-style inspector: pick a shader, edit its features, parameters and
texture slots with previews, see the result live, and keep the result valid
glTF. MaterialDoc is a native, Windows-only tool, so on Linux s&box has no
material editor at all today.

## What MaterialDoc does (parity list)

| Capability | MaterialDoc | Needed here |
|---|---|---|
| Shader picker with search | yes | yes, filtered by glTF compatibility (below) |
| Feature toggles (`F_*` combos) | yes, grouped | yes, from the shader schema |
| Parameters grouped and ordered (`UiGroup`, ranges, steps) | yes | yes, from the shader schema |
| Texture slot with thumbnail, file pick, drag and drop | yes | yes; slot points at a glTF texture/image |
| Solid color when a slot has no texture | yes | glTF factor (baseColorFactor etc.) |
| Per-texture processing (color space, invert, channel) | yes, compiled to `.vtex` | partly: glTF fixes color space and channels per slot; packing tools are a separate feature |
| Live preview (sphere, cube, plane, custom model) | yes | yes: sphere in the list, the model itself in the viewport |
| Physics surface property | yes (`SystemAttributes`) | yes, as material data |
| Reset parameter to default, show only used | yes | yes |
| Copy and paste of parameters | yes | later |

## What the engine already gives

- `Shader.Schema` (`engine/Sandbox.Engine/Resources/Shader/Shader.Editor.cs`)
  returns every variable (name, type, `UiGroup`, `UiType`, min/max, step,
  default, default texture) and every combo (`F_*` name, group, values). Its
  comment says it exists for the material editor. A generic, MaterialDoc-like
  UI can be built from it without per-shader code.
- The runtime maps glTF to three shaders and picks one from the data:
  `gltf_surface_five_uv` (lit PBR, with `F_GLTF_CLEARCOAT` and
  `F_GLTF_ANISOTROPY`), `gltf_unlit_five_uv` (`KHR_materials_unlit`) and
  `gltf_diffuse_transmission` (`GltfManagedModelResource.SelectTemplate`).
  The parameter names follow one convention: `g_vGltfBaseColor`,
  `g_flGltfClearcoatFactor`, `g_tGltfNormalTexture`, and so on.
- `AssetPreview` and `PreviewMaterial` render thumbnails in an editor scene.

## Design

### The glTF facets are the contract between glTF and shaders

Each facet (one class per glTF property group, already implemented) gets the
shader parameter of each property:

```csharp
[GltfFacetFor( "materials", "extensions/KHR_materials_clearcoat" )]
public sealed class MaterialClearcoatFacet : GltfFacet
{
	[Property, ShaderParameter( "g_flGltfClearcoatFactor" )] public float ClearcoatFactor { get; set; }
	...
}
```

A shader supports a facet when its schema has all parameters of that facet.
This gives every rule in the request with no lists to keep up to date:

- **The shader drives the properties.** The inspector shows the facets that the
  picked shader supports. The core glTF properties stay visible because every
  glTF-capable shader must support the core facets.
- **Shader function to extension.** A shader feature or parameter that belongs
  to an extension facet adds that extension to the material when the user
  turns it on. `extensionsUsed` is kept in sync (already implemented).
- **Extension data limits the shader list.** The picker only offers shaders
  that support every facet present on the material. A material with
  clearcoat data cannot pick a shader without clearcoat parameters.
- **User shaders work too.** A project shader that uses the same parameter
  names supports the same glTF features.

### Shader parameters that glTF cannot express

A custom shader can have parameters with no glTF meaning (wind strength, a
detail texture). These are stored in an s&box material extension, with the
shader choice and the physics surface:

```json
"extensions": {
  "SBOX_material": {
    "shader": "shaders/foliage.shader",
    "features": { "F_WIND": 1 },
    "parameters": { "g_flWindStrength": 0.4 },
    "textures": { "g_tDetail": { "index": 7 } },
    "surface": "wood"
  }
}
```

The extension is optional, so other glTF tools ignore it and still show the
standard material. The runtime must read it: use the named shader and apply
the extra parameters on top of the glTF mapping. That is an engine change in
the material template selection.

### Textures

A slot shows a thumbnail (the image previews already exist), the texture
name, and a picker with every image in the file plus "Import image...". The
import adds a glTF image with a relative `uri` and a texture that points at it.
The current override storage patches existing elements by index and cannot add
elements, so texture import needs either append support in the overrides or
writing the glTF directly (decision 1).

### Material list with spheres

For each material, build an in-memory glTF that holds one UV sphere with that
material, load it with `GltfModelLoader.LoadPreview` (added for the mesh
preview), and render it to a thumbnail in an editor preview scene. This uses
the same runtime path as the model, so the sphere matches what the model
shows. A stock `.vmdl` sphere with the material as an override would not:
the glTF shaders expect the glTF vertex layout.

## Hierarchy depth

`timber-house.glb` has three root nodes and no children, so a flat list is
correct for that file. Nested files (for example `CesiumMan` and `BrainStem`
in the Khronos set) will show depth through the default GameObject nodes.

## Decisions for the owner

1. **Where edits are stored.** The current implementation keeps the source
   file unchanged and stores edits as patches in the `.meta` file.
   `research/gltf-model-editor-direction.md` says model data belongs in the
   glTF/GLB and `.meta` is only for import settings. Texture import (adding
   images) does not fit the patch format. Recommendation: write edits into
   the glTF/GLB on save, as the direction says, and drop the `.meta` patches.
2. **`SBOX_material` extension.** Approve an s&box extension for the shader
   choice, extra parameters and surface, and the engine change to honor it.
3. **Order.** Recommended: (a) shader picker and schema-driven UI over the
   three glTF shaders, (b) texture slots and the sphere thumbnails, (c)
   `SBOX_material` with custom shaders, (d) runtime support for it.
