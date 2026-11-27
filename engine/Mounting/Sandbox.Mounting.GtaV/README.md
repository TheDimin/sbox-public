# Grand Theft Auto V Enhanced mount

Build the managed mount against the matching s&box engine assemblies on Windows. The project writes its output under the engine’s mount directory.

Install GTA V Enhanced and configure the [ragekit archive keys](https://github.com/RatPrez/ragekit/blob/9d4110ef0fce900787aaeccb52b67fa5402ff41b/docs/archives.md#keys) for the current Windows user. The mount checks the Steam installation and the Rockstar launcher registry. Set `GTA_V_ENHANCED_DIR` to select a different installation. Enable **Grand Theft Auto V Enhanced** in the s&box Mounts panel.

| Mounted path under `mount://gtav/` | Source |
| --- | --- |
| `maps/<map>.scene` | YMAP placements and referenced static collision dictionaries |
| `archetypes/<archetype>.prefab` | One prefab per YTYP archetype. Interior prefabs include base interior placements. |
| `models/fragments/<fragment>.vmdl` | Intact YFT with model break pieces |
| `models/fragments/<fragment>/gibs/<group>.vmdl` | Detachable YFT group with its own geometry and collision |
| `models/<drawable>.vmdl` | YDR geometry and embedded collision |
| `models/<dictionary>/<drawable>.vmdl` | Individual YDD member |
| `textures/<dictionary>/<texture>.vtex` | Individual YTD member with its mip chain |
| `collision/<bound>.vmdl` | YBN physics mesh. Scenes attach it to a static ModelCollider. |

The reader resolves duplicate archive entries using the game's DLC load order. It reads resources from the installed archives. The engine creates each requested asset in memory. The reader can cache archive index metadata under the local s&box data directory.

The mount builds geometry at the first available detail level. YTYP flags select fixed props, movable props, and doors. YMAP flags can fix a placement or suppress its embedded collision. Map YBN collision is independent. Movable collision uses convex hulls of the authored collision leaves. It uses the source diffuse and normal textures and builds trace geometry for viewport selection. Specialized GTA shaders, skeletal animation, and interior entity-set activation are outside this implementation.

YFT group strength below zero disables detachment. A fragment with detachable groups uses the lowest finite detachable strength as whole-prop health, with a minimum of 1 HP. This is a mount tuning rule. s&box spawns the model’s break list when the prop dies. The intact prop and all gib models load from the game archives at runtime.

## Rebuild in an open editor

Build the fork with the editor development reload API once. Open the mounted scene. In PowerShell 7.3 or later, run the following command from the repository root. Replace 12345 with the process ID of that editor.

```powershell
./engine/Mounting/Sandbox.Mounting.GtaV/Reload-Editor.ps1 -EditorProcessId 12345
```

The command builds the managed mount. Each build goes into a separate folder under `obj/editor`. The editor switches to that build and reloads the active mounted scene. The script leaves the installed mount in place for future editor sessions. Close the editor before removing old development builds.

## Compare geometry paths

The default build decodes GTA geometry and submits it through the standard mesh builders. Set the MSBuild property `GtaVUseBorealisGeometry=true` to compile the stored-geometry path. This option requires the matching fork assemblies. Run both builds on the same Borealis engine with the same scene, camera path and settings to compare geometry paths.

```powershell
dotnet build engine/Mounting/Sandbox.Mounting.GtaV/Sandbox.Mounting.GtaV.csproj -p:GtaVUseBorealisGeometry=false -p:GtaVMountOutputPath=./Temp/gtav-build/standard
dotnet build engine/Mounting/Sandbox.Mounting.GtaV/Sandbox.Mounting.GtaV.csproj -p:GtaVUseBorealisGeometry=true -p:GtaVMountOutputPath=./Temp/gtav-build/borealis
```

## World streaming

Open `scenes/gta_world.scene` in the Sandbox game to edit the camera, sky, sun and global post-processing at the Strawberry crossing. Its GtaWorld component streams the same retail archives as `mount://gtav/gtav.scene`. The mounted scene contains the world root and player spawn. The camera selects render placements using the YMAP LOD distance or, when the override is absent, the archetype LOD distance. Use `gtav_lod_scale 1.5` to extend authored distances by 50 percent. Use `gtav_lod_scale 1` to restore the authored distances. A change takes effect at a stationary camera. A physics focus controls collision within 80 metres. Set GtaWorld.PhysicsFocus to the controlled vehicle when needed. The default is the local player controller in play mode and the camera otherwise. Individual mounted maps still open for inspection.

Mounted archetype prefabs store disabled colliders. The physics streamer creates active colliders for nearby static bounds and props. Ladder GameObjects start disabled. The ladder streamer enables each trigger only when the physics focus is less than 5 metres from the closest point on its climb line. In play mode a ladder requires a local player or an explicit physics focus. The editor uses its camera if no focus is set. World model and texture packets come from the retail archives. Physics and rendering share map records until both release them. The engine mount can keep loaded visual resources cached until unmount.

### Fast camera movement

Fast flight restricts requests to authored levels 3, 4 and 6. After one second at rest or on descent, all authored levels become eligible. Level 5 is orphan HD. Flight starts above 65 m of estimated clearance and ends within 40 m. The map search uses authored streaming bounds and expands them conservatively when the multiplier exceeds one. The same multiplier applies to placement and child LOD distances. A placement with no positive distance on either the entity or archetype follows map eligibility. Physics keeps its own focus.

At a stationary focus, arrival of a YMAP adds its placements to the render queue and resolves pending parent and sibling links. A focus, distance setting or priority cap change rebuilds selection. A requested sibling group keeps its parent visible until the group is ready.
### Render request order

The render queue loads coarse coverage first. Within 120 metres of the camera it then prioritizes detailed structural placements and the parent and sibling representations needed for their LOD handoff. Interior placements qualify directly. Other detailed placements qualify when the archetype is static and its scaled horizontal footprint is at least 8 metres long and 3 metres wide. Remaining uncovered objects precede other detail upgrades.

Mount registration starts the map and collision bounds index on a worker. Entity records are still read on demand. A model request for a YDD member decompresses the container and decodes the selected drawable. It does not construct the other drawables in that dictionary.

### Measure an editor flight

Open `gta_world` and select its scene viewport. Run `./ProfileFlight.ps1 -EditorId <pid>` from this folder. Add `-Low` for the low approach. Moving or rotating the viewport camera cancels the flight and keeps your view. A completed flight restores the starting view. The output includes frame times for flight, descent, and the 39 seconds after arrival, plus the first observed activation time for the road and building.

The .NET meter `GtaV.WorldStreaming` reports two measurements tagged with `ymap` and the authored integer `lod`. The histogram `gtav.render.activation` measures milliseconds from dequeue to successful placement activation, including cache hits. The counter `gtav.render.selected` counts LOD selections for display, including reselections. GPU submission can occur later. Level 5 denotes orphan HD.

Use `gtav_lod_debug 1` to show the render LOD counts while moving through the world. Each cell shows placements selected for display divided by resident render placements. Distance is measured in metres from the streaming camera to the placement origin. A large mesh can extend closer than its origin. The overlay samples four times per second. Use `gtav_lod_debug 0` to turn it off.
