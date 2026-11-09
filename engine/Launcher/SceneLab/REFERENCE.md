# SceneLab reference

SceneLab renders a `RenderWorld` into a panel window, optionally alongside the native scene for parity. See the [renderer README](../../Sandbox.SceneRenderer/README.md) for architecture, development rules and general verification. This reference summarizes SceneLab-specific workflows; inspect current source for current flags and presets.

## Source map

- `SceneLabAppSystem.cs` initializes the app and native modules. `SceneLabWindow.cs` owns the window, rendering and capture. `SceneLabScene.cs` defines presets and arguments; `SceneLabWindow.Game.cs` handles GameObject scenes, their bridge/native captures and change checks.
- `GameScenes.cs`, `MapScenes.cs`, `OutfitScenes.cs`, `TerrainScenes.cs` and `LightModulatorScenes.cs` supply themed presets; `BridgeScene.cs` builds the component bridge case. `HeavyScene.cs`, `DynamicScene.cs` and `OcclusionScene.cs` exercise load, changing worlds and culling.
- `NativeReference.cs` mirrors the managed world and compares pixels. `ParityRun.cs` runs the sweep; `Benchmark.cs` and `FrameTimer.cs` measure it. `PlanOverlay.cs` renders the frame plan. `SceneViewport.cs`, `LodTestModel.cs` and `SkyCube.cs` support viewing and test assets.

## Runs and parity

From `game/`, for example:

```sh
bin/managed/scenelab.exe -scene "All Shadows"
bin/managed/scenelab.exe -scene "Reflections" -capture out.png -native
bin/managed/scenelab.exe -benchmark "10k Boxes;50k Mixed" -novsync
bin/managed/scenelab.exe -parity "Sun Shadows;Reflections"
```

`-parity` runs all or semicolon-separated presets and writes `parity.txt`; failures exit 1. Capture comparisons exit 2 on failure. `-parity-out`, `-parity-timeout`, `-save-native` and `-compare-native` control the sweep and native baselines. Compare saved native frames in the same execution mode: they can differ across one-process and per-preset runs. Timeouts or load exceptions are reported and skipped, not passes.

GameObject captures compare native and managed before and after the preset's change. A new preset uses a `Func<Scene>`, a meaningful `ChangeGame` and a grouped `Presets` entry; disable its feature once and confirm the comparison fails. Panel apps load game-specific resources explicitly and use the window UI system for screen UI.

`-isolated` runs a GameObject preset in the selected renderer; `-native-renderer` saves the native image for comparison. Parity runs isolated presets in fresh native and managed passes separated by idle frames so the render-target pool drops effect history. AO and SSR select this automatically. Auto exposure, random particles, Dynamic and some map presets can vary between separate native runs; compare within the same run and investigate flakes rather than claiming parity from mismatched scenes.

## Useful controls

- `-gamescene builtin` selects the bridge preset; `-gamescene path.scene` loads an authored scene. `-sequence-time` sets a normalized animation pose. `-model`, `-grid`, lights, sun and floor options build a custom scene.
- `-zoom`, `-width`, `-height`, `-capture-frame` and `-debugmode <SceneCameraDebugMode>` control views and captures. Captures freeze lights and read the swap chain or target before panels draw.
- `-target`, `-targetformat`, `-msaa` and `-swapchain-msaa` control targets and samples; compare native at matching settings. `-serialrecording`, `-noprepass`, `-nosizecull`, `-noshadows`, `-debugshader`, `-native-renderer` and `-novsync` affect rendering. `+name value` sets registered convars before scene load.
- `-gpuprofile` adds GPU timings by layer; `-plan [level]` writes the frame plan. For occlusion, use View > Occlusion Culling or the appropriate managed/native convar; compare culling off and on under matched conditions.

Preserve other tasks when a running SceneLab holds managed DLLs. Consult the applicable host guard before expensive builds or runs.
