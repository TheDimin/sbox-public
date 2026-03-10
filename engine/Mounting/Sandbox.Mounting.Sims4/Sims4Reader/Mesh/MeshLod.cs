using Sims4Reader.Rcol;

namespace Sims4Reader.Mesh;

/// <summary>
/// Primitive type for mesh rendering.
/// </summary>
public enum ModelPrimitiveType : byte
{
    PointList = 0,
    LineList = 1,
    LineStrip = 2,
    TriangleList = 3,
    TriangleFan = 4,
    TriangleStrip = 5,
    RectList = 6,
    QuadList = 7,
    DisplayList = 8,
}

/// <summary>
/// Mesh rendering flags.
/// </summary>
[Flags]
public enum MeshFlags : uint
{
    None = 0x00000000,
    BasinInterior = 0x00000001,
    HDExteriorLit = 0x00000002,
    PortalSide = 0x00000004,
    DropShadow = 0x00000008,
    ShadowCaster = 0x00000010,
    Foundation = 0x00000020,
    Pickable = 0x00000040,
}

/// <summary>
/// A geometry state within a mesh LOD, defining a sub-range of vertices/indices.
/// </summary>
public class GeometryState
{
    public uint Name { get; set; }
    public int StartIndex { get; set; }
    public int MinVertexIndex { get; set; }
    public int VertexCount { get; set; }
    public int PrimitiveCount { get; set; }

    public void Parse(BinaryReader reader)
    {
        Name = reader.ReadUInt32();
        StartIndex = reader.ReadInt32();
        MinVertexIndex = reader.ReadInt32();
        VertexCount = reader.ReadInt32();
        PrimitiveCount = reader.ReadInt32();
    }
}

/// <summary>
/// A single mesh entry within an MLOD chunk.
/// References vertex buffer, index buffer, material, skin controller, and vertex format
/// via chunk reference indices.
/// </summary>
public class LodMesh
{
    public uint Name { get; set; }
    public int MaterialIndex { get; set; }
    public int VertexFormatIndex { get; set; }
    public int VertexBufferIndex { get; set; }
    public int IndexBufferIndex { get; set; }
    public ModelPrimitiveType PrimitiveType { get; set; }
    public MeshFlags Flags { get; set; }
    public uint StreamOffset { get; set; }
    public int StartVertex { get; set; }
    public int StartIndex { get; set; }
    public int MinVertexIndex { get; set; }
    public int VertexCount { get; set; }
    public int PrimitiveCount { get; set; }

    /// <summary>Bounding box: min XYZ.</summary>
    public float[] BoundsMin { get; set; } = new float[3];
    /// <summary>Bounding box: max XYZ.</summary>
    public float[] BoundsMax { get; set; } = new float[3];

    public int SkinControllerIndex { get; set; }
    public int ScaleOffsetIndex { get; set; }
    public List<uint> JointReferences { get; set; } = new();
    public List<GeometryState> GeometryStates { get; set; } = new();

    /// <summary>Present when MLOD version > 0x201.</summary>
    public uint ParentName { get; set; }
    /// <summary>Mirror plane XYZW, present when MLOD version > 0x201.</summary>
    public float[] MirrorPlane { get; set; } = new float[4];
    /// <summary>Present when MLOD version > 0x203.</summary>
    public uint Unknown1 { get; set; }

    public void Parse(BinaryReader reader, uint mlodVersion)
    {
        uint entrySize = reader.ReadUInt32();

        Name = reader.ReadUInt32();

        // ChunkReferences are stored as int32 indices
        MaterialIndex = reader.ReadInt32();
        VertexFormatIndex = reader.ReadInt32();
        VertexBufferIndex = reader.ReadInt32();
        IndexBufferIndex = reader.ReadInt32();

        // PrimitiveType is in low byte, Flags in upper bytes
        uint val = reader.ReadUInt32();
        PrimitiveType = (ModelPrimitiveType)(val & 0xFF);
        Flags = (MeshFlags)(val >> 8);

        StreamOffset = reader.ReadUInt32();
        StartVertex = reader.ReadInt32();
        StartIndex = reader.ReadInt32();
        MinVertexIndex = reader.ReadInt32();
        VertexCount = reader.ReadInt32();
        PrimitiveCount = reader.ReadInt32();

        // BoundingBox: min vertex (3 floats) + max vertex (3 floats)
        BoundsMin = new float[3];
        BoundsMax = new float[3];
        for (int i = 0; i < 3; i++) BoundsMin[i] = reader.ReadSingle();
        for (int i = 0; i < 3; i++) BoundsMax[i] = reader.ReadSingle();

        SkinControllerIndex = reader.ReadInt32();

        // Joint references: count-prefixed uint list
        int jointCount = reader.ReadInt32();
        JointReferences = new List<uint>(jointCount);
        for (int i = 0; i < jointCount; i++)
            JointReferences.Add(reader.ReadUInt32());

        ScaleOffsetIndex = reader.ReadInt32();

        // Geometry states: count-prefixed list
        int geoCount = reader.ReadInt32();
        GeometryStates = new List<GeometryState>(geoCount);
        for (int i = 0; i < geoCount; i++)
        {
            var gs = new GeometryState();
            gs.Parse(reader);
            GeometryStates.Add(gs);
        }

        if (mlodVersion > 0x00000201)
        {
            ParentName = reader.ReadUInt32();
            MirrorPlane = new float[4];
            for (int i = 0; i < 4; i++) MirrorPlane[i] = reader.ReadSingle();
        }

        if (mlodVersion > 0x00000203)
        {
            Unknown1 = reader.ReadUInt32();
        }
    }
}

/// <summary>
/// MLOD RCOL chunk - Level of Detail mesh container with references to
/// vertex/index buffers and materials via chunk indices.
/// </summary>
public class MeshLod : RcolChunk
{
    public uint Version { get; set; }
    public List<LodMesh> Meshes { get; set; } = new();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        Tag = reader.ReadUInt32();
        Version = reader.ReadUInt32();

        int meshCount = reader.ReadInt32();
        Meshes = new List<LodMesh>(meshCount);
        for (int i = 0; i < meshCount; i++)
        {
            var mesh = new LodMesh();
            mesh.Parse(reader, Version);
            Meshes.Add(mesh);
        }
    }
}
