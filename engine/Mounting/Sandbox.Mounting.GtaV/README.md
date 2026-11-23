# Grand Theft Auto V Enhanced mount

Build the engine on Windows with CMake and the Visual Studio x64 C++ tools installed. The project builds the pinned MIT-licensed [ragekit reader](https://github.com/RatPrez/ragekit/tree/9d4110ef0fce900787aaeccb52b67fa5402ff41b) and installs the managed mount and native reader into the engine. Its first build needs Git and network access.

Install GTA V Enhanced and configure the [ragekit archive keys](https://github.com/RatPrez/ragekit/blob/9d4110ef0fce900787aaeccb52b67fa5402ff41b/docs/archives.md#keys) for the current Windows user. The mount checks the Steam installation and the Rockstar launcher registry. Set `GTA_V_ENHANCED_DIR` to select a different installation. Enable **Grand Theft Auto V Enhanced** in the s&box Mounts panel.

| Mounted path under `mount://gtav/` | Source |
| --- | --- |
| `maps/<map>.scene` | YMAP placements and referenced static collision dictionaries |
| `archetypes/<archetype>.prefab` | One prefab per YTYP archetype. Interior prefabs include base interior placements. |
| `models/<drawable>.vmdl` | YDR geometry and embedded collision |
| `models/<dictionary>/<drawable>.vmdl` | Individual YDD member |
| `models/fragments/<fragment>.vmdl` | YFT intact geometry and static collision |
| `textures/<dictionary>/<texture>.vtex` | Individual YTD member with its mip chain |
| `collision/<bound>.vmdl` | YBN physics mesh. Scenes attach it to a static ModelCollider. |

The reader resolves duplicate archive entries using the game's DLC load order. It reads resources from the installed archives. The engine creates each requested asset in memory. The reader can cache archive index metadata in the user's ragekit directory.

The mount builds static geometry at the first available detail level. It uses the source diffuse and normal textures and builds trace geometry for viewport selection. Specialized GTA shaders, skeletal animation, fragment destruction, and interior entity-set activation are outside this implementation.

## Rebuild in an open editor

Build the fork with the editor development reload API once. Open the mounted scene. In PowerShell 7.3 or later, run the following command from the repository root. Replace 12345 with the process ID of that editor.

```powershell
./engine/Mounting/Sandbox.Mounting.GtaV/Reload-Editor.ps1 -EditorProcessId 12345
```

The command builds the managed mount and its native reader together. Each build goes into a separate folder under `obj/editor`. The editor switches to that build and reloads the active mounted scene. The script leaves the installed mount in place for future editor sessions. Close the editor before removing old development builds.

## World streaming

Open `mount://gtav/gtav.scene` for the assembled world. The scene camera starts above the Strawberry road crossing. The camera controls rendering within 350 metres. A physics focus controls collision within 80 metres. Set GtaWorld.PhysicsFocus to the controlled vehicle when needed. The default is the local player controller in play mode and the camera otherwise. Individual mounted maps still open for inspection.

Mounted archetype prefabs store disabled colliders. The physics streamer creates active colliders for nearby static bounds and props. Ladder GameObjects start disabled. The ladder streamer enables each trigger only when the physics focus is less than 5 metres from the closest point on its climb line. In play mode a ladder requires a local player or an explicit physics focus. The editor uses its camera if no focus is set. World model and texture packets come from the retail archives. Physics and rendering share map records until both release them. The engine mount can keep loaded visual resources cached until unmount.

### Crossing check

Use Enhanced world index key `c1426cd87425d39a` with the multiplayer map replacements enabled. The patched maps are in `update/update.rpf/dlc_patch/mplowrider/x64/levels/gta5/_citye/scentral_01/scentral_metadata.rpf`. The patched road collision is in the sibling archive `sc1_rd.rpf`. The route uses GTA metres. In s&box, divide each coordinate by 0.0254.

| Event | Verified GTA source or Enhanced data | s&box proof of concept |
| --- | --- | --- |
| Approach | `lr_sc1_rd_strm_4` has streaming bounds (-43.90, -1510.53, -121.70) to (497.32, -1031.23, 181.77). Its entity bounds are (39.17, -1430.81, 27.43) to (364.57, -1149.93, 47.21). The parent map is `lr_sc1_rd`. | Approach (202.80, -1327.65, 32) along the X axis. A 256-metre grid indexes entity and static collision bounds. |
| Enter detail range | Entity 132, `sc1_rd_r2_06`, has distance 129 m. Its flags select parent-map entity 502. That entity uses drawable `sc1_rd_r2a_slod1_children/hash_63D803DC`, has one child, and has distance 400 m. Nearby maps include `lr_sc1_07`, `lr_sc1_rd_critical_1`, and `hei_fwy_02_long_0`. | The parent remains available during child preparation. At the camera start, the child was enabled and the parent disabled. A 350 m render limit applies to this prototype. |
| Cross | A vertical triangle test at (202.80188, -1327.64526) hit `lr_sc1_rd_15.ybn` at Z 28.340. An AABB test alone also returned unrelated YBN files. | Play-mode traces at X offsets -6, 0 and +6 m hit the road. The first hit `ma@lr_sc1_rd_1`. The other two hit `lr_sc1_rd_15`. |
| Depart or reverse | The GTA store checks child, reference and dependent counts before release. The exact retail rule for an in-flight reversal remains unverified. | Both subsystems check current demand before attaching completed work. The test moved the camera 2,032 m away, reversed it, and kept an independent physics focus at the road. |

### Source trace

The source snapshot came from VHD ID `B9F0F9253EAF0A43870E68CED3F241B7`. Its retail revision is unknown. The s&box graphs cover the working files based on `7081e9103`. Graphify 0.9.112 used local AST extraction with `graphify update <tree> --no-cluster`. The GTA tree contains the game scene and streaming folders, framework fwscene, fwsys and streaming folders, and the streaming script commands. The s&box trees are `engine/Sandbox.Engine/Scene` and `engine/Mounting/Sandbox.Mounting.GtaV`.

Use `graphify explain fwMapDataStore --graph <gta-graph>`, `graphify explain gta5_src_dev_ng_rage_framework_src_fwscene_stores_boxstreamer_fwboxstreamervariable --graph <gta-graph>`, `graphify explain gameobjectsystem_gameobjectsystem_sandbox_gameobjectsystem_1 --graph <scene-graph>`, and `graphify explain MapLoader --graph <mount-graph>`. Check these conditions in source. Some C++ macros and indirect calls lack graph edges.

- `game/scene/streamer/SceneStreamerList.cpp`, `UpdateInteriorAndCamera`, adds player-speed read-ahead to the camera position used for fade scoring.
- `game/script/commands_streaming.cpp`, `SetFocusPosAndVel` and `ClearFocus`, set an explicit streaming focus and restore the default. The setter checks for an active player switch.
- `game/streaming/BoxStreamer.cpp`, `CGTABoxStreamerInterfaceNew::GetGameSpecificSearches`, adds mover searches for eligible physical entities that require collision. Frozen objects are excluded.
- `rage/framework/src/fwscene/stores/boxstreamer.cpp`, the critical ensure-loaded path near line 392, requests missing streamable assets that pass its asset filters. `fwMapDataStore::PlaceAsynchronously` calls `RequestAsyncPlacement` for the map file. These are separate stages.
- `rage/framework/src/fwscene/mapdata/mapdatacontents.cpp`, `fwMapDataContents::Entities_Create`, selects the parent map when entity flag bit 3 is set. `fwMapDataStore::RemoveUnrequired` checks for loaded children, box-streamer retention, swapper use, delete flags, reference count and dependents.
- `Scene.InitSystems`, `Scene.SharedTick`, and `SceneLoader<T>.Load` establish the s&box scene lifecycle. The streamer attaches its update hook only while the world component is enabled. Workers read archive packets. The engine thread creates components, physics models, meshes, and textures.
