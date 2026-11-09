# MandarinOrange current acceptance

The user-requested standard `KHR_materials_diffuse_transmission` implementation now passes bounded Linux technical acceptance. Human appearance approval is pending these **new** captures. The prior core-fallback review was rejected; it is retained as historical evidence, not current appearance approval.

The pinned corpus commit is `90d7ede14c7e280af263824604b427a1ca02cb66`. There is one actual upstream variant, `glTF`, source SHA256 `148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091`. The one canonical derived artifact is `Temp/gltf-validation/direct-gpu/MandarinOrange/MandarinOrange.glb_c`, SHA256 `60373e48dd9216b020590a16fd0c7d3005dc489646b60f0abb129e90132d422f`, 4,146,980 bytes. It was refreshed once through the actual installed `GltfCompiler.CompileWithEmbeddedImages`; all four encoded source images were preserved exactly, no external image resolver was needed, and the original source/dependencies stayed unchanged. This establishes explicit artifact portability, not automatic native asset-graph packaging.

The exact reviewed seven-UV shader source is `dfbc59486512cab43579aa7e6a12ed14cf10d2667d50ab9e54346e2a655d24e9`; named compiled shader SHA256 is `cf871317a3ecbaf99b9c85b71219c1e6050534d46ab0cab3bd6149b6bcc08260`. Alpha/Primitive candidate changes were kept separate. The current public producer is Engine MVID `e66560df-8eba-4c30-998a-a0296a91e4af`, Gltf MVID `56e0e078-1fed-49a1-a3fc-86f4516b0cd0`; full module hashes and editor identity are in `result.json`.

Current receipt `40e408ee3a8956744516ba508d6504dd5ceba6023292886b8a0e9ccf032089fc` passed **3/3**, zero failures/incomplete/cleanup errors: two actual public source/canonical cases and one measured roughness-compatibility diagnostic. Both model cases used `Model.Load(path,0)` and ordinary lazy `ModelRenderer` realization, the named diffuse-transmission material, original source textures and actual GPU submission with a null native IModel. They checked 122,508 indices and four independent literal positions through the new material layout; maximum targeted position error was 1.0726392607462998e-8 inches. A same-shader factor-zero instance control changed 6,390 pixels. Original restoration, independent second-instance submission, survival after first-instance deletion and final empty deletion controls all passed.

Both actual main captures have identical SHA256 `4d78bd230389157509188e7db36596cac4edb24e81122ac65a5997ef81d373ff`, 6,457 foreground pixels:

- [Actual source capture](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/MandarinOrange/diffuse-public/a46a115ee4b24c4ebfe1b54c7b370cb6/actual_public_diffuse_transmission.png).
- [One canonical compiled capture](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/MandarinOrange/diffuse-public/e4925c926d5846208616b80252467ebe/actual_public_diffuse_transmission.png).

These forms belong to **one visual comparison**. Its main visual is now the source-only Field HDRI/sky1151x1309 capture described in the current lighting correction below; the canonical compiled capture remains the original192px functional proof. The full authored `with-without.jpg` has diffuse transmission enabled on the **LEFT** and core fallback on the RIGHT. The current captures enable the extension. Their 192px private orthographic camera, black background and front/rear point lights are inferred; the reference camera, environment, lighting and exposure are unknown. The actual captures were viewed locally. This is not calibrated appearance parity or a benchmark.

The current public proof builds on original reader **17/17** receipt `66bc20f08320d03be1d1765f9fe10e17043725af957242d215e8e3b544e3b735` and anonymous candidate **1/1** receipt `710bdec85f756188d2238255621ee25e0a8812d75d7c0fb9249d7d3e25a12071`, each with its own producer identity. Candidate controls include independently calibrated .96 PI/Fresnel physics and wrong-PI negatives, measured positive FP16 truncation with per-value intervals, literal linear-alpha/sRGB-color and wrong-transfer/channel negatives, factor/metal suppression, seven arbitrary consumed UV sets with normalized offset/stride GPU readback and independent transform/repeat/mirror negatives, retained front specular and exact readback of every authored cube face/mip before opposite-hemisphere IBL. The exact candidate source was promoted only after root review. All 27 original positive core inputs, six compatibility inputs and the actual canonical artifact passed the pinned official Validator with zero core errors/no truncation. Its unsupported diffuse-transmission notices remain: these reports do not validate extension semantics. Failed prior attempts remain retained.

Factor-zero output is **not pixel-identical** to the ordinary native core source shader. The extension uses Lambert diffuse while the native core BRDF varies with roughness. At 60-degree light incidence, roughness 0/.5/1 gave maximum absolute linear RGB differences .023681640625/.019287109375/.0390625, and mean differences .009402771790822348/.0077340975403785706/.018119369943936665 across 12,288 interior values each. Both paths retain native specular services. This is a measured compatibility distinction, not an assumed equality or a tolerance-based conformance claim.

Historical core/resource evidence remains scoped to its original producers. The valid source has one authored scene and no root default scene: unselected `Model.Load(path)` remains valid, explicit scene0 supplies presentation, and no fake default or native geometry placeholder was added. Resource identity/library/empty cases and exhaustive source/index GPU checks were proven on 01459; six affected reimport/observer cases passed on e12 (`380c7c994c6f411ce62eec7da72e99e74b65ccc680816c1aefaf715477ed4def`). Earlier 8fc source/core-fallback public2 (`1ca782cf...`) is historical. None of those suites was rerun or relabeled as the current producer. Full previous scopes remain under `historical_core_fallback` in `result.json`.

User appearance approval, editor UI parity, Windows, performance and complete-format acceptance remain separate gates. Point-light/literal-cubemap controls do not claim all directional/baked lighting, volume or optical transmission behavior; unrelated BLEND and no-normal point/line work remains separate. See [result.json](/home/damian/projects/sbox-public/research/gltf-validation/direct-gpu/models/MandarinOrange/result.json) for receipt/media hashes and current limitations.


## Historical black-background settling follow-up

The bounded source-only exposure follow-up passed **1/1**, receipt
`3a470c3d656481c1624c62f66ea3c7ff157290bea620a0f82ed0e7fdc47d7179`.
The previous visual was [settled source capture](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/MandarinOrange/diffuse-convergence/8a470e404eea46918535792a30e57905/settled_actual_source_diffuse_transmission.png),
SHA256 `d2ef1d57919fead0e0880b50f773b0e4c02567cf405b1260119166a7e7986a5b`,
1151x1309 pixels (the authored screenshot-large dimensions). It was viewed
locally and by root. Camera pose, vertical orthographic extent, original source
material and front/rear point lights are unchanged; resolution/aspect change
horizontal extent. This is one source visual, not a high-resolution compiled
rerun. The accepted public3 receipt/canonical192px proof is unchanged.

Runtime postprocessing, auto exposure and native tonemap were all enabled:
rate20, min1/max2, compensation0, AAtrue. Five actual RenderToTexture submissions
and readbacks produced exactly zero RGB deltas across the four successive
foreground-union comparisons; first/final full PNG bytes were identical.
Foreground mean display luminance was .5085539063743977 with299765pixels.
The camera was already settled at the first actual RTT. No exposure warm-up
defect was observed. The grey/yellow appearance versus the upstream reference
remains human review under different inferred lighting; no brightness edit,
reference-match claim or native feature-suite rerun was made.


## Current HDRI lighting correction

The user's v28 chat concern was that the Mandarin should have an HDRI skybox
and the bright lighting made it almost white. This concern is tied to prior
visual fingerprint `a567b47f98b6e735dcd7fe3aa41bbf96d1a76ef0dec31ec68283f18ceddaf336`;
it is parent-relayed feedback, not an invented website form vote.

The current source-only [Field HDRI and sky capture](/home/damian/projects/sbox-public/Temp/gltf-validation/direct-gpu/MandarinOrange/hdri-presentation/80b44f113b434e8590bddf8da790e34d/settled_actual_source_field_hdri_sky.png)
has SHA256 `ba635d35cc0d8c03e5bb853d6a107b17470ab8a0a1de8939d029cda45505491d`,
1151x1309 pixels. Root viewed it and approved publication for user review,
not final appearance acceptance. This replaces the current visual row; it
is not another source variant. No further lighting iteration is planned
until the user reviews it.

The two bright white point lights are disabled/removed. Native Field IBL and
a matching visible Field sky use intensity .75/yaw0. Ambient RGBA is zero,
including alpha, so it does not override the actual indirect irradiance.
The native camera uses ACES, exposure min=max1/EV0. Camera pose remains
(4,0,0) toward the origin; projection deliberately changes from orthographic
to perspective18degrees vertical, approximately preserving vertical framing
while showing the sky. These choices are inferred, not authored upstream
camera/environment/exposure parity. Source textures, geometry, material and
shader feature data remain unchanged.

Receipt `00ead190e6f7049b42210039ca9b6f8bcff0d1a77e6badb1a1795b9a13d30995`
passed **1/1**, zero failed/incomplete/cleanup errors. Actual sky-only,
model+sky+IBL, same-sky/no-IBL and final deletion/black states each settled
in5 actual render-to-texture submissions, with four successive full-image
RGB maximum/mean deltas exactly0. Removing the IBL probe changed311280
foreground pixels while sky stayed fixed. Final sky/probe/model deletion
restored exactly black. Foreground mean display luminance is
.2387252284608487, with zero near-white pixels where everyRGBchannel>=230.
This measured presentation result does not impose an automatic appearance
approval. The initial attempt's metadata-only Width/width serialization
failure is retained; its corrected retry changed measurement names only.
The accepted diffuse feature3 and canonical192px proof were not rerun.

Field is the existing HDRI-Hub environment distributed with Khronos
Sample Environments: [HDRI source](https://www.hdri-hub.com/hdrishop/freesamples/freehdri/item/116-hdr-040-field-free),
[CC BY3.0 license](https://creativecommons.org/licenses/by/3.0/).
Compiled disk SHA256 is `01cc0d0f0f69aa856b6650c71d5a4c8528667f61444b35b7c9bc382cda98fc55`;
actual native sky/IBL response is proved here, while live resident HDR texel
byte identity is not newly proved. Reference environment/orientation and
exposure are unknown. The existing diffuse BRDF/core factor-zero distinction
and remaining platform/editor/performance limitations still apply.

## Aligned reference overlay

The follow-up to comparison `93008dff7f61a2f316419af09eb7fb6c82bcc3009707829ccc63c3f7a2c6e88b`
uses the pinned standalone `screenshot/screenshot-large.jpg` at 1151×1309.
The upstream Mandarin README identifies it as a render from the development
glTF Sample Viewer with diffuse transmission support. The paired
`with-without.jpg` remains a supplemental extension-on/off comparison.

The private native camera was fit to the reference silhouette from the
original geometry. It looks from `(1.11069021,-.02845442,.00645021)`
toward `(.00087155,-.01676070,-.03103364)` with up vector
`(-.03377338,-.00170327,.99942807)` and a 42.21343° vertical perspective
field of view. A 12-profile native sweep compared four Field HDRI rotations
and three exposures. The final comparison uses Field intensity 0.75, yaw
0°, and fixed ACES exposure 2.5. Upstream camera and lighting settings
are unknown.

The aligned native capture reached **88.65% display-luma similarity**
and **97.20% silhouette intersection over union**. Linear-luminance
similarity measured **79.92%**. For the brightness score, compute the
sum of pixelwise lesser luma values divided by the sum of greater values
over 803,873 common foreground pixels after a three-pixel erosion.
Display luma uses Rec.709 weights on RGB codes. Linear luminance applies
the same weights after inverse sRGB transfer. The native mask comes from
same-camera sky-only subtraction. The screenshot mask comes from a
green-minus-blue threshold and a five-pixel morphological close.
These metrics cover the aligned fruit. The reference appears redder
through its pulp and peel. The native image retains its Field background.

Final receipt `81c00d8bdb194c634229a28ff33f50a6391bd464d6e60c1a1e64572f0eb26285`
passed the focused native case. Sky-only settled after eight actual RTTs.
The visible source render, IBL-off control and deletion control each
settled after five. All four final pairs had zero RGB code delta. Removing
the IBL probe changed all 849,129 native foreground pixels and five
pixels outside that foreground mask. Deleting the source, sky and probe
restored an exactly black frame. The public source/canonical 3/3
functional receipt remains separate.

The selected unmodified native PNG SHA-256 is
`62b86753ba04be5ad4692e2748aef7cf501b4d00ea9a5f4d8c685a822fb41403`.
The comparison handoff in
`Temp/gltf-validation/direct-gpu/MandarinOrange/overlay-handoff.json`
records both original image paths and hashes, measurement inputs, the
earlier comparison fingerprint, and capture and control provenance.
