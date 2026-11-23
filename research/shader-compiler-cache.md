# Shader compiler cache

## Change

Project shaders continue to use their existing `.shader_c` files. A private record under
`game/.source2/shadercompiler` now links each output to the compiler binary checksums and the
output SHA-256. There is no new version flag, native interface, or upstream requirement.

The compiler identity uses the existing `.sbox-artifacts.json` SHA-256 values after checking
the installed files. Missing or stale artifact metadata falls back to actual file hashes.
Only compiler files affect the identity; an unrelated upstream commit does not invalidate it.
The compiler, DXC, resource compiler, and installed Slang components are covered. Identity is
captured once per compiler process. Alternate loaded compiler paths disable cache reuse.

A matching record is necessary but not sufficient for reuse: the existing native source and
include check still runs. A compiler change bypasses the native combo cache as well as the
whole-shader cache. A missing record requires one initial rebuild. A changed or damaged output
cannot inherit a prior record because its bytes are checked before native loading.

The shader is published with the existing atomic replacement helper. Its record is published
after successful output publication. Failed compiles, cancelled publication, and SPIR-V dumps
do not approve a new cached output. The editor still reloads rebuilt shaders before loading
further assets. Startup logs now separate reused, compiled, and failed shaders.

Runtime-generated game shader cleanup and package shader upgrade caches are separate existing
paths. This change does not remove their cleanup or security rules.

## Validation setup

The user requested that the current VallArk editor session stay open. Validation output is
therefore under `Temp/shader-cache-validation/game`, with copied native compiler libraries and
core includes. The live editor binaries are not deployed from this build.

SboxBuild's `test --no-build false` previously started concurrent builds of shared projects,
which produced CS2012 file locks during this check. It now builds the solution once before
starting test processes. For this validation, `OutDir` selects the temporary managed folder.
A temporary `CustomAfterMicrosoftCommonTargets` file disables the mount and UI-content deploy
steps, which otherwise ignore `OutDir` and target files held by the open editor.

The native probe uses one compute shader and starts a new ShaderCompiler process through
SboxBuild for each case. It checks both output SHA-256 and modification time on reuse. The
compiler-change case modifies only a staged DLL and leaves the artifact manifest unchanged.
The test script is `Temp/shader-cache-validation/Verify-ShaderCache.ps1`.

Full VallArk editor restart validation remains separate from compiler-process restart tests.
Do not treat these checks as a measured reduction in full editor startup time or memory use.

## Results (2026-10-23)

- The managed solution build passed with zero warnings and zero errors, using the temporary
  output folder and disabled live deployment targets described above.
- All 28 focused unit tests passed: compiler identity, cache records, and atomic shader writes.
  Log: `Temp/shader-cache-validation/tests-final.log`.
- All 15 native process checks passed. An unchanged shader reused its output across fresh
  processes with identical bytes and modification time. A source edit, direct include edit,
  nested include edit, changed compiler DLL, restored compiler DLL, damaged output, and missing
  output each caused a compile. The next unchanged process reused each result.
- The compiler DLL test appended a byte outside the staged DXC PE image. The DLL still loaded,
  while its SHA-256 changed. The existing artifact manifest stayed stale. This tested actual
  compiler fingerprint invalidation without changing an ABI value or adding a version flag.
  The staged DLL was restored after the test.
- A further invalid-HLSL test failed as expected and preserved both the prior output and cache
  record. Restoring valid source reused that prior output in a fresh process.
- `git diff --check` passed. No push was made. Existing source changes were preserved.

Native details are in `Temp/shader-cache-validation/native-results.json` and the per-case logs.
Later unchanged process runs took 2.5–5.2 seconds including native process startup; the first
unchanged run took 18 seconds under memory pressure. These are correctness test durations,
not a controlled editor performance comparison.

The running editor was not closed, restarted, or updated. The new assemblies are in the
temporary validation folder. A normal local build/deploy and two VallArk starts are still
needed to verify the complete editor startup path after the user releases the current session.
