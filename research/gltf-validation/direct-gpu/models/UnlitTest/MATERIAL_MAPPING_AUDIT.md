# Unlit stock material audit

2026-11-05, offline source/binary metadata inspection only. No stock material was
loaded or rendered by this model worker, and no shared implementation was edited.

## Existing coupling

`engine/Sandbox.Engine/Resources/Model/Gltf/GltfStockMaterialAdapter.cs` rejects
`material.Unlit` before native allocation. The direct GPU backend calls its
`Prepare` and `Acquire` without making a native Model. The historical native-model
loader also uses this adapter; changing it therefore needs explicit ownership and
ordinary-material regression coverage. `GltfMaterialCache` copies its template,
uses template reference identity in its key, and owns leases for the copies.
The ordinary stock template `materials/default/white.vmat` names
`shaders/complex.shader_c`. The compiled complex metadata exposes no `F_UNLIT`
combo, so inventing that feature name would not be a valid implementation.

## Actual provided candidates

| Shader resource | SHA-256 | Audited offline metadata |
|---|---|---|
| `game/core/shaders/unlit.shader_c` | `d137f9acbdb0987a9476a63cfcbca84ef207ba622ce09cca191e263b3453f24c` | Default mode; POSITION/PosXyz, COLOR, TEXCOORD; aspect-fill and translucent features; `g_tColor`, `g_vViewport`, `g_vImageAspectRatio`; no visible material tint. Appears image/screen oriented. |
| `game/core/shaders/tools_textured_unlit.shader_c` | `6c708d514e5d7bcfb473898fec52127e2308ff73fb85aebf3376d7a02e168b4e` | ToolsUtil mode; 3D POSITION/PosXyz, NORMAL, TEXCOORD and instance transform input; `g_tColor`, `g_flOpacityScale`, translucent feature; no visible COLOR, tint or sidedness feature. |
| `game/core/shaders/complex.shader_c` | `b30471c714e1828378b357d0e5c8ddb697b5a5acacee3b3da69973b21b172d9a` | Ordinary stock PBR shader, with tint/normal/MR/alpha/backface parameters; no visible `F_UNLIT`. |

These are identities of files on disk, not identities of shader programs loaded
in the editor. Printable binary metadata is a discovery hint, not a proof of
sampling, 3D transformation, runtime declaration acceptance or color transfer.
`game/templates/unlit.shader` is a template that returns constant white, not the
source of the shipped compiled `unlit.shader_c` and not a ready glTF material.
`vertex_color.shader` still calls the ordinary lighting combiner and therefore
does not provide an unlit substitute. `sprite.shader` has unlit color output but
fixed sprite UV layout, no culling and sprite-specific blend/depth state, so it
does not meet general glTF material requirements either.

## Required coordinator audit

Inspect `Shader.Load(path).Schema` (Variables and Combos), actual material
`ShaderName`, current program load identity, and usable native draw mode. A
matched raw 3D triangle must verify object/world projection and depth/culling,
not merely successful `Material.Create`. In particular, test whether
`tools_textured_unlit` can participate in the established native Forward/default
scene presentation despite its ToolsUtil mode. Use an exact linear RGBA32F
constant texture to establish factor transfer without 8-bit quantization or an
invented tint binding. Record actual shader/resource/program identity and color
output with the existing capture controls.

## Conditional bounded implementation proposal

Only after that audit and explicit file handoff: add a separate unlit preparation
and acquisition branch in `GltfStockMaterialAdapter.cs`, with an explicit prepared
unlit flag. Keep the existing opaque/MR branch unchanged. Select the proven stock
template only for this branch, keep its cache identity distinct, and use the
existing texture/material leases. This untextured sample can represent each exact
linear base-color factor with a 1x1 RGBA32F resource using the existing VTex writer;
it needs no normal or metallic/roughness resource. No global features or ordinary
material mutation are involved.

This is a **conditional sample-sized proposal**, not approval or a claim of full
extension support. General unlit texture-factor multiplication, vertex colors,
alpha and sidedness cannot silently degrade. If the stock candidate cannot meet
those requirements, first review the supported ShaderGraph unlit output route
(`GraphCompiler.GeneratePixelOutput` returns the authored color directly) rather
than assuming a shader feature exists. Any new shader/source stage belongs to
the coordinator and needs a separate reviewed freeze.

Acceptance is the default source-material path with both independently authored
colors, different normals across faces, two substantially different lighting
setups, an explicitly derived lit control that responds to those lights, matched
independent constant-color references and clean disposal. Diagnostic geometry,
successful preparation, material flags or source/compiled agreement cannot prove
unlit shading. The model's `UnlitTestImageChecks` implements those numeric image
comparisons once actual captures and independent interior masks are supplied.
