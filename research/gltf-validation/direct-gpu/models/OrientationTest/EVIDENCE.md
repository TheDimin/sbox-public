# OrientationTest evidence

## Actual editor producer tests, 5 November 2026

The coordinator executed `OrientationCoreTests.RunAll` against the physical
editor's loaded engine and glTF assemblies. Inspection of the receipt confirms
**30/30 Orientation cases passed**, covering all three original source variants
and compiled GLBs derived from each. Cleanup errors were empty.

- Receipt:
  `Temp/gltf-validation/direct-gpu/orientation-cesium-core-first/probe-01-editor_direct_gpu_tests/receipt-01.json`
- Receipt SHA256:
  `cafe8f5212a59e56badad42e625518aa447ae236f779cf193078e1f9b5b8862c`
- Tested `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/OrientationCoreTests.cs` SHA256:
  `d7323a9717e3b2dce86b050b6bda68ada3429ea0b90b34e80f7bfa1207491977`
- Independent generated oracle SHA256:
  `0f285d1233e60bde0da516a01d7da922233a751fb8859f13426cc0e179826af9`
- Engine MVID: `04b86c2e-4406-422e-a466-b61ec13f1f86`
- glTF MVID: `b064c360-8818-4f8c-8963-27d894571c5f`

This establishes source/container/upload/transform/static-assembly behavior only.
No GPU renderer, shader, public `Model.Load` managed backend or rendered-image
claim follows from these rows. The enclosing combined receipt is **51/53**, with
two separate Cesium Embedded failures; it is not an all-green overall suite.

The oracle's first self-test run was 10/11 because its geometry fingerprint
distinguished JSON `0` from `0.0`. Read-only inspection confirmed identical
27,168-byte geometry buffers in all three variants. Fingerprint canonicalization
now normalizes only numeric spelling/signed zero, with a regression proving that
different nonzero double values remain distinct. Its next run passed 12/12.
The first red is retained; no engine behavior was changed to satisfy it.

## Expanded source-layout tests

The coordinator subsequently executed all **40/40 cases successfully**: the
30 upstream/container cases plus ten source/compiled interleaved fixture cases.
These include shared-view storage and owned-byte accounting. The independent
layout fixture's four self-tests and generation also passed.

- Run: `animation-layout-draco-first-161`.
- Receipt SHA256:
  `cee5955d7dff542675725caf59e467c8bb14a32ac6a708f98a3a254eb2ac6ef1`.
- Tested `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/OrientationCoreTests.cs` SHA256:
  `825eadd1b5670d2b1cc4cdf23d2f76d58041be91983ecff3076906d5759d5669`.
- Engine MVID remained `04b86c2e-4406-422e-a466-b61ec13f1f86`;
  glTF MVID was `1de213d3-1748-43d1-9a16-92a13bc9277d`.

This remains CPU/data proof, not rendered-image or public managed-Model proof.

## Original public API red baseline and pending rerun

The coordinator executed the unchanged original public API tests before the
managed resource implementation: **7 passed, 18 failed, zero incomplete**.
The six bounds/material metadata rows and native vmdl control passed. Twelve
resource/cache architecture cases failed because the glTF loader produced a
native `IModel`; six ordinary renderer cases failed because it did not produce
the required `SceneCustomObject`. There were no compilation, setup or cleanup
errors. These are retained expected architecture reds, not parser failures.

- Run: `public-model-original-red-baseline`.
- Receipt SHA256:
  `ec3f3a6d72146904201a6e5b139702cdcb4a1f3ceaecfeb2b33d3a88200bebe7`.
- Original `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/OrientationPublicModelTests.cs` SHA256:
  `80dd0c8cd042e4262d98c2762876db7b035acd6d67c702cc2290d12298e4b9a4`.

## Installed managed implementation, 6 November 2026

The coordinator built and installed the frozen `fee5f79b9c384156bde089e96666d306`
Engine/glTF stage, then executed the unchanged original public cases together
with seven backend CPU/empty controls: **26 passed, six failed, zero incomplete**.
All 18 public resource cases passed, including absence of a native `IModel`,
cache identity, destroy/reload, bounds and materials. The vmdl control and seven
backend controls passed. All six managed component cases found a custom scene
object but failed the original `HasNativeSceneModel` predicate. That predicate
tests only whether the returned handle wrapper is non-null; existing engine
`Model.FromNative` additionally tests `IsStrongHandleValid`. These six failures
remain recorded and have not been waived.

- Public/backend 32-case receipt SHA256:
  `ba660d1310025fd2b896cc51610f76ee16938a0ad1300b10581d919ed6c68f29`.
- Installed Engine MVID: `1480297f-f517-46ce-b6a6-3689ef15f266`.
- Installed glTF MVID: `d2ca5caf-1c76-42c8-8756-77be9f1a556b`.

The first three-way handle diagnostic measured the following:

| Control | Handle wrapper null | Wrapper valid | Strong model valid | Binding read | Public `SceneObject.Model` |
|---|---|---|---|---|---|
| Bare `SceneCustomObject` | false | true | false | no | null |
| Managed public glTF renderer | false | true | false | no | null |
| Existing vmdl renderer | false | true | true | nonzero | non-null |

That run was **2/3**, because its native identity assertion compared against the
static `Model.Cube` wrapper rather than a captured public path load in the active
resource context. The native model's strong-valid/binding/public-model evidence
was still recorded. The next diagnostic revision captures one explicit
`Model.Load("models/dev/box.vmdl")`, as the passing original vmdl control does.
That corrected control passed **3/3**, including exact native public-path wrapper
identity, without compilation, cleanup or incomplete cases.

- First diagnostic receipt SHA256:
  `bb030a1bb0ad59cfbcccadd95563ad3bbed259b2c6e573bd616956189f47cf60`.
- First diagnostic source SHA256:
  `bf58a8205cece6cd7c5097573bd940a231354e3e27d361ca6a6e8c7c57fefe7c`.
- Corrected identity diagnostic source SHA256:
  `56634c32f26fa03d1fecd8012c279985cacc748f65c0bdbd378a998697ebce6d`.
- Corrected identity diagnostic receipt SHA256:
  `de5b8725a80c9f09d944f258cbf1f6479c25a657cd26751c7d4d5a5378e716af`.

After that measured control and managing-agent review, the public acceptance
predicate was corrected to `!handle.IsNull && handle.IsStrongHandleValid()`,
matching `Model.FromNative`; every non-null returned handle is still released.
There are no type-specific exemptions and all other original assertions remain.
The original 25 cases plus the unchanged three controls passed **28/28** against
the same installed `fee5` producer, using corrected public helper source SHA256
`7b100032111d017ca7715f5a1e85407be32e0895e246f55d749d85f6d34f878d`.
The receipt SHA256 is
`6f10ea3ea583e955e581505019cfcf33fea3974d77238fb10ca70bc2c38e5fdf`
(`fee5-public-model-predicate-fixed-28`); compilation and cleanup were clean.
The original source `80dd…`, initial red baseline, first managed 26/6 receipt and
first diagnostic 2/1 receipt remain historical evidence.

The additional six `LoadAsync` and ten ordinary-renderer lifecycle cases then
passed **16/16**, without compilation or cleanup errors. They cover shadow and
render-option transitions, failed replacement and enable cleanup, independent
placements and retained-generation lifetime.

- Run: `fee5-public-async-lifecycle-16`.
- Receipt SHA256:
  `917974e1395fd7fcb7e0c48033cd40276bc6fc9b27010d60826bb931c4bd8fba`.
- `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/OrientationLoadAsyncTests.cs` SHA256:
  `447649d04d3c151dd5aeeecaf1af7066021be658f7b6e3986b609e0dbd57def7`.
- `../../../../../engine/Tools/Gltf/direct-gpu/models/OrientationTest/OrientationRendererLifecycleTests.cs` SHA256:
  `79a9a49a9b42154c01983b5c170fc32bdd1db1a1567538b3c6f8ba721d7082dc`.

These establish the tested public resource/component structure and lifetime
behavior; they do not establish image parity or complete SkinnedModelRenderer
integration. Two additional public invalid-wrapper CPU-retirement weak-reference
cases await execution; they hold the destroyed Model and disposed components
deliberately so that disposing native resources alone cannot hide retained
source arenas or decoded rigs.

## Actual bounded public Orientation capture

`OrientationPublicCapture.Run` passed on the actual fee5 public `Model.Load`
and ordinary `ModelRenderer` path. Its single glTF/source, identity-placement,
positive-source-camera case produced **13 passing GPU rows and seven clean
native image controls**. The source camera was mapped without reflection;
visual review confirms blue on the left, red on the right and green on top.

- Receipt SHA256:
  `e4bbd94e73e154ce1ba31fe33f87700a7c7ae266cde6d34a6bffd394789261ff`
  (`fee5-public-orientation-capture-first`).
- Maximum GPU position component error: `1.52588e-5` inches.
- Maximum GPU normal component error: `1.19e-7`.
- Actual/reference foreground: `11256` pixels each; foreground and colored
  region IoU: `1`. Reversed-winding negative changed `11243` pixels.
- RGB maximum error: `.4706`; mean error: `.000941`. These are recorded
  observations, not independent material-fidelity acceptance gates.
- Public evidence manifest:
  `Temp/gltf-validation/direct-gpu/public-captures/OrientationTest/latest-evidence.json`,
  SHA256 `2a42de714e17849ed63d5b65a940641081682dadf4a72f8eac31a1e42b9b151a`.

The literal geometry reference borrows public model-owned native materials.
This proves independent geometry/normals/winding with shared material binding;
it does not independently establish glTF PBR material fidelity. The broader
48-case source/compiled/placement/camera matrix remains pending, and this fee5
receipt is not automatically evidence against later installed producers.

## Pending packaging and broader gates

- Original serialized `.gltf` compiled-artifact discovery and collision handling
  remain required integration tests pending an audited compiler/resolver mapping;
  see `PUBLIC_API_INTEGRATION.md`. Direct artifact loads do not establish them.
