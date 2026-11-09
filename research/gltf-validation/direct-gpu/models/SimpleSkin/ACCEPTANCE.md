# SimpleSkin direct GPU validation

Owner: dedicated `gpu_simple_skin` worker. Runtime/build/capture execution belongs to `gpu_validation`.

Status: the 24 CPU/data cases passed in the actual s&box editor; direct GPU tests and image validation remain pending. These artifacts do not claim that `Model.Load` has acquired the direct GPU backend, that a GPU draw has occurred, or that existing native-model captures prove this path.

The executed receipt is `Temp/gltf-validation/direct-gpu/core-model-tests-alias-fixed/probe-01-editor_direct_gpu_tests/receipt-01.json`, SHA-256 `1a7570f0167fa928fe7927b959f9bb8b2c2a15a0d36d8126d45697c656f98e94`. It contains 24 passing SimpleSkin rows against loaded Gltf MVID `b064c360-8818-4f8c-8963-27d894571c5f` and Engine MVID `04b86c2e-4406-422e-a466-b61ec13f1f86`. Executed source hashes are CoreTests `c18763004d294e3440a2e6eb20a8170346b2cf7023ce10d994d0421a491ed852` and Reference `2bb15660cb3a57fdd997c2b6b92b16b0ac6baa9789a2d2f360a48b65c0722226`. The earlier rejected compile ran zero cases and is preserved separately at `Temp/gltf-validation/direct-gpu/model-tests/5dbe4ae3e42643d99d469e58ab51ef72/receipt.json`; the alias correction changed no test logic or tolerances.

The corpus is `Temp/gltf-reference/glTF-Sample-Assets`, pinned to `90d7ede14c7e280af263824604b427a1ca02cb66`. The sample has two authored variants, `glTF` and `glTF-Embedded`; there is no upstream binary variant to invent. Each source and its independently compiled valid GLB must be tested, giving four forms. Input files are bounded and hash-checked by `SimpleSkinReference.OpenForms`. Keep the upstream license with any packaged copy of the model.

## Independent expected data

There are ten positions: the two X coordinates `-.5, +.5` at each Y level `0, .5, 1, 1.5, 2`, all Z zero. There are eight positively wound triangles. The two joints are nodes 1 and 2, with the second joint's pivot at `(0,1,0)` and inverse bind translation `(0,-1,0)`. The second joint's weight is `Y/2`; the first joint's weight is `1-Y/2`. No authored normal, tangent, UV, color or material exists in this sample.

The twelve rotation keys are at half-second intervals from zero through 5.5 seconds. They are rounded authored quaternion values, including `(.383,.924)` and `(.707,.707)` in the Z/W components. The oracle converts these literal values to angles with `2*atan2(z,w)`, then linearly interpolates the same-axis shortest arc. It does not substitute exact 45-degree keys, call the production sampler, evaluate a production palette, or read implementation output to construct expected vertices.

For bind position `p` and weight `w=p.y/2`, the expected posed point is `(1-w)*p + w*(Rz(angle)*(p-(0,1,0))+(0,1,0))`. The expected engine point is `(z,x,y)/.0254`, followed by the explicitly supplied placement matrix once. All triangles remain planar with source normal +Z, hence engine normal +X before placement. An inverse transpose of placement supplies the independently expected final normal.

The source JOINTS accessor is **U16x4 with stride 16**, although the element contains only eight useful bytes. WEIGHTS is FLOAT4 at **offset 160 in the same 320-byte view**, also stride 16. This makes the real sample a useful test against packed-layout assumptions. POSITION is FLOAT3 at stride 12; indices remain U16. Compilation may move the bufferView's buffer offset; it must preserve these relative binding semantics.

## Callable tests

Compile `../../../../../engine/Tools/Gltf/direct-gpu/models/SimpleSkin/SimpleSkinReference.cs` and `../../../../../engine/Tools/Gltf/direct-gpu/models/SimpleSkin/SimpleSkinCoreTests.cs` against the **actually loaded** `Sandbox.Gltf` assembly. Do not compile a replacement producer into the helper. Invoke:

```csharp
Sandbox.Gltf.DirectGpu.Validation.SimpleSkinCoreTests.RunAll(checkoutRoot);
```

Expected inventory: **24 rows**, six named cases across four forms. The 67 distinct sample times cover every authored key, eighth-second samples, both sides of ten internal keys, and before/after endpoint clamping. Report each row separately. An exception before inventory completion is a harness failure rather than zero passing tests. The strict CPU fixture tolerances (`2e-6` source meters for animation, `.0002` engine inches for the composed palette) check this known analytic sample only; they do not approve an engine-wide resampling approximation.

Once the shared backend compiles against its required producer version, `SimpleSkinPlanTests.RunAll(checkoutRoot)` adds **12 pure staging-plan rows**. They check the actual raw arena's storage alias, offset 160 and stride 16, unchanged source bytes, flat per-corner topology with preserved winding, runtime field extents/nonoverlap, and independent staging snapshots. These cases allocate no device buffers and remain unrun; they cannot stand in for compute, draw or image evidence.

For GPU testing, compile `../../../../../engine/Tools/Gltf/direct-gpu/models/SimpleSkin/SimpleSkinGpuChecks.cs` and `../../../../../engine/Tools/Gltf/direct-gpu/models/SimpleSkin/SimpleSkinGpuTests.cs` with the shared direct GPU implementation. Invoke:

```csharp
await SimpleSkinGpuTests.MeasureAll(
    checkoutRoot, privateScene.SceneWorld, borrowedCompiledComputeMaterial,
    awaitCompletedFrame, borrowedDiagnosticMaterialResolver);
```

`awaitCompletedFrame` must wait for an actual completed native draw submission of the current revision and resume on the main thread; it must not modify a clock or pose. The private camera must include both instances (instance B is translated +140 engine Y). The result contains **544 measurement rows**: 67 sampled phases for A and unchanged independent B, plus full-affine placement and B-isolation checks, over four forms. Readback verifies descriptor-derived output fields, all output vertices and actual bound GPU triangle indices (`IndexDataOrigin="gpu-readback"`), finite values, independent phase, and nonzero actual draw calls with no callback errors. Generated flat corners are interpreted through the **CPU submission/provenance** map `SourceVertices`; that map is not a GPU readback. Output is never assumed to retain ten vertices.

Numerical errors are measurements. A parity pass additionally requires an explicit tolerance review and `SimpleSkinGpuChecks.RequireWithin`; do not convert merely obtaining a readback into a passing result. The shear placement test intentionally cannot round-trip through a decomposed TRS.

`SimpleSkinImageProbe.MeasureAll` accepts the coordinator's private scene, borrowed compiled compute/diagnostic materials, completed-frame wait and native capture delegate. It creates neither another camera nor another capture/rendering pipeline. The callback returns observed dimensions, `Color[]` readback, image path/hash, engine frame, monotonic wallclock and camera-state JSON. The probe runs 24 model/form rows with empty/reference/deleted-empty/actual/deleted-empty controls; four deliberately wrong-phase references additionally prove comparison sensitivity. The six poses include bind, positive and negative bends, between-key interpolation, shear and mirrored single-sided placement. The reference compensates reflected winding independently; forcing both sides visible is not an acceptable fix for a missing mirrored draw. Image IoU/RGB values remain measurements until `SimpleSkinImageProbe.RequireWithin` enforces reviewed explicit image and GPU thresholds. `ObservedPlaybackTime` is deliberately null: deterministic pose sampling has no playback clock to observe.

The bounded `MeasureSelected` overload takes explicit form and case-name arrays, rejecting empty/duplicate/unknown names before GPU allocation. Selecting `glTF/source` with `bind` and `positive_rotation_key` runs **two rows and 12 captures**, retaining the same positive/deletion controls and the bind case's wrong-phase sensitivity control. This is a smoke subset, leaving the full 24-row/128-capture matrix pending. Both methods share the same orchestration.

## Acceptance gates still pending

- [x] The 24 source/compiled CPU rows execute in s&box against the loaded frozen producer and pass.
- [ ] The 12 direct GPU staging-plan rows execute against the correct loaded producer/backend and pass.
- [ ] All four forms construct only C# GPU resources and raw draw submissions, with no native Model/Mesh construction or native animation/morph controllers.
- [ ] The shared raw-triangle gate and native empty-before, positive-draw, deleted-empty-after controls pass before this model's image assertions.
- [ ] All 544 GPU measurement rows complete; measured numerical tolerances are reviewed and enforced. Source indices, per-instance outputs and callback revisions match.
- [ ] Rest, positive bend, negative bend, between-key, full-affine placement and reflected-placement images are compared to the independent `ReferenceTriangles` callback using the same stock diagnostic material and camera. Verify a deliberately wrong phase/bind reference produces a measurable difference.
- [ ] Normal visualization confirms generated normal orientation; a source with no normals must receive the correct generated flat normal rather than an accidental engine-axis default.
- [ ] Native capture route is the proven `Normal` debug mode, normal postprocessing, owned light and explicit camera `CustomSize`; failed Albedo routes remain failures. Record camera projection and exact object transforms.
- [ ] Both source formats and their compiled GLBs render equivalently. Compiled GLB packaging does not yet constitute the goal's optimized-layout/load-time proof.
- [ ] Two instances animate independently; changing A's placement or pose does not alter B's GPU output or apparent phase.
- [ ] Creation failure rolls back GPU resources, repeated load/unload releases buffers/material leases, and deleting instances stops callbacks before buffers are freed. No retained geometry appears in a subsequent empty capture.
- [ ] Actual wall-clock playback has no dropped elapsed time. A 5.5-second presentation at 30 FPS requires 165 captured states with recorded durations; a low-rate diagnostic sequence or deterministic seek movie cannot claim real-time smoothness/performance.
- [ ] Gameplay-facing `Model.Load`/renderer integration uses the actual managed backend. This research helper is not accepted as the final loading API.

Material scope here is explicitly the stock default for an asset with no authored material or textures. It cannot prove textured PBR, sampler, extension or shared-material fidelity.
