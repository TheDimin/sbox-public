# LightsPunctualLamp: light metadata and affine plan

Implementation is isolated in the new internal
`Sandbox.Gltf.DirectGpu.Lighting.GltfPunctualLightPlan`. It does not change native
vmdl loading, allocate a native model/mesh, create gameplay components, or choose
an engine brightness approximation. Root/backend owner must integrate the plan
with the resource asset and renderer-instance lifecycle. The resource retains the
document; instances evaluate their own pose and placement.

## Pinned source and specification

Corpus revision `90d7ede14c7e280af263824604b427a1ca02cb66` was verified from the
actual checkout. `reference.json` pins all ten source/dependency files. The
independent Python author reads glTF JSON or the GLB JSON chunk, evaluates
column-vector node hierarchy arithmetic, and records five light instances in
each variant. The source really has three meshes and three materials, despite
the upstream prose describing an older five-mesh/six-material asset. Artist
Teresa González Viegas/DGG; CC-BY-4.0 source provenance remains in upstream
`LICENSE.md`/`metadata.json`.

The extension baseline is the ratified [KHR_lights_punctual at published glTF
revision 8e798b02](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/extensions/2.0/Khronos/KHR_lights_punctual/README.md)
(Git blob `6ab46ed52518c4d58f6a1765f2ce44440bbc92ab`) and its [light
schema](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/extensions/2.0/Khronos/KHR_lights_punctual/schema/light.schema.json)
(blob `bc17310783cc61ca003f985b1e18cc6f76a0eedd`). Both were fetched by the
connected GitHub reader. This baseline requires the `spot` object. It is not an
assumption that an omitted spot object means defaults. The later [854a757a
clarification](https://github.com/KhronosGroup/glTF/commit/854a757a61e6bc0dde91631579b9d3d2e1743df9)
clarifies color versus intensity; it does not change the units, defaults, range
or angle domains used here.

Point/spot intensity is candela; directional intensity is lux. Color is linear
RGB, default white; intensity defaults to one. Range is meters and defaults to
infinite. Finite ranges must be positive and stop contribution beyond the
cutoff. Local -Z is the emitted direction. Node scale affects inherited position
and orientation, while light intensity, range and cone shape remain unchanged.
Spot cones are half angles in radians: default inner zero, outer pi/4; inner is
less than outer, and outer is at most pi/2. Smooth angular falloff remains a
rendering requirement.

## Tests and numerical sensitivity

The initial focused MSTest cases were authored before the implementation; the
current 21-case inventory adds regressions from source review. They cover
all types/defaults/units, zero intensity and black color, meters-to-inches range,
half-angle conversion, selected-scene instances/shared definitions, inherited
shear, reflected affine placement, tiny and collapsed axes, recovery, animated
parents, independent state, cached-pose and empty-fallback document disposal,
ignored directional position, foreign poses, invalid placement/reference, explicit
unsupported fallback, undeclared extensions and missing default scenes. A
collapsed emitted axis is finite zero plus `HasEmissionDirection=false`; point
lights never require an axis. No arbitrary near-zero threshold is introduced.

`LightsPunctualLampCoreTests.RunAll(checkoutRoot)` has 30 CPU/data cases: two
oracle sensitivity controls and seven per form across upstream glTF, upstream
GLB, and one derived `.glb_c` packaging form for each. Expected light nodes are
3..7, definitions 0..4, intensities exactly 15, 1.5, 80, 80 and 180 candela.
All five have infinite range. Independent source positions use exactly
`(z,x,y)/0.0254` once, followed by scalar affine placement arithmetic. Enforced
position tolerances are 0.00002 engine inch at identity and 0.00005 after affine
placement, accounting for float32 source matrices and conversion. These are CPU
arithmetic tolerances, not a rendered-image threshold.

Optional `KHR_materials_transmission` remains separately reported as unsupported;
lamp shade material fidelity is pending. Packaging preserves image URIs and their
dependencies; this is not compiled-only mounted/public loading proof.

## Existing engine API audit and remaining rendering work

`ScenePointLight` and `SceneSpotLight` initialize `QuadraticAttenuation=1`;
`SceneLight` divides this property by 10000 before the native setter.
`SceneSpotLight.ConeInner/ConeOuter` explicitly use half-angle degrees, so
conversion must multiply by 180/pi only. `Light.Intensity` documents an arbitrary
HDR brightness multiplier, not candela/lux. Native scene lights use `LightColor`
and `Radius`, with no documented infinite range contract.

The managed `LightBinnerFeature.Pack` fallback uses quadratic attenuation divided
by 10000 and subtracts a finite-radius bias. Its cone denominator clamps to 0.01,
where the glTF reference uses 0.001, and `Light.hlsl` uses an unsquared cone factor
at the inspected call site. The authoritative live path may instead use
`RenderContext.PackSceneLight`; native implementation and shader include
`CalculateDistanceFalloff` are not present here. Therefore these observations
do not establish a physical brightness/range/cone mapping.

`LightsPunctualLampNativeLightAudit.Run(privateWorld)` prepares six bounded actual
native packing observations for the coordinator. It creates only temporary
scene lights, captures loaded Engine/Renderer MVIDs and packed color, quadratic,
bias, radius, direction and cones, then deletes every light in `finally`. One
explicitly labeled candidate quadratic tests a possible meter conversion; it is
not production mapping or a fidelity claim. No infinity is submitted to native
code and no hidden finite cutoff replaces the lamp's authored infinite range.

Still required: execute the C# suites against the actually staged producer;
realize all five lights in the instance with exact physical rendering behavior;
measure native/light-buffer properties and actual GPU/pixel response; verify
range cutoff, inverse-square ratios, cone shape, emission direction, linear
color, two independent placements and disable/destroy/replacement lifetime;
run empty-before/positive/deleted-empty image controls; preserve a vmdl control;
verify public source/compiled-only `Model.Load`, Windows and loading/memory
gates. CPU plan success cannot pass these separate requirements.

Current evidence: the lightweight source author verified the corpus pin and
generated the independent reference successfully. The coordinator compiled and
executed the separate six-observation native packing audit; its cleanup and
producer-identity wrapper passed 1/1. See [actual native findings](NATIVE_PACK_FINDINGS.md)
for receipt identity and limits. The 51 focused/model CPU cases, new Engine plan,
GPU/readback, final images and public loading remain pending. Only the validation
coordinator runs editor, build, shader and capture jobs.
