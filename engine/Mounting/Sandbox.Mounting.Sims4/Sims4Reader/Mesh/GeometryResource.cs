using Sims4Reader.Material;

namespace Sims4Reader.Mesh;

/// <summary>
/// Usage types for GEOM vertex format entries (distinct from VRTF's ElementUsage).
/// </summary>
public enum GeomUsageType : uint
{
    Position = 0x01,
    Normal = 0x02,
    UV = 0x03,
    BoneAssignment = 0x04,
    Weights = 0x05,
    TangentNormal = 0x06,
    Color = 0x07,
    VertexID = 0x0A,
}

/// <summary>
/// A GEOM vertex format entry describing a single vertex attribute.
/// </summary>
public struct GeomVertexFormatEntry
{
    public GeomUsageType Usage;
    public uint DataType;
    public byte ElementSize;
}

/// <summary>
/// Sub-mesh skin controller data stored within a GEOM (version 0x0C+).
/// Contains a reference hash and a list of (float, float) pairs.
/// </summary>
public class GeomUnknownThing
{
    public uint Unknown1 { get; set; }
    public List<(float X, float Y)> Unknown2 { get; set; } = new();

    public void Parse(BinaryReader reader)
    {
        Unknown1 = reader.ReadUInt32();
        int count = reader.ReadInt32();
        Unknown2 = new List<(float, float)>(count);
        for (int i = 0; i < count; i++)
        {
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            Unknown2.Add((x, y));
        }
    }
}

/// <summary>
/// Extended sub-mesh data stored within a GEOM (version 0x0C+).
/// Contains transform/bounds data (53 bytes).
/// </summary>
public class GeomUnknownThing2
{
    public uint Unknown1 { get; set; }
    public ushort Unknown2 { get; set; }
    public ushort Unknown3 { get; set; }
    public ushort Unknown4 { get; set; }
    public float Unknown5 { get; set; }
    public float Unknown6 { get; set; }
    public float Unknown7 { get; set; }
    public float Unknown8 { get; set; }
    public float Unknown9 { get; set; }
    public float Unknown10 { get; set; }
    public float Unknown11 { get; set; }
    public float Unknown12 { get; set; }
    public float Unknown13 { get; set; }
    public float Unknown14 { get; set; }
    public float Unknown15 { get; set; }
    public float Unknown16 { get; set; }
    public float Unknown17 { get; set; }
    public byte Unknown18 { get; set; }

    public void Parse(BinaryReader reader)
    {
        Unknown1 = reader.ReadUInt32();
        Unknown2 = reader.ReadUInt16();
        Unknown3 = reader.ReadUInt16();
        Unknown4 = reader.ReadUInt16();
        Unknown5 = reader.ReadSingle();
        Unknown6 = reader.ReadSingle();
        Unknown7 = reader.ReadSingle();
        Unknown8 = reader.ReadSingle();
        Unknown9 = reader.ReadSingle();
        Unknown10 = reader.ReadSingle();
        Unknown11 = reader.ReadSingle();
        Unknown12 = reader.ReadSingle();
        Unknown13 = reader.ReadSingle();
        Unknown14 = reader.ReadSingle();
        Unknown15 = reader.ReadSingle();
        Unknown16 = reader.ReadSingle();
        Unknown17 = reader.ReadSingle();
        Unknown18 = reader.ReadByte();
    }
}

/// <summary>
/// GEOM (Geometry) resource - a standalone resource (not inside an RCOL container)
/// containing all mesh data in a single file: vertex format, vertex data, indices,
/// material info, bone hashes, and TGI references.
///
/// On-disk layout:
///   "GEOM" tag (4 bytes)
///   Version (4 bytes) - 0x05, 0x0C, 0x0D, 0x0E, 0x0F, etc.
///   TGI offset (4 bytes) - offset from current position to TGI block
///   TGI size (4 bytes)
///   Shader hash (4 bytes) - ShaderType enum
///   [if shader != 0] MTNF material block size (4 bytes) + MTNF data
///   MergeGroup (4 bytes)
///   SortOrder (4 bytes)
///   VertexCount (4 bytes)
///   Vertex format entries (count-prefixed)
///   Vertex data (VertexCount * stride bytes)
///   Face data (count-prefixed, 2 bytes per index)
///   [version 0x05] SkinIndex (4 bytes)
///   [version 0x0C] UnknownThings + UnknownThings2 (count-prefixed lists)
///   BoneHashes (count-prefixed uint list)
///   TGI block (at tgiOffset)
/// </summary>
public class GeometryResource : IResource
{
    private const uint GeomTag = 0x4D4F4547; // "GEOM"

    public uint Version { get; set; }
    public ShaderType Shader { get; set; }
    public MaterialInfo? Material { get; set; }
    public uint MergeGroup { get; set; }
    public uint SortOrder { get; set; }
    public List<GeomVertexFormatEntry> VertexFormats { get; set; } = new();

    /// <summary>Raw vertex data bytes for all vertices.</summary>
    public byte[] RawVertexData { get; set; } = Array.Empty<byte>();

    /// <summary>Number of vertices in the raw vertex data.</summary>
    public int VertexCount { get; set; }

    /// <summary>Raw face indices as ushort triplets.</summary>
    public ushort[] RawIndices { get; set; } = Array.Empty<ushort>();

    /// <summary>Skin controller index (version 0x05 only).</summary>
    public int SkinIndex { get; set; }

    /// <summary>Sub-mesh data (version 0x0C+).</summary>
    public List<GeomUnknownThing> UnknownThings { get; set; } = new();

    /// <summary>Extended sub-mesh data (version 0x0C+).</summary>
    public List<GeomUnknownThing2> UnknownThings2 { get; set; } = new();

    /// <summary>Bone name hashes used for skinning.</summary>
    public List<uint> BoneHashes { get; set; } = new();

    /// <summary>TGI reference list at the end of the resource (texture/material references).</summary>
    public ResourceKey[] TgiReferences { get; set; } = Array.Empty<ResourceKey>();

    public void Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length == 0)
            return;

        using var ms = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(ms);

        uint tag = reader.ReadUInt32();
        if (tag != GeomTag)
            throw new InvalidDataException($"Invalid GEOM tag: 0x{tag:X8}; expected 0x{GeomTag:X8}");

        Version = reader.ReadUInt32();
        if (Version < 0x00000005)
            throw new InvalidDataException($"Unsupported GEOM version: 0x{Version:X8} (minimum supported: 0x05)");

        // TGI offset is relative to the position after reading the offset field
        long tgiOffsetBase = ms.Position;
        uint tgiOffset = reader.ReadUInt32();
        long tgiPosition = tgiOffsetBase + tgiOffset;
        uint tgiSize = reader.ReadUInt32();

        // Shader
        Shader = (ShaderType)reader.ReadUInt32();
        if (Shader != 0)
        {
            uint mtnfSize = reader.ReadUInt32();
            long mtnfStart = ms.Position;
            Material = new MaterialInfo();
            Material.Parse(reader, isGeom: true);
            // Ensure we consumed exactly mtnfSize bytes
            ms.Position = mtnfStart + mtnfSize;
        }
        else
        {
            Material = null;
        }

        MergeGroup = reader.ReadUInt32();
        SortOrder = reader.ReadUInt32();

        // Vertex count
        VertexCount = reader.ReadInt32();

        // Vertex format entries
        int formatCount = reader.ReadInt32();
        VertexFormats = new List<GeomVertexFormatEntry>(formatCount);
        for (int i = 0; i < formatCount; i++)
        {
            var entry = new GeomVertexFormatEntry
            {
                Usage = (GeomUsageType)reader.ReadUInt32(),
                DataType = reader.ReadUInt32(),
                ElementSize = reader.ReadByte(),
            };
            VertexFormats.Add(entry);
        }

        // Compute stride from format entries
        int stride = 0;
        foreach (var fmt in VertexFormats)
            stride += fmt.ElementSize;

        // Raw vertex data
        int vertexDataSize = VertexCount * stride;
        RawVertexData = reader.ReadBytes(vertexDataSize);

        // Face data
        int numFacePointSizes = reader.ReadInt32();
        // Typically 1
        byte facePointSize = reader.ReadByte();
        // facePointSize should be 2 (16-bit indices)

        int numFaceIndices = reader.ReadInt32();
        RawIndices = new ushort[numFaceIndices];
        for (int i = 0; i < numFaceIndices; i++)
            RawIndices[i] = reader.ReadUInt16();

        // Version-specific trailing data (sub-mesh metadata + bone hashes).
        // The vertex/index data above is the critical payload; everything below
        // is best-effort because the sub-mesh structures change across versions.
        try
        {
            if (Version == 0x00000005)
            {
                SkinIndex = reader.ReadInt32();
            }
            else if (Version >= 0x0000000C)
            {
                // Versions 0x0C and later use compact sub-mesh data
                int count1 = reader.ReadInt32();
                if (count1 >= 0 && count1 < 10000)
                {
                    UnknownThings = new List<GeomUnknownThing>(count1);
                    for (int i = 0; i < count1; i++)
                    {
                        var ut = new GeomUnknownThing();
                        ut.Parse(reader);
                        UnknownThings.Add(ut);
                    }
                }

                int count2 = reader.ReadInt32();
                if (count2 >= 0 && count2 < 10000)
                {
                    UnknownThings2 = new List<GeomUnknownThing2>(count2);
                    for (int i = 0; i < count2; i++)
                    {
                        var ut2 = new GeomUnknownThing2();
                        ut2.Parse(reader);
                        UnknownThings2.Add(ut2);
                    }
                }
            }
            else
            {
                // Versions 0x06–0x0B: assume same layout as 0x05 (skin index only)
                SkinIndex = reader.ReadInt32();
            }

            // Bone hashes
            int boneCount = reader.ReadInt32();
            if (boneCount >= 0 && boneCount < 100000)
            {
                BoneHashes = new List<uint>(boneCount);
                for (int i = 0; i < boneCount; i++)
                    BoneHashes.Add(reader.ReadUInt32());
            }
        }
        catch (EndOfStreamException)
        {
            // Version-specific trailing data format mismatch —
            // mesh vertices and indices are still valid for rendering.
        }

        // TGI block at the end of the resource (may be absent in RCOL-embedded GEOM)
        if (tgiPosition >= 0 && tgiPosition < ms.Length && tgiSize > 0)
        {
            ms.Position = tgiPosition;
            ParseTgiBlock(reader, tgiSize);
        }
    }

    private void ParseTgiBlock(BinaryReader reader, uint tgiSize)
    {
        // TGI block format: count(4) + entries (each: Instance(8) + Type(4) + Group(4) = 16 bytes)
        if (tgiSize < 4)
        {
            TgiReferences = Array.Empty<ResourceKey>();
            return;
        }

        int count = reader.ReadInt32();
        int maxPossible = (int)((tgiSize - 4) / 16);
        if (count < 0 || count > maxPossible)
        {
            TgiReferences = Array.Empty<ResourceKey>();
            return;
        }
        TgiReferences = new ResourceKey[count];
        for (int i = 0; i < count; i++)
        {
            // ITG order: Instance(8), Type(4), Group(4)
            ulong instance = reader.ReadUInt64();
            uint type = reader.ReadUInt32();
            uint group = reader.ReadUInt32();
            TgiReferences[i] = new ResourceKey((ResourceType)type, group, instance);
        }
    }

    /// <summary>
    /// Decode all vertices from the raw vertex data using the embedded format entries.
    /// </summary>
    public Vertex[] GetVertices()
    {
        int stride = 0;
        foreach (var fmt in VertexFormats)
            stride += fmt.ElementSize;

        if (stride == 0 || VertexCount == 0)
            return Array.Empty<Vertex>();

        var vertices = new Vertex[VertexCount];

        for (int i = 0; i < VertexCount; i++)
        {
            int baseOffset = i * stride;
            var v = new Vertex();
            int uvIndex = 0;

            // Count UV channels to pre-allocate
            int uvCount = 0;
            foreach (var fmt in VertexFormats)
                if (fmt.Usage == GeomUsageType.UV) uvCount++;

            var uvList = new float[uvCount][];

            int elementOffset = 0;
            uvIndex = 0;
            foreach (var fmt in VertexFormats)
            {
                int readPos = baseOffset + elementOffset;

                switch (fmt.Usage)
                {
                    case GeomUsageType.Position:
                        v.Position = ReadFloats(readPos, 3);
                        break;
                    case GeomUsageType.Normal:
                        v.Normal = ReadFloats(readPos, 3);
                        break;
                    case GeomUsageType.UV:
                        uvList[uvIndex++] = ReadFloats(readPos, 2);
                        break;
                    case GeomUsageType.BoneAssignment:
                        v.BlendIndices = ReadBytes(readPos, fmt.ElementSize);
                        break;
                    case GeomUsageType.Weights:
                        if (Version <= 0x0000000B)
                        {
                            // Version 0x05–0x0B: 4 floats (16 bytes)
                            v.BlendWeights = ReadFloats(readPos, 4);
                        }
                        else
                        {
                            // Version 0x0C+: 4 bytes normalized to floats
                            v.BlendWeights = new float[4];
                            for (int j = 0; j < 4; j++)
                                v.BlendWeights[j] = RawVertexData[readPos + j] / 255f;
                        }
                        break;
                    case GeomUsageType.TangentNormal:
                        v.Tangent = ReadFloats(readPos, 3);
                        break;
                    case GeomUsageType.Color:
                        // 4 bytes packed as ARGB
                        if (fmt.ElementSize >= 4)
                        {
                            v.Color = BitConverter.ToUInt32(RawVertexData, readPos);
                            v.HasColor = true;
                        }
                        break;
                    case GeomUsageType.VertexID:
                        // Skip, not useful for rendering
                        break;
                }

                elementOffset += fmt.ElementSize;
            }

            v.UV = uvList;
            vertices[i] = v;
        }

        return vertices;
    }

    /// <summary>
    /// Get the face indices as a flat int array (each 3 consecutive values form a triangle).
    /// </summary>
    public int[] GetIndices()
    {
        var result = new int[RawIndices.Length];
        for (int i = 0; i < RawIndices.Length; i++)
            result[i] = RawIndices[i];
        return result;
    }

    private float[] ReadFloats(int offset, int count)
    {
        var result = new float[count];
        for (int i = 0; i < count; i++)
            result[i] = BitConverter.ToSingle(RawVertexData, offset + i * 4);
        return result;
    }

    private byte[] ReadBytes(int offset, int count)
    {
        var result = new byte[count];
        System.Array.Copy(RawVertexData, offset, result, 0, count);
        return result;
    }
}
