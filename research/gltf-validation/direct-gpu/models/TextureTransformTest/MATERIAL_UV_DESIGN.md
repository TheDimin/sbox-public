# Material and UV boundary

Status: the managing agent approved the CPU-only UV plan, and its source and 18
focused tests are implemented and frozen for actual Engine staging. Twenty-eight
model data/resource cases passed in the inspected editor receipt; four verifier
cases, six new real-model UV-plan consumer cases, four identity-reference
prerequisites and eight native material-binding cases remain unrun. This document does not establish shader/image
support. Existing native sampler failures remain open.

## What the current engine actually provides

| Inspected source | Capability and practical limit |
| --- | --- |
| `game/core/materials/default/white.vmat` | Uses the provided compiled complex shader; serializes primary `g_vTexCoordScale` and `g_vTexCoordOffset`. A serialized field is not proof that a particular loaded shader program uses a new runtime binding. |
| `game/core/shaders/vr_shared_standard_vs_code.fxc`, lines 27–69 | Shared vertex processing scales then offsets **one primary UV**. It has no general primary rotation or per-material-role UV selection. Source inspection does not establish that the shipped compiled complex program matches these includes. |
| `game/core/shaders/vr_detail_texture.fxc`, lines 8–42 | Detail UVs have a rotation, but it uses degrees and a centered transform coupled to detail texture semantics. It is not the glTF origin-based, radian transform and is not a substitute for independent core texture roles. |
| `game/core/shaders/common/material.hlsl`, `Material::From` | The legacy common-input path samples color, normal and RMA using the same `i.vTextureCoords.xy` and filtering state. Merely exposing a second source UV does not give each role an independent mapping. |
| `game/core/shaders/common/utils/Material.CommonInputs.hlsl` | The common-input helper declares one `TextureFiltering` sampler with a small filtering feature selection. This is not proof of independently selectable min/mag/mip behavior. |
| `game/editor/ShaderGraph/Code/Nodes/Texture.cs` | TextureSampler accepts explicit coordinate expressions. TextureCoord selects primary or secondary UV; the current node exposes tiling, not the complete glTF affine operation as one node. Separate graph expressions can perform that operation. Instantiating texture nodes may compile resources; this worker only reads source. |
| `game/addons/tools/Code/ShaderGraph/Compiler/GraphCompiler.cs`, `ResultTexture` / `GenerateGlobals` | Distinct sampler descriptions are deduplicated and emitted as separate `g_sSamplerN` declarations. This is a viable per-role mechanism, subject to actual native execution proof. |
| `game/addons/tools/Code/ShaderGraph/Compiler/ShaderTemplate.cs`, generated vertex path, and `game/core/shaders/sbox_vertex.fxc` | The provided graph calls `ProcessVertex`, which calls `VS_CommonProcessing` directly. `vr_common_vs_code.fxc` copies raw UV0/UV1 into xy/zw; the legacy shared-standard primary scale/offset stage is not part of this path. Final native interpolation/selection still needs pixels. |
| `game/addons/tools/Code/ShaderGraph/Sampler.cs` | Addressing includes wrap, mirror and clamp. Exposed filters are Aniso, Bilinear, Trilinear and Point. These four choices must not be advertised as complete independent glTF min/mag/mip support. |
| `game/core/shaders/common/material.hlsl` and `common/shadingmodel.hlsl` | Material properties can be populated before calling the provided standard lighting implementation. This offers an engine material-system route without implementing a separate Khronos lighting model. The unlit worker separately audited ShaderGraph's direct unlit output route. |

`complex.shader` source is not shipped in this checkout. Editing a public include
does not recompile the existing complex binary. Binary metadata and Schema are
discovery evidence; final binding and pixels still need the coordinator's native
controls. The UnlitTest worker's [audit](../UnlitTest/MATERIAL_MAPPING_AUDIT.md)
also rules out inventing an `F_UNLIT` feature on complex.

## Approved CPU contract and remaining material composition

`Materials/GltfMaterialUvPlan.cs` now supplies a CPU-only immutable per-role UV
plan consumed by the Unlit plan and proposed managed-backend integration. It owns
the canonical `GltfMaterialTextureRole` enum and exact source stream references.
The encoding plan separately supplies image/sampler identities and channel/color
policy. Neither allocates a Material, Texture, sampler index, GPU buffer or native
model. Resource realization follows on the established rendering thread.

A material plan contains source material identity, factors, alpha mode/cutoff,
sidedness and lit/unlit mode, plus up to five **role bindings**:

| Role | Texture interpretation | Additional property |
| --- | --- | --- |
| Base color | sRGB RGB, linear alpha | RGBA base factor and vertex color multiplication |
| Metallic/roughness | Linear B metallic / G roughness | Separate metallic and roughness factors |
| Normal | Linear tangent-space data | Normal scale, distinct from UV scale |
| Occlusion | Linear R | Occlusion strength |
| Emissive | sRGB RGB | Linear emissive factor |

Each present role binding records:

- Its original texture-info location and texture/image identity, distinct from
  intentional shared authoring identity or a runtime rendering-equivalence key.
- Effective source `TEXCOORD_n`, after a supported extension's texCoord override.
  Preserve the original source description for round trips and optional fallback.
- A source affine UV map, equivalent to two rows of coefficients and an offset.
  Its meaning is scale, rotation about the UV origin in radians, then translation.
  Do not center, clamp or vertically flip it. This stage should not depend on any
  specific native shader's parameter names or matrix packing convention.
- The requested sampler: independent min/mag/mip choices and S/T addressing,
  including the existing explicit policy for omitted filters. Preserve whether a
  single base mip/view is required. A native sampler index is realization state,
  never portable GLB data.
- Color-space interpretation and role-specific scalar properties. The same image
  used as sRGB color and linear data cannot silently reuse an incompatible view.

Validate each selected material's requested source UV set against the primitive
that will use it. This includes material variants and native overrides with
declared source-channel requirements, not only the original default material.
Unknown override shader channel usage needs capability inspection rather than a
guessed mapping. Failure must identify the material, role and missing set.

The source plan is separate from draw-material context. Realization also receives
topology, whether NORMAL was authored or generated, and whether a native override
is selected. The PrimitiveModeNormalsTest owner confirmed that one source/default
slot is shared across points, lines and triangles, including missing-normal
cases. Do not lose that context by caching solely on material index. Missing
triangle normals require flat generation; they do not automatically select an
unlit material. Point/line lighting conventions are a separate documented style
policy with their actual normative strength, not an invented universal core
failure. Include contextual shader choices in realization keys when they affect
the rendered result, while preserving authored source identity.

## Preserve source geometry; select a material projection

The asset keeps immutable, source-derived UV streams and descriptors. A variant
or per-instance override must not rewrite that shared arena. The source document
and material role plan preserve all required channel information even if a
particular draw consumes only a few channels.

The GPU draw declaration may project the **consumed** source channels into the
input fields of the chosen native material program. Record that projection
explicitly: original set, destination semantic, and whether an affine map was
already applied. A raw-UV readback is transport evidence, not extension support;
the current model checker already makes that distinction. Do not assume the
provided ShaderGraph's two ordinary UV inputs cover every possible source set.
Selecting the needed channels and emitting the corresponding declaration is the
backend owner's work. A hardware/interface limit must be measured and reported.

A compatible existing-complex fast path can remain useful if all consumed roles
use the **same** effective mapping and the native material satisfies the required
sampling/features. For a single base-color texture, an explicitly declared GPU
projection can apply that mapping without mutating source data. Such a fast path
does not justify one global UV bake when roles differ, and cannot turn the known
clamp/filter failures into passes.

For independent role mappings, prefer the provided ShaderGraph/material pipeline
after its native capability audit. Each role supplies its own source coordinates,
affine expression, sampler and texture to the engine material properties. Keep
standard lighting in `ShadingModelStandard` and use the provided unlit graph
route where appropriate. Do not repurpose detail texturing or pack unrelated
normal/MR channels together when they have different UV/sampler mappings. The
existing adapter's flat-normal-plus-roughness packing is a compatible-path detail,
not a general glTF material representation.

No claim is made that the current graph sampler enum covers all glTF filter
combinations. Before choosing implementation, run one bounded native proof for
the sample's LINEAR/no-mip/clamp mapping and a separate probe of independently
different min/mag/mip requests. If an engine Graph API extension or an annotated
native sampler/bindless route is needed, assign and review that exact change.
Do not generate a combinatorial shader family for every material's sampler tuple
without measuring compilation/cache cost. Sampler bindings must be verified with
typed state and pixels; Material.Set returning true is insufficient.

## Variants, overrides and caches

Agree one asset-wide `SetVariant(int?)`/ActiveVariant contract with the variants
owner; null and unmapped slots retain the defined core material. The proposed
implementation resolves all new binding/material rows before an atomic instance
selection commit. Each row chooses the material plan and its source-channel
projection together. Instances keep independent selected rows while immutable
asset buffers and equivalent native resources remain shared.

Keep rendering-equivalence keys separate from source material/slot identity.
Native cache realization keys must cover the chosen program/template, all factor
values, role textures and revisions, color space, UV mapping/projection and
requested/realized sampler policy where those values affect that resource.
If mapping is per-draw instead of material-owned, cache the native program
separately and retain immutable per-instance draw attributes; do not mutate a
shared Material to implement another instance's selection.

The actual established native precedence among whole-model, slot, material-group
and component overrides still needs runtime evidence. Do not invent a new winner
or change vmdl results as part of this plan. Native `.vmat` references/overrides
remain borrowed or explicitly leased according to the existing resource workflow.

## Current implementation ownership

The managing agent assigned these source boundaries explicitly. Files submitted
to the validation coordinator remain frozen until its terminal release.

| Owned change | Assigned owner | Review boundary |
| --- | --- | --- |
| `Materials/GltfMaterialUvPlan.cs` and focused tests | TextureTransformTest worker | CPU UV description only; five role bindings; consumed-role selection; source sets/affine maps; preserve exact source identity and descriptor. |
| `Materials/GltfMaterialTextureEncodingPlan.cs`, provided-template material adapter slice | TextureEncodingTest worker | Role/image/sampler/channel interpretation; direct ORM sampling; source material and native resource/cache lifetime. Exact shared edits require root handoff. |
| `Materials/GltfUnlitMaterialPlan.cs` | UnlitTest worker | Base-color-only consumption, raw linear vertex color, factors, alpha and sidedness; no ignored-role image/UV demand. |
| Direct GPU backend source-channel projection and per-instance selection rows | Fox/shared-backend owner | Original accessors/arena immutable, dynamic output fields, variant/override isolation, atomic commit, native model count remains zero. |
| Generic ShaderGraph annotated sampler support | TextureSettingsTest worker | Optional sampler attribute and generator output; typed exact runtime state; no glTF-specific names in the generic compiler. |
| Actual Schema, candidate program and sampler/color-transfer captures | Validation coordinator only | Existing guarded visible editor, independent expected patches, failed controls retained. |

The agreed initial provided-template binding for each consumed role is
`g_flGltf{Role}Uv1` (validated float 0 or 1), plus
`g_vGltf{Role}UvRowU` and `g_vGltf{Role}UvRowV`. Select unmodified raw UV0/UV1,
then evaluate each row against `(u,v,1)`. Encoding owns the exact candidate and
material-key wiring. Shared primary UV scale/offset must not run before these
rows. The native template's two ordinary UV inputs do not establish arbitrary
`TEXCOORD_n` support; source plans preserve those sets and realization must reject
an unavailable mapping clearly until Fox supplies a declared projection.
The initial Surface/Lit candidate consumes base color, metallic/roughness,
occlusion and emissive. Normal textures, alpha modes and double-sided behavior
remain explicitly rejected there until their separate implementation and actual
template gates pass.

Fox's proposed ownership seam retains the exact original upload with its owning
document, mesh and primitive. `RequireSourceOwnership` checks all identities,
then `RequireSourceStreamOwnership` checks each exact selected stream reference.
Matching accessor numbers or an equivalent layout is insufficient. This closes
the UV factory's documented caller precondition when wired before packet
acquisition/publication; actual negative integration evidence remains pending.

First source-plan tests should include the existing per-role fixture: material0's
UV0 is accessor2's half quad while UV1 is accessor1's unit quad. Different sets are
already present, so an implementation always choosing UV0 must fail the later GPU
and image gate. Preserve the existing frozen 17 cases; new explicit byte and
projection sensitivity controls can be a separate freeze.

## Acceptance sequence

1. Retain the inspected 28 passing model data/resource rows. Run the four pending
   verifier cases and the 18 focused, six model-consumer and four identity-source
   prerequisites against their actual producer generation. Preserve failed rows.
2. Inspect bounded actual stock/Graph Schema and native input acceptance. Test
   primary scale/offset without assuming those fields support rotation or roles.
3. Add source-plan tests first, including distinct UVs/affine maps/samplers for
   multiple roles, negative/large transforms, optional fallback and required
   rejection, and variant selection that changes the demanded UV set.
4. Execute GPU data checks with the declared raw/projected representation and
   source-channel provenance. Prove two instances select different mappings
   without changing shared source bytes or each other's output.
5. Execute the six prepared transform image rows and missing-transform negative
   controls, using the actual default material path. A reference borrowing the
   same texture material isolates geometry/UV agreement only.
6. Close sampling independently with decoded source PNGs/linear-light bilinear
   clamp landmarks and constant-color shader/lighting-transfer controls. Shared
   wrong samplers cannot become an oracle. Extend to all required min/mag/mip and
   wrap modes before claiming complete core sampling support.
7. Recheck unlit/light response, variants/override behavior, default model APIs,
   resource reuse/disposal, packaging and the unchanged vmdl branch. Full model
   corpus, performance and platform gates remain separate requirements.
