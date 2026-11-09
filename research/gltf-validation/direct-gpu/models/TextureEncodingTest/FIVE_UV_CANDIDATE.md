# Staged five-input Surface/Lit candidate

`../../../../../engine/Tools/Gltf/direct-gpu/models/TextureEncodingTest/gltf_surface_five_uv.shader` is source for review, not the selected public
material or a compiled shader. It uses the stock `ProcessVertex`,
`FinalizeVertex`, `Material` and `ShadingModelStandard` path. It currently
implements an opaque candidate only; MASK, BLEND, normal generation, complete
double-sided handling and tint fade still require their own integration and
runtime evidence.

The shader's compact source slots 0–4 use vertex TEXCOORD semantics
`[0, 1, 4, 5, 6]`. TEXCOORD2 and 3 are left to the common curvature and
lightmap declarations. The pixel interface carries the last three inputs in
TEXCOORD8 and 9. These declarations avoid conflicts visible in the checked-in
common includes; actual shader compilation and native vertex declaration
matching have not been proved. `GltfGpuPrimitivePlan.ProjectMaterial` accepts
that physical semantic mapping separately from role slot numbers.

Each role has its own texture, typed sampler attribute, presence flag, compact
UV selector, and two affine rows. `BaseColor` and `Emissive` request sRGB reads;
`MetallicRoughness`, `Normal`, and `Occlusion` request linear reads. The
candidate compensates for the legacy Y flip inside stock `TransformNormal`.
It multiplies source linear vertex colour and the engine instance tint
separately. Actual binding, filtering, mip selection, colour transfer and
surface output must be checked on the GPU.

This projection must be paired atomically with the material for the same
primitive and instance. The original raw vertex declaration and output route
must be available for arbitrary native material overrides. Do not select this
candidate or advertise rendering support based on source and CPU tests alone.
The adjacent Python tests check static declarations and the independent
asymmetric-normal oracle; they do not compile or execute HLSL.
