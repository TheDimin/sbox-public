# Rotated missing-tangent diagnostic

The strict actual public midpoint probe (`.375` source seconds) fails under
7eb's named deformation shader, receipt
`619579271fc67cbf15e6cf7123cd36c4dba5c2a55b781ea9288cfec8e4245f11`.
The loader's resource-owned document is used; the engine's foreign-pose guard
remains strict. No production source/layout or numerical limit was changed.

Retained actual GPU payload:
`Temp/gltf-validation/direct-gpu/MeshoptCubeTest/poses/17ffcc8f8d404b8696d8d46e3aa57336/gpu_pose_0.375.json`,
SHA256 `791a180926dcdfb3aafc888d2868a39f43e36ac8f76733f00742118b8e83de88`.
Independent stdlib Python inspection uses output field offsets/stride plus the
pinned fallback values, analytic source Y rotation, literal transforms, and one
source-to-engine conversion. Its report SHA256 is
`7db2bed5a23cf0f5450a32e56e680549977595d237ae7dcb566967b981dfe9be`.

All 640 positions/normals/colors/UVs pass. Maximum errors are position
`3.556379181191005e-05` inches, normal `1.6870263741530778e-08`, color
`5.983838846645995e-08`, and UV zero. All 320 oriented triangles match per
ordinal (248 exact, 72 cyclic, no reversed/changed topology). Forty tangent
vertices fail orthogonality: eight +/-X-normal face vertices in each of the five
rotating cubes. First actual vectors at node14/vertex0:

- normal `(-.5555702447891235, .8314695954322815, 0)`
- tangent `(.9723995923995972, .23332157731056213, 0, 1)`
- dot `-.34623648209025326`, against the unchanged `3e-6` bound

These meshes have authored NORMAL but no TANGENT. The current shader selects a
local default tangent `(1,0,0)` and Gram-Schmidt projects it against the world
normal. For the parallel +/-X faces, rotation leaves a small cancellation
residual which `Unit` normalizes. At the axis-aligned rest pose, exact cancellation
instead reached the valid perpendicular fallback.

The Normal shader owner owns the minimal generic correction: a missing tangent
descriptor, unless a generated corner descriptor is present, derives
`Perpendicular(worldNormal)` directly. Authored and generated corner tangents
retain their existing paths. The model worker did not edit the shared shader.
Owner candidate SHA256
`5f02c8ca37cfbb0296f5deb3c26d4c6f431641c0145251314c5ec7afcae9968d`
passed actual old-versus-candidate GPU and authored/corner/flat/reflection
regressions in owner receipt
`fecfef1d9ece35e4ee12560d3e2bbb5b6654bf137e24eeaf209fb694ddae94b9`.
The integration owner promoted the correction as named compute shader SHA256
`4e5f9c9da78a82fcdef7ae72d0280a454697a0bf1eea6e46a83ca5a94c259c65`.
Actual public canonical pose/index-negative test passed, receipt
`5af9f799098552bd904ac16ee4de65eee4b730ef8b7fc04077ef8896b26ac0c2`;
ordinary two-loop public playback/preview test passed, receipt
`7c7ce23616416480a9844e1eba12540917c37d020f18937ecf95daa2b79919b2`.
Independent parsing of all five posed canonical GPU payloads measured tangent
length and normal-dot errors of zero. No original failure was rewritten, no
threshold was widened, and historical rest images were not replayed.
