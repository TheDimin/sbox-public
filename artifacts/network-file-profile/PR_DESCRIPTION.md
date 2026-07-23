# Retain editor network-file manifests across play sessions

## What was slow

Every editor play rebuilt the complete client network-file manifest. VallArk's gym scene enumerated 10,394 files, read 283 small files, and recalculated CRCs for 6,734 large files totaling 9.124 GB. The ten-run warm median was 31.623 seconds. ETW identified the repeated large-file CRC path as the dominant cost.

## What changed

`GameInstanceDll` now owns an editor-lifetime manifest keyed by canonical virtual paths. Game and transient watchers are installed once and only mark affected paths dirty outside play. A warm unchanged play returns immediately. A changed path resolves the winning source, reevaluates eligibility and small/large routing, updates only changed payloads, and handles deletion and route transitions.

Resource include rules are replaced from the current normalized/deduplicated configuration instead of appended. Table setters avoid marking byte-identical payloads changed. Large-file requests retain the source filesystem that supplied the manifest entry, including transient files.

Full rebuilds remain the fallback for first build, project/filesystem/transient-root/resource-rule changes, environment reset, and explicit debug invalidation. Standalone and dedicated hosts continue to use full builds.

## Invariants

- The complete small/large manifest contract is preserved.
- Network identity remains the normalized virtual path.
- Transient files retain precedence over ordinary game files.
- Existing live-host hotload refreshes only the affected path.
- Dirty paths remain pending after unsuccessful processing.
- Watchers and manifest state are disposed on environment reset.

## Measured result

Same machine, VallArk revision, gym scene, Developer build:

- Cold n=5: 28.970 s -> 25.738 s median (11.16% faster; no regression).
- Warm unchanged n=10: 31.623 s -> 0.0475 ms median (99.99985% reduction).
- Warm operational counters: 0 scans, 0 reads, 0 CRCs/bytes, 0 `AddFile` calls, 0 table changes, 0 new watchers, 0 dirty paths.
- One large path: one 65,572-byte CRC, no project scan, 1.575 ms.
- Manifest hash before and after: `338C3982297079C8396CCC6EA8D198FD63D10D4A6EAF43B98401A1D19744B451`.

## Tests

- Developer build: passed with zero warnings/errors.
- Focused networking tests: 11/11 passed.
- Full unit suite: 1,004 passed, 1 skipped, 0 failed.

See `artifacts/network-file-profile/REPORT.md` for the protocol, raw-log links, lifetime evidence, ETW attribution, and remaining manual multiplayer acceptance checks.
