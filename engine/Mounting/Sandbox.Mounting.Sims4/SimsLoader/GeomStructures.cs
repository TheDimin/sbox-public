// ============================================================================
// GEOM Structures — Vertex formats, element definitions, mesh data.
// All structs are blittable where possible for zero-copy span casts.
// ============================================================================

using System;
using System.Runtime.InteropServices;

namespace Sims4.Dbpf.Resources;

// ---- Vertex Element Metadata ------------------------------------------------

public enum VertexElementType : uint
{
    Position    = 1,   // 3× float (12 bytes)
    Normal      = 2,   // 3× float (12 bytes)
    UV          = 3,   // 2× float (8 bytes)
    BoneAssign  = 4,   // 4× u8 bone indices (4 bytes)
    BoneWeights = 5,   // 4× u8 normalized weights (4 bytes)
    Tangent     = 6,   // 3× float (12 bytes)
    TagValue    = 7,   // 4× u8 (4 bytes)
    VertexID    = 10,  // 1× u32 (4 bytes)
}

public readonly struct ElementDefinition
{
    public readonly VertexElementType Type;
    public readonly byte SubType;
    public readonly uint ByteSize; // NOTE: stored big-endian on disk; reader swaps it

    public ElementDefinition(VertexElementType type, byte subType, uint byteSize)
    {
        Type = type;
        SubType = subType;
        ByteSize = byteSize;
    }
}

// ---- Blittable vertex components (for zero-copy span casts) -----------------

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Vec3
{
    public readonly float X, Y, Z;
    public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Vec2
{
    public readonly float U, V;
    public override string ToString() => $"({U:F3}, {V:F3})";
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BoneAssignment
{
    public readonly byte Bone0, Bone1, Bone2, Bone3;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BoneWeights
{
    public readonly byte W0, W1, W2, W3;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct TagValue
{
    public readonly byte A, B, C, D;
}

/// <summary>
/// Standard 64-byte vertex layout (the most common GEOM layout).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 64)]
public readonly struct Vertex64
{
    public readonly Vec3 Position;
    public readonly Vec3 Normal;
    public readonly Vec2 UV0;
    public readonly Vec2 UV1;
    public readonly TagValue Tag;
    public readonly BoneAssignment Bones;
    public readonly BoneWeights Weights;
    public readonly Vec3 Tangent;
}

/// <summary>A triangle represented as three u16 indices.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Triangle
{
    public readonly ushort A, B, C;
    public override string ToString() => $"({A}, {B}, {C})";
}

// ---- MTNF Shader Parameter Block -------------------------------------------

public enum ShaderDataType : ushort
{
    Float   = 1,
    Integer = 2,
    Texture = 4,
}

public readonly struct ShaderParam
{
    public readonly uint NameHash;
    public readonly ShaderDataType DataType;
    public readonly ushort DataFlags;
    public readonly uint ElementCount;
    public readonly uint DataOffset;

    public ShaderParam(uint nameHash, ShaderDataType dataType, ushort dataFlags,
                       uint elementCount, uint dataOffset)
    {
        NameHash = nameHash;
        DataType = dataType;
        DataFlags = dataFlags;
        ElementCount = elementCount;
        DataOffset = dataOffset;
    }
}

// ---- Face Group (index buffer) ----------------------------------------------

public readonly struct FaceGroup
{
    /// <summary>Bytes per face index (typically 2 for u16).</summary>
    public readonly int BytesPerIndex;

    /// <summary>Total index count.</summary>
    public readonly int IndexCount;

    /// <summary>Raw index data bytes. Interpret based on BytesPerIndex.</summary>
    public readonly ReadOnlyMemory<byte> IndexData;

    public FaceGroup(int bytesPerIndex, int indexCount, ReadOnlyMemory<byte> indexData)
    {
        BytesPerIndex = bytesPerIndex;
        IndexCount = indexCount;
        IndexData = indexData;
    }

    /// <summary>Number of triangles (IndexCount / 3).</summary>
    public int TriangleCount => IndexCount / 3;
}

// ---- UV Map (variable-length) -----------------------------------------------

public readonly struct UvMapEntry
{
    public readonly uint VertexId;
    public readonly Vec2[] Pairs; // This one allocates — UV maps are variable-length

    public UvMapEntry(uint vertexId, Vec2[] pairs)
    {
        VertexId = vertexId;
        Pairs = pairs;
    }
}

// ---- Seam Stitch (63-byte fixed entries) ------------------------------------

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 63)]
public readonly struct SeamStitchEntry
{
    public readonly uint VertexRef;
    public readonly ushort FaceRef0, FaceRef1, FaceRef2;
    // 52 bytes of interpolation data + 1 byte end marker
    // Not individually named — access via raw span if needed
}

// ---- Parsed GEOM resource ---------------------------------------------------

/// <summary>
/// Complete parsed GEOM mesh resource. The vertex and index data can be
/// accessed as zero-copy spans when the backing data is still alive.
/// </summary>
public sealed class GeomResource
{
    public uint Version;
    public uint EmbeddedShaderId;

    // MTNF block (if present)
    public ShaderParam[] ShaderParams;
    public ReadOnlyMemory<byte> ShaderParamData;

    // Mesh properties
    public uint MergeGroup;
    public uint SortOrder;

    // Vertex element layout
    public ElementDefinition[] Elements = Array.Empty<ElementDefinition>();
    public int VertexStride;

    // Vertex data — raw bytes; cast to Vertex64 if stride == 64
    public int VertexCount;
    public ReadOnlyMemory<byte> VertexData;

    // Face groups
    public FaceGroup[] FaceGroups = Array.Empty<FaceGroup>();

    // UV maps
    public UvMapEntry[] UvMapEntries = Array.Empty<UvMapEntry>();

    // Seam stitch
    public int SeamStitchCount;
    public ReadOnlyMemory<byte> SeamStitchData;

    // Bone table
    public uint[] BoneHashes = Array.Empty<uint>();

    // TGI references at end
    public Structures.ResourceKey[] TgiReferences = Array.Empty<Structures.ResourceKey>();

    /// <summary>
    /// If the vertex stride is exactly 64 bytes (the common 8-element layout),
    /// returns the vertex buffer as a span of <see cref="Vertex64"/>.
    /// </summary>
    public ReadOnlySpan<Vertex64> GetVertices64()
    {
        if (VertexStride != 64)
            throw new InvalidOperationException(
                $"Vertex stride is {VertexStride}, not 64. Use VertexData with element definitions.");
        return MemoryMarshal.Cast<byte, Vertex64>(VertexData.Span);
    }
}
