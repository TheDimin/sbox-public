# Current integration impact and evidence reuse

Stage `a4ee445500ed490e9a720d46a4e5e947` is live with Engine SHA
`60bc2b51bbd70c40a000f8b29f420714aff4e3dbda723794b18fdfc43b571396`.
Linear PNG16 production controls passed 61/61 (`4202567c…`) plus 5/5
follow-up (`69fce954…`), including public source/compiled realization and GPU
float sampling; sRGB16 is not claimed. Diffuse-transmission public source,
canonical compiled, and compatibility controls passed 3/3 (`40e408ee…`),
with measured nonidentity versus ordinary lit shading and visual review still
pending. Do not infer full extension parity or automatic external-image
packaging from these bounded tests.

The current corrective stage `6543fdeea62146c681a7732eb4a16585` has a verified
live Engine SHA `ed5f8073e45d3a4f90e5140323cc5c5a0dddf9707be71ee375966ca0f05403f0`
and named deformation shader SHA `4e5f9c9da78a82fcdef7ae72d0280a454697a0bf1eea6e46a83ca5a94c259c65`.
The public missing-tangent pose and playback checks passed (receipts `5af9f799…`
and `7c7ce236…`); the anonymous shader candidate separately passed missing,
authored, generated-corner and skinned controls (receipt `fecfef1d…`). Texture
batch failure-path controls passed (`f964373a…`); public embedded-GLB-derived
compiled-only material texture mip readback passed (`95dfec77…`). Matched
whole-model material readiness measured 31.680–31.967 s versus 61.792–63.706 s
before, with matching prepared-resource hashes (receipt `5d6937b5…` includes a
separate failed negative-control assumption). A borrowed valid error texture
case and external-image packaging remain unproven.
Prior receipts keep their original producers. Use one canonical compiled
`.glb_c` per model and retain additional source cases only for distinct spec
coverage; existing evidence remains valid in its original scope.

Website owner reports private main v21 at source `72df05be79a104f57475b4b13f5fa5b58adab0d7`:
73 captured views, 362 cases and 44 visual groups across 19 isolated models.
One canonical review each was added for MorphPrimitives, TextureLinearInterpolation
and Interpolation; existing v20 rows and verdicts remain preserved. Current
feedback was retrieved and merged; seven commented rejected models remain
unresolved and require their specific review comments and fingerprints.

Keep each receipt's original producer identity. A rebuilt assembly alone does not
invalidate an unchanged test: compare the relevant source, asset, shader, test
and oracle hashes before reusing its result. A changed dependency requires a
targeted rerun, not an automatic full-corpus replay.

| Changed area | Required new evidence | Reuse boundary |
| --- | --- | --- |
| Public unlit route and named shader | The 01459 public unlit checks passed. On e12 the mixed route passed targeted transformed-UV, candidate variant, override and lifecycle controls (receipt `267dcdfc…`). Mixed lit inputs remain limited to the legacy adapter's supported opaque material subset; public material variants remain unsupported. | Existing independent geometry and codec checks remain usable when their inputs and producer code are unchanged. Anonymous candidate proofs retain their original producer and do not prove the shipped route. |
| Scene selection and grouped reimport | Unselected/default/explicit identity and rendering, source/compiled aliases, lifetime, successful group reload and failure rollback | Prior default-scene rendering checks remain historical evidence for unchanged inputs; do not present them as a new selected-scene run. |
| Single-sided source material parameter | Actual template with and without the backface-only parameter, plus material realization and cleanup | Unchanged texture encoding and UV numeric tests can be reused; double-sided normal acceptance remains pending. |
| Directional factory | Native lifecycle, direction and independently calibrated photometric rendering | Earlier reader/plan tests retain their original scope; required punctual-light extension support is not established. |
| Opaque source-lit and corner compute | Named-shader generated-corner and skinned flat-normal regressions, actual public normal/AO source/compiled controls | Earlier anonymous shader observations keep their producer and scope. Normal, alpha and full PBR parity are not implied by compilation. |
| Opt-in sparse compilation | Source/package/dense accessor and GPU comparisons plus bounded load/allocation measurements | Default compilation and unrelated renderer receipts remain reusable if their inputs and relevant code are unchanged. |

The unlit named shader compile was interrupted by an editor shutdown on
2026-11-06. A later guarded offline compile succeeded for the exact tested
source, producing shader SHA256 `e494af0983bd7ab5c00c9ee8c78c3fc3cbd406eb9c63c86c466f9396ebf5251d`;
the interrupted attempt is not a success. Stage `01459` built and installed
the current Engine and Tools batch. Do not infer a loaded producer from that
installation or a saved marker; rediscover and verify the live process and binaries.
Stage `e12` includes the mixed-route and reload notification-order changes and
was installed after a normal unsaved-work audit. Focused reimport tests passed
6/6 (receipt `380c7c99…`). Stage `8fc` subsequently installed the conditional
opaque source-lit route, corner compute shader and opt-in sparse compiler.
Mandarin's formerly blocked single-sided normal/AO source and compiled public
captures passed on 8fc (receipt `cc763d92…`), and named corner/flat-normal GPU
regressions passed (receipt `046dcbb1…`). Double-sided public realization,
other material features and broader parity gates remain open.

Website review identity and grouping: 43 existing captured forms represent 25
unique decoded pixel/reference/review-scope comparisons; the 33 updated owner
forms represent 17 unique comparisons. Existing per-form fingerprints, producer
receipts and saved verdicts stay intact. A verdict or comment applies across
exact duplicate source/derived forms; changed frame pixels, timing, reference or
meaningful scope break sharing. Build IDs, timestamps and receipt-only updates
do not by themselves require another review. Website grouping validation and
publication are tracked by the website owner.
