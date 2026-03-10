namespace Sims4Reader.Rcol;

/// <summary>
/// FTPT (Footprint) RCOL chunk. Defines footprint and slot placement data for objects,
/// including polygon areas with flags, intersection/surface types, and bounding boxes.
///
/// On-disk layout:
///   Tag:       4 bytes ("FTPT" = 0x54505446)
///   Version:   4 bytes (uint)
///   Instance:  8 bytes (ulong)
///   Type:      4 bytes (uint)
///   Group:     4 bytes (uint)
///
///   If Type != 0:
///     MinHeightOverrides list (byte count + entries)
///     MaxHeightOverrides list (byte count + entries)
///   If Type == 0:
///     FootprintAreas list (byte count + entries)
///     SlotAreas list (byte count + entries)
///     MaxHeight: 4 bytes (float)
///     MinHeight: 4 bytes (float)
/// </summary>
public class FootprintChunk : RcolChunk
{
    private const uint FtptTag = 0x46545054; // "FTPT" in little-endian

    public uint Version { get; set; }

    /// <summary>Template resource key components.</summary>
    public ulong TemplateInstance { get; set; }
    public uint TemplateType { get; set; }
    public uint TemplateGroup { get; set; }

    /// <summary>
    /// Minimum height overrides per polygon. Only populated when TemplateType != 0.
    /// </summary>
    public List<PolygonHeightOverride> MinHeightOverrides { get; set; } = new();

    /// <summary>
    /// Maximum height overrides per polygon. Only populated when TemplateType != 0.
    /// </summary>
    public List<PolygonHeightOverride> MaxHeightOverrides { get; set; } = new();

    /// <summary>
    /// Footprint polygon areas. Only populated when TemplateType == 0.
    /// </summary>
    public List<FootprintArea> FootprintAreas { get; set; } = new();

    /// <summary>
    /// Slot polygon areas. Only populated when TemplateType == 0.
    /// </summary>
    public List<FootprintArea> SlotAreas { get; set; } = new();

    /// <summary>Only set when TemplateType == 0.</summary>
    public float MaxHeight { get; set; }

    /// <summary>Only set when TemplateType == 0.</summary>
    public float MinHeight { get; set; }

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        uint tag = reader.ReadUInt32();
        if (tag != FtptTag)
            throw new InvalidDataException(
                $"Invalid FTPT tag: 0x{tag:X8} at 0x{reader.BaseStream.Position:X8}; expected 'FTPT'");

        Tag = tag;
        Version = reader.ReadUInt32();
        TemplateInstance = reader.ReadUInt64();
        TemplateType = reader.ReadUInt32();
        TemplateGroup = reader.ReadUInt32();

        if (TemplateType != 0)
        {
            // Height override mode
            MinHeightOverrides = ReadHeightOverrideList(reader);
            MaxHeightOverrides = ReadHeightOverrideList(reader);
            FootprintAreas = new List<FootprintArea>();
            SlotAreas = new List<FootprintArea>();
        }
        else
        {
            // Area mode
            MinHeightOverrides = new List<PolygonHeightOverride>();
            MaxHeightOverrides = new List<PolygonHeightOverride>();
            FootprintAreas = ReadAreaList(reader);
            SlotAreas = ReadAreaList(reader);
            MaxHeight = reader.ReadSingle();
            MinHeight = reader.ReadSingle();
        }
    }

    private static List<PolygonHeightOverride> ReadHeightOverrideList(BinaryReader reader)
    {
        byte count = reader.ReadByte();
        var list = new List<PolygonHeightOverride>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(new PolygonHeightOverride
            {
                NameHash = reader.ReadUInt32(),
                Height = reader.ReadSingle(),
            });
        }
        return list;
    }

    private static List<FootprintArea> ReadAreaList(BinaryReader reader)
    {
        byte count = reader.ReadByte();
        var list = new List<FootprintArea>(count);
        for (int i = 0; i < count; i++)
        {
            var area = new FootprintArea();
            area.Parse(reader);
            list.Add(area);
        }
        return list;
    }
}

/// <summary>
/// A height override entry associating a name hash with a height value.
/// </summary>
public class PolygonHeightOverride
{
    public uint NameHash { get; set; }
    public float Height { get; set; }
}

/// <summary>
/// A polygon area definition within a footprint, including placement flags,
/// intersection rules, surface types, and a 3D bounding box.
///
/// On-disk layout:
///   Name:                    4 bytes (uint hash)
///   Priority:                1 byte
///   AreaTypeFlags:           4 bytes (uint, FootprintPolyFlags)
///   PointCount:              1 byte
///   Points[PointCount]:      each 8 bytes (float X, float Z)
///   IntersectionObjectType:  4 bytes (uint, IntersectionFlags)
///   AllowIntersectionTypes:  4 bytes (uint, IntersectionFlags)
///   SurfaceTypeFlags:        4 bytes (uint, SurfaceTypeFlags)
///   SurfaceAttributeFlags:   4 bytes (uint, SurfaceAttribute)
///   DeprecatedLevelOffset:   1 byte
///   BoundingBox3D:           24 bytes (min_x, min_z, max_x, max_z, min_y, max_y -- note non-standard order)
/// </summary>
public class FootprintArea
{
    public uint Name { get; set; }
    public byte Priority { get; set; }
    public FootprintPolyFlags AreaTypeFlags { get; set; }
    public List<PolygonPoint> Points { get; set; } = new();
    public IntersectionFlags IntersectionObjectType { get; set; }
    public IntersectionFlags AllowIntersectionTypes { get; set; }
    public SurfaceTypeFlags SurfaceTypeFlags { get; set; }
    public SurfaceAttribute SurfaceAttributeFlags { get; set; }
    public byte DeprecatedLevelOffset { get; set; }

    // 3D bounding box
    public float BoundsMinX { get; set; }
    public float BoundsMinY { get; set; }
    public float BoundsMinZ { get; set; }
    public float BoundsMaxX { get; set; }
    public float BoundsMaxY { get; set; }
    public float BoundsMaxZ { get; set; }

    public void Parse(BinaryReader reader)
    {
        Name = reader.ReadUInt32();
        Priority = reader.ReadByte();
        AreaTypeFlags = (FootprintPolyFlags)reader.ReadUInt32();

        byte pointCount = reader.ReadByte();
        Points = new List<PolygonPoint>(pointCount);
        for (int i = 0; i < pointCount; i++)
        {
            Points.Add(new PolygonPoint
            {
                X = reader.ReadSingle(),
                Z = reader.ReadSingle(),
            });
        }

        IntersectionObjectType = (IntersectionFlags)reader.ReadUInt32();
        AllowIntersectionTypes = (IntersectionFlags)reader.ReadUInt32();
        SurfaceTypeFlags = (SurfaceTypeFlags)reader.ReadUInt32();
        SurfaceAttributeFlags = (SurfaceAttribute)reader.ReadUInt32();
        DeprecatedLevelOffset = reader.ReadByte();

        // Bounding box in non-standard order: minX, minZ, maxX, maxZ, minY, maxY
        BoundsMinX = reader.ReadSingle();
        BoundsMinZ = reader.ReadSingle();
        BoundsMaxX = reader.ReadSingle();
        BoundsMaxZ = reader.ReadSingle();
        BoundsMinY = reader.ReadSingle();
        BoundsMaxY = reader.ReadSingle();
    }
}

/// <summary>
/// A 2D polygon point (X, Z coordinates on the ground plane).
/// </summary>
public class PolygonPoint
{
    public float X { get; set; }
    public float Z { get; set; }
}

/// <summary>
/// Footprint polygon placement and behavior flags.
/// </summary>
[Flags]
public enum FootprintPolyFlags : uint
{
    None = 0x00,
    Placement = 0x01,
    Pathing = 0x02,
    Enabled = 0x04,
    Discouraged = 0x08,
    LandingStrip = 0x10,
    NoRaycast = 0x20,
    PlacementSlotted = 0x40,
    Encouraged = 0x80,
    TerrainCutout = 0x100,
}

/// <summary>
/// Flags specifying which object/structure types participate in intersection checks.
/// </summary>
[Flags]
public enum IntersectionFlags : uint
{
    None = 0,
    Walls = 1 << 1,
    Objects = 1 << 2,
    Sims = 1 << 3,
    Roofs = 1 << 4,
    Fences = 1 << 5,
    ModularStairs = 1 << 6,
    ObjectsOfSameType = 1 << 7,
    Columns = 1 << 8,
    ReservedSpace = 1 << 9,
    Foundations = 1 << 10,
    FenestrationNode = 1 << 11,
    Trim = 1 << 12,
}

/// <summary>
/// Flags indicating which surface types an area applies to.
/// </summary>
[Flags]
public enum SurfaceTypeFlags : uint
{
    Terrain = 1 << 0,
    Floor = 1 << 1,
    Pool = 1 << 2,
    Pond = 1 << 3,
    FencePost = 1 << 4,
    AnySurface = 1 << 5,
    Air = 1 << 6,
    Roof = 1 << 7,
}

/// <summary>
/// Surface attribute flags (inside/outside/slope).
/// </summary>
[Flags]
public enum SurfaceAttribute : uint
{
    Unknown00 = 0x00,
    Inside = 0x01,
    Outside = 0x02,
    Slope = 0x04,
    Unknown08 = 0x08,
}
