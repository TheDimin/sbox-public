# ABeautifulGame loading baseline, stage 8fc

Pinned Khronos `glTF-Binary` SHA-256
`bd7133b4b322aae97c589b8839dae8155ad2546acb35ae32a127e722a959d007`.
No sample texture or quality setting was changed. The original license and
asset pin are maintained by the ABeautifulGame owner.

The guarded three-iteration benchmark used the live VallArk editor, Engine MVID
`0ce2dfa5-c98c-45e4-ad3a-6c810a17a6ef`, glTF MVID
`075675b7-ad83-46c0-8a5d-3c53301f2d9c`, warm OS file cache, and fresh temporary
resource aliases for each public load. Receipt SHA-256
`32d412877a7d3a55fe50dd639367621b1ed209c9b229e14cfc15946da2f0309b`:
`Temp/gltf-validation/direct-gpu/texture-benchmark/baseline-8fc/probe/probe-01-editor_direct_gpu_tests/receipt-01.json`.

| Metric | Run 1 | Run 2 | Run 3 |
| --- | ---: | ---: | ---: |
| Prepare fifteen materials, ms | 12949.95 | 12550.12 | 12772.17 |
| Representative sRGB color compile, ms | 551.50 | 552.42 | 571.90 |
| Representative linear normal compile, ms | 179.64 | 177.88 | 177.12 |
| Public Model.Load, ms | 346.38 | 341.70 | 318.00 |
| Public Materials readiness, ms | 62427.77 | 61791.61 | 63705.67 |
| Preparation and hash managed allocation, bytes | 2903998784 | 2904101136 | 2904019280 |
| Post-load process working set, bytes | 12527251456 | 12445495296 | 12447436800 |

Document image reads totaled 2.52–2.81 ms. These preparations and the public
loads are separate experiments, not additive stages within one load. The
working-set samples are not peaks; process-wide allocations can include other
editor activity. Hashing is outside the preparation time. The benchmark does
not measure cold OS cache, GPU completion, compiled `.glb_c`, or visual parity.

On the same producer a follow-up test timed real texture-cache acquisitions for
all fifteen prepared materials with leases retained. Receipt SHA-256
`d79fdde41eb305cb75999c2381ddf83eece582f371a6d337d339dac45a970c28`:
`Temp/gltf-validation/direct-gpu/texture-benchmark/attribution-8fc/probe/probe-01-editor_direct_gpu_tests/receipt-01.json`.
33 first-content acquisitions totaled 47,912.54 ms, median 1,448.00 ms,
range 1,417.80–1,501.58 ms; 23 live-content hits totaled 332.48 ms, median
14.44 ms. Separately timed SHA-256 calls took 477.92 ms for first-content and
332.79 ms for duplicate-content resources. Preparation in that single pass
totaled 12,679.41 ms. With those texture leases held, the first public model's
materials took 13,559.89 ms; a second `Model.Load` of the same path returned
the same wrapper in 0.0138 ms and its materials in 0.0001 ms. This is one
follow-up run and is not a cold-resource repeated-load comparison.

The subsequent stage 7eb phase probe measured nine first-resource operations:
database invalidation took 1,397.65–1,468.20 ms each; texture load returned in
1.28–19.89 ms, file writes took 10.89–12.52 ms, and separate SHA calls took
14.39–14.88 ms. A controlled three-resource experiment using one shared redirect
and one invalidation took 1,421.41–1,461.24 ms, versus 4,286.17–4,345.95 ms
for individual registrations and invalidations. Receipt SHA-256
`c642bdb95bc372e78cabaaa6f63607d35344cbe30957590679a7b033e2143b95`:
`Temp/gltf-validation/direct-gpu/texture-benchmark/phases-batch-7eb/probe/probe-01-editor_direct_gpu_tests/receipt-01.json`.
These are synchronous operation timings and valid handles, not proof that GPU
streaming completed. A follow-up with the candidate's separate redirects and
fresh aliases passed: individual invalidations took 4,182.37–4,282.50 ms and
one batched invalidation took 1,434.39–1,539.77 ms across three paired runs.
Receipt SHA-256 `755ff3ddf832a231323f613c92790157040afeba65279411e88c6079e4e138a7`,
`Temp/gltf-validation/direct-gpu/texture-benchmark/distinct-batch-7eb/probe/probe-01-editor_direct_gpu_tests/receipt-01.json`.
These handles were valid but their streamed pixels were not verified. Any candidate
optimization requires matching before/after performance, exact prepared-byte
comparison, and targeted native texture/readback/lifetime checks before claiming
success.

## Candidate results

The installed candidate batches the missing resources for each material, while
retaining one fresh alias and redirect per resource and one independent lease per
role. It does not change the sample assets or texture quality. The three-run
benchmark case passed on Engine MVID `5a07f228-dff8-4d6b-b4ef-890b32a80299`
and unchanged glTF MVID `075675b7-ad83-46c0-8a5d-3c53301f2d9c`. In the same
warm-OS-cache, fresh-alias setup, public material readiness was 31,756.46,
31,680.22 and 31,967.30 ms, versus 62,427.77, 61,791.61 and 63,705.67 ms.
The medians are 31,756.46 versus 62,427.77 ms: 30,671.31 ms (49.1%) lower.
Including `Model.Load`, the median was 32,152.16 versus 62,774.16 ms. Separate
material preparation was 12,470.62–13,743.91 ms and managed preparation-plus-
hash allocations were approximately 2.90 GB, both similar in scale to baseline.
All prepared texture-resource hashes matched every baseline iteration exactly.
The runs were not interleaved and the Engine stage also contained other changes;
the observed whole-model difference is not an isolated cross-device claim.
Sampled post-load working set was 11.47–11.54 GB versus 12.45–12.53 GB before;
these process-wide point samples are not peak RAM or GPU memory measurements.

Combined candidate receipt SHA-256
`5d6937b5553b53455fe8b3008b253ea0bc75dceb974225bc15b92d5578bc42af`:
`Temp/gltf-validation/direct-gpu/texture-benchmark/candidate-95c/probe/probe-01-editor_direct_gpu_tests/receipt-01.json`.
It is overall **failed**, although the benchmark case passed: the initially
requested missing-resource negative control returned null instead of a valid
native error handle. The corrected null, thrown-load, staging rollback, mixed
cache hit/miss, duplicate lease, cleanup and fresh-generation test passed in
receipt `f964373a367b9fb73dd64457e6bcbc19b55cd0e4ee89d5f6c5e624c02e195059`,
`Temp/gltf-validation/direct-gpu/texture-benchmark/cache-null-95c/probe/probe-01-editor_direct_gpu_tests/receipt-01.json`.
The non-null borrowed error-handle branch is source-reviewed but unexecuted.

Batch GPU readback on source and derived input passed after a bounded residency
wait: BaseColor, Normal, MetallicRoughness and Occlusion each matched all 12
prepared mip payloads byte-for-byte; the transfer flags and shared ORM/AO lease
identity passed. Receipt `f7f48803d88bfb3b5a0272b0a47be70a53f30bfee9b36e324bc27ac551e7cd0f`,
`Temp/gltf-validation/direct-gpu/texture-benchmark/gpu-wait-95c/probe/probe-01-editor_direct_gpu_tests/receipt-01.json`.
The earlier immediate readback failed on normal texture residency, receipt
`74cc897bf93613994b471970e0c25342c7280baeddd774d9803aa8d3dc998b22`; it is
retained. A distinct public `Model.Load` test mounted only the derived `.glb_c`
and read all 12 mips of the first material's four bound roles, with zero byte
differences after a bounded wait. Receipt
`95dfec77fce530b8352b098fe744185727df905cf5cf0b47face614c79ee69ca`,
`Temp/gltf-validation/direct-gpu/texture-benchmark/public-compiled-95c/probe/probe-01-editor_direct_gpu_tests/receipt-01.json`.
The source GLB already embeds its images; this is not proof of external-image
packaging. Serialized producer bytes are the readback oracle, not an independent
codec, sampler or appearance oracle. Scoped preload is not a normal-streaming
latency measurement, and existing user visual approval remains separate.
