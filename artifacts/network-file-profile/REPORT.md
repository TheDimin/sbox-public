# BuildNetworkedFiles retained-manifest report

## Outcome

The editor now retains the complete network-file manifest across ordinary play/stop cycles. An unchanged warm play exits after checking manifest validity and the empty dirty-path collection. It performs no enumeration, metadata lookup, file read, CRC, `AddFile`, table mutation, or watcher installation.

The baseline bottleneck was repeated hashing of 6,734 large files (9,124,294,886 bytes) on every play. ETW attributed about 5.412 seconds of main-thread CPU exclusively to `Crc64.FromBytes`, 9.990 seconds inclusively to `LargeNetworkFiles.AddFile`, and 9.157 seconds inclusively to `BaseFileSystem.GetCrc` during the clean baseline capture.

## Reproduction environment

- Engine baseline/instrumentation commit: `95b7813cfa0366d5ad4a5fd18d928390b0a444eb`
- Optimization commit: `251de0eef048d51b2d74e033169b36abd211db05`
- Project: VallArk, `E:\Projects\survive\survive.sbproj`
- Scene: `survival/scenes/gym.scene`
- Build: Developer
- CPU: AMD Ryzen 7 5800H, 8 cores / 16 logical processors
- OS: Microsoft Windows 11 Home, 64-bit, build 26200
- Project volume: `E:` Data, approximately 2 TB with 564 GB free during capture
- Physical storage inventory: Samsung SSD 970 EVO Plus 2TB, Samsung MZVLQ1T0HBLB 1TB, Crucial CT240BX200SSD1

Cold protocol restarted the editor before each of five measurements. Warm protocol populated the manifest once, then measured ten consecutive play/stop cycles. The scene, project, build, machine, and profiling verbosity were held constant.

## Results

| Scenario | Baseline median | New median | Improvement | Baseline p95 | New p95 | Files scanned before/after | CRC bytes before/after | AddFile calls before/after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Cold first play (n=5) | 28,970.000 ms | 25,737.597 ms | 11.16% | 33,106.201 ms | 28,061.570 ms | 10,394 / 10,394 | 9,124,294,886 / 9,124,294,886 | 7,017 / 7,017 |
| Warm unchanged (n=10) | 31,622.966 ms | 0.0475 ms | 99.99985% | 68,492.271 ms | 0.12515 ms | 10,394 / 0 | 9,124,294,886 / 0 | 7,017 / 0 |
| One small edit | 31,622.966 ms | 14.317 ms | 99.955% | n/a | n/a | 10,394 / 0 | 9,124,294,886 / 0 | 7,017 / 1 |
| One large path touch | 31,622.966 ms | 1.575 ms | 99.995% | n/a | n/a | 10,394 / 0 | 9,124,294,886 / 65,572 | 7,017 / 1 |
| Rename (add + remove notifications) | 31,622.966 ms | 2.206 ms total | 99.993% | n/a | n/a | 10,394 / 0 | 9,124,294,886 / 0 | 7,017 / 1 |
| Ten-play final cycle | 31,622.966 ms | 0.048 ms | 99.99985% | 68,492.271 ms | 0.12515 ms | 10,394 / 0 | 9,124,294,886 / 0 | 7,017 / 0 |

The edit rows compare against the original implementation's unchanged full-rebuild median because the baseline implementation always performed that same full rebuild regardless of the edit.

## Lifetime and accumulation evidence

One `GameInstanceDll` ID (`ec38bc87-0e85-446d-80c5-eb18525a86d1`) served builds 1 through 11. Its constructor ran once and no finalizer ran during the cycles. Small and large tables stayed at 283 and 6,734 entries across stops. This validates `GameInstanceDll` as the editor-session cache owner.

The original path accumulated four managed watchers per play: two config/language watchers before the manifest build and game/transient watchers during it, growing from 4 after the first play to 44 after the eleventh. The optimized path remained at four watchers throughout ten measured warm cycles.

VallArk has `Project.Config.Resources = null`, so include-pattern growth could not be demonstrated dynamically in this project. The parser is now replacement-based and case-insensitively deduplicated; automated tests cover duplicate removal, comments, blank lines, and removal from the current rules.

## Correctness and invariants

- Baseline full-build manifest: `338C3982297079C8396CCC6EA8D198FD63D10D4A6EAF43B98401A1D19744B451`
- Optimized cold-build manifest: the same hash
- Optimized hash after small create/rename/delete, large touch/restore, and transient touch/restore: the same hash
- Entry counts: 283 small and 6,734 large before and after
- Source precedence is transient then game for dirty refresh, matching full-build overwrite order.
- Cache identity is the normalized virtual network-table path.
- Small/large route transitions remove the opposite table entry before setting the new one.
- A dirty path is cleared only after successful processing.
- Project/filesystem/transient-root/resource-configuration changes and the explicit `network_manifest_invalidate` command force full rebuilds.
- `ResetEnvironment` disposes watchers and clears retained state.
- Standalone and dedicated hosts explicitly retain full-rebuild behavior; caching is editor-only.
- Temporary constructor/finalizer diagnostics were removed. Summary profiling logs are emitted only with `debug_network_files`.

## Validation

- `dotnet build engine\Sandbox.GameInstance\Sandbox.GameInstance.csproj -c Developer --no-restore`: passed, 0 warnings / 0 errors.
- Focused network tests: 11/11 passed.
- Full `Sandbox.Test.Unit`: 1,004 passed, 1 skipped, 0 failed.
- The new string-table regression test was first observed red against the instrumentation build, then green after the no-op setter fix.
- Gym launched successfully for every retained cold and warm measurement.

Not fully exercised in this run: a separate joining client, later scene transition, dynamic prefab delivery, live client receipt of changed assets, threshold-crossing files, resource-config reload, and project reload. The underlying complete manifest was proven byte/metadata equivalent, and the relevant routing/invalidation logic is covered or inspected, but these items should remain explicit multiplayer/editor acceptance checks rather than being represented as completed runtime tests.

## Raw evidence

- `baseline-instrumented.log` contains the five cold rows, ten warm rows, lifetime/watcher series, manifest hash, and ETW attribution.
- `after-profile.log` contains the final-commit cold/warm rows, dirty-path rows, manifest hash, and optimized ETW locations.
