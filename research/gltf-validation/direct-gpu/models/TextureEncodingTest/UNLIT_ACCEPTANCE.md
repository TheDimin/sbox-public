# Unlit source candidate acceptance

`../../../../../engine/Tools/Gltf/direct-gpu/models/TextureEncodingTest/gltf_unlit_five_uv.shader` is an uncompiled, non-default candidate using the
same projected physical UV semantics `[0,1,4,5,6]` as the five-UV lit candidate.
The host must prepare only the base-color role and pass its selected stream,
affine rows, independent sampler, sRGB texture and linear factor. Ignored
metallic/roughness, normal, occlusion and emissive inputs must not resolve
images or UVs. `GltfUnlitSourceMaterialAdapter` accepts an explicit template;
it does not advertise the extension or select this shader in the public loader.

The candidate outputs the linear base-color texture × factor × clamped COLOR_0
product directly, following the unlit output form in s&box's ShaderGraph
compiler. For OPAQUE, authored alpha is ignored. For MASK, discard exactly when
the source alpha product is strictly below alphaCutoff, including cutoff zero
and values above one. For BLEND, output straight RGB and the source alpha
product, with source-alpha / one-minus-source-alpha blending, depth testing on
and depth writes off. Engine instance tint remains separate from the authored
product. These are source-code intentions, not compiled or pixel evidence.

Coordinator acceptance must include: actual compiled features/parameter and
sampler bindings; literal linear color product and sRGB-negative controls;
factor, texture and RGBA vertex alpha independently varied with RGB COLOR_0
alpha-one control; masked below/equal/above cutoff including alpha0/cutoff0;
identical discard in forward, depth and shadow passes; blend against two known
backgrounds and layered sorting with reversed authored node order; depth-write
controls; culling on single/double-sided cases; two independent instances with
whole/slot overrides and clear/reacquire. Existing `AlphaControlFixtures`,
`AlphaBlendModeImageChecks` and `AlphaMaterialRoutingProbe` provide independent
source-alpha, numeric and two-instance classification controls, but a run must
bind actual acquired unlit source materials and identify its producer/template.
The classification probe is not a rendered-pixel or sorting proof. No pass is
claimed until the coordinator runs those gates and public Model.Load selects the
verified source template without replacing vmdl or explicit overrides.
