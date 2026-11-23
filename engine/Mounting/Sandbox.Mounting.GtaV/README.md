# Grand Theft Auto V Enhanced mount

Build the engine on Windows with CMake and the Visual Studio x64 C++ tools installed. The project builds the pinned MIT-licensed [ragekit reader](https://github.com/RatPrez/ragekit/tree/9d4110ef0fce900787aaeccb52b67fa5402ff41b) and installs the managed mount and native reader into the engine. Its first build needs Git and network access.

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

The reader resolves duplicate archive entries using the game's DLC load order. It reads resources from the installed archives. The engine creates each requested asset in memory. The reader can cache archive index metadata in the user's ragekit directory.

The mount builds geometry at the first available detail level. YTYP flags select fixed props, movable props, and doors. YMAP flags can fix a placement or suppress its embedded collision. Map YBN collision is independent. Movable collision uses convex hulls of the authored collision leaves. It uses the source diffuse and normal textures and builds trace geometry for viewport selection. Specialized GTA shaders, skeletal animation, interior entity-set activation, and world streaming are outside this implementation.

YFT group strength below zero disables detachment. A fragment with detachable groups uses the lowest finite detachable strength as whole-prop health, with a minimum of 1 HP. This is a mount tuning rule. s&box spawns the model’s break list when the prop dies. The intact prop and all gib models load from the game archives at runtime.

## Rebuild in an open editor

Build the fork with the editor development reload API once. Open the mounted scene. In PowerShell 7.3 or later, run the following command from the repository root. Replace 12345 with the process ID of that editor.

```powershell
./engine/Mounting/Sandbox.Mounting.GtaV/Reload-Editor.ps1 -EditorProcessId 12345
```

The command builds the managed mount and its native reader together. Each build goes into a separate folder under `obj/editor`. The editor switches to that build and reloads the active mounted scene. The script leaves the installed mount in place for future editor sessions. Close the editor before removing old development builds.
