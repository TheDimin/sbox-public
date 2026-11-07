# Direct GPU validation execution queue

Model owners execute their own guarded tests and captures under the shared native
test lock. The integration owner coordinates shared builds, installed shaders and
any safe audited editor restart. Freeze source and expected assertions before a
run; the lock spans guarded admission, request copy, execution and receipt review.

## 2026-11-06 current integration audit

The actual editor was revalidated under the lock and resource guard: PID 155513,
start 1006069, boot cb62e383-be18-435a-84e8-c3ed85ac4ced, visible physical
Survive editor, minimal.scene clean and idle, compile succeeded. Live loaded
Engine b5b66ab1, Gltf 88f89cfc, SceneRenderer c22b305b and Tools 01d79f9c
match their disk MVIDs; the 2,047 frozen stage 9b9f project inputs still match.
The current marker is `Temp/gltf-validation/editor-session-ready.json`; it is a
locator and does not replace fresh checks. Older current-runtime sections below
are historical snapshots, not live identities.

The actual graph compiler bf9c2c65 generated both provided opaque Surface/ORM
and literal unlit reference sources: two generation cases passed, and output
hashes matched the earlier reviewed bytes. On the current producer the guarded
anonymous-template emission control passed all eight source/compiled literal
comparisons, with clean teardown. Receipt:
`Temp/gltf-validation/direct-gpu/today-emission-run/probe-01-editor_direct_gpu_tests/receipt-01.json`,
SHA256 `5d1b40ee467269d3f6ce51716b92038e50618ac7343e96f21245afe8aec1b492`.
This proves only the bounded emission and omitted-texture controls, not complete
PBR, normal, alpha, public selection or whole-model appearance. The NormalTangent
and Alpha owners independently reproduced current public stock adapter rejections.

## Current validation runtime: revalidate before acting

Managed stage `8e23b6c8610f41009cd6f9983351530e` built Sandbox.Gltf,
Sandbox.Engine and Sandbox.Tools with zero warnings/errors. All 2,448 inputs and
outputs were verified, source snapshots retained, and eight assemblies/symbols/XML
files installed with rollback copies. No new corner/surface/unlit shader candidate
was promoted. Public projected-material selection remains inactive.

Loaded stage identities were verified: Engine `e8247150-3553-4a7b-96d9-684a78471248`,
Gltf `c99cbed5-9108-4caa-a331-6b2a1f589bb2`, Tools
`01d79f9c-a5ad-4d9b-80f2-721dddb2c092`; Renderer remains
`c22b305b-109d-4166-a212-a7d4bc301e40`. Six public glTF/vmdl compatibility checks
and twelve animation-mixer checks passed inside the editor. The larger candidate
suite inventory is not a blanket execution pass.

The first new capture batch completed 18 cases, then editor PID33988 crashed with
SIGSEGV during SimpleInstancing/glTF. Kernel and coredump top frames are in
libmaterialsystem2.so; no OOM evidence was found. Its process and supervisor are
absent. One guarded recovery excluded instancing, corrected Duck's view, and
completed six safe cases. Current editor PID57616/start220370, guard57590,
persistent session88468, physical window `{f5e27dac-52bc-442d-af86-1753c76730b6}`;
last verified idle project Survive/VallArk, minimal.scene, no unsaved changes.
Revalidate before calls. No blind instancing retry or restart loop.

Current run `561cf9f4beef496dbeb4613a2e9c6fe2` has 22 actual captures. Twenty
are ready for user review; two BoxVertexColors captures render solid black and
are held as known issues. Another315 variants are blocked/unrun in this bounded
batch. Actual post-feedback capture receipts distinguish the first and recovery
executions without relabeling old pixels or changing any of the42 saved reviews.

The private website and new /review page were published from commit
`44d3b6291889ef773f2fce3ae4ab8c53afcd1f08` and browser-verified. Single current
receipts: `Temp/gltf-validation/latest-managed-validation.json`,
`current/capture-batch-receipt.json`, `simple-instancing-crash.json`, and
`comparison-site-publication.json`. Review queue: `CAPTURE_REVIEW_QUEUE.md`.
The instancing owner is investigating concurrent render-state/attribute access
using bounded core reads; no crash fix is established yet. Vertex-colour diagnostic
is prepared, unrun. Other remaining feature/native gates stay explicit.

## Historical corpus comparison run — superseded by the current batch

The current task is `./research/gltf-validation/Run`: attempt all 337 pinned
Khronos variants through public `Model.Load` and ordinary/SkinnedModelRenderer,
using default materials and the model-specific reference recipes. Actual images
and animation samples are comparisons for the user to judge; technical capture
success does not approve visual matching. Unit/spec assertions remain separate.

The authoritative current state is `Temp/gltf-validation/current/run.json`.
It contains every variant, actual load/render errors, applied recipe limitations,
current capture hashes and the producer identity. Website input is refreshed at
`Temp/gltf-validation/current/website`; there is no previous-result fallback.

The final current comparison is **interrupted**. Across the initial sweep and
one approved recovery, 200 variants completed: **83 captures** and **117 ordinary
load errors**. **137 variants are blocked**, including two observed native-crash
cases with no returned result and 135 unattempted cases. All 337 remain recorded.
The 83 captures cover 34 models: 62 images and 21 APNGs, containing 1,302 published
native image samples. Visual approval remains entirely pending user review.

Resume session **42113** ended1 following the second observed SIGSEGV at
MetalRoughSpheresNoTextures/glTF. Recovery launch **24355** ended245; native
**661160** / start **8198167**, supervisor **661128**, is absent. The guard
verified no editors or pending launches. No further restart was attempted.
Both crashes have the same top native stack offsets in libmaterialsystem2.so
and libengine2.so; the cause remains unproven, and kernel checks found no OOM
event. Subsequent memory PSI/swap measurements disallow new heavy admissions.

The approved recovery had restored saved lost.scene in the intended VallArk
project and verified the exact visible window, idle compiler and no Play before
dispatch. Completed rows retain their actual original producer identities;
orchestration changes do not relabel their pixels as a new rendering build.

Final canonical matrix SHA256:
`c507b897511c6b2110ce4dc04f6a2284df73d51637b038b959091349dbf703f2`.
Runner source SHA256:
`d4301d6427999b92e93db5da18c028e626c4b026bd2228036c9fed8a04122137`.
Fifteen bounded orchestration regressions and ten recipe inventory tests pass;
independent source review is clear. No returned-result or cleanup-success claim
is made for either crashed case. Unreferenced intermediate frames were removed.

The interrupted initial full-corpus session **76606** stopped after
59 complete variants: 33 captures and 26 ordinary model-load failures. During
BrainStem/glTF, native editor **496167** / start **7742489** crashed with
SIGSEGV; coredump metadata identifies thread 496220 in libmaterialsystem2.so
and libengine2.so. Kernel checks found no OOM event. BrainStem has a separate
transport/crash failure with no returned load result; 277 remaining variants
are blocked. Partial website export succeeded. Persistent launch **60072**
ended143, and the guard subsequently verified no editor/pending launch. No
replacement has been launched. Revalidate host state before further work.

The former exact physical KWin window
`{2cdd1a7c-c09b-4656-881d-14fd0e574abc}` was restored from minimization and
was verified visible before dispatch. Project was Survive/survive.sbproj with
lost.scene preserved. Never launch a replacement on transport timeout alone;
revalidate host process identities and run.json first.

Installed stage is `485badc0783e4ca2bdc4ea4c5307c0ac`, Engine MVID
`4181538e-896c-467c-966f-c8fcbf4f3282`, Gltf MVID
`df511f06-1c72-424c-a957-16a3fda208f6`, Renderer MVID
`c22b305b-109d-4166-a212-a7d4bc301e40`. The driver verifies loaded modules
against their disk identities before each case. No build or restart is pending.

The representative smoke captured Triangle, BoxTextured, AnimatedMorphCube
(82 actual frames / 4.1 seconds), and the corrected looping SimpleSkin
(129 actual frames / 14.32 seconds). UnlitTest returned its actual unsupported
required extension error. All cleanup completed and website export succeeded.
Nine runner orchestration tests and ten recipe inventory tests passed.

## Historical unit/spec execution notes

Revalidation-required snapshot after the explicit user restart approval (6 November
2026 local time). Do not treat saved handles as authority after a reset; inspect actual host,
window, project, MVID and job state before actions.

- User explicitly approved **End Play, restart and validate**. The old editor was
  freshly observed idle with lost.scene/no unsaved scene change, then exited
  independently before the comprehensive close audit could connect. No close/kill
  was performed by this worker. Host audit verified both old native 303768 and
  guard 303683 absent, no editor/compiler process and no pending launch.
- Historical **fee5** Gltf + Engine DLL/PDB pairs were installed with backups,
  recorded in stage `installation.json`. Renderer D remains unchanged. Previously verified
  loaded Engine MVID `1480297f-f517-46ce-b6a6-3689ef15f266`, Gltf
  `d2ca5caf-1c76-42c8-8756-77be9f1a556b`; Renderer D is
  `c22b305b-109d-4166-a212-a7d4bc301e40`. Named compiled shader and material
  loaded successfully, shader is non-anonymous.
- Core shader source `game/core/shaders/gltf_deform_cs.shader` SHA256 `43101e5f4c4ae9727f5a21c2aecc6e391bf16b303099395a703babb490006e47`
  was promoted only after no editors remained. Guarded one-worker SboxBuild
  compilation succeeded: one CS combo, compiled resource 34,575 bytes, SHA256
  `f15a8f245dd6d9f0293ee895c54a5f46a1b41eb094140153f1a5f7539178e5fc`.
  Native log and promotion receipt are in `fee5-install/`; no anonymous runtime
  compiler is needed by Model.Load.
- **CURRENT physical launch session31426**, supervisor449504/start3856554,
  native449542/start3856644, project Survive/survive.sbproj. The guard has verified
  the visible startup splash `{88e58a54-5d0e-4381-9a17-b45099f33bbe}`;
  final window `{9a636b39-7f3e-49b2-844a-92091a69d828}` is verified visible
  at1920x1034. lost.scene restored, editor idle/compiler success0errors. Do not
  replace this live editor on MCP timeout. Previous editor420266/guard420237 exited through
  normal Close and launch34030 ended0, after exact no-unsaved scenes/resources/
  assets and no-Play checks with lost.scene selected.
- **c5c0 stage installed and actual loaded MVIDs verified**: manifest
  `Temp/gltf-validation/staged-build/c5c0fe3da02d46efaca6decd71760d42/manifest.json`
  SHA256 `8582db935dd317ddb53f9152ba82af61954a8769e09ea17fee0290508a1931a5`.
  Gltf/Engine both built successfully with warnings-as-errors; all2015 input files
  verified and archived under source-snapshot. Selected DLL/PDB pairs installed
  with backups/rollback; RendererD and compiled named compute shader unchanged.
  Actual loaded Engine MVID2c054bba-c0fa-43b5-a3a2-39a34ff7577f,
  Gltfc869761c-5b80-462d-982a-bc3a688e57b6; both match actual loaded producers.
- Pre-fix fee5 feature79 terminal:16pass/63fail,0incomplete/cleanup.
  Cube reader25 MissingMethod reds, publicCube12 reds, instancingBackend12 explicit
  not-implemented reds, samplerGraph12=2legacyPass/10missingAttributeRed, legacy
  transform18=14pass/4tiny-scale rejects. Receipt
  `cf229f85cd7b0e48fc6edcd8ac8d45e01ecb56153247f90f907072701df81b75`.
  Cache rollback3 all real failures, receipt
  `09d6ac599e15dddea81b0ef1b13024c25f1a846f222fee8f44424d9a40cfe914`.
  Provenance5 is compile-only absent API evidence:0executed/5incomplete, receipt
  `7d218f3548ae8a15bf7bd7ff803fc79f04812d43793992829d8d9b778d8ef740`.
- fee5 corrected public25+handleControl3 all28PASS, receipt
  `6f10ea3ea583e955e581505019cfcf33fea3974d77238fb10ca70bc2c38e5fdf`.
  Async6/lifecycle10 all16PASS917974e1. Original SimpleSkin41 baseline
  1nativeVmdlPASS/40actualRED/0incomplete6185f212; immutable source archives retained.
- fee5 actual public Orientation capture passed1wrapper/13GPUrows/7fresh images,
  receipt `e4bbd94e73e154ce1ba31fe33f87700a7c7ae266cde6d34a6bffd394789261ff`.
  Same-material geometry/binding proof, not independent PBR fidelity. Frozen
  `public-captures/OrientationTest/latest-evidence.json` SHA256
  `2a42de714e17849ed63d5b65a940641081682dadf4a72f8eac31a1e42b9b151a`.
- c5c0 cohort114 TERMINAL:110pass/4fail/0incomplete/cleanup0, receipt
  `2a00ccf3fb6374ce901d4029c614fbaf45e509dbe06cd6013a7a9fd2a02f768d`.
  Skin53/57pass with four invalid-wrapper predicate failures only; reader25,
  publicCube12, instancingBackend12, cache3 and provenance5 allPASS. Fresh exact
  Skinned/bare-custom/native-vmdl controls then passed3/3, showing invalidstrong
  wrappers nonnull for bare/custom and validstrong native positive. Owner preparing
  narrow test predicate correction; no producer correction claimed from those reds.
- Actual publicCube source+compiled three-authored-key captures bothPASS, in same
  five-case control/capture run, receipt
  `66ac4a3006173a0b9359591738cad7e85a264071cadf0ef1593d43f037a80315`.
  Six positive stills, actual GPU output+indices, source-stock materials, named
  compute shader. Both forms coverage0/9778/7134/9778/0; changedpixels13590;
  maxP7.6294e-6in/maxN6.1439e-8. Captures visually inspected, clear morphshape
  changes. Sampling is not realtimecadence or independent final material parity.
  `public-model-smokes/AnimatedMorphCube/latest-evidence.json` SHA256
  `967dbd6ea9f9e8ecde4e1f6484f1dddd2c0e82e01b7824643a35cc67e9380b02`.
- Reviewed optional samplerAttribute patch66946129 applied once under guard to
  two watched addon files. Actual automatic compile success; first followup query
  transiently found no active sampler during type-library reload, then read-only
  observation verified activeMVID81834be5-03d7-4d82-9b89-fe189665a884/Attribute=true.
  Same12 generation plus4compatibility cases all16PASS, receipt
  `13fc2b7232e0ae062307790b1cd75ad32793a9863db7d4324ef823881ce2d3f8`.
- SamplerGPU/provided-generation2 TERMINAL1pass/1fail/0incomplete/cleanup0:
  receipt `454761de8fe6263661da49a84989385a14f6d991a98c279071a998b4732b5716`.
  GPU sampler test compiled,16 measure requests passed; request17 atmin9984,
  mag9728,lod-1 expectedRGBA1 but got(.3515625,.76953125,.3515625,1),
  maxerror.6484375. All17includingfailed retained; remaining85notexecuted.
  Settings owner triaging cause; no sampler rendering pass or tolerance waiver.
- Actual providedSurface/Lit graph generation passed55nodes, activeTools
  81834be5-03d7-4d82-9b89-fe189665a884. Manifest in
  `c5c0-source-orm-generation-423954c3c05743eabf7bb471fd7b495e/generation.json`
  SHA256 `026afe68daaba1dadd5f983e59170f5bf37de9c9e08c3ebef75f7be1b2735f84`;
  generatedsource d988509c2c958680cec9d1334bd16869e39e0bbcd38ed12a4021c9c08c586366.
  Exact source review pending before anonymous compile; no source template default
  promotion or provided material GPU appearance claimed. No heavy job live now.
- Corrected publicSkin57+same3controls all60PASS on unchangedc5c0, independent
  SourceORMresource4PASS, receipt
  `e9a3bc6ad826740b93c7a29696e6abcd9c323de967aa55840095728bc2f7579d`.
  TransferGPU1 failed before samples on exact native PS_INPUT undefined diagnostic;
  owner made a narrow COMMON-interface helper correction, separate frozen rerun.
- PublicSkin image2 tests BOTHsource/compiled PASS at reviewed1e-4in/1e-5normal,
  IoU1/RGB0: four actual poses/16totalcontrolcaptures, actual/ref motion4898pixels
  eachform; maxP3.8146973e-6in/N0. Receipt
  `2c7a40a3a203e78c1183045c57f314c9f61de408d7356e1209ffbcfb437dd5a5`;
  `public-model-smokes/SimpleSkin/latest-evidence.json` SHA256
  `8bff76c975abde3fe50f53d981d70e7af68ff8a49979979b2f5c064e328ccf97`.
  Shared source materials isolate geometry/binding; finalmaterial/cadence separate.
- ReviewedSurface template compilation succeeded but both default-texture lookup
  cases failed BaseColor validity before readbacks. Cube public18(sceneclock
  controls included) allPASS in same20run, receipt
  `98e4f877ab989a58d1579b2636c27a137883c6c074e003046a443f9442d459c0`.
- Two-loop publicCube cadence PASS in diagnostic25 run, receipt
  `165598de63db1add007c7ddedb19b5d4ecb7c95501302d5c117e68255ac20f7f`.
  848 observations/8.407102248s/2.001692authoredloops, maxgap.032611475s,
  maxphaseerror2.38485839e-7s within recorded float/double arithmetic bounds;
  finalPose/Submitted854. Native sourcecomponent uses Scene.EditorTick full
  monotonicgaps, no per-frame seek/helperplayer. Not visualFPS/geometryatalltimes.
  Cadence manifest3620384a is in public-model-smokes/AnimatedMorphCube.
  Same25run retained material/default-introspection/precision failures and actual
  tinyTRS19=7pass12fail. Future source tinyfallback9db4 is NOT installed.
- Full current pure regression816 +separate nativecodecs3 ALLPASS, receipt
  `e8f0cbc404fe8303b54d03a24ba5b4d69956680f5c9560ccffdc9906d83c43f2`.
  NEW isolated runner editor_gltf_current_tests.csx64e6c33a +version8manifest483e3856;
  all47sources/4fixtures/actualEngine&Gltf/request/runner rehashedend. Helperrefs
  BCL+Gltf+MSTest excludeEngine. Allinputsarchived c5c0/pure-current-source-snapshot.
  No codecblocks/incomplete/cleanup exceptions; context Unload invoked without
  exception, not a claim of proven GC collection. Historical764 untouched.
- Readiness-corrected sampler102 ACTUALALLPASS under original.04 pixel gate:
  receipt `c97866b304711f7c8da2f8e64b0ca37c2f588434251bf05fb90e6823a0e14d2c`.
  102sampling rows +15actualGPUviewreadiness records +1plan. No source sampler
  algorithm/globalpolicy changes. Initial17th-sample failure454761 and fresh
  scopedpreload diagnosticred165598 remain separate historical evidence.
- BoxTextured ALL6 public source/derived forms nowPASS: exact GPUpos/normal/UV0,
  sourceRGBA0differenttexels, IoU1/RGB0, Vflip5574changedpixels allforms. Both
  batches include checker8controls before capture. Current all6manifest
  public-model-smokes/BoxTextured/latest-evidence.json SHA256
  `62debf0d52775a3c6c5fe3c1129c0a6433cd569f394649494422cab1cce33860`,42PNGs.
  First2forms receipt46d4c955,remaining4c97866b3. Shared material reference is
  geometry/UV/binding proof, not independent final sampler/sRGB/PBR fidelity.
- Actual providedUnlit literal-color reference generated, no compile yet:
  c5c0-literal-unlit-generation-fd1edf2933ca4503b5b2b2fa480949ea/generation.json
  SHAfd5691d4; literalshaderbf7fb12b. Encoding/Unlit owners reviewed exactreference
  for actual source-emission pixel diagnostic, queued separately.
- Bounded Cube lighting comparison4/1/.25 x3keys PASS, receipt
  `df4e6ab96677f0c6100dc86059725d6a265a49332c1db5fa6cb65f1f4429bd91`.
  All camera/GPUbytes unchanged; point4 has0>=254whitepixels and99thpercentile
  start242/middle176 in rawactualPNG bytes. Original4selected, no materialchange
  or further search. Calibrationmanifest9b96f104 in public-model-smokes/Cube.
- PublicCube252 deterministic publicmovie CAPTURE/NUMERIC/ENCODE ALLPASS.
  Capture79acddca38896af76cecc65e3b099a02f80cfacad51d99c7cc05cb7e48aa5530;
  accepted artifact gate679297d0f082837e231f64ab5be74fa88dacfca424825ce2bbd0def617d3f4ff.
  Recording22cc980bc9bc40459e3448d38f1f1d20/public-movie-recording.json SHA12dc7c16;
  252sampledPNG+separateendpoint/506GPUbins all rehashed; root-approved recording
  limits1e-4in/1e-5normal/tangent gates1e-5; maxP3.8147e-5in/N3.8463e-7,
  all tangent errorcomponents0 (16defined+8explicitundefined). Capturewalltime
  24.4604702s is not source playback duration or anFPS claim.
  APNGexact252encodedframes duration4.199997424974802s vs authored4.199997425079346;
  maxcumulativeholdtimingerror1.04544e-10s; first1/60,last755/45307. No generated,
  interpolated/coalesced/dropped captured frames. APNG82aef7f4781b11ac3725f2535e2025e3f7ea830a956a209a2a63aa199d067509,
  526397bytes; public-apng/manifest.json SHAdeca30a39c332d96f1ce503e90ca7ea724d9a06f3e01707021a42022c5e4a26a.
  Fullpathroot public-model-smokes/AnimatedMorphCube/22cc980bc9bc40459e3448d38f1f1d20.
  Deterministic publicScene.EditorTick sampling; true monotonic cadence3620384a
  remains separate. Source-only movie; compiledmovie/materialfidelity separate.
- Site published by parent at c978 cutoff, subsequent publicmovie APNG followup
  is parent-owned; do not touch Site from validation worker.
- Material emission/precision attempt2 TERMINAL COMPILE-ONLY0executed/2incomplete:
  receipt6b34634c84da37c51f24754853b7183806b74e1cca76fcba06217c7ec325fd47.
  Precisionhelper missing Sandbox.Rendering.ResourceState import (4CS0103 at56/58),
  emissionhelper no compileerrors. Owner narrowfix requested, no runtimefailure or
  material acceptance inferred.
- Fresh host revalidation: native449542/start3856644 remains the exact visible
  KWin window9a636b39, lost.scene selected, idle/no unsaved scene/compile errors.
  Historical guard449504 is absent and native parent is1853; old launch31426 is
  not assumed live. Preserve this responsive editor; no replacement launch.
  Guard status reports one editor/no reservations/23.44GB used/PSI0.
- Corrected emission1+precision1 TERMINAL:0pass/2fail/0incomplete/cleanup0,
  receipt abd55378afb65e4263454cf010a286927bdfa04561ca91c31ba8fbc206b38712,
  output c5c0-source-emission-and-precision-fixed-2. All8 emission comparisons
  retained: six textured plain/gAMA/ICC source+portable exactRGB0/IoU1, two
  omitted-texture factor-only mean95.333/max188/IoU1 fail. Independent literal
  RGB reference and fresh empty/deletion controls retained. Precision8 numeric
  rows isolate hardware sRGB SampleLevel AND Load before color targets: blue
  .5859375 vs analytic .5840784178911641; rawlinear/manualEOTF/literal compute/
  float clear/literal raster controls retain precision. No tolerance waiver;
  actual installed c5c0 pins verified and owner reviewing actual failures.
- PublicFox1 plus analytic midpoint9 TERMINAL5pass/5fail/0incomplete/cleanup0,
  receipt9b840cc679eb99c3882bdd36fd4dc385c76a96226c8ffc5391ecee93728f0965,
  output c5c0-public-fox-and-midpoint-10/request97428c218d71fe0b. Public Model.Load
  returned but frozen helper failed its hardcoded24-bone/clip-list assertion
  before creating renderers. Owner checking authored node versus joint semantics;
  no rendered public Fox proof claimed. Analytic midpoint9:5pass/4fail atfixtures
  2,3,4,6, one-float-step differences with physicalangle<=3.202e-8rad. Explicit
  projectprecision target, not bitwise glTFconformance. Original Fox12 CPU reds
  retained. Awaiting targeted helper freeze then publiccapture.
- Existing Alpha native routing2 first attempt COMPILE-ONLY0executed/2incomplete,
  receiptf773118171a8c8313e6f77fb83737838dcc574130f284ef96d45e87b6e57cad9:
  wrapper used Scene in using although actual Scene is not IDisposable. Owner
  made explicit finally Destroy cleanup-only fix c3cdac7e. Corrected native2
  TERMINAL1pass/1fail/0incomplete/cleanup0, receipt
  ce272234dc5e25f16d77b38d2cade0677994131f8da9bc0e04ec8421ea64adeb,
  output c5c0-alpha-native-routing-fixed-2/request7ec62e01020a6531d. Native8
  callback/pass/sorting rows reported pass with24PNGs; two negative-camera
  positives equalempty (foreground0), so reverse-view visible proof remains
  unproven. Actual native material control flags distinguish opaque/blend.
  Backend material-routing8:3pass/5fail, actualSceneObject flags stayopaque for
  blend/mixed/clear/source and override changes. Primary identity and opaque
  restore controls pass. These16 nested observations are not16MSTest cases.
  Owner reviewing minimum routing fix; no producer edits by coordinator. No
  producer build before coherent changes are ready.
- Corrected publicFox source1 and same compiled-form1 BOTH TERMINALPASS,
  receiptscf39db8bcc6634443cca0396a741376295b30c3c282d5d6036f2ad5dcabe21e7
  andbcd4d52a12b60c535b3222455c6112418ae17c56e85e8b6a8968bd5867af3d91.
  Helper68bdd1e4 independently pins25 publicbone nodes[0,2..25]/names and records
  actualmetadata. Total18GPUrows/22PNGs, 12solo clip positives. Survey1348/
  Walk2411/Run2623changedpixels perform; pausedB byteisolation/deletion pass.
  Visually inspected Run start/middle, clearly different posedFox. MaxP.000954498in/
  N4.2252084e-7/UV0 remain measured-notaccepted; CPU8/20 andSLERP5/9 unchanged.
  public-model-smokes/Fox/latest-evidence.json contains bothforms, frozen22PNG
  hashes and exact producer/helper/compute identities. Currentc5 remains pre-fix.
  New source-only GltfAnimationSampler d8fc1127 applied by owner after root
  approval and actual9baseline, held for later combined stage with tinyTRS9db4.
  Neither source correction is installed; no precision9 or816 repeat now.
- Existing SimpleInstancing API/checker11 plus GPUprobe1 ALL12PASS, receipt
  a64b707726dd2bf7f6e9805aceee32a0ab87d72cff2c8b0c6a29064c2f2d2149,
  output c5c0-instancing-gpu-12. One GPU MSTest retained20measurements/all4forms,
  each125authored IDs/3000positions+normals/4500actualboundindices. Affine,
  mirrored, shared canonical inputs, B isolation and disposal passed. Original
  candidate bounds.0005in/2e-6 retain maxP.000108705775in/N1.786091874e-7;
  scoped rootreview accepted, not blanket numerical/PBR/hardwarebatching proof.
  Independent union-fit/norescale, native empty0/positive264/deleted0 controls;
  actual public loader remains separately pending. All8helper inputs archived.
- Combined next stage BUILD TERMINAL0 session52165 under guard4GiB; selectedGltf+
  Engine only. Stage485badc0783e4ca2bdc4ea4c5307c0ac manifest128a204a86864ce5,
  Gltf3.547s/Engine42.848s both0/warnings-as-errors. All2015inputs matched freeze
  and verified/copied to source-snapshot, alloutput hashes verified. Exactlyfive
  changed existing inputs vs immutablec5: Backend86ac3f,
  ModelRig9db4,StockAdapter642886,Sampler d8fc and root-approved loadera199189f
  EXT promotion after actual125GPU proof. No added/removed selectedsource paths.
  All2015 source/buildinputs frozen in next-material-stage-freeze.json SHA256
  b2f6733ca96038844a5055ecda797ea55303d68eb44585a5dc28c07379b8e050.
  Prebuildused24.33GB/PSI0, admitted4GiBprojection28.63GB<29GB.
  Fresh normal-close audit: noPlay/unsavedScenes0/resources0/assets0, lost.scene
  selected and oldc5loadedMVIDs verified. NormalwindowClose requested, then
  hostguard/status andPS verified449542/449504 absent/noeditors/pending0.
  Guarded install completed: selected4DLL/PDB files backedup/replaced/rehashed,
  stageinstallation.json; RendererD/compiledcompute unchanged. Expected new
  Engine4181538e-896c-467c-966f-c8fcbf4f3282/Gltfdf511f06-1c72-424c-a957-16a3fda208f6.
  CURRENT guarded physical launch session60072: supervisor496129,
  native496167/start7742489, finalvisible1920x1034 window
  {2cdd1a7c-c09b-4656-881d-14fd0e574abc}. Initialstartup tookresource/25shaderchecks;
  finalprojectVallArk/compilerSuccess0errors/notPlay/noUnsaved verified. lost.scene
  restored throughnormalopen_scene. ACTUAL newEngine/Gltf MVIDs matchabove,
  RendererDc22b unchanged/namedcomputevalidnonanonymous. Prior449542/31426 are
  historical; retain current60072 supervision, no replacement ontimeout.
  Freshdiscovery-stage485 SHA f882fe0d37d531a21752f8424938cece0820d47e47f6cb98cf0ef7bc05d85dae.
  Targeted92 TERMINAL91pass/1fail/0incomplete/cleanup0, receipt
  f7aa221b0116150b48bd3b6cff4424b3516e4550b7094ffdfb26147c5d2dd2ef.
  Unchanged tiny55/SLERP9/originalFox20/EnginepresenceKey4 ALLPASS. Alpha3/4pass:
  oldnativepass/routing/emptygreen; NEWmixedanimated/no-op test fails perpass draw
  counts2vs1 and partial aggregate completion. Full33measurementevents retained,
  owners reviewing actualcounter/frame/pass grouping; no blindrebuild/waiver.
  All11helperinputs archived. Stage485 FULL816+3codecs ALLPASS, nofailed/blocked/
  incomplete/cleanup, end47sources/4fixtures/producerpins verified. Receipt
  50b159b4506f92821d3e28afdcead877138ad7839a474f00f0bd621af939b00e;
  exactinputs archived stage485/pure-current-source-snapshot. UnitKey4 remains
  separatelyexecuted, not part of816. Requestfac9a6cfaaa20881 pinsnewDLLs.
  Actual active providedGraphCompiler afterrestart package.toolbase MVID
  d7a9163f-8542-45c9-b8d3-b453802fd8f5. Actual67node generationPASS abcbc172,
  emitted6648ccfa/source1f286 graph/manifest47fc2227 independently reviewed byroot
  andEncoding for four neutralfloat4presence branches; no defaultpromotion.
- InstrumentedAlpha4 ran3pass/1samemixedfail, then loggerIndex/index anonymous
  serialization aborted fifthliteralrow: receipt eb50de0f7d333f3a9807c41ce4b8fc345ce7dbf79b6ccc0a75a824f58e464de4,
  5planned/3pass1fail1incomplete/cleanup0. Exact originalhelper34bdee4e retained;
  read-only recovered-measurements.json SHA836c47854c425de75168cafb913428c7d86eb3b4875f499f86e227beec224da7
  contains34events/30callbacks/0dropped. Only colliding row/node index keys renamed,
  no test rerun. Actual selected-rowdelta1 inall30; globalcounter windows combine
  overlapping distinctrows. Root/owner reviewing counteroracle correction, no
  producerchange or assumption from active_callbacks alone. Originalred retained.
- Standalone freshliteral1 +actualNativePresence1 BOTH PASS e493fcdc57f219f5dd313142bf11b95bacb68a31ab8ffc767a7ce32e4880e4fa.
  Surface6648 anonymouscompile +12exactrole/cache/lease observations pass. Fresh
  literalmanifestf8afe8ba in stage485-literal-unlit-generation-7f30b1ca92904906ba18e7893e4e5b1c,
  actualToolsd7a; literal-sourcebyte-identical previouslyreviewedbf7fb12b. Old
  manifest81834 remainshistorical. Encoding preparing freshconfig/output for
  unchangedEmission698 actual8image gate; prior52ba/proof01 immutable.
  Noheavyjob currentlylive; editor496167/guard496129/launch60072 remaincurrent.
- Root requested a closure audit and **no new tests/probes/builds while auditing**.
  All confirmed jobs are terminal. Do not infer that this is a goal-completion or
  runtime-support claim. Current ready/unrun jobs require resumed scheduling:
  unchangedEmission698 with new stage485 config58c63a78/requestfd601c03;
  evidence-correctedAlpha4 probe809879/requestaf6e649e (per-selected-row counters,
  original GPU/no-op/empty gates, no producerchange); publicSimpleInstancing
  policy14/resource8 plus existing publicRenderer125 readback across4forms,
  requestacf00511. Installed loadera199 is present, publicgate unrun.
  SourceORM Presence12 passed but new emission8 runtime remains UNRUN; historical
  abd553 six-textured-pass/two-omitted-default-fail is still latest actual emission.
  Current publishedSite is an earlier receipt snapshot; latest publicFox22PNGs
  and instancing20 actualGPU results must not be described as absent backend
  implementation solely because the published status page lacks them.
- Closure audit is COMPLETE; root explicitly RELEASED executionhold for existing
  ready priority work. Current host revalidated496167/start7742489 visible/lost/
  idle/noUnsaved/compilerSuccess0, guardused21.81GB/PSI0/noreservations.
  Existing unchangedEmission698 eight comparisons +correctedAlpha809879 four
  tests are now admitted as5actualMSTests (16frozenhelpers), requestaRuntime
  selection recorded in stage485-emission-and-alpha-final-5. No new test/media
  expansion or producerbuild; publicinstancing22+4forms follows this terminaljob.
  NewAlpha4/tiny55/Slerp9+Fox20 and neutralrole generation/pixels queue after new
  actualproducer; full816 repeat justified only after changedGltf installed.
- Fresh discovery-c5c0 SHA256
  `d98176b46afbc917dde04c8656fe8cc2a51bf6228e69a790a67194234336e192`.
- Runner now supports the optional bounded `snapshot_path` only for the same
  original allowed test/helper scope, stage GUID and matching archive manifest;
  checks archived inputs at end and rejects helper types shadowing loaded Engine
  or Gltf. Snapshot selection changes are not claimed executed until first run.

Latest terminal evidence (historical producers are explicitly distinguished):

- Combined actual producer stage `fee5f79b9c384156bde089e96666d306` compiled
  Gltf and Engine successfully (3.925 / 41.528 seconds), manifest SHA256
  `ca60cf66d80dcb31bdfee063b37e220bd1d83e164fe43820180f09002383de14`.
  All 2,008 source inputs were hash-verified/copied; 33 upcoming test/helper
  sources separately archived with their original scopes. Newer working-tree
  implementation is not this installed generation.
- Original native-backend architecture baseline 32 executed: seven pass, 25 fail,
  no incomplete/compile/cleanup errors; receipt `ec3f3a6d72146904201a6e5b139702cdcb4a1f3ceaecfeb2b33d3a88200bebe7`.
  Public25 had seven metadata/vmdl passes and 18 actual native-model architecture
  reds; Prepare five plus empty metadata two were actual missing-API reds.
- Material CPU 186 executed: 180 pass, six actual failures (four required-unlit
  default adapter, two missing variant API). Box50 and TextureTransform28 passed.
  Receipt `0da83949e97504dcc493158bf7f912d928b288827ebf3b5f8653ff35c74d59d2`.
- Animation/layout/Draco161 executed: 151 pass, ten old unsigned research-clock
  failures. Interpolation40+9, Cesium23+7+2, Rigged12+8, Orientation40 all passed.
  Receipt `cee5955d7dff542675725caf59e467c8bb14a32ac6a708f98a3a254eb2ac6ef1`.
- Core159 executed: 149 pass, ten failures. PrimitiveModes84 passed; Meshopt
  index/fallback and weak-control failures sent owner; one invalid local-shear
  instancing fixture corrected without producer/tolerance change. Receipt
  `495cd936b999645b57398cc36669aa5790c24edfe1d9617c50d62ee606d40240`.
- Corrected instancing40 and texture-serialization28 passed in batch69; standalone
  GPU transfer test failed shader parsing before any samples. Receipt
  `ca0c3292debc880375790d87e546d9a1c9a6999cd9ad2584d21960bd9e0612db`.
  Narrow grammar fix remains prepared/unrun; no texture-transfer pass claimed.
- Native light packing six observations plus cleanup assertion retained in
  `f7cc4844ec96d8c1da6d9ba94208cd8157705f663253e2204cca528e2227d9ad`.
  Read-only shader/asset-type metadata `b00c37351ba13b8c1c2b37d512b0b6d0c588d51172cb9baff3c1cfee4dd56f6e`.
- TextureSettings21 passed; 12 sampler generation rows hit helper TypeLibrary
  reflection ambiguity before feature evaluation, receipt
  `7a21a72f339b1f20e16732200b986e909039b2a58dc25d87f0e72082e09ba765`.
  Corrected12 attempt stopped before dispatch when user entered Play. Actual
  missing-Sampler.Attribute red baseline remains pending; no addon edits approved
  solely by these context errors.

- On loaded D, model-local CPU/data assertions passed 74/74: SimpleSkin 24,
  InterpolationTest 40, RiggedSimple 10. Receipt SHA256
  `1a7570f0167fa928fe7927b959f9bb8b2c2a15a0d36d8126d45697c656f98e94`.
- OrientationTest passed 30/30, CesiumMan 21/23. Both Cesium failures were traced
  to an incorrect shared oracle: Embedded has 46 keys ending at 1.91666698,
  while the canonical source has 48 ending at 2.0. Per-variant oracle correction
  is queued; original failed receipt is retained as
  `cafe8f5212a59e56badad42e625518aa447ae236f779cf193078e1f9b5b8862c`.
- On actual new Gltf, reader/player/morph assertions passed 35/36. The failed
  reader fixture counted eager constructor source I/O as upload I/O. Corrected
  fixture preserves exact owned-byte budget checks and no additional upload I/O;
  the rerun passed all 23 reader + 10 player + 3 morph tests, plus five backend
  CPU groups (41/41), receipt SHA256
  `c422cb799669645e319ae7def6b145d43fef738fe8ddf629f88614201f7535de`.
  Original failed receipt SHA256
  `f9cac878ee528318850557f45382799024473f8305fdbd71e7cd50a481e24762`.
- Shared GPU backend/Rig/raw checker compiled against actual new Gltf and Engine
  D; five CPU contract assertion groups passed. Receipt SHA256
  `62b2bad4f6fa74a2c0f478fcedee5e7d2d9aa3dd392ee0193885381eaabd36d4`.
- First actual raw GPU triangle gate passed with anonymous shader SHA256
  `43101e5f4c4ae9727f5a21c2aecc6e391bf16b303099395a703babb490006e47`,
  receipt SHA256
  `884bb3276c4fd27a8c47ba4597848e284d23248d4f452ebfa6ec3cb7b2d93a01`.
  Same-run fresh empty/reference/deleted/actual/deleted coverage was
  0/2775/0/2775/0. IoU was 1, RGB errors zero; actual GPU position, normal,
  tangent and UV errors were zero, normalized color error 5.96e-8. Sixteen raw
  draws submitted current revision without callback errors, and borrowed compute
  material ownership survived asset disposal. No native Model/Mesh was created.
  This proves one Linux diagnostic triangle, not sample-model/API/material,
  wall-clock or performance parity. Initial inline-MODES shader failure remains
  retained (`432370b3eac0b81c1f4dc5c002f4494a6ab99ab30b04069efb69737635074686`).
- Actual bound-index readback/corruption control completed and passed, receipt
  `920299bdcd1fce5f2d4329aae560c6fc31ce0340f943578f3299d3c0226a5850`,
  output `raw-triangle-index-negative-frame-type`. Only the owned bound GPU
  index bytes were reversed while CPU submission metadata and pose stayed
  unchanged: actual index readback returned `[0,2,1]` with origin `gpu-readback`,
  the independent checker rejected it and single-sided coverage became zero.
  Restoring GPU bytes restored the positive draw and checker pass. Old handle
  11064 is terminal. The preceding compile-only frame-count type error is retained.
- Three actual direct GPU model smoke MSTests now passed in
  `three-model-smokes-source-fitted`, receipt SHA256
  `79e57fbdfd955db7333b4294601beb8af7921cbe81a5cdfa404e8445c06c96e6`.
  The complete run was 26 cases: 14 passed, 12 failed, zero incomplete. The
  12 failures remain Fox's original strict CPU clip numeric gates; corrected
  independent exact-key/clamp controls all three passed. No tolerance was raised.
  SimpleSkin bind/key images match independent references exactly (IoU 1, RGB
  errors zero) and show actual pose changes. Fox's correctly fitted fixed camera,
  owned directional light, all three clips and independent instance/disposal
  controls render; source and diagnostic material are compared on identical
  camera/pose/buffers. Cube renders three authored keys with morph/tangent/index
  readbacks and changed pixels. Numeric errors are measured, not silently
  accepted as final tolerances. These are research helper smokes, not public
  Model.Load, real-time playback, smooth movie, Windows or performance parity.
- Cube's exact rest tangent degeneracy was independently diagnosed and marked
  only where authored direction is undefined. Defined directions, handedness,
  finite/unit/orthogonal fallback and geometry gates remain strict. Corrected
  oracle author controls passed 5/5; nine model CPU and eight checker sensitivity
  cases passed. Its generated reference SHA256 is
  `d18dd9c6f5acf94b5c29001c6501bf8d5249fcc52214a91b3e0283ee2bcd65b0`.
- Fractional-timing APNG media codec tests passed 8/8. No new movie or website
  capture publication has occurred; actual 252-frame and wall-clock proof remain.
- Next: finish current fee5 missing-feature baselines, then build/install a coherent
  next Engine/Gltf slice and validate actual public Skinned57 and GPU output.
  Remaining model submissions stay queued, including BoxTextured,
  TextureTransformTest, UnlitTest, MaterialsVariantsShoe, RiggedSimple/CesiumMan
  Draco, InterpolationTest, MeshoptCubeTest, MeshPrimitiveModes,
  PrimitiveModeNormalsTest and SimpleInstancing.

Exact replacement command (working directory `/home/damian/projects/sbox-public/game`):

```sh
/home/damian/.local/bin/codex-resource-guard launch --project /home/damian/projects/Survive/survive.sbproj -- /home/damian/projects/sbox-public/game/sbox-dev -mcp -project /home/damian/projects/Survive/survive.sbproj
```

This preserves the inherited physical graphical session; no virtual display,
new isolation wrapper or environment override was introduced. The splash window
first passed the guard, then the final native editor window and actual project,
compiler state and producer MVIDs were checked separately.

## Model assertion runner

`editor_direct_gpu_tests.csx` accepts a frozen request at
`Temp/gltf-validation/direct-gpu/current-tests.json`. Version 1 explicitly lists
loaded Engine/Gltf/framework hashes, model-local helper source hashes and suite
types/counts. The runner compiles only research helpers against the actual loaded
producers; it rejects replacement producer namespaces. It supports primitive
MSTest DataRows or explicit static result arrays. Optional public static setup
methods/properties receive declared string arguments (`$checkout` expands to the
current checkout). All declared case totals are counted before compilation, so
compile/setup aborts remain incomplete rather than appearing to execute zero
planned cases. A transport success alone is not a test pass; inspect its receipt.

The older 764-case pure suite retains its own frozen manifest. Its inventory
check currently rejects the newly added MorphUpload test source. New model probe
discovery uses the existing audited `CliSession.discover` function independently
of that pure-suite inventory. Do not silently change the historical manifest to
make discovery pass; independently recount/hash the new suite for a new producer.

## Execution order

1. Recover the established launch environment and start one guarded physical
   editor. Verify actual project, visible window, idle compiler and loaded MVIDs.
2. Establish empty, literal native triangle and post-deletion controls using the
   previously accepted Normal debug mode, normal postprocessing, owned light and
   exact camera target size. Retain failed Albedo routes as failures.
3. Execute the Fox owner's frozen shared C# GPU-buffer triangle tests. Require
   raw vertex/index binding and runtime vertex declaration without constructing
   a native Model or Mesh.
4. Run each model owner's frozen focused assertions and direct GPU captures:
   AnimatedMorphCube, Fox, OrientationTest, SimpleSkin, RiggedSimple, CesiumMan
   and InterpolationTest. BoxTextured and TextureTransformTest are additional
   dedicated model owners preparing source/tests; their matrices follow the
   first raw GPU gate. Evaluate independent references and independent
   instance state; do not treat recognized metadata as rendered parity.
5. Produce high-cadence actual capture media for animated samples, record source
   times and exact playback durations, and separately verify wall-clock playback.
   Deterministic seeks are not real-time performance evidence.

## Submission contract

Send the validation worker the model name, owned source paths and hashes,
required shared backend API/version, callable test/probe entry point, expected
case count and acceptance assertions. List any build/stage/restart requirement.
Do not edit a submitted freeze while its execution is active. Execution receipts
record producer MVIDs, source hashes, actual assertion results, images and limits.

The direct GPU renderer must own its C# GPU buffers and derive input descriptors
from glTF accessors and bufferViews. Native scene presentation, stock shaders,
materials and draw submission services are allowed. Native Model/Mesh-based
rendering is historical evidence and is not a passing substitute.
