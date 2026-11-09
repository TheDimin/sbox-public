# MorphStressTest direct GPU acceptance

Owner `/root/gpu_morph_stress`; executor `/root/gpu_validation`; shared backend
`/root/gpu_fox`; clock/morph helper `/root/gpu_morph_cube`. This folder owns its
independent oracle, tests and probes. No native Model, Mesh, IModel, builder or
native morph controller is constructed. The expected image uses `Graphics.Draw`
and a `SceneCustomObject` with independently decoded geometry.

The actual corpus pin is `90d7ede14c7e280af263824604b427a1ca02cb66`. Actual upstream
forms are `glTF/MorphStressTest.gltf` and `glTF-Binary/MorphStressTest.glb`; each
also has a derived portable `.glb_c` form produced by the actual loaded compiler.
The generated numerical reference is independent Python standard-library JSON/BIN
decoding. It never calls the reader, pose evaluator, staging plan or renderer.
Source geometry is CC-BY-4.0, Ed Mackey / Analytical Graphics, Inc. (2021); retain
the upstream `Models/MorphStressTest/LICENSE.md` when distributing these arrays.

One node references mesh 0, with two primitives: 24 and 1,504 vertices, 36 and
7,200 UNSIGNED_SHORT indices. Both have exactly eight ordered targets, `Key 1`
through `Key 8`, each with FLOAT3 POSITION and NORMAL. Base attributes are FLOAT
POSITION, NORMAL, TEXCOORD_0 and TEXCOORD_1. There is no authored tangent, sparse
accessor, interleaved stream, skin or extension in this model. The static base
primitive's target positions are all zero; checking that primitive alone would
miss all morph motion. Material 0 uses occlusion UV set 1. Both materials are
double sided, so reversed-winding pixel absence would be an invalid negative
control; actual bound GPU index readback retains strict winding checks.

Mesh weights are eight zeros and the node has no override. `Individuals`,
`TheWave`, `Pulse` have 281, 59, 191 keys respectively, all LINEAR and all authored
weights within [0,1]. Their first key is 0.033333335 seconds, not zero. CPU tests
exercise all 531 authored keys, 528 between-key samples and six external clamp
samples per form. Synthetic fixtures deliberately exercise signed/unbounded node
overrides versus nonzero mesh defaults, and a ninth target duplicating authored
target 7, in source and compiled forms. These are controls, never described as an
upstream nine-target variant. No weight or target is clamped, normalized or
arbitrarily truncated.

Independent Python controls passed 7/7. C# suite inventory is 60: 28 CPU/data,
18 pure staging, 10 synthetic GPU checker controls and four synthetic image
checker controls. C# execution remains pending. There are four bounded CPU
measurement rows, 48 callable actual GPU readback states, and 16 callable native
default-lit image cases. An unrun gate is not a pass.

All 1,528 vertices and 7,236 actual bound GPU indices are checked per readback.
Runtime declarations supply field offsets, stride and representation; a checker
control uses a shuffled padded 100-byte layout with NaN padding. Target staging
checks each descriptor's target, semantic, source format, stride, arena origin
and raw bytes, including the ninth target. Independent assets must own separate
raw arenas. CPU position checks use moving witnesses from each target rather
than repeatedly evaluating the static base. Readback compares every position,
defined authored normal and both UV sets, plus finite unit generated tangent,
orthogonality and handedness. Missing authored tangent XYZ has no invented
direction oracle. Exact zero morphed normals are counted explicitly and retain
finite/unit fallback checks; none occurs in the independently audited rest,
all-one, signed-eight or ninth-target controls.

Candidate enforced readback limits are 0.0005 engine inch per position component,
2e-6 per normal component and normal length, exact UV values/handedness, and 2e-6
generated tangent length/orthogonality. These values need coordinator review
before actual acceptance; measured errors do not silently choose tolerances.
Synthetic negative controls alter positions, normals, UV1, indices, index origin,
primitive count, target 7 contribution, finite values and generated tangent
length. Checker success proves sensitivity only, not device execution.

The 48 device states contain 10 per actual/derived form: rest, three clip peaks,
three between-key samples, target 7 peak, independent affine B, and A after B
disposal. B output bytes must remain identical after A changes. Eight additional
source/compiled synthetic states cover 8/9 targets with mesh defaults/node
overrides. Timings name CPU read, preparation, pose, frame wait and blocking
readback explicitly. They are not GPU kernel timestamps or throughput results.

The 16 image cases sample all three clips and the last target in all four forms.
Each uses seven fresh captures: empty before, actual GPU, empty after deletion,
independent expected geometry, expected deleted-empty, unmorphed negative, and
negative deleted-empty. Fixed independent interval bounds cover every [0,1]
weight combination. Camera, source-material leases, directional light,
dimensions, default-lit mode/postprocessing, placement and source time match.
Candidate image limits are IoU >= .99, mean RGB error <= .003, changed pixels
<= max(12, .015 * expected coverage), both coverages >128 and >=64 changed
negative pixels. The coordinator must review these limits. Positive geometry,
missing reference and insensitive-negative checker controls stay explicit.

The accepted native camera route uses `SceneCameraDebugMode.Normal`, meaning
ordinary lit rendering; it is not a world-normal diagnostic. GPU authored normal
metrics are independent. These lit comparisons do not establish source PBR
material fidelity. None of
the deterministic key or interior probes proves real wall-clock playback,
high-cadence media, GPU performance, matched vmdl memory/load throughput, Windows,
public `Model.Load` integration, native model-editor parity, or full model
acceptance. These remain distinct pending gates owned by the managing task.

Only the coordinator compiles/installs producers and helpers, compiles shaders,
uses the editor, renders or runs heavy measurements under the resource guard.
Internal production backend types are consumed from the actual loaded Engine by
its authorized `Sandbox.Test.Unit` friend helper. Model classes whose signatures
expose internal backend types are internal and retain public static reflection
entry methods. No replacement backend/producer source is compiled into this
model's helper to simulate production integration.
