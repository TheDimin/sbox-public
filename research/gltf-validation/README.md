# glTF sample validation scaffold

Rendering acceptance runs in the real s&box editor, using its virtual display when
requested. The user chose editor-only validation on 2026-11-04; do not substitute
a standalone `TestAppSystem` render or manually pump engine frames for this proof.
Reuse an existing authorized editor and confirm its project with `editor_status`.
Use [editor_render.csx](editor_render.csx) through the discovered `run_csharp`
tool. It creates its own `SceneRenderingWidget`, waits for real editor frames,
and writes PNGs plus a scoped receipt under `Temp/gltf-validation/editor-render`.
It checks the pinned Triangle's source and packaged geometry, both windings and
camera sides, single/double-sided stock materials, shared material leases and
empty-frame controls. This is one geometry fixture, not full corpus parity.

[editor_texture.csx](editor_texture.csx) compiles and packages the pinned
BoxTextured sample inside the editor, compiles its PNG into a native RGBA texture,
and renders through the shared material cache and stock complex shader. It checks
all native texture texels, source/package render agreement, an untextured control,
and black initial/disabled frames. Its receipt and PNGs are under
`Temp/gltf-validation/editor-texture`. This covers a static base-color fixture,
not complete PBR, sampling, mip or material conformance.

[editor_cache.csx](editor_cache.csx) checks the actual registered
`GltfMaterialSceneSystem` in two private editor scenes, an asset lease that
outlives both, isolated edits, rejection after disposal and final material-wrapper
invalidation. It writes `Temp/gltf-validation/editor-cache/receipt.json` and does
not clear the shared cache. GPU release timing and game-context teardown remain
separate requirements.

The preview does not edit the open project scene. Confirm editor/compile state
and read new console output after running it. Engine and glTF module identities,
input and image hashes, frames, failures and the precise tested scope belong in
the receipt. Wait for the preview widget's actual `OnPreFrame` callbacks before
and after render submission, rather than only counting global application-loop
iterations. The receipt records callback counts, visibility, widget/camera sizes
and frame numbers. Readback in the editor's active view batch must follow the
render submission's completion.

The current Triangle geometry, winding and sharing cells pass, but deleting the
last visible model and rendering a fresh empty scene can retain the prior image.
Keep this baseline failing. An opt-in `SBOX_GLTF_CAMERA_CLEAR=1` diagnostic adds a
color-only command to the two private cameras at `AfterDepthPrepass`; it passes
the same strict empty-view controls. This is evidence for a managed clear-path
fix, not a production fix or permission to mark full parity complete. Default
and diagnostic captures use separate `editor-render` and
`editor-render-camera-clear` output directories. The process-local diagnostic
flag must be restored after use.

The `SBOX_GLTF_CAMERA_CLEAR=noop` control registers an action-only command at the
same camera stage, records execution on both cameras, and writes
`editor-render-camera-noop`. It still fails the two empty-view checks by 1,275
pixels in the current editor. Merely registering a custom command therefore does
not explain the explicit-clear diagnostic's success. Preserve this failing
control when investigating the renderer; no global pipeline change is included.

[editor_morph.csx](editor_morph.csx) constructs a pinned, self-contained glTF with
one animated position target, builds stock-shader native morphs and independent
CPU-evaluated reference models, and captures 0/+1/−1 weights. It records actual
controller names, requested and native-reported weights, preview updates and
strict empty controls. The default path currently fails for empty images and
negative weights. With the private camera-clear diagnostic enabled, 0/+1 match
exactly; −1 is clamped to zero by the public managed setter. The separate
`SBOX_GLTF_SIGNED_MORPH_OVERRIDE=1` diagnostic calls the existing generated native
setter without that clamp: readback reports −1 but the image still matches zero.
That rules out fixing only the managed clamp as a sufficient solution.

`SBOX_GLTF_PAIRED_MORPH=1` instead creates positive and negative delta targets and
drives both through the public API, explicitly zeroing the inactive partner. With
the private clear diagnostic, this passed all five 0/±1/±2 reference comparisons
with zero differing pixels and the +1→−1→0 transition on a single scene model.
The ±2 references use separately pinned static-weight glTF variants. The receipt
is under `editor-morph-camera-clear-paired`; other diagnostic combinations have
distinct output directories. This proves a position-morph construction option,
not production loader integration, normal/tangent morphs, combined targets,
skinning, memory efficiency or full deformation conformance. Pairing duplicates
target data and controllers; quantify that cost before selecting the compiled
representation. The baseline remains failing.

[editor_material_schema.csx](editor_material_schema.csx) reads the actual loaded
stock complex shader's 159 variables and 20 combos in the current editor and
writes `editor-material-schema/receipt.json`. It does not mutate the shader or
material. Metadata supplies candidates for subsequent controlled render tests;
it does not prove material equations, packing, per-texture UV/sampler behavior or
color space. The cache's scalar isolation check uses the exposed
`g_flRoughnessScaleFactor` and establishes parameter readback, not shading fidelity.

These editor observations used Engine module ID
`aa9884de-2db1-4752-aac1-d13ad9ea8d36` and glTF module ID
`b2fb1c46-6cba-4256-b064-6d56daae5acb`. Re-run against rebuilt modules; retain the
module IDs and diagnostic flags in every receipt. No standalone graphics host
was used for these observations, and the project's open scene was not modified.

This tool inventories every model variant in the pinned [Khronos sample assets repository](https://github.com/KhronosGroup/glTF-Sample-Assets) and aggregates independently produced evidence. The pin is [90d7ede14c7e280af263824604b427a1ca02cb66](https://github.com/KhronosGroup/glTF-Sample-Assets/commit/90d7ede14c7e280af263824604b427a1ca02cb66), a dated upstream commit, not a claim about current HEAD. It is recorded in `coverage.json`. No model assets are included in this repository.

This is scaffolding, **not an s&box adapter, glTF conformance validator, reference evaluator, image comparison or performance benchmark**. It checks enough of a `.glb` to read its JSON chunk, but does not validate the rest of the GLB or glTF specification. Artifact hashes establish file integrity, not that a claimed adapter ran or that its assertions are correct. Consequently even a well-formed, hash-matching evidence record reports `adapter-integration-pending` and cannot pass. No engine capability is established by running this tool alone.

## Corpus and licenses

Obtain the upstream repository and its complete Git LFS content through the project's approved network workflow, then check out the exact manifest commit. The tool does not download or alter it. A missing repository, wrong commit, local changes under `Models` or `LICENSES`, missing indexed assets, LFS pointer instead of an indexed asset or referenced buffer/image, unsafe paths and malformed asset JSON all prevent either pass. Do not run against a partial or modified corpus. Git status does not verify ignored files or changes outside these paths; CI should use a fresh, trusted checkout.

Preserve each model's upstream README, metadata, attribution and applicable license, including any relevant files under `LICENSES`, when obtaining, retaining, distributing or publishing results using its assets. License terms vary by model; repository code licensing is not a blanket license for model content. The report records available per-model provenance files; it does not interpret licenses. A model without either README or metadata is an inventory error. Review licenses and any asset defects individually.

## Run

```sh
python3 -B research/gltf-validation/validate.py --corpus /path/to/glTF-Sample-Assets > report.json
python3 -B research/gltf-validation/validate.py --corpus /path/to/glTF-Sample-Assets --evidence /path/to/evidence.json > report.json
python3 -B research/gltf-validation/validate.py --coverage-status > pending-integrations.json
python3 -B -m unittest discover -s research/gltf-validation -p 'test_*.py' -v
```

Exit status is 0 only for an `applicable_full_pass`, 1 for an incomplete or failed applicable assessment, and 2 for a fatal setup error. With no adapters or evidence, status 1 is expected. `--coverage-status` needs no corpus and emits the full machine-readable list of pending integration categories and acceptance coverage, including the Linux and managed-first renderer targets. The corpus report contains every indexed variant, actual declared `extensionsUsed` and `extensionsRequired`, external buffer/image paths, provenance paths, scope and separate proof status for parser, numeric, render and compiled output. Performance is reported separately. Index tags are recorded, not used to determine core status. Changing the selected extensions requires review and an explicit manifest change. Duplicate JSON keys are rejected. An unindexed `.gltf` or `.glb` in a conventional `Models/<model>/glTF*` variant folder is a fatal inventory discrepancy requiring investigation; it cannot disappear silently from coverage.

The report has two distinct outcomes. Once real integrations exist, `applicable_full_pass` will require at least one applicable case; every core-only variant and every variant using only selected extensions must have all four proofs, and every coverage item and the global benchmark must pass. **Any inventory error blocks it**, including errors in variants whose scope could not be established. Variants using at least one unselected extension are listed as excluded with the extension names and are never counted as supported; these exclusions alone do not prevent the applicable goal from passing. A mixed selected/unselected variant is excluded as a whole, so it cannot prove support for its selected extensions; the manifest requires separate evidence for each selected extension. `corpus_full_pass` additionally requires no excluded variants. Do not remove excluded variants from the index to make either result green. At present both outcomes are always false: no adapters are integrated.

`KHR_meshopt_compression` is explicitly pinned to Khronos glTF commit [3e9dfff91d52cd79fa0169e8453b7fdaca663fd8](https://github.com/KhronosGroup/glTF/commit/3e9dfff91d52cd79fa0169e8453b7fdaca663fd8); its [specification](https://github.com/KhronosGroup/glTF/blob/3e9dfff91d52cd79fa0169e8453b7fdaca663fd8/extensions/2.0/Khronos/KHR_meshopt_compression/README.md) is a release candidate, not a ratified specification. This is a separate pin from the asset corpus. Reassess maturity, compatibility and changes before advancing it.

## Evidence contract

The optional evidence file is JSON. `corpus_commit` must exactly match the pin. `cases` maps each `model/variant` ID from the report to four independent proofs: `parser`, `numeric`, `render` and `compiled`. `coverage` maps **every** `required_coverage` item in the manifest to its evidence. `performance` is a separate global proof. Each proof requires:

```json
{
  "result": "pass",
  "fallback": false,
  "adapter": {"name": "adapter-name", "version": "version-or-revision"},
  "artifacts": [{"path": "relative/path/to/evidence", "sha256": "64-lowercase-hex-characters"}]
}
```

Artifact paths are relative to the evidence file's directory and must remain within it. Render proofs also require nonempty `reviewer`, `reference` and `conditions` strings. Performance proofs also require `hardware`, `thresholds` and `metrics` strings. Real artifacts must contain the results and provenance that substantiate these assertions. A failed, skipped, missing or fallback proof cannot pass. Invalid or mismatched hashes cannot pass. Even correctly shaped records with matching hashes remain pending: adding a reviewed executable adapter integration, binding its exact version and inputs to the run, validating its output and defining trustworthy visual-review and benchmark provenance are prerequisites for replacing the explicit gate. Merely registering adapter names, versions or hashes from the evidence file is insufficient. Tests mock completed adapter results solely to exercise future aggregation; they do not validate engine behavior.

The current gate requires all four case proofs even where an asset has no visible geometry or animated values. Define case-specific applicability through a reviewed extension of this contract before relaxing it; absent results are not assumed inapplicable. Coverage IDs are placeholders for independently reviewed concrete fixtures and acceptance criteria, not claims that those tests exist. In particular, thresholds for animations and performance remain to be agreed after measurement.

## Integration still needed

- An official glTF validator adapter and an s&box import/load adapter reporting reproducible errors; rejection of unsupported required extensions and optional fallbacks need dedicated negative tests.
- Reference numerical evaluation at keys and between keys, including STEP boundaries, skinning, morphs and transformations, with measured and approved tolerances.
- Captures and human visual review against a documented reference renderer under matched conditions; preserve image, renderer versions and review records.
- Actual portable `.glb_c` generation, structural validation, dependency packaging, source and compiled runtime behavior, plus editor, reimport and vmdl parity fixtures.
- Matched vmdl and `.glb_c` static and animated benchmarks: load time, process memory, available GPU-memory evidence and package size, with agreed thresholds and hardware/run details.
- Linux execution first, then Windows validation before the full goal is complete, as requested. Keep separate platform evidence, a machine-readable mapping from coverage IDs to concrete tests, pinned tool versions, CI artifacts and a reviewed process for documented upstream defects and scope exclusions.
