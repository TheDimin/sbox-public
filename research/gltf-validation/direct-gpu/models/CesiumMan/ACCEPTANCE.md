# CesiumMan direct GPU acceptance

Owner: `gpu_cesium_man`. Status: source/CPU tests first ran 21/23; both remaining
cases exposed a variant-oracle error corrected below, with rerun pending. GPU
execution and rendering evidence are pending. Historical native Model
captures do not satisfy these tests. No engine renderer files were changed.

The Khronos sample is pinned to
`90d7ede14c7e280af263824604b427a1ca02cb66`. Its original glTF,
embedded glTF and GLB contain one triangle primitive with 3,273 vertices and
14,016 U16 indices. A 22-node hierarchy contains a 19-joint skin and one unnamed
animation with 57 LINEAR TRS channels. Original glTF and Binary use 48 keys ending
at 2 seconds; Embedded uses 46 keys ending at 1.9166669845581055 seconds. Their
identical geometry and animation prefixes do not make their end poses identical.
The first key is 0.04166661947965622 seconds. Time zero must clamp to
the first key when that animation is selected; authored rest is a separate state.

Two shared views deliberately exercise nonzero accessor offsets:

| Semantic | Components | Component type | Byte offset in view | Stride |
| --- | --- | --- | ---: | ---: |
| JOINTS_0 | 4 | U16 | 0 | 8 |
| TEXCOORD_0 | 2 | FLOAT | 26184 | 8 |
| NORMAL | 3 | FLOAT | 0 | 12 |
| POSITION | 3 | FLOAT | 39276 | 12 |
| WEIGHTS_0 | 4 | FLOAT | 0 | 16 |

JOINTS_0 shares owned storage with TEXCOORD_0; NORMAL shares storage with
POSITION. This is a source-format regression case, not a proposed engine vertex
layout. Float weights are preserved without renormalizing; the source maximum
sum error is approximately 8.94e-8, with 1–4 nonzero influences per vertex.

`../../../../../engine/Tools/Gltf/direct-gpu/models/CesiumMan/CesiumManReferenceTests.cs` is written before GPU implementation. Compile only
the test source against the editor's actually loaded `Sandbox.Gltf` and MSTest;
do not compile producer code into the helper. Configure the class with
`Configure(checkoutRoot, oraclePath)`, then run its 8 public test methods / 23
expanded cases (primitive string DataRows; no lifecycle hooks or DynamicData).

The independent reference is authored with:

```bash
python3 engine/Tools/Gltf/direct-gpu/models/CesiumMan/author_reference.py \
  --corpus Temp/gltf-reference/glTF-Sample-Assets \
  --output Temp/gltf-validation/direct-gpu/CesiumMan/reference.json
```

The coordinator runs this bounded authoring command and all engine work under
the resource guard. It checks the corpus revision, reads actual bufferView and
accessor ranges using Python stdlib, verifies equivalent core geometry, and
records source/dependency hashes. Version 2 evaluates ten rest/key/inter-key/
clamped states **per variant** using independent double-precision TRS, quaternion slerp, hierarchy and
all-influence skin math. It emits all source vertex positions and inverse-
transpose normals for every state. Generating that JSON is test-input authoring,
not an engine or rendering pass. No engine pose/deformation implementation is
used to construct expected values. The first execution was 21/23: two Embedded
last-key cases exposed the first oracle's incorrect canonical-pose reuse. That
red receipt is retained; the fix authors each variant's actual pose/key schedule,
without changing producer code or numerical tolerances.

The 23 source/CPU cases cover input identity and skin inventory (4), exact shared
source bindings (3), all raw attribute/index bytes plus a wrong-offset negative
control (3), all joint worlds and selected vertices against the independent
reference (3), valid lossless GLB packaging and retained material inputs (3),
independent immutable poses/rest (3), nonzero-first-key clamping (3), and clear
rejection of unsupported required Draco (1). The bounded vertex subset in CPU
tests avoids repeatedly decoding entire source streams per queried vertex;
completed GPU readbacks must cover **every** output vertex.

`CesiumManGpuChecks.AssertSnapshot(variant, snapshot, sample, placement)` consumes
the shared backend's actual completed GPU readback. It discovers POSITION and
NORMAL from runtime field descriptors, maps output corners through SourceVertices,
and checks every position/normal against the independent source. Explicit source
basis conversion and full-affine placement apply exactly once. Numerical gates
are provisional 0.001 inches for positions and 0.0002 normal-vector error; the
receipt must record observed maxima before reviewing/agreeing final tolerances.
No camera/raster difference is waived by these numeric gates.
`AssertTopology(snapshot, oracleRoot.GetProperty("variants").GetProperty(variant))` independently checks every output triangle
through its source-vertex mapping, and reports whether the whole primitive's
winding is preserved or reversed. Mixed winding or changed connectivity fails;
the independent raster controls decide whether a consistent reversal is correct.
Both GPU checks require `IndexDataOrigin="gpu-readback"`. `SourceVertices` is
explicitly CPU submission provenance and is not reported as a GPU map readback.

The coordinator's shared native capture wrapper must run this matrix, retaining
failures and every source/helper/shader hash plus loaded MVIDs:

1. Original and valid compiled GLB forms of each core variant; authored rest and
   all ten reference states; current GPU PoseRevision equals SubmittedRevision.
2. Identity placement and a nonuniform full-affine placement, including inherited
   shear/reflection fixture placements when the shared backend supports them.
3. Two instances sharing immutable input with independent different poses and
   placements. Check each against its own reference; check meaningful geometry
   change; update one and prove the other's captured/readback bytes remain stable.
4. SceneCustomObject raw buffer draw with zero new native Model/Mesh/IModel or
   native morph-controller allocations. Verify actual compute-to-vertex barriers,
   runtime declaration and raw vertex/index binding in the executed path.
5. Fresh empty-before, positive independent triangle, positive CesiumMan, deletion
   and fresh empty-after controls through the accepted native Normal+normal
   postprocessing+owned-light route with exact camera target size. No inserted
   camera clear, global renderer switch, pixel rewrite or stale image acceptance.
6. Compare actual model raster against an independent reference presentation;
   GPU vertex readback alone does not prove the stock shader/material/texture
   appearance. Default material must retain the Cesium texture, metallic=0 and
   roughness=1. Diagnostic-white renders must be labeled diagnostics.
7. Exact seek/reset/bind and smooth high-cadence source-time media, then separate
   actual wall-clock playback evidence. No dropped elapsed-time clamp or frame
   synthesis; deterministic seeks do not prove performance.
8. Repeated creation/disposal, no callbacks after deletion, idempotent instance
   disposal, asset-dispose rejection while instances live, and rollback after
   creation failure. Verify allocations/material leases released, not just GC.

The required Draco variant is in scope. It declares 3,572 vertices and has a
different ordering after compression, so the core source-index oracle cannot
prove it. Its declared WEIGHTS min/max include negative values, which are
metadata rather than evidence of actual decoded values. Inspect actual decoded
influences before classifying an upstream defect; do not silently clamp,
renormalize, exclude or pass.

`../../../../../engine/Tools/Gltf/direct-gpu/models/CesiumMan/CesiumManDracoReferenceTests.cs` adds five methods / seven expanded cases for
source and compiled decoded components/topology, pinned decoder provenance and
positive core skin validity. Tests precede the authoring implementation. The
separate integration reference is authored by the coordinator:

```bash
node engine/Tools/Gltf/direct-gpu/models/CesiumMan/author_draco_reference.cjs \
  --corpus Temp/gltf-reference/glTF-Sample-Assets \
  --decoder-dir Temp/gltf-validation/direct-gpu/CesiumMan/draco-1.5.7 \
  --output Temp/gltf-validation/direct-gpu/CesiumMan/draco-reference.json
```

The helper verifies exact JS, WASM and retained license hashes before executing
the official decoder. It uses the Draco variant's own compressed bufferView,
mapped unique attribute IDs, actual component representations and point mapping.
Every value, raw component byte, triangle and weight statistic is retained.
No renderer validation occurs outside s&box and this is not a production
dependency. This oracle is a separate JS/WASM integration with **shared codec
ancestry** to the production native Draco integration; it is not a fully
independent compression algorithm.
The optional `--source-relative Models/RiggedSimple/glTF-Draco/RiggedSimple.gltf`
selects another owned model's local source with the same corpus/buffer bounds and
provenance checks. The default remains CesiumMan. Other model owners invoke this
one pinned decoder helper and adapt its payload in their own folder; they do not
copy a second codec integration or reuse CesiumMan vertex ordering/pose data.

After the decoded source satisfies the positive core weight/joint tests,
`../../../../../engine/Tools/Gltf/direct-gpu/models/CesiumMan/author_draco_pose.py` creates `draco-pose-reference.json` from the Draco variant's
own decoded component bytes and its own animation/hierarchy/inverse binds. It
reuses only the model-local independent reference math, not producer code or core
vertex correspondence. If decoded weights are actually negative it refuses to
create a clamped/modified oracle and preserves the earlier decode evidence for
an upstream-defect decision. `../../../../../engine/Tools/Gltf/direct-gpu/models/CesiumMan/CesiumManDracoPoseTests.cs` adds two original/
compiled pose cases with the same numerical gates as core forms. The shared GPU
snapshot checks also accept `glTF-Draco` only with a sample explicitly tagged for
that variant and its own reference geometry size. These are authored tests,
pending execution; a produced JSON never constitutes a renderer pass.

Decoder source is official Draco 1.5.7 commit
`8786740086a9f4d83f44aa83badfbea4dce7a1b5`, Apache-2.0:

| File | SHA-256 |
| --- | --- |
| draco_wasm_wrapper_gltf.js | 8bb2952d2ba7d67e1414f8df819410cb0434a666be53f671fff75f68843d76f6 |
| draco_decoder_gltf.wasm | 712db3449ae2041d6e8a224c395bda6cedb49e51322fae38b7db9beb8b381889 |
| LICENSE | d3709b0fb4b8a94bbb1d02b8a2e484f258b0d9c5c5a01f940391f3fe662cd1a4 |

The coordinator retains the download source URLs/commit/license in its
`dependency.json`. The official [glTF decoder package documentation](https://github.com/google/draco/blob/8786740086a9f4d83f44aa83badfbea4dce7a1b5/javascript/npm/draco3dgltf/README.md)
describes decoding the extension's compressed bufferView. The installed system
CLI's [PLY exporter](https://github.com/google/draco/blob/8786740086a9f4d83f44aa83badfbea4dce7a1b5/src/draco/io/ply_encoder.cc)
omits generic joint/weight attributes, so a successful CLI geometry export would
not establish an animated skin oracle.

The model's assets retain `CC-BY-4.0` and the separately described Cesium mark
conditions in its `LICENSE.md`; references/captures preserve that attribution.
This prototype does not establish Model.Load backend delegation, final portable
GLB GPU optimization, vmdl load/memory parity, Windows validation or editor parity.

## Frozen execution handoff

The coordinator resumed after its temporary model-capacity error. The raw GPU
shader/triangle gate takes precedence over expanding model execution. This owner
has no editor/job/process handles. No renderer/producer file was edited here.

Queued in order:

1. Preserve the first v1 oracle/receipt (`reference.json` SHA
   `e025da32168f89a031c07cc10a1a36b7898affc2e3031e3e8c581b2ef88eebc2`;
   first red receipt SHA
   `cafe8f5212a59e56badad42e625518aa447ae236f779cf193078e1f9b5b8862c`).
   Run the updated `../../../../../engine/Tools/Gltf/direct-gpu/models/CesiumMan/author_reference.py` command above to generate schema v2 with
   thirty total states across three distinct core variant oracles. Run
   `Sandbox.Gltf.DirectGpu.CesiumMan.CesiumManReferenceTests`: 23 expanded cases,
   `Configure(checkoutRoot, referenceJsonPath)`. The corrected expectation is
   unrun, not an assumed 23/23 result.
2. Run `../../../../../engine/Tools/Gltf/direct-gpu/models/CesiumMan/author_draco_reference.cjs` above, then
   `Sandbox.Gltf.DirectGpu.CesiumMan.CesiumManDracoReferenceTests`: seven cases,
   `Configure(checkoutRoot, dracoReferenceJsonPath)`. Inspect actual weight and
   joint validity. The official decoder download is complete; execution remains
   pending. Preserve any original decode-invalid evidence for review.
3. Only when decoded geometry is valid, author own-variant poses:

   ```bash
   python3 engine/Tools/Gltf/direct-gpu/models/CesiumMan/author_draco_pose.py \
     --corpus Temp/gltf-reference/glTF-Sample-Assets \
     --decoded Temp/gltf-validation/direct-gpu/CesiumMan/draco-reference.json \
     --output Temp/gltf-validation/direct-gpu/CesiumMan/draco-pose-reference.json
   ```

   Run `Sandbox.Gltf.DirectGpu.CesiumMan.CesiumManDracoPoseTests`: two cases,
   `Configure(checkoutRoot, dracoPoseReferenceJsonPath)`.
4. After the shared backend's raw GPU gate passes, compile
   `../../../../../engine/Tools/Gltf/direct-gpu/models/CesiumMan/CesiumManGpuChecks.cs` against that actual shared backend and use the
   coordinator's private scene/camera wrapper. Invoke `AssertSnapshot`,
   `AssertTopology` and `AssertDistinctPoses` as described above. Run source and
   compiled forms and all instance/lifetime/capture controls. Publish only actual
   completed GPU/raster/media evidence, separately from these 32 source/CPU cases.
