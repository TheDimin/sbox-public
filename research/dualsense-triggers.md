# DualSense adaptive trigger support

Research date: 2026-10-22. This report records source and review evidence. It does not promise pull request acceptance.

## Maintainer position

The strongest evidence is [issue #5174, Controller Vendor Features](https://github.com/Facepunch/sbox-public/issues/5174). It is closed. Facepunch member DevulTj started it to discuss controller features, hardware differences, and the public haptic API. The discussion includes adaptive trigger resistance as a separate feature from Xbox trigger vibration.

In the [final maintainer reply](https://github.com/Facepunch/sbox-public/issues/5174#issuecomment-3799153248), DevulTj said DualSense features had low priority. The stated costs were support for one vendor and one controller, plus an extra dependency outside SDL. The reply also said SDL would not support the features. Treat this as the maintainer position at the time of that reply, not as proof of current SDL capability.

This is a material acceptance risk. A small patch and passing tests cannot resolve a feature-priority decision. A proposal must explain whether the current SDL route removes the extra dependency, then seek agreement on scope before a full implementation. This is a recommendation based on the evidence, not a mandatory approval step in the repository rules.

The [contribution guide](https://github.com/Facepunch/sbox-public/blob/master/CONTRIBUTING.md) says new features should have demand and ideally a proposal discussion. It also asks for small changes, relevant commits, and tests where useful. The [PR template](https://github.com/Facepunch/sbox-public/blob/master/.github/PULL_REQUEST_TEMPLATE.md) asks for motivation, implementation details, public API documentation, and tests. It states that a PR can be rejected or require changes.

## Pull request evidence

The table separates a GitHub merge from an upstream merge. In sbox-public, a closed PR with `merged: false` can still have an sboxbot comment that confirms an upstream merge. Do not classify those PRs as rejected from the merge flag alone.

| Change | Verified state | Relevant evidence and limit |
|---|---|---|
| [sbox-public #12016: Remove per-frame allocations in the input system](https://github.com/Facepunch/sbox-public/pull/12016) | Closed; [bot confirms upstream merge](https://github.com/Facepunch/sbox-public/pull/12016#issuecomment-5989564373) on 2026-10-05 | The change preserves input behavior and removes repeated allocations. The PR gives allocation measurements. This supports a design that adds no repeated allocation to input processing; it does not establish a formal allocation rule. |
| [sbox-public #10785: WorldPanelInput uses the appropriate action](https://github.com/Facepunch/sbox-public/pull/10785) | Closed; [bot confirms upstream merge](https://github.com/Facepunch/sbox-public/pull/10785#issuecomment-5023390865) on 2026-07-20 | A narrow controller/remapping fix links an issue and keeps existing mouse behavior. There is no detailed public review. |
| [sbox-public #11218: Camera ignore TagSet](https://github.com/Facepunch/sbox-public/pull/11218) | Closed; [bot confirms upstream merge](https://github.com/Facepunch/sbox-public/pull/11218#issuecomment-4908128796) on 2026-07-07 | A small public property solves a specific game need and uses an existing naming pattern. The PR includes a sample and video. It is an API example, not a controller feature approval. |
| [sbox-public #11930: Headless project compile command](https://github.com/Facepunch/sbox-public/pull/11930) | Closed without a GitHub merge | [Maintainer reply](https://github.com/Facepunch/sbox-public/pull/11930#issuecomment-5909544222) says the feature can make sense, but needs more thought and is likely work the team wants to do itself. Useful features and extensive validation do not guarantee acceptance. |
| [sbox-public #12064: Steam Deck Game Mode input](https://github.com/Facepunch/sbox-public/pull/12064) | Open | Describes a tested hardware/focus failure and a bounded change. No public comments were returned. It is an example of test evidence, not accepted design. |
| [sbox-public #12138: Display selection](https://github.com/Facepunch/sbox-public/pull/12138) | Open | Uses SDL-related display changes. No public comments were returned. It is not evidence that new SDL API work will be accepted. |
| [Facepunch.Steamworks #866: Haptics and bind screen](https://github.com/Facepunch/Facepunch.Steamworks/pull/866) | Merged on 2026-09-15 | Adds thin controller methods for vibration, simple haptic events, LEDs, motion data, and the binding panel. It documents value ranges, unsupported-device behavior, and stop/reset methods. The patch does not add adaptive trigger resistance. No public review comments were returned. |
| [Facepunch.Steamworks #864: SteamInput session lifecycle](https://github.com/Facepunch/Facepunch.Steamworks/pull/864) | Merged on 2026-08-20 | Places initialization, shutdown, and cached-handle reset inside the existing interface lifecycle. This supports engine-owned cleanup as a design example. It is a separate repository with a different API. |

The public reviews endpoint returned no reviews for #12016, #10785, #11218, and #11930. The table therefore does not infer detailed reviewer preferences from absent reviews. These examples are a selected sample, not an acceptance-rate study.

## Search scope and related reports

GitHub connector searches covered Facepunch PRs for `DualSense`, `haptic`, `controller`, and `adaptive trigger`, then sbox-public PRs for `gamepad`, `rumble`, and `SDL`. No PR that directly implements DualSense adaptive triggers was found. This is a search result, not proof that no private or differently named work exists.

Issue searches also found the following relevant records:

- [#11890: PS5 glyph shows as Xbox](https://github.com/Facepunch/sbox-public/issues/11890), open when checked. The reporter used USB with Steam Input enabled. This is a reported device-identification case to include in testing, not a verified current defect in this checkout.
- [#11891: Control gamepad color](https://github.com/Facepunch/sbox-public/issues/11891). This is related vendor-feature demand. Keep LED work outside an adaptive-trigger PR.
- [#9833: Steam Input support](https://github.com/Facepunch/sbox-public/issues/9833), closed. [Garry's reply](https://github.com/Facepunch/sbox-public/issues/9833#issuecomment-3799417715) states that controller support already exists. Do not frame the proposal as a new controller system.

## Upstream source check

The current upstream [Controller.cs](https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Systems/Input/Controller/Controller.cs) resolves a gamepad by device ID through `Sdl.GetGamepadFromID`. It uses `Sdl.RumbleGamepad` and `Sdl.RumbleGamepadTriggers`. The returned file blob SHA was `ed5ab01ac2c7b095c1611799d3b0f1c0f4c0f97f`.

The public [commit dbd9f4413550dc0785193212858048f234655361](https://github.com/Facepunch/sbox-public/commit/dbd9f4413550dc0785193212858048f234655361) moves SDL input and window ownership to managed code. Its message references `#5858`, but the public PR endpoint for that number returns 404. Cite the commit as evidence, not a public PR review.

## Proposal guidance from the review evidence

These are recommendations, not maintainer-approved requirements:

1. Link #5174 and address the stated dependency and support costs directly. Do not assume its closure means the feature was delivered.
2. Describe the feature as adaptive trigger resistance and effects on supported devices. Keep it separate from normal vibration and Xbox trigger motors.
3. Extend the existing controller path. Do not add a second device manager or a general raw-device API merely to deliver this feature.
4. Propose the smallest complete API, with documented ranges, unsupported-device results, and explicit reset behavior. Do not include touchpad, microphone, speaker, LED, or a new effect editor in the same PR.
5. Include measured hardware behavior, transport details, Steam Input state, cleanup tests, and a short reproducible sample.
6. Make capability checks and lifecycle cleanup part of the initial change. The implementation must remain maintainable without a later rewrite.
7. Ask maintainers whether they want this scope before presenting implementation work as ready for acceptance. No message or issue was posted during this research.

## Evidence limits

This research read public source, issues, PR metadata, patches, and comments. No controller test or code implementation was performed for this report. Hardware support and current SDL transport behavior need separate verification. PR states can change after the research date.

## Technical findings and proposed implementation

This section uses the local checkout at commit b2c97190ea324cea4cfec2de48696467d27d21f7. The checkout contains unrelated changes. No engine code was changed for this research. The agent also checked the current upstream Controller source. "Clean" is interpreted as a small API, one device owner, and no duplicate transport code. "Tautology" is not a defined software design requirement.

### Existing code

- [Input.Haptics.cs](../engine/Sandbox.Engine/Systems/Input/Input.Haptics.cs) exposes rumble through Input.CurrentController. Its trigger parameters are documented for Xbox One.
- [Controller.cs](../engine/Sandbox.Engine/Systems/Input/Controller/Controller.cs) resolves the SDL handle from the device ID on each use. It calls SDL rumble functions. Its PlayStation glyph category includes PS3, PS4, and PS5, so it cannot identify adaptive-trigger support.
- [SdlGamepads.cs](../engine/Sandbox.Engine/Systems/Input/Controller/SdlGamepads.cs) owns connection and handle lifetime. [Input.Controller.cs](../engine/Sandbox.Engine/Systems/Input/Controller/Input.Controller.cs) selects the controller for the player scope.
- [sdl.def](../engine/Definitions/engine/sdl.def) defines the SDL bindings. It has no SendGamepadEffect binding.
- [ControllerTests.cs](../engine/Tests/Sandbox.Test.Engine/Input/ControllerTests.cs) already substitutes native function pointers to test failure and disconnected devices. Extend this test method of control.
- [Build.cs](../engine/Tools/SboxBuild/Steps/Build.cs) skips native compilation in a public checkout. [InteropGen.cs](../engine/Tools/SboxBuild/Steps/InteropGen.cs) generates managed bindings with that restriction. A new definition requires matching native artifacts. A managed compile alone cannot prove the new native call works.

The installed Windows SDL DLL reports version 3.4.14. This is file metadata, not a device test.

### What SDL provides

SDL exposes [SDL_SendGamepadEffect](https://wiki.libsdl.org/SDL3/SDL_SendGamepadEffect), with a success/failure result. Its [controller test](https://github.com/libsdl-org/SDL/blob/main/test/testcontroller.c) demonstrates PS5 trigger effects, including clear, resistance, and vibration.

The [SDL 3.4.14 PS5 driver](https://github.com/libsdl-org/SDL/blob/release-3.4.14/src/joystick/hidapi/SDL_hidapi_ps5.c) rejects trigger-rumble calls but accepts device effect packets. Its effect structure is 47 bytes, with 11-byte right and left trigger fields at offsets 10 and 21. SDL adds the USB or Bluetooth report framing and Bluetooth checksum. The PS5 type can also cover third-party devices. A successful send does not prove a physical trigger moved.

SDL's [enhanced-report setting](https://wiki.libsdl.org/SDL3/SDL_HINT_JOYSTICK_ENHANCED_REPORTS) controls Bluetooth effects. Once enabled, PlayStation controllers need a power cycle to leave that report mode. The [PS5 HIDAPI setting](https://wiki.libsdl.org/SDL3/SDL_HINT_JOYSTICK_HIDAPI_PS5) can disable the driver.

These sources establish a transport route. They do not establish a new capability added after issue #5174, a Sony-supported protocol contract, or acceptance by Facepunch.

### Recommended scope

Propose adaptive trigger resistance only, with an explicit Off state. Add other modes only if the proposal has a real use case and hardware evidence. This is a complete narrow feature, not a temporary implementation. Exclude audio-driven body haptics, microphone control, touchpad input, and a new controller framework.

Suggested API shape for discussion, not an approved API:

```csharp
bool Input.TrySetAdaptiveTriggerEffects(
    AdaptiveTriggerEffect left,
    AdaptiveTriggerEffect right);
```

Use an immutable value type with Off and a validated Resistance factory. Define the start-position and strength ranges and their device quantization. Reject non-finite input and invalid ranges before native access. Default should mean Off. Keep the packet representation private. Check the game-code access rules and compile a game-code example.

Return false when there is no usable controller or the send fails. Document that true means the command was accepted, not that motion was verified. Avoid a broad SupportsAdaptiveTriggers property based only on glyphs or PS5 type. Use a narrowly verified device policy, with vendor/product identification if needed. Decide that policy with the maintainer.

Use the current player's controller. Store effect state on that controller. Send both trigger states together so one call has a clear result. A later call replaces the pair. Keep normal rumble independent. The current TriggerHaptics path calls StopAllVibrations; that method currently forwards to StopAllHaptics. Adjust this interaction deliberately so starting ordinary rumble does not accidentally clear adaptive resistance. Explicit StopAllHaptics must clear both.

### Internal work

1. Add the SDL effect function to sdl.def. Generate the interop output through the build tools. Do not edit generated bindings. Coordinate native artifact generation with Facepunch.
2. Add one private packet encoder and one controller send path. Use the existing SDL handle. Keep USB framing and checksums in SDL.
3. Name packet constants and cite a fixed SDL version. Test the complete packet against independent expected bytes. Start with the effect modes supported by the chosen sources and device tests.
4. Send a clear command when stopping effects. Clear state on game stop, game change, focus loss, and shutdown. On disconnect, discard state and avoid a stale handle. Reset on reconnect before accepting new effects. Do not promise delivery after a physical disconnect.
5. Use the existing frame/lifetime paths. Do not add a timer per effect or a general effect scheduler. If a duration is part of the accepted API, expire it on an engine path that continues when gameplay input is suppressed.
6. Define player opt-out behaviour and test it. Do not force driver hints against user settings. Decide whether an existing preference can cover the feature before adding a setting.
7. Avoid repeated sends for unchanged successful state. A failed send must not mark that state as applied. Clear local state at lifetime boundaries so old effects cannot restart.

These are proposed requirements. Exact game-stop and focus hooks still need implementation review. They must be part of the first shipping change.

### Required validation

| Area | Evidence required |
| --- | --- |
| Encoder | Fixed expected bytes for Off and resistance; correct left/right positions; zero unrelated control flags |
| Input validation | Boundaries, invalid values, NaN, infinity, default Off |
| Native boundary | False return, absent device, disconnect between calls, no stale handle |
| Existing behaviour | Ordinary rumble and LED changes coexist with trigger resistance; Xbox trigger rumble is unchanged |
| Lifetime | Stop, focus loss, editor play exit, game switch, shutdown, reconnect, and player opt-out |
| Player scope | Two controllers; one player's effect does not affect the other |
| Real hardware | DualSense over USB and Bluetooth; firmware and OS recorded; repeated set/clear operations |
| Compatibility | Steam Input on/off; virtual Xbox mapping; PS4, Xbox, and no controller; headless mode |
| Performance | No repeated allocation or packet send for steady state; first-use delay measured |
| Build | Matching native artifacts, then SboxBuild build, test, and format --verify |

Use physical tests to confirm force and release. Native-call substitutes cannot prove those effects. Claim Edge or another platform only after tests on that device or platform. Steam Input may change the device visible to SDL; record the result instead of claiming all configurations work.

No build or hardware tests were run because this task produced research only.

### Concrete proposal draft

**Title:** DualSense adaptive trigger resistance through the existing SDL path

**Problem:** Games cannot express trigger resistance for a bow draw, brake, or similar input. The existing Input.TriggerHaptics API exposes rumble.

**Proposed change:** Add a small typed Input API for left/right resistance and Off. Keep packet encoding internal. Send effects through SDL_SendGamepadEffect on the controller already owned by the engine. Include reset behaviour, failure handling, tests, and a physical-controller demonstration.

**Relation to #5174:** The proposal addresses the extra-library concern by using SDL already present in the engine. It still requires device-specific packet maintenance and serves a limited device set. It does not include the full vendor-feature request.

**Decisions requested from maintainers:** Is this limited feature wanted? Is the typed API suitable? Which controller identification policy and user preference should apply? Can Facepunch provide matching native artifacts for the new binding?

**Delivery:** One focused PR after scope agreement. Reference #5174 and the proposal. Attach exact test results and a short demonstration. Include required cleanup in that PR.

This draft is saved only. No issue, PR, or comment was sent.

## Local hardware result — 2026-10-22

After the research, the user requested a local test. The test tool is saved at
[Test-DualSense.ps1](../engine/Tools/DualSense/Test-DualSense.ps1), with
[run instructions](../engine/Tools/DualSense/README.md).

The sandboxed SDL check returned no controllers. Outside the sandbox, the same
SDL library found a DualSense with vendor/product IDs 054C:0CE6. The user reported
a USB connection. SDL accepted the resistance and release commands. During a
15-second test at strength 70/255, the user confirmed that both triggers applied
resistance and then returned to normal.

This confirms the local SDL transport and physical effect for this controller.
It does not validate Bluetooth, Edge, Steam Input compatibility, or engine lifetime
handling. The tool calls SDL directly in its own process. No engine API or native
binding was changed. Direct SDL calls can therefore support a local device test
without new native engine artifacts; the proposed generated engine binding would
still require matching artifacts.


## Local game API implementation — 2026-10-22

The next local change implements `Input.TrySetAdaptiveTriggerResistance(leftStrength,
rightStrength, duration = 500)`. Strengths are finite values from 0 to 1. Zero
releases a trigger. The return value reports command acceptance. See the
[API usage and limits](../engine/Tools/DualSense/README.md#local-game-api).

The implementation uses three exports from the shipped SDL library and the
existing engine-owned controller handle. It does not change generated interop or
require a new native build. This replaces the generated-binding route proposed
above for the local implementation. Maintainers must still review this binding
choice. Only standard DualSense IDs 054C:0CE6 are enabled.

The change keeps resistance separate from ordinary rumble. It adds expiry,
focus-loss and game-lifetime cleanup, retry of failed release writes, a saved
user preference, and per-player routing. No new public type needs an allow-list
entry. A test compiles a separate game assembly and checks its access permissions.

SboxBuild build --config Developer --skip-native passed in 81 seconds. All 35
focused controller tests passed, including the game-assembly access test. The
scoped format check passed for all 13 changed C# files. Full-repository format verification found unrelated changes that require
formatting; this change does not alter those files. Physical USB effects were
confirmed with the standalone tool. The new API still needs a live editor check.

Final live API check: the rebuilt editor (PID 41724) compiled and ran a call to
`Input.TrySetAdaptiveTriggerResistance(0, 0)` in Controller1 scope. The preference
was enabled. `Input.ControllerCount` was 0, so the method returned false, as
required. The editor stayed out of play mode. The API is loaded and callable;
physical resistance and release through this editor remain unverified because
this session did not detect a controller. The earlier standalone USB test is
separate evidence. Local logs are in `Temp/dualsense-final-build.log`,
`Temp/dualsense-final-tests.log`, and `Temp/dualsense-live-api.json`.

## Editor detection diagnosis — 2026-10-22

The USB device remained connected and healthy in Windows. The standalone shipped
SDL library found one DualSense. In editor PID 45112, SDL initialization and
mapping load both succeeded, but native SDL reported zero joysticks and gamepads.
The process environment `SDL_GAMECONTROLLER_IGNORE_DEVICES` included
`0x054c/0x0ce6`; the process had Steam app ID 590830. SDL uses this list to skip
controller devices. This explains the zero engine controller count without an
initialization or connection-event failure.

The user disabled Steam Input through s&box's normal per-game Steam setting.
The running editor retained the old ignore list, so a normal restart is required
for the next check. No controller was injected, no focus state was changed, and
no engine code overrides Steam's filter.

Reference: https://wiki.libsdl.org/SDL3/SDL_HINT_GAMECONTROLLER_IGNORE_DEVICES

After the normal restart (PID 52824), the ignore list no longer contained the
DualSense. The existing controller path detected one device, and the public API
accepted a zero-strength release. No engine fix was needed for detection.

The game API test then used Controller1 scope and
`Input.TrySetAdaptiveTriggerResistance(70f / 255f, 70f / 255f, 15000)` in the
running minimal scene. The script waited for normal game focus; it did not alter
focus or input suppression. The command returned true. At the check after the
test, adaptive effect state was inactive, and an explicit release also returned
true. The final focus state was false, so this result alone does not distinguish
timeout cleanup from focus-loss cleanup. Physical feel confirmation is pending.
Results: `Temp/dualsense-game-api-test-result.json`. The original saved Lost scene
and play state were restored, with zero compile errors.

The user missed the first physical check. A second run used a 30-second duration
through the same public API and normal game focus. It also returned Accepted=true,
post-test inactive state=true, ExplicitRelease=true, Controllers=1. No scene or
play state was changed for the repeat. Result:
`Temp/dualsense-game-api-repeat-result.json`. Human confirmation is pending.

The user confirmed the repeat game API test: "Yes — both worked and released".
This verifies physical USB resistance and release through the new public game
API, the existing engine-owned controller, and SDL. No source fix, ignore-list
override, synthetic controller, or forced focus was needed. Steam Input remains
disabled for s&box as selected by the user. Bluetooth and DualSense Edge remain
untested. The final focus was false, so automatic timeout and focus-loss cleanup
are not isolated by this physical test; the controller tests cover each path.

## Steam Input support — 2026-10-22

The user asked to keep Steam Input enabled and enabled it in Steam. Valve's
supported API is `ISteamInput::SetDualSenseTriggerEffect`; SDL exposes the matching
handle via `SDL_GetGamepadSteamHandle`. Both required exports and the Steam Input
v006 interface are present in the shipped native libraries.

The implementation adds Steam Input to the existing Steam client interface
lifetime. Controller effects use SDL's exact Steam handle, never an enumeration
index or a second controller list. Steam-owned controllers use Valve's effect
API; unsupported Steam devices do not fall back to direct device output. Shared
expiry, focus, scene, preference and shutdown cleanup remains in Controller.
A virtual-gamepad disconnect releases the stored Steam handle even if SDL no
longer has a gamepad handle. The Steam command layout is 120 bytes, with feedback
strength quantized to eight nonzero levels. Valve's call has no delivery result;
the public bool reports submission, not physical motion.

Validation: SboxBuild Developer build with --skip-native passed in 135 seconds.
All 41 focused ControllerTests/SdlGamepadsTests passed, including six new tests
for Steam layout, routing, ownership changes, unsupported devices, Steam shutdown,
and virtual disconnect. Scoped whitespace verification passed for eight files.

Live editor PID51204 initialized Steam Input. Valve reported one connected
PS5 controller (type13), but gamepad index -1. SDL reported zero controllers.
The Steam controller log confirmed `mapping uses xinput : false` for s&box. The
user has been asked to apply Steam's normal Gamepad layout. No engine override
of Steam's ignore list or synthetic controller was added. Physical validation
with Steam Input enabled remains pending investigation of the missing gamepad.

Follow-up: the user confirms Gamepad is already selected and supplied an image
showing Steam Input enabled for PlayStation. After the requested editor focus
change, Steam logged configuration activation for app 590830, still with
`mapping uses xinput : false`. Thus background focus alone does not explain
the missing mapping. The 45-second live sampler ended before the user's focus
change; its Focused=false, SDL count=0 and slot=-1 result does not establish
the live state during that change. No trigger command was sent. Further live
validation waits for the concurrent wheat editor test to finish.

Primary sources:
- https://partner.steamgames.com/doc/api/isteaminput#SetDualSenseTriggerEffect
- https://wiki.libsdl.org/SDL3/SDL_GetGamepadSteamHandle
- https://github.com/ValveSoftware/Proton/blob/proton_11.0/lsteamclient/steamworks_sdk_155/isteamdualsense.h
