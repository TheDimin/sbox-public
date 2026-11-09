# Current execution queue

2026-11-08: the runner, probe and helper code moved from `research/gltf-validation/` to `engine/Tools/Gltf/` (see [CODE_LOCATIONS](../CODE_LOCATIONS.md)). Dispatch paths in this file now point there. The move changed no queue state: no native job was dispatched, the editor was not launched, and the ready marker was not re-checked. Recheck the guard and the marker before the next dispatch. Frozen selections that name helper sources under the old research path need a new freeze.

The coordinator is `/root/integration_resume`. Devstation rebooted at 09:54
local time on November 7. The prior-boot marker named PID 533781/start 3180312;
the host guard and direct process check confirmed that PID was absent and there
were no editor reservations. The refreshed marker at
`Temp/gltf-validation/editor-session-ready.json` names PID 18734/start 80791,
boot `7de00756…`, Engine `041e2e00…` and glTF `e9370989…`; discovery SHA
`17f3ea25…`. Recheck the guard and the current marker before
each native dispatch. A marker is a locator for fresh PID/start, project,
responsiveness and loaded-producer checks under the guarded lease.

Do not rerun unchanged completed evidence merely because a producer changed:

- PNG16 production 61/61 `4202567c…`, follow-up 5/5 `69fce954…`, permanent
  regression 1/1 `91eba668…`.
- Diffuse-transmission public/compatibility 3/3 `40e408ee…`; convergence
  1/1 `3a470c3d…`.
- Point native cookie probe 1/1 `49670f6a…`; cookie shader probe 1/1
  `f7cac5c5…`.
- Field HDR shader 1/1 `3fa1bad4…`; exposure control 1/1 `aa578c7d…`.
- MorphPrimitives corrected source/canonical synthetic morph-and-skin cases
  passed 4/4 (`fc864156…`); the earlier invalid-fixture receipt is retained.
- Merged alpha candidate: four passed rows in terminal receipt `862ffdaa…`;
  the remaining two point/line cases passed `82cf9253…` using one compile and
  one scoped reuse (journal `4bfd5974…`).

AnimatedColors isolated parser tests passed 18 model and 8 synthetic checks.
Mandarin's HDRI/brightness correction passed its scoped capture gate
(`00ead190…`) and was published in private comparison version 30, deployment
`appgdep_6aee8fdf339081919b72a1ef5836e423`. Visual review is pending.

The isolated ABeautifulGame lease-transfer Engine build exited successfully
and released the lease. The Alpha/Primitive stage `e3add552…` passed its source,
baseline and output hash checks. The guarded three-shader compile and scoped
Engine installation passed (`60c634f0…`). The guard admitted one physical
editor launch for Survive. Its final editor window, idle `editor_status`,
graphics availability and all four loaded/disk module identities were verified.
Primitive public routing passed 6/6 (`86ca3055…`). Alpha lifecycle passed 6/7
(`1a905a3b…`, including the stock-vmdl image regression) and public tests passed
6/8 (`87d4292b…`). Three model-local fixture corrections required
no producer change: node-visibility opt-in and two standalone compiled
fixtures requiring embedded images. The scoped retry passed 3/3 (`9e022751…`),
so all 21 unique planned cases have passing observations across those receipts.
Retain the original failures as evidence. Point finite photometry passed 1/1
(`38e6cccb…`) and selected light ownership passed 2/2 (`a83b9528…`), both with
clean teardown. Two Alpha/Primitive matched-reference corpus captures completed
with successful load/render and cleanup. Their image handoff is in progress;
user visual review remains pending. Cameras' isolated System build passed, but
its Engine build failed on local variable shadowing in the private candidate.
The failed build is retained and a corrected freeze is being prepared.
AnimatedColors' corrected private stage `45fccb95…` passed glTF and Engine
builds with zero warnings/errors; its GPU helper compiled against that output
with one MSTest reference warning. The initial failed stage remains retained.
The revised Alpha source presentation passed 1/1 (`7806f9f4…`) with measured
fixed exposure and equal four-zero-delta-pair empty/source/deleted controls.
Its earlier failed receipts remain retained. The corrected camera Engine build
passed privately (`efb06e54…`), and the corrected 22-case helper inventory
compiled against its output with one MSTest reference warning. The texture
encoder-fingerprint candidate passed seven MSBuild checks, six focused cache
tests and the isolated Engine build (`f2cec12c…`). The Cameras existing-API
near-π probe returned terminal receipt `81014eae…` on installed Engine
`041e2e00…`. Finite custom and ordinary 45° raster controls matched exactly;
the near-π point query differed from the independent expected value by 0.06392.
Cleanup passed and the lease was released. No new candidate producer is
installed or runtime-tested.

## Model work after private comparison v31

The host revalidated PID 18734/start 80791 on the same boot. The editor was
responsive and idle in saved `pt1/scenes/lost.scene`, with no unsaved scene
changes. Keep the nine newly approved visual decisions attached to their exact
image fingerprints. Prioritize explicit review fixes and complex models.

- Camera private custom-query stage `d9e508e9…` passed its sequential System
  and Engine builds. Runtime query regressions await a reviewed producer.
- ABeautifulGame anonymous optical shader preflight passed 1/1 (`8356450b…`).
  Its thin framebuffer probe reached actual rendering (`458b0780…`). Copy
  callbacks and lit transmission-zero controls passed, but dark controls all
  rendered black. The optical effect remains under diagnosis.
- Mandarin alignment sweep passed 1/1 (`3bf4644d…`), covering 12 actual
  profiles. Its selected final four-state control passed 1/1 (`81c00d8b…`).
- NormalTangent canonical 1380×1302 capture passed (`4c5ddfa1…`); samples 2–7
  are pixel-identical. The first HD attempt stopped at script compilation.
- FlightHelmet required BasisU and original PNG source routes passed. The
  standalone canonical case failed before loading at the default embedded-image
  packaging budget. Receipt `d40f6709…` is terminal with 2/3 passing cases and
  clean teardown. The owner is measuring packaging charges and limits.
- CarConcept source and canonical both rendered (`c8d394f8…`). The canonical
  variant/reset checks passed; the source default-restoration image check failed.
- FlightHelmet's canonical-only retry has the next native slot, preserving the
  default budget refusal and exercising a separate explicit 384 MiB bound.
  AnisotropyBarnLamp follows. DragonAttenuation's pinned Footprint Court fixture
  is prepared for a scoped, separately coordinated environment import.

The next coherent managed build plan is
`Temp/gltf-validation/coherent-next.F0zygGox/plan.json` (`300b95e5…`). Its
private e3add snapshot verifies 25 overlay targets and 2,286 build inputs for
Cameras, AnimatedColors, texture lease transfer and encoder fingerprint. Its
private build runner verifies the plan, compiles System first, then stages glTF
and Engine. The CarConcept UV sign slice is tracked separately pending review
of the parser and material routes and actual source/canonical controls. The
combined build, installation and restart await their respective handoffs.

Coordinate before dispatching another expensive job.
All expensive jobs remain serialized; owners
retain their own receipt inspection and cleanup. Update this file when the
actual queue or ready identity changes, rather than inferring execution from
a scheduled request.

The user explicitly confirmed that validation jobs also use the installed
resource guard's admission policy. Estimate additional peak RAM honestly and
preserve all installed checks; do not impose an extra fixed 29 GB ceiling or
change the guard or operating-system settings.
