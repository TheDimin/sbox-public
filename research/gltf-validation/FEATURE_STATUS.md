# glTF implementation and test status

Source audit: 2026-11-05. Scope: complete glTF 2.0 core, the ten selected
extension names in [coverage.json](coverage.json), and the agreed s&box model
integration. This is a feature checklist, not a claim of conformance.

**Most data features have reader/evaluator tests. Most still need engine
integration and rendered acceptance tests.** The current loader is a small
static, untextured prototype. Its fixed `GltfVertex`/`StockVertex` staging does
not meet the requirement for accessor-driven GPU layouts.

Tests listed below are written tests, not necessarily passing tests. Recent
source additions have not been built or run. The full current pure project has
not passed inside s&box. Its versioned source/case inventory is
[gltf-test-manifest.json](gltf-test-manifest.json); engine, editor, packaging and
GPU tests are additional to that inventory.

Legend:

- **U**: focused reader, CPU or managed unit tests exist. Coverage is partial.
- **E**: an actual-editor probe or native integration test exists. Its presence
  does not mean it ran or passed; standalone-host results are insufficient for
  the user's current editor-only acceptance requirement.
- **P**: a narrow historical actual-editor proof is recorded in the
  [validation README](README.md). It does not prove the current unbuilt source
  or the new dynamic layout path.
- **Missing**: no glTF-specific acceptance test for the stated behavior was
  identified in this audit.

## glTF 2.0 core

| Feature | Current source implementation | Tests written | Still required |
|---|---|---|---|
| JSON `.gltf`, asset version/minVersion and metadata | Reader implemented | U: Container, ReaderRobustness, Compiler | Complete current-build validation; preserve metadata through editor operations |
| GLB header, JSON/BIN chunks, ordering and padding | Reader and lossless writer implemented | U: Container, ValidationEdge, Compiler; E/P: small actual-load cases | Full format-variant coverage in editor; unknown chunks are read/ignored but compilation currently blocks their unsafe loss |
| External buffers and data URIs, including percent encoding | Reader/resolver implemented | U: Accessor, ValidationEdge, ReaderRobustness; E/P: Triangle forms | Complete mounted/build dependency handling and failure cases |
| BufferView buffer/offset/length/target/stride | Validated reader implemented | U: Accessor, ValidationEdge, Primitive, UploadLayout | Dynamic engine/GPU binding using those values; no fixed struct assumptions |
| Accessor component types, vector/scalar counts and normalization | Reader implemented | U: Accessor, ValidationEdge, Primitive, UploadLayout | GPU-format mapping and exact integer/normalized interpretation across layouts |
| Accessor relative/absolute offsets, alignment, bounds and matrix column padding | Reader implemented; view-relative alignment correction added in unrun source | U: Accessor, ValidationEdge, EvaluationEdge, UploadLayout | Verify the correction and GPU reads, including valid non-four-aligned view origins |
| Sparse accessors and zero-initialized accessors without a base view | CPU substitution implemented; upload descriptor flags required work | U: Accessor, ValidationEdge, UploadLayout | Sparse GPU upload/materialization and lifetime tests, without changing effective values |
| Dynamic separate/interleaved attribute streams | Metadata description added; current renderer still uses fixed staging | U: UploadLayout, Accessor | Actual dynamic single/multiple-buffer uploads, shader bindings and pixel/value comparisons; **GPU acceptance tests missing** |
| Indexed and nonindexed geometry; unsigned 8/16/32-bit indices | Reader and CPU index handling implemented | U: Accessor, Primitive, Geometry; E: primitive-mode probe | Actual dynamic index upload; prove widening/expansion where native APIs require it |
| All seven primitive modes | CPU list expansion implemented | U: Geometry, DrawData; E: `editor_primitive_modes.csx` | Point, line/loop/strip, triangle/list/strip/fan rendering and winding for source/compiled assets; editor probe unrun |
| Meshes, multiple primitives and per-primitive material slots | Static assembly implemented | U: Primitive, StaticSceneAssembly; E/P: multi-primitive loaded model | Complete material, deformation and dynamic-stream paths |
| POSITION attributes, coordinate basis and metre-to-engine-unit conversion | CPU conversion and static prototype implemented | U: Geometry, DrawData, StaticSceneAssembly; E/P: static geometry | Retain correct conversion with dynamic fetch, animation, skins and bounds |
| NORMAL and TANGENT attributes; tangent handedness | CPU preservation/conversion; stock-frame fallback for simple untextured geometry | U: DrawData, StaticSceneAssembly, GltfStockVertexFrameTests; E/P: limited stock-frame renders | Dynamic formats, normal-map tangent generation, deformation and correct transformed tangent frames |
| Missing normals/tangents | Static triangle flat-normal generation and simple stock-frame fallback | U: GltfStockVertexFrameTests, GltfModelPrimitiveBuilderTests; E: primitive-mode probe | Correct missing-attribute behavior for all modes; UV-dependent normal-map tangent tests **missing** |
| All TEXCOORD sets and texture-specific set selection | Reader/CPU retain sets; current stock staging exposes two sets | U: Primitive, DrawData, Material/MaterialEdge | Dynamic bindings for every used set and per-texture UV selection; rendered acceptance missing |
| COLOR_0 RGB/RGBA and normalized integer colors | Reader/CPU implemented; loader rejects nonwhite colors | U: Primitive, DrawData | Stock-shader color multiplication, linear interpretation and alpha; loader/render tests missing |
| Application attributes and names/extras | Primitive metadata retains application attributes; opaque data preserved conservatively | U: Primitive, Compiler, CompilerSafety, UploadLayout | Preserve references through compile/edit/reimport and expose dynamic custom bindings when requested |
| Scenes, selected default scene, multiple scenes and no default | Reader implemented; static loader requires selected default scene | U: Scene, StaticSceneAssembly; E/P: selected static scene | Complete import behavior and valid no-default handling outside default-scene import |
| Node hierarchy, matrix/TRS transforms, repeated mesh instances | Reader/world transforms and static flattening implemented | U: Scene, HierarchyCache, StaticSceneAssembly; E/P: static repeated nodes | Linked/editable hierarchy integration, deformation, runtime instance behavior and lifecycle |
| Negative/nonuniform transforms, normals, winding and bounds | CPU handling implemented for invertible transforms | U: Scene, StaticSceneAssembly; E: scene probe | Full rendered transform matrix across dynamic layouts and deformation |
| Zero/singular transforms | **Current static assembler still rejects valid cases** | U: positive zero/rank-two StaticSceneAssembly cases; E: updated scene runner | Record editor failure, implement positions/bounds and a reviewed degenerate-direction policy, verify rendering |
| Skin joints, skeleton roots, inverse binds and missing-IBM identity defaults | Reader and CPU evaluator implemented | U: SceneFeature, Deformation, SkinningPalette; E: skinning probe | Actual skinned Model.Load and native bone/remap readback; editor proof missing |
| Joint/weight formats and multiple influence sets | CPU reads all sets; stock GPU influence limits unproven | U: Primitive, Deformation, AnimatedDeformation | Exact integer joints/float or normalized weights on GPU, including more than four influences; no truncation |
| Full-affine skinning, nonjoint ancestors and instance placement | CPU palette helper plus unbuilt renderer hook | U: SkinningPalette, DirectSkinningPaletteTests; E: palette/rig runners | Connect per-skin/per-mesh native mapping and per-instance provider; GPU deformation, scale/shear, visibility and bounds proof |
| Morph POSITION, NORMAL and TANGENT deltas | Reader retains all three; CPU vertex evaluator covers position | U: SceneFeature, MorphConsistency, PoseEdge, Deformation | GPU normal/tangent morphing, arbitrary target counts/weights and actual loaded model integration |
| Node/mesh morph weights, defaults and morph-before-skin order | CPU evaluator implemented | U: MorphConsistency, AnimatedDeformation, Deformation; E/P: limited position-morph diagnostic only | Full combined morph+skin rendering, negative/large weights and instance isolation; diagnostic is not Model.Load parity |
| Animation TRS and morph-weight channels, arbitrary key times | CPU pose/channel evaluator implemented | U: AnimationChannel, AnimatedDeformation | Model/component playback, clip selection/seeking/looping, rest-state and instance lifecycle integration |
| STEP, LINEAR and CUBICSPLINE; quaternion interpolation | CPU samplers implemented | U: Animation, AnimationChannel, EvaluationRobustness; E: animation/playback probes | Actual playback at keys, boundary neighbors and intermediate times; measured tolerances before any approximation |
| Core default material and base-color factor | Limited stock complex material mapping implemented | U: Material, MaterialEdge, material cache tests; E/P: opaque RGB-factor static models | Complete material inputs/defaults across the dynamic path |
| Metallic/roughness factors and packed texture channels | Parsed; loader permits only default factors and rejects textures | U: Material, MaterialEdge; E: material-channel/lighting probes | Arbitrary factors, B/G channel mapping and rendered material behavior; existing experiments are not full parity |
| Base-color, metallic-roughness, normal, occlusion and emissive textures | Texture-role metadata parsed; loader rejects all textures | U: Material, Image; E: texture and channel probes | Integrate all five roles, normal scale, occlusion strength, emissive and texture/factor multiplication |
| OPAQUE, MASK, BLEND and alphaCutoff | Parsed; loader supports only OPAQUE | U: Material, MaterialEdge; E: partial material probes | Alpha masking, blending, sorting/depth and cutoff in actual loaded models; complete acceptance tests missing |
| Double-sided materials and back-face shading | Parsed and mapped to stock backface feature | U: Material; E: partial frame/material probes | Independent front/back normal and lighting tests with textures/deformation |
| Texture sources: external URI, data URI and image bufferView | Encoded bytes/reference metadata implemented | U: Image, ArtifactDependencies; E/P: BoxTextured helper only | Actual textured Model.Load across external/embedded/binary/compiled-only cases; prepared loader probe unrun |
| PNG/JPEG decoding, alpha and valid image precision | Native texture compiler handles a limited PNG/JPEG path; rejects 16-bit PNG | U: image byte handling; E: GltfCompiledTextureTests | Complete valid core image support, especially positive 16-bit PNG precision coverage; no accepted end-to-end 16-bit tests |
| sRGB versus linear interpretation by texture role | Metadata/compiler flag support; complete stock material mapping unproven | U: Material; E: native texture tests and partial channel probes | Independent rendered color-space/factor tests for all roles and shared image used in different roles |
| Sampler wrap S/T and all min/mag/mip choices | Reader and new data mapper implemented; loader/shader not connected | U: Material, GltfSamplerMappingTests; E: GltfSamplerPixelTests | Stock-shader sampler binding, all 12 glTF filter pairs, independent axes, NPOT/mips and no-mip views in editor |
| Perspective and orthographic cameras, aspect/FOV and clipping | Reader implemented | U: SceneFeature | Engine camera creation, coordinate/units/projection/activation tests; glTF camera component acceptance missing |
| Required/optional extension handling and standard fallbacks | Reader capability gate and targeted declaration/fallback checks implemented | U: Container, MaterialEdge, BasisFallback, QuantizationDeclaration, TextureTransformDeclaration | End-to-end loader/build behavior for every selected extension and fallback; do not report fallback as extension support |

## Selected extensions

These are the selected names, not a promise to implement every extension in the
registry. Other extensions require a separate scope/maturity review.

| Extension | Current source | Tests written | Missing engine acceptance |
|---|---|---|---|
| `KHR_texture_transform` | Reader UV transform and texCoord override | U: Material, MaterialEdge, TextureTransformDeclaration | Stock-shader/dynamic UV mapping for each material role, instances and variants; rendered tests missing |
| `KHR_materials_unlit` | Reader recognition and optional fallback | U: Material, MaterialEdge | Actual loaded unlit materials, colors/textures/alpha/backfaces and lighting-independent renders |
| `KHR_materials_variants` | Reader mappings/defaults/validation | U: Material, MaterialEdge | One active variant per instance across primitives, independent instances, overrides, reimport and packaging tests |
| `KHR_mesh_quantization` | Decoder; loader policy enabled in unbuilt source | U: Primitive, MorphConsistency, QuantizationDeclaration, GltfModelDocumentTests; E: quantization probe | Dynamic normalized formats and actual source/compiled loading/rendering; probe unrun |
| `KHR_draco_mesh_compression` | Standalone codec and accessor bridge | U: Draco, DracoAccessor, UploadLayout; historical data-only corpus harness | Loader capability/integration, dynamic decoded streams, actual rendered/skinned/morphed assets and platform proof |
| `EXT_meshopt_compression` | Standalone decoder/filter/fallback support | U: Meshopt, MeshoptIntegration, CompilerSafety, UploadLayout | Actual loader/render path and all selected filters/modes/platforms |
| `KHR_meshopt_compression` | Standalone decoder including selected v1 support; audited specification pinned as release candidate | U: Meshopt, MeshoptIntegration, CompilerSafety | Version/compatibility audit at upgrade, loader/render integration and complete selected-mode proof |
| `KHR_texture_basisu` | Image selection and bounded KTX2 Basis-to-RGBA8 decoding | U: Ktx, Image, BasisFallback | Native texture/compiler/GPU integration, portable output, mips/transfer/orientation and performance. Current corpus subset supplies no in-scope Basis case, so isolated acceptance assets are also required |
| `KHR_lights_punctual` | Reader for directional/point/spot metadata | U: SceneFeature | Native component creation and units/intensity/range/cone/transform rendering tests |
| `EXT_mesh_gpu_instancing` | Reader for TRS/custom instance metadata; static assembler rejects it | U: Instancing | Dynamic instance streams, draws, bounds/shadows/selection and independent runtime instances; rendered acceptance missing |

The actual loader currently advertises only `KHR_mesh_quantization`, and that
policy change is unbuilt. Reader support for the other extensions does not make
the engine loader support them.

## Additional agreed s&box work

These are product requirements beyond glTF core. Existing vmdl tests do not
count as glTF acceptance without exercising glTF-backed assets.

| Requirement | Tests already written | Still required |
|---|---|---|
| Existing Model.Load/LoadAsync APIs and stable resource identity | E/P: static 13-case/42-call loader matrix and material lifetime checks | Complete materials/deformation/dynamic layouts, GUID-only cold loads, source `.gltf` aliases in compiled-only builds |
| Valid portable optimized `.glb_c` | U: Compiler, CompilerSafety, CompilerOrphanBinary, ArtifactDependencies; E/P: packaging-only GLBs | GPU-ready representation, first-class asset registration, automatic compile/invalidation, output collision/ownership and portable dependency packaging |
| Generated dependency publication | New PublishGeneratedDependenciesTests | Execute generic publisher regression matrix in editor; actual glTF compiled build/package test still missing |
| Runtime material/texture sharing and overrides | U: material cache identity/lifetime; E/P: limited material lease sharing | Texture cache, reimport revisions, per-slot/per-instance/per-variant overrides and shader/material-graph properties |
| Animation graph references | No glTF-specific acceptance test identified | Packaged graph references and ordinary component/graph behavior; graph editing remains deferred |
| Collision shapes/meshes, mass/surfaces, joints/ragdolls/hitboxes | No glTF-specific acceptance test identified | Standard-extension suitability review, authoring/storage/runtime mapping and equivalent-vmdl physics tests |
| LODs, bodygroups, attachments and project-defined game data | No glTF-specific acceptance test identified | Reuse established APIs; settle standard/essential s&box encoding and implement authoring/runtime/roundtrip tests |
| New model-only editor, mesh selection and modifier/component-style authoring | No complete glTF editor acceptance suite identified | Model preview workspace and all applicable parity controls; undo/redo/save/reopen and dependency/source-control tests |
| Direct source `.gltf`/`.glb` editing and safe reimport | No complete glTF reimport acceptance suite identified | Preserve extension/extras references, source-material edits and links; automatic reload/conflict UI; `.meta` import settings only |
| Standalone authored export and DCC plugins | Deferred by user | Keep outside first milestone |
| Every applicable Khronos model/format variant | Pinned inventory and data-reader/roundtrip harness exist | Actual in-editor rendering/numeric evidence, independent validation, exclusions and upstream defect ledger; no full corpus pass |
| Load time, process/GPU memory and package size versus vmdl | No matched acceptance benchmark suite identified | Agree measured thresholds, matched static/animated assets, cold/warm protocol, raw-loading measurements separately |
| Linux then Windows | Narrow Linux editor proofs only | Full feature matrix on both; Windows proof and cross-platform compiled portability remain missing |

## Source/test pointers

- Pure project and complete current test filenames/counts:
  [manifest](gltf-test-manifest.json),
  [test sources](../../engine/Tests/Sandbox.Test.Gltf),
  [editor runner](editor_gltf_unit_tests.csx).
- Current actual loader and its fixed staging:
  [GltfModelLoader](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfModelLoader.cs),
  [GltfModelPrimitiveBuilder](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfModelPrimitiveBuilder.cs),
  [GltfGeometryBuffer](../../engine/Sandbox.Engine/Resources/Model/Gltf/GltfGeometryBuffer.cs).
- Dynamic metadata work:
  [upload description](../../engine/Sandbox.Gltf/GltfDocument.UploadLayout.cs),
  [layout tests](../../engine/Tests/Sandbox.Test.Gltf/GltfUploadLayoutTests.cs).
- Native/integration test sources:
  [engine integration tests](../../engine/Tests/Sandbox.Test.Integration/Engine),
  [resource tests](../../engine/Tests/Sandbox.Test.Integration/Resources),
  [managed resource tests](../../engine/Tests/Sandbox.Test.Unit/Resources).
- Actual-editor scripts, historical receipts and their limitations:
  [validation README](README.md).

The immediate critical path is dynamic accessor-driven uploads, full material
and texture loading, then real skinned/morphed/animated Model.Load behavior.
Their in-engine tests must pass before broader corpus parity can be claimed.
