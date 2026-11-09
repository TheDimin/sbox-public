# Animation validation repair

## Validation-version audit

Stage C was built and tested using the newer clarification contract pinned below.
The subsequently selected published baseline is revision
`8e798b02d254cea97659a333cfcb20875b62bdd4`. The two revisions must not be
silently combined into one claim of strict published conformance.

Published requirements independently support duplicate nonzero-joint rejection,
nonnegative weights and the quantized normalization denominator; float-weight
normalization remains a recommendation. They also require nonvertex data to omit
stride, animation input bounds, actual decoded extrema, float32 rounding of those
bounds and the descriptor-only bounds exception.
[Published skinning](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L1462-L1486),
[published stride](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L832),
[published bounds](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L1087-L1103).

The following positive compatibility cases are newer clarifications, not strict
published-baseline evidence:

| Behavior retained by stage C | Published text | Newer clarification |
|---|---|---|
| Rigid primitives within a node that also references a skin | The node schema requires skin attributes on every primitive | Attribute-free primitives use the node transform |
| Morph-weight animation on a matrix node | Animation-target nodes cannot contain matrix | Only TRS-targeted nodes have that prohibition |
| Ignoring an unused trailing nonaffine inverse-bind matrix | The narrative describes every inverse-bind matrix as affine | Only matrices associated with joints have the affine constraint |

[Published node schema](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/schema/node.schema.json#L5-L6),
[published animation transform restriction](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L692),
[published inverse binds](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/Specification.adoc#L1368).

The added nonvertex GPU-target-hint rejection has an explicit requirement in the
newer text; the published text calls this field an intended-GPU-type hint.
Current official validator source independently assigns declared targets a
vertex/index role and rejects a conflicting actual role. That corroboration is
source evidence, not execution or a substitute for classifying the version change.
[Published buffer-view schema](https://github.com/KhronosGroup/glTF/blob/8e798b02d254cea97659a333cfcb20875b62bdd4/specification/2.0/schema/bufferView.schema.json#L32-L49),
[validator role checking](https://github.com/KhronosGroup/glTF-Validator/blob/main/lib/src/base/buffer_view.dart#L47-L58).

The historical failure accounting below describes the stage C contract. Its
actual receipt reports733/745 overall and54/54 core-animation cases. Twelve
unrelated data/scene failures remain. These totals do not erase the compatibility
distinctions above.

Baseline: Gltf `39c031d5-72d0-4b52-8b24-80d390419dfe`, full pure-glTF
receipt `editor-gltf-unit-tests/648e65f201594457afa07ac9439cc44b/receipt.json`:
704/732 passed. Sixteen failures were in `core-animation-conformance`.
This patch is separate from morph rendering, authored-skeleton/API acceptance,
and the six data plus six scene failures.

## Pinned requirements and failure classification

Khronos glTF repository revision:
`9d23a5a5b1d10f22d1554e847f2a1a3f24222392` (main resolved 2026-11-05).

| Baseline failures | Requirement |
|---|---|
| Two duplicate-joint cases | Nonzero influences for one vertex must use distinct joint indices, across all sets. [Source](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#L1787-L1842) |
| Four quantized-sum cases | If every weight set is quantized, the combined dequantized sum must equal one exactly, including mixed UNORM8/UNORM16 sets. [Source](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#L1825-L1842) |
| Three stride cases | Animation and inverse-bind accessor views cannot declare vertex stride or GPU target hints. [Source](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#L999-L1013) |
| Two missing-bound cases | Animation inputs require min/max. Float bounds compare after float32 rounding; descriptor-only accessors retain their stated exception. [Source](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#L1380-L1403) |
| Four matrix cases | Inverse binds associated with joints require an affine fourth row. Extra unused matrices do not acquire that restriction. [Source](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#L1882) |
| One incorrect negative test | A primitive without skinning attributes uses its node transform even when that node references a skin. Replace the erroneous rejection expectation with positive mixed-primitive checks. [Source](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#L1983-L1989) |

Thus fifteen failures identify missing validation and one identifies an incorrect
test expectation. No invalid input is changed into a successful load.

The authorized additional regression permits morph-weight animation on matrix
nodes while still rejecting TRS animation there. The original matrix remains
unchanged. [Source](https://github.com/KhronosGroup/glTF/blob/9d23a5a5b1d10f22d1554e847f2a1a3f24222392/specification/2.0/Specification.adoc#L2882-L2888)

## Implementation and fixtures

Rest and sampled skinning use one validation/evaluation helper. It preserves
all influence sets and authored float weights; no weight renormalization or
animation resampling is introduced. Quantized sums use an integer denominator
after recovering the original at-most-16-bit weights from decoded float32 values.
Zero-weight padding may repeat joint indices.

Only `ReadSkin` changes in the general scene reader. Scene hierarchy/matrix
validation failures remain outside this patch. Animation input/output and
inverse-bind accessors get role-specific buffer-view checks; general accessor
decoding and vertex interleaving stay intact.

`GltfAnimatedDeformationTests` contained two positive time accessors without
bounds. Their fixture metadata is repaired, with per-component extrema in the
shared fixture writer. Sample values and expected deformation stay unchanged.

`GltfCoreAnimationConformanceTests` adds mixed quantized sums, zero-weight
padding, float32 bounds, mismatched bounds, descriptor-only bounds, prohibited
GPU hints, unused inverse binds and matrix-node morph-animation controls.

No new passing runtime result is claimed here. The coordinator must build a new
Gltf producer, run focused cases inside s&box, then rerun the affected pure suite
and actual animated/morph loads. Rendering, native/default shader dispatch,
authored skeletons, attachments, graph playback and physics remain independently
reported acceptance scopes.
