# Portable compiled glTF pipeline direction

Status: audited proposal, 2026-11-06. This document records the requested direction,
existing engine mechanisms, acceptance cases, and unresolved integration work. It
does not approve implementation or establish runtime, performance, or platform
acceptance. Keep this stable file current; Git preserves previous revisions.

## Requested behavior

Authors edit the original `.gltf` or `.glb`. Per-model `.meta` import settings
contain policy overrides; no required `.vmdl`, generated `.vmat`, or replacement
authoring format is introduced. The default uses s&box shaders and shared runtime
material resources, with optional `.vmat` overrides.

Produce one portable valid GLB representation in `.glb_c`, usable across supported
platforms. Automatically choose safe model/shader-specific preparation to reduce
runtime CPU retention, GPU memory, and loading work. Defaults must preserve
supported authored behavior. Quantization, lossy texture compression, resolution
reduction, animation reduction, and other lossy changes require explicit opt-in.
Unknown optional extensions and extras must survive safely; transformations that
cannot preserve their interpretation are skipped or rejected with a precise reason.

This does not change engine source identity or output aliases. Current mappings
keep `name.gltf` -> `name.gltf.glb_c` and `name.glb` -> `name.glb_c` distinct
([GltfModelLoader.cs:37](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfModelLoader.cs#L37)).
Scene fragments select a view of that model; they are not another physical source
or URI base ([same file:123](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfModelLoader.cs#L123)).

## Established engine workflows

| Existing mechanism | Evidence and consequence |
| --- | --- |
| Source-adjacent metadata | `Asset.GetMetadataFile()` derives `<source>.meta`, preserving existing GUID/tags and excluding cloud/transient assets ([Asset.cs:168](../../engine/Sandbox.Tools/Assets/Asset.cs#L168)). |
| Inspector settings | The existing glTF inspector uses a settings object, `ControlSheet`, metadata persistence verification, and reimport ([GltfImportSettings.cs:24](../../game/addons/tools/Code/Inspectors/GltfImportSettings.cs#L24)). Extend this pattern rather than adding a second settings file. |
| Audio precedent | Audio provides per-asset values, multi-selection, debounced writes, save-all-before-compile, and flush-on-destruction ([SoundFileCompileSettings.cs:132](../../game/addons/tools/Code/Inspectors/SoundFileCompileSettings.cs#L132), [177](../../game/addons/tools/Code/Inspectors/SoundFileCompileSettings.cs#L177)). |
| Metadata invalidation | `ResourceCompileContext.ReadMeta()` registers an optional compile dependency even when `.meta` does not exist yet ([ResourceCompileContext.cs:102](../../engine/Sandbox.Engine/Resources/Compiling/ResourceCompileContext.cs#L102)). |
| Dependency categories | Runtime resource, compile-only input, and arbitrary packaged game-file references are distinct ([ResourceCompileContext.cs:54](../../engine/Sandbox.Engine/Resources/Compiling/ResourceCompileContext.cs#L54)); their implementation delegates to native compiler context registration ([ResourceCompileContextImp.cs:54](../../engine/Sandbox.Tools/Assets/ResourceCompile/ResourceCompileContextImp.cs#L54)). |
| Asset graph queries | Forward and reverse queries delegate to native `IAsset` ([NativeAsset.cs:160](../../engine/Sandbox.Tools/Assets/NativeAsset/NativeAsset.cs#L160)). `RegisterFile()` registers asset identity, not dependency edges ([AssetSystem.cs:418](../../engine/Sandbox.Tools/Assets/AssetSystem.cs#L418)). |
| Publication | Input dependencies are omitted when sources are excluded ([PackageManifest.cs:295](../../engine/Sandbox.Tools/Utility/ProjectPublisher/PackageManifest.cs#L295)); compiled primary files and additional game files are collected after compilation ([402](../../engine/Sandbox.Tools/Utility/ProjectPublisher/PackageManifest.cs#L402)). Reachability follows actual asset references ([PackageManifest.Reachability.cs:34](../../engine/Sandbox.Tools/Utility/ProjectPublisher/PackageManifest.Reachability.cs#L34)). |

The current glTF importer is not integrated with those dependency registration
mechanisms. It keeps an in-memory path dictionary, reads sources/dependencies, and
publishes bytes directly ([GltfAssetImport.cs:11](../../engine/Sandbox.Tools/Assets/GltfAssetImport.cs#L11),
[90](../../engine/Sandbox.Tools/Assets/GltfAssetImport.cs#L90)). `.gltf`/`.glb` are
currently simple asset types ([assettypes.txt:622](../../game/bin/assettypes.txt#L622)).
The compiler's normal path packages buffers and retains external image URIs; its
documentation explicitly distinguishes packaging from GPU optimization
([GltfCompiler.cs:12](../../engine/Sandbox.Gltf/GltfCompiler.cs#L12)). Explicit image
embedding is a mechanism, not the approved automatic policy.

## Texture ownership and metadata policy

Default **Automatic** means: embed an externally referenced image only when the
supported authored dependency graph is complete for the declared build scope and
proves this model is its sole authored owner. Shared, unresolved, unindexed,
unsupported, or otherwise uncertain resources remain external. Never delete or
rewrite authored source images as a consequence of embedding.

Expose **Keep external** as an override. Do not treat unconditional **Embed all**
as satisfying the exclusivity rule. Keep the chosen decision, completeness scope,
and reason visible as computed inspector information; do not save transient owner
counts into author policy. Setting names/schema remain an implementation decision.
Use a default automatic optimization policy plus explicit policy overrides and
opt-in lossy settings, rather than requiring users to select low-level layouts.

Ownership is based on canonical resolved source asset identity: GUID with the
resolved path/mount/package revision context needed to disambiguate it. Source,
compiled alias, and scene-view identities must resolve coherently. Byte identity
alone does not prove ownership. Two image slots in one model count as one model
owner; a second model or independent material consumer defeats exclusivity.

`AssetSystem.All` contains tracked assets only
([AssetSystem.cs:21](../../engine/Sandbox.Tools/Assets/AssetSystem.cs#L21)); the
content filesystem mounts core, cloud, and active projects
([FileSystem.cs:80](../../engine/Sandbox.Tools/FileSystem.cs#L80)). Existing
unreferenced-asset guidance explicitly excludes code, UGC, scripts, and sharing
with other mods ([AssetSystem.Tags.cs:16](../../engine/Sandbox.Tools/Assets/AssetSystem.Tags.cs#L16)).
Thus a reverse count of one is not global exclusivity evidence. Inventory supported
unimported authored sources and unresolved references before deciding; missing
coverage means external. Unknown projects cannot be declared scanned or exclusive.

Persist authored ownership/input edges independently of emitted runtime edges.
Embedding must retain those source edges: otherwise M1 embeds and loses its runtime
edge, making M2 falsely appear exclusive. A texture kept raw externally requires
both a compile input and a guaranteed runtime publication edge. An embedded image
requires its source input/ownership edges, but no separate runtime payload. Native
texture resources use resource references; managed raw files need game-file
inclusion. Generic `ScanJson()` alone does not solve this: raw images become
compile-only inputs, and its strings are not resolved as glTF-relative URIs
([ResourceCompileContextImp.cs:283](../../engine/Sandbox.Tools/Assets/ResourceCompile/ResourceCompileContextImp.cs#L283)).

## Deterministic invalidation and publication

First resolve/register the complete supported authored graph, then derive storage
decisions and compile outputs. Record compiler/policy version, relevant metadata,
canonical input hashes, shader/layout contract version, sorted authored consumers,
and completeness state in the build dependency decision. Exact persistence and
hash integration require the compiler contract audit below; no new public API or
cache schema is assumed here.

Changes to reference membership invalidate every affected consumer: adding,
removing, renaming, or retargeting models/materials; package revision/mount changes;
and resolution/completeness changes. Source-image bytes, image `.meta`, model
`.meta`, and shader contracts invalidate relevant preparation too. Derived output
events must not feed back into authored ownership. The current watcher only queues
the changed model and existing path-dependent entries; adding M2 does not queue
unchanged M1 merely because they now share an image
([GltfAssetImport.cs:40](../../engine/Sandbox.Tools/Assets/GltfAssetImport.cs#L40)).

Use a consistent graph snapshot for a build and re-evaluate if inputs change before
publication. Preserve last-good artifacts on failure and publish the artifact and
dependency decision together. Source-excluded packages must include every external
runtime payload, including resources reached through variants and overrides.

## Preparation costs and shader contracts

Storage ownership, encoded file size, CPU decode/mip work, GPU texture format,
vertex layout, and retained CPU data are separate decisions and measurements.
Embedding PNG/JPEG bytes removes a file lookup; it does not make them GPU-ready.
Current material preparation still compiles images and generates mips at load time
([GltfStockMaterialAdapter.cs:72](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfStockMaterialAdapter.cs#L72));
ordinary decoded images currently become RGBA8888 resources
([GltfTextureCompiler.cs:54](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfTextureCompiler.cs#L54)).
The runtime texture cache already shares identical compiled resources by SHA256
within a context while leases live
([GltfNativeTextureCache.cs:35](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfNativeTextureCache.cs#L35)).

Prepare descriptors from actual bufferViews/accessors. Consider the union of
default material, all variants, permitted `.vmat` overrides, skin/morph/animation,
topology, UV transforms/sets, tangent frames, colors, samplers, and color spaces
before pruning or specializing. Preserve a valid reconstructible raw route where
runtime overrides need it; unused-by-default does not mean unused-by-model.
Existing variants gather all material choices and retain raw plans
([GltfGpuAsset.Variants.cs:142](../../engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuAsset.Variants.cs#L142));
material overrides request raw outputs
([GltfGpuBackend.cs:551](../../engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/GltfGpuBackend.cs#L551)).

Texture preparation keys must include color-space, mip, encoding, and relevant
processing contracts, not only source bytes. Material reuse also includes template
revision, features, parameters, texture revisions, and sampler state
([GltfMaterialCache.cs:7](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfMaterialCache.cs#L7)).
Move safe preparation out of load time where useful, but choose the portable GLB
representation before claiming native texture blobs or shader-specific acceleration
data can be stored without compromising validity and cross-platform use.

## Validation selection and acceptance

Follow the [current inventory matrix](WORKSTREAMS.md): **one canonical derived
`.glb_c` per model; retain source forms only for unique feature/layout/codec proof;
shared fixtures own generic container/source-encoding checks**. Select the canonical
source explicitly and record its pin/hash and dependencies. This reduces duplicate
sample validation, not supported formats or engine aliases. Historical per-source
receipts remain historical; do not rename their coverage into canonical passes.

| Acceptance case | Required observation |
| --- | --- |
| Sole owner -> shared -> sole owner | One model embeds; adding another model/material consumer makes affected models external; removing it permits embedding again. Retained authored edges prevent feedback. |
| Incomplete graph | Unimported source, unresolved reference, unavailable package, unsupported reference-bearing extension, or uncertain mount preserves external resources with a reason. |
| Identity | Escaped/relative URIs normalize correctly; different mounts/package revisions and same-stem `.gltf`/`.glb` remain distinct; scene fragments do not add owners. |
| Invalidation | Reference membership, image/model metadata, image bytes, shader contracts, and packages invalidate all relevant outputs. Fresh-editor and headless builds agree for the same complete input snapshot. |
| Source-excluded publication | A fresh cold load using packaged files only reaches canonical model, variants, animations, overrides, and every external dependency. Verify GLB magic/structure and actual package paths. |
| Lossless defaults | Independent accessor/layout, skin/morph/animation, UV/sampler, texture and variant/override checks preserve authored semantics; unsafe opaque-data rewrites are skipped. |
| Performance | Measure named-model load time, peak/retained CPU memory, GPU memory, and visual parity against the same input/build/settings. Packaging or smaller file size alone is not a performance pass. |
| Failure and reproducibility | Mid-build input changes, missing dependencies, and publication failure preserve last-good state and cannot leave an artifact paired with a different dependency decision. Portable output is tested on supported platforms. |

## Unresolved integration and bounded decisions

The native asset/compiler-context integration gap remains unresolved. Public managed
code can register edges while it owns a `ResourceCompileContext`; this audit found
no general managed dependency setter for the current direct-file importer. Native
recompile/freshness operations delegate to native interfaces
([NativeAsset.cs:280](../../engine/Sandbox.Tools/Assets/NativeAsset/NativeAsset.cs#L280)).
Adding `ResourceIdentity` selects a managed compiler when a native context exists
([AssetSystem.Compile.cs:9](../../engine/Sandbox.Tools/Assets/AssetSystem.Compile.cs#L9));
it does not prove native registration, aliases, raw GLB output framing, persisted
graph updates, or headless compilation for a simple glTF asset.

Resolve with a bounded integration proof of raw GLB output plus native graph and
asset lifecycle participation, or an explicitly supported managed asset seam.
Do not invent a setter, hand-edit generated interop, assume `SetExtension` removes
native framing, or call a custom compiler sufficient without that proof. The older
[raw compiled-GLB audit](README.md#raw-compiled-glb-asset-pipeline-audit) describes
candidate seams, not demonstrated end-to-end support.

Remaining engineering decisions are the provable ownership/build scope and its
completeness signal; graph/decision persistence and atomic invalidation contract;
the valid portable representation of preparation data; version/hash integration;
and the safe specialization boundary under arbitrary runtime material overrides.
These require engine/API evidence and representative tests, not broad user
preferences. The user's authoring, lossless-default, external-sharing, portability,
and canonical validation directions are already established.
