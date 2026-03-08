// ============================================================================
// RcolReader — Parses RCOL (Resource Container Object Linking) headers.
//
// Used by MODL (0x01661233), MLOD (0x01D10F34), and other RCOL-wrapped types.
// ============================================================================

using System;
using Sims4.Dbpf.Structures;

namespace Sims4.Dbpf.Readers;

public readonly struct ChunkLocation
{
    public readonly uint Position;
    public readonly uint Size;
    public ChunkLocation(uint position, uint size) { Position = position; Size = size; }
}

public readonly struct RcolChunkTag
{
    public readonly uint Tag;       // 4 ASCII bytes as a u32
    public readonly uint Version;

    public RcolChunkTag(uint tag, uint version) { Tag = tag; Version = version; }

    public string TagString => new(new[]
    {
        (char)(Tag & 0xFF),
        (char)((Tag >> 8) & 0xFF),
        (char)((Tag >> 16) & 0xFF),
        (char)((Tag >> 24) & 0xFF),
    });
}

public sealed class RcolResource
{
    public uint ContextVersion;
    public ResourceKey[] PublicKeys = Array.Empty<ResourceKey>();
    public ResourceKey[] ExternalKeys = Array.Empty<ResourceKey>();
    public ResourceKey[] DelayLoadKeys = Array.Empty<ResourceKey>();
    public ChunkLocation[] ObjectLocations = Array.Empty<ChunkLocation>();

    /// <summary>Raw chunk data following the RCOL header. Use ObjectLocations to index into it.</summary>
    public ReadOnlyMemory<byte> ChunkData;
}

public static class RcolReader
{
    /// <summary>
    /// Parses an RCOL container header. The returned <see cref="RcolResource.ChunkData"/>
    /// points into <paramref name="backingMemory"/> for zero-copy access to chunk data.
    /// </summary>
    public static RcolResource Read(ReadOnlySpan<byte> data, ReadOnlyMemory<byte> backingMemory)
    {
        var r = new SpanReader(data);
        var rcol = new RcolResource();

        rcol.ContextVersion = r.ReadU32();
        int publicKeyCount = (int)r.ReadU32();
        int externalKeyCount = (int)r.ReadU32();
        int delayLoadKeyCount = (int)r.ReadU32();
        int objectCount = (int)r.ReadU32();

        // Resource keys are in ITG order (Instance64, Type32, Group32) = 16 bytes
        rcol.PublicKeys = ReadKeys(ref r, publicKeyCount);
        rcol.ExternalKeys = ReadKeys(ref r, externalKeyCount);
        rcol.DelayLoadKeys = ReadKeys(ref r, delayLoadKeyCount);

        // Object data (position + length pairs)
        rcol.ObjectLocations = new ChunkLocation[objectCount];
        for (int i = 0; i < objectCount; i++)
        {
            uint pos = r.ReadU32();
            uint len = r.ReadU32();
            rcol.ObjectLocations[i] = new ChunkLocation(pos, len);
        }

        // Everything after here is chunk data
        if (r.Remaining > 0)
            rcol.ChunkData = backingMemory.Slice(r.Position, r.Remaining);

        return rcol;
    }

    /// <summary>Reads the chunk tag (4 bytes + version u32) at the start of a chunk.</summary>
    public static RcolChunkTag ReadChunkTag(ReadOnlySpan<byte> chunkData)
    {
        var r = new SpanReader(chunkData);
        uint tag = r.ReadU32();
        uint version = r.ReadU32();
        return new RcolChunkTag(tag, version);
    }

    private static ResourceKey[] ReadKeys(ref SpanReader r, int count)
    {
        if (count == 0) return Array.Empty<ResourceKey>();
        var keys = new ResourceKey[count];
        for (int i = 0; i < count; i++)
            keys[i] = ResourceKey.ReadITG(ref r);
        return keys;
    }
}
