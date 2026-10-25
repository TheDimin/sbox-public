# Committed main versus validation branch: warm startup, CPU and RAM

Measured on 2026-10-25. The incremental branch does not demonstrate a substantial performance improvement in this workload. Idle CPU is close; proportional RAM is within ordinary launch-to-launch variation. Merge value is Linux correctness, reliable CLI runtime discovery, more accurate diagnostics, and reproducible validation. Do not justify this merge as a CPU/RAM optimization.

Main `3a9b4eb11ddc5d0c92b6b952e5017be65f16a356` already contains the artifact symlink packaging and startup/import progress changes. Candidate `ca8f176b6f55ff216e04dceedfd8b898f64a3a90` adds the subsequent validation fixes. This comparison measures only that increment, and excludes all uncommitted input/viewport changes from the original checkout.

## Results

Three measured launches per variant, after one preparation launch each. Table values are medians (minimum–maximum); negative candidate deltas mean lower. Small samples are descriptive, not proof of statistical equivalence or a causal saving.

| Metric | Main A | Candidate B | Candidate delta | Unit |
|---|---:|---:|---:|---|
| Warm readiness | 75.36 (71.59–75.37) | 77.00 (75.11–84.78) | +2.17% | s |
| Startup process CPU | 293.80 (288.02–311.02) | 294.93 (294.74–295.94) | +0.38% | CPU s |
| Startup peak RSS | 4522.21 (4483.41–4731.20) | 4522.13 (4474.14–4635.61) | -0.00% | MiB |
| Idle process CPU | 59.46 (59.21–60.69) | 58.85 (58.68–59.07) | -1.03% | % of one core |
| Idle proportional RAM (PSS) | 5474.30 (5470.03–5753.27) | 5461.67 (5448.84–5773.34) | -0.23% | MiB |
| Idle resident RAM (RSS) | 5698.43 (5670.35–5973.26) | 5670.08 (5649.27–6007.82) | -0.50% | MiB |
| Managed heap snapshot | 1838.10 (1832.00–1855.40) | 1829.10 (1820.70–1842.30) | -0.49% | MiB |

| Run | Ready, s | Startup CPU, s | Idle CPU, % of one core | Idle PSS, MiB |
|---|---:|---:|---:|---:|
| A-1 | 75.36 | 293.80 | 59.46 | 5753.27 |
| B-1 | 77.00 | 294.74 | 58.85 | 5461.67 |
| B-2 | 75.11 | 295.94 | 58.68 | 5448.84 |
| A-2 | 75.37 | 311.02 | 59.21 | 5470.03 |
| A-3 | 71.59 | 288.02 | 60.69 | 5474.30 |
| B-3 | 84.78 | 294.93 | 59.07 | 5773.34 |

Startup CPU counts the engine process's user and system time across all its threads, excluding compiler subprocesses. Idle CPU uses the actual process CPU delta over 45 sampling intervals (about 48 seconds including collection overhead): 100% means one logical core, not the whole 16-thread machine. PSS divides shared pages among processes; RSS counts each process's resident shared pages in full. RAM values are the median of one-second samples during each idle window (collection overhead included), followed by the median across launches. Managed heap values are end-of-window snapshots, not retained-object measurements.

## Method and controls

- AMD Ryzen 7 5800H, 16 logical CPUs; NVIDIA RTX 3080 Laptop Vulkan rendering; native libraries copied independently from the same runtime and verified with recorded hashes.
- Separate engine builds, outputs, project snapshots and caches for A/B. Both Developer builds passed. Only the benchmark FPS settings differ from each variant's committed tracked sources.
- Same copied VallArk project (`9e1a231aa0a6da4400e1ba952558e92f1dbdbaf2`), same `scenes/minimal.scene`, editor mode, no play session, no interaction. One recorder-owned project snapshot is temporarily moved to the same logical project path for each launch, preserving shader cache provenance; the pre-existing recorder-owned snapshot is restored afterward. The original user project is never moved.
- Private KWin virtual compositor and Xwayland, 1280×720 virtual output. These are controlled editor idle measurements, not an interactive desktop or gameplay benchmark. The compositor's own CPU and memory are excluded. Both variants report the same 30 FPS limits, VSync and display mode. NVIDIA selection is confirmed by launch logs.
- Preparation A/B excluded. Measured order `A1 B1 B2 A2 A3 B3` reduces order bias. No builds/tests/copies run during measurement. One-second `/proc` sampling records process CPU, RSS, anonymous RAM, PSS, private pages and host load average.
- Readiness requires a fresh startup profile and a successful read-only `editor_status` call to the exact child PID, correct engine/project paths and scene, no playing, and successful managed compilation with zero errors. Wait 15 seconds, then sample idle for 45 one-second intervals, using actual elapsed time for CPU rates. Collect read-only `perf_cpu` and `perf_memory` afterward, without requesting GC or allocation tracing.
- Both isolated runtimes have matching repaired SDL library aliases. A common known-good CLI observes both variants so the baseline CLI's runtime lookup issue does not prevent measurement. Packaging/CLI reliability improvements are therefore outside the timed comparison.
- The launcher stops only its own editor child/process group. No original checkout binary was rebuilt or replaced, and no signal was sent to a pre-existing editor. During desktop preparation the pre-existing display/editor disappeared through external activity; all six reported measurements used the subsequent private compositor.

## What still costs time and memory

Median stage seconds below overlap and must not be summed:

| Startup stage | Main | Candidate |
|---|---:|---:|
| Load Project: Check Compiled Projects | 31.02 | 29.78 |
| Load Project: CompileAllAssets | 13.55 | 15.43 |
| Load Project: Load Game | 13.78 | 14.09 |

All six runs reused 19/19 shaders with zero shader compilations. Both versions still attempt the same 45 asset imports that return false because the copied project has missing/invalid dependencies; those unsuccessful attempts recur, so this is a warm shader/cache benchmark with project asset errors, not a completely compiled asset set. Managed compilation succeeds in all samples. No import policy was changed or errors suppressed.

The best next performance investigations are retained compilation/type-library memory and GC heap fragmentation, repeated unsuccessful asset dependency work, and editor/render work while idle. Managed heaps and committed GC space are recorded separately in each memory snapshot; matching resource counts do not imply matching GC commitment. Single end-window allocation rates and CPU scopes are short snapshots and are not substitutes for the full-window process CPU measurement. GPU memory usage was not reliably available and is not claimed here.

## Excluded attempts and validation limits

Two baseline preparations blocked in SDL `X11_ShowWindow` waiting for a map event on the inherited desktop display. A window-sync setting retry did not clear the wait. A subsequent desktop baseline preparation completed, but the candidate's X connection disappeared during its idle window; all desktop preparation results were excluded from the six-run comparison. The measured private-display runs use the ordinary SDL synchronization setting. See [SDL's window-show implementation](https://github.com/libsdl-org/SDL/blob/release-3.4.14/src/video/x11/SDL_x11window.c) and the local raw logs for the mapping diagnosis.

This performance study does not change the previous validation result: one terrain-contact engine test remains failing; see the [validation report](../linux-engine-validation-2026-10-25/README.md). Tests and format were not repeated for this data-only report. There is no demonstrated gameplay performance improvement, no cold-import benchmark, and no long-session memory/leak conclusion. More runs and allocation/retention profiling would be needed to attribute small changes causally.

## Recorded data

`summary.json` contains aggregate metrics and each value. `measured-records.json` includes all six startup profiles, readiness states, shader/asset outcomes, CPU scopes and memory/resource snapshots. `*.process-samples.json` contains the one-second raw process samples. Preparation and excluded attempt records are separate. `environment.json`, `native-hashes.json`, `benchmark.py`, and `summarize.py` document the controls and collector. Scripts use fixed recorder-owned paths and require the prepared independent snapshots and display; they are not general-purpose launch commands.

Full build/launch/managed logs and excluded desktop baseline data remain locally in `validation-results/ab-comparison/`; logs are not committed because they contain unrelated project diagnostics. No push or merge was performed.
