# What to fix next (from your review of 2026-11-10)

Source: `review-feedback.json`, 56 entries reviewed on 2026-11-10 (05:29 to 05:55). Captures: `Temp/gltf-validation/current`, 2026-11-10 12:31, with the diffuse fix, gain 2 and the blurred sky.
Status: **facts** come from your notes and from measurements. **Opinion** and the causes marked "likely" are not yet proved.

## Verdicts

| Verdict | Count | Models |
|---|---|---|
| Approved | 15 | AlphaBlendModeTest, AnimatedCube, AnimatedMorphCube, AnimatedTriangle, EmissiveStrengthTest, Fox, MeshPrimitiveModes, MeshoptCubeTest, MorphPrimitivesTest, MultipleScenes, NodePerformanceTest, NodeVisibilityTest, OrientationTest, RiggedFigure, SimpleSparseAccessor, SimpleTexture |
| Close | 10 | ABeautifulGame, AnimatedColorsCube, Duck, FlightHelmet, MaterialsVariantsShoe, MetalRoughSpheres, MorphStressTest, MultiUVTest, PlaysetLightTest, SimpleMaterial, StainedGlassLamp |
| Rejected | about 30 | see the groups below |

The simple core-glTF models now pass. All remaining rejects need one of six causes.

## Priority list

| # | Work item | Models it closes | Size | Confidence in cause |
|---|---|---|---|---|
| 1 | **Capture at 1024 px** (and render the glTF reference at the same size) | FlightHelmet, Lantern, MultiUVTest, StainedGlassLamp, SheenCloth, SheenWoodLeatherSofa, IridescenceAbalone, PotOfCoals, SciFiHelmet (and BoomBox, BoomBoxWithAxes from before) | Small | Medium-high |
| 2 | **Metals look dull: fix IBL for metals** | MetalRoughSpheresNoTextures (you marked HIGH), NormalTangentTest, NegativeScaleTest, PrimitiveModeNormalsTest, MetalRoughSpheres, SpecularTest | Medium | Medium |
| 3 | **Punctual lights** (`KHR_lights_punctual`) | LightVisibility, LightsPunctualLamp, PointLightIntensityTest, PlaysetLightTest | Medium | High |
| 4 | **Transmission** (steps 1 to 4 of `TRANSMISSION_PLAN.md`) | IridescenceLamp, IridescentDishWithOlives, AnisotropyBarnLamp, ABeautifulGame, DragonDispersion, IORTestGrid, CarConcept, ChronographWatch, CommercialRefrigerator | Large | High |
| 5 | **Animation regression and video review** | InterpolationTest, MorphStressTest, LightVisibility, AnimatedColorsCube | Small to medium | Medium |
| 6 | **Sheen** (`KHR_materials_sheen`) | SheenChair, SheenCloth, SheenWoodLeatherSofa, SheenTestGrid | Medium | High |
| 7 | **Iridescence** (`KHR_materials_iridescence`) | IridescenceAbalone, IridescenceDielectricSpheres, IridescenceMetallicSpheres, IridescenceLamp, IridescenceSuzanne | Medium | High |
| 8 | **Sky orientation and a studio environment** | ClearCoatTest, ChairDamaskPurplegold, IORTestGrid, Duck | Small | Medium |
| 9 | Smaller items (see the end) | SimpleInstancing, Sponza, GlamVelvetSofa, MosquitoInAmber, ScatteringSkull, SciFiHelmet geometry | Mixed | Mixed |

My order: 1, 2, 3, 5, 8, then 4, 6, 7. Items 1, 2, 3, 5 and 8 are small or medium and close about 25 models. Items 4, 6 and 7 are new shader features, and each one closes only a few models.

## Detail

### 1. Fuzzy textures (9 models, and 2 from before)
- **Facts:** you said "fuzzy", "super low quality", "texture detail lost", "different MIP level". `GltfCorpusCase.cs` limits the capture size to 64 to 512 px. The reference is rendered at 2× (1024 px) and scaled down for the diff, but the site shows the reference at full size next to a 512 px capture.
- **Likely cause:** most of the "fuzzy" is the 512 px capture, and not a texture error. The two images are not compared at the same resolution.
- **Test:** capture FlightHelmet and Lantern at 1024 px and look again. If they are still soft, check the texture compiler (`GltfTextureCompiler.cs`) for the mip selection and the sampler filter. The glTF shader uses `Filter( BILINEAR )` for all textures. Use trilinear or anisotropic, as the Sample Viewer does (16× anisotropic).
- **SciFiHelmet** ("lowest quality geometry and textures") may be a different problem. It is in the 3.0 sample set with several resolutions. Check which variant loaded.

### 2. Metals do not look metallic
- **Facts:** MetalRoughSpheresNoTextures: "in-engine this is not metallic at all" (HIGH PRIORITY). NormalTangentTest: "only the gold has reflection, the other two are fuzzy". PrimitiveModeNormalsTest: "not as reflective". NegativeScaleTest: "dark blue and metal are off". After the gain change, MetalRoughSpheres still reads 1.22× the reference in luma.
- **Measured:** the rough metal columns were 0.5 to 0.8× the reference before the gain change. The gain was fitted on dielectrics, not on metal.
- **Likely causes** (in order): (a) the cubemap mips lose energy at high roughness; (b) a `metallicFactor`-only material (no texture) takes a different route in the adapter than a textured one; (c) the specular uses the stock GGX with `EnvMap`, and the Sample Viewer uses its own prefiltered chain.
- **Test:** capture MetalRoughSpheresNoTextures with the camera pulled back (you asked for the FOV change so the back row shows). Then measure the luma per roughness column against the reference, and fit the gain per roughness. Do not tune by eye.

### 3. Punctual lights (4 models)
- **Facts:** "no lights show up in-engine" (PointLightIntensityTest, LightVisibility, LightsPunctualLamp). The recipe notes already say that public `Model.Load` does not create `KHR_lights_punctual` lights.
- **Work:** read the lights in `Sandbox.Gltf`, and create `Light` objects in the capture (and in the model loader if you want them in the game). The unit conversion needs a decision: glTF uses candela and lux; the Sample Viewer uses these values directly. Match the viewer first.
- **Note:** PlaysetLightTest: you said the Khronos reference is very dark, the Sample Viewer is over-saturated, and the engine looks fine. That model needs a judgement call, not a fix. Mark it as accepted when lights work.

### 4. Transmission and volume
- **Facts:** glass does not render on 6 models (IridescenceLamp, IridescentDishWithOlives, AnisotropyBarnLamp, ABeautifulGame, IORTestGrid, DragonDispersion). The plan is in `TRANSMISSION_PLAN.md`.
- **Opinion:** this is the biggest feature and the riskiest, because it needs the framebuffer copy in the translucent pass. Do it after items 1 to 3, because glass is judged by its reflection of the environment, and that needs correct metals and a correct sky first.

### 5. Animation
- **Facts:** InterpolationTest: "does not get close to the Khronos reference anymore" (rejected). It was not rejected earlier, so this may be a regression from my changes to the corpus runner or from another session's changes. MorphStressTest and LightVisibility "should be a video".
- **Work:** (a) compare InterpolationTest frames with the previous run in `Temp/gltf-validation/exp-before`; (b) record these captures as video frames. The recipes already list `sampleTimes`. Check whether the capture saves `frames/` for these models, and whether the site plays them. (c) `KHR_animation_pointer` for AnimatedColorsCube is still open from the first triage.

### 6 and 7. Sheen and iridescence
- **Facts:** 4 sheen models and 5 iridescence models show no effect, because both extensions are ignored. They are optional in these files, so the models load.
- **Opinion:** sheen first. The Khronos formula is short (Charlie distribution, a lookup table that the Sample Viewer assets already contain). Iridescence needs a thin-film Fresnel term and is longer. Neither one gets visible results without the right sky, so do both after item 8.

### 8. Sky
- **Facts:** the blur sweep gave opposite results: DamagedHelmet wanted blur 0.9, ClearCoatTest and ChairDamaskPurplegold wanted less. In the reference these two show ground behind the model, and in s&box they show cloud.
- **Likely cause:** a vertical flip or a rotation difference in the cubemap, not the blur. My shader rotates only around Z. Test with the yaw, and with a flipped Z, on ChairDamaskPurplegold.
- **IORTestGrid:** you asked for a studio environment. That is a different HDRI from the Sample Viewer default. Opinion: do not add one. It breaks the rule that the reference and the engine use the same light. Instead, accept the limit of this model.

### 9. Smaller items
- **SimpleInstancing:** you said the Sample Viewer does not show instancing either. The reference may be wrong. Use the Khronos screenshot for this one and add `EXT_mesh_gpu_instancing` to the engine later.
- **Sponza:** the camera recipe does not match. Fix the camera in the recipe builder, not in the engine.
- **GlamVelvetSofa:** colour is off (it uses sheen, so it is likely item 6).
- **MosquitoInAmber, ScatteringSkull:** the Sample Viewer cannot show the volume scattering that these models test (black, or no mosquito). Mark both as "reference unusable" and compare to the Khronos screenshot.
- **Still open from the first triage:** BrainStem speed, CesiumMilkTruck wheel direction, CompareAlphaCoverage black spots, black outlines on spheres, `KHR_animation_pointer`.

## Models that still fail to load (6)
CommercialRefrigerator, IridescenceSuzanne, PotOfCoalsAnimationPointer, SheenTestGrid, SpecGlossVsMetalRough, SpecularSilkPouf. They declare a required extension. Items 4, 6 and 7 plus `KHR_materials_pbrSpecularGlossiness` and `KHR_materials_specular` unlock them.

## Do first, in this session
1. Raise the capture limit to 1024 and recapture 5 fuzzy models (30 minutes).
2. Pull the camera back on MetalRoughSpheresNoTextures and measure metal per roughness.
3. Check InterpolationTest against `exp-before`.
