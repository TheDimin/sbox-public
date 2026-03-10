namespace Sims4Reader.Rcol;

/// <summary>
/// LITE (Light) RCOL chunk. Contains light source definitions and occluder geometry.
///
/// On-disk layout:
///   Tag:              4 bytes ("LITE" = 0x4554494C)
///   Version:          4 bytes (uint)
///   Unknown1:         4 bytes (uint)
///   LightSourceCount: 1 byte
///   OccluderCount:    1 byte
///   Unknown2:         2 bytes (ushort)
///   LightSources[LightSourceCount]
///   Occluders[OccluderCount]
/// </summary>
public class LightChunk : RcolChunk
{
    private const uint LiteTag = 0x4C495445; // "LITE" in little-endian

    public uint Version { get; set; }
    public uint Unknown1 { get; set; }
    public ushort Unknown2 { get; set; }
    public List<LightSource> LightSources { get; set; } = new();
    public List<Occluder> Occluders { get; set; } = new();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        uint tag = reader.ReadUInt32();
        if (tag != LiteTag)
            throw new InvalidDataException(
                $"Invalid LITE tag: 0x{tag:X8} at 0x{reader.BaseStream.Position:X8}; expected 'LITE'");

        Tag = tag;
        Version = reader.ReadUInt32();
        Unknown1 = reader.ReadUInt32();

        byte lightSourceCount = reader.ReadByte();
        byte occluderCount = reader.ReadByte();
        Unknown2 = reader.ReadUInt16();

        // Read light sources
        LightSources = new List<LightSource>(lightSourceCount);
        for (int i = 0; i < lightSourceCount; i++)
        {
            var ls = new LightSource();
            ls.Parse(reader);
            LightSources.Add(ls);
        }

        // Read occluders
        Occluders = new List<Occluder>(occluderCount);
        for (int i = 0; i < occluderCount; i++)
        {
            var occ = new Occluder();
            occ.Parse(reader);
            Occluders.Add(occ);
        }
    }
}

/// <summary>
/// Light source types supported by the LITE chunk.
/// </summary>
public enum LightSourceType : uint
{
    Unknown = 0x00,
    Ambient = 0x01,
    Directional = 0x02,
    Point = 0x03,
    Spot = 0x04,
    LampShade = 0x05,
    TubeLight = 0x06,
    SquareWindow = 0x07,
    CircularWindow = 0x08,
    SquareAreaLight = 0x09,
    DiscAreaLight = 0x0A,
    WorldLight = 0x0B,
}

/// <summary>
/// A light source definition with position, color, intensity, and type-specific parameters.
///
/// On-disk layout:
///   LightType:    4 bytes (uint, LightSourceType)
///   Transform:    12 bytes (3 floats: X, Y, Z position)
///   Color:        12 bytes (3 floats: R, G, B)
///   Intensity:    4 bytes (float)
///   Data:         96 bytes (24 floats, interpretation depends on LightType)
///
/// Total per light source: 128 bytes.
/// </summary>
public class LightSource
{
    public LightSourceType LightType { get; set; }

    // Transform (position)
    public float TransformX { get; set; }
    public float TransformY { get; set; }
    public float TransformZ { get; set; }

    // Color
    public float ColorR { get; set; }
    public float ColorG { get; set; }
    public float ColorB { get; set; }

    public float Intensity { get; set; }

    /// <summary>
    /// Raw 24-float data block. Interpretation depends on LightType.
    /// Use the typed accessor properties for known light types.
    /// </summary>
    public float[] Data { get; set; } = new float[24];

    public void Parse(BinaryReader reader)
    {
        LightType = (LightSourceType)reader.ReadUInt32();

        TransformX = reader.ReadSingle();
        TransformY = reader.ReadSingle();
        TransformZ = reader.ReadSingle();

        ColorR = reader.ReadSingle();
        ColorG = reader.ReadSingle();
        ColorB = reader.ReadSingle();

        Intensity = reader.ReadSingle();

        for (int i = 0; i < 24; i++)
            Data[i] = reader.ReadSingle();
    }

    // ----- Typed accessors for known light source types -----
    // The Data array is interpreted differently depending on LightType.
    // These properties provide named access to the relevant fields.

    // -- Spot (0x04) --
    // Data[0..2] = At direction (X, Y, Z)
    // Data[3]    = FalloffAngle
    // Data[4]    = BlurScale

    /// <summary>Spot/LampShade/TubeLight: "At" direction X. Data[0].</summary>
    public float AtX => Data[0];
    /// <summary>Spot/LampShade/TubeLight: "At" direction Y. Data[1].</summary>
    public float AtY => Data[1];
    /// <summary>Spot/LampShade/TubeLight: "At" direction Z. Data[2].</summary>
    public float AtZ => Data[2];

    /// <summary>Spot/LampShade: Falloff angle. Data[3].</summary>
    public float FalloffAngle => Data[3];

    /// <summary>Spot/TubeLight: Blur scale. Data[4].</summary>
    public float BlurScale => Data[4];

    // -- LampShade (0x05) --
    // Data[0..2] = At direction
    // Data[3]    = FalloffAngle
    // Data[4]    = ShadeLightRigMultiplier
    // Data[5]    = BottomAngle
    // Data[6..8] = ShadeColor (R, G, B)

    /// <summary>LampShade: Shade light rig multiplier. Data[4].</summary>
    public float ShadeLightRigMultiplier => Data[4];
    /// <summary>LampShade: Bottom angle. Data[5].</summary>
    public float BottomAngle => Data[5];
    /// <summary>LampShade: Shade color R. Data[6].</summary>
    public float ShadeColorR => Data[6];
    /// <summary>LampShade: Shade color G. Data[7].</summary>
    public float ShadeColorG => Data[7];
    /// <summary>LampShade: Shade color B. Data[8].</summary>
    public float ShadeColorB => Data[8];

    // -- TubeLight (0x06) --
    // Data[0..2] = At direction
    // Data[3]    = TubeLength
    // Data[4]    = BlurScale

    /// <summary>TubeLight: Tube length. Data[3].</summary>
    public float TubeLength => Data[3];

    // -- SquareWindow (0x07) / SquareAreaLight (0x09) --
    // Data[0..2] = At direction
    // Data[3..5] = Right direction (X, Y, Z)
    // Data[6]    = Width
    // Data[7]    = Height
    // Data[8]    = FalloffAngle
    // Data[9]    = WindowTopBottomAngle

    /// <summary>Window/AreaLight: Right direction X. Data[3].</summary>
    public float RightX => Data[3];
    /// <summary>Window/AreaLight: Right direction Y. Data[4].</summary>
    public float RightY => Data[4];
    /// <summary>Window/AreaLight: Right direction Z. Data[5].</summary>
    public float RightZ => Data[5];
    /// <summary>SquareWindow/SquareAreaLight: Width. Data[6].</summary>
    public float Width => Data[6];
    /// <summary>SquareWindow/SquareAreaLight: Height. Data[7].</summary>
    public float Height => Data[7];
    /// <summary>SquareWindow/SquareAreaLight: Falloff angle. Data[8].</summary>
    public float WindowFalloffAngle => Data[8];
    /// <summary>SquareWindow/SquareAreaLight: Window top/bottom angle. Data[9].</summary>
    public float WindowTopBottomAngle => Data[9];

    // -- CircularWindow (0x08) / DiscAreaLight (0x0A) --
    // Data[0..2] = At direction
    // Data[3..5] = Right direction (X, Y, Z)
    // Data[6]    = Radius

    /// <summary>CircularWindow/DiscAreaLight: Radius. Data[6].</summary>
    public float Radius => Data[6];
}

/// <summary>
/// Occluder types supported by the LITE chunk.
/// </summary>
public enum OccluderType : uint
{
    Disc = 0x00,
    Rectangle = 0x01,
}

/// <summary>
/// An occluder definition specifying a shape (disc or rectangle) with orientation axes.
///
/// On-disk layout:
///   OccluderType: 4 bytes (uint)
///   Origin:       12 bytes (3 floats: X, Y, Z)
///   Normal:       12 bytes (3 floats: X, Y, Z)
///   XAxis:        12 bytes (3 floats: X, Y, Z)
///   YAxis:        12 bytes (3 floats: X, Y, Z)
///   PairOffset:   4 bytes (float)
///
/// Total per occluder: 56 bytes.
/// </summary>
public class Occluder
{
    public OccluderType OccluderKind { get; set; }

    public float OriginX { get; set; }
    public float OriginY { get; set; }
    public float OriginZ { get; set; }

    public float NormalX { get; set; }
    public float NormalY { get; set; }
    public float NormalZ { get; set; }

    public float XAxisX { get; set; }
    public float XAxisY { get; set; }
    public float XAxisZ { get; set; }

    public float YAxisX { get; set; }
    public float YAxisY { get; set; }
    public float YAxisZ { get; set; }

    public float PairOffset { get; set; }

    public void Parse(BinaryReader reader)
    {
        OccluderKind = (OccluderType)reader.ReadUInt32();

        OriginX = reader.ReadSingle();
        OriginY = reader.ReadSingle();
        OriginZ = reader.ReadSingle();

        NormalX = reader.ReadSingle();
        NormalY = reader.ReadSingle();
        NormalZ = reader.ReadSingle();

        XAxisX = reader.ReadSingle();
        XAxisY = reader.ReadSingle();
        XAxisZ = reader.ReadSingle();

        YAxisX = reader.ReadSingle();
        YAxisY = reader.ReadSingle();
        YAxisZ = reader.ReadSingle();

        PairOffset = reader.ReadSingle();
    }
}
