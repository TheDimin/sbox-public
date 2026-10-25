# Isolated Linux validation and warm startup — 2026-10-25

Branch: `codex/linux-engine-validation`. Engine/project copies are independent of the running installation. Nothing was pushed.

## Validation

- Full Developer build: **passed**, including the CLI publishing follow-up. Initial build wall time: 7m56.94s, including a 5.81 GB artifact restore; subsequent builds reused those artifacts.
- Full format verification: **passed**, including the final CLI publishing follow-up.
- Full non-LiveBackend suite: **10,692 passed, 605 skipped, 1 failed** across 11,298 tests. Allocation shards all pass when run serially after the other shards.
- The Windows PE export boot test is explicitly inapplicable on Linux. Other platform/graphics skips remain visible in the suite counts.
- Remaining failure: `WalkingOnHeightfieldsPreservesTheRequestedHorizontalHeading(2,225,160,1)`. It reproduces in a focused integration run. Sideways velocity exceeds the unchanged 0.5 threshold at a terrain contact transition; the reported before/after vectors and ground normal are in `results.json`. No assertion was weakened and no native binary was patched. A safe correction across controller and native contact response remains unresolved.

## Fixes committed during validation

| Commit | Change |
| --- | --- |
| `2d6f668e` | Separate measured durations from counters/configuration; record asset-catalog checking time. |
| `557ea2d4` | Invalidate cached ancestor listings after nested directory creation, with regression coverage. |
| `d0d5fc71` | Host-native test paths/libraries, executable compiler permissions, real fixture casing, deterministic tree fixture order and serial allocation measurement. |
| `35bc7e9f` | Match checkout line endings to editorconfig and preserve legacy characters as UTF-8. |
| `a10ab4c7` | Normalize five legacy C# blobs; no non-whitespace content change. |
| `5c7fa5e8` | Publish the CLI with app-relative bundled runtime discovery; verified with DOTNET_ROOT unset. |

## Warm load measurements

These are fresh editor processes after preparation, using a copied Survive/VallArk project and its caches. All three opened `scenes/minimal.scene`, were not in play mode, and returned `LastCompileSucceeded=true`, `LastCompileErrors=0`, `IsCompiling=false` from the recorder-owned PID.

| Sample | Managed startup | Wall time to verified readiness | Shader checks | Reused / compiled | Catalog check |
| --- | ---: | ---: | ---: | ---: | ---: |
| warm-1 | 116.086s | 119.391s | 0.588s | 19 / 0 | 21.115s |
| warm-2 | 87.428s | 90.517s | 0.631s | 19 / 0 | 9.937s |
| warm-3 | 128.404s | 131.236s | 0.656s | 19 / 0 | 22.809s |

Median verified readiness: **119.391s**; range **90.517–131.236s**. This is a small, uncontrolled diagnostic sample: the user's original editor remained running, system load was not held constant, and readiness is observed by one-second polling plus IPC.

The largest recurring stage is **Check Compiled Projects (32.524–58.423s)**, followed by **Load Game (19.088–23.908s)** and asset processing (17.132–29.896s, including catalog checks). Resource discovery loaded 3,070 GameResources in 4.941–6.320s across 87,986 scanned files. Cached shader validation is under 0.7s; shader compilation is not the warm-start bottleneck in these samples. Nested stage times overlap and must not be summed.

Each warm run still has 45 asset compile candidates. The logs report unsuccessful returns, including materials referencing disabled painterly shaders and existing model issues. This profile does not claim those authored assets are healthy, and no game content was changed to hide them.

Preparation took **472.308s managed startup**, compiling all 19 shaders with zero shader failures. Its first CLI readiness probe failed before connecting because the apphost could not find .NET; that failure and the profile were preserved separately. It must not be reported as an editor main-thread timeout.

## Conditions and retained evidence

- NVIDIA RTX 3080 Laptop GPU; Vulkan; Qt xcb. Per-child overrides: `SDL_VIDEO_X11_EXTERNAL_WINDOW_INPUT=1`, `SDL_VIDEO_X11_XINPUT2=0`, and the isolated bundled HarfBuzz preload.
- The old manifest flattened SDL aliases. Only the isolated copies were relinked after matching payload hashes; original wrappers and SDL files were not edited.
- Source/project revision, counters, per-stage milliseconds, readiness responses and test counts are in [results.json](results.json). Exact startup JSON and status responses are committed beside it.
- Raw build/test/format logs, timing/resource-use reports, profiling scripts and per-run managed/stdout logs remain in the attached worktree at `validation-results/`. No binaries or large raw logs are committed.
- Final hash comparison: the three checked native libraries match the original baseline. Original `sbox-dev`, `Sandbox.Engine.dll` and `Sandbox.Tools.dll` were replaced at 13:43–13:50 UTC (20:43–20:50 local), after all warm samples ended around 13:13 UTC. All recorded build/launch commands here target the isolated tree; global immutability of the original files across other activity cannot be certified.

The final CLI `--help` smoke test also passed with `DOTNET_ROOT` and `DOTNET_ROOT_X64` unset. Build/test/format wall time and peak resident memory are recorded in `results.json` under `validation_runs`.
