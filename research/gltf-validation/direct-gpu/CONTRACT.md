# Direct GPU model contract

The sole backend/rig source lives in `engine/Sandbox.Engine/Resources/Model/Gltf/DirectGpu/`, with internal types in `Sandbox.Gltf.DirectGpu`. Production extraction is installed and the public `Model.Load` path has executed: current stage `c5c0fe3da02d46efaca6decd71760d42` has Engine `2c054bba-c0fa-43b5-a3a2-39a34ff7577f` and glTF `c869761c-5b80-462d-982a-bc3a688e57b6`. Selected ordinary/skinned components own real GPU-buffer instances and `SceneCustomObject`s without native Model/Mesh placeholders. Existing vmdl retains its native branch. Complete model acceptance remains pending.

Accepted c5 proofs include SimpleSkin's 57 public cases plus three controls and four source/compiled reference poses (`e9a3bc6a…`, `2c7a40a3…`); BoxTextured's six public source/derived-compiled forms (`46d4c955…`, `c97866b3…`); and AnimatedMorphCube's public controls, six authored-key stills and source-form two-loop cadence (`66ac4a30…`, `98e4f877…`, `165598de…`). The current pure suite passes 816 cases plus three separate codec preflights (`e8f0cbc4…`); CPU success is not GPU/corpus parity. Earlier fee5 public Model/resource/lifecycle and Orientation capture proofs retain their earlier producer labels. Exact receipts and limits are in [FEATURE_STATUS](../FEATURE_STATUS.md) and [execution evidence](EXECUTION.md).

Helpers compile only tests/oracles against the actual engine assembly, never replacement backend source. Test helper classes exposing internal types are internal with public reflection-callable entry methods. The API below describes the single current source tree; unexecuted fields or newer worktree changes are not automatically validated by the installed-stage receipts.

Namespace: `Sandbox.Gltf.DirectGpu`.

```csharp
// CPU-only source generation, metadata and texture serialization. No GPU or
// native material realization; the caller owns document unless a successful
// Prepare transfers its final release through the optional callback.
GltfGpuAsset definition = GltfGpuAsset.Prepare(
    GltfDocument document,
    string compiledComputeShaderPath,
    Func<int?, Material> materialResolver = null,
    Action finalRelease = null );

// Source-compatible shorthand for CPU preparation. A borrowed Material
// overload is also available for the coordinator's anonymous shader probes.
GltfGpuAsset asset = GltfGpuAsset.Create(
    GltfDocument document,
    string computeShaderPath,
    Func<int?, Material> materialResolver = null );
GltfGpuInstance instance = asset.CreateInstance(SceneWorld world);
instance.Placement = System.Numerics.Matrix4x4.Identity;
instance.SetPose(document.EvaluatePose(animationIndex, sourceTime));

// Metadata is available before realization. GetMaterials is lazy and returns
// an immutable ordered view; explicit resolver materials remain borrowed.
BBox exactRestBounds = asset.RestBounds;
IReadOnlyList<int?> authoredSlots = asset.MaterialSlotSourceIndices;
IReadOnlyList<Material> nativeMaterials = asset.GetMaterials();
SceneCustomObject sceneObject = instance.SceneObject;

// Compute runs once for each explicit SetPose/Placement revision in a graphics
// context, before raw drawing. No animation time advances in a draw callback.
// Readbacks are diagnostics; they wait for a completed GPU submission.
IReadOnlyList<GltfGpuPrimitiveSnapshot> rows = instance.Readback();
instance.Dispose();
asset.Dispose();
```

`Prepare` selects the requested or default scene, preserving independent node instances while sharing identical mesh/primitive input buffers and immutable CPU rigs. A missing default is an error for a default-scene rendering request; an unselected resource can remain valid. An empty selected scene remains a valid empty model definition; core primitives without `POSITION` remain preserved in the source and are skipped for drawing. `MeshCount` currently counts selected drawable primitive instances. `RestBounds` has no culling padding. Used authored material indices are ordered ascending, with an implicit default-material slot last when present; runtime material dedup does not merge authored slots.

Public `Model.Load` retains valid empty or unselected resources; `Model.Load(path, sceneIndex)` selects a scene under a distinct identity. Scene count, default and selected indices are exposed on `Model`; creating a renderer for an unselected resource reports the missing selection. Public scene/resource controls passed on 01459 and grouped reimport controls on e12; material and full API parity remain separate gates.

`CreateInstance` lazily realizes shared GPU/material resources, then owns a `SceneCustomObject` and per-node output/palette/weight/index buffers. It starts in the authored rest pose. Every output row identifies `Node`, `Mesh`, `Primitive`, `VertexCount`, `VertexStride`, `Fields`, `Bytes`, `SourceVertices` and `Indices`. Each field identifies `Semantic`, `SemanticIndex`, `Offset`, `Components`, and its actual output component representation. `SourceVertices[outputVertex]` maps expanded flat-shading corners to the original accessor vertex. Positions and normals are in engine world coordinates after full-affine placement. Placement does not round-trip through TRS. The scene object's transform remains identity so placement is applied exactly once. Pose/placement validation and finite bounds are preflighted before publishing a revision; partial native GPU-upload failure rollback is not yet guaranteed.

Rows also expose authored `SourceMode`, nullable actual submitted `Graphics.PrimitiveType`, per-row successful `DrawCalls` and `SubmittedRevision`. Those fields are CPU submission diagnostics recorded after `DrawIndexed` returns, not raster visibility or GPU pipeline-query proof. Current instancing source exposes nullable `InstanceIndex`; c5's 12 backend preparation cases passed, while the 125-instance GPU probe and public-loader capability remain pending.

Input streams are the owned `MaterializePrimitiveUpload` bytes, not a `GltfVertex` struct. Packing storage objects into one immutable raw GPU arena preserves their bytes, with explicit per-storage arena base offsets. Every source descriptor carries `arenaBase + stream.ByteOffset`, source `ByteStride`, `ComponentType`, `Components`, and `Normalized`; those values drive GPU loads. Sparse/implicit-zero/codec materialization remains the reader's documented responsibility. No source vertex layout is inferred from semantic order. All numbered joint/weight pairs are processed; no four-influence truncation. Output fields/offsets/stride are derived at runtime from the primitive's consumed semantics plus explicitly generated stock-shader inputs. Conversion to that declared output representation is a GPU operation, distinct from source layout.

The same `GltfPose` revision supplies full-affine node/joint matrices and signed, unclamped morph weights. Morph deltas apply in glTF local space before skin/node transform and basis conversion. Source meters map to engine inches and `(x,y,z)` maps to `(z,x,y)` exactly once. Skinning ignores the skinned mesh node transform. Normals use the inverse transpose of the weighted affine linear transform. Output buffers use C# `GpuBuffer` with byte-address/vertex usage, explicit compute-write-to-vertex-read barriers, runtime native vertex declarations and raw render-context binding/draw calls. The executed path must construct zero native `Model`, `Mesh`, `IModel`, model builder or native morph controllers. Stock shader/material, light, camera, render-context and scene-object services are allowed.

The default material path acquires existing stock glTF material-cache leases directly, without constructing a native model. CPU preparation retains the serialized texture generation so lazy realization does not reread mutable source files. An explicit resolver can supply stock diagnostic materials or native `.vmat` overrides; the receipt must state when diagnostics replace source materials. Existing stock-adapter feature limitations remain visible, never material-fidelity passes. Internal `ClearMaterialOverrides` and `SetMaterialOverride` commands edit a final slot vector; persistent whole-versus-slot component policy must follow the separately measured native behavior. Material typed attributes are merged before draw state binding; that alone does not prove sampler/material pixels.

Disposing an asset releases its owner lease and immediately refuses new instances. Existing instances retain the same CPU/document/GPU generation until their own disposal; final release runs once in the captured engine context. Instance disposal detaches/deletes its callback before freeing its buffers and releasing the generation lease; repeated disposal is harmless. Cleanup attempts every resource and retains the original acquisition error when rollback also fails. Disposal during an active graphics callback is deferred to the frame boundary. Validation must prove creation failure rollback, two instances with different poses, repeated load/unload, positive geometry and empty/deletion controls.

Model owner folders contain independent source oracles and model-specific checks, not copies of shared runtime code. The coordinator alone compiles/executes them and records source hashes, loaded assembly MVIDs, shader identity, source/compiled form, requested/observed clock, GPU readback and capture controls. GPU readback checks and final rendered-image comparisons are separate acceptance rows. A deterministic seek movie is not wall-clock playback/performance proof.

The original first execution gate passed: one raw GPU triangle with runtime position/normal/tangent/color/UV field offsets, visible native default presentation, independent vertex readback and empty/positive/deleted controls without native model/mesh construction. Subsequent selected public-model proofs are listed above; each remaining model/layout still needs its own focused tests and acceptance before broader animation/media claims.

Readback clarification after the first successful triangle receipt: `Indices` now comes from the actual bound per-instance index `GpuBuffer.GetData<uint>`, with `IndexDataOrigin="gpu-readback"`. `SourceVertices` remains CPU submission/provenance metadata; it must not be described as GPU map readback. The first backend source (`2a8dab92...`) returned CPU index submission metadata, so its receipt does not establish GPU index-buffer readback. Native pixel comparisons and winding negative controls remain independently required.
