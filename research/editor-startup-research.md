# Editor startup: redundant work and parallel work

Date: 22 October 2026. Source revision: `cca2bf927c79f4c1b8d60e46fdbac15a1123c6b8`.

## Result

There is avoidable work in the editor startup path. The small constant-condition checks are real, but they do not explain a startup delay of minutes. The best concrete reduction is to parse conditional source files once. The largest architectural opportunity is a validated persistent cache for local compiled assemblies. The best new overlap candidates are Razor preparation during dependency waits and bounded code-archive serialization on workers.

This is a source review with existing performance evidence. No engine code was changed, no editor was restarted, and no new performance capture was made. Proposed savings are not measured.

## Performance evidence and limits

The saved warm run from 14:54 on 22 October predates the latest upstream merge. It is a baseline for choosing what to measure, not a current-build benchmark.

| Measured scope | Time | Interpretation |
| --- | ---: | --- |
| Stable editor ready | 150.213 s | Includes time outside the internal startup timer. |
| Check Compiled Projects | 36.547 s | Includes compile waits, output publication and loader work; not pure C# compiler CPU time. |
| Native asset dependency scan | 33.839 s | Native callback scope. Nested within Load Game. |
| GameResource discovery/load | 23.525 s | Of this, load work was 22.769 s; file indexing was 0.396 s. |
| Startup shader pass | 6.280 s | Includes freshness checks; not proof that all shaders were rebuilt. |
| Generate solution | 0.400 s | Small direct upper bound for moving this step off the ready path in this run. |
| Register custom asset types | 0.540 s | Combined timer label; not an isolated icon-generation measurement. |

Do not add nested stage times. Resource load cost also varied in other saved runs. The normal warm sample is only one sample.

Sources: [saved timings](C:/Users/DPHoo/AppData/Local/Temp/vallark-startup-profile/native-cache-warm-20261022-timings.json), [saved log](C:/Users/DPHoo/AppData/Local/Temp/vallark-startup-profile/native-cache-warm-20261022-full.log), [prior native analysis](../Temp/vallark-startup-profile/native-cache-results.md).

## Ranked work reductions

### 1. Parse conditional source files once, then retain the effective tree

The compiler first parses, updates or reuses a normal source tree. It then discards that result and parses a new `#if` wrapper when a filename replacement directive applies. This defeats unchanged-tree reuse for these files. The standard mapping is `.Server.cs` to `SERVER`.

The current Survive `code` folder contains 99 `.Server.cs` files, with 861,709 source bytes. This count does not include every library or built-in project.

Construct the final effective text before the parse/reuse decision. The cache identity must include source content, replacement directive, parse options and disabled-trivia behavior. Preserve file paths and diagnostic line mapping. Test first compilation, unchanged rebuild, changed text, changed directive, client/server symbols and code exclusion. This removes verified discarded work; its startup saving is not yet measured.

Sources: [Compiler.SyntaxTree.cs:151](../engine/Sandbox.Compiling/Compiler/Compiler.SyntaxTree.cs#L151), [wrapper replacement:177](../engine/Sandbox.Compiling/Compiler/Compiler.SyntaxTree.cs#L177), [CompilerConfiguration.cs:94](../engine/Sandbox.Compiling/CompilerConfiguration.cs#L94).

### 2. Add a correct persistent cache for local compiled assemblies

`CacheAssemblies` permits disk-cache reuse only for built-in projects in a retail editor. Local game/editor projects rebuild in a new process. The incremental syntax/compilation state is held in process memory.

This is repeated work across starts, not a literal tautology. It has the largest plausible impact on the measured compile/update wait. Do not enable the existing cache for all projects by changing one condition. Its existing validation is not a complete content/dependency manifest.

A production cache must cover added and deleted sources, content, compile options, source generators, referenced assembly identities, engine/API/toolchain versions and relevant assets used by generators. Publish outputs atomically. Preserve access checks, assembly load order and hotload behavior. Validate unchanged starts, edits with preserved timestamps, deleted files, changed references and interrupted cache writes. Measure cache validation separately from cache misses and assembly loading.

Sources: [Project.Compiling.cs:37](../engine/Sandbox.Engine/Systems/Project/Project/Project.Compiling.cs#L37), [existing validation:219](../engine/Sandbox.Engine/Systems/Project/Project/Project.Compiling.cs#L219), [IncrementalCompileState.cs:13](../engine/Sandbox.Compiling/Compiler/IncrementalCompileState.cs#L13).

### 3. Retain asset-type icons when their inputs did not change

Startup updates custom types before the native dependency scan and again after game loading. `UpdateType` always calls `old.Init`, which calls `GenerateGlyphs`, even when it returns false because no new type was created. Native scan completion also calls plain `Init` on every asset type, which loads/resizes default icons. A later custom-type update can render those custom icons again.

Separate type binding from icon creation. Keep rebinding after hotload, but cache icons by their real inputs and type generation. Include changes to icon callbacks, attributes and image paths. Keep Qt/native pixmap work on the main thread. Do not use a permanent cache keyed only by extension: custom icon code can depend on other state.

Test unchanged repeat calls, new types, hotloaded icon methods and native scan completion. Count generated icons and measure their time. The saved 0.540 s custom-type scope does not justify a multi-second claim for this change.

Sources: [StartupLoadProject.cs:252](../engine/Sandbox.Tools/StartupLoadProject.cs#L252), [preload event:308](../engine/Sandbox.Tools/StartupLoadProject.cs#L308), [AssetType.cs:161](../engine/Sandbox.Tools/Assets/AssetType.cs#L161), [default icon creation:251](../engine/Sandbox.Tools/Assets/AssetType.cs#L251), [UpdateType:345](../engine/Sandbox.Tools/Assets/AssetType.cs#L345), [AssetScanComplete:273](../engine/Sandbox.Tools/Assets/AssetSystem.cs#L273).

### 4. Avoid duplicate asset metadata refresh when a change is already queued

`AssetChanged` updates an asset immediately, then queues it. `Tick` later resolves dependency information and updates the asset again. Several callbacks can repeat the immediate update even though the queue deduplicates the notification.

Split the immediate fields from the full dependency-aware refresh, or track a revision. Do not remove either pass until processor and rename/delete behavior are checked. The saved warm native scan had zero `AssetChanged` callbacks, so this candidate cannot explain that scan's 33.839 s delay. It may help changed-asset startup and hotload instead.

Sources: [AssetSystem.cs:247](../engine/Sandbox.Tools/Assets/AssetSystem.cs#L247), [queued update:301](../engine/Sandbox.Tools/Assets/AssetSystem.cs#L301), [NativeAsset.cs:16](../engine/Sandbox.Tools/Assets/NativeAsset/NativeAsset.cs#L16), saved warm log above.

### 5. Build one compiler dependency graph per build

The group repeats dependant traversals, referenced-compiler closures, cycle checks and dependency-depth calculations. Name lookup scans the compiler list under a lock. This repeats graph work even though the graph can be stable for one build.

Take an immutable compiler/reference snapshot at the build boundary. Build name and reverse-edge maps once, then compute a shared dirty closure and topological order. Preserve newly dirty work for the next build. Test cycles, diamond dependencies, removal and changes during a build. This reduces work without adding threads, but the graph's share of startup time has not been measured.

Sources: [CompileGroup.cs:171](../engine/Sandbox.Compiling/CompileGroup.cs#L171), [name lookup:202](../engine/Sandbox.Compiling/CompileGroup.cs#L202), [repeated closures:263](../engine/Sandbox.Compiling/CompileGroup.cs#L263), [reference cycle check:373](../engine/Sandbox.Compiling/CompileGroup.cs#L373), [Compiler.cs:247](../engine/Sandbox.Compiling/Compiler/Compiler.cs#L247).

## Safe parallelism candidates

| Candidate | Current ordering | Required boundary | Priority |
| --- | --- | --- | --- |
| Prepare Razor while referenced projects compile | Archive build, await references, then Razor processing | Prepare reference-independent inputs before the wait. Use one owner for mutable output, archive and incremental state. Preserve diagnostics. | First overlap experiment. Survive has 34 Razor files; benefit is limited to work hidden by a real dependency wait. |
| Serialize code archives on workers | Successful archives serialize and compress one at a time in `OnCompileSuccess` | Freeze each successful archive, prepare bytes with bounded concurrency, then publish in existing order. | Measure this phase first. It copies source text and compresses JSON. |
| Read resource bytes ahead of object creation | Read bytes, resolve native GUID, create/load/register resource, repeat | Bounded byte buffers from stable, verified thread-safe filesystem owners. Keep native GUID operations, resource creation, callbacks and registration on their required thread. | Larger experiment; load work is costly, but read time is not yet isolated. |
| Defer solution-file generation | Awaited before project compilation check | Snapshot project configuration; do not mutate a shared solution generator from workers. Complete before a user requests IDE access. | Low priority: 0.400 s measured scope. |

Sources: [Compiler.Build.cs:91](../engine/Sandbox.Compiling/Compiler/Compiler.Build.cs#L91), [Razor processing:189](../engine/Sandbox.Compiling/Compiler/Compiler.Build.cs#L189), [Compiler.Razor.cs:13](../engine/Sandbox.Compiling/Compiler/Compiler.Razor.cs#L13), [Project.Compiling.cs:388](../engine/Sandbox.Engine/Systems/Project/Project/Project.Compiling.cs#L388), [CodeArchive.cs:89](../engine/Sandbox.Compiling/CodeArchive/CodeArchive.cs#L89), [ResourceLibrary.cs:741](../engine/Sandbox.Engine/Resources/ResourceLibrary.cs#L741), [registration main-thread assertion:41](../engine/Sandbox.Engine/Resources/ResourceLibrary.cs#L41), [FileIndex.cs:77](../engine/Sandbox.Filesystem/FileIndex.cs#L77), [StartupLoadProject.cs:180](../engine/Sandbox.Tools/StartupLoadProject.cs#L180).

Use a small worker limit and a byte budget. More simultaneous source strings, syntax trees and decompressed resources can increase editor memory. Do not use unbounded `Task.WhenAll` over the complete asset set.

## Literal redundant checks: valid cleanup, small gain

- `CompileGroup.BuildAsync` checks `NeedsBuild` twice with no await, callback or change to the pending set between the checks. The second check is redundant under the current single-owner model. It does not make the plain HashSet thread-safe. [CompileGroup.cs:228](../engine/Sandbox.Compiling/CompileGroup.cs#L228), [second check:244](../engine/Sandbox.Compiling/CompileGroup.cs#L244).
- `BuildReferencesAsync` creates `foundHash` and tests `Contains`, but never adds an entry. That test is always false. The archive already stores references in a case-insensitive HashSet. [Compiler.cs:211](../engine/Sandbox.Compiling/Compiler/Compiler.cs#L211), [CodeArchive.cs:49](../engine/Sandbox.Compiling/CodeArchive/CodeArchive.cs#L49).
- `Bootstrap.Init` calls `Material.Preload` twice. This only requests the same class constructor, which runs once. The more useful fix is to put the first call inside the timer: the current timer measures the second, cheap call. [Bootstrap.cs:280](../engine/Sandbox.Engine/Core/Bootstrap.cs#L280), [Material.Static.cs:15](../engine/Sandbox.Engine/Resources/Material/Material.Static.cs#L15).
- `AssetSystem.All` already excludes deleted assets; `DeleteOrphans` repeats that filter. `File.ReadAllBytes` returns bytes or throws, so the later null-result branch is unreachable. Preserve exception behavior. [AssetSystem.cs:24](../engine/Sandbox.Tools/Assets/AssetSystem.cs#L24), [DeleteOrphans:447](../engine/Sandbox.Tools/Assets/AssetSystem.cs#L447), [Asset.cs:493](../engine/Sandbox.Tools/Assets/Asset.cs#L493).

## Work that is already parallel, or must remain ordered

- Independent C# compilers already start with `Task.WhenAll`. Source parsing, Razor work, generators and blacklist walks have parallel paths. Roslyn concurrent build is enabled. Adding a broad `Task.Run` does not remove the dependency graph. [CompileGroup.cs:296](../engine/Sandbox.Compiling/CompileGroup.cs#L296), [Compiler.SyntaxTree.cs:98](../engine/Sandbox.Compiling/Compiler/Compiler.SyntaxTree.cs#L98), [Compiler.Build.cs:172](../engine/Sandbox.Compiling/Compiler/Compiler.Build.cs#L172).
- Asset metadata updates already use `Parallel.ForEach` after native dependency information is prepared on the main thread. [AssetSystem.cs:301](../engine/Sandbox.Tools/Assets/AssetSystem.cs#L301).
- The compile check after `FileWatch.Tick` is not redundant: the tick can discover new changes. [Utility.Projects.cs](../engine/Sandbox.Tools/Utility/Utility.Projects.cs).
- Repeated package-context events cannot simply be dropped. Built-in installation can occur before the tools subscriber is registered. Later notifications can enroll assemblies for that subscriber. An idempotent event change requires subscriber replay or equivalent behavior. [PackageManager.ActivePackage.cs:79](../engine/Sandbox.Engine/Services/Packages/PackageManager/PackageManager.ActivePackage.cs#L79), [ToolsDll.cs:198](../engine/Sandbox.Tools/ToolsDll.cs#L198).
- The menu's immediate/deferred resource passes repeat managed discovery, but `MenuDll.Initialize` excludes this resource path in the editor. It is not an editor-startup gain. [MenuDll.cs:158](../engine/Sandbox.Menu/MenuDll.cs#L158), [two passes:275](../engine/Sandbox.Menu/MenuDll.cs#L275).
- The inventory definition request is already in progress when startup waits for it. Moving the wait does not start the download sooner, and the five-second timeout is not a measured five-second cost. [Bootstrap.cs:359](../engine/Sandbox.Engine/Core/Bootstrap.cs#L359), [Inventory.Definitions.cs:28](../engine/Sandbox.Engine/Game/Services/Inventory/Inventory.Definitions.cs#L28).
- The native asset scan must follow mount/type setup and precede resource loading. The saved trace contains many repeated failed file lookups, but this checkout has no native implementation or supported negative-cache control. Do not skip `UpdateMods`, remove mount roots or run it concurrently with mutable native state. [StartupLoadProject.cs:316](../engine/Sandbox.Tools/StartupLoadProject.cs#L316), prior native analysis above.
- Shader freshness checks must include transitive includes and engine/compiler versions. Running several full shader compilations together can multiply worker use and memory. Do not infer that a warm cache makes these checks redundant. [ShaderSource.cs:20](../engine/Sandbox.Engine/Systems/Render/ShaderCompile/ShaderSource.cs#L20), [StartupLoadProject.cs:360](../engine/Sandbox.Tools/StartupLoadProject.cs#L360).

## Recommended sequence

1. Add narrow counters/timers for conditional source parsing, Razor preparation, archive serialization and resource byte reads. Correct the material preload timer.
2. Fix effective-source parsing and unchanged-tree reuse. Cache unchanged type icons. Run behavioral tests for each change.
3. Compare bounded archive preparation and Razor/dependency overlap independently. Track peak private bytes as well as ready time.
4. Design and validate the persistent local assembly cache. Do not bypass current source or access validation.
5. Take repeated warm starts on the latest merged build with the same project/scene and no other editor builds. Report startup and stable-ready times separately. Do not promise a total saving from the historical nested timings.

No implementation, commit or push was made by this research task.
