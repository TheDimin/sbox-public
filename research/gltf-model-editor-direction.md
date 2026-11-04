# glTF model editor direction

Status: design direction, not an implementation plan. This document records the
agreed editor experience and its relationship to the broader glTF work. Native
engine capabilities and the exact modifier representation still require an audit.

## Intent

The model editor should feel like editing a model in an s&box scene: the model
is the central interactive workspace, its hierarchy is selectable, and the
inspector exposes authoring tools in a familiar component-like interface.
The goal is a more mature replacement for the ModelDoc workflow for new
glTF/GLB assets, while existing vmdl assets and their editor remain supported.

The editor handles model authoring, not gameplay composition. Modifiers expose
the kinds of work that would be done in ModelDoc. They do not attach gameplay
components to every model instance. The generated model data must still work
with existing game-facing model, rendering, animation, and physics APIs.

## Workspace

- A scene-style viewport displays **only the model for now**. Do not add
  preview floors, reference props, or a configurable preview environment as
  part of this direction. Editor camera and lighting needed to render the
  preview are viewport infrastructure, not authored model content.
- A hierarchy represents the model's nodes, meshes, skeleton, attachments,
  and other relevant authored elements. Selecting in the hierarchy and in
  the viewport should stay synchronized. Multi-selection applies compatible
  edits to multiple targets.
- The inspector shows the selected element's properties and modifier cards.
  An Add Modifier control should feel like Add Component: searchable,
  categorized, with useful defaults, validation, and clear target scope.
- Use appropriate viewport gizmos and overlays to edit and inspect collision,
  hitboxes, attachments, bones, bounds, LODs, materials, and animation.
  Animation and physics previews should show the actual model behavior.
- Separate preview-only selection, camera, overlays, and simulation state
  from changes to the glTF asset. Saving the model must not accidentally
  persist temporary scene objects or preview state.

## Modifier model

Modifiers are typed, inspectable authoring operations or settings attached to
the appropriate model, node, mesh, bone, or other target. Examples include:

- Collision hulls or meshes, physical surfaces, mass, and related settings.
- LOD assignment and generation; bodygroups and their mesh choices.
- Attachments, hitboxes, ragdoll bodies, and joints.
- Material assignment and standard material variants.
- Model-wide animation-graph references and existing model game data,
  including project-defined types.

Some geometry-processing operations can be order-dependent. Other model
settings are independent and should not acquire artificial stack-order
semantics merely because they are displayed as cards. Define behavior,
dependencies, enable/disable rules, and ordering per modifier type.

Preview changes promptly, retain the last usable preview if rebuilding fails,
and provide undo/redo for document edits and viewport drags. Saving and
reopening must preserve the authored result. Audit the full existing ModelDoc
feature set, including specialized features, before declaring parity.

## glTF and material boundaries

- Edit both `.gltf` and `.glb` source assets directly. Generate an ignored
  `.glb_c` from either source when needed and package the generated artifact
  for builds. The `.glb_c` must contain a valid, portable optimized GLB; native
  loader integration and performance are not yet proven.
- Store model-authored data in the glTF/GLB, using established specifications
  where suitable and documented s&box extensions only after an extension
  audit. `.meta` is for import settings, not model data.
- Materials are model-owned by default. External textures are preferred for
  authoring, but valid embedded glTF textures must also work. A model material
  may carry a shader or graph reference. Shared material-only glTF/GLB assets
  and optional native `.vmat` links are explicit authoring choices; automatic
  compilation deduplication must not silently link independent materials.
- Use `KHR_materials_variants`: one active named variant per model instance,
  with slot mappings and standard fallback behavior. Per-instance s&box
  material overrides remain supported without changing legacy vmdl behavior.
- Preserve unknown optional extensions and extras when it is safe to do so;
  warn or block an edit that could silently invalidate data the editor cannot
  interpret. Unsupported required extensions must produce a clear error.
- Maintain a clear distinction between authored glTF scenes and the temporary
  editor scene used to preview a model.

## Reimport and preservation

Reimport should preserve model-authored settings, material edits, shared links,
and variant assignments where targets can be matched confidently. Conflicts
must be shown rather than silently applying settings to the wrong object.
The user chose to reload external changes and discard unsaved editor edits
when a source file is overwritten externally.

There is an unresolved limitation: if an external exporter overwrites the
only source file and strips **saved but uncommitted** s&box data, version
control cannot recover data it never recorded. A protected reimport workflow,
recovery snapshots, or another explicit preservation mechanism is needed to
guarantee non-destructive reimport in that case. Do not claim that matching
or conflict UI alone solves the loss.

## Existing s&box starting points

- `SceneViewportWidget` combines a renderer, editor camera, gizmos, and scene
  selection. `SceneRenderingWidget` and isolated editor scenes are used by
  existing previews. Reuse is a candidate, not proof of drop-in support.
- The scene inspector and `ControlSheet` provide component-like editing
  patterns. Existing component sheets and scene undo scopes assume scene
  objects; a model-document adapter and undo transactions need investigation.
- Existing mesh tracing reports rendering information, not a glTF node or
  primitive identity. Selecting shared, instanced, skinned, or LOD meshes
  requires a verified mapping from preview hits to source elements.
- The current ModelDoc tool includes native code outside this repository.
  Complete parity requires editor-level inspection and acceptance tests, not
  only a managed-source audit.

## Acceptance of the editor experience

Verify viewport/hierarchy selection and multi-selection; adding, editing,
enabling, disabling, and removing each applicable modifier; gizmos and
undo/redo; save/reopen; material and variant editing; collision and ragdoll
preview; LOD and animation preview; reimport and conflict handling; and that
preview-only state never enters the model. Test picking across shared meshes,
skinned meshes, instancing, and LOD changes. Compare supported model features
and runtime behavior with equivalent vmdl assets.

## Managed rendering direction

Prefer C# ownership of glTF parsing, preparation, hierarchy, animation, GPU
data, draw submission, and editor integration. Reuse native services when they
provide concrete value, such as graphics-device operations, scene scheduling,
lighting, shader compilation, or physics. A native ModelBuilder or vmdl
conversion is not a prerequisite merely to render geometry.

Source inspection confirms `GpuBuffer` supports vertex, index, structured,
byte-address, and indirect-argument buffers. `Graphics.Draw` and `CommandList`
support direct and indexed drawing; command lists also support instancing,
indirect drawing, and shader-side vertex fetching through `SV_VertexID`.
`SceneCustomObject` supplies a scene rendering callback. `SceneLineObject`
is an existing example combining C# GPU buffers, compute work, barriers,
lighting setup, and indexed drawing.

These mechanisms support investigating a managed glTF rendering path without
converting geometry through the native model builder. Earlier restrictions
observed in `Mesh` and `ModelBuilder` are not proof of restrictions in this
separate path. Conventional `VertexLayout` mapping has its own restrictions;
shader-side vertex fetching is an alternative to investigate for prepared
glTF layouts.

Validate opaque, masked, transparent, depth, shadow, lighting, and motion
behavior, bounds/culling, skinning/morph evaluation, material override
compatibility, resource lifetimes, and existing model APIs. The buffer API
currently creates semistatic buffers and its `SetData` upload is synchronous;
allocation and upload policy need measurement before claiming vmdl-equivalent
loading or memory use. Keep the portable `.glb_c` and benchmark requirements.

No implementation or performance claim is made by this document.
