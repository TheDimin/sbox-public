# Shared context for glTF model workers

Use this context for one assigned model in the sbox-public direct GPU project. Read applicable `AGENTS.md`, the installed s&box CLI instructions, and the [direct GPU contract](CONTRACT.md). For resource or component work, also read [Model.Load integration](MODEL_LOAD_INTEGRATION.md). [Execution coordination](EXECUTION.md) describes the runner and handoff; [native capture controls](../NATIVE_CAPTURE_CONTROLS.md) covers camera setup.

The assignment identifies the model, owned files, integration coordinator and shared-backend owner. Verify current owners and live state with the assigning agent; saved agent names, PIDs, ready markers, installed versions and receipts are historical evidence, not authorization or proof of current state.

## Source and scope

The reference corpus is `Temp/gltf-reference/glTF-Sample-Assets`, from [Khronos glTF-Sample-Assets](https://github.com/KhronosGroup/glTF-Sample-Assets) at commit `90d7ede14c7e280af263824604b427a1ca02cb66`. Verify the checkout and assigned input hashes. Query the assigned model in the [corpus manifest](../corpus-case-manifest.json) for actual variants, dependencies, licenses, extensions and existing coverage. Preserve licenses and do not invent variants.

The conformance baseline is published glTF 2.0.1, registry commit `8e798b02d254cea97659a333cfcb20875b62bdd4`. Consult primary specifications before changing compatibility policy; see [data](../DATA_VALIDATION.md) and [animation](../ANIMATION_VALIDATION.md) notes. Reject unsupported required extensions clearly and report standards-defined optional fallbacks separately. Unimplemented core features remain applicable; resource admission limits leave cases pending. The [feature status](../FEATURE_STATUS.md) tracks broader gaps.

The goal includes glTF 2.0 core, selected extensions, source and portable optimized `.glb_c` loading, existing model APIs and eventual editor parity. Linux evidence comes first; Windows and matched compiled-vmdl load/memory benchmarks are separate completion gates. A one-model assignment does not reduce that goal. Follow the contract's direct GPU architecture: native-model captures are historical evidence, not a substitute for the managed buffer path or public `Model.Load` acceptance.

## Ownership and execution

Audit the assigned source and existing tests first. Work in the assigned model folder and any explicitly handed-off files; preserve unrelated changes. Coordinate shared interfaces and fixes with their owner rather than editing shared backend, rig, shader, reader or global manifest files concurrently. Use an independent numeric or literal-geometry oracle with meaningful controls; producer output, generated images and source-versus-compiled agreement cannot be their own expected results.

Model owners run their own tests and captures; the integration owner coordinates shared builds, shader installation and any authorized editor restart. For native work hold `/tmp/codex-sbox-gltf-model-tests.lock` across guarded admission, request copy, execution and receipt inspection. Follow current `AGENTS.md` resource and editor rules, revalidate the live editor identity, project, responsiveness and loaded binaries, and keep shader source outside watched content until the integration owner deliberately compiles it. A prior Survive launch estimate was 14 GiB; assess its relevance alongside current comparable measurements and margin. Do not bypass the guard or launch another editor to avoid waiting. See [execution coordination](EXECUTION.md) for the runner and selection manifest.

Freeze the request before execution: include the model, owned source paths and hashes, producer dependencies and agreed contract, callable entry/setup, expected case count and source/compiled forms, independent assertions and controls, evidence scope, and build/install/restart/shader/capture needs. Do not edit a submitted freeze during a run; corrections require a new freeze and the prior failure remains evidence. Copy the runtime selection manifest under the native test lock, inside the guarded child immediately before dispatch, as the runner specifies. Do not silently rewrite historical manifests or receipts to admit new source. Receipts should identify actual producer, helper, shader and source identities, case coverage, requested and observed clocks, observations and artifact hashes; helpers must not compile replacement producer code.

## Evidence and handoff

Report planned, executed, failed and incomplete cases separately. CPU/data, staging plans, actual GPU readback, rendered comparison, wall-clock cadence and public API behavior are distinct gates. A deterministic seek does not prove playback cadence; diagnostic materials do not prove source-material rendering. Verify capture mode: `SceneCameraDebugMode.Normal` is ordinary lit rendering; `NormalMap` visualizes normals.

Compare against the pinned source media and record the camera, presentation and animation recipe, distinguishing authored settings from inferred framing or unknown lighting. Do not label a generic fit as an exact match or impose automatic pixel parity when the original presentation is unknown. Final appearance approval belongs to the user. Missing media or captures remain explicit results. A change to reference bytes, recipe, producer or capture invalidates visual approval.

The Run-all site presents the current run: use the agreed runner/exporter to replace stale rows and captures. Historical images need not be retained as current output; preserve relevant source, test and receipt provenance. Empty/deletion/negative controls remain useful targeted diagnostics without blocking human review. Report actual blockers and continue independent work; the managing agent audits full-goal completion.
