# glTF tooling

Validation tooling for glTF/GLB support. Docs, receipts, corpus manifests, reference recipes and fixtures stay in `research/gltf-validation/`, and the scripts read them from there.

## Layout

- `editor/`: `editor_*.csx` probes. They run inside the s&box editor through `scripts/run_editor_probes.py`, whose guard accepts probes only from this folder.
- `scripts/`: Python and Node runners. Includes `Run` (corpus driver), `run_editor_probes.py`, `run_editor_tests.py`, the comparison-site exporters, the ingest scripts and their unit tests.
- `direct-gpu/`: per-model code. C# test helpers, request builders and runners, candidate patches, the capture diagnostics harness and the texture benchmark. Each model keeps its code here and its requests, results and docs in `research/gltf-validation/direct-gpu/models/<Model>/`.

## Running the Python tests

```
cd engine/Tools/Gltf/scripts
python3 -m unittest discover -p 'test_*.py'
```

`test_visual_comparison_identity` fails. It expects 33 isolated executions and finds 74. This is a known data-count mismatch.

## Not in this folder

- Engine code: `engine/Sandbox.Engine/Resources/Model/Gltf/` and `engine/Sandbox.Gltf/`.
- glTF tests: `engine/Tests/Sandbox.Test.Gltf/`.
- Unmerged candidate sources and patches. They stay beside their model code, for example `direct-gpu/models/Cameras/draft/` and `direct-gpu/models/AnimatedColorsCube/candidate/`. No project compiles them. Review them before copying anything into the engine.

Historical paths cited by older receipts are mapped in [CODE_LOCATIONS](../../../research/gltf-validation/CODE_LOCATIONS.md).

## Compare site (`compare/`)

A local site that shows each Khronos reference image next to the s&box capture from the latest `Run`.

```bash
python3 engine/Tools/Gltf/compare/serve.py   # then open http://127.0.0.1:8765/
```

- Views: side by side, or a wipe slider.
- Feedback: a verdict (correct, close, wrong), issue tags and notes for each case.
- The server writes feedback to `research/gltf-validation/review-feedback.json`. Each entry stores the capture SHA-256, so a verdict for an older capture shows as stale after a new run.
- Keys: `J`/`K` next/previous, `1`/`2`/`3` verdict, `Ctrl+Enter` save.

## Reference renderer (`reference-renderer/`)

Renders our own reference images with the official Khronos glTF Sample Renderer (`@khronosgroup/gltf-viewer` 1.1.0) in headless Chrome. The camera for each case is the same as in the s&box capture recipe. Presentation uses the Sample Viewer startup defaults: Cannon Exterior HDRI, exposure 1.0, Khronos PBR Neutral tone map, IBL intensity 1.0, environment rotation 90°, blurred HDR background.

```bash
python3 engine/Tools/Gltf/scripts/build_reference_render_settings.py   # writes research/gltf-validation/reference-render-settings.json
cd engine/Tools/Gltf/reference-renderer && npm install
node render.mjs                     # all cases; skips cases that already have result.json
node render.mjs --model Fox --force # one model again
```

- Output: `Temp/gltf-reference/rendered/<Model>/<Variant>/reference.png`, `frames/` for animated recipes, and `result.json`.
- Assets: `Temp/gltf-reference/sample-viewer-assets/` holds the HDR, the sheen LUT and the Draco decoder.
- Resolution is twice the s&box capture resolution.
- GPU: Chrome uses ANGLE on EGL (`--use-angle=gl-egl`). The Vulkan backend gives no WebGL2 on this machine.
- The compare site shows these renders by default, with a switch to the Khronos screenshot.
