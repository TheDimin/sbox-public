// ============================================================================
// DbpfEntry — Fully resolved index entry with all fields populated.
//
// The DBPF index uses a bitmask to hoist constant fields out of per-entry
// records. DbpfEntry is the *resolved* view where constant fields have been
// merged back in, giving every entry a uniform layout.
// ============================================================================

using System;
using Sims4.Dbpf.Enums;

namespace Sims4.Dbpf.Structures;

public readonly struct DbpfEntry : IEquatable<DbpfEntry>
{
    /// <summary>Resource type ID (e.g. GEOM = 0x015A1849).</summary>
    public readonly Sims4.Dbpf.Enums.ResourceType Type;

    /// <summary>Resource group / namespace.</summary>
    public readonly uint Group;

    /// <summary>Full 64-bit instance ID (Hi &lt;&lt; 32 | Lo).</summary>
    public readonly ulong Instance;

    /// <summary>Absolute file offset to the (possibly compressed) chunk data.</summary>
    public readonly uint ChunkOffset;

    /// <summary>On-disk compressed size. Bit 31 is an internal flag — mask with 0x7FFFFFFF.</summary>
    public readonly uint FileSizeRaw;

    /// <summary>Uncompressed / in-memory size.</summary>
    public readonly uint MemSize;

    /// <summary>Compression type (Zlib, None, etc.).</summary>
    public readonly CompressionType Compression;

    public DbpfEntry(
        Sims4.Dbpf.Enums.ResourceType type, uint group, ulong instance,
        uint chunkOffset, uint fileSizeRaw, uint memSize,
        CompressionType compression)
    {
        Type = type;
        Group = group;
        Instance = instance;
        ChunkOffset = chunkOffset;
        FileSizeRaw = fileSizeRaw;
        MemSize = memSize;
        Compression = compression;
    }

    /// <summary>Compressed byte count on disk (bit 31 masked off).</summary>
    public uint CompressedSize => FileSizeRaw & 0x7FFFFFFFu;

    /// <summary>Whether the high bit of FileSizeRaw is set (internal compression flag).</summary>
    public bool IsInternalCompressed => (FileSizeRaw & 0x80000000u) != 0;

    /// <summary>Whether this entry's data needs decompression.</summary>
    public bool IsCompressed => Compression == CompressionType.Zlib;

    /// <summary>The TGI key for dictionary lookups.</summary>
    public ResourceKey Key => new(Type, Group, Instance);

    public bool Equals(DbpfEntry other) =>
        Type == other.Type && Group == other.Group && Instance == other.Instance;

    public override bool Equals(object obj) => obj is DbpfEntry e && Equals(e);
    public override int GetHashCode() => HashCode.Combine(Type, Group, Instance);
    public static bool operator ==(DbpfEntry a, DbpfEntry b) => a.Equals(b);
    public static bool operator !=(DbpfEntry a, DbpfEntry b) => !a.Equals(b);

    public override string ToString() =>
        $"{Type} G:{Group:X8} I:{Instance:X16} @ 0x{ChunkOffset:X8} ({CompressedSize} -> {MemSize})";
}
