# Native punctual-light control audit

This is a source audit and design proposal, not a feature implementation.
The actual bounded float observation is documented in `ACCEPTANCE.md`.

`SceneLight` exposes color, finite radius, constant/linear/quadratic terms,
shadows, cookie, render-lobe switches and shape. `SceneSpotLight` additionally
exposes half-angle inner/outer cones and `FallOff`. The exposed quadratic
property divides by 10,000 before the native setter. No exposed photometric
or inverse-square mode is defined in those classes. `FallOff` is forwarded to
native state, but its effect in native packing/rendering has not been measured;
the managed fallback pack does not consume it. Do not infer a physical mode
from the property name.

`common/classes/Light.hlsl::GetLightAttenuation` calls the distance helper in
`Shadows/Obsolete.hlsl`, which saturates inverse polynomial falloff and subtracts
a finite-radius bias. The spot factor uses the packed cone reciprocal without
clamping or squaring. The stock `Light.Init` separately computes light color,
direction, attenuation and shadow visibility, so a tagged branch confined to
attenuation can preserve stock BRDF, lobe flags, cookies, modulators and shadow
sampling for both imported and ordinary lights. Untagged lights retain exactly
the existing branch. For tagged point/spot, compute 1/(distance in meters)²,
apply authored finite-range cutoff and smooth edge if desired; spot additionally
uses saturated angular falloff calculated from packed inner/outer cosines.
The measured native pack clamps the precomputed spot reciprocal, so recompute it
from the two cosines rather than consuming that reciprocal. Handle coincident
positions explicitly and choose a finite numerical epsilon without asserting
exact intensity at the singular point. Intensity-to-native-BRDF calibration
still requires an independent calibrated test; the float Lambert test proves
relative behavior only.

Native and managed renderer paths differ. The managed renderer mirrors native
lights in `SceneMirror.ApplyLight`, retains `NativePacked`, and uploads a
156-byte `GpuLight` via `LightBinnerFeature.PackLights`; managed state and the
GPU mirror could carry a new opt-in tag before upload. The tested native
standard pipeline instead calls `NativeLayers.AddLightBinner`, whose native
`LightBinnerSetupLayer` packs the lights. `SceneLight.lightNative` already has
generated `GetLightFlags`/`SetLightFlags`, used for native baked/mixed-shadow
flags, but native flags and `BinnedLight.Flags` are distinct domains with
different documented bits. No collision-free flag or guaranteed passthrough
is established. No arbitrary flag, negative coefficient, color sentinel or
cookie slot should be repurposed as a production tag without establishing an
explicit contract and negative controls. `NativeFlagProbe` is a pending
private-world packing experiment, not proof of culling, shader visibility or
permission to use any bit.

The guarded private-world probe on Engine MVID `123f2cf1-6081-4d93-810b-333342cb20ee`
produced receipt SHA-256 `b3ca1c1189885b44b809a03c94319cd64d1b2d3c9826373d3ca0a84caa2ad101`.
Its test failed after three recorded rows, with no cleanup errors. Baseline
native flags were 12 and GPU packed flags were 270. Setting native bits
`0x10000` and `0x40000000` was retained by the native getter and returned
flags, but each packed GPU flag word remained 270. Attempting `0x80000000`
made `RenderContext.PackSceneLight` return false before a fourth row; its
signed result handling is a possible cause but not established. The two
observed bits therefore do not offer a packed tag in this producer. This does
not exclude another mechanism or establish a safe bit, shader visibility,
culling behavior or the need for native core changes.

## Cookie identity candidate

`SceneLight.LightCookie` publicly binds a texture on both derived light
classes; `SpotLight.Cookie` documents a beam-projection texture. The packed
`BinnedLight.LightCookieTextureIndex` is shader-visible, and `Texture.Index`
returns the native 2D view index. A separately created private white texture
could potentially identify imported lights if its live index is bound to the
view and compared with that packed index. This is an existing-API candidate,
not an established identity contract: native packing, view-index equality,
resource lifetime/index reuse, all-view binding and ordinary-cookie
non-aliasing require direct tests. The fallback managed pack currently puts
`0xFFFFFFFF` there when it lacks `NativePacked`. The sentinel for an absent
view attribute must never accidentally match the no-cookie index.

Point cookie support in the projection shader is especially uncertain:
`GetCookieUV` divides by the cone tangent while the native-packed point's
cone tangent is zero. A marker branch would have to identify it before and
bypass `GetLightCookie` sampling; a white texture alone is not proof of a
neutral point light. A spot cookie is documented, but tagging consumes its
cookie slot and cannot support an unrelated user cookie simultaneously without
additional design. A private-world `NativeCookieProbe` measured packing
against two live textures and an ordinary-light control; details follow.
No stable light/object ID appears in the inspected GPU light record;
cluster indices are per-view packed slots, and baked indices describe baked
lighting rather than stable dynamic object identities. Even a confirmed cookie
identity would not solve infinite-range culling or physical calibration.

The guarded cookie test passed 1/1 on Engine `e66560df-8eba-4c30-998a-a0296a91e4af`
and glTF `56e0e078-1fed-49a1-a3fc-86f4516b0cd0`, with no cleanup errors.
Receipt `Temp/gltf-validation/direct-gpu/point-native-cookie-run-01/probe-01-editor_direct_gpu_tests/receipt-01.json`
SHA-256 `49670f6a6c20aa3932e8d0c2e8ac051d22de752ddd238f437fe87535e56ee308`.
The private marker's view index was 2556, matching the packed point and spot
cookie indices; no-cookie was `0xffffffff`. A second live cookie used index
2558 and its ordinary spotlight remained unchanged. The marker's index stayed
2556 across 34 editor frames and first-light deletion; clearing, replacing and
restoring the other light's cookie packed the expected identities. Both marked
lights acquired the cookie-enabled flag; the point's cone tangent was zero.
This supports an identity candidate for the measured producer and lifetime,
not a rendered shader-attribute proof or a general lifetime guarantee. A
subsequent isolated shader test must distinguish the marker from ordinary
white and colored cookies, ensure the point marker bypasses cookie sampling,
verify view-level binding and disposal without index reuse, and test ordinary
lights in the same view. It is not permission to claim extension support.

The isolated anonymous shader test subsequently passed 1/1 on the same
Engine/glTF producer, with no cleanup errors. Receipt
`Temp/gltf-validation/direct-gpu/point-cookie-shader-run-03/probe-01-editor_direct_gpu_tests/receipt-01.json`
SHA-256 `f7cac5c56614abe1e74e07404ba233d65f46ff32938e4e01461d973a41655e83`.
It compares a per-camera marker attribute with each clustered light's packed
cookie index, clears the cookie flag on a local light copy before calling the
stock `ComputeDirectLightingForLight`, and applies a diagnostic ×2 color
factor. Point output was 3.958984 without the cookie and 7.917969 when marked;
spot output was 5.40625 and 10.8125. The same marked spotlight in a second
camera with the marker disabled produced 3.958984. Unrelated white and red
cookies agreed across cameras, restored bindings reproduced their readings,
and deleting the light produced zero. Shadows were disabled. This supports
per-camera identity and bypass for these controlled views; it does not prove
physical light units, enabled shadows, all camera paths, long-term index reuse,
unbounded-range culling or public glTF light lifecycle. The earlier attempts
`08ac1a6be8fc13710aa8248f55024fad0414eb1c1034bf7273f43f444d6a4191` and
`4071985ac244d1506ff46707829e0c7af1b7fc031228cf23e5f142f69a7e9f00`
failed at anonymous shader parsing and compilation respectively, before pixels;
they remain recorded separately.

Finite imported range can travel in existing `Radius` in inches, but an absent
glTF range means unbounded, not a large arbitrary radius. Both clustered shader
`LightBounds` and the managed light binner use `Radius` for culling, while the
native binner is not in this repository. A tag and shader math alone do not
solve admission of an unbounded light to all relevant clusters, nor prove
shadow projection behavior. A finite proxy based on a camera or world bound is
only an approximation until its correctness is established for every view.

Smallest candidate, contingent on evidence: per-instance glTF light factory
owns ordinary scene lights; a reserved, documented native-to-GPU tag plus an
unbounded-range representation reaches both light binners; the tagged shader
branch computes photometry and cones while leaving normal lights intact. The
existing generated native flag setters might supply the first bridge if native
packing preserves a demonstrably safe bit; if not, a native producer interface
or an equivalent collision-free per-light side channel is missing. A custom
material-only light loop does not illuminate ordinary scene receivers or
automatically preserve native shadow integration, so it is not a substitute
for the full selected extension. No native C++ change is proven necessary
until the binding and culling alternatives have been tested.
