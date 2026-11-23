# Editor startup work reduction

Date: 22 October 2026. Base: `cca2bf927c79f4c1b8d60e46fdbac15a1123c6b8`.

## Changes

- Conditional C# source is wrapped before parsing. The compiler parses it once and reuses the final tree when the source, directive, and parse options match. Disabled text remains excluded from client archives. No second tree cache is added.
- Razor source preparation runs in the archive worker before the compiler waits for dependency assemblies. Reference resolution, access checks, and assembly emission keep their required order.
- Completed archives serialize on at most two workers. A pair must fit a 16 MiB UTF-16 source budget; larger archives run alone. This is a work estimate, not a bound on total JSON and compression allocations. Publication keeps its original order and caller context. Batches release serialized buffers after publication. A changed project or compiler cannot receive a stale output.
- Native type initialization preserves custom resource bindings and icons. Custom icon callbacks still run on custom-type updates, because they can use external state.
- Remove the duplicate material preload call, the unused reference-set check, the second compile-group build check, the repeated deleted-asset filter, and the impossible null result check after `File.ReadAllBytes`. The remaining material preload call is inside its timer. File-read exceptions retain their existing behavior.

## Measurement method

Use `Temp/startup-work-audit/Measure-WarmStartup.ps1`. Each run starts one editor with `-mcp -startup-profile`, opens the saved `scenes/minimal.scene`, and leaves play stopped. Existing caches remain. The same three saved scene tabs remain open. The user approved saving Meadow Look before the tests.

Ready is the first of 15 stable editor-status responses: correct project and scene, stopped, saved, compiler idle and successful, and no change in the on-demand compile count. Memory counters are sampled about every 250 ms, including while the status process runs. Settled memory is read 30 seconds after the stable-state check. Private bytes and working set are process counters, not GPU memory. No forced collection or heap dump is used.

The first run (`baseline-1`) is excluded from the repeated warm comparison. It had much slower native I/O, and the editor entered Lost play before the next restart could complete. The exact start of that external action is not known, so its 178.150-second ready time and 9.09 GB settled private memory are not used. A first attempt to start `baseline-2` stopped when normal close did not finish within 30 seconds. The game was then stopped through the editor before the repeat. `IGameInstance.IsLoading` is not a valid editor-ready gate: an idle editor creates a game instance but defers its first scene until play, so that flag remains true.

## Validation and results

The Developer build passed. The first build found missing disposal in the new test factory callers; those callers now use `using`. The repeat build passed. Native artifacts remain at `468c55e88878021c33daa83d403302b57ce5a3f4`.

The focused SboxBuild test run passed 188 tests and skipped one. It covers the compiler suites, the custom resource binding regression, and synchronization-context checks. All 16 new test cases passed. The filter was `(FullyQualifiedName~CompilingTests|FullyQualifiedName~AssetTypeInitializationTests|FullyQualifiedName~CustomType|FullyQualifiedName~AsyncPump)&TestCategory!=LiveBackend&TestCategory!=Slow`, with `--no-build` after the successful build.

| Run | Ready (s) | Project compilation stage (s) | Native checks (s) | Settled private GB | Settled resident GB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline 2 | 96.913 | 42.282 | 7.247 | 8.850 | 6.491 |
| Baseline 3 | 88.869 | 37.716 | 6.544 | 8.924 | 6.531 |
| Candidate 1 | 100.563 | 40.495 | 7.412 | 8.928 | 6.581 |
| Candidate 2 | 91.085 | 36.410 | 7.192 | 8.955 | 6.608 |

GB here means 1,000,000,000 bytes. The project compilation stage includes publication and dependency waits; it is not pure compiler CPU time. Both baseline runs loaded 3,164 resources and had zero on-demand recompiles. A later counter read found about 1.95 GB of managed objects, with 2.76–2.83 GB of managed GC committed memory. These counters do not identify the owner of the remaining private memory.

The two-run means are:

| Metric | Before | After | Observed change |
| --- | ---: | ---: | ---: |
| Stable editor ready | 92.891 s | 95.824 s | 2.933 s slower |
| Project compilation stage | 39.999 s | 38.453 s | 1.547 s faster (3.9%) |
| Settled private memory | 8.887 GB | 8.942 GB | 0.055 GB more (0.6%) |
| Settled resident memory | 6.511 GB | 6.595 GB | 0.084 GB more (1.3%) |
| Sampled peak private memory | 9.067 GB | 9.091 GB | 0.024 GB more |

These samples do **not** establish a full-startup or memory improvement. The compilation-stage mean is lower, but the ranges overlap and the other startup stages vary. CPU time through the end of sampling also increased from a mean of 379.6 to 389.3 seconds. The source changes remove proven duplicate work and preserve tested behavior; do not claim a measured whole-editor speed gain from them. A two-run sample is too small to attribute the small differences to one change.

All four compared runs had zero on-demand recompiles, the same resource discovery counts (88,886 files, 17,647 indexed paths, 3,164 loaded resources), and successful compilation. Project C# and Razor file paths, lengths, and write times stayed fixed. The final editor has the saved Minimal scene active, play stopped, 498 tools, and zero compile errors. Existing missing `combat.armor_c`, missing `sounds/tools/sfm/denyundo.vsnd_c`, and the grass material's missing transmissive texture appear in baseline and candidate logs; they are not new failures from this change.

Format verification failed with 592 existing `ENDOFLINE`, 66 `WHITESPACE`, and one `FINALNEWLINE` error. No diagnostic names a file changed by this task. `git diff --check` passed. Unrelated formatting was not changed.

The memory read after candidate runs found about 1.98–1.99 GB of managed objects and 2.80–2.87 GB of managed GC committed memory. This change does not implement demand loading of all editor resources. It keeps resource load and initialization rules intact. Reducing the remaining process memory needs allocation ownership evidence, including native resources; the process counters cannot supply that attribution.

Raw logs, counters, binary hashes, and timing JSON are under `Temp/startup-work-audit/`. `comparison.json` holds the unrounded results. No heap dump or forced collection was taken.

No commit or push is part of this task. The separate motion-blur shader change is preserved.
