# Capture review queue

Generated from the terminal current run, its matching website export, and your saved feedback.

**Fresh means a verified native recapture, not a confirmed fix. Only you judge visual correctness.**

Local current export; site links show these results only after the owner publishes them.

Saved reviews are unchanged. An approval still applies only to its exact comparison fingerprint. Old alternate formats are not later fixes. Browser decisions made after the saved feedback export are not included.

| Category | Cases |
| --- | ---: |
| Fresh captures — ready for your review | 0 |
| Fresh captures — held for known issues | 0 |
| Awaiting verified regeneration | 6 |
| Current load or capture failures | 0 |
| Blocked or not run | 315 |
| Existing captures — not a new fix | 6 |
| Your approval still matches the current comparison | 10 |

## Fresh captures — ready for your review

None.

## Fresh captures — held for known issues

None.

## Awaiting verified regeneration

### BoxVertexColors

Inspect the per-face vertex colors and sharp cube edges; color must come through the COLOR_0 path.

Limit: A uniformly colored material is not evidence that vertex colors are being consumed.

Your saved feedback: Vertex colors not loaded

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxVertexColors&variant=BoxVertexColors%2FglTF) — still image
  - No post-feedback native capture batch receipt is recorded.
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxVertexColors&variant=BoxVertexColors%2FglTF-Binary) — still image
  - No post-feedback native capture batch receipt is recorded.
### MetalRoughSpheres

Compare the texture-driven metallic/roughness grid: highlight width and metal/dielectric response should follow its rows and columns.

Limit: A visible grid is not a material pass; keep light/environment/exposure fixed to diagnose roughness and metallic behavior.

Your saved feedback: Camera is not located at the middel, hdri seems to be missing

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MetalRoughSpheres&variant=MetalRoughSpheres%2FglTF) — still image
  - No post-feedback native capture batch receipt is recorded.
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MetalRoughSpheres&variant=MetalRoughSpheres%2FglTF-Binary) — still image
  - No post-feedback native capture batch receipt is recorded.
### RiggedFigure

Watch the figure's joint deformation and overall articulated motion through the clip.

Limit: A rest pose cannot establish the animation or skinning behavior.

Your saved feedback: Speed does not match

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RiggedFigure&variant=RiggedFigure%2FglTF) — sampled animation
  - No post-feedback native capture batch receipt is recorded.
### TextureTransformTest

Inspect all six base-color quads: arrows should hit green markers, not yellow (unapplied) or red (opposite rotation), with the intended offsets and scales.

Limit: Correct UVs without the extension transform are not enough; compare every quad.

Your saved feedback: arrow does not point at the correct elements, and all 3 show yellow instead of the u,v,uv transform being done correctly.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureTransformTest&variant=TextureTransformTest%2FglTF) — still image
  - No post-feedback native capture batch receipt is recorded.

## Current load or capture failures

None.

## Blocked or not run

### ABeautifulGame

Check the chess-piece silhouettes, transparent pieces and thickness-dependent tint; look through overlapping pieces at the board.

Limit: One board view cannot establish refraction, volume attenuation or every viewing angle; use controlled lighting and backgrounds.

Your saved feedback: Sandbox.Gltf.GltfException: The stock glTF model material adapter does not yet support normal, occlusion or emissive textures. Please add support for this !

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ABeautifulGame&variant=ABeautifulGame%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ABeautifulGame&variant=ABeautifulGame%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary-KTX-ETC1S-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ABeautifulGame&variant=ABeautifulGame%2FglTF-Binary-KTX-ETC1S-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AlphaBlendModeTest

Check OPAQUE ignores texture alpha, BLEND reveals the checks, and MASK follows its 0.25/0.5/0.75 cutoffs.

Limit: The README permits thin masked borders disappearing with mipmapping. One view still does not verify overlap/sorting at every angle.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AlphaBlendModeTest&variant=AlphaBlendModeTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AlphaBlendModeTest&variant=AlphaBlendModeTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AnimatedColorsCube

Watch the cube's base colors change through the clip as well as its transforms; frozen colors indicate a missing animation target.

Limit: A moving cube alone does not verify KHR_animation_pointer material-color animation.

Your saved feedback: no interperlation of colour, location or rotation is happening.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimatedColorsCube&variant=AnimatedColorsCube%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimatedColorsCube&variant=AnimatedColorsCube%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AnimatedMorphCube

Watch both thin and angle morph targets change the cube's shape; faces should remain connected and shading should follow deformation.

Limit: A rotating or translated rigid cube does not demonstrate morph weights; one sampled loop is not a real-time performance test.

Your saved feedback: Flip 180degree, we had similar issue with duck being rotated before we steered it, keep this in mind for any model we render.

- [ ] [glTF-Quantized](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimatedMorphCube&variant=AnimatedMorphCube%2FglTF-Quantized) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AnimationPointerUVs

Watch texture patterns move with animated UV transforms on each material input, not merely object movement.

Limit: One static texture placement cannot establish animation-pointer support across the listed material channels.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimationPointerUVs&variant=AnimationPointerUVs%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimationPointerUVs&variant=AnimationPointerUVs%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AnisotropyBarnLamp

Inspect directional highlights on the lamp, the clearcoat layer and transmitted light through its transparent parts.

Limit: A recognizable lamp does not establish anisotropy, emissive strength and volume behavior independently; isolate them under controlled lights.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyBarnLamp&variant=AnisotropyBarnLamp%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyBarnLamp&variant=AnisotropyBarnLamp%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-KTX-BasisU](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyBarnLamp&variant=AnisotropyBarnLamp%2FglTF-KTX-BasisU) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AnisotropyDiscTest

Inspect highlight direction around the textured disc; check the directional pattern against the reference.

Limit: A circular highlight or visible texture alone does not establish the anisotropy texture's direction and strength channels.

Your saved feedback: Texture is not loaded, so i am unable to compare material properties

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyDiscTest&variant=AnisotropyDiscTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyDiscTest&variant=AnisotropyDiscTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AnisotropyRotationTest

Compare highlight orientations between the rotation cases, keeping camera and light fixed.

Limit: Changing the light can mimic a changed anisotropy rotation; a controlled common setup is needed.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyRotationTest&variant=AnisotropyRotationTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyRotationTest&variant=AnisotropyRotationTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AnisotropyStrengthTest

Compare the highlight shape as anisotropy strength changes across the samples.

Limit: Overall brightness differences alone cannot distinguish anisotropy strength from lighting or roughness.

Your saved feedback: matching light, and camera angle would be preferred, but this does look correct material property wise.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyStrengthTest&variant=AnisotropyStrengthTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnisotropyStrengthTest&variant=AnisotropyStrengthTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AntiqueCamera

Check the camera body, lens and tripod remain complete, with their surface textures on the correct parts.

Limit: This object view is not a calibrated lighting test and does not exercise all material or texture-filter settings.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AntiqueCamera&variant=AntiqueCamera%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AntiqueCamera&variant=AntiqueCamera%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### AttenuationTest

Compare tint through different thicknesses and scales; thin and thick transmission paths should not collapse to one uniform appearance.

Limit: Background visibility alone does not verify attenuation distance, thickness or scale conversion.

Your saved feedback: sbox scene brightness is to high, makes the scene unreadable.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AttenuationTest&variant=AttenuationTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AttenuationTest&variant=AttenuationTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Avocado

Inspect the avocado's pit, flesh and skin boundaries and the placement of its painted texture.

Limit: One attractive render does not establish texture color-space handling, mip selection or sampler behavior.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Avocado&variant=Avocado%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Avocado&variant=Avocado%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Avocado&variant=Avocado%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Quantized](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Avocado&variant=Avocado%2FglTF-Quantized) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BarramundiFish

Inspect the fish's fins, body silhouette and textured scales for missing geometry or texture seams.

Limit: A single view does not demonstrate all normals, tangent-space details or texture filtering.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BarramundiFish&variant=BarramundiFish%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BarramundiFish&variant=BarramundiFish%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BarramundiFish&variant=BarramundiFish%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BoomBox

Check the radio's body and speaker textures, and distinguish its glowing front panel from externally lit surfaces.

Limit: A bright panel under a bright light does not isolate emissive behavior; repeat with controlled external lighting.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoomBox&variant=BoomBox%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoomBox&variant=BoomBox%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoomBox&variant=BoomBox%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BoomBoxWithAxes

Check the radio and labeled X/Y/Z tripod together; axis orientation must agree with the object's placement.

Limit: A correct-looking radio without the axis tripod does not verify coordinate orientation.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoomBoxWithAxes&variant=BoomBoxWithAxes%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Box With Spaces

Check that the box and all its material textures load despite spaces and encoded spaces in their resource names.

Limit: A fallback-colored cube can hide failed texture URI resolution; inspect the expected texture content too.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Box+With+Spaces&variant=Box+With+Spaces%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Box

Check the cube silhouette and all visible faces, the red non-metallic material, sharp face boundaries and sensible face-to-face shading from its normals.

Limit: A cube existing is only a geometry smoke check. Exact screenshot brightness, shadows and background are not a gate without matched lighting/exposure; ambiguous shading needs a controlled engine-light check.

- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Box&variant=Box%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Box&variant=Box%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Box&variant=Box%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BoxAnimated

Watch the smaller box rise and rotate relative to the larger box; both translation and rotation channels must be visible.

Limit: One pose or one channel moving does not verify the two-channel animation or its timing.

Your saved feedback: Looks correct, colours are a bit off, but this could be lighting in-engine

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxAnimated&variant=BoxAnimated%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxAnimated&variant=BoxAnimated%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxAnimated&variant=BoxAnimated%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BoxInterleaved

Check the cube shape and flat face shading stay intact when positions and normals share an interleaved buffer layout.

Limit: A surviving silhouette can hide a wrong normal stride; compare face lighting under a controlled light.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxInterleaved&variant=BoxInterleaved%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxInterleaved&variant=BoxInterleaved%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxInterleaved&variant=BoxInterleaved%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BoxTextured

Check the Cesium logo's orientation, scale and placement on the cube faces; confirm the texture is actually present.

Limit: A colored cube alone does not verify UVs, texture decoding or orientation; brightness is affected by lighting and exposure.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxTextured&variant=BoxTextured%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxTextured&variant=BoxTextured%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxTextured&variant=BoxTextured%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BoxTexturedNonPowerOfTwo

Check the logo remains correctly scaled and mapped with the non-power-of-two texture dimensions.

Limit: A near view does not establish minification, mipmap selection or all sampler modes for NPOT images.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxTexturedNonPowerOfTwo&variant=BoxTexturedNonPowerOfTwo%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxTexturedNonPowerOfTwo&variant=BoxTexturedNonPowerOfTwo%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxTexturedNonPowerOfTwo&variant=BoxTexturedNonPowerOfTwo%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BoxVertexColors

Inspect the per-face vertex colors and sharp cube edges; color must come through the COLOR_0 path.

Limit: A uniformly colored material is not evidence that vertex colors are being consumed.

Your saved feedback: Vertex colors not loaded

- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BoxVertexColors&variant=BoxVertexColors%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### BrainStem

Inspect the articulated robot's limbs and hierarchy throughout its animation, looking for detached parts or incorrect joint motion.

Limit: A rest pose cannot establish skinning or animation; a blocked/crashed attempt has no visual result to approve.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BrainStem&variant=BrainStem%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BrainStem&variant=BrainStem%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BrainStem&variant=BrainStem%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BrainStem&variant=BrainStem%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Meshopt](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BrainStem&variant=BrainStem%2FglTF-Meshopt) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Meshopt-EXT](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=BrainStem&variant=BrainStem%2FglTF-Meshopt-EXT) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Cameras

Inspect the authored perspective and orthographic views separately, checking framing and perspective differences.

Limit: A capture from our review camera does not demonstrate that both glTF camera objects were imported and selected correctly.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Cameras&variant=Cameras%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Cameras&variant=Cameras%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CarConcept

Inspect paint, clearcoat, glass and material variants on the car; check that variant changes preserve the geometry.

Limit: One selected appearance cannot establish every material variant or its transmission and iridescence behavior.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CarConcept&variant=CarConcept%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CarConcept&variant=CarConcept%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-JPG](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CarConcept&variant=CarConcept%2FglTF-JPG) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-KTX-BasisU-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CarConcept&variant=CarConcept%2FglTF-KTX-BasisU-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-WEBP](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CarConcept&variant=CarConcept%2FglTF-WEBP) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CarbonFibre

Inspect the carbon-fibre weave and the direction of its elongated highlights around the ball.

Limit: A visible weave texture alone does not establish anisotropic shading; rotate a controlled light/view to separate the effects.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CarbonFibre&variant=CarbonFibre%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CarbonFibre&variant=CarbonFibre%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CesiumMan

Watch the character deform at its joints while the texture follows the skin; inspect limbs through the clip.

Limit: A textured rest pose cannot establish skeletal deformation or animation interpolation.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CesiumMan&variant=CesiumMan%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CesiumMan&variant=CesiumMan%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CesiumMan&variant=CesiumMan%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CesiumMan&variant=CesiumMan%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CesiumMilkTruck

Check the truck's body and wheel nodes, texture placement and animated parts together.

Limit: One view cannot establish every node transform or the full animation; separate rigid-node motion from skinning.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CesiumMilkTruck&variant=CesiumMilkTruck%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CesiumMilkTruck&variant=CesiumMilkTruck%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CesiumMilkTruck&variant=CesiumMilkTruck%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CesiumMilkTruck&variant=CesiumMilkTruck%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### ChairDamaskPurplegold

Inspect the purple/gold upholstery pattern and grazing-angle sheen on the seat.

Limit: A fabric-colored chair does not isolate sheen from diffuse color or lighting; use controlled grazing views.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ChairDamaskPurplegold&variant=ChairDamaskPurplegold%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ChairDamaskPurplegold&variant=ChairDamaskPurplegold%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### ChronographWatch

Inspect the watch face, hands, case and distinct material variants, including small texture details.

Limit: One watch appearance does not establish all variants, fine-detail filtering or exact specular lighting.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ChronographWatch&variant=ChronographWatch%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ChronographWatch&variant=ChronographWatch%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-KTX-BasisU](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ChronographWatch&variant=ChronographWatch%2FglTF-KTX-BasisU) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-WEBP](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ChronographWatch&variant=ChronographWatch%2FglTF-WEBP) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### ClearCoatCarPaint

Look for a clearcoat highlight layered over the base car-paint material rather than replacing it.

Limit: A single glossy highlight cannot isolate the coat layer; compare controlled light/view changes.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ClearCoatCarPaint&variant=ClearCoatCarPaint%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ClearCoatCarPaint&variant=ClearCoatCarPaint%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### ClearCoatTest

Compare the documented clearcoat cases, including coat strength, roughness and any coat normal detail.

Limit: A shiny surface alone does not establish each clearcoat input or separation from the base layer.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ClearCoatTest&variant=ClearCoatTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ClearCoatTest&variant=ClearCoatTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### ClearcoatWicker

Check the woven base and the differently wrinkled clearcoat highlights remain distinct.

Limit: One shared normal pattern on both layers can look plausible but does not demonstrate separate coat normals.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ClearcoatWicker&variant=ClearcoatWicker%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ClearcoatWicker&variant=ClearcoatWicker%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CommercialRefrigerator

Inspect the refrigerator glass, condensation and bottles, then watch the door animation.

Limit: A closed-door still does not verify door motion or transparency ordering through multiple glass surfaces.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CommercialRefrigerator&variant=CommercialRefrigerator%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CommercialRefrigerator&variant=CommercialRefrigerator%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareAlphaCoverage

Compare the alpha-coverage examples side by side, especially edge coverage and holes against the same background.

Limit: Overall transparency is insufficient; sampling and anti-aliasing can mask differences between coverage methods.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareAlphaCoverage&variant=CompareAlphaCoverage%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareAlphaCoverage&variant=CompareAlphaCoverage%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareAmbientOcclusion

Compare the occluded and unoccluded examples, looking for the intended darkening in creases without changing base color.

Limit: Strong direct lighting can hide occlusion differences; use a common indirect-light setup.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareAmbientOcclusion&variant=CompareAmbientOcclusion%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareAmbientOcclusion&variant=CompareAmbientOcclusion%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareAnisotropy

Compare the paired highlight shapes with and without anisotropy under the same light.

Limit: Different camera or light directions can mimic the intended contrast.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareAnisotropy&variant=CompareAnisotropy%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareAnisotropy&variant=CompareAnisotropy%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareBaseColor

Compare factor-only color, texture color and texture multiplied by vertex color; verify the three authored cases remain distinct.

Limit: A texture appearing is not enough to verify factor and vertex-color multiplication or color-space handling.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareBaseColor&variant=CompareBaseColor%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareBaseColor&variant=CompareBaseColor%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareClearcoat

Compare the coat examples for a distinct top reflection layer while retaining the base material.

Limit: A generic glossy shader can look convincing without actually implementing clearcoat.

Your saved feedback: The scene lacks a skybox ? seems to have skybox reflection in sample, where ours just shows black.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareClearcoat&variant=CompareClearcoat%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareClearcoat&variant=CompareClearcoat%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareDispersion

Look for wavelength-separated refraction in the dispersion example compared with the non-dispersive case.

Limit: Colored base textures or environment reflections can imitate rainbow edges; isolate transmission under a common background.

Your saved feedback: Ours is not transparant! shader seems to fail here

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareDispersion&variant=CompareDispersion%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareDispersion&variant=CompareDispersion%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareEmissiveStrength

Compare emissive-only and emissive-strength examples without conflating surface emission with scene illumination.

Limit: Tone mapping may hide HDR intensity differences; a screenshot alone does not measure emitted radiance.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareEmissiveStrength&variant=CompareEmissiveStrength%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareEmissiveStrength&variant=CompareEmissiveStrength%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareIor

Compare changes in surface reflection and transmitted distortion between the IOR examples.

Limit: Brightness alone does not identify IOR; keep lighting and background fixed.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareIor&variant=CompareIor%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareIor&variant=CompareIor%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareIridescence

Compare angle-dependent color shifts with and without iridescence.

Limit: One frontal color cannot establish thin-film behavior across angles and thickness settings.

Your saved feedback: Needs hdri

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareIridescence&variant=CompareIridescence%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareIridescence&variant=CompareIridescence%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareMetallic

Compare metallic and non-metallic reflections under a shared environment, not only their base colors.

Limit: A bright highlight is not by itself proof of metallic response.

Your saved feedback: Needs hdri

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareMetallic&variant=CompareMetallic%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareMetallic&variant=CompareMetallic%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareNormal

Compare normal-mapped detail with the unmodified surface while keeping the silhouette stable.

Limit: Painted color detail can imitate relief; use directional light movement to isolate normal mapping.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareNormal&variant=CompareNormal%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareNormal&variant=CompareNormal%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareRoughness

Compare highlight width and reflection blur between roughness cases.

Limit: Different exposure or lighting intensity can obscure roughness; brightness is not the defining check.

Your saved feedback: needs hdri

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareRoughness&variant=CompareRoughness%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareRoughness&variant=CompareRoughness%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareSheen

Compare the grazing-angle cloth highlight with and without sheen.

Limit: A diffuse color change is not evidence of a separate sheen response.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareSheen&variant=CompareSheen%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareSheen&variant=CompareSheen%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareSpecular

Compare the dielectric reflection strength/color between specular cases under identical lighting.

Limit: A metallic-looking highlight can hide incorrect specular parameters; inspect the intended non-metallic response.

Your saved feedback: needs hdri

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareSpecular&variant=CompareSpecular%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareSpecular&variant=CompareSpecular%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareTransmission

Compare visibility and distortion through the transmission examples against the shared background.

Limit: Alpha blending can look transparent without reproducing refractive transmission.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareTransmission&variant=CompareTransmission%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareTransmission&variant=CompareTransmission%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CompareVolume

Compare the volume examples for thickness-dependent tint/attenuation through the object.

Limit: Surface transparency alone cannot establish travel through a material volume.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareVolume&variant=CompareVolume%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CompareVolume&variant=CompareVolume%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Corset

Inspect the corset, collar and mannequin silhouette; check that fabric and other material regions remain distinct.

Limit: A single material-colored mannequin is not enough to verify the mapped surface details.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Corset&variant=Corset%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Corset&variant=Corset%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Corset&variant=Corset%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Cube

Check the textured cube's hard, non-smoothed face boundaries and orientation.

Limit: A rounded-looking or uniformly shaded cube can hide wrong normals; exact reference lighting is not established.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Cube&variant=Cube%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### CubeVisibility

Watch the authored cube visibility changes through the animation-pointer sequence.

Limit: A single visible cube says nothing about the hidden intervals or animated visibility property.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CubeVisibility&variant=CubeVisibility%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=CubeVisibility&variant=CubeVisibility%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### DamagedHelmet

Inspect the damaged panels, decals and surface relief; compare metallic and rough regions.

Limit: Texture detail alone does not isolate normal, occlusion and metallic-roughness channels under unknown lighting.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DamagedHelmet&variant=DamagedHelmet%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DamagedHelmet&variant=DamagedHelmet%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DamagedHelmet&variant=DamagedHelmet%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### DiffuseTransmissionPlant

Inspect light passing through thin leaves, particularly from their back side.

Limit: A green opaque plant from the front does not demonstrate diffuse transmission.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DiffuseTransmissionPlant&variant=DiffuseTransmissionPlant%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DiffuseTransmissionPlant&variant=DiffuseTransmissionPlant%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### DiffuseTransmissionTeacup

Inspect the cup's translucency under front and back lighting while retaining its surface detail.

Limit: A transparent-looking cup does not distinguish diffuse transmission from ordinary refractive transmission.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DiffuseTransmissionTeacup&variant=DiffuseTransmissionTeacup%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DiffuseTransmissionTeacup&variant=DiffuseTransmissionTeacup%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### DiffuseTransmissionTest

Compare the authored diffuse-transmission cases under a shared front/back-light setup.

Limit: A single bright render cannot isolate diffuse-transmission color, strength and thickness behavior.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DiffuseTransmissionTest&variant=DiffuseTransmissionTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DiffuseTransmissionTest&variant=DiffuseTransmissionTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### DirectionalLight

Inspect illumination from the authored directional light and compare surface orientation responses.

Limit: An added review light or unmatched exposure prevents a quantitative check of authored light calculations.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DirectionalLight&variant=DirectionalLight%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DirectionalLight&variant=DirectionalLight%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### DispersionTest

Compare the dispersion cases for colored refractive separation through the object.

Limit: Rainbow surface color alone is not sufficient; the transmitted background must be inspected.

Your saved feedback: camera in s&box does not match khronos reference. As suchh visual is hard to compare. But glass doesn't seem to render (looks like just white?)

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DispersionTest&variant=DispersionTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DispersionTest&variant=DispersionTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### DragonAttenuation

Inspect the dragon's thickness-dependent tint against its background, including each material variant.

Limit: One material variant and one viewing direction cannot establish attenuation across the model.

Your saved feedback: camera in s&box does not match khronos reference. As suchh visual is hard to compare. But glass doesn't seem to render (looks like just white?)

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DragonAttenuation&variant=DragonAttenuation%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DragonAttenuation&variant=DragonAttenuation%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Meshopt](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DragonAttenuation&variant=DragonAttenuation%2FglTF-Meshopt) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Meshopt-EXT](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DragonAttenuation&variant=DragonAttenuation%2FglTF-Meshopt-EXT) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### DragonDispersion

Inspect spectral separation through the dragon against its background, especially thick and thin regions.

Limit: A multicolored reflection is not enough to establish dispersion in transmission.

Your saved feedback: camera in s&box does not match khronos reference. As suchh visual is hard to compare. But glass doesn't seem to render (looks like just white?)

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DragonDispersion&variant=DragonDispersion%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=DragonDispersion&variant=DragonDispersion%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Duck

Check the duck's silhouette and single texture, including its eyes and bill placement.

Limit: This basic textured object does not isolate lighting fidelity, mipmaps or normal-map behavior.

Your saved feedback: Light too saturated, but looks correct

- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Duck&variant=Duck%2FglTF-Draco) — blocked / blocked
  - Requested comparison recipe changed; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Duck&variant=Duck%2FglTF-Embedded) — blocked / blocked
  - Requested comparison recipe changed; rerun required
- [ ] [glTF-Quantized](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Duck&variant=Duck%2FglTF-Quantized) — blocked / blocked
  - Requested comparison recipe changed; rerun required
### EmissiveStrengthTest

Compare the strength samples with controlled exposure; distinguish emission from reflected illumination.

Limit: Tone mapping and clipping can conceal intensity errors; exact HDR values need a controlled engine check.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=EmissiveStrengthTest&variant=EmissiveStrengthTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=EmissiveStrengthTest&variant=EmissiveStrengthTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### EnvironmentTest

Inspect the roughness progression on both metal and dielectric spheres using the same environment.

Limit: An arbitrary added light cannot establish environment-lighting accuracy; environment and exposure must be controlled.

Your saved feedback: Add HDRI before we can compare visuals, ligthing info looks wrong in S&BOX BECAuse lack os skybox. Ideally we have matching skybox/scene for all these tests!

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=EnvironmentTest&variant=EnvironmentTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-IBL](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=EnvironmentTest&variant=EnvironmentTest%2FglTF-IBL) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### FlightHelmet

Inspect helmet, visor, straps and stand, keeping glass, metal and fabric regions distinct.

Limit: A convincing overall silhouette does not establish each material channel independently.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=FlightHelmet&variant=FlightHelmet%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-KTX-BasisU](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=FlightHelmet&variant=FlightHelmet%2FglTF-KTX-BasisU) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### GlamVelvetSofa

Inspect velvet sheen at grazing angles and each material variant on the sofa.

Limit: One sofa color does not verify variants or separate sheen from specular reflections.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=GlamVelvetSofa&variant=GlamVelvetSofa%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=GlamVelvetSofa&variant=GlamVelvetSofa%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### GlassBrokenWindow

Check that the broken holes are absent while the remaining glass transmits the background.

Limit: Treating all transparency as alpha blending does not verify masked holes plus transmissive glass.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=GlassBrokenWindow&variant=GlassBrokenWindow%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=GlassBrokenWindow&variant=GlassBrokenWindow%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### GlassHurricaneCandleHolder

Inspect the holder's glass silhouette and thickness-dependent tint around the candle.

Limit: A transparent outline alone does not establish transmission and volume behavior.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=GlassHurricaneCandleHolder&variant=GlassHurricaneCandleHolder%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=GlassHurricaneCandleHolder&variant=GlassHurricaneCandleHolder%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### GlassVaseFlowers

Compare the alpha-blended left glass with the transmission/volume right glass while keeping the flowers visible.

Limit: The two methods are not expected to be identical; inspect their distinct ordering and refraction behavior.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=GlassVaseFlowers&variant=GlassVaseFlowers%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=GlassVaseFlowers&variant=GlassVaseFlowers%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### IORTestGrid

Compare distortion of the checker backdrop across the IOR sphere grid.

Limit: A single sphere or uncontrolled backdrop does not test the grid's IOR progression.

Your saved feedback: Glass renders as just white, lack hdri, the two left rows just look like black ?

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IORTestGrid&variant=IORTestGrid%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IORTestGrid&variant=IORTestGrid%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### InterpolationTest

Review STEP, LINEAR and CUBICSPLINE across translation, rotation and scale; compare the intended simultaneous tracks.

Limit: One active sequence cannot verify the nine-track comparison; a still cannot distinguish interpolation behavior.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=InterpolationTest&variant=InterpolationTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=InterpolationTest&variant=InterpolationTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### IridescenceAbalone

Inspect angle-dependent color across the shell's iridescent interior, retaining its surface structure.

Limit: One saturated shell color is not enough to verify view-dependent iridescence.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescenceAbalone&variant=IridescenceAbalone%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescenceAbalone&variant=IridescenceAbalone%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### IridescenceDielectricSpheres

Compare iridescent color shifts across the non-metallic sphere cases.

Limit: Do not infer metallic behavior from a dielectric test; a single angle misses the film response.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescenceDielectricSpheres&variant=IridescenceDielectricSpheres%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### IridescenceLamp

Inspect iridescent reflections and transmitted light on the lamp's transparent parts.

Limit: A colorful glass appearance alone does not isolate iridescence from transmission and volume.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescenceLamp&variant=IridescenceLamp%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescenceLamp&variant=IridescenceLamp%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### IridescenceMetallicSpheres

Compare the iridescent metallic sphere cases under a common environment.

Limit: Non-metallic substitutions or one view can hide an incorrect metallic iridescence response.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescenceMetallicSpheres&variant=IridescenceMetallicSpheres%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### IridescenceSuzanne

Inspect color shifts across Suzanne's curved surfaces as viewing angle changes.

Limit: One still is a limited sample of the view-dependent response, not a full iridescence test.

Your saved feedback: Add HDRI, and brightness value seems to high ?

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescenceSuzanne&variant=IridescenceSuzanne%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescenceSuzanne&variant=IridescenceSuzanne%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### IridescentDishWithOlives

Inspect the dish, olives and transparent/tinted material interactions; keep the different material regions separate.

Limit: The asset name alone does not establish iridescence support; inspect the actual declared transmission, volume, IOR and specular inputs.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescentDishWithOlives&variant=IridescentDishWithOlives%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=IridescentDishWithOlives&variant=IridescentDishWithOlives%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Lantern

Inspect the lantern's frame, glass and illuminated interior as separate material regions.

Limit: A bright lantern image does not distinguish emissive surfaces from actual emitted scene light.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Lantern&variant=Lantern%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Lantern&variant=Lantern%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Lantern&variant=Lantern%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Quantized](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Lantern&variant=Lantern%2FglTF-Quantized) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### LightVisibility

Watch authored light visibility changes, checking the illumination as well as visible lamp geometry.

Limit: Hiding a mesh alone does not verify that the light's contribution is disabled.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=LightVisibility&variant=LightVisibility%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=LightVisibility&variant=LightVisibility%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### LightsPunctualLamp

Inspect illumination from the lamp's authored punctual lights, including direction and range where used.

Limit: A visible lamp lit only by our review light does not verify imported punctual lights.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=LightsPunctualLamp&variant=LightsPunctualLamp%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=LightsPunctualLamp&variant=LightsPunctualLamp%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MandarinOrange

Inspect transmitted/scattered light through the orange under back lighting.

Limit: A textured orange viewed from the front does not establish diffuse transmission.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MandarinOrange&variant=MandarinOrange%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MaterialsVariantsShoe

Switch Beach, Midnight and Street; each should change the intended shoe materials without changing the mesh.

Limit: A default-material screenshot cannot verify the variant-selection API or all three looks.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MaterialsVariantsShoe&variant=MaterialsVariantsShoe%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MaterialsVariantsShoe&variant=MaterialsVariantsShoe%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MeshPrimitiveModes

Check all seven primitive types: points, lines, loops, strips, triangles, triangle strips and fans.

Limit: A triangle-only render does not verify the other primitive modes or their topology.

Your saved feedback: The dots/line thickness should be increased. but looks correct otherwise.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MeshPrimitiveModes&variant=MeshPrimitiveModes%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MeshPrimitiveModes&variant=MeshPrimitiveModes%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MeshoptCubeTest

Compare all grid cells across compressed layouts, normalized attributes and index sizes; watch rotating cells too.

Limit: A few intact cubes do not establish every compression/layout case; the animated columns also need motion evidence.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MeshoptCubeTest&variant=MeshoptCubeTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Meshopt](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MeshoptCubeTest&variant=MeshoptCubeTest%2FglTF-Meshopt) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MetalRoughSpheres

Compare the texture-driven metallic/roughness grid: highlight width and metal/dielectric response should follow its rows and columns.

Limit: A visible grid is not a material pass; keep light/environment/exposure fixed to diagnose roughness and metallic behavior.

Your saved feedback: Camera is not located at the middel, hdri seems to be missing

- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MetalRoughSpheres&variant=MetalRoughSpheres%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MetalRoughSpheresNoTextures

Compare the factor-driven metallic/roughness grid, including smooth/rough and metal/dielectric extremes.

Limit: Do not substitute texture sampling success for factor handling; uncontrolled lighting can mask incorrect values.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MetalRoughSpheresNoTextures&variant=MetalRoughSpheresNoTextures%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MetalRoughSpheresNoTextures&variant=MetalRoughSpheresNoTextures%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MorphPrimitivesTest

Check the static mesh weight of 0.5 raises the centre at half strength across both the large red and small green primitives.

Limit: This is a static morph-weight check, not necessarily an animation test. A flat mesh or a missing red/green region does not satisfy it.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MorphPrimitivesTest&variant=MorphPrimitivesTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MorphPrimitivesTest&variant=MorphPrimitivesTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MorphPrimitivesTest&variant=MorphPrimitivesTest%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MorphStressTest

Review Individuals, TheWave and Pulse; check all eight position/normal morph targets, first separately and then simultaneously.

Limit: The README explicitly describes a stress sample that can expose runtime limits. Record any limit; do not treat it as an automatic pass or exclusion.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MorphStressTest&variant=MorphStressTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MorphStressTest&variant=MorphStressTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MosquitoInAmber

Inspect the mosquito through the amber, including thickness-dependent tint and refraction.

Limit: A tinted transparent shell does not alone establish volume attenuation or correct IOR.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MosquitoInAmber&variant=MosquitoInAmber%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MosquitoInAmber&variant=MosquitoInAmber%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MultiUVTest

Compare texture placement from both UV sets, checking that the second set is actually selected where authored.

Limit: A correct base-color texture on UV0 can hide a missing or wrongly selected UV1 stream.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MultiUVTest&variant=MultiUVTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MultiUVTest&variant=MultiUVTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MultiUVTest&variant=MultiUVTest%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### MultipleScenes

Verify the default scene shows the square, then explicitly select the separate triangle scene.

Limit: One scene rendered successfully does not verify scene selection or both scene graphs.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MultipleScenes&variant=MultipleScenes%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=MultipleScenes&variant=MultipleScenes%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### NegativeScaleTest

Inspect mirrored/scaled objects for correct visible faces, winding and lighting.

Limit: Matching positions alone can hide flipped normals or incorrect backface culling under negative scale.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NegativeScaleTest&variant=NegativeScaleTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NegativeScaleTest&variant=NegativeScaleTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### NodePerformanceTest

Check the expected node/mesh/primitive/texture content is present before measuring its scaling behavior.

Limit: A screenshot cannot establish performance; time/memory measurements need a controlled benchmark.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NodePerformanceTest&variant=NodePerformanceTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NodePerformanceTest&variant=NodePerformanceTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### NodeVisibilityTest

Inspect every intended visible/hidden node and the effects of its visibility property.

Limit: One visible object does not verify hidden nodes or descendants' visibility behavior.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NodeVisibilityTest&variant=NodeVisibilityTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NodeVisibilityTest&variant=NodeVisibilityTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### NormalTangentMirrorTest

Compare the supplied-tangent normal-map blocks with their geometric references; use a clear horizon reflection to inspect mirrored orientations.

Limit: A textured surface alone cannot reveal tangent-handedness errors; this sample tests supplied tangents, not generated tangents.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NormalTangentMirrorTest&variant=NormalTangentMirrorTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NormalTangentMirrorTest&variant=NormalTangentMirrorTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### NormalTangentTest

Compare the normal-map blocks with their geometric references under a clear horizon environment.

Limit: This sample needs generated tangents; plausible color/detail is not sufficient to verify their orientation.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NormalTangentTest&variant=NormalTangentTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=NormalTangentTest&variant=NormalTangentTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### OrientationTest

Check the colored axes, labels and transformed nodes against the intended coordinate directions.

Limit: A familiar object silhouette alone does not establish axis mapping, rotations and translations.

- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=OrientationTest&variant=OrientationTest%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### PlaysetLightTest

Inspect the playset's authored lights and resulting illumination on its objects.

Limit: A generic review light can conceal missing imported lights; exact screenshot lighting is not established by object presence.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PlaysetLightTest&variant=PlaysetLightTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PlaysetLightTest&variant=PlaysetLightTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### PointLightIntensityTest

Compare the equally intense colored point lights; color should act as a multiplier rather than renormalizing brightness.

Limit: Unknown exposure or added lights prevent a quantitative intensity check.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PointLightIntensityTest&variant=PointLightIntensityTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PointLightIntensityTest&variant=PointLightIntensityTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### PotOfCoals

Inspect the pot's clearcoat and glowing coals as separate surface effects.

Limit: Glowing pixels do not prove that the coals illuminate nearby geometry; emission and scene lighting are separate checks.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PotOfCoals&variant=PotOfCoals%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PotOfCoals&variant=PotOfCoals%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### PotOfCoalsAnimationPointer

Watch the animated heat-refraction effect while inspecting the clearcoated pot and emissive coals.

Limit: A static pot or ordinary object animation does not verify the material-property animation.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PotOfCoalsAnimationPointer&variant=PotOfCoalsAnimationPointer%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PotOfCoalsAnimationPointer&variant=PotOfCoalsAnimationPointer%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### PrimitiveModeNormalsTest

Inspect unlit-looking points/lines without normals, flat-shaded triangles without normals and lit primitives with normals.

Limit: Rendering every primitive with one generic shading mode does not verify these documented distinctions.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=PrimitiveModeNormalsTest&variant=PrimitiveModeNormalsTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### RecursiveSkeletons

Watch reused meshes and nested/recursive skeleton relationships for independent, correct deformation.

Limit: One correctly posed mesh does not verify every reused mesh and skeleton binding.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RecursiveSkeletons&variant=RecursiveSkeletons%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RecursiveSkeletons&variant=RecursiveSkeletons%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### RiggedFigure

Watch the figure's joint deformation and overall articulated motion through the clip.

Limit: A rest pose cannot establish the animation or skinning behavior.

Your saved feedback: Speed does not match

- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RiggedFigure&variant=RiggedFigure%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RiggedFigure&variant=RiggedFigure%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RiggedFigure&variant=RiggedFigure%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### RiggedSimple

Inspect the simple rig through bending poses; vertices should follow the correct joints without separating.

Limit: One upright pose does not validate joint influences, inverse-bind transforms or animated normals.

Your saved feedback: has to be rotated 180 degree, but looks correct other then that

- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RiggedSimple&variant=RiggedSimple%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RiggedSimple&variant=RiggedSimple%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RiggedSimple&variant=RiggedSimple%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### ScatteringSkull

Inspect light transport through thin and thick skull regions under controlled back lighting.

Limit: An opaque skull or a uniformly tinted transparent shell does not establish subsurface scattering.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ScatteringSkull&variant=ScatteringSkull%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ScatteringSkull&variant=ScatteringSkull%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SciFiHelmet

Inspect the helmet's hard-surface details and distinct material regions under a stable light.

Limit: A recognizable helmet alone does not verify normal, roughness and metallic inputs independently.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SciFiHelmet&variant=SciFiHelmet%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SheenChair

Inspect the chair's grazing fabric sheen and each material variant.

Limit: One static color choice does not establish variants or the sheen layer.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SheenChair&variant=SheenChair%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SheenChair&variant=SheenChair%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SheenCloth

Inspect the cloth folds and grazing-angle sheen, separating it from base color.

Limit: A bright cloth image alone cannot distinguish sheen from diffuse lighting.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SheenCloth&variant=SheenCloth%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SheenTestGrid

Compare the sheen sphere grid under a common camera and light setup.

Limit: One sphere or uncontrolled illumination does not exercise the grid's material progression.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SheenTestGrid&variant=SheenTestGrid%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SheenTestGrid&variant=SheenTestGrid%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SheenWoodLeatherSofa

Inspect wood, leather and fabric regions, their tiling textures and distinct sheen/specular responses.

Limit: One overall sofa image can hide incorrect texture scale or a shared material response across different surfaces.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SheenWoodLeatherSofa&variant=SheenWoodLeatherSofa%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SheenWoodLeatherSofa&variant=SheenWoodLeatherSofa%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SimpleInstancing

Check the 5×5×5 arrangement of 125 instances with varying translations, rotations and scales, rather than a single cube.

Limit: A correct image does not establish one-draw efficiency, instance ownership or resource sharing; those need separate measurements.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleInstancing&variant=SimpleInstancing%2FglTF) — blocked / blocked
  - Previous case producer identity is unverified; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleInstancing&variant=SimpleInstancing%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SimpleMaterial

Check that the authored material is applied to the simple mesh rather than a fallback material.

Limit: A visible mesh alone does not establish material factors or lighting correctness.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleMaterial&variant=SimpleMaterial%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleMaterial&variant=SimpleMaterial%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SimpleMeshes

Check both nodes using the shared triangle mesh, with their intended transforms and attributes.

Limit: One triangle does not verify both nodes or correct shared-mesh instantiation.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleMeshes&variant=SimpleMeshes%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleMeshes&variant=SimpleMeshes%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SimpleMorph

Watch the triangle change shape through the morph animation without substituting rigid motion.

Limit: A single shape cannot establish interpolation of morph weights.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleMorph&variant=SimpleMorph%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleMorph&variant=SimpleMorph%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SimpleSkin

Watch the strip bend under its joints; inspect continuity and shaded normals across the bend.

Limit: A straight strip alone does not establish skinning, and a sampled movie does not measure real-time playback cadence.

- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleSkin&variant=SimpleSkin%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SimpleSparseAccessor

Inspect the intended displaced vertices produced by sparse accessor overrides.

Limit: A base mesh appearing is insufficient if sparse replacements were ignored; non-overridden vertices should remain unchanged.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleSparseAccessor&variant=SimpleSparseAccessor%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleSparseAccessor&variant=SimpleSparseAccessor%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SimpleTexture

Inspect the texture's orientation and placement on the simple mesh.

Limit: A plain-colored fallback does not demonstrate texture loading, UV selection or sampling.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleTexture&variant=SimpleTexture%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleTexture&variant=SimpleTexture%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SpecGlossVsMetalRough

Compare the specular/glossiness and metallic/roughness material examples under the same lighting.

Limit: A metallic/roughness-only fallback does not verify the specular/glossiness extension.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SpecGlossVsMetalRough&variant=SpecGlossVsMetalRough%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SpecGlossVsMetalRough&variant=SpecGlossVsMetalRough%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SpecularSilkPouf

Inspect silk sheen and specular reflections on the pouf, especially at grazing angles.

Limit: One fabric-colored render does not isolate the two reflection contributions.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SpecularSilkPouf&variant=SpecularSilkPouf%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SpecularSilkPouf&variant=SpecularSilkPouf%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SpecularTest

Compare the authored specular cases for reflection strength and color under common lighting.

Limit: Glossiness alone does not prove the specular extension; roughness and lighting can obscure its effect.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SpecularTest&variant=SpecularTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SpecularTest&variant=SpecularTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Sponza

Inspect architecture, hanging fabric and surface normals/materials before using the scene for lighting comparisons.

Limit: The preview's lights are not part of the asset; matching that screenshot requires a separately specified lighting setup.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Sponza&variant=Sponza%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### StainedGlassLamp

Inspect colored glass, frame and each material variant, including transmitted light through the panels.

Limit: A colorful opaque lampshade does not establish transmission, volume or variant selection.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=StainedGlassLamp&variant=StainedGlassLamp%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-JPG-PNG](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=StainedGlassLamp&variant=StainedGlassLamp%2FglTF-JPG-PNG) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-KTX-BasisU](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=StainedGlassLamp&variant=StainedGlassLamp%2FglTF-KTX-BasisU) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### SunglassesKhronos

Inspect the frame/logo and transmitted, angle-dependent appearance of the lenses.

Limit: One tinted lens view cannot establish IOR, volume and iridescence independently.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SunglassesKhronos&variant=SunglassesKhronos%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SunglassesKhronos&variant=SunglassesKhronos%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SunglassesKhronos&variant=SunglassesKhronos%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Suzanne

Inspect Suzanne's silhouette and smooth curved shading for missing geometry or normal discontinuities.

Limit: This general test object is not a calibrated lighting or full PBR conformance fixture.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Suzanne&variant=Suzanne%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TextureCoordinateTest

Compare the labeled texture orientation and UV-to-geometry placement on the test surfaces.

Limit: A texture appearing somewhere does not verify its origin, orientation or each coordinate mapping.

- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureCoordinateTest&variant=TextureCoordinateTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureCoordinateTest&variant=TextureCoordinateTest%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TextureEncodingTest

Compare equal-material representations within each row: factors and textures with different embedded metadata should agree.

Limit: Base color/emission need sRGB decoding while metallic/roughness stay linear; visual ambiguity needs controlled numeric/readback checks.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureEncodingTest&variant=TextureEncodingTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureEncodingTest&variant=TextureEncodingTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TextureLinearInterpolationTest

Compare the two green spheres: the factor-only and filtered-texture versions should be nearly the same color.

Limit: This isolates filtering after sRGB decoding; unrelated lighting/exposure changes can hide the difference.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureLinearInterpolationTest&variant=TextureLinearInterpolationTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureLinearInterpolationTest&variant=TextureLinearInterpolationTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TextureSettingsTest

Compare Test against Sample pass: expect green checks, with solid green boxes for the clamp cases; watch for red failure marks.

Limit: One green box does not establish every wrap or sidedness case; inspect every labeled cell.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureSettingsTest&variant=TextureSettingsTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureSettingsTest&variant=TextureSettingsTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureSettingsTest&variant=TextureSettingsTest%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TextureTransformMultiTest

Inspect texture transforms on each listed material input, not just base color.

Limit: A correct base-color transform cannot establish normal, emissive or extension-channel transforms.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureTransformMultiTest&variant=TextureTransformMultiTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureTransformMultiTest&variant=TextureTransformMultiTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### ToyCar

Inspect paint clearcoat, transparent parts and fabric/sheen regions on the toy car.

Limit: A recognizable toy does not independently establish transmission, sheen and clearcoat.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ToyCar&variant=ToyCar%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=ToyCar&variant=ToyCar%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TrafficCone

Inspect the cone's retroreflective regions with the light near and away from the viewing direction.

Limit: A bright stripe under arbitrary lighting does not establish retroreflection toward the light source.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TrafficCone&variant=TrafficCone%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TransmissionOrderTest

Inspect overlapping transparent objects in the authored order, including alpha and transmission interactions.

Limit: One isolated transparent object cannot reveal ordering errors among overlapping surfaces.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TransmissionOrderTest&variant=TransmissionOrderTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TransmissionOrderTest&variant=TransmissionOrderTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TransmissionRoughnessTest

Compare blur and refraction across roughness/IOR combinations against the same background.

Limit: A visible background alone does not establish the interaction between rough transmission and IOR.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TransmissionRoughnessTest&variant=TransmissionRoughnessTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TransmissionRoughnessTest&variant=TransmissionRoughnessTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TransmissionTest

Inspect the transmitted background and reflection behavior across the authored transmission examples.

Limit: Alpha blending can mimic transparency without implementing the transmission extension.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TransmissionTest&variant=TransmissionTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TransmissionTest&variant=TransmissionTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TransmissionThinwallTestGrid

Compare the grid's transmitted checker backdrop across its authored thin-wall/IOR cases.

Limit: One sphere cannot verify the grid; inspect the actual material parameters rather than inferring thin-film support from the sample title.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TransmissionThinwallTestGrid&variant=TransmissionThinwallTestGrid%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TransmissionThinwallTestGrid&variant=TransmissionThinwallTestGrid%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Triangle

Check the single indexed triangle's shape and winding without relying on a fallback mesh.

Limit: This minimal geometry test has no basis for claiming complex materials, animation or lighting fidelity.

- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Triangle&variant=Triangle%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TriangleWithoutIndices

Check the non-indexed triangle appears with the intended winding and vertex positions.

Limit: An indexed fallback triangle would not test this path; the image alone cannot prove index-buffer absence.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TriangleWithoutIndices&variant=TriangleWithoutIndices%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TriangleWithoutIndices&variant=TriangleWithoutIndices%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### TwoSidedPlane

Inspect the plane from both sides; back-facing geometry should remain visible as authored.

Limit: A front-only capture cannot establish the double-sided flag or back-face shading.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TwoSidedPlane&variant=TwoSidedPlane%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### USDShaderBallForGltf

Inspect the shader ball's curved, recessed and layered surfaces and its transmitted/volume material appearance.

Limit: A single overall material impression does not establish every glTF shading parameter or USD equivalence.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=USDShaderBallForGltf&variant=USDShaderBallForGltf%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=USDShaderBallForGltf&variant=USDShaderBallForGltf%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### Unicode❤♻Test

Check that geometry and materials resolve through Unicode file, mesh and material names.

Limit: A renamed ASCII substitute would bypass the intended name/path handling; visual output alone does not inspect retained names.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Unicode%E2%9D%A4%E2%99%BBTest&variant=Unicode%E2%9D%A4%E2%99%BBTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Unicode%E2%9D%A4%E2%99%BBTest&variant=Unicode%E2%9D%A4%E2%99%BBTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### UnlitTest

Check the orange and blue objects appear uniformly colored without light-driven surface shading.

Limit: A normally lit orange/blue object does not verify unlit behavior; change external lighting in a controlled engine check.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=UnlitTest&variant=UnlitTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=UnlitTest&variant=UnlitTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### VertexColorTest

Compare the authored COLOR_0 examples and their interaction with material color.

Limit: Matching a uniform base color is insufficient if vertex colors were ignored or incorrectly multiplied.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=VertexColorTest&variant=VertexColorTest%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=VertexColorTest&variant=VertexColorTest%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=VertexColorTest&variant=VertexColorTest%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### VirtualCity

Inspect the city layout, repeated buildings and material placement for missing or misplaced content.

Limit: An overview image does not establish scene-loading performance, culling correctness or every distant detail.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=VirtualCity&variant=VirtualCity%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=VirtualCity&variant=VirtualCity%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=VirtualCity&variant=VirtualCity%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=VirtualCity&variant=VirtualCity%2FglTF-Embedded) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### WaterBottle

Inspect the bottle's metal/roughness material plus normal, occlusion and emissive details.

Limit: A bottle silhouette alone does not establish the separate material-map channels.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=WaterBottle&variant=WaterBottle%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=WaterBottle&variant=WaterBottle%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Draco](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=WaterBottle&variant=WaterBottle%2FglTF-Draco) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
### XmpMetadataRoundedCube

Inspect the rounded cube as a rendering sanity check, then examine the attached XMP metadata through the appropriate data/API path.

Limit: Metadata preservation cannot be approved from pixels; this is principally a non-visual extension test.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=XmpMetadataRoundedCube&variant=XmpMetadataRoundedCube%2FglTF) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required
- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=XmpMetadataRoundedCube&variant=XmpMetadataRoundedCube%2FglTF-Binary) — blocked / blocked
  - Rendering inputs or stock resource proof changed or unavailable; rerun required

## Existing captures — not a new fix

### AnimatedMorphCube

Watch both thin and angle morph targets change the cube's shape; faces should remain connected and shading should follow deformation.

Limit: A rotating or translated rigid cube does not demonstrate morph weights; one sampled loop is not a real-time performance test.

Your saved feedback: Flip 180degree, we had similar issue with duck being rotated before we steered it, keep this in mind for any model we render.

- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimatedMorphCube&variant=AnimatedMorphCube%2FglTF-Binary) — sampled animation
### AnimatedTriangle

Watch the triangle's node rotation while its geometry remains intact.

Limit: A single triangle image proves neither the rotation channel nor interpolation between keys.

- [ ] [glTF-Embedded](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimatedTriangle&variant=AnimatedTriangle%2FglTF-Embedded) — sampled animation
### Duck

Check the duck's silhouette and single texture, including its eyes and bill placement.

Limit: This basic textured object does not isolate lighting fidelity, mipmaps or normal-map behavior.

Your saved feedback: Light too saturated, but looks correct

- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Duck&variant=Duck%2FglTF-Binary) — still image
### Fox

Review Survey, Walk and Run separately; check limb/ear/tail deformation and continuity throughout each cycle.

Limit: One pose or one clip does not establish all three animations, looping or real-time playback timing.

- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Fox&variant=Fox%2FglTF-Binary) — sampled animation
### OrientationTest

Check the colored axes, labels and transformed nodes against the intended coordinate directions.

Limit: A familiar object silhouette alone does not establish axis mapping, rotations and translations.

- [ ] [glTF-Binary](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=OrientationTest&variant=OrientationTest%2FglTF-Binary) — still image
### SimpleSkin

Watch the strip bend under its joints; inspect continuity and shaded normals across the bend.

Limit: A straight strip alone does not establish skinning, and a sampled movie does not measure real-time playback cadence.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=SimpleSkin&variant=SimpleSkin%2FglTF) — sampled animation

## Your approval still matches the current comparison

### AnimatedCube

Watch the textured cube rotate continuously; its face orientation and texture should follow the same rigid object.

Limit: One pose does not establish rotation interpolation, loop continuity or playback timing.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimatedCube&variant=AnimatedCube%2FglTF) — sampled animation
### AnimatedMorphCube

Watch both thin and angle morph targets change the cube's shape; faces should remain connected and shading should follow deformation.

Limit: A rotating or translated rigid cube does not demonstrate morph weights; one sampled loop is not a real-time performance test.

Your saved feedback: Flip 180degree, we had similar issue with duck being rotated before we steered it, keep this in mind for any model we render.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimatedMorphCube&variant=AnimatedMorphCube%2FglTF) — sampled animation
### AnimatedTriangle

Watch the triangle's node rotation while its geometry remains intact.

Limit: A single triangle image proves neither the rotation channel nor interpolation between keys.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=AnimatedTriangle&variant=AnimatedTriangle%2FglTF) — sampled animation
### Box

Check the cube silhouette and all visible faces, the red non-metallic material, sharp face boundaries and sensible face-to-face shading from its normals.

Limit: A cube existing is only a geometry smoke check. Exact screenshot brightness, shadows and background are not a gate without matched lighting/exposure; ambiguous shading needs a controlled engine-light check.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Box&variant=Box%2FglTF) — still image
### Duck

Check the duck's silhouette and single texture, including its eyes and bill placement.

Limit: This basic textured object does not isolate lighting fidelity, mipmaps or normal-map behavior.

Your saved feedback: Light too saturated, but looks correct

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Duck&variant=Duck%2FglTF) — still image
### Fox

Review Survey, Walk and Run separately; check limb/ear/tail deformation and continuity throughout each cycle.

Limit: One pose or one clip does not establish all three animations, looping or real-time playback timing.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Fox&variant=Fox%2FglTF) — sampled animation
### OrientationTest

Check the colored axes, labels and transformed nodes against the intended coordinate directions.

Limit: A familiar object silhouette alone does not establish axis mapping, rotations and translations.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=OrientationTest&variant=OrientationTest%2FglTF) — still image
### RiggedSimple

Inspect the simple rig through bending poses; vertices should follow the correct joints without separating.

Limit: One upright pose does not validate joint influences, inverse-bind transforms or animated normals.

Your saved feedback: has to be rotated 180 degree, but looks correct other then that

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=RiggedSimple&variant=RiggedSimple%2FglTF) — sampled animation
### TextureCoordinateTest

Compare the labeled texture orientation and UV-to-geometry placement on the test surfaces.

Limit: A texture appearing somewhere does not verify its origin, orientation or each coordinate mapping.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=TextureCoordinateTest&variant=TextureCoordinateTest%2FglTF) — still image
### Triangle

Check the single indexed triangle's shape and winding without relying on a fallback mesh.

Limit: This minimal geometry test has no basis for claiming complex materials, animation or lighting fidelity.

- [ ] [glTF](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site/#model=Triangle&variant=Triangle%2FglTF) — still image
