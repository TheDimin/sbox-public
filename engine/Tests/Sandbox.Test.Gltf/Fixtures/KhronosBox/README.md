# Khronos Box test data

The embedded byte strings in `GltfDracoTests.cs` contain the geometry buffers
`Models/Box/glTF/Box0.bin` and `Models/Box/glTF-Draco/Box.bin` from the
[Khronos glTF Sample Assets repository](https://github.com/KhronosGroup/glTF-Sample-Assets/tree/90d7ede14c7e280af263824604b427a1ca02cb66/Models/Box)
at revision `90d7ede14c7e280af263824604b427a1ca02cb66`. The test constructs
minimal glTF JSON around those unmodified buffers and compares decoded geometry.

Box is copyright 2017 Cesium and is licensed under the Creative Commons
Attribution 4.0 International license. The complete license is reproduced in
`CC-BY-4.0.txt`; the upstream asset's license notice is `UPSTREAM-LICENSE.md`.
