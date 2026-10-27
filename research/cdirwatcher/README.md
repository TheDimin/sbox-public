# Linux native CDirWatcher investigation

2026-10-27. Investigation performed from the isolated linux-file-watchers checkout.
No editor was started, stopped, attached to, or restarted. No main-checkout files,
project assets, shared build outputs, generated interop, or native binaries were modified.
`sbox-cli --list` returned an empty list; this is an observation, not permission to launch an editor.

## Confirmed origin and mechanism

The literal warning is in `game/bin/linuxsteamrt64/libtier0.so` and is emitted
by `CDirWatcher::SetDirToWatch(char const*, unsigned int, bool)`.
In both inspected builds its exported address is 0x63960. For a nonempty path,
the function stores the path, emits `DevWarning` once per process via a static
flag, and returns true without installing an operating-system watch.
`CDirTreeWatchManager::CheckForPendingChanges()` at 0x63a90 is a single `ret`.
`CDirWatcher::GetChangedFile(CUtlString*, bool)` at 0x64250 only drains its
explicit change list; it does not read inotify or poll directory contents.
Its manual `AddFileToChangeList` entry point still works.

`libassetsystem.so` imports `CDirTreeWatchManager::TraverseAndAddWatch`,
`GetChangedFile`, and `RawTraverse`; its update path calls `GetChangedFile`.
`libresourcecompiler.so` imports `CDirTreeWatchManager::CheckForPendingChanges`.
This is native asset-discovery/change tracking and the compiler's pending-change
path. Initial traversal, explicit registration and explicit compilation can
still work. The evidence does not establish that every native cache or resource
reload operation uses this watcher, or that the warning disables every form of
asset reload.

## Reproduction against the actual main-checkout library

`probe_native_watcher.py` is an isolated Linux x86-64 diagnostic for the inspected
native ABI, not a proposed production binding. It loads tier0 into a separate
Python process; all mutations occur in a freshly created temporary directory.
It calls the exported CDirWatcher methods using oversized, aligned opaque storage.
A direct libc inotify watch observes the exact same filesystem operations as a
positive control. No engine, resource compiler, or editor is booted.

| Operation | Native CDirWatcher event | Inotify control bytes |
|---|---:|---:|
| Create minimal.scene_c | No | 128 |
| Modify | No | 96 |
| Rename | No | 64 |
| Delete | No | 32 |

SetDirToWatch returned true. Explicit AddFileToChangeList delivered
`manually-fed.scene_c`. The older native library already present in the isolated
checkout produced the same results. This reproduces the missing OS notification
backend independently of the project, editor state, and managed watcher pool.
The script asserts this known-broken behavior so it fails if the observed native
implementation changes; it is not a passing test of correct native watching.

Run from the isolated checkout:

```sh
python3 research/cdirwatcher/probe_native_watcher.py /home/damian/projects/sbox-public/game/bin/linuxsteamrt64/libtier0.so
```

Raw results are in `native-probe.json` and `isolated-native-probe.json`.
The relevant native disassembly is included beside this report.

## Relationship to the managed inotify improvements

`LocalFileSystem` chooses `CaseInsensitivePhysicalFileSystem` on Linux.
Its WatchImpl constructs `SharedPhysicalFileWatcher`, which owns/reuses a
`LinuxDirectoryWatcher` using inotify_init1/inotify_add_watch, poll, read and eventfd.
This supports managed source hotload, styles, and managed filesystem subscriptions;
the editor folder/asset-browser helper changes also handle their own watcher
lifetime and batching. None installs a CDirWatcher backend or feeds its queue.

A native asset notification normally reaches the managed side through the
`Editor.AssetSystem.AssetAdded/AssetChanged` reverse-interop callbacks. A managed
filesystem notification is not automatically equivalent to that native update.
The public managed RegisterAssetFile API can explicitly register an asset, but it
is not a proven substitute for source, compiled-output, deletion, dependency,
meta/GUID, and resource-system cache invalidation across the native watcher.

## The Blender minimal.scene incident: evidence and limits

`game/logs/sbox-dev-2026-10-27.4.log` records:

- 16:01:03.2258: the native watcher warning.
- 16:01:43: local.b2sbox mounting and native asset scanning.
- 16:02:07.3918: on-demand recompile of scenes/minimal.scene, with reason
  `Invalid Dependency Information`.
- 16:02:12.9982: suppression of a repeated attempt with the same fingerprint.
- 16:02:13.0009: ResourceSystem ERROR_FILEOPEN for scenes/minimal.scene_c.
- 16:03:05 and 16:06:50: SceneSource reports that GetCompiledFile(true) supplied
  no compiled path. At 16:06 another on-demand attempt is still suppressed.

Read-only stat of the project file records birth at 16:01:44.622439530 +0700,
before those failures. Its current mtime is 16:10:51.656897699 and its current
size is 53,287 bytes. Birth time is filesystem metadata, not proof that the bytes
were valid compiled scene data at the earlier failure; they have changed since.

`game/logs/sbox-dev-2026-10-27.6.log` records the same warning at 16:07:43.6023,
then a successful local.b2sbox load at 16:08:17.7902. Therefore the warning does
not by itself prevent loading a compiled scene after a fresh scan.

The managed SceneSource LoadForPlay path asks Asset.GetCompiledFile(true), which
NativeAsset forwards to native.GetAbsolutePath_Transient(AssetLocation_t.Game).
An empty native catalog answer can reject a real disk file before managed
File.ReadAllBytes runs. Native resource loading also reported failure in this
incident, so bypassing this managed check alone would not prove a complete fix.

Missing native events are a credible explanation for newly created compiled
outputs not updating a running asset catalog. The probe proves that prerequisite
failure, not this incident's complete causal chain. Invalid dependency metadata,
on-demand compile suppression, path/mount state and compiled-file validity remain
alternative or contributing causes. No controlled scene create/compile/load test
was run in the user's editor, and no causal claim is made from restart alone.

## Fix feasibility and next native change

AGENTS.md identifies this checkout as managed engine/editor/tooling; the native
C++ core is supplied as prebuilt binaries. There is no CDirWatcher implementation
here to patch and rebuild. Existing generated bindings expose neither the live
CDirTreeWatchManager handle nor a supported native file-change injection API.
No generated Interop file was edited, no warning was suppressed, and no ABI
interposition or binary patch was installed.

The actual fix belongs in the native tier0 Linux CDirWatcher implementation:
install OS watches, translate create/write/rename/delete events into its queue,
provide cancellation/cleanup and thread-safe ownership, handle overflow/rescan,
and integrate the tree manager's recursive traversal and pending-change checks.
Keep its failure return truthful if watch creation fails. Verify native asset
catalog registration, dependency invalidation and ResourceSystem reload together.

Required acceptance tests include files existing before startup; files created
or replaced after startup; compiled scene output created by an external compiler;
rename/delete/recreate; nested directories; watch overflow and retry; and repeated
source/compiled scene loads without an editor restart. Compare direct disk bytes,
native HasCompiledFile/GetAbsolutePath answers and SceneFile.Load before and after
explicit registration/rescan to isolate which cache needs invalidation.

A general managed replacement, broad periodic rescans, or blanket recompilation
is not implemented: it would not repair native watching and could mis-handle
mount precedence/deletions or trigger large resource/shader work. The native
warning fix remains dependent on native source or an upstream native binary.
A subsequent request produced a deliberately narrow, validated C# workaround
for local compiled-scene loading; see WORKAROUND.md for its exact scope and limits.

## Binary identities

- main tier0: SHA256 `408949b3c2bb784a9d9fce249af9abb0a1d92ff1d276b8533177b1b320b6027c`
- isolated tier0: SHA256 `70832a93ca62bb7eb6c00ffeb20e5f17a6ac3c46ed00ff15eeaaddec1c44057b`
- main assetsystem: SHA256 `7c5a74935ed6006b29fab7b2b885854c98c4df2afec1ed7a88eec32e2206156b`
- main resourcecompiler: SHA256 `58e4d07c9376c4ce17c6ffaf3b5872e9714b30c57cbe581f8834c3203bc75a37`
