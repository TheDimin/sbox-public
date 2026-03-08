# Sims4.Dbpf — Zero-Allocation DBPF 2.1 Parser for C#

A high-performance, memory-mapped parser for The Sims 4 `.package` files (DBPF 2.1 format). Built for GB-scale files with minimal allocations and span-based APIs.

## Architecture

```
DbpfPackage                     ← Main entry point (memory-mapped file)
├── DbpfHeader                  ← 96-byte blittable struct (MemoryMarshal.Read)
├── DbpfEntry[]                 ← Resolved index entries (bitmask-aware)
├── GetRawData(entry)           ← Zero-copy span into memory map
├── GetResourceData(entry)      ← Auto-decompresses (ArrayPool-backed)
│
├── Readers/
│   ├── DbpfIndexReader         ← Parses variable-width index with bitmask
│   ├── GeomReader              ← GEOM mesh: verts, faces, bones, MTNF shader
│   ├── StblReader              ← STBL string table: FNV32 → UTF-8 strings
│   ├── CobjReader              ← COBJ catalog: price, tags, categories
│   └── RcolReader              ← RCOL container: MODL/MLOD wrapper
│
├── Resources/
│   └── GeomStructures          ← Vertex64, Vec3, Vec2, Triangle, etc.
│
├── Structures/
│   ├── DbpfHeader              ← Blittable 96-byte header
│   ├── DbpfEntry               ← Resolved per-resource record
│   └── ResourceKey             ← Type-Group-Instance lookup key
│
├── Enums/
│   ├── ResourceType            ← All known Sims 4 type IDs
│   └── CompressionType         ← None / Zlib / Streamable / Deleted
│
├── SpanReader                  ← ref struct binary reader (zero-alloc core)
└── PackageExtensions           ← One-call convenience: ReadGeom, ReadStbl, etc.
```

## Design Principles

| Principle | Implementation |
|---|---|
| **No managed heap for index parsing** | `MemoryMappedFile` + `ReadOnlySpan<byte>` — the OS pages data on demand |
| **Zero-copy where possible** | Vertex buffers, index buffers, raw chunk data are `ReadOnlyMemory<byte>` slices |
| **Pooled decompression** | Zlib inflate uses `ArrayPool<byte>.Shared` — returned via disposable `ResourceData` |
| **Blittable structs** | `DbpfHeader`, `Vertex64`, `Vec3`, `Triangle` can be reinterpreted directly from bytes |
| **Allocation-free enumeration** | `EntryTypeEnumerator` is a value-type enumerator over entries by type |
| **Mixed-endian awareness** | GEOM's BE fields (`ElementDefinition.ByteSize`, `FaceGroup.IndexByteSize`) handled explicitly |

## Quick Start

```csharp
using Sims4.Dbpf;
using Sims4.Dbpf.Enums;

// Open a package (memory-mapped, ~instant for any file size)
using var package = DbpfPackage.Open("MyMod.package");

// Iterate all GEOM meshes
foreach (ref readonly var entry in package.EntriesOfType(ResourceType.GEOM))
{
    var geom = package.ReadGeom(in entry);
    Console.WriteLine($"{geom.VertexCount} vertices, {geom.FaceGroups[0].TriangleCount} triangles");

    // Zero-copy typed vertex access (standard 64-byte layout)
    if (geom.VertexStride == 64)
    {
        ReadOnlySpan<Vertex64> verts = geom.GetVertices64();
        // Direct access to position, normal, UV, bone weights...
    }
}

// Read string tables
foreach (ref readonly var entry in package.EntriesOfType(ResourceType.STBL))
{
    var stbl = package.ReadStbl(in entry);
    var dict = stbl.ToDictionary(); // FNV32 hash → string
}

// Read catalog objects
foreach (ref readonly var entry in package.EntriesOfType(ResourceType.COBJ))
{
    var cobj = package.ReadCobj(in entry);
    Console.WriteLine($"§{cobj.Common.SimoleonPrice} — {cobj.Tags.Length} tags");
}

// Look up by TGI key
var key = new ResourceKey(ResourceType.STBL, 0x80000000, 0x001234567890ABCD);
if (package.TryGetEntry(key, out var found))
{
    using var data = package.GetResourceData(in found);
    // data.Span is the decompressed bytes
}
```

## Index Bitmask

The DBPF index uses a clever bitmask to reduce file size. When a bit is SET, that field is constant across all entries and stored once in the index header. Per-entry records only contain fields whose bit is CLEAR.

| Bit | Mask | Field |
|-----|------|-------|
| 0 | 0x01 | ResourceType |
| 1 | 0x02 | ResourceGroup |
| 2 | 0x04 | InstanceHi |
| 3 | 0x08 | InstanceLo |
| 4 | 0x10 | ChunkOffset (never constant) |
| 5 | 0x20 | FileSize |
| 6 | 0x40 | MemSize |
| 7 | 0x80 | Compression + pad |

Most Sims 4 packages use `indexType = 0x00000002` (only ResourceGroup is constant).

## GEOM Mixed Endianness

The GEOM format has a quirk: `ElementDefinition.ByteSize` and `FaceGroup.IndexByteSize` are stored **big-endian** while everything else is little-endian. The Python reference implementation (SmugTomato/io_simgeom) works around this by skipping these fields. This library reads them correctly using `BinaryPrimitives.ReadUInt32BigEndian`.

## Requirements

- .NET 8.0+
- `AllowUnsafeBlocks` (for memory-mapped pointer access)
- No external NuGet dependencies

## File Structure Reference

Based on ImHex pattern files for DBPF 2.1, GEOM, STBL, and COBJ formats.
