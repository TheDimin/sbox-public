# Rejected NormalTangentTest visual comparison

The private review rejected fingerprint `69435aacb9af730a2758dcbdbb3bd5f2013c6924aa233759b5a79f8292e64640` with “Comperison does not look anything like.” That decision remains unresolved until a changed actual capture is reviewed. The old public source capture is visibly brighter than the upstream image, with white/black cells and no environment horizon in the metallic column. The upstream preview shows an outdoor green-ground / sky reflection; its exact HDRI, camera, exposure and tone mapper are undocumented.

The [Khronos sample environment list](https://github.com/KhronosGroup/glTF-Sample-Environments) names HDRI-Hub's [HDRI 040 Field](https://www.hdri-hub.com/hdrishop/freesamples/freehdri/item/116-hdr-040-field-free), CC BY 3.0, as an outdoor viewer environment. Its panorama has a ground/sky horizon and is a plausible selected diagnostic, not evidence it is the exact upstream environment. The fetched Khronos LFS HDR SHA256 is `ff5fca75908af1f98d1a458070b7fadac2e6fa9c0b7f997115ec4bc496aee0ef`. Shared environment owner prepared a linear float EXR SHA256 `a9ad16a6d516171e425d0fc22cdde643b2116ed544578945899958cea21e1e7f`, losslessly round-tripped decoded RGB at 4096x2048 with maximum 18.75. Native resource compilation and scene lighting remain unverified.

The bounded `../../../../../engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/capture_visual.py` prepares a private capture request, not a global recipe update. It retains the source glTF/materials and independent source hashes; selects measured reference background RGB `(51,51,76)` and an inferred front orthographic height of 2.28m to reduce the previous excess borders; requests a chosen Field probe and explicitly records a controllable directional fill and native ACES exposure. Input, transformed environment, output identity and actual post-render exposure need receipts before any claim. Both the environment and renderer's reference correspondence remain user review, not an automatic image pass. Preserve previous numerical GPU and public four-form proofs; they do not establish appearance.

As of this note, no editor is running. The integration owner deferred the guarded editor launch due the active resource-admission policy, so there is no native Field import, new capture, or resolved feedback. Do not present the prepared recipe or source HDR as a rendered result.

## 7 November native source investigation

The first outdoor Field source capture (`normal-tangent-visual-field-first`) improved framing and reflective background but did not resolve the user feedback: several normal-mapped right-hand cells look concave or rotated compared with their neighboring true geometry. The public world-normal capture (`normal-tangent-visual-normal-world`) makes the orientation difference explicit; first pink pair's true-sphere top/bottom blue channel is 183/61 while mapped-face top/bottom is 72/194. Both captures use the actual unchanged source model/material and complete cleanup. Hashes are `82d0519a7647d79249fc8f5c90d8749cc109b3ab9b4b0b09c5149e564f59930c` and `4372b2ccb09adbc6f7fbfd393d234a1dcceecfa5b4ac2189ffaaa3d122bdc0ad` respectively. The chosen Field environment is HDRI-Hub HDRI 040 Field, CC BY 3.0; its exact correspondence to the old preview is unknown. Tone-mapping component settings were retained, but the independent EV negative control did not change pixels; exposure behavior is not proven.

Three-way source-material GPU diagnostic receipt `33413feb92f0e9499a034a23352735b6a8a5971afea4242c1ace887f275948a9` compared the unchanged source with private anonymous shader controls. Inverting texture green and inverting only generated tangent W yielded identical normal-debug pixels (`75e6301c6f8e7836a6ee488ac3e92b3f18d8e438565d6f6e838529501eb2ab2d`); their geometry-to-mapped patch error was approximately .0977 versus .1847 baseline. This is sensitivity evidence and does not decide which conversion boundary would be specification-correct. The earlier independent authored/mirrored-UV fixture passes the installed shader and must be retained as a regression.

A source-independent analytic check (`../../../../../engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/inspect_normal_reference.py`) reads the pinned binary accessor bytes and original PNG directly, without producer evaluators. At point `(−.642,.8452054)` on mapped first sphere, triangle `(258,3966,259)` has N=+Z, UV-derivative tangent T=+X and Mikk orientation W=−1. PNG pixel `(126,185,241)` gives tangent-space positive Y and the glTF bitangent formula `cross(N,T)*W` predicts source Y ≈ −.452; the neighboring true sphere at corresponding point `(−.963,.8452054)` has interpolated source normal Y ≈ +.459. This establishes a normal-direction difference between the source mapped cell and adjacent geometry under the stated analytic convention; it does not identify its cause or establish a source defect. [glTF 2.0.1](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html) defines tangent handedness and normal-texture interpretation; a global inversion cannot be selected solely to mimic this asset while the independent authored fixture succeeds. The investigation remains open, and neither negative control is a public shader fix or visual approval.

An independent native MikkTSpace reference implementation (`mmikk/MikkTSpace` revision `3e895b49d05ea07e4c2133156cfa94369e19e409`, C source SHA256 `de87e74107df766ce68108801262bd8d53899414236b59810509a8fc2a51e288`, header SHA256 `17fc433894f24c73753d548086cc4d8c5c0379f4a6edfb98b5da243e4f0bc3d0`) was compiled with the model-owned `native_mikk_oracle.c`. It consumed all 3,983 pinned positions/normals/UVs and all 23,322 source indices; input SHA256 `fd9395a30c5003c379d153678ed244c72b81f853f59a93f41982df23ccbd3435`, output SHA256 `b4f9f843a7e503c2d14c9463b07f240cb94b57c3b8829c0a9f0b96163b3e5187`. All three target triangle corners output `(1,0,0,-1)`, consistent with the analytic derivative.

`../../../../../engine/Tools/Gltf/direct-gpu/models/NormalTangentTest/inspect_normal_reference.py` independently maps two measured output pixels to source positions, source triangles and nearest PNG texels, constructs `cross(N,T)*W`, and predicts the public world-normal visualization. Its report `/tmp/normal-tangent-source-oracle.json` SHA256 `20c5cea0ec631217cb7bfe7c031089909e86704e31b62771aeb744fdb27cf019`; upper prediction RGB `(241,126,70)` vs actual `(242,128,72)`, lower `(238,126,192)` vs actual `(236,128,194)`. Nearest source texel approximates actual bilinear/mip filtering and is not a direct shader-sample readback. A direct bound-texture resource readback at those source texels remains queued. This evidence is not an identification of a defective source, and no global normal-sign change has been made.

## Confirmed Mikk-to-glTF handedness conversion

The earlier arithmetic above correctly identified the native Mikk output and current GPU behavior, but incorrectly assumed raw Mikk W was already the glTF output convention. It must not be interpreted as evidence of a source defect or as the final expected glTF world normal. The [official Khronos glTF Sample Renderer](https://github.com/KhronosGroup/glTF-Sample-Renderer/blob/93d0a94da6728ba36090f2526f212e11c26183aa/source/gltf/primitive.js#L1495-L1499), pinned commit `93d0a94da6728ba36090f2526f212e11c26183aa`, source SHA256 `9fc3ff57d14033791126ea42c8983bafe43a8cf01606139afe90e64492e2eae3`, explicitly negates Mikk W before publishing generated glTF tangents. The renderer author's [MikkTSpace WASM documentation](https://github.com/donmccurdy/mikktspace-wasm#other-considerations) and the original [Khronos NormalTangentTest issue](https://github.com/KhronosGroup/glTF-Sample-Models/issues/174) record the same source test and conversion, including mirrored tangent controls. The vendored managed port callback and original pinned native C both return `+1` for orientation-preserving and `−1` otherwise; there is no implicit conversion in the port. Our generator previously stored that raw result unchanged.

Thus the correct candidate fix is a single negation at the generated-frame Mikk callback boundary; existing authored glTF `TANGENT` values and normal-map green channel remain untouched. The glTF basis remains `cross(N,T)*W`. Existing GPU green-vs-generated-W diagnostic produced exactly matching flipped pixels and establishes sensitivity, not complete acceptance. Independent converted/raw Mikk, mirrored/non-mirrored and degenerate controls and a fresh source/compiled actual model render are required after installing the fix; prior raw-sign receipts remain historical.

The focused shipped `Sandbox.Test.Gltf.GltfGeneratedTangentsTests` run after the generator edit passed 6/6 cases, including mirrored seams, UV rotation, strip/fan expansion, and the large shared fan; no tolerances were changed. Source SHA256s: generator `e4bd4df1b2243655c378fb5195f4b28645bb56d9c78fdcffb5b8bed37e867029`, pure test `d7ffe9544af589b62f9822855e51e3d7265bef19cbe64c3099ad39ab616ffb3c`. The independent native differential remains a separate post-install gate. Old-stage red receipt `bd7afacbc953be35af3af546d80be7968e1c71e11b4de6e20d8843cc0db9fc02` recorded 4 passes and 3 expected failures before the corrected generator was loaded; it is not a green result.

The corrected 0bc producer passed the focused native gate 12/12, receipt `a392f5cd3cfd211e8ee4d38a5ea02123390fffa42266397f314f41a8526f0a13`. This includes independent pinned native Mikk differential, mirrored/generated corner and skinned flat-normal checks, authored-tangent image controls with wrong UV and wrong W negatives, and actual public source and canonical compiled renderer frames. Both forms checked all 23,322 GPU corner tangents with maximum error 2.682209e-7. The exact canonical `.glb_c` SHA256 is `5ac0932355ae1ea05a7485eeddcc3f1fbe56c678e00b519075feced80e9e9d6a`.

The corrected source and canonical compiled 512×483 captures have SHA256s `65ab8cf8d7a8a86d2d728d79577f1c7b08affe57b726434eefa7afb53c3d2ada` and `ff4c02e3b709b11bf71516efa3272889a8f90d05685728f61c4b0bda09e07d0d`; corresponding returned-value receipts are `aa48d35e1ea4d4c6e68c09f1f38a525b781ca766b9d3df8c2f76ba4d3633779a` and `ceabe8062943628529cca9604f47367630a2a6e1dc002e9c7a76e7a81095d42f`. Both public loads/renders completed and cleanup was clean. Mapped cells now look convex, although the metallic reflection visibly differs from the reference horizon. These two captures differ at 38,057 RGB pixels, so they are not claimed identical. The current compiled Field file is `01cc0d0f0f69aa856b6650c71d5a4c8528667f61444b35b7c9bc382cda98fc55`; the capture records a valid 1024×1024×6, nine-mip BC6H descriptor. Raw HDR texels, resource-to-resident byte identity, mip filtering, effective exposure and reference environment equivalence remain unverified; independent Field BC6H readback produced NaNs. The user’s rejection remains recorded and the changed images require their judgment.

## High-resolution canonical review

The user rejected comparison `NormalTangentTest/glTF-Binary::canonical-normal-review`
at 2026-11-07T03:19:15.352Z, fingerprint
`2e380de7f0b5695d1ce0af550f994222eaaeb325444be13db1c30b7267172a9d`, because
the 512×483 capture was too small to judge. The pinned Khronos README shows
`screenshot-larger.png` at 460×434, SHA256
`fec8952e7af0ed6768547524070a6a30fd3c5ee9986886afe47de9be9196c3b7`.
The new native target is 1380×1302, exactly three times those dimensions.

Public `Model.Load` and `ModelRenderer` captured the same canonical GLB
`5ac0932355ae1ea05a7485eeddcc3f1fbe56c678e00b519075feced80e9e9d6a`
with the prior orthographic framing, Field cubemap, directional intensity
0.3, and ACES settings. The guard admitted 1.5 GiB additional RAM on the
existing idle Survive editor. Capture and teardown succeeded on Engine
`362ddfea-670c-444f-8088-0f6c6453080e` and glTF
`56e0e078-1fed-49a1-a3fc-86f4516b0cd0`.

Seven native render-to-texture submissions ran on distinct editor frames
with active native tonemapping and observed exposure limits [1,1] at EV0.
Samples 2–7 had identical decoded RGB pixels. The initial sample differed
at 437,755 pixels, with the cause unassigned. The final image differs at
808,817 RGB pixels from a Lanczos enlargement of the rejected capture and
at 101,620 RGB pixels when reduced to the old dimensions. The larger image
makes the paired cells and orientation arrows legible. Metallic colors and
horizon reflections still differ visibly from the pinned reference.
Appearance approval is pending.

Artifacts are in `Temp/gltf-validation/direct-gpu/normal-tangent-canonical-hd/`.
The final `case/capture.png` SHA256 is
`c83d7db28c43f057ac1023c4914086af201a03f96dd907b74a807a3c2161e63f`.
The `returned-value.json` SHA256 is
`4c5ddfa1f20f30ea762c41f563aa947ed55ae3de57975c7b02f214fec33eb7cd`.
The frozen `request.json` SHA256 is
`32f2d042625bfb0b26286bef6d4d85cff737d1689abeb8aa96d30ed82d6dd24d`.
The initial script compilation rejection for a direct internal
`SceneCamera` access is retained in the sibling `-compile-rejected` directory.
The successful retry observed the native flag by reflection. The 12/12
generated-tangent gate retains its original producer and receipt.
