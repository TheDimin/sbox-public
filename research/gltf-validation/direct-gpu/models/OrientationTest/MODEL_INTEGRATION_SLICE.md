# Managed Model resource and ordinary renderer slice

Status: the managing agent approved the listed bounded file ownership and
changes. The unchanged original 25-case suite has an actual red baseline
(7 passed, 18 failed); Fox's CPU definition tests also ran red before extraction.
The first managed Model/ordinary renderer implementation was built and installed
as the frozen `fee5` stage. All 25 public resource/component/vmdl cases pass
after a measured correction to the test's strong-handle validity predicate;
the same three-way native contract controls, six async and ten lifecycle cases
also pass. The producer did not change for that predicate correction, and all
earlier reds remain recorded.
The coordinator owns all builds, shader promotion, installation, restarts,
editor calls and captures; see `EVIDENCE.md` for exact receipts and remaining
image, packaging and API gates.

## Ownership and single implementation

The Fox worker owns the single authoritative backend/rig extraction into
`engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/`. Its namespace remains
`Sandbox.Gltf.DirectGpu`, with internal engine types accessible to the existing
`Sandbox.Test.Unit` friend assembly. Research source copies must be removed,
and helpers must then reference the loaded engine implementation. The parser
assembly `Sandbox.Gltf` remains independent of rendering/native dependencies.

Fox also owns backend metadata, lazy realization, instance material hooks and
exact final-release semantics. The coordinator alone promotes the one compute
shader source to its reviewed core-content location and compiles it under the
guard. Loading a model must not compile anonymous shader source or require a
graphics device merely to prepare metadata.

The Orientation worker owns the Model resource and ordinary `ModelRenderer`
surfaces below. No generated interop, native ABI changes or unrelated dirty
work are part of this slice. Existing native model lifetime/cache fixes remain
intact for vmdl.

## Agreed backend contract

```csharp
GltfGpuAsset.Prepare(
    GltfDocument document,
    string compiledComputeShaderPath,
    Func<int?, Material> materialResolver = null,
    Action finalRelease = null);
```

Preparation performs the one CPU scene traversal, source upload planning and
shared immutable rig definition. It does not construct a native Model/Mesh,
allocate GPU buffers, resolve the compute shader, invoke a material resolver or
realize textures. Metadata includes:

- Exact unpadded authored-rest `RestBounds`.
- Selected node/primitive instance `MeshCount`.
- `MaterialSlotSourceIndices`: used authored material indices in ascending order,
  with the nullable default-material slot last when used. This mapping is
  independent of material-resource deduplication.
- The same retained `GltfDocument`, without another wrapper traversal/decoder.

`GetMaterials` is lazy and separate from vertex GPU realization. It preserves
real material-slot identity and uses the existing stock adapter/cache; it never
fabricates an empty `Material[]` merely because the renderer is unavailable.
`CreateInstance` realizes the shared GPU state only when scene presentation
requires it. Each instance exposes its real `SceneCustomObject`, full-affine
`Placement`, `SetPose`, and independent whole/per-slot material override state.
Effective override precedence awaits the single Variants worker's native
component-level probe; source call ordering alone is not a measured rule.

The final-release callback transfers to the asset only when `Prepare` succeeds.
If preparation throws, the caller still owns and disposes its document. Releasing
the owner rejects new instances, while existing instances retain that generation
of CPU data, GPU resources, material leases and document. Final cleanup runs once
on the main thread in the captured `GlobalContext`, followed by the callback.
The Model wrapper must not introduce a second asset/instance lifetime counter.

## Proposed bounded Model changes

The listed existing-file edits are approved after those execution/interface
gates; files are listed so review can inspect the resulting concrete diff.

| File | Intended change |
|---|---|
| New `Resources/Model/Gltf/GltfManagedModelResource.cs` | Adapt the prepared backend to resource metadata, lazy material reads, instance acquisition and owner release; capture the source document exactly once. |
| New `Resources/Model/Gltf/Model.GltfResource.cs` | Store explicit managed-resource identity/state and add its internal Model constructor. Register the full logical path through the existing weak resource registration. No native handle or synthetic GUID is created. |
| `Resources/Model/Model.cs` | Dispatch validity, error state and mesh count; release the managed owner without entering native model destruction. Leave the native branch/cache behavior unchanged. |
| `Resources/Model/Model.Bounds.cs` | Delegate mesh/render bounds to exact managed rest metadata rather than dereferencing a null `IModel`. Other optional data requires its documented empty or unsupported semantics. |
| `Resources/Model/Model.Material.cs` | Delegate lazy material slots. Core-only models have no authored material groups; variant/group integration remains explicitly required separately. |
| `Resources/Model/ModelExtensions.cs` | Make `HasRenderMeshes` recognize valid managed geometry without native scene meshes. |
| `Resources/Model/Model.BodyPart.cs` | Supply the legitimate empty read model for core assets with no authored bodygroups. Do not fabricate successful authored bodygroup support. |
| `Resources/Model/Gltf/GltfModelLoader.cs` | Replace glTF native construction with the managed resource path, retaining audited filesystem confinement, URI resolution and extension diagnostics. No native fallback. |
| `Resources/Model/Model.Load.cs`, if necessary | Preserve original `Load`/`LoadAsync` path identity and shared cache behavior. GUID-only/compiled identity work must follow an audited mapping rather than a filename guess. |

`IsValid` must not mean “GPU realized.” A successfully prepared CPU model can be
valid before rendering. Destroying the Model invalidates that resource/cache
owner immediately; retained renderer instances keep the old physical generation
until their own disposal. Cold loading then creates a new resource generation
without resurrecting the explicitly destroyed wrapper.

## Proposed ordinary ModelRenderer changes

| File | Intended change |
|---|---|
| New `Scene/Components/Render/ModelRenderer.Gltf.cs` | Own one managed GPU instance for this component and centralize creation, metadata/application and ordered disposal. |
| `ModelRenderer.cs` | Branch before native mesh-group/model dereferences; expose the real custom scene object; handle model replacement and component enable/disable/destroy. |
| `ModelRenderer.Deformations.cs` | Select the managed instance when creating the scene representation and prevent fallback through native Model/SceneModel creation. Unsupported authored modifiers remain explicit failures. |
| `ModelRenderer.Materials.cs` | Route whole/per-slot overrides through the instance according to measured native precedence; retain material leases/asset data independently of overrides. |
| `ModelRenderer.Bounds.cs` | Read managed local bounds and apply the normal component transform without native model data queries. |

The GPU output is already in engine world coordinates after `Placement`.
Consequently `SceneCustomObject.Transform` stays identity; the component updates
the instance matrix instead of applying its world transform a second time.
For component placement, use existing `GameTransform.InterpolatedWorld`
semantics converted once with `Matrix.FromTransform`, without another TRS round
trip. That existing engine hierarchy is TRS and does not preserve inherited
shear; this adapter does not silently substitute a different hierarchy policy.
Raw glTF node/skin matrices and explicit matrix `Placement` remain fully affine,
without decomposition, including inherited shear within the model itself.
Normal component attributes, tint, tags, render options and visibility require
their normal update paths and positive tests; silently ignoring them is not parity.

The native/vmdl branch must still use its ordinary scene object, materials,
serialization and lifetime. No hidden native model may be introduced solely to
make an inherited API succeed. Skinned renderer/sequence/graph/morph APIs are
separate required integration work after this ordinary-renderer slice, with no
claim that a helper animation film completes them.

## Headless material evidence and remaining measurement

Source audit establishes:

- `Material.Load.cs:20-41` has no `Graphics.IsAvailable` check and loads native
  resource metadata; current `Model.Materials` lazily wraps native material
  handles in `Model.Material.cs:45-56`.
- The glTF stock adapter always acquires a flat normal/roughness texture, even
  when no texture is authored.
- `GltfNativeTextureCache.cs:51-53` uses `Texture.Load` and rejects a null result.
- `Textures/Texture.Load.cs:159-160` deliberately returns null when
  `Application.IsUnitTest && !Graphics.IsAvailable`.

Therefore eager adapter acquisition cannot be part of CPU-only Model loading.
Do not change that test guard or toggle application state to obtain a pass.
Dedicated/headless native material behavior beyond these source facts still
needs actual evidence. Until measured, lazy public `Material[]` behavior remains
a scoped validation question; it is not a proven native blocker.

## Acceptance and explicit remaining integration

The frozen 18 resource plus seven component/vmdl tests in
`../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/OrientationPublicModelTests.cs` (SHA256
`80dd0c8cd042e4262d98c2762876db7b035acd6d67c702cc2290d12298e4b9a4`)
ran before implementation. Architecture failures are distinguished from parser
failures and retained. Fox's additive CPU definition/lifetime tests also preceded
the extraction.

After the reviewed implementation and coordinator installation/restart:

1. Re-run those same original tests against the installed engine implementation.
2. Add `LoadAsync` identity, CPU-only preparation, independent instances/overrides,
   model replacement, owner destruction with retained instances and rollback
   regressions before claiming those surfaces complete.
3. Run Orientation's 40 core/source-layout cases and 48 source/compiled GPU/image
   cases with independent expected data and fresh empty/deletion controls.
4. Render the original public `Model.Load` + ordinary `ModelRenderer` path, with
   an unchanged native vmdl control in the same scene. A prototype helper load
   cannot stand in for this gate.

Original `.gltf` compiled-only discovery, `.glb` identity, persistent GUID/hash
discovery, same-basename collisions and publishing remain required. The source
audit in `PUBLIC_API_INTEGRATION.md` distinguishes missing mapping from existing
dependency packaging. Reuse `GltfArtifactDependencies`; do not wrap GLB bytes in
Source2 DATA, substitute filenames without a contract, or report direct `.glb_c`
loading as logical-source package identity proof.

Full skeleton/graph/physics, authored LOD/bodygroups/attachments/game data, editor
workflows, portable optimized layout, performance parity and Windows validation
remain part of the full goal. This bounded slice does not declare any of them
complete or excluded.
