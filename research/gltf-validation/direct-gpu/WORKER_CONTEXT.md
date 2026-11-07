# Shared context for glTF model workers

This file contains durable project direction and the model-worker handoff protocol. Live process identity, loaded producer versions, current source freezes, job handles and latest receipts belong to the validation coordinator and [execution log](EXECUTION.md); discover them again after restarts. No saved PID or old result grants an execution slot.

Current coordinator: `/root/today_integration`. Current website and publication
owner: `/root/today_website`. These current assignments supersede historical
worker names in earlier skills and submissions; verify live availability before
sending an execution or evidence handoff.

The newer user-provided November 7 AGENTS instructions make the installed
resource guard authoritative for both launches and expensive jobs. Budget
additional peak memory honestly using comparable measurements and margin;
defaults are estimates, not mandatory minima. Keep the 14 GiB Survive launch
estimate, serialize expensive jobs, and do not change or bypass the helper or
OS policy.

## Read before acting

Read applicable `AGENTS.md` and installed s&box CLI skills, then [the direct GPU contract](CONTRACT.md). Read [Model.Load integration](MODEL_LOAD_INTEGRATION.md) when touching resource/component behavior. [Execution coordination](EXECUTION.md) defines actual runners, selection requests and capture ordering. [Native capture controls](../NATIVE_CAPTURE_CONTROLS.md) records the valid native camera route and failed alternatives. Follow these files rather than copying their implementation into a model folder.

The implementation goal remains complete glTF 2.0 core plus the selected practical extensions, source loading and portable optimized valid `.glb_c`, existing model APIs/vmdl behavior and eventual model-editor parity. Linux evidence comes first; Windows and matched compiled-vmdl load/memory benchmarks remain completion gates. This worker's one-model assignment does not shrink that goal.

## Corpus and specifications

The reference checkout is `Temp/gltf-reference/glTF-Sample-Assets`, repository `https://github.com/KhronosGroup/glTF-Sample-Assets.git`, pinned commit **`90d7ede14c7e280af263824604b427a1ca02cb66`**. Verify the actual checkout and assigned input hashes. Its [corpus case manifest](../corpus-case-manifest.json) enumerates models, actual format variants, dependencies, licenses, required/optional extensions and existing oracle coverage. Query just the assigned model instead of dumping this large single-line JSON. Preserve asset licenses and do not invent an upstream variant that does not exist.

The selected extension list is maintained in that manifest. Unsupported required extensions need a clear rejection; optional extensions use their standards-defined fallbacks and must be reported separately. Valid core features stay applicable when unimplemented. Resource admission limits produce pending cases, not exclusions or passes.

Published glTF 2.0.1 (registry commit `8e798b02d254cea97659a333cfcb20875b62bdd4`) is the current conformance baseline. [Data](../DATA_VALIDATION.md) and [animation](../ANIMATION_VALIDATION.md) notes distinguish newer draft compatibility policies. Check primary specifications before changing those policies. The [feature checklist](../FEATURE_STATUS.md) tracks broader gaps; an old count is not current coverage.

## Architecture boundaries

The new glTF path owns C# GPU buffers, descriptor-driven compute and explicit write-to-draw barriers, runtime vertex declarations, and raw submission through a `SceneCustomObject`. It constructs **zero native Model, Mesh, IModel, ModelBuilder or native morph controllers**, including placeholders. Useful native camera/light/presentation, stock s&box shader/material and drawing services remain allowed. Existing native-model implementation/results are historical evidence; do not extend that path to make a model green.

Input component type, normalization, accessor/bufferView offsets, stride, sparse/implicit-zero state and all influence sets come from the file and materialized stream descriptors. No assumed `GltfVertex` input layout. Output packing may differ only through an explicit runtime declaration and actual conversion. Shared assets own immutable input/material resources; instances own output, pose clock, palette, morph weights, placement and overrides. Follow the contract for full-affine transforms, morph-before-skin order, source basis/units, winding and lifetime.

The research helper does not complete the public loading API. The installed bounded `Model.Load` path now uses the managed backend, with selected ordinary/skinned public proofs; existing vmdl paths keep their native branch. Helper-only APIs or hidden native models cannot satisfy remaining public acceptance. The integration document distinguishes completed scopes from the remaining API, identity and packaging work.

## Ownership and useful parallel work

One worker owns one assigned model folder under `models/`. Read the current code/tests and audit the source before questions or suggestions. Build an independent numeric or literal-geometry oracle; do not use the producer's evaluator, generated image or source-versus-compiled agreement as its own expected result. Tests should detect a plausible wrong offset, phase, transform, normal or material behavior. Unknown behavior remains a measured question, not an arbitrary tolerance increase.

Capture naming: `SceneCameraDebugMode.Normal` (value 0) is ordinary lit
rendering, while `SceneCameraDebugMode.NormalMap` (value 21) visualizes normals.
Check the actual enum and capture mode before describing a result; do not
recapture an unchanged source-lit image merely because `Normal` was misread.

The assigning agent names the shared-backend owner and the single execution coordinator. Agree shared interfaces before changing consumers. Send shared defects, a reproducer and proposed acceptance to that owner; do not concurrently edit shared backend/rig/shader/reader files or global manifests. Extra files outside the model folder require an explicit ownership handoff. Preserve unrelated dirty work.

Model owners execute their own meaningful unit and native tests and captures. Native jobs serialize through `/tmp/codex-sbox-gltf-model-tests.lock`, held across guarded admission, request copy, execution and receipt inspection; the installed host resource guard and editor identity checks remain mandatory. The shared integration owner builds and installs shared engine binaries or shaders and coordinates any safe, audited normal editor restart. Revalidate the live PID/start identity, project, editor status and loaded binaries; old receipts and ready markers are locators only. Follow the current user-provided AGENTS.md resource rules and estimate additional peak memory from comparable workloads plus margin; the installed helper decides admission. Source shader text stays outside watched live content until the integration owner deliberately compiles it. Do not launch an alternative editor, run a detached job or bypass admission to keep a worker busy. The current website and publication owner is `/root/today_website`; `/root/site_evidence_update` is a stopped historical owner. Send new bounded receipt and capture handoffs to the current owner.

## Submit a frozen validation request

Record a compact frozen request and send the shared integration owner any dependency or shared-fix needs:

```json
{
  "model": "Triangle",
  "owner": "/root/model_triangle",
  "files": [{ "path": "research/gltf-validation/direct-gpu/models/Triangle/ModelTests.cs", "sha256": "computed complete SHA-256" }],
  "producer_dependencies": ["loaded Sandbox.Gltf version supporting the APIs used"],
  "shared_backend_contract": "CONTRACT.md and the agreed source freeze",
  "entry": "fully qualified callable method and arguments/setup",
  "expected_cases": 2,
  "forms": ["actual upstream source variant", "derived compiled GLB"],
  "scope": "CPU, staging-plan, GPU-readback, image or wall-clock; identify each",
  "acceptance": "independent expected data, positive/negative controls and reviewed thresholds",
  "execution_needs": "build/install/restart/shader/capture requirements, or none"
}
```

This is a submission example, not executable case inventory. Use actual hashes/counts/names; separate planned, executed, failed and incomplete cases. The executing model owner creates the frozen [runtime selection manifest](../../../Temp/gltf-validation/direct-gpu/current-tests.json) using the runner's documented schema. Copy it only while holding the native test lock, inside the guarded child immediately before dispatch; that runtime file may be absent or stale. The older [pure-test manifest](../gltf-test-manifest.json) describes its own suite and is not automatically the inventory of newly added model or backend tests. Do not rewrite historical manifests/receipts to admit new source silently.

Do not edit submitted files while execution is active. Corrections need a new freeze and the prior failure remains evidence. Receipts identify loaded producer MVIDs/hashes, helper/shader/source identity, case/form coverage, requests/observations and artifact hashes. Never compile replacement producer source into a test helper to simulate an installed change.

CPU/data success, staging-plan success, actual GPU output, human visual review, wall-clock cadence and public API behavior are separate gates. The current Run-all comparison uses the pinned upstream image/video and a recorded per-case camera/presentation/animation recipe. Inspect the actual reference; classify authored settings separately from inferred framing or unknown lighting. A generic fit must never be labeled an exact upstream match. Final source-versus-engine appearance approval belongs to the user; do not impose automatic pixel parity against artwork whose original renderer/camera/light/exposure is unknown. Missing source media or an unimplemented capture stays an explicit current result rather than a fabricated substitute or a reason to omit the case.

The Run-all website presents the current run only. Overwrite/regenerate current capture artifacts and replace stale comparison rows through the coordinator's agreed runner/exporter; historical images and videos are not mandatory retained comparison output. Git and scoped test/receipt provenance preserve useful history without resurrecting old captures as current evidence. A visual approval is invalidated when source/reference bytes, recipe, producer or actual capture changes. Empty/deletion/negative-image controls remain useful targeted infrastructure or numerical regression diagnostics, but a mandatory historical archive or a bespoke image oracle must not obstruct the new human-reviewed Run-all workflow. Continue independent spec/numerical/unit tests with their explicit assertions; this visual policy does not weaken them. Deterministic seeks do not establish cadence or performance, and diagnostic materials must remain distinguished from source-material rendering.

Report a real blocker with its actual failed evidence and the decision it needs. Continue independent work that does not depend on it. Leave full-goal completion to the managing agent's requirement-by-requirement audit.

## Fresh-worker spawn example

Use the actual current root, names, model and owned folder; no full chat history is needed:

```javascript
collaboration.spawn_agent({
  task_name: "model_triangle",
  fork_turns: "none",
  message: "Use $sbox-gltf-model-worker at /home/damian/.codex/skills/sbox-gltf-model-worker/SKILL.md. Repo: /home/damian/projects/sbox-public. Assigned model: Triangle. Own research/gltf-validation/direct-gpu/models/Triangle/ and any explicitly handed-off files. Read the skill and repository context before acting. Audit source/tests, write independent tests, implement the owned slice, and execute your own guarded tests and captures under the shared native lock. Coordinate shared fixes and installations with the integration owner; report evidence and remaining gates."
});
```

The tool's currently available concurrency governs dispatch. This project context does not change global worker limits or authorize extra processes.
