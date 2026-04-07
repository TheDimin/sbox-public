# Sandbox.Mounting.Sims4

Mount The Sims 4 assets directly into s&box -- models, textures, materials, and catalog metadata, all converted on the fly.

## What is this?

This is an s&box game mount that reads The Sims 4's `.package` files (DBPF v2 format) and exposes their contents as native s&box resources. If you have The Sims 4 installed via Steam, this mount auto-detects it and makes thousands of meshes,available in the engine without any manual conversion step.

<img width="2174" height="1216" alt="image" src="https://github.com/user-attachments/assets/564db238-bf33-4f64-8ecf-a5ea672b36e8" />
<img width="926" height="629" alt="image" src="https://github.com/user-attachments/assets/c403b888-6d26-40bf-9552-c48ffb384a16" />

## Quick look

Once mounted, Sims 4 assets appear under the `sims4` mount prefix:

```
mount://sims4/models/{category}/{name}.vmdl\
mount://sims4/catalogs/{group}_{instance}.s4cor
```

Models load with full material chains -- diffuse, normal, specular, emissive maps, alpha testing, translucency, and two-sided rendering all wired up automatically.

## Supported asset types

| Asset | Formats | Notes |
|-------|---------|-------|
| **Models** | MODL, ~~GEOM~~, VBUF, IBUF | Multi-LOD, multi-mesh, collision generation |
| **Textures** | DST, RLE, DDS | Auto-unshuffles DST, handles mipmaps |
~~| **Materials** | MATD | 50+ shader types mapped to the `sims4` shader |~~
| **Catalogs** | COBJ, CFLR, CFLT, CWAL | Serialized as JSON with resolved strings and thumbnails |
~~| **Character** | RIG, CASPART | Parsed but not fully mounted yet |~~

## Prerequisites

- [s&box](https://sbox.game) with the engine source / mounting framework
- The Sims 4 installed via **Steam** (app ID `1222670`)

## Building

The project builds as part of the s&box engine solution. The post-build step copies the output to `game/mount/sims4/` and compiles the `sims4.shader`:

```bash
dotnet build Sandbox.Mounting.Sims4.csproj
```

No external NuGet packages are required -- everything uses .NET built-ins (`System.IO.Compression`, `System.Text.Json`).

## How it works

1. **Detection** -- On startup, checks the Windows registry for a Steam installation of The Sims 4.
2. **Package scan** -- Opens all `Data/*.package` files in parallel and indexes resources by type.
3. **Lazy loading** -- Resources are decompressed and converted only when the engine requests them. Nothing is unpacked upfront.
4. **Conversion** -- Each resource type has a dedicated loader that translates Sims 4 formats into s&box equivalents (MODL to VMDL, DST to VTEX, MATD to VMAT, etc.).

## Project structure

```
SimsMount.cs                 # IGameMount entry point, detection, registration
Resource/                    # s&box resource loaders (model, texture, material, catalog)
Sims4Reader/
  Core/                      # DBPF package reader, compression, resource keys
  Mesh/                      # MODL/GEOM/VBUF/IBUF parsers
  Material/                  # MATD/MTNF/MTST parsers
  Image/                     # DST, RLE, DDS, thumbnail decoders
  Resources/                 # COBJ, OBJD, STBL, string tables
  Rcol/                      # RCOL container format (VPXY, FTPT, LITE)
  Character/                 # CASPART, RIG (skeleton) parsers
Assets/shaders/sims4.shader  # Custom s&box shader for Sims 4 materials
```

## Contributing

1. Fork and clone the repo.
2. Build with `dotnet build` -- warnings are treated as errors in both Debug and Release.
3. The project uses C# 14, nullable reference types, and allows `unsafe` blocks for performance-critical paths.
4. Test against a real Sims 4 install -- there are no mock packages.
5. Submit a PR against `main`.
