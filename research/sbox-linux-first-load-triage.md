# Linux first-load triage — 2026-10-25

Source baseline: `63eab6aeadca15fdc7691591301e31c6c0074691`. This report separates locally validated managed fixes from native defects and larger changes that still need implementation. No native binary, user launcher, SDL alias, system setting or running engine output was changed by this investigation. Recovery uses Git/LFS history.

## Required changes, in priority order

| Priority | Issue | Disposition |
|---|---|---|
| High | Wrapped Qt/X11 window receives no keyboard/mouse input | Native owner must review event selection before SDL window creation; existing launcher override restored physical movement in the supplied evidence. No managed workaround added. |
| High | SIGSEGV on mouse release in SDL XInput2 | Native/SDL owner must investigate window lifetime and null window data; disabling XInput2 is an experiment, not an established fix. |
| High | Artifact distribution flattens SDL aliases into separate libraries | Fixed optional relative link metadata in publishing and ordered link restoration. Existing flattened manifests need republishing. |
| Medium | Linux MCP discovery silently misses a working endpoint | Fixed stable Linux process identity in shared transport; isolated regression validation passed. |
| Medium | Editor launches Windows CLI filename on Linux | Fixed platform executable selection; isolated CLI build passed. |
| Medium | First startup blocks on unrelated asset compilation with weak diagnostics | Added active-item/queue/elapsed splash messages, periodic log heartbeats, slow-item timings and bounded summaries; import policy and event-pump cadence preserved. |
| Medium | Slang uniformity analysis and oversized native formatting failure | Native compiler reproducer and bounded diagnostics required; no shader/compiler workaround retained. |
| Low | KDE outer decoration repeats Sbox's title/header | Needs a separate Qt/X11/Wayland window-state test before choosing client or server decoration behavior. No global KDE changes. |

Watcher efficiency belongs to the separate watcher investigation. Survive audio, source casing, generated abilities, authored shaders, textures and models belong to the parent project task.

## Managed fixes

### Stable Linux MCP endpoint identity

Both `McpPipeServer.Listen` and CLI `CreatePipe` previously named the endpoint using `Process.StartTime.ToUniversalTime().Ticks`. Independent .NET runtimes can calculate slightly different wall-clock values for the same Linux process. In an isolated parent/child test, the old values were `8df3258169c311c` and `8df3258169c2c2f`; they therefore identify different sockets. The protocol's `sbox/instance` response already runs on the thread pool, so the evidence does not justify changing it to address main-thread startup delays.

The shared `McpWire.PipeName(Process)` now uses Linux `/proc/<pid>/stat` field 22, the kernel start tick count. It retains PID plus start identity, avoiding a PID-only endpoint vulnerable to PID reuse. Parsing accounts for spaces and closing parentheses in the parenthesized process name. Windows/macOS retain the prior identity calculation and same-user pipe options are unchanged. No directory-wide socket scanning or relaxed permissions were introduced.

**Compatibility:** Linux endpoint names change. Build CLI and engine from the same revision and restart the editor in a separate validation window; a new CLI cannot attach to an old Linux endpoint. The current editor was not rebuilt or restarted for this test.

### Platform CLI executable

`game/addons/tools/Code/Editor/AgentSkill/AgentSkill.cs` now selects `sbox-cli.exe` on Windows and `sbox-cli` elsewhere. This matches the already-shipped Linux apphost. The distributed CLI skill and MCP README now describe both executable names. No skill was installed, removed or messaged to another agent as part of validation.

## Artifact packaging fix (follow-up authorized 2026-10-25)

The old publisher explicitly resolved symlinks and recorded only payload path/hash/size; the downloader consequently created ordinary copies for every alias. The fix adds optional `linkTarget` metadata while retaining the resolved payload hash and size. The publisher validates the complete alias graph before uploading. `ArtifactManifest.cs` holds the shared manifest records; `ArtifactLinks.cs` contains path/graph validation and ordered alias restoration.

The downloader validates the selected paths and link chains before downloads, restores and verifies regular payloads first, and then creates relative symlinks through temporary links and rename. Link chains are ordered target first. It repairs flattened copies, wrong or dangling aliases, and does not trust a regular-file cache entry when the leaf has become a symlink. Alias type/target is checked every restore rather than added to the size/mtime cache. Existing parent-directory symlinks, rooted/escaping targets, cycles, missing or filtered targets, duplicate/ancestor paths and mismatched alias payload metadata are rejected. Partial restores require the link's target closure to remain selected; they fail explicitly rather than leaving dangling aliases. No platform-specific SDL filename is embedded in the implementation.

**Migration:** manifests without `linkTarget` retain regular-file semantics. Re-publish artifacts from a source tree with the intended symlinks to repair an already flattened distribution. Identical hashes alone are not enough to infer aliases. Older downloaders can read the new manifests because payload hashes remain present, but will still flatten aliases; use the updated downloader for symlink restoration. Windows symlink creation still requires OS permission/developer-mode support and reports failure rather than silently copying.

Focused validation completed entirely under `/tmp/sbox-linux-investigation`:

- **16 focused regression cases passed**, compiling the exact repository helper, manifest and test sources. Coverage includes producer metadata capture/JSON round trip, SDL alias chains, flattened-copy repair, repeated restore, dangling aliases, cross-directory relative targets, invalid paths/targets, cycles, missing/filtered targets, metadata and path collisions, existing/dangling parent symlinks, missing payload preservation and legacy manifests. Tests are wired into `Sandbox.Test.Unit` through source links, without native-engine dependencies.
- **Full SboxBuild source compilation passed**, zero warnings/errors, using an isolated project with the existing InteropGen/ShaderProc dependency assemblies read from the checkout. No build-tool command, artifact restore or publisher was run against the working checkout.
- An isolated probe invoked the actual publisher in dry-run mode and the actual downloader with an in-memory HTTP handler. Publishing recorded the alias target; downloading a two-alias chain fetched **one** payload. Repeating and repairing a flattened alias with a populated cache fetched **zero** additional payloads. Replacing a regular payload with a same-content symlink forced its proper file restoration. Excluding a required payload from a partial selection failed before downloads.
- New helper/manifest/test files passed scoped whitespace verification; `git diff --check` passed. Full repository SboxBuild checks remain deferred until the active editor session permits them.

The supplied input report observed three identical SDL files but two loaded inodes; repairing aliases reduced mappings to one library. Physical input remained broken until the separate event-selection override. Packaging and event selection remain distinct defects. No artifact restore was performed against `game/bin`; the user's repaired SDL aliases and running editor were untouched.

## Startup progress and diagnostics follow-up

`StartupImportProgress` now observes both startup shader checks and both existing asset-import paths. Its splash message includes the active path, elapsed time on that item, completed count and remaining queue. The message is evaluated only when the existing splash pump renders; it does not add editor event pumping, move native calls to workers or change import ordering. When a synchronous native import blocks rendering, a background timer logs the active item and stage elapsed time every ten seconds. That timer accesses only the diagnostic helper and logger, never editor widgets, assets or native compilation APIs.

Shader starts are logged before compilation/cache checking so a crash has an identifiable last shader. Completion distinguishes reused, compiled and failed shader results. Asset diagnostics record the actual boolean returned by `Compile(false)` rather than inventing native success semantics. Imports lasting at least two seconds log path, duration and result. Up to ten fast unsuccessful returns are logged individually; further ones are counted. End-of-stage output includes outcome totals and the five slowest imports over the threshold. Exception unwinding reports the interrupted active item while preserving the exception. Heartbeats stop on disposal; logging and splash-message scopes are removed before the separate thumbnail phase.

Import selection, catalog order, fast-path ordering, shader options, shader reload behavior, main-thread execution, startup blocking and existing `Spin`/splash call frequency are unchanged. Native shader variants and BC7 substage progress are not exposed by these APIs: elapsed time on a model can include dependency/texture compilation, and no inferred native percentage or ETA is displayed. A fully blocked main thread cannot render a live elapsed label; the log heartbeat is the available diagnostic during that interval.

Validation: **4 focused deterministic tests passed**, covering active path/elapsed/queue messages, no heartbeat after completion/disposal, interruption diagnostics, quiet fast imports, bounded slowest-item summaries, unsuccessful returns and log-flood prevention. The full `Sandbox.Tools` source also compiled successfully (zero warnings/errors) in an isolated project using existing dependency DLLs as read-only references, with outputs under `/tmp/sbox-linux-investigation/startup-tools`. Full repository build/test and live splash validation remain deferred to a separate editor session. The running editor and `game/bin` were not rebuilt, restarted or modified.

## Native findings and upstream requests

- **Input selection:** supplied disassembly places `SDL_VIDEO_X11_EXTERNAL_WINDOW_INPUT=0` in `libtier0`'s Qt-window wrapping path. SDL documents that this disables SDL's XSelectInput call; `1` enables it and must be chosen before window creation ([SDL hint](https://wiki.libsdl.org/SDL3/SDL_HINT_VIDEO_X11_EXTERNAL_WINDOW_INPUT)). The supplied foreground traces show working keys, look angles and movement with `1`. Environment variables take precedence over ordinary `SDL_SetHint` ([SDL priority documentation](https://wiki.libsdl.org/SDL3/SDL_SetHint)). Upstream should establish which layer selects and forwards events for wrapped windows, then validate physical keyboard, relative mouse capture, focus changes and teardown together.
- **Release crash:** both supplied crash records place the main thread at `X11_HandleButtonRelease+17`, called by `X11_HandleXinput2Event` → `X11_DispatchEvent` → `X11_PumpEvents` → `SDL_PollEvent_REAL` → `libengine2`. Registers include `rax=0`, consistent with the reported null-data investigation, but the stack alone does not establish the ownership race or a safe source patch. Request SDL/native symbols and a reduced Qt-wrapped-window reproduction covering capture/focus/window destruction and release events. The parent's `SDL_VIDEO_X11_XINPUT2=0` trial remains unvalidated here. Do not add it globally or call it a permanent resolution.
- **Painterly/diagnostics:** supplied workers repeatedly enter Slang `ValidateUniformityContext`; a later native `AppendFormatV` requests 180,061,337 characters. The entire 20m48 startup interval cannot be assigned to one shader. Request a reduced deprecated shader reproducer with the exact shipped Slang build, uniformity-analysis profile and bounded native diagnostic formatting. This is not evidence of a GPU fault. Neither C++ implementation is present here.
- **Startup:** supplied cold import logs show 936.674s in eager catalog asset compilation, with BC7 worker stacks, while a later asset pass is 17.366s. Existing `CompileAllAssets` and `CompileAllShaders` already expose counters and splash messages, but do not expose native BC7 progress or shader variant analysis. Incremental caching works; scene dependency scheduling needs a correctness design before moving imports behind editor readiness. Record active item, elapsed time and compile result first; avoid promising cancelability of synchronous native compilers.
- **Decoration:** managed `Window` wraps native `CFramelessMainWindow`; native construction and platform decoration policy are absent. Changing flags late can recreate Qt surfaces and affect capture. Obtain the window flags and compositor behavior in a separate session before patching the header.
- **Other reported native integration failures:** HarfBuzz coexistence and Flatpak/native Steam IPC remain evidence-backed upstream investigations, with no proven managed root cause in this scope.

## Validation and remaining checks

- Built `engine/Launcher/SboxCli/Sbox-Cli.csproj` with all output/intermediate paths redirected to `/tmp/sbox-linux-investigation`: success, zero warnings/errors.
- Compiled the exact repository transport and new `McpPipeNameTests.cs` in an isolated MSTest project: **2 passed**. Tests cover Linux kernel start identity and distinct identities for a reused PID.
- Independent parent/child runtimes used the exact patched `McpWire` to open a same-user named pipe and exchange request/reply: **passed**, including a parent process name containing spaces and `)`. Their old wall-clock identities differed, while the patched identity matched (`sbox-mcp-32628-30d32`).
- The sandbox initially denied local test sockets; the same isolated probes passed with local socket access authorized. NuGet audit metadata was unreachable (NU1900); cached test dependencies restored and compiled successfully.
- A scoped folder whitespace verification found pre-existing formatting and line-ending discrepancies in touched existing files; this is not a full-format pass. New test/transport formatting was checked separately. `git diff --check` is used for patch hygiene.
- Full SboxBuild build/test/format checks and editor startup/agent-preference validation are deferred because they can rebuild shared engine outputs or involve the active editor. Run the repository's documented build, non-LiveBackend test and format verification commands in a separate window before pushing. No commit, push or upstream issue filing was performed.

## Correctness review follow-up — 2026-10-25

Reviewed the CLI executable selection and shared process identity, publisher/downloader and cache paths, alias graph/path handling, startup observer lifetime, splash scopes, and existing regression tests. Three edge cases were fixed:

1. **Publication across batches:** final link validation previously used only newly added entries. If an earlier batch had already supplied the target, path deduplication removed it from the new batch and a valid alias was rejected. Validation now uses the accumulated manifest. An actual publisher dry-run probe publishes a payload first, then a new alias with that already-deduplicated target, and succeeds without another upload.
2. **Diagnostic sink exceptions:** logging exceptions could escape the ThreadPool heartbeat and terminate the process, or mask an import exception during disposal. The observer now isolates sink exceptions. A regression test uses a throwing sink through construction, begin, heartbeat, completion and disposal and confirms import accounting completes normally.
3. **Uncollapsed target traversal:** `directory-link/../payload` can resolve differently on disk from lexical path normalization. Validation now rejects directory symlinks in the original target's intermediate components before collapsing `..`. A focused regression confirms rejection.

Final focused validation: **24 tests passed** together (17 artifact cases, 5 startup-diagnostics cases, 2 MCP identity cases). The actual publisher/downloader probe passed alias metadata, cross-batch deduplication, one-payload downloads, idempotence, cache/type repair and missing-target partial-selection checks. The independent-process MCP exchange passed again, including spaces/`)` in the process name; the old wall-clock identities still differed while the new endpoint matched. CLI, full editor-tool source and build-tool source compiled in isolated `/tmp` outputs. CLI/editor-tool compilation reported zero warnings/errors. Scoped whitespace verification and `git diff --check` passed; existing unrelated line-ending notices remain.

No additional managed correctness defect was identified in this review. This does not validate native SDL behavior or live splash rendering. Full repository checks, a paired engine/CLI deployment, and live editor validation remain deferred. No editor restart, attachment, artifact restore into the checkout, or write to its running binaries was performed.

## Evidence read

External evidence remains in its original location, rather than being copied into source control:

- `/home/damian/Documents/Codex/2026-10-25/i-x20/outputs/sbox-first-load-performance.md`
- `/home/damian/Documents/Codex/2026-10-25/i-x20/outputs/sbox-first-load-evidence.txt`
- `/home/damian/Documents/Codex/2026-10-25/i-x20/outputs/sbox-input-investigation.md`
- `/home/damian/Documents/Codex/2026-10-25/i-x20/work/sbox-punch-crash.txt`
- `/home/damian/.codex/attachments/14433d1f-1a78-4b35-8628-3effede8fe60/Pasted text.txt`

## Post-merge commit validation

Upstream merge `faa41f94` completed before the remaining commits. The restored publisher uses upstream's `HashArtifacts` and `AddToManifest` pipeline, with combined-manifest link validation before uploading. The isolated full build-tool source compilation passed with zero warnings/errors, and all **24 focused tests passed again** against the restored sources. Full editor/native validation remains deferred; no running engine output was rebuilt. The original preservation stash remains available, and unrelated `game/bin/dotnet/` files are excluded from the commits. No push was performed.
