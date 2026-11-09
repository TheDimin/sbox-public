# Authored gray/white feedback, 7 November 2026

Current user rejection `12592799b9c4aae8b7bbd8adb9e488d98e3df0cce2e66a8a66591bdd75d708c6`:
“the arrow background is just white, while in the original image it has a gray/white background diference going on.”
The historical wrong-arrow/all-yellow rejection remains preserved separately.

The seven actual inputs match the corpus manifest and pinned checkout
`90d7ede14c7e280af263824604b427a1ca02cb66`. `Arrow.png` is fully opaque:
all alpha values are 255. Every outside edge texel is `(224,224,224,255)`;
the unmarked interior is `(255,255,255,255)`. The model README explicitly
documents a gray border, clamp sampling and disabled mipmapping. This is
authored opaque border color exposed when the transformed UV leaves the unit
square, rather than a checkerboard transparency backdrop. All nine material
base-color factors default to one, metallic is authored zero and roughness
defaults to one; there are no authored alpha modes or cameras.

The actual rejected coherent9b9f image `f4977fb2663c9474417372a46ccfb0e2eb8c9e478f23b0762716a9ecd7e1f325`
was checked with `../../../../../engine/Tools/Gltf/direct-gpu/models/TextureTransformTest/audit_background.py`. Independent scalar transformed-UV
landmarks, excluding marker rectangles, filter boundaries and arrow pixels,
report median border and white-interior RGB of 255 on all three arrow quads.
Their contrast is exactly zero. This confirms the reported loss without
changing or recoloring any source bytes. Lighting/exposure saturation is the
current hypothesis; a new native light sweep is required to establish it.

The existing 92/92 coherent9b9f source/compiled CPU and native tests retain
their recorded identity. They already separate UV transforms, wrong signs,
origin/order, raw overrides, source image serialization, sampler wrapping,
stock material/depth controls and cleanup. They do not prove that gray/white
contrast survives the canonical lit image. Source-versus-compiled agreement
or another shared-material image comparison cannot settle that question.

The prepared native control runner uses actual public `Model.Load` and the
unchanged default material route. The legacy intensity-2 control preserves its
camera-only exposure request. The shared runner now reports that this request
is unverified because editor ticks reset camera settings. A second intensity-2
control opts into the stock Linear Tonemapping component with exposure fixed
at one and compensation zero; observed post-render settings must be retained.
The next controls vary only intensity (1, 0.5) with that same explicit effect,
camera and source bytes. A separately labeled diagnostic fixture compares original
clamped gray and white Arrow.png texels with independent linear uniform
factors obtained by sRGB decoding 224 and 255. It retains the original PNG and
geometry-buffer bytes and uses the default material route at intensity 0.5.
Texture/factor agreement and visible gray/white separation are separate checks.
After inspection, a separate final run can capture source and exactly one canonical
compiled GLB at the measured intensity, retaining exact original relative PNG
dependencies alongside the GLB. The compiled producer checks
the five encoded images against the original hashes before public rendering.
All jobs require a fresh integration lease, project/status/producer check,
shared native lock and host guard. No new editor is launched by this worker.

The source preview's primary quads occupy approximately 85 of 300 horizontal
pixels; the current camera corresponds to approximately 75 of 300. An optional
final orthographic height of 2.6 metres (previously 2.9333) preserves the same
front view and target while approximating that framing. Camera and illumination
remain inferred because the original renderer setup is undocumented.

Frozen source audit and pending request:
`Temp/gltf-validation/direct-gpu/texture-background-audit/`.
The five native controls subsequently completed on the current producer. Both
legacy and explicit-settings intensity-2 controls clip gray and white to 255.
Intensity one yields gray 204 / white 232 on all three panels, and intensity
0.5 yields gray 149 / white 170. The separately prepared factor grid matches
both original texels exactly (zero channel error). This identifies excessive
recipe illumination as the cause; retained exposure settings alone do not
establish a pixel-level exposure effect.

The first final helper failed to compile (`CS0117`): the installed compiler has
no `CompileWithEmbeddedImages`, although that method exists in the working
source. No GLB or capture was produced. Its transport failure remains in
`Temp/gltf-validation/direct-gpu/texture-background-canonical/`.
The corrected helper uses the installed public `Compile` API and preserves
authored PNG dependencies. The final source and single canonical compiled
captures completed at intensity 1.25 / orthographic height 2.6m. Both public
managed submissions and cleanup passed. Independent transformed-UV landmarks
measure gray 225 / white 255 (contrast 30) on every arrow panel, and all three
arrow-marker direction checks pass in both forms. The actual PNGs are identical:
`bfe892260eea8c668570d24909136a2f8fc0c6a554dffb88d8ef4ca9b13e678c`.
The native source image was visually inspected; intended gray/white shapes are
restored and the framing is closer to the pinned preview. This is a presentation
recipe correction; no source material, texture, sampler or engine change was
needed. Whole-image matching and user approval remain pending.

Final receipt package:
`Temp/gltf-validation/direct-gpu/texture-background-canonical-retry/` contains
`summary.json`, `independent-checks.json`, source and compiled native result
records, exact tested recipe requests, the compiled package and compilation
provenance. The stable owner result and publisher handoff are
`Temp/gltf-validation/direct-gpu/texture-background-audit/TextureTransformTest-feedback-result.json`.
The publisher must preserve both rejected fingerprints and show the changed
capture as new pending review, rather than transferring an old decision.

Source model/image/buffer assets remain CC0-1.0. Their metadata and LICENSE.md
remain CC-BY-4.0, as recorded in the pinned model license. No corpus bytes have
been modified or copied into the engine source payload.
