# Provided ShaderGraph unlit candidate

2026-11-05 offline audit continuation. This proposal uses the repository's
existing surface/unlit graph generator. It does not change the shared adapter,
renderer, graph compiler, material cache, live content or frozen model tests.

## Audited behavior

- `game/addons/tools/Code/ShaderGraph/Compiler/ShaderTemplate.cs:20` declares
  Forward and Depth modes. It includes the existing common 3D vertex/pixel inputs,
  defines `S_UV2=1`, and adds a float4 `COLOR0` input and interpolator.
- `GraphCompiler.cs:1154` calls `ProcessVertex`, copies `v.vColor` into `i.vColor`
  without a color-space conversion, and returns `FinalizeVertex` at line 1197.
  `sbox_vertex.fxc` routes these to the ordinary 3D world/projection processing.
- `GraphCompiler.cs:871` handles `ShadingModel.Unlit` by returning the graph's
  Albedo and Opacity directly. It skips the lit material initialization and
  `ShadingModelStandard::Shade`. Lighting, material normal, metallic, roughness,
  occlusion and emission do not enter this color expression.
- `GraphCompiler.cs:475` can emit a named vector parameter such as
  `g_vGltfBaseColor`. `ResultValue` at line 548 preserves the supplied numeric
  vector values, with no implicit sRGB encode/decode. `Nodes/Parameter.cs:110`
  supplies the existing `Float3` node for this purpose.
- `game/editor/ShaderGraph/Code/Nodes/Geometry.cs:139` exposes raw vertex RGB and
  alpha through the existing `VertexColor` node. An untextured opaque material
  can therefore use `linearBaseColorFactor.rgb * vertexColor.rgb`, with opacity
  fixed to 1. This needs **no generated factor texture**.
- The existing `TextureCoord` node in `Nodes/Texture.cs:472` selects UV0
  (`i.vTextureCoords.xy`) or UV1 (`.zw`) and can tile them; it does not directly
  implement glTF's arbitrary rotation/translation or numbered UV sets beyond 1.
  `TextureSampler.Coords` accepts an explicit graph coordinate expression and
  each registered texture has a sampler state. This is a useful future per-role
  binding route, not proof of current stock sampler behavior.

The surface/unlit generator still includes common shader code. Its
`GenerateCommon` only sets `S_ALPHA_TEST` and `S_TRANSLUCENT`, not `S_UNLIT`.
Consequently the compiled material may advertise common light-probe metadata
even though its direct color expression does not sample lighting. Record actual
compiled flags and allocation evidence rather than inferring them from the graph
UI label. Do not change the graph compiler globally as part of this model fix.

## Concrete candidate artifact

`unlit_surface_candidate.shdrgrph`, SHA-256
`579e22ddfedc2588ba051afd01799aaced463135fb0ef230b7413327f1d788c7`,
contains only four provided node types:

1. Surface graph, Unlit shading, Opaque blend mode.
2. `Float3`, named `GltfBaseColor`, holding the exact authored orange linear RGB.
3. `VertexColor.RGB` multiplied by that parameter using the provided `Multiply`.
4. Result Albedo connected to the product; default Opacity is 1.

This graph is in the research folder outside live content. It has not been
deserialized, generated, compiled, loaded or drawn. Its default orange value is
only the initial value of a reusable parameter; blue must use the other exact
linear factor on a distinct cached material descriptor. No graph or shader per
model instance is proposed.

Important execution side effect: do not instantiate `TextureSampler` just to
inspect a candidate. Its base constructor sets an image, and its result path
calls `CompileTexture` / `AssetSystem.CompileResource` when the image is found.
This candidate deliberately contains no texture node. The existing integration
`engine/Tests/Sandbox.Test.Integration/ShaderGraph/ShaderGraphTests.cs` shows how
to locate the actually loaded tools graph/compiler types and call their real
`GraphCompiler.Generate` by reflection; do not compile replacement producer code
into a research helper or start the integration test's broad package build.

## Coordinator runtime candidate gate

After the model's actual current-adapter red rows are retained, the coordinator
can perform a bounded candidate proof:

1. Identify actual loaded tools/editor assemblies, MVIDs, and graph node/compiler
   types. Deserialize the research graph using the existing graph API, call the
   real non-preview generator, and save the generated source and full hash in
   the execution receipt. Record errors before any shader stage.
2. Review the generated source: ordinary world/projection processing; Forward
   and Depth modes; linear vector uniform; raw vertex RGB multiplication;
   opaque output; no call to lighting and no factor texture. Identify the
   actually consumed runtime input fields and any stripped UV1/normal inputs.
3. Under the coordinator's resource guard, compile the exact reviewed generated
   source through the existing supported shader compiler/anonymous-shader path.
   Record the actual compiled/program/material identity. Use native Model/Mesh
   allocation controls already required by the direct GPU contract.
4. Draw independent literal triangles and actual direct GPU output using the
   candidate material. Exercise exact orange and blue factors, white and
   deliberately nonwhite vertex colors, two different normal directions, and
   distinct lighting setups. Verify world projection, depth and one-sided
   winding with the established empty/positive/deleted-empty controls.
5. Apply `UnlitTestImageChecks` with independent constant-color references,
   independent eroded object masks, fixed exposure and a responsive explicit
   lit control. Successful ShaderGraph generation or material flags alone do
   not satisfy source-material rendering acceptance.

Only a terminal passing candidate gate and an explicit file handoff authorize a
bounded shared implementation. Suggested interface then: add unlit-specific
prepared linear RGB/shading identity and an isolated acquisition branch in
`GltfStockMaterialAdapter.cs`; select this proven generated template, use the
existing `GltfMaterialCache` with the actual template identity and exact linear
parameter, and retain existing cache/lease/disposal rules. Keep the ordinary
opaque/MR path unchanged. A runtime-only proof shader is not automatically a
shippable compiled content asset; its source/packaging/runtime availability also
need review before production integration.

## Explicit limits

This candidate covers untextured OPAQUE RGB and COLOR_0, matching UnlitTest.
It does not establish full KHR_materials_unlit support. The generic graph's
masked path enables alpha-to-coverage state, but the direct unlit return does
not call `AdjustAlphaToCoverage` or emit an explicit cutoff comparison. Do not
claim glTF MASK/alphaCutoff from the graph's blend-mode setting. BLEND ordering,
alpha, doubleSided, arbitrary per-role UV transforms/samplers, texture color
space and material-editor/public-loader integration remain explicit gates.
Unsupported behavior must stay visible rather than being approximated silently.

Existing frozen UnlitTest request and all six C# hashes remain unchanged:
49 CPU/staging/verifier rows and 20 separately planned actual GPU measurements.

## Audited source identities

| Source | SHA-256 |
|---|---|
| `GraphCompiler.cs` | `7b61d3bad8ba703167ee1c624fb052de8f186325771fb49b3bd1b91df4a8ad1c` |
| `ShaderTemplate.cs` | `9290ce4064ae144771991efa4f65d0132f6db66ff9dac14ccf0f64db87e7d1c1` |
| `Nodes/Parameter.cs` | `b36575306bd5526ba2568c2101f0ea9c4d3f3a23a410c06c695ef8b5e42431b7` |
| `Nodes/Geometry.cs` | `f41644a59e518c7cc3f1bd4c0c090272e12ef783b1103beca6e5e778c0e53999` |
| `Nodes/Binary.cs` | `ff35d237ecac4519259c7ae9b7698e95c2dc22811067e34fb65b88e2a41cbfe0` |
| `sbox_pixel.fxc` | `5d35a1fde4ceb108b613570fc9c5d286adf26d96b8849cffb733703261898a15` |
| `common/shadingmodel.hlsl` | `df12e39abe4a671cf4b256485ed6f6c96404138d4d3255c0183852f391106a43` |
