# Isolated no-normal point/line candidate

`../../../../../engine/Tools/Gltf/direct-gpu/models/MeshPrimitiveModes/point_line_candidate.shader` is model-local research source, derived from the
existing five-UV unlit shader, with separately selected emissive UV, sampler,
texture and factor. It has not been added to watched core content or selected by
public Model.Load. `PointLineCandidateTests` compiles the anonymous candidate
and explicitly supplies it to the existing direct GPU asset resolver. The sample
itself has neither authored normals, COLOR_0 nor materials/textures; routing the
candidate to all seven primitives is deliberate diagnostic setup, not correct
production selection for triangles.

The first probe checks default source geometry under light on/off/on, base-only,
emissive-only, no emission/base, and separately black/white constant base and
emissive textures, for the original glTF and canonical compiled output. These
constant textures are GPU resources injected by the test; they establish neither
reader/adapter bindings nor color-space conversion, UV transforms, sampler
selection, color/alpha interpolation, variant or override semantics. Tests must
be executed against actual loaded producers and retained as limited candidate
evidence, not public material support.

Remaining proof before any shared route: derived point/line fixtures carrying
nonwhite COLOR_0, nontrivial independent textures and sampler/UV transforms,
base and emission factors with independent expected pixels and wrong-input
controls; comparison against a normals-present line and triangle using their
ordinary lit source material; mixed shared-material slots, variants and
per-instance override/clear with actual public drawing; MASK/cutoff and lifecycle.
Transparent BLEND integration depends on the active transparency work and cannot
be claimed from this shader candidate.
