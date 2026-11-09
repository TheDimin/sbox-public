# No-normal point and line material behavior

The pinned sample has no NORMAL or authored material. glTF 2.0.1 section 3.9.7
recommends that points and lines lacking normals use the sum of base color and
emissive without lighting. This is a SHOULD recommendation. It also recommends
one-pixel viewport widths; size and style are not mandatory.

The public source and compiled captures in receipt `d19189cad4665f571160cd144ac16cae2e97f90be6dbd88f032af8eed188250b`
use `shaders/gltf_stock_sampler.shader` in Lit mode and fixed camera exposure.
Turning the owned direct light black changes point and all three line regions to
black; restoring the light restores foreground. Triangle regions also change,
confirming the lighting control. This is an observed deviation from the
recommended no-normal point/line behavior; the failed receipt's later raster
assertion was an unrelated incorrect region boundary, not a material pass.

A complete implementation must select the program per primitive and selected
material, because the same default or authored material slot may serve both a
triangle and a no-normal line. The no-normal point/line result uses linear base
color factor multiplied by source base-color texture and COLOR_0, plus emissive
factor multiplied by emissive texture, respecting source UV selection, sampler,
alpha and sidedness. Authored-normal point/line and triangles retain their
existing material route. Material variants, overrides, texture leases and draw
ownership must remain coherent. The KHR_materials_unlit adapter explicitly
ignores emissive, so simply setting that flag is insufficient.

A focused regression should measure public Model.Load and ModelRenderer under
light on/off/restored, for source and compiled; independent colored base factor,
texture and vertex color plus nonzero emissive, including a wrong-color and
wrong-emission control; companion triangle and normal-bearing line must remain
light responsive. Recheck alpha and sampler behavior where the route consumes
them. No GPU/index corpus replay is needed for this material-only change.

## Integration design for review

1. Determine `unlitNoNormal` from each `GltfGpuPrimitivePlan` after codec
   decoding (`Mode` is 0 or 1 and `Upload.Attributes` lacks `NORMAL`), never
   from an authored material index or a model-wide flag. Recognize explicit
   KHR_materials_unlit independently; it continues to ignore emission as its
   own extension requires.
2. Ensure selected scenes containing at least one such primitive take a packet
   route capable of mixed programs, even when every primitive uses the implicit
   default material. Do not alter the source material slot table: its public
   indices and nullable default identity remain source-derived and stable.
3. Classify/acquire the effective packet by `(primitive, selected material)`.
   Existing variant packet ownership already keys a primitive and choice; add a
   separate no-normal program choice alongside lit and KHR unlit. A point and
   triangle sharing source slot must acquire distinct effective programs without
   creating extra public material slots. Each packet projects both base-color and
   emissive roles from the selected material's own TEXCOORD streams, transforms,
   encodings, UV projections and per-role sampler states. An implicit material
   supplies default white base, zero emission, opaque alpha and no textures.
4. Runtime overrides retain existing meaning: a stock vmat override uses its
   requested material and raw stream route; clearing it restores the selected
   source packet. Variant changes acquire and swap candidate packets atomically,
   preserve source generation and defer disposal under existing lifetime rules.
   Classification for alpha/depth/sorting must reflect the selected effective
   material, and unsupported transparent routes must retain explicit errors or
   existing guarded behavior, not silently turn opaque.
5. Candidate shader carries base plus emissive, independent sampler attributes
   and five projected UV inputs, and retains current opaque/MASK/BLEND features;
   nevertheless no transparent production acceptance follows from compilation.
   The existing KHR-unlit adapter cannot represent this equation since it omits
   emissive; use a separate prepared material plan and key, retaining texture
   leases independently. Avoid material-cache collision between this program and
   the same authored source index's lit or explicit KHR-unlit program.

Required actual mixed-scene validation: one shared authored slot on missing-
normal points/lines, normal-bearing points/lines and triangles; an implicit
slot; source/compiled with a variant changing material inputs; two independent
instances and override/clear; light on/off; separate role color+texture, UV,
sampler, alpha/cutoff and emission negative controls; deletion/reload and
failure rollback. Preserve and do not replay unchanged GPU index tests. Route
selection must be checked through public Model.Load and ordinary ModelRenderer;
an explicit research resolver cannot establish it.

## Applicability correction and shared-material alternative

The missing-normal-only UV fixture used in initial design discussion is not a
valid positive core fixture. glTF 2.0.1 sections 5.20.2, 5.21.2 and 5.30.2
require the referenced coordinate set for the material to apply. The pinned
Khronos validator reports three errors on the missing-UV witness and zero errors
on its paired fixture with TEXCOORD_0 present (two tangent-space warnings).
Reports and inputs are in `Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/validator-uv/`;
`../../../../../engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/validate_missing_uv.py` reproduces them. An unlit rendering recommendation
does not itself remove this applicability requirement. Do not add fabricated
zero-stream handling based on that invalid fixture.

Root selected preservation of the public mutable material handle as an
important requirement. The superseded `public-point-line.proposed.patch` uses
separate lit and point/line Material realizations and is not for integration.
`public-combined.proposed.patch` is a new **unbuilt, unrun draft** that marks
source material indices used by any no-normal point/line across reachable
variants. All primitives using each such selected material route through one
combined source shader and the existing full-role projection, retaining one
material cache key where role slot mappings coincide. A per-draw attribute,
set after material and global attributes and suppressed for explicit overrides,
selects the unlit branch. A separate seven-UV candidate derives from Mandarin's
frozen transmission shader to preserve transmission triangles; that candidate
also requires native equivalence tests. The old stock-to-source shader change
for triangles and preservation of existing material APIs remain acceptance gates.
The combined shader is not installed and this design does not imply support.

## Legacy stock parameter preservation

The `public-combined.proposed.patch` is also held and not integration-ready: it
would move previously legacy-compatible shared slots to a source-lit shader and
change the public material parameter surface. The existing legacy material uses
`g_vColorTint` (sRGB-encoded factor), `g_tColor2` and stock sampler bindings;
source-lit instead uses `g_vGltfBaseColor` (linear) and different texture names.
A shared object alone does not preserve clients' `Material.Set` behavior.

The narrower candidate modifies the existing stock shader's pixel path only:
`../../../../../engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/stock_draw_attribute_candidate.shader` retains existing `PS_SharedStandardProcessing`
and `PS_FinalCombinerDoPostProcessing`, choosing its no-normal point/line result
from `SampleColorTexture * i.vVertexColor` instead of the lighting function.
The stock vertex path already applies glTF linear COLOR_0 once and converts the
legacy mutable tint. The legacy adapter supports opaque, zero-emission material
only, so the point/line branch's omitted emission is valid for that subset.
Source-lit and diffuse-transmission materials retain their existing program
family with separately reviewed combined variants, stable source material keys
and full-role projection. This candidate remains uninstalled; the frozen native
probe checks baseline triangle, tint/texture mutation and linear vertex-color
controls, but fog/depth/tool-mode pixel coverage also remains to be measured.
