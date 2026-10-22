# Local DualSense trigger test

Run this tool with PowerShell 7 from the repository root. It uses the SDL3 DLL in
this checkout. It does not require a running s&box editor or a native engine rebuild.

List connected controllers:

```powershell
pwsh -NoProfile -File engine/Tools/DualSense/Test-DualSense.ps1
```

Hold the controller, then run a bounded resistance test:

```powershell
pwsh -NoProfile -File engine/Tools/DualSense/Test-DualSense.ps1 -Test -Seconds 15
```

Use `-Trigger Left` or `-Trigger Right` to test one trigger. The other trigger is
released. The default is `Both`. `-Strength` accepts 1 to 110; the default is 70.
`-Seconds` accepts 1 to 15. The tool tests only the first standard Sony DualSense
with USB vendor/product IDs `054C:0CE6`. Other devices are listed but not changed.
Use `-Library` to select another SDL3 DLL. Start a new PowerShell process when
changing libraries.

The tool sends an explicit release in `finally`, then gives SDL time to send it
before it closes the device. A forced process termination or a disconnected device
can prevent release. If resistance remains, disconnect and reconnect the controller.
Do not run this test while a game is also controlling trigger effects.

SDL's return value confirms command acceptance. The user must check physical
resistance and release. A sandbox can hide the controller; run the command from a
normal local terminal if the list is empty. Administrator rights are not required.

## Verified result

On 2026-10-22, Windows with the checkout's SDL 3.4.14 found a DualSense over USB.
The effect and release calls succeeded at strength 70. The user confirmed that
both triggers applied resistance during the 15-second test and returned to normal.
Bluetooth, DualSense Edge, and interaction with Steam Input were not tested.

The script is a device test tool. It does not establish that an upstream pull
request will be accepted. The packet layout follows SDL's
[controller test](https://github.com/libsdl-org/SDL/blob/main/test/testcontroller.c).

## Local game API

The local engine now exposes resistance through `Sandbox.Input`:

```csharp
// Apply left/right resistance for one second. Call on the main thread.
bool accepted = Input.TrySetAdaptiveTriggerResistance( 0.25f, 0.5f, duration: 1000 );

// Release both triggers, without stopping ordinary vibration.
Input.TrySetAdaptiveTriggerResistance( 0, 0 );

// Stop both ordinary vibration and trigger resistance.
Input.StopAllHaptics();
```

Strength is from 0 to 1. Direct SDL effects use an 8-bit value; Steam Input uses
eight nonzero feedback levels. Zero releases that
trigger. Resistance starts at the beginning of the trigger travel. Both strengths
must be finite. Duration must be positive; its default is 500 milliseconds.
An invalid argument throws `ArgumentOutOfRangeException`.

The method returns false if the device is unavailable or unsupported, the user has
disabled the feature, the game lacks focus, input is suppressed, or SDL rejects the
write. A zero-strength release is allowed without focus and when disabled. True
means command submission, not measured physical motion. Steam Input has no
delivery acknowledgement. Ordinary rumble is
independent. The API does not simulate resistance on Xbox or PS4 controllers.

Each call replaces both trigger strengths. Repeating the same strengths extends
the duration without sending another effect packet. Use `Input.PlayerScope` to
select a local player:

```csharp
using ( Input.PlayerScope( Input.PlayerIndex.Controller2 ) )
{
    Input.TrySetAdaptiveTriggerResistance( 0, 0.25f, duration: 500 );
}
```

The default scope uses the first controller. The keyboard/mouse scope has none.
Effects release on timeout, focus loss, game shutdown, `Game.ChangeScene`, or
`Input.StopAllHaptics`. A cancelled game context also releases its effects.
The SDL event loop handles expiry even when game input is not processed. A failed
release is retried at 250 ms intervals while the controller is connected. A forced
process termination or physical disconnect cannot guarantee delivery.

Steam Input uses the handle SDL supplies for the selected controller. The effect
goes through Valve's `SetDualSenseTriggerEffect` API. The existing Steam client
owns initialization and shutdown; there is no second controller list. Removing
a Steam virtual gamepad also releases its effect on the physical controller.

Steam must expose a gamepad for `Input.PlayerScope` to select it. Use a gamepad
layout in Steam's s&box Controller Layout. A layout that only sends keyboard and
mouse input does not provide a gamepad. The local Steam log reports
`mapping uses xinput : false` and gamepad index -1, although the user confirms
the Gamepad template is selected. The cause of this mismatch is not yet known.

The saved user console setting is `controller_adaptive_triggers 0` to disable
resistance, or `controller_adaptive_triggers 1` to enable it. Game code can read
`Preferences.AdaptiveTriggers` but cannot change it. Disabling releases an active
effect on the next SDL event-loop update.

Build and restart the engine before using this API. The engine code does not
hotload with game code. The internal binding loads four exports from the same
shipped SDL library and uses the controller handle owned by the engine. It does
not change generated interop, add a dependency, or create another device manager.
This binding choice requires maintainer review before an upstream PR.

Direct SDL support is limited to standard Sony DualSense IDs `054C:0CE6`.
The Steam path accepts controllers that Steam identifies as PS5 controllers.
DualSense Edge and third-party PS5 controllers have not been physically tested.
USB physical effects were confirmed with the script. Bluetooth remains untested.

If Windows detects the controller but the editor reports zero controllers, check
Steam Input. During the local test, Steam put `054C:0CE6` in the editor process's
`SDL_GAMECONTROLLER_IGNORE_DEVICES` list. In Steam, use s&box > Properties >
Controller > Disable Steam Input, then restart the editor to use direct SDL
effects. To keep Steam Input enabled, apply a gamepad layout and use the Steam
path described above. The engine respects Steam's device filter.

Validation: the managed SboxBuild build passed with the existing native binaries.
All 41 focused controller tests passed, including Steam routing, cleanup,
SDK layout, compilation and access checks for a separate game assembly. The
eight C# files changed for Steam support passed scoped whitespace
verification. After disabling Steam Input and restarting normally, the rebuilt
editor detected one DualSense. The public API accepted resistance at 70/255 on
both triggers. In the repeat test with a 30-second duration, the user confirmed
that both triggers resisted and returned to normal. The post-test check found no
active effect, and a final explicit release succeeded. The test used normal game
focus. Focus was lost by the final check, so this physical test does not isolate
timeout cleanup from focus-loss cleanup; the automated tests cover both paths.

Steam Input physical validation is blocked by missing gamepad enumeration. The
rebuilt engine initialized Steam Input and identified the connected PS5
controller through Valve's API; Steam exposed no gamepad to SDL. After the user
focused the editor, Steam logged activation of app 590830 and still reported
no XInput mapping. A separate timed sampler ended before that focus change,
so it did not capture the corresponding live SDL state.
