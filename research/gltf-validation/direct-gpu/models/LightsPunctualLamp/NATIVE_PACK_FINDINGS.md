# Actual native light audit

This is additive evidence after the original `submission.json` freeze. It does
not modify the executed audit source or claim that the new light-plan producer
was installed.

The coordinator executed six native packing observations against actual loaded
Engine `04b86c2e-4406-422e-a466-b61ec13f1f86` and Renderer
`c22b305b-109d-4166-a212-a7d4bc301e40`. The wrapper's cleanup, exact observation
inventory and producer-identity case passed (1/1). The six records are measured
values, not six photometric pass assertions. Temporary native lights were deleted
and private-world object count returned to its initial value.

Receipt:
`Temp/gltf-validation/direct-gpu/native-light-pack-observations/probe-01-editor_direct_gpu_tests/receipt-01.json`
SHA256 `f7cc4844ec96d8c1da6d9ba94208cd8157705f663253e2204cca528e2227d9ad`.

| Probe | Observed packed result |
| --- | --- |
| Point, property quadratic=1, radius=1000 | quadratic 0.0001, bias 0.010000001, position (11,23,37) |
| Point, explicit candidate quadratic=10000×0.0254², radius=1000 | quadratic 0.00064516003, bias 0.0015500031; literal RGB (15,9.4781246185,3.5864963531) preserved |
| Same point, radius=2000 | same quadratic and RGB; bias 0.00038750077 |
| Spot half angles 30°/60°, rotation LookAt(-X) | emitted packed forward (-1,0,0), cosines 0.8660253882/0.4999999702, reciprocal 2.7320506573 |
| Spot half angles 59.99°/60° | inner cosine 0.5001510978, outer 0.4999999702, reciprocal clamped to 100 |
| Directional constructor LookAt(-X) | packed type 2, forward (+1,0,0), far-away position (+2000000,0,0), no attenuation; radius/radius² both float.MaxValue |

The finite-radius bias changes by a factor of four when radius doubles. This
confirms that copying the sample's candela to `LightColor` plus a selected finite
`Radius` is not evidence of its authored unbounded inverse-square behavior. The
candidate quadratic remains explicitly experimental; the native hidden distance
function and the actual stock shader's light response still need execution.

Spot native packing agrees with the engine's documented half-degree geometry at
30°/60°. Its narrow-cone clamp differs from the Khronos reference's reciprocal
cap of 1000. Existing `Light.hlsl` also multiplies an unsquared, unsaturated cone
factor at the inspected call site. These are concrete reasons to measure the
actual shader response before declaring glTF cone fidelity; the reference code
is not a mandate to make all native lights use a new curve.

The directional opposite sign is not by itself a bug: the packed far-away source
is on +X, while the requested rays travel -X. `SceneMirror` separately sets
`SunDirection=-sun.WorldDirection`. Receiver-to-source versus emitted-ray
conventions must be measured in the consumer, rather than forcing identical
packed signs for spot and directional lights.

Next acceptance should observe linear floating-point light response before tone
mapping/exposure: point receivers at 1 m and 2 m; finite range inside/at/beyond
cutoff; spot receivers inside inner cone, between cones and outside outer cone;
directional receivers with opposite facing normals; linear RGB scaling and zero
intensity. Include empty-before, positive and deleted-empty controls, two placed
instances, another retained instance after one disposal, and unchanged native
vmdl control. Raw packed observations, CPU plans and source-material shading
remain separate gates.

The source material adapter has independent blockers for this model: materials
0/2 author emissive factor (1,1,1), emissive texture and normal texture; material
1 authors BLEND and a normal texture. Current stock-adapter rejection of those
features is distinct from optional transmission fallback. Diagnostic materials
may isolate light arithmetic but cannot pass lamp source-material fidelity.
