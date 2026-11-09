# glTF comparison website

[Open the private comparison site](https://sbox-gltf-parity.fancy-chord-2986.chatgpt.site).

The comparison library covers the pinned Khronos corpus: 150 models and 337
actual format variants. It shows one current run, one reference and one current
engine capture per variant. A failed or missing capture never falls back to an
older image. Loading and capture results are automatic; visual approval belongs
to the user.

## Run and export

The single entry point is `engine/Tools/Gltf/scripts/Run`. Its current result is
`Temp/gltf-validation/current/run.json`; the generated website data and media are
under `Temp/gltf-validation/current/website`. The runner owns actual public
loading, capture, dependency checks, cleanup and interrupted-run reporting.
Every result includes all corpus case IDs, including explicit unrun or blocked
rows. It does not publish the website automatically.

[Reference recipes](reference-recipes.json) describe the pinned source artwork,
camera, presentation and animation setup. Inferred setups remain marked as
needing visual tuning. A recipe is not a claim that the images match.

[The current exporter](../../engine/Tools/Gltf/scripts/export_current_comparison.py) joins the run, recipes and
[corpus inventory](corpus-case-manifest.json). It verifies source identity and
media hashes, stages replacements before changing the current files, and removes
superseded generated website media. It does not append revision arrays or copy
diagnostic reports into the comparison library. For a standalone export:

```bash
python3 engine/Tools/Gltf/scripts/export_current_comparison.py \
  --output Temp/gltf-validation/current/website
```

The hosted source checkout is `Temp/gltf-validation/comparison-site`, project
`appgprj_6aec4257fe60819194f3bd64da17be8e`. The Sites owner must open that existing
source through the official helper before replacing its `dist` data/media, then
package and publish through the official Sites workflow. Preserve `.openai`
identity and the owner-private audience. The engine repository is not published.

## User reviews

Approve, reject and pending decisions are stored in the user's browser, with
notes and explicit export/import controls. There is no server-side review store.
Approval requires an actual displayed capture; rejection is available for
missing or failed captures as well. Import accepts only decisions for the exact
current comparison identity.

A comparison fingerprint covers source/dependency content, actual engine
producer, reference artwork, capture content and applied comparison recipe.
Changing any of these invalidates an earlier decision. Automated results never
set a visual approval or reinterpret a user decision as a test pass.

## Checks

Run `python3 -m unittest discover -s research/gltf-validation -p
test_export_current_comparison.py`. Validate website JavaScript and interaction
state separately. Browser visual review is performed by the user; automated
syntax/data checks are not a substitute for that decision.

## Current execution scope

The current dataset is **interrupted**, not a completed full sweep: 200 of 337
variant checks returned results, with 83 captures across 34 models and 117
ordinary loading errors. The 83 captures comprise 62 stills and 21 sampled
animations. Another 137 variants are blocked: two attempts ended in native editor
SIGSEGV without a returned result (BrainStem glTF and MetalRoughSpheresNoTextures
glTF), and 135 were not attempted. The native cause remains unproven. All visual
reviews start pending; captures are not automated appearance approvals.

Run against the existing, verified visible editor for this checkout:

```bash
./engine/Tools/Gltf/scripts/Run
./engine/Tools/Gltf/scripts/Run --case SimpleSkin/glTF
./engine/Tools/Gltf/scripts/Run --resume
```

`Run` neither launches an editor nor publishes the site. The full command starts
all pinned variants; the case command is a bounded diagnostic. Targeted runs and
resume retain completed rows only when their actual producer, source/dependencies
and requested recipe are unchanged. Changed selected recipes are rerun; changed
unselected cases are blocked and cannot show their stale captures. Each new row
stores its requested recipe snapshot and fingerprint, with one current full
document at `Temp/gltf-validation/current/recipes.json`.

For a one-time migration of older results without per-case snapshots, supply the
exact prior recipe JSON whose file hash matches that run's recorded recipe hash:

```bash
./engine/Tools/Gltf/scripts/Run --model Fox \
  --previous-recipes /absolute/path/to/previous-recipes.json
```

Missing or mismatched proof refuses the migration. This option does not bypass
producer checks: an engine, capture-driver or stock-resource change invalidates
old capture reuse; `--resume` requires the same rendering build. The on-disk
stock proof hashes the default shader/material family and recursively follows
compiled RERL dependencies. Shader mode delegation requires audited compiled
parent bytes; a new shader build needs a fresh delegation audit before reuse.
Missing or unproven compiled dependencies and legacy results without this proof
cannot certify reuse. This covers known material inputs, not live native-cache
identity or every camera/postprocessing dependency. New captures retain their
per-case stock proof; export rejects missing or stale proofs before writing.
Untouched legacy comparisons stay readable with their original fingerprints.
A guard refusal
leaves the existing matrix, recipe snapshot, assets and website files intact.

After a process
failure, verify the old process is dead and recover the correct visible editor
through the resource guard before resuming. Do not loop restarts around a native
crash. Start a fresh Run after changing engine capture inputs. The actual resume
selected 272 remaining cases, retained 59 completed results and completed 141
more before the second interruption. The SimpleSkin diagnostic produced 129
actual sampled frames over 14.32 seconds; it was replaced by the current sweep.

## Purpose and visual review guidance

`../../engine/Tools/Gltf/scripts/model_review_guidance.py` covers all 150 pinned models and 337 variants. The
site quotes a short hash-verified Khronos README summary separately from our
recommended review checks. Variant guidance comes from the pinned source
inventory; it is not evidence that a feature passed. This is a sample collection,
including showcase and tutorial assets, not 150 formal conformance tests.

Check the stated geometry, material or animation behavior using normal s&box
rendering. For Box, a visible cube is only the first check: inspect the red
non-metallic material, sharp face normals and sensible face shading. Exact
reference brightness is not an acceptance gate without matched illumination
and exposure. Material ambiguities require controlled lighting; normal-map
fixtures need their documented view and environment. Missing motion or unshown
clips remain unproven. User approval applies only to observed features.

Guidance is explanatory metadata outside the comparison fingerprint. Editing it
must retain capture identities and saved user decisions. Run the focused
`../../engine/Tools/Gltf/scripts/test_model_review_guidance.py` tests and the current comparison DOM harness
alongside exporter checks. These checks never judge visual correctness.

## Main comparison integration and visual review identity

`../../engine/Tools/Gltf/scripts/integrate_owner_comparisons.py` verifies the bounded owner manifest, receipt SHA256s, copied media and source frame pixels before attaching each individually executed form to the main comparison flow. Derived compiled forms have distinct `::compiled` IDs and retain their upstream source form, native receipt and producer. `evidenceCollection.type=mixed-individual-executions` explicitly distinguishes this collection from a combined batch run. `/latest` redirects to the main flow. Earlier corpus evidence retains its own scope.

The default Updated—needs review filter includes only visually changed owner comparisons awaiting a decision. Review identity version 2 uses decoded RGBA frames, dimensions, frame durations, loop behavior, decoded reference pixels, clip coverage, sampling mode and meaningful applied review limitations. Producer/build IDs, timestamps, PNG metadata and container bytes remain provenance, not review triggers. Earlier decisions migrate only after their exact historical Git evidence passes the same decoded comparison. The original feedback file is unchanged. Browser-local superseded decisions are kept in exportable history; importing stale fingerprints cannot approve new frames.

Actual captured frames can be paused, stepped and scrubbed. Animated upstream references use normalized recorded time for linked scrubbing; the UI explicitly warns that this does not establish matched camera, brightness or exact animation parity. Review stills remain still references. Approval requires loaded engine media and is always a user action.

Focused checks: `python3 -m unittest discover -s research/gltf-validation -p test_visual_comparison_identity.py`, exporter/queue tests, both UI harnesses (including `--current-data`) and browser verification of actual frame stepping, comment persistence and derived-form image loading. Captured output remains user review evidence, not automatic visual or full corpus acceptance.

## Exact duplicate visual comparisons

`../../engine/Tools/Gltf/scripts/group_visual_reviews.py` derives a shared identity from the existing decoded capture frames/timing, pinned reference pixels and meaningful scope, within the same model. It keeps existing per-form comparison fingerprints, producer receipts and saved verdict records unchanged. A current verdict/comment on one exact duplicate covers the identical peers; prior single-form exports work without asking the user to repeat their judgment. A changed frame, timing, reference or scope breaks sharing. Conflicting pre-existing per-form decisions are retained until the user changes the group decision. The queue lists one review link per unique visual comparison while every source/derived form remains inspectable in the format selector.

The initial grouping has25 unique comparisons across43 captured forms, including17 unique comparisons across33 updated owner forms. The grouping change ran only three new identity checks and four new UI grouping checks (`test_current_comparison_ui.cjs --grouping-only`); previously passing validation suites and captures were not rerun.

## Incremental completed-model ingest

`ingest_completed_model.py <owner-result.json> --sha256 <verified-owner-hash>` consumes a completed public owner manifest and its already executed receipt. It verifies the declared loaded producer, current public route, pinned reference, copied media and group scope; it does not rerun tests or captures. Unrelated comparison rows and saved verdicts must remain exactly unchanged. No prior anonymous/source-helper captures are promoted into this public evidence. `test_current_comparison_ui.cjs --current-data --model-only UnlitTest` checks only the four added comparison states.

The UnlitTest increment reused its12/12 public receipt for four source/derived forms sharing one visual comparison. Mixed reachable lit/unlit scenes remain explicitly rejected; unknown upstream camera/exposure, Windows/editor/performance and final user appearance approval remain limitations.

The MandarinOrange increment uses the final8fc lazy-material-realization receipt (`1ca782cf…`) for source and derived compiled forms. The primary reference is the complete authored `with-without.jpg`: its RIGHT half is the standard optional diffuse-transmission fallback; the LEFT half enables the extension. The extension-enabled screenshot remains an additional reference. A prominent notice records the inferred camera/light/exposure and prevents a calibrated pixel-parity claim.

Later Unlit e12 mixed-path receipts appear as `currentProof` supplemental capability evidence. They retain their own producer, while primary01459capture provenance, visual/group fingerprints and verdicts remain unchanged. Current presentation notes supersede the old blanket mixed rejection, and the full recorded capture-time setup remains labeled historical evidence. Synthetic mixed controls are not added as upstream model review images.

## BrainStem still review and content license

`../../engine/Tools/Gltf/scripts/ingest_brainstem_stills.py` publishes12 actual public first-key rendered PNGs and a verified extraction of pinned upstream GIFframe0. These form six exact decoded review groups. The primary flow is still-only; the sparse61-capture actual-clock APNG (including its duplicated final encoding frame) is not copied or shown as engine animation evidence. Pose correspondence, camera/light/exposure and final appearance remain unmatched and explicitly pending. Numerical proof samples three vertices per primitive at four phases and full index connectivity; it is not all-vertex/full-spec proof.

BrainStem content attribution is Keith Hunter/Smith Micro Software, Inc.,2017, Poser EULA, with pinned LICENSES/LicenseRef-Poser-EULA.txt linked. Metadata-only CC BY does not label the content. The Site contains rendered/reference raster media only; no BrainStem model/geometry/texture payload is packaged. Original full GIF is an upstream link, not an autoplaying matched reference.

The NormalTangentTest increment (`../../engine/Tools/Gltf/scripts/ingest_normal_canonical.py`) publishes only its two512×483 canonical glTF/glb source captures. Four public numerical forms are proven across the original3/4receipt and the corrected dependency1/1receipt; the historical failure remains explicit. Derived compiled192px diagnostic images are not promoted, and external-image dependencies remain required. The notice calls out brighter output, absent unknown HDRI horizon, inferred camera/light/exposure and pending appearance/PBR/AO acceptance. Numerical proof and appearance review remain separate.

The MaterialsVariantsShoe source-only increment (`../../engine/Tools/Gltf/scripts/ingest_shoe_source_views.py`) contains three tighter192px material views: Default/Midnight authored material0 once, Beach and Street. Source default restoration is byte-identical. Overall model acceptance remains incomplete: compiled tight-frame reset differs by69pixels above64threshold, and prior compiled loader success required mounted external JPEGs. No compiled canonical capture or full-model pass is claimed. `materialVariant` enters visual-review context only when present, so existing unselected comparison identities remain unchanged.
