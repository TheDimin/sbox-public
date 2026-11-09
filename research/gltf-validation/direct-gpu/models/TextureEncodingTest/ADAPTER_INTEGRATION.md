# Approved source-ORM adapter slice

Root approved sole ownership of the existing `GltfStockMaterialAdapter.cs`
(Prepared, Prepare, Acquire and helpers), new focused tests, and this model's
provided Surface/Lit candidate graph/gate after the coordinator released the
source freeze. Immutable stage `fee5` remains unchanged; the explicit source
route is implemented for a later stage. Fox retains backend template selection
and raw-draw attribute
merging; the sampler owner retains provided graph sampler annotation generation;
the UV owner retains canonical per-primitive selection/projection.

The current default prepares base-color sRGB resources, but converts linear
source metallic/roughness into two RGBA32F resources matching the complex
shader's packed normal/roughness and metalness layouts. Factors are baked into
these resources. It then always borrows `materials/default/white.vmat`, and
assumes that template's feature and parameter names. This representation cannot
be reused as direct source ORM by merely changing its texture names.

The existing `GltfMaterialKey` already includes template identity/revision,
feature values, vector/scalar values, texture identity/revision, and named
sampler states. Its cache and lease rollback need no new ownership system or
codec. Typed per-role sampler attributes must reach the raw draw through Fox's
material attribute merge. The old stock sampler override's metadata alone is
not proof that the actual shader receives it.

The implementation adds explicit `PrepareSource` and `AcquireSource` routes
inside the existing adapter, consumed first by the candidate gates. The ordinary
`Prepare`/legacy template behavior, including the retained
unlit/alpha rejection controls, stays unchanged until the actual candidate and
root's default-template handoff pass. No extra unattached plan class is needed.

The source route consumes `GltfMaterialTextureEncodingPlan` and resolves only
selected roles. It uses existing `GltfTextureCompiler.Compile` or
`CompileWithMipmaps` with each binding's actual `IsSrgb`; the Basis owner's
existing general dispatcher remains the codec owner. Preparation deduplicates
resources by image, transfer and mip demand. One linear source texture may
therefore serve metallic/roughness and occlusion through independent role
bindings/UVs/samplers. No per-model shader, RGBA32F source repack or factor
texture is introduced. Native resources retain existing cache/lease disposal.

The adapter's source and legacy acquisition/disposal paths reuse the reviewed
exhaustive cleanup helper. Acquisition errors remain first, alongside all
release errors. Root additionally approved the two lower cache fault paths
(`GltfMaterialCacheCore<T>.Acquire`, native `GltfMaterialCache.Create`): a created
resource's release could previously replace its validation/binding error. Their
cleanup now retains both, without changing key/dedup/ownership policy. Three
fault-injection tests cover invalid creation, throwing liveness inspection,
shutdown during creation, retries, and retained live peer leases. These fixes
and tests require actual next-stage execution; source inspection is not a pass.

Four independent model resource cases check direct RGBA8 pixel preservation,
transfer/mip cache separation, role sampler independence and unbaked factors.
Three focused unit cases check source preparation, missing image failures and
factor-only laziness. Two guarded native template cases check the actual
compiled absent-role white texture defaults and linear factor/zero-emission
values, repeated material sharing and peer/template lifetime. Those two cases
read actual default texture GPU bytes but do not prove final shader/PBR pixels.
The current OPAQUE uniform readback checks XYZ/scalar X; alpha enablement must
add the corresponding exact W/coverage acceptance rather than inherit this gate.

Provided material uniforms use the same family as the unlit candidate:

| Authored input | Ordinary material parameter | Arithmetic |
|---|---|---|
| Linear base factor | `g_vGltfBaseColor` | Multiply sRGB-decoded texture RGB and clamped linear COLOR_0 RGB once |
| Metallic factor | `g_flGltfMetallicFactor` | Multiply source linear B once |
| Roughness factor | `g_flGltfRoughnessFactor` | Multiply source linear G once |
| Occlusion strength | `g_flGltfOcclusionStrength` | `lerp(1, source R, strength)` |
| Linear emissive factor | `g_vGltfEmissiveFactor` | Multiply sRGB-decoded emissive RGB once |

For each supported role `Role`, the candidate declares texture
`g_tGltfRoleTexture`, typed sampler attribute `GltfRoleSampler`, scalar UV
selector `g_flGltfRoleUv1`, and affine vectors `g_vGltfRoleUvRowU` /
`g_vGltfRoleUvRowV`. Selectors must be validated as exactly 0 or 1 before
realization. Rows come from the corresponding canonical UV binding, evaluated
as `dot(float3(selectedUv,1),row)` independently for each role. The four current
roles are BaseColor, MetallicRoughness, Occlusion and Emissive. A source set
beyond UV1 remains preserved by the CPU plan and explicitly pending in this
candidate; it must never be mapped silently to UV0.

Per-primitive canonical UV/source-descriptor validation remains outside the
shared material cache. Completed material/UV selections must be paired in the
same published slot generation. A literal baked-UV reference must use an
independent identity-transform reference material to avoid transforming twice.

`surface_orm_candidate.shdrgrph` contains only provided graph nodes, generated
deterministically by `../../../../../engine/Tools/Gltf/direct-gpu/models/TextureEncodingTest/author_surface_orm_candidate.py`. The coordinator calls
`SurfaceOrmCandidateGate.GenerateForReview(checkout, freshOutput, toolsMvid)`
against the actual active editor type registry and actual graph compiler. It
requires the sampler owner's real optional `Sampler.Attribute` implementation,
verifies the resolved sampler identities and reviewed generated declarations,
and freezes generated source for review before compilation. Provided texture
nodes can compile their default preview texture during construction, so even
generation belongs to the guarded coordinator. No graph/compiler code is copied
into the helper.

Runtime gates still required before production selection: compile the exact
reviewed generated source; actual source ORM and linear factors versus literal
numeric expectations; independent per-role UV/sampler controls; explicit
lighting response and normal/backface checks; native empty/positive/deleted
controls; cache/lease rollback and repeat disposal; compiled artifact dependency
closure. The separate transfer diagnostic's original compiler failure and the
Core28 pass remain actual evidence. No source-color claim can use only two
images rendered with the same material as its expected value.

Current candidate limitations are explicit: OPAQUE only, source UV0/UV1 only,
no normal texture, no glTF MASK cutoff or BLEND ordering, and no final shading
or shipped compiled template proof. Alpha and normal owners have the same
family contract and are preparing their separate model gates. Unlit remains
the existing untextured OPAQUE provided candidate until its own current-producer
red/control and runtime proof permit integration.
