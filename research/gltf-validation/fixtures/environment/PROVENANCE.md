# Artist Workshop environment fixture

- Artist: Oliksiy Yakovlyev. Asset: [Artist Workshop](https://polyhaven.com/a/artist_workshop), published by Poly Haven under [CC0](https://polyhaven.com/license).
- Original 1K Radiance HDR source: <https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/1k/artist_workshop_1k.hdr>
- SHA-256: `f6da9025acadb1149c973fe2bef53d55f4e7144285a87ec1a357520fbcad4767`.
- 1024 × 512 equirectangular RGBE. The original bytes are retained unchanged.
- The pinned Khronos AttenuationTest README explicitly identifies this environment. For other cases this is a chosen comparison environment, not evidence of the environment used in their reference images.
- The adjacent vtex is an **unvalidated import proposal**. Its linear CUBE input follows the shipped `skybox_default.vtex`; its `GGXCubeMapBlur` mip processor follows the shipped baked envmap. The coordinator must verify the resource mount, compiler output, orientation, HDR range, and reflective response before use. No render or exposure match is implied.

The native compiler rejected the Radiance `.hdr` source (`bitmap/floatbitmap.cpp: Unknown file type .hdr`). The pending guarded retry transcodes the same bytes with OpenImageIO to ZIP-compressed float32 linear OpenEXR and requires exact equality of every decoded RGB float, finite values, and maximum > 1 before updating the same staged vtex. It records decoder version, dimensions, range, means and SHA-256. This is an unexecuted conversion plan until the coordinator's receipt exists; neither tone mapping nor an 8-bit intermediate is permitted.
