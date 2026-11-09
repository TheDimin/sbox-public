# Executable provided-graph candidate gate

Source: `../../../../../engine/Tools/Gltf/direct-gpu/models/UnlitTest/UnlitTestGraphCandidateGate.cs`. Coordinator: `/root/gpu_validation`.
This is a separate freeze from `validation-request.json`; the original 49 cases
and six frozen C# files are unchanged. No generation, shader compilation or
runtime execution has been performed by the model worker.

## Two separate coordinator stages

Compile the frozen research helpers against the actual loaded Engine/Gltf/tools
assemblies using the established runner. Include the shared Backend/Rig/Morphs
source freeze and the existing six model-local C# sources; do not compile copies
of producer code or run the broad ShaderGraph integration test.

1. **Generation only — one static result row.** Invoke
   `Sandbox.Gltf.DirectGpu.UnlitTestValidation.UnlitTestGraphCandidateGate.GenerateForReview(checkoutRoot, freshOutputDirectory, expectedLoadedToolsMvid)`.
   Use `kind: "static_results"`, `method: "GenerateForReview"`,
   `expected_cases: 1`, with these three explicit string arguments. The tools
   MVID must identify the **active EditorTypeLibrary** assembly containing
   `Editor.ShaderGraph.ShaderGraph` and `GraphCompiler`, rather than the core
   Sandbox.Tools MVID or a historical hotload assembly. The helper reflects
   `GlobalToolsNamespace.EditorTypeLibrary.GetType(fullName, true).TargetType`
   and checks each resolved node against that active registry. The method deserializes
   the pinned research graph through that implementation, verifies the four
   resolved provided node types, and saves generated candidate/reference shader
   sources and `generation.json`, including producer and source identities.
   It performs no shader compilation. Record its actual source-generation result
   separately; it does not establish GPU support.
2. **Review the actual generated sources and fill a fresh configuration.** The
   candidate uses the named linear factor times raw vertex RGB. The independent
   reference uses only raw vertex RGB; expected factors/products are baked by
   literal CPU arithmetic into FLOAT32 reference vertex colors. Thus it does not
   share the candidate's uniform conversion or multiply operation. Verify the
   expected 3D path, opaque output, direct unlit return and no texture allocation.
   Copy reviewed generated-source and manifest hashes into a coordinator-owned
   configuration using `candidate-config.example.json` as the field schema.
3. **Runtime gate — one MSTest orchestration case, six model form measurements.**
   Run `ReviewedProvidedGraphHas3DUnlitColorAndLightingBehavior`, after
   `Configure(checkoutRoot, configurationPath)`, with `measurements_property:
   "Measurements"`. Under the coordinator's guard this anonymously compiles only
   the reviewed candidate, independent reference and frozen shared compute shader;
   it does not write watched content or change a global renderer setting.

Every output directory must be fresh beneath
`Temp/gltf-validation/direct-gpu/`. Existing evidence is never overwritten.
Graph node constructors can have effects, so even generation belongs to the
coordinator. This candidate contains no texture node; its type check rejects
missing/unexpected nodes.

## Mandatory prior red receipt

The runtime configuration must name and hash the actual current-producer
UnlitTest receipt containing all four failed
`default_stock_material_preparation_accepts_required_unlit` rows. The gate checks
their failure messages and current Engine/Gltf MVIDs. A stale result from another
producer or an intentionally expected-rejection pass cannot satisfy this step.
No shared adapter change is part of this gate.

## Runtime checks and evidence

- Literal 3D front triangle agrees with the independent reference; changing its
  normal leaves color unchanged; reversed winding is empty. Near blue geometry
  wins over far orange in both submission orders. Every control has deletion
  captures and stopped callback checks.
- Four actual source/compiled forms use authored white vertex color. Two
  explicitly derived source/compiled fixtures add normalized U8 COLOR_0 values
  `(64,128,192,255)` to both primitives. All six are labeled separately.
- Every actual GPU row is checked against literal positions, normals and
  topology; source colors are checked independently. Bound indices must report
  `gpu-readback`; source maps remain CPU provenance metadata. Byte hashes and
  runtime field declarations are retained.
- Each form captures two light setups and compares both objects with independent
  float-color literal references and eroded masks. A separate ordinary lit
  material control must respond to those lights. Exposure is locked on the owned
  camera, with native state recorded after camera initialization.
- An intentionally sRGB-encoded green factor must be rejected by the independent
  image comparison; restoring the linear factor must restore the positive image.
- Fresh empty/deleted images, real frame counters, exact target dimensions,
  callback errors, material/shader identities and source hashes are recorded.

Reviewed upper limits are mean RGB error/change 1 byte/channel and maximum 3,
with lit-control mean change at least 8. Position component error is at most
0.0002 inches and normal Euclidean error at most 0.00001. Configuration values
cannot widen those bounds. Failed or incomplete rows remain evidence.

This candidate resolver is an explicit test override. Even a passing gate does
not make the default source-material path pass. No MASK, BLEND, doubleSided,
textured/general unlit, public Model.Load, Windows, load/memory or performance
claim follows from it. Production integration requires a bounded shared-file
handoff, one shipped compiled content template and existing material-cache leases;
there must be no runtime graph/shader compilation in Model.Load.
