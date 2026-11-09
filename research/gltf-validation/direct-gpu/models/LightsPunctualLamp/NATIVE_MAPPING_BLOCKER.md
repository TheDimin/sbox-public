# Punctual-light native mapping: current blocker

`KHR_lights_punctual` requires point/spot inverse-square response, infinite
range when unspecified, and a bounded spot intensity inside the inner cone.
The spec's smooth cone reference is squared, although the exact interpolation
formula is not the sole normative prescription. See the [Khronos extension](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_lights_punctual/README.md).

The existing six native packing observations and exact loaded assembly/receipt
identities are in [NATIVE_PACK_FINDINGS.md](NATIVE_PACK_FINDINGS.md). They are
packing measurements, not rendered photometry. The following comparisons are
analytical consequences of inspected source, not new GPU observations:

* `game/core/shaders/Shadows/Obsolete.hlsl` saturates the distance response;
  `common/classes/Light.hlsl` calls it. With the measured experimental native
  quadratic `0.00064516` and radius 1000 inches (bias `0.0015500031`), 1 m is
  about 39.37 inches: the calculated distance factor is about `0.99845` at
  1 m and saturates to `1` at 0.5 m. An unbounded glTF inverse-square light
  would have a 4:1 response ratio at those distances, not approximately 1:1.
  A constant intensity multiplier cannot repair both distances. Native finite
  radius also introduces a cutoff for an authored infinite-range light.
* For a spot with 30°/60° half angles, the observed native packed reciprocal
  is approximately 2.73205. In `Light.hlsl` the angular factor is neither
  squared nor saturated. At the cosine midpoint between the cones it is `0.5`,
  while the Khronos reference is `0.25`; on the center axis it is about
  `1.366`, whereas the reference is `1`. The recorded 59.99°/60° case also
  shows native reciprocal clamping at 100. These findings preclude treating
  copied source cone properties as a proven general native mapping.

The directional packed sign represents a receiver-to-source convention and
cannot alone establish the emitted direction or lux calibration. There is no
demonstrably correct nonzero-intensity native subset yet, so no default native
factory or required-extension advertisement is enabled. The per-instance
ownership helper can accept a future proven implementation; it is not one.

To resolve, the coordinator should render pre-tonemap floating-point receivers
at 0.5, 1 and 2 m for point/spot, at and beyond finite range, across and inside
both cones, and with opposite-facing normals for directional. Include zero-light,
deleted-light and independent-instance controls, compare native packed values
against the same loaded producers, and record actual image/readback values.
Investigate an appropriately scoped direct glTF shading path or native controls
if the measured stock light response cannot represent the required physics.
