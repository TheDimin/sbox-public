# Point Light Intensity Test: evidence boundary

The upstream CC0 source and reference are unchanged. Its eight 1 cd point
lights have finite 1.125 m range; the three RGB lights share a location. The
source README expects RGB additivity and gray to be dimmer than white, and
warns that exposure, IBL and tone mapping alter the screenshot. There is no
authored camera. The source oracle checks the pinned bytes, declarations, RGB
co-location, and independent 1/r² unbounded ratios. It does not render.

`PointPhotometryTests` tests the existing native point light on an independent
literal plane with an anonymous diagnostic shader that returns the native
direct Lambert term. It uses a floating-point target, disables postprocessing,
measures candidate coefficients at 0.5, 1 and 2 m, and checks linear RGB,
intensity, finite-range and deletion. The shader isolates native light
arithmetic; it cannot establish sample material or public-loader support, and
the native unit scale has no claimed candela calibration. A large native radius
is a finite approximation, not proof of infinite range. The extension requires
cutoff at the authored range, prefers inverse-square within it and permits
softening near the edge; its illustrative fourth-power smoothing is not the
only allowed curve.

`PointPublicFallbackTests` mounts the unchanged glTF and dependencies, invokes
canonical `Model.Load`, creates the ordinary component and records the current
optional-extension fallback. Its no-imported-light assertion characterizes a
known failure, not acceptance. Positive imported-light ownership, transforms,
two-instance isolation and deletion, actual source-material capture against
the reference, calibrated radiometry and compiled-form parity remain open.
No test result should be inferred from source presence or a submitted request.

The aggregate receipt `e885d551ac44f2b4b401796174f72d738faff021ba81456a658d489e56848232`
failed this test's fractional float-target clear assertion before collecting any
photometry. Native clear's packed-color quantization was independently observed
in the Mandarin float control. The revised one-case request uses exact clear
endpoints, waits for native frames, checks every pixel, and separately tests
shader-rendered HDR and linearity. The previous failure remains part of the record.

The corrected guarded one-case run passed on Engine MVID `123f2cf1-6081-4d93-810b-333342cb20ee`
and glTF MVID `56e0e078-1fed-49a1-a3fc-86f4516b0cd0`. Receipt
`Temp/gltf-validation/direct-gpu/point-photometry-run-01/probe-01-editor_direct_gpu_tests/receipt-01.json`
SHA-256 `f0d0768af8792c247e8d8238ad67c5d74202e71a6ebc9721514024fe67fa6e00`;
cleanup errors were empty. The shader-rendered HDR response was 25.75 and
12.875 for half intensity; white equaled three simultaneous RGB lights, gray
was half white, and zero/deletion controls agreed. For the original candidate
quadratic 6.4516, 0.5/1 m was 1.0015 versus the unbounded inverse-square
ratio 4. With native quadratic 1000 and compensated color, the 0.5/1 and
1/2 m ratios were 4.0038 and 4.0190 respectively. Authored 1.125 m radius
produced positive response inside and zero at and beyond cutoff with the first
candidate. This demonstrates a tunable bounded approximation, not general
physical or cd calibration: the native saturation still exists near the light
and the native radius remains finite. The second coefficient was not tested
with the authored finite radius. Source-material rendering, actual imported
lights and public lifecycle remain open.

Reference: [Khronos KHR_lights_punctual](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_lights_punctual/README.md).

## Finite point candidate

The anonymous shader probe checks the recommended finite-range attenuation
on the diagnostic receiver with a fixed Lambert scale of `1 / pi`. It samples
0.5, 1, 2, 4 and 5 m against a 4 m cutoff, checks linear intensity and color,
and measures the ordinary point light with the marker enabled and disabled.
A combined readback checks additivity of the marked and ordinary lights in
the same view. The separate factory probe uses the pinned glTF and its derived
GLB to check all eight authored positions, colors and finite radii on native
objects, two independent placements, visibility, disposal and explicit rejection
of an omitted range.

The independent source and finite-formula oracles passed. Both helpers
compiled offline with the runner's framework against installed Engine
`60bc2b51…` and staged Engine `041e2e00…`. The host reboot invalidated the
previous editor marker. A new verified editor ran both requests under the
native lock and installed host guard on Engine MVID
`362ddfea-670c-444f-8088-0f6c6453080e` and glTF MVID
`56e0e078-1fed-49a1-a3fc-86f4516b0cd0`.

The finite diagnostic passed 1/1, with zero failed or incomplete cases and no
cleanup errors. Receipt
`Temp/gltf-validation/direct-gpu/point-finite-candidate-runs/9649f8e5c2f9466688301b233f14d1a2/probe-01-editor_direct_gpu_tests/receipt-01.json`
has SHA-256
`38e6cccb0d59627dbe9c2690ddb1c74b013e2a0b923494163f684fa7a26fba10`.
The measured 0.5, 1 and 2 m responses were 1.271484375, 0.31689453125 and
0.0745849609375, against independent expectations 1.2729286952369363,
0.31706648819088523 and 0.07460387957432595. At 4 m, beyond range,
zero intensity and after deletion the response was zero. The ordinary point
light gave 0.99951171875 with the marker enabled and disabled. The combined
ordinary-plus-marked response was 1.31640625, agreeing with their sum within
the declared tolerance.

The source and derived-compiled ownership cases passed 2/2, with zero failed or
incomplete cases and no cleanup errors. Receipt
`Temp/gltf-validation/direct-gpu/point-finite-ownership-runs/877e9d250f0b41979835f9d363824ee9/probe-01-editor_direct_gpu_tests/receipt-01.json`
has SHA-256
`a83b95284faabeb28ba7b754a3c3d2bb635eb86573626953d51e8882fb594394`.
Each case created 16 lights across two instances and retained the second eight
after first-instance disposal. Literal source positions and colors, authored
1.125 m radii, packed marker identity, visibility and final cleanup passed.
The factory rejected omitted range without creating another light.

These are model-local candidate proofs. The shader was compiled anonymously
for the test and the factory was invoked by the test helper. Public
`Model.Load` light creation, production material integration, enabled shadows,
other camera paths, long-term marker reuse, spots and unbounded-range culling
remain open. The earlier cookie-marker receipt `f7cac5c…` covers its separate
shader and retains its original producer identity.
