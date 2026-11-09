# Material identity and override audit

`Model.Materials` lazily caches `GltfResource.GetMaterials()` in
`Model.Materials` (Model.Material.cs). `ModelRenderer.Materials.GetOriginal(i)`
returns exactly `Model.Materials[i]`; neither is a source-material wrapper.
`GetProjectedMaterials()` (GltfGpuAsset.Variants.cs) selects the *first*
per-primitive packet material for each source slot and caches that array, while
each draw's variant state selects its own per-primitive packet. The packet cache
key is `(mesh, primitive, material)`; packet realization uses projected UV slot
and transforms in material cache keys. Thus differing UV projections for two
primitives sharing a material index can already produce distinct native Material
realizations on the existing projected route. This is a source-level inference;
no current live direct-mutation observation has yet been run.

`Material.Set` writes the specific native Material and marks that native object
edited (Material.Parameters.cs); no material dirty-version propagation or
shared-parameter rebind mechanism was found in the managed direct-GPU path.
A direct mutation to the returned material is consequently not demonstrably
propagated to other packet realizations. The draft adds a second reason for
distinct native materials (lit versus no-normal point/line shaders), so it must
not claim direct mutation parity. The existing unprojected legacy route assigns
one material per source slot and therefore does not have that particular split.

Explicit replacements have a different, traceable path: `MaterialAccessor` keeps
indexed overrides; `ModelRenderer.ApplyGltfMaterialOverrides` constructs the
source-slot vector with whole `MaterialOverride` taking precedence over indexed
entries; `GltfGpuInstance` applies the vector by `primitive.MaterialSlot` before
variant packets and routes overrides through the raw vertex declaration. This
should replace *all* packet realizations sharing a source slot, and clear should
restore selected packets. Both component-level and internal asset-instance
commands need an actual mixed-slot regression. Tagged glTF overrides are
explicitly rejected by the existing public facade.

Options for root review:

1. Keep stable slot indices and existing explicit override vector; establish
   the direct-mutation behavior as an existing projected-route limitation with a
   live two-UV baseline and mixed-program regression. This avoids changing the
   API but leaves direct native material edits non-propagating where realizations
   differ.
2. Design a shared source-material parameter state/facade and explicit change
   notifications, with mappings into each shader program and transactional
   update/lifetime ownership. This is a broader public API and asset-lifecycle
   change; arbitrary shader-specific parameter names are not automatically
   translatable, and native Material.Set has no managed interception here.
3. Reject mixed realization for one source slot until shared-state semantics
   exist. This preserves neither desired sample behavior nor broad compatibility;
   it makes a valid asset fail, so is undesirable without a deliberate decision.

Do not add per-frame polling or copy arbitrary native material parameters to
conceal the mismatch. Preserve observed API behavior and test all explicit
whole/slot override and variant transitions separately from direct mutations.
