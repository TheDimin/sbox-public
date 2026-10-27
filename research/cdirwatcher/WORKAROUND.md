# C# workaround for a missing compiled-scene catalog path

Implemented only in the isolated worktree. No main-checkout or user-project files
were changed; no existing editor was attached to, restarted, or rebuilt.

## Behavior

SceneSource.LoadForPlay retains its existing behavior for unsaved scenes and
mount:// scenes. For a saved, local scene it calls LoadCompiledScene. An existing
native catalog path takes precedence. On Linux only, if that path is absent or
no longer names a file, the loader checks the absolute source path and its
adjacent .scene_c output. The source must still exist and have the .scene
extension. Relative paths, other resource types, and orphaned outputs do not
qualify. The decoder remains SceneFile.FromCompiled; invalid compiled data is
rejected and the existing load-error handler reports the failure. No source JSON
is loaded as a substitute. A successful disk fallback emits a diagnostic warning.

The lookup is performed when Play loads the saved scene, so a compiled file that
appears after the first missing lookup can be opened on a later attempt without
caching that negative result or restarting. This is not a source compiler or a
freshness guarantee: it uses an existing compiled output, just like the original
catalog-path load. Recompile changed source as appropriate. It does not update
native IsCompiled/dependency metadata, native asset discovery, GUID indexing,
resource reloads, other resource types, or the native directory-watcher warning.
Native resource/dependency failures during actual scene instantiation may remain.

## Validation

- SboxBuild test build: succeeded with zero warnings/errors.
- Seven new engine tests passed: output created after a missing catalog answer;
  native-path precedence; disappeared catalog file; disabled fallback; rejected
  corrupt bytes; rejected orphaned output; rejected another resource type.
- SboxBuild targeted regression run: 22 engine tests plus all 38 watcher tests,
  60 passed. The 22 include the seven new tests and existing scene load/save tests.
  Unrelated shards had no matching tests; this was not a full-suite rerun.
- dotnet format whitespace --verify-no-changes --no-restore for the two changed
  C# files passed; workspace-loading warnings were reported without diagnostics.
- A separate headless process invoked LoadCompiledScene with a null catalog path
  and the current Blender project's source path. It decoded the existing 89,740
  byte compiled file, resource path scenes/minimal.scene, and nine GameObjects.
  The project assets were read only. This verifies decoding the current output,
  not the earlier log incident's cause or a live Play-mode reproduction.
- git diff --check passed.

Logs remain under game/.source2/validation/: scene-compiled-fallback-tests.log,
scene-fallback-regressions.log, scene-fallback-format.log and
scene-real-file-probe.log. The working read-only scene probe source is included
as scene_file_probe.cs.txt; it references the isolated build's assemblies.

## Why not a general managed watcher bridge yet?

IAssetSystem.RegisterAssetFile exists, but a game-only test process does not
initialize the native editor asset service. Experimental isolated service/editor
initialization either failed in native startup or required editor Qt services.
No supported registration-to-native-cache bridge was validated. These experimental
harnesses were not installed, and their crashes affected only their own processes.
The workaround therefore makes no unverified native registration or cache calls.

The confirmed native CDirWatcher reproduction, its positive inotify control, and
the upstream native fix requirements remain documented in README.md.
