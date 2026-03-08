// ============================================================================
// DbpfIndexReader — Parses the DBPF index block with bitmask-hoisted fields.
//
// The index starts with a bitmask DWORD. Each set bit means the corresponding
// field is constant across all entries and stored once in the header.
// Per-entry records only contain fields whose bit is CLEAR.
// ============================================================================

using Sims4.Dbpf.Enums;
using Sims4.Dbpf.Structures;

namespace Sims4.Dbpf.Readers;

public static class DbpfIndexReader
{
    /// <summary>
    /// Parses the index block at the given span and populates the entry array.
    /// </summary>
    /// <param name="indexSpan">Span covering exactly the index block bytes.</param>
    /// <param name="entryCount">Number of entries (from the header).</param>
    /// <param name="entries">Pre-allocated array to fill. Must have length >= entryCount.</param>
    public static void Read(ReadOnlySpan<byte> indexSpan, int entryCount, DbpfEntry[] entries)
    {
        var r = new SpanReader(indexSpan);

        uint indexType = r.ReadU32();

        // Read constant values for bits that are SET
        ResourceType constType = (indexType & 0x01) != 0 ? (ResourceType)r.ReadU32() : 0;
        uint constGroup        = (indexType & 0x02) != 0 ? r.ReadU32() : 0;
        uint constInstanceHi   = (indexType & 0x04) != 0 ? r.ReadU32() : 0;
        uint constInstanceLo   = (indexType & 0x08) != 0 ? r.ReadU32() : 0;
        // Bit 4 (0x10) = ChunkOffset — never constant in practice, but handle it
        uint constChunkOffset  = (indexType & 0x10) != 0 ? r.ReadU32() : 0;
        uint constFileSize     = (indexType & 0x20) != 0 ? r.ReadU32() : 0;
        uint constMemSize      = (indexType & 0x40) != 0 ? r.ReadU32() : 0;
        uint constCompressed   = (indexType & 0x80) != 0 ? r.ReadU32() : 0;

        for (int i = 0; i < entryCount; i++)
        {
            ResourceType type = (indexType & 0x01) != 0 ? constType : (ResourceType)r.ReadU32();
            uint group        = (indexType & 0x02) != 0 ? constGroup : r.ReadU32();
            uint instanceHi   = (indexType & 0x04) != 0 ? constInstanceHi : r.ReadU32();
            uint instanceLo   = (indexType & 0x08) != 0 ? constInstanceLo : r.ReadU32();
            uint chunkOffset  = (indexType & 0x10) != 0 ? constChunkOffset : r.ReadU32();
            uint fileSizeRaw  = (indexType & 0x20) != 0 ? constFileSize : r.ReadU32();
            uint memSize      = (indexType & 0x40) != 0 ? constMemSize : r.ReadU32();

            CompressionType compression;
            if ((indexType & 0x80) != 0)
            {
                compression = (CompressionType)(constCompressed & 0xFFFF);
            }
            else
            {
                compression = (CompressionType)r.ReadU16();
                r.Skip(2); // unknown hi-word
            }

            ulong instance = ((ulong)instanceHi << 32) | instanceLo;

            entries[i] = new DbpfEntry(type, group, instance, chunkOffset, fileSizeRaw, memSize, compression);
        }
    }
}
