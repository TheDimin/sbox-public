// ============================================================================
// GeomReader — Parses decompressed GEOM resources (0x015A1849).
//
// GEOM files are wrapped in an RCOL container. This reader parses both
// the RCOL header and the GEOM chunk.
//
// ⚠️ Mixed endianness: ElementDefinition.ByteSize and FaceGroup header
//    fields are stored big-endian while everything else is little-endian.
// ============================================================================

using System;
using Sims4.Dbpf.Resources;
using Sims4.Dbpf.Structures;

namespace Sims4.Dbpf.Readers;

public static class GeomReader
{
    /// <summary>
    /// Parses a decompressed GEOM resource (RCOL wrapper + GEOM chunk).
    /// The <paramref name="backingMemory"/> must remain alive while the
    /// returned <see cref="GeomResource"/> is in use (for zero-copy vertex/index spans).
    /// </summary>
    public static GeomResource Read(ReadOnlySpan<byte> data, ReadOnlyMemory<byte> backingMemory)
    {
        var geom = new GeomResource();
        var r = new SpanReader(data);

        // ---- RCOL Header ----
        SkipRcolHeader(ref r);

        // ---- GEOM Chunk Header ----
        ReadOnlySpan<byte> tag = r.ReadTag(4);
        if (tag[0] != 'G' || tag[1] != 'E' || tag[2] != 'O' || tag[3] != 'M')
            throw new InvalidOperationException("Expected GEOM magic tag.");

        geom.Version = r.ReadU32();
        _ = r.ReadU32(); // dataSize
        _ = r.ReadU32(); // flags

        // ---- Embedded Shader (MTNF) ----
        geom.EmbeddedShaderId = r.ReadU32();
        if (geom.EmbeddedShaderId != 0)
            ReadMtnfBlock(ref r, geom, backingMemory);

        // ---- Mesh Properties ----
        geom.MergeGroup = r.ReadU32();
        geom.SortOrder = r.ReadU32();
        geom.VertexCount = (int)r.ReadU32();
        int elementCount = (int)r.ReadU32();

        // ---- Element Definitions (mixed endianness!) ----
        geom.Elements = new ElementDefinition[elementCount];
        int stride = 0;
        for (int i = 0; i < elementCount; i++)
        {
            var elemType = (VertexElementType)r.ReadU32();
            byte subType = r.ReadU8();
            uint byteSize = r.ReadU32BE(); // ⚠️ BIG-ENDIAN
            geom.Elements[i] = new ElementDefinition(elemType, subType, byteSize);
            stride += (int)byteSize;
        }
        geom.VertexStride = stride;

        // ---- Vertex Buffer (zero-copy slice) ----
        int vertexBytes = geom.VertexCount * stride;
        geom.VertexData = backingMemory.Slice(r.Position, vertexBytes);
        r.Skip(vertexBytes);

        // ---- Face Group ----
        {
            byte formatType = r.ReadU8();
            uint indexByteSize = r.ReadU32BE(); // ⚠️ BIG-ENDIAN
            int numIndices = (int)r.ReadU32();
            int indexDataBytes = numIndices * (int)indexByteSize;
            var indexData = backingMemory.Slice(r.Position, indexDataBytes);
            r.Skip(indexDataBytes);
            geom.FaceGroups = new[] { new FaceGroup((int)indexByteSize, numIndices, indexData) };
        }

        // ---- UV Map Entries (variable-length) ----
        if (r.Remaining >= 4)
        {
            int uvEntryCount = (int)r.ReadU32();
            geom.UvMapEntries = new UvMapEntry[uvEntryCount];
            for (int i = 0; i < uvEntryCount; i++)
            {
                uint vertexId = r.ReadU32();
                int pairCount = (int)r.ReadU32();
                var pairs = new Vec2[pairCount];
                for (int p = 0; p < pairCount; p++)
                    pairs[p] = r.ReadStruct<Vec2>();
                geom.UvMapEntries[i] = new UvMapEntry(vertexId, pairs);
            }
        }

        // ---- Seam Stitch Data (63-byte fixed entries) ----
        if (r.Remaining >= 4)
        {
            geom.SeamStitchCount = (int)r.ReadU32();
            int seamBytes = geom.SeamStitchCount * 63;
            if (r.Remaining >= seamBytes && seamBytes > 0)
            {
                geom.SeamStitchData = backingMemory.Slice(r.Position, seamBytes);
                r.Skip(seamBytes);
            }
        }

        // ---- Bone Table ----
        if (r.Remaining >= 4)
        {
            int boneCount = (int)r.ReadU32();
            if (boneCount > 0 && r.Remaining >= boneCount * 4)
            {
                geom.BoneHashes = new uint[boneCount];
                for (int i = 0; i < boneCount; i++)
                    geom.BoneHashes[i] = r.ReadU32();
            }
        }

        // ---- TGI References ----
        if (r.Remaining >= 4)
        {
            int tgiCount = (int)r.ReadU32();
            if (tgiCount > 0 && r.Remaining >= tgiCount * 16)
            {
                geom.TgiReferences = new ResourceKey[tgiCount];
                for (int i = 0; i < tgiCount; i++)
                    geom.TgiReferences[i] = ResourceKey.ReadTGI(ref r);
            }
        }

        return geom;
    }

    private static void SkipRcolHeader(ref SpanReader r)
    {
        _ = r.ReadU32(); // rcolVersion (typically 3)
        _ = r.ReadU32(); // publicKeyCount
        _ = r.ReadU32(); // externalKeyCount
        uint externalCount = r.ReadU32();
        uint internalCount = r.ReadU32();

        // ITG records: 16 bytes each (instance u64, type u32, group u32)
        r.Skip((int)(internalCount + externalCount) * 16);

        // Chunk locations: 8 bytes each (position u32, size u32)
        r.Skip((int)internalCount * 8);
    }

    private static void ReadMtnfBlock(ref SpanReader r, GeomResource geom, ReadOnlyMemory<byte> backing)
    {
        uint totalSize = r.ReadU32();
        r.Skip(4); // "MTNF" magic
        _ = r.ReadU32(); // version
        uint dataSize = r.ReadU32();
        int paramCount = (int)r.ReadU32();

        geom.ShaderParams = new ShaderParam[paramCount];
        for (int i = 0; i < paramCount; i++)
        {
            uint nameHash = r.ReadU32();
            var dataType = (ShaderDataType)r.ReadU16();
            ushort dataFlags = r.ReadU16();
            uint elementCount = r.ReadU32();
            uint dataOffset = r.ReadU32();
            geom.ShaderParams[i] = new ShaderParam(nameHash, dataType, dataFlags, elementCount, dataOffset);
        }

        geom.ShaderParamData = backing.Slice(r.Position, (int)dataSize);
        r.Skip((int)dataSize);
    }
}
