# MandarinOrange diffuse transmission follow-up

User review at `2026-11-06T16:51:54.355Z` rejected fingerprint
`aa31f2083c8721fda05499dcd9ba26d889b4f01d783c5525f482dd80805a5737`
with: “We should add support for diffuse transmission.” The earlier core-fallback
GPU receipts remain bounded historical proofs; they do not satisfy this request.
Reader/schema/UV acceptance now passed 17/17 on the installed candidate producer.
The anonymous native shader compiled and bound, but the native radiance case
stopped at its float-target clear positive check; no BSDF or public-extension
acceptance is claimed. The shader remains outside watched content and default
extension registration remains disabled.

## Primary specification and tooling audit

Checked the primary Khronos sources on 7 November 2026:

- [Extension registry](https://github.com/KhronosGroup/glTF/blob/main/extensions/README.md)
  lists `KHR_materials_diffuse_transmission` as **Release Candidate**, separately
  from ratified extensions. It is written against glTF 2.0 and has no separate
  numerical version field. Supporting it uses the standard extension name and
  properties; no private schema or alternate material metadata is needed.
- [Extension specification](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_diffuse_transmission/README.md)
  describes thin, non-volumetric diffuse transmission. The strength defaults to
  zero and lies in [0,1]. Its optional texture supplies **linear alpha**. The
  color factor defaults to linear white, with three components in [0,1]. Its
  optional texture supplies **sRGB RGB**, decoded before factor multiplication.
  Both texture properties are standard textureInfo objects with independent UV
  selections, transforms and samplers. Reflection loses the diffuse fraction
  that is transmitted; the specular lobe is unchanged. Metal does not acquire
  a dielectric transmission lobe.
- [Schema](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_diffuse_transmission/schema/material.KHR_materials_diffuse_transmission.schema.json)
  uses JSON Schema draft-04 and core glTF textureInfo references. The extension
  forbids coexistence with unlit and specular/glossiness on one material.
  Unsupported optional volume/transmission extensions retain their own fallback;
  required unsupported extensions still reject. No volume approximation is added.
- [Khronos Sample Renderer](https://github.com/KhronosGroup/glTF-Sample-Renderer/blob/main/source/Renderer/shaders/pbr.frag)
  confirms opposite-normal diffuse IBL and mirrored-light Fresnel for punctual
  diffuse transmission. Its implementation is reference context, not a second
  producer used to manufacture expected GPU values. The current Sample Viewer
  exposes a diffuse-transmission toggle. The current
  [Validator support list](https://github.com/KhronosGroup/glTF-Validator#implemented-features)
  does **not** list diffuse transmission, despite the registry's general Release
  Candidate tooling guidance. Its
  [changelog](https://github.com/KhronosGroup/glTF-Validator/blob/main/CHANGELOG.md)
  names 2.0.0-dev.3.11 as unreleased and 2.0.0-dev.3.10 as the previous entry.
  Do not treat a generic validator pass as extension schema/rendering validation.
  The pinned 2.0.0-dev.3.10 binary has now executed against all 27 positive
  core inputs with zero core errors. Its unsupported-extension notices remain;
  this is not extension validation. See diffuse-positive-fixtures-validation.json.

MandarinOrange uses factor 1, the same PNG in strength and color roles, and UV0
with linear/repeat samplers. The image must remain untouched: the two roles use
different transfer contracts even when their encoded bytes are identical.

## Native engine capability audit

The existing shader services are useful but `Material.Transmission` cannot be
mapped directly and called support:

- `common/shadingmodel.hlsl` forwards the mask into `vTransmissiveMask`, but its
  normal shaded composition does not add `lightingTerms.vTransmissive`.
- `vr_lighting.fxc` applies a hardcoded 0.1 thickness multiplier for unshadowed
  point/spot transmission. Thickness-based falloff is not the requested thin
  surface model. Its default diffuse lobe is roughness-dependent rather than
  Lambertian.
- Native indirect transmission eventually adds the front indirect irradiance;
  the extension needs the opposite hemisphere. Merely changing the mask or
  enabling foliage subsurface/wrap does not satisfy that rule.

`../../../../../engine/Tools/Gltf/direct-gpu/models/MandarinOrange/gltf_diffuse_transmission_candidate.shader` therefore keeps native material
initialization, normal mapping, specular evaluation, light selection, cookies,
attenuation, shadow visibility, environment/probe/DDGI sampling, fog and rendering
services. It supplies the missing diffuse BSDF and separately samples rear
irradiance. This is unexecuted source reasoning, not a proven native blocker or
visual acceptance. Native light-unit calibration, directional/baked paths and
environment directions remain targeted gates before public capability enablement.

## Independent controls prepared

`diffuse-transmission-plane.gltf` is a literal four-vertex plane, unrelated to the
model's vertex producer. `diffuse-transmission-role-control.png` is a literal
1x1 RGBA(128,64,192,64) PNG. `diffuse-transmission-oracle.json` records independently
calculated sRGB values and linear alpha. `../../../../../engine/Tools/Gltf/direct-gpu/models/MandarinOrange/DiffuseTransmissionCandidateTests.cs`
prepares actual float-target positive clear/readback checks, textured versus
literal-uniform comparisons, wrong RGB/alpha and wrong-transfer negatives,
back factor 0/0.25/1 linearity and front energy reduction while specular stays.
The first actual receipt was 16/18 (`287b6000...`): the adversarial fixture omitted
mandatory intermediate TEXCOORD declarations, and the float clear positive failed.
After fixing only the fixture and waiting for native frames, receipt `66bc20f0...`
passed all 17 reader cases with the native case still failing that positive check.
It observed `(31,63,127)/255` from `Clear(.125,.25,.5)` on RGBA32F: the clear API
quantizes packed color. The next prepared check uses exact Magenta endpoints;
actual rendering linearity, native Lambert/Fresnel calibration and HDR cube
checks retain their strict assertions. See result.json for full receipt hashes.

The native candidate also prepares an independent 60-degree Lambert/Fresnel
ratio, a seven-role fixture with nonconsecutive UV sets and normalized/strided
source formats, repeat-versus-clamp and mirror-versus-repeat negatives, and an
asymmetric literal HDR cube. Every native cube face/mip must match its authored
floating values before front-red versus rear-green irradiance can be evaluated.
These are prepared controls, not executed passes.

The public route must then use the actual parser, retained image generation,
role-specific resource/cache/sampler bindings and descriptor-driven UV projection.
One source case proves source/external-image behavior; one canonical compiled
case proves the actual portable artifact. Existing source/material overrides
remain independent and source files remain byte-identical. Old resource/reimport
suites are not part of this follow-up. Final appearance approval belongs to the
user and is pending new captures and publication.


## Current executed calibration boundary

Receipt `4b42c3521cd2754f99573c7ec78535752bbf0c84637c90eb29594f76d8e5973c`
is an overall failed candidate case, with these bounded partial results. An
independent constant HDR fragment returning (.12345, 2.3456, .4568) observed
(.1234130859375, 2.34375, .456787109375), identifying positive FP16 truncation
before the RGBA32F destination. AA and postprocessing were disabled. Radiance
above one and a fractional value outside 8-bit quantization were verified.
The exact internal attachment/pass location is not exposed by this proof.
For an observed positive half value h, [h,nextHalf(h)] encloses the source
before truncation; ratio and linear-combination intervals are propagated from
these individual bounds. This does not introduce a percentage epsilon.

The fixed .96 native Lambert/Fresnel expectation lies within measured ratio
bounds [.9593289273004576,.960325534079349], excluding 1 and wrong PI controls.
The independent 60-degree expectation .4999784184671149 lies within bounds
[.4996027542372881,.500132485426603]. Factor energy, metal suppression,
linear-alpha/sRGB-color and wrong-transfer/alpha sensitivity also passed.
The case then correctly rejected a U16 vertex accessor at byteOffset2. Only
the fixture changed to offset4/stride8 with distinct padding; no producer
validation was relaxed. Pinned official validator execution now covers 27
positive fixtures with zero core errors and no report truncation, including
shader negative controls and actual Mandarin source. Native draws consume
those exact frozen validated files. Seven-UV, front-specular and opposite-
hemisphere IBL controls remain pending; the extension stays unadvertised.


The corrected frozen candidate then passed **1/1** actual native case, zero
failures/incomplete/cleanup errors, receipt
`710bdec85f756188d2238255621ee25e0a8812d75d7c0fb9249d7d3e25a12071`.
The seven-UV descriptor/readback and adversarial samplers, retained front
specular, all 24 authored cube face/mip readbacks and rear-hemisphere IBL
controls now passed. Front IBL center red/green was .44384765625/.030853271484375;
transmitted IBL red/green was .147705078125/3.951171875. This uses the unchanged
`dfbc5948...` anonymous candidate, actual Engine123f2cf1/Gltf56e0e078, with
original reader17 evidence retained on its own producer. Named public source
and canonical compiled rendering remain pending root review/integration;
the extension is still disabled by default and user appearance approval is pending.

The reviewed exact candidate is now installed as the named shader and the
loader registration is enabled. Actual public source/canonical2 and one
zero-factor roughness compatibility diagnostic passed3/3, receipt
`40e408ee3a8956744516ba508d6504dd5ceba6023292886b8a0e9ccf032089fc`.
Both main192px captures have hash `4d78bd23...`; original textures and
per-instance restore/lifetime controls passed. Factor0 differs measurably from
ordinary native core shading: at60degree light, roughness0/.5/1 max absolute
linearRGB differences were .023681640625/.019287109375/.0390625. No pixel
identity is claimed. The final current forms, canonical packaging proof,
producer scope and visual limitations are in ACCEPTANCE.md/result.json.
Human appearance approval is pending the new extension-enabled comparison.


Presentation follow-up1 passed at receipt3a470c3d…: actual autoexposure and
native tonemap enabled (rate20/min1/max2), five1151x1309 RTT renders stable
from first, four successive foreground RGB deltas0, first/final fullPNG
identicald2ef1d57…. Main visual is source-only highres; canonical192 functional
proof and acceptedpublic3 remain unchanged. No adaptive warm-up defect was
observed, and grey/yellow versus reference stays user appearance review.


Current lighting review replaces the old black/point view with actual Field
HDRI+visible sky source capture ba635d35…, receipt00ead190…1/1. NativeIBL/sky
intensity.75/yaw0, ambientRGBA0, oldpointsdisabled, ACESfixedEV0; perspective18°
vertical atsamecamera pose is an explicit inferred presentation change.
FouractualRTTstates each settled5renders/zero successive full-imageRGBdeltas;
probe-off affected311280modelpixels, deletionrestoredblack. Source/shader
featuredata unchanged; functional3/canonical192proof not rerun. Rootapproved
publicationforuserreview; appearance acceptance stillpending.
