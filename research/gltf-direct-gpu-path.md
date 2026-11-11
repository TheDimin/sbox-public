# glTF direct GPU path: findings and resume notes

As of 2026-11-11. Online copy: https://claude.ai/artifact/4HTxEstUXb2WxrBxo5kNG9

## Status

The glTF work is paused. The engine can already load glTF data into its own GPU buffers and draw it with no `.vmdl` and no ModelBuilder. But the draw path is too slow for games: about 2 fps in the test scene.

**Repo:** branch `master`. The last glTF commit is `647ccfbce` (glTF textures: write Basis images as BC7 instead of RGBA32), 2026-11-10.

**Committed glTF work (newest first):**

| Commit | What |
| --- | --- |
| `647ccfbce` | Textures: Basis images written as BC7 |
| `c66c68127` | KTX: transcode Basis to BC7 or BC5 blocks |
| `90b2ef589` | Import: compile `.glb_c` with views ordered for LOD streaming |
| `67ee0326e` | Compiler: known extensions can merge buffers; views ordered by LOD |
| `eafbf522d` | Loader: declare MSFT_lod support, log chain warnings |
| `da69ad042` | Runtime: draw MSFT_lod chains, pick a level per view |
| `69a0cb476` | LOD selector from screen coverage |
| `54b111bce` | Editor: MSFT_lod level editing helpers and tests |
| `375b44847` | Read and validate MSFT_lod node and material chains |
| `097f8395a` | Runtime: loader, direct GPU backend, shaders and materials |

About 30 commits mention glTF. Run `git log --oneline -i --grep=gltf` to see the full list.

**Uncommitted work:** on 2026-11-11 the working tree had about 4,700 changed paths, including most files under `engine/Sandbox.Engine/Resources/Model/Gltf/`. Many of these changes are not glTF (benchmarks, launcher, panel gallery). Two older stashes (`stash@{0}`, `stash@{1}`) are not glTF.

**How to resume:**

1. Find the glTF stash with `git stash list`. Apply it with `git stash apply stash@{N}`. Use `apply`, not `pop`, until the build passes.
2. Build: `dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- build --config Developer`.
3. Run the glTF Integration and Unit tests. The known baseline was 71 pass, 5 fail, 4 skipped.
4. Read the sections below, then start with the next steps.

## Glossary

glTF describes data as bytes plus descriptions of those bytes. The GPU needs buffers plus a way for the vertex shader to read them.

| Term | Side | Meaning |
| --- | --- | --- |
| buffer | glTF | One large block of bytes. In a `.glb` file it is the binary chunk. |
| bufferView | glTF | A part of a buffer: `byteOffset`, length, and an optional `byteStride`. |
| accessor | glTF | How to read one attribute from a bufferView: count, `componentType` (float, uint16, int8, ...), type (VEC3, ...), and `normalized`. |
| primitive | glTF | One drawable piece of a mesh: attribute accessors (POSITION, NORMAL, TEXCOORD_0, ...), an index accessor, and a material. |
| vertex buffer | GPU | GPU memory that holds per-vertex data. |
| index buffer | GPU | GPU memory that holds the triangle list (3 vertex numbers for each triangle). |
| vertex shader | GPU | A small program that runs once for each vertex and calculates its screen position. |
| vertex input layout | GPU | A description of where each attribute is in the vertex buffer (offset and format). The GPU uses it to give the vertex shader its inputs ready to use. |
| stream / slot | GPU | One vertex buffer bound to a draw. A layout can read one stream or several. |
| interleaved | Both | All attributes of one vertex are next to each other: `[P N UV][P N UV]...` |
| planar | Both | One block per attribute: `[P P P...][N N N...][UV UV UV...]`. glTF files are usually planar. |
| quantized | Both | Values in a smaller type, for example 16-bit or 8-bit normalized integers in place of 32-bit floats. |
| vertex pulling | GPU | The vertex shader reads the vertex bytes from a buffer itself, with no vertex input layout. |
| combo | s&box | A compiled shader variant. One combo per format set means the shader needs no `if` checks. |
| load cost / per-frame cost | Both | Work done once when the model loads, compared with work done every frame. |

## What the engine allows

Managed engine code can own the GPU buffers, the accessor layout and the draw calls. It cannot touch raw Vulkan objects. Confidence: high for the managed side, which I read in the source. Medium (about 75%) for the native limits, because only the `.def` interop files are visible.

**Available to engine code:**

| Need | API | Notes |
| --- | --- | --- |
| Own GPU buffers | `GpuBuffer(count, stride, UsageFlags)` | The flags combine: `Vertex`, `Index`, `ByteAddress`, `Structured`, `IndirectDrawArguments`. One buffer can hold a whole glTF buffer. |
| Accessors as vertex inputs | `NativeEngine.VertexLayout.Create(name, stride)`, `Add(semantic, index, format, offset)`, `Build()` | `format` is any `ColorFormat`, so UNORM, SNORM and 16-bit types work. |
| byteOffset and byteStride | `Context.BindVertexBuffer(slot, buffer, offset, stride)`, `BindIndexBuffer(buffer, 2 or 4, offset)` | Maps to a glTF bufferView. |
| Draws | `Draw`, `DrawIndexed(first, count, maxVertex, baseVertex)`, `*Instanced`, `*InstancedIndirect(drawCount, stride)` | Multi-draw indirect is available. |
| Pipeline and descriptors | `RenderTools.SetRenderState(context, attributes, materialMode, layout, stats)` | The material mode selects the shader and pipeline. `RenderAttributes` binds buffers, textures and constants. |
| Compute | `RenderTools.Compute(...)`, `CommandList.DispatchCompute` | The current backend writes the final vertices this way. |

The current backend already uses this path. It uploads at `GltfGpuBackend.cs:493-497` and draws at `:1062-1069` (`SetRenderState`, `BindVertexBuffer`, `BindIndexBuffer`, `DrawIndexed`). Line numbers are from 2026-11-11.

**Not available:**

- `VkPipeline`, descriptor set layouts, push constants, render passes, vertex binding divisors, and memory allocation. The native render system owns them, and its source is not in this repo.
- More than one vertex stream for a custom layout. `VertexLayout.Add` and `RawVertexField` have no slot field. The native side supports several streams (`BindModelDrawCallVertexIds(..., nFirstStream)` in `RenderTools.def`), but only the `.vmdl` model path reaches it.
- `uint8` indices. The managed checks accept only 2-byte and 4-byte indices.
- Control of descriptor caching. `SetRenderState` runs for each draw, and its cost is native.

**Data that needs work at compile time:** sparse accessors, `uint8` indices, and strip or fan topologies.

## Storage formats

You can store the data in any format. The one-stream limit applies only to the fixed-function vertex input. Vertex pulling removes it.

**Each layer is a separate choice:**

| Layer | Who decides the format |
| --- | --- |
| `.glb` on disk | The glTF author, within the glTF spec. |
| `.glb_c` on disk | Our compiler. It can keep the glTF bytes as they are, quantize them, compress them (for example EXT_meshopt_compression), and order the LODs with the last LOD first. |
| GPU memory | Our loader. A `GpuBuffer` is plain bytes, so planar regions are valid. |
| Vertex shader input | Depends on the route below. |

**Three routes for the vertex shader:**

| Route | GPU storage | Per-frame cost | Shader change | Full format control |
| --- | --- | --- | --- | --- |
| A. Fixed-function vertex input | One interleaved stream (can be quantized) | None | No | No: one stream, and only formats the GPU can convert |
| B. Compute expand (current) | Planar source + interleaved output | One dispatch when the pose changes, plus memory for a second copy | No | Source yes, output no |
| C. Vertex pulling | Any layout, for example the glTF bytes as they are | A few buffer loads per vertex | Yes, our glTF vertex shader | Yes |

**Why route C costs nothing you can measure:** AMD GPUs have no separate vertex-fetch hardware, so the driver already turns route A into buffer loads in the shader. On NVIDIA the difference is small. This is well-documented driver behaviour, and I am highly confident in it. Do not check the format with `if` for each vertex. Compile one combo per format set instead. The `.glb_c` compiler decides how many format sets exist.

**Quantization in route A:** the GPU converts UNORM and SNORM values to float for free. So a 20 to 24 byte vertex works with no shader change, compared with 48+ bytes as floats. Octahedral normals need decode code in the shader.

The glTF vertex shaders in this repo are ours (added in `097f8395a`), so we can make route C. The earlier plan said "no shader changes". That was a choice in the plan, not an engine limit.

## Performance evidence

Each glTF draw costs about 2.4 ms on the CPU. The cause is that the driver allocates new GPU memory for each draw, every frame, in every view. The data format is not the cause.

Measured in the live editor with ABeautifulGame (KTX textures) on Linux with the NVK Vulkan driver:

| Fact | Value |
| --- | --- |
| Frame rate | About 2 fps |
| Main thread | 432 ms per frame in `RenderScene` at 0% CPU (it waits for the render job threads) |
| glTF instances | 49, one draw each (the editor makes one renderer for each node) |
| Draw calls | About 180 per frame (49 × 3 to 4 views), so about 2.4 ms per draw |
| Compute work | None per frame (the pose revision does not change) |
| Job thread stacks | `GltfGpuInstance.RenderExclusive` → `libengine2` → `librendersystemvulkan` → `nvk_AllocateMemory` (a kernel call), in 2 of 4 job threads in each sample |
| Native models on the same machine | 70 to 100 fps |

**Not proven yet:** which native call allocates. The native libraries are stripped, so the stacks do not name the functions. The candidates are:

1. `RenderTools.SetRenderState` with a new merged attribute set for each draw (`Graphics.Attributes.MergeTo(draw.RenderAttributes)` at `GltfGpuBackend.cs:1062`). About 70% likely.
2. `BindVertexBuffer` or `BindIndexBuffer` on raw `GpuBuffer`s.
3. `DrawIndexed`.

**Waste found in the managed draw callback** (small, but it happens every frame): `materialOverrides.Clone()`, LINQ in `UpdateLodLevels`, `draws.Where(IsDrawn).All(...)`, and an `OrderBy` for each view for the opaque layers. The code also makes a new `VertexLayout` for each primitive with a `Guid` name (`:497`). One cached layout for each signature is enough.

## Recommended route and next steps

Use vertex pulling (route C). Upload the glTF or `.glb_c` bytes as they are, and let our glTF vertex shader read them. This gives full control of the format, no conversion, and no per-frame cost you can measure. Fix the per-draw `SetRenderState` cost separately, because it limits every route.

**Target design:**

- **`.glb_c` compile:** keep planar accessors, quantize where it is safe, order the LODs with the last LOD first (already done for views), resolve sparse accessors, widen `uint8` indices, and convert strips and fans to lists.
- **Load:** copy each buffer into one `GpuBuffer` with `ByteAddress | Index`. Static meshes need no CPU conversion and no compute pass.
- **Draw:** bind the arena through `RenderAttributes`. Pass the accessor offsets, the strides and the format set as constants. Call `DrawIndexed(firstIndex, count, 0, baseVertex)`. Use one combo per format set.
- **Skinned and morphed meshes:** skin in the same vertex shader, or keep the compute pass. Decide later.
- **MSFT_lod, variants and material overrides:** keep the current managed logic (`GltfLodSelector` and the variant state).

**Order of work when we continue:**

- [ ] Step 0: time each native call in `RenderExclusive` behind a debug switch, to find what costs 2.4 ms per draw.
- [ ] Step 1: cache the merged attributes for each draw, remove the per-callback allocations, cache one `VertexLayout` for each signature, and skip draws that cast no shadow in shadow views.
- [ ] Step 2: if `SetRenderState` stays expensive, test the native `SceneObject` path for static meshes as a comparison.
- [ ] Step 3: write a concrete plan for vertex pulling: the shader changes, the `.glb_c` layout, the backend changes, and the tests. Write no code before the plan is approved.
- [ ] Step 4: build route C. Then measure fps, Render ms, draw calls, and a stack sample with no `nvk_AllocateMemory`. Compare with a native `.vmdl` that has a similar draw count.

## Side findings and open questions

**Side finding (an engine bug, not glTF):** the public `VertexLayout` in `engine/Sandbox.Engine/Systems/Render/VertexLayout.cs` swaps the integer formats: `uint` maps to `R32_SINT` and `int` maps to `R32_UINT`. Our backend does not use it, but addon code that uses `CommandList.DrawIndexed<T>` does. It is a candidate for a small upstream fix.

**Open questions:**

- Which native call allocates GPU memory for each draw? Step 0 answers this.
- Can `SetRenderState` reuse a binding set when the attributes do not change, or is the cost always native? We cannot know without the native source.
- Can the stock material shaders (not only our glTF shaders) be used with vertex pulling, or do glTF materials always need our own vertex shader?
- Skinning in the vertex shader or in compute: which costs less with 3 to 4 views per frame? Compute skinning runs once per pose. Vertex shader skinning runs once per view.
- The editor makes one renderer for each glTF node (49 draws in the test scene). Should one renderer draw the whole asset, to cut the overhead per renderer?
