// ============================================================================
// StblReader — Parses decompressed STBL (String Table) resources (0x220557DA).
//
// Format:
//   Header: "STBL" magic, version u16, compressed u8, numEntries u64,
//           reserved u8[2], totalStringLength u32
//   Entries: key u32 (FNV32), flags u8, length u16, value char[length]
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace Sims4.Dbpf.Readers;

/// <summary>A single string table entry: FNV32 hash key → UTF-8 string value.</summary>
public readonly struct StblEntry
{
    /// <summary>FNV32 hash of the string key.</summary>
    public readonly uint Key;

    /// <summary>Per-entry flags byte.</summary>
    public readonly byte Flags;

    /// <summary>The string value (allocated).</summary>
    public readonly string Value;

    public StblEntry(uint key, byte flags, string value)
    {
        Key = key;
        Flags = flags;
        Value = value;
    }

    public override string ToString() => $"0x{Key:X8} = \"{Value}\"";
}

/// <summary>Parsed STBL resource with header metadata.</summary>
public sealed class StblResource
{
    public ushort Version;
    public byte Compressed;
    public ulong DeclaredEntryCount;
    public uint TotalStringLength;
    public StblEntry[] Entries = Array.Empty<StblEntry>();

    /// <summary>Builds a dictionary from FNV32 hash → string value.</summary>
    public Dictionary<uint, string> ToDictionary()
    {
        var dict = new Dictionary<uint, string>(Entries.Length);
        foreach (ref readonly var entry in Entries.AsSpan())
            dict.TryAdd(entry.Key, entry.Value);
        return dict;
    }
}

public static class StblReader
{
    /// <summary>Parses a decompressed STBL resource.</summary>
    public static StblResource Read(ReadOnlySpan<byte> data)
    {
        var r = new SpanReader(data);
        var stbl = new StblResource();

        // ---- Header (21 bytes) ----
        ReadOnlySpan<byte> magic = r.ReadTag(4);
        if (magic[0] != 'S' || magic[1] != 'T' || magic[2] != 'B' || magic[3] != 'L')
            throw new InvalidOperationException("Expected STBL magic tag.");

        stbl.Version = r.ReadU16();
        stbl.Compressed = r.ReadU8();
        stbl.DeclaredEntryCount = r.ReadU64();
        r.Skip(2); // reserved
        stbl.TotalStringLength = r.ReadU32();

        // ---- Entries ----
        int count = (int)stbl.DeclaredEntryCount;
        stbl.Entries = new StblEntry[count];

        for (int i = 0; i < count; i++)
        {
            uint key = r.ReadU32();
            byte flags = r.ReadU8();
            ushort length = r.ReadU16();
            string value = length > 0
                ? Encoding.UTF8.GetString(r.ReadBytes(length))
                : string.Empty;
            stbl.Entries[i] = new StblEntry(key, flags, value);
        }

        return stbl;
    }
}
