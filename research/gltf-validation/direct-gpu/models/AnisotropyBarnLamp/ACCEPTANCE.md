# AnisotropyBarnLamp acceptance

All actual upstream variants are applicable: `glTF`, `glTF-Binary`, and
`glTF-KTX-BasisU`. Each needs original source and derived packaging `.glb_c`.
The last requires `KHR_texture_basisu`; the other five material extensions are
optional and remain explicitly reported as standard core fallbacks. Anisotropy,
clearcoat, emissive strength, transmission and volume fidelity are outside the
selected extension scope. Core emissive/normal/occlusion behavior remains in
scope. Falling back from transmission still requires the authored opaque core
material, rather than dropping the glass primitive.

The source checkout is pinned at
`90d7ede14c7e280af263824604b427a1ca02cb66`. Model assets are CC-BY-4.0, by Eric
Chadwick, Copyright 2023 Wayfair, LLC. Existing LICENSE.md/metadata.json remain
the attribution source; no source assets are copied into this model folder.

`../../../../../engine/Tools/Gltf/direct-gpu/models/AnisotropyBarnLamp/audit_source.py` is an independent standard-library file/GLB/accessor audit. Its
frozen `source-audit.json` hashes every logical geometry stream (including source
offsets/strides), all images, source files and KTX DFD/KVD. It imports neither
the reader nor the producer evaluator. It verifies three primitives with
6,803/140/769 vertices and 25,257/840/4,512 U16 indices. Shared POSITION/NORMAL
views and the later primitives' nonzero offsets are covered by full logical
stream hashes. The audit and source-byte checks are prepared; they are not a
renderer result.

The canonical production file is
`engine/Sandbox.Engine/Resources/Model/Gltf/Textures/GltfBasisTexturePlan.cs`.
The 15 `GltfBasisTexturePlanTests` cases use synthetic independent headers and
literal texels to distinguish codec choice, authored mip order, linear-light
color reduction, alpha handling, missing-tail generation, malformed metadata,
role mismatch and decoder shape disagreement. These tests were authored before
the implementation and remain unrun by this model worker.

`AnisotropyBarnLampTests.RunAll(checkoutRoot)` declares **43 result rows**:

- Three pinned source identity rows.
- Four CPU data/packaging groups for all six source/compiled forms (24 rows).
- Four metadata and four native codec/VTEX CPU resource rows for each Basis
  source/compiled form (16 rows).

The native resource rows decode through actual loaded Gltf/native producers and
check the native resource framing, 2048×2048 RGBA format, 12-level full chain,
correct sRGB/linear flags and 22,369,620-byte payload. They are metadata and codec
integration checks. They do not independently prove the lamp's decoded image
fidelity, texture sampling, material render, GPU output or rendered parity.
`GltfKtxTests` already has independent Khronos color/alpha/mip fixtures; those
existing tests must also run against the deployed codec.

The compiled form uses the existing lossless GltfCompiler. External texture
URIs remain explicit relative dependencies, and exact encoded bytes/metadata are
checked again. Embedded source images remain embedded; no external geometry
buffer may be resolved by a compiled result. This establishes portable packaging
when executed, not optimized `.glb_c` load-time or GPU-memory parity.

Remaining gates are marked **unrun/pending**: compilation and focused test
execution, native codec staging/load on Linux and Windows, shared material
compiler wiring, real source/compiled model creation through the managed GPU
backend, actual GPU readbacks, matched native image comparison with independent
expected pixels and empty/positive/deletion controls, public Model.Load behavior,
load/unload/lifetime checks, resource budgets and matched compiled-vmdl
performance/memory comparisons. The model is static, so animation-clock tests
are not applicable. No native Model/Mesh rendering is a substitute for these
gates. No editor, shader build, heavy build/test or network artifact download
was started by this worker.
