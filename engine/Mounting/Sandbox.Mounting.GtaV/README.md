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

The mount builds static geometry at the first available detail level. It uses the source diffuse and normal textures and builds trace geometry for viewport selection. Specialized GTA shaders, skeletal animation, fragment destruction, interior entity-set activation, and world streaming are outside this implementation.

## Rebuild in an open editor

Build the fork with the editor development reload API once. Open the mounted scene. In PowerShell 7.3 or later, run the following command from the repository root. Replace 12345 with the process ID of that editor.

```powershell
./engine/Mounting/Sandbox.Mounting.GtaV/Reload-Editor.ps1 -EditorProcessId 12345
```

The command builds the managed mount and its native reader together. Each build goes into a separate folder under `obj/editor`. The editor switches to that build and reloads the active mounted scene. The script leaves the installed mount in place for future editor sessions. Close the editor before removing old development builds.
