# Native scene capture controls for the direct GPU backend

This diagnostic precedes the C# GPU-buffer renderer. It does not load glTF through
a native Model, create a native Model/Mesh, compile a shader, or change the global
renderer. The stock sphere is an existing engine resource used only as an
independent native scene visibility control.

## Test contract, written before the probe

`../../engine/Tools/Gltf/editor/editor_native_capture_controls.csx` runs only in the idle physical editor, with
the existing `r_managed_scene` value false. It creates one private editor scene
and a visible `SceneRenderingWidget`, waits for real editor frames, and uses
standard camera capture APIs. It records the producer MVID, global renderer
value, camera size/viewport, frame counters and every image hash.

Each route independently must pass all five captures:

1. A fresh magenta-initialized target/bitmap becomes entirely black in an empty
   scene. Any retained magenta, unrelated object or nonblack pixel fails.
2. The stock sphere produces visible coverage above 64 pixels.
3. After sphere deletion and real frames, a new target is entirely black.
4. A `SceneCustomObject` callback draws a literal stock-material triangle through
   `Graphics.Draw`; visible coverage exceeds 64 pixels and the callback runs in
   an active native render scope without an exception.
5. After custom-object deletion and real frames, a fresh target is entirely black
   and the deleted object's callback count does not increase.

No route inherits another route's controls. Failed images/metrics remain in the
receipt; a black actual or an unmodified magenta target cannot pass. No explicit
camera command-list clear, target readback rewrite, hidden renderer toggle,
manual frame pumping or viewport crop is permitted.

## Source-based hypothesis and paired routes

`CameraComponent.UpdateSceneCamera` sets `SceneCamera.Size` from `ScreenRect`,
which uses `CustomSize ?? Screen.Size`. The direct `RenderToTexture` call does not
replace that size with the destination dimensions. `RenderToBitmap` calls
`SceneCamera.OnPreRender(bitmap.Size)` before native submission. The previous
192-pixel direct native empty capture retained magenta across most of the image.
This suggests a camera/target extent mismatch; it remains a hypothesis until the
controls pass.

The probe preserves the ordinary direct-texture route without a CustomSize as a
baseline, then tests the same public API with the private camera's CustomSize
set to the 192×192 destination, followed by the standard bitmap capture API.
Camera initialization follows the existing widget workflow:
`Scene.PreCameraRender()` and `Camera.InitializeRendering(false)`.

The corrected-size direct route or bitmap route must pass its complete controls
before any native direct-GPU render claim. The baseline remains explicitly
reported even if it fails. A successful route establishes only these native
capture and callback capabilities, not glTF conformance or Model/component API
parity. All execution belongs to the validation coordinator after stage D's
reliability gate.
