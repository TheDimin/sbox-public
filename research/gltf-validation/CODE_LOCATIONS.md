# Where the glTF code lives

On 2026-11-08 all code moved out of `research/`:

- glTF validation tooling: `engine/Tools/Gltf/` (editor probes, runners, per-model code). Its layout is in that folder's [README](../../engine/Tools/Gltf/README.md).
- Native watcher probe: `engine/Tools/CdirWatcherProbe/`. This is not glTF work. It moved because the user asked for all code out of `research/`.
- Linux engine A/B benchmark scripts: `engine/Tools/LinuxEngineBenchmark/`. Also not glTF work, moved for the same reason.

`research/` keeps the docs, JSON manifests, receipts, captures, reference recipes and fixtures. The full old-to-new map is in [code-move-map.json](code-move-map.json). It has 940 entries.

## Rules

- `editor_*.csx` probes go to `engine/Tools/Gltf/editor/`.
- Python, Node and the `Run` driver go to `engine/Tools/Gltf/scripts/`.
- Per-model code (`.cs`, `.csx`, `.py`, `.csproj`, `.shader`, `.patch`) goes to `engine/Tools/Gltf/direct-gpu/` and keeps its subfolder names.
- Data and docs stay in `research/gltf-validation/`. Scripts that need them read them from there by path.
- Patches and candidate engine sources were not applied. They sit beside their model code and no project compiles them. Candidate copies of engine files stay in their model folders, so they never overwrite the real engine files.
- Generated `bin/`, `obj/` and `__pycache__/` folders were deleted. Builds and test runs regenerate them.
- `.gitignore` allows everything under `engine/Tools/Gltf/`, except `bin/`, `obj/`, `__pycache__/` and `.pyc` files. The per-file allowlist for research was removed, and the obsolete `research/gltf-validation/.gitignore` was deleted.

## Effects on existing records

- Files that only moved keep their bytes. 686 of the 940 moved files are byte-identical to their originals.
- 254 moved files had path strings edited so they point at the new locations. Their SHA-256 values changed. A receipt that records an old hash for one of those files will not match the new file. Hashes in receipts are historical evidence and were not rewritten.
- Receipts, frozen requests and result JSON keep the paths they recorded. Use the map to find where a cited file went.
- A frozen selection that names a helper source under `research/gltf-validation/direct-gpu/` no longer dispatches. `editor_direct_gpu_tests.csx` now accepts helper sources only under `engine/Tools/Gltf/direct-gpu/`. Re-freeze the selection against the new path. Do not edit the old freeze.
- Markdown links and path references in code, docs, `.gitignore` and `SboxBuild`'s corpus step were updated.

## Behavior changes in the runners

- `run_editor_probes.py` takes probes only from `engine/Tools/Gltf/editor/`. Its guard requires that folder to be its own probe directory.
- It reads `corpus-case-manifest.json` from `research/gltf-validation/`, and runs `run_editor_tests.py` from `scripts/`.
- `editor_direct_gpu_tests.csx` accepts helper sources only under `engine/Tools/Gltf/direct-gpu/`.
- Scripts that read data from their own folder now read it from `research/gltf-validation/`. Scripts that import shared helpers put `engine/Tools/Gltf/scripts` on `sys.path`.

## What was checked and what was not

Checked after the move:

- Python parses in every moved file, and the runner modules import.
- The 15 Python test modules in `scripts/` match the baseline: 14 pass, and `test_visual_comparison_identity` fails as it did before the move. The capture-diagnostics suite passes.
- `run_editor_probes.py` loads its probes from the new folder. Its guard, manifest path and transport path resolve.
- `Run` and the two UI tests parse and resolve their paths.
- The pure glTF test project still runs: 886 cases, 883 pass, 3 fail (texture-transform rotation). The engine did not change, so this matches the earlier result.

Not checked:

- The editor probes and the C# helpers, which need a running editor.
- The three harness projects (`CaptureDiagnosticsHarness`, `CarConceptHarness`, `OpticalReaderHarness`). `OpticalReaderHarness` referenced `../engine/Sandbox.Gltf/` before the move, so its reference was already broken.
- The `SboxBuild` project build. Only one string literal changed there.
