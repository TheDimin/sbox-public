namespace Sims4Reader.Material;

/// <summary>
/// Known shader types used in MATD chunks.
/// </summary>
public enum ShaderType : uint
{
    None = 0x00000000,
    Subtractive = 0x0B272CC5,
    Instanced = 0x0CB82EB8,
    FullBright = 0x14FA335E,
    PreviewWallsAndFloors = 0x213D6300,
    ShadowMap = 0x21FE207D,
    GlassForRabbitHoles = 0x265FFAA1,
    ImpostorWater = 0x277CF8EB,
    Rug = 0x2A72B9A1,
    Trampoline = 0x3939E094,
    Foliage = 0x4549E22E,
    ParticleAnim = 0x460E93F4,
    SolidPhong = 0x47C6638C,
    GlassForObjects = 0x492ECA7C,
    Stairs = 0x4CE2F497,
    OutdoorProp = 0x4D26BEC0,
    GlassForFences = 0x52986C62,
    SimSkin = 0x548394B9,
    Additive = 0x5AF16731,
    SimGlass = 0x5EDA9CDE,
    Fence = 0x67107FE8,
    LotImposter = 0x68601DE3,
    Blueprint = 0x6864A45E,
    BasinWater = 0x6AAD2AD5,
    StandingWater = 0x70FDE012,
    BuildingWindow = 0x7B036C01,
    Roof = 0x7BD05F63,
    GlassForPortals = 0x81DD204D,
    GlassForObjectsTranslucent = 0x849CF021,
    SimHair = 0x84FD7152,
    Landmark = 0x8A60B969,
    RabbitHoleHighDetail = 0x8D346BBC,
    CASRoom = 0x94B9A835,
    SimEyelashes = 0x9D9DA161,
    Gemstones = 0xA063C1D0,
    Counters = 0xA4172F62,
    FlatMirror = 0xA68D9E29,
    Painting = 0xAA495821,
    RabbitHoleMediumDetail = 0xAEDE7105,
    Phong = 0xB9105A6D,
    Floors = 0xBC84D000,
    DropShadow = 0xC09C7582,
    SimEyes = 0xCF8A70B4,
    Plumbob = 0xDEF16564,
    SculptureIce = 0xE5D98507,
    PhongAlpha = 0xFC5FC212,
    ParticleJet = 0xFF5E6908,
}

/// <summary>
/// Shader parameter field type identifiers. Each value corresponds to a named
/// shader parameter and implies its data type (Float, Float2/3/4, Int, Texture, TextureKey).
/// </summary>
public enum ShaderFieldType : uint
{
    None = 0x00000000,

    // Float parameters
    AlignAcrossDirection = 0x01885886,
    DimmingCenterHeight = 0x01ADACE0,
    Transparency = 0x05D22FD3,
    BlendSourceMode = 0x0995E96C,
    SharpSpecControl = 0x11483F01,
    RotateSpeedRadsSec = 0x16BF7A44,
    AlignToDirection = 0x17B78AF6,
    DropShadowStrength = 0x1B1AB4D5,
    ContourSmoothing = 0x1E27DCCD,
    Reflectivity = 0x29BCDD1F,
    BlendOperation = 0x2D13B939,
    RotationSpeed = 0x32003AD4,
    DimmingRadius = 0x32DFA298,
    IsGenericBox = 0x347C9E07,
    IsSolidObject = 0x3BBF99CF,
    NormalMapScale = 0x3C45E334,
    NoAutomaticDaylightDimming = 0x3CB5FA70,
    FramesPerSecond = 0x406ADE00,
    BloomFactor = 0x4168508B,
    EmissiveBloomMultiplier = 0x490E6EB4,
    IsObject = 0x4C12ECE8,
    IsPartition = 0x5250023D,
    RippleSpeed = 0x52DEC070,
    UseLampColor = 0x56B220CD,
    TextureSpeedScale = 0x583DF357,
    NoiseMapScale = 0x5E86DEA1,
    AutoRainbow = 0x5F7800EA,
    DebouncePower = 0x656025DF,
    SpeedStretchFactor = 0x66479028,
    WindSpeed = 0x66E9B6BC,
    DaytimeOnly = 0x6BB389BC,
    FramesRandomStartFactor = 0x7211F24F,
    DeflectionThreshold = 0x7D621D61,
    LifetimeSeconds = 0x84212733,
    NormalBumpScale = 0x88C64AE2,
    DeformerOffset = 0x8BDF4746,
    EdgeDarkening = 0x8C27D8C9,
    OverrideFactor = 0x8E35CCC0,
    EmissiveLightMultiplier = 0x8EF71C85,
    SharpSpecThreshold = 0x903BE4D3,
    RugSort = 0x906997A9,
    Layer2Shift = 0x92692CB2,
    SpecStyle = 0x9554D40F,
    FadeDistance = 0x957210EA,
    BlendDestMode = 0x9BDECB37,
    LightingEnabled = 0xA15E4594,
    OverrideSpeed = 0xA3D6342E,
    VisibleOnlyAtNight = 0xAC5D0A82,
    UseDiffuseForAlphaTest = 0xB597FA7F,
    SparkleSpeed = 0xBA13921E,
    WindStrength = 0xBC4A2544,
    HaloBlur = 0xC3AD4F50,
    RefractionDistortionScale = 0xC3C472A1,
    DiffuseMapUVChannel = 0xC45A5F41,
    SpecularMapUVChannel = 0xCB053686,
    ParticleCount = 0xCC31B828,
    RippleDistanceScale = 0xCCB35B98,
    DivetScale = 0xCE8C8311,
    ForceAmount = 0xD4D51D02,
    AnimSpeed = 0xD600CB63,
    BackFaceDiffuseContribution = 0xD641A1B1,
    BounceAmountMeters = 0xD8542D8B,
    IsFloor = 0xD9C05335,
    IndexOfRefraction = 0xDAA9532D,
    BloomScale = 0xE29BA4AC,
    AlphaMaskThreshold = 0xE77A2B60,
    LightingDirectScale = 0xEF270EE4,
    AlwaysOn = 0xF019641D,
    Shininess = 0xF755F7FF,
    FresnelOffset = 0xFB66A8CB,
    BouncePower = 0xFBA6B898,
    ShadowAlphaTest = 0xFEB1F9CB,

    // Float2 parameters
    DiffuseUVScale = 0x2D4E507E,
    RippleHeights = 0x6A07D7E1,
    CutoutValidHeights = 0x6D43D7B7,
    UVTiling = 0x773CAB85,
    SizeScaleEnd = 0x891A3133,
    StretchRect = 0x8D38D12E,
    SizeScaleStart = 0x9A6C2EC8,
    WaterScrollSpeedLayer2 = 0xAFA11435,
    WaterScrollSpeedLayer1 = 0xAFA11436,
    NormalUVScale = 0xBA2D1AB9,
    DetailUVScale = 0xCD985A0B,
    SpecularUVScale = 0xF12E27C3,
    UVScrollSpeed = 0xF2EEA6EC,

    // Float3 parameters
    Ambient = 0x04A5DAA3,
    OverrideDirection = 0x0C12DED8,
    OverrideVelocity = 0x14677578,
    CounterMatrixRow1 = 0x1EF8655D,
    CounterMatrixRow2 = 0x1EF8655E,
    ForceDirection = 0x29881F55,
    Specular = 0x2CE11842,
    HaloLowColor = 0x2EB8E8D4,
    Emission = 0x3BD441A0,
    NormalMapUVSelector = 0x415368B4,
    UVScales = 0x420520E9,
    LightMapScale = 0x4F7DCB9B,
    Diffuse = 0x637DAA05,
    Reflective = 0x73C9923E,
    AmbientUVSelector = 0x797F8E81,
    HighlightColor = 0x90F8DCF0,
    DiffuseUVSelector = 0x91EEBAFF,
    Transparent = 0x988403F9,
    VertexColorScale = 0xA2FD73CA,
    SpecularUVSelector = 0xB63546AC,
    EmissionMapUVSelector = 0xBC823DDC,
    HaloHighColor = 0xD4043258,
    RootColor = 0xE90599F6,
    ForceVector = 0xEBA4727B,
    PositionTweak = 0xEF36D180,

    // Float4 parameters
    TimelineLength = 0x0081AE98,
    UVScale = 0x159BA53E,
    FrameData = 0x1E5B2324,
    AnimDir = 0x3F89C2EF,
    PosScale = 0x487648E5,
    Births = 0x568E0367,
    UVOffset = 0x57582869,
    PosOffset = 0x790EBF2C,

    // Int parameters
    AverageColor = 0x449A3A67,
    MaskWidth = 0x707F712F,
    MaskHeight = 0x849CDADC,

    // Texture reference parameters (TGI by index or inline)
    SparkleCube = 0x1D90C086,
    DropShadowAtlas = 0x22AD8507,
    DirtOverlay = 0x48372E62,
    OverlayTexture = 0x4DC0C8BC,
    JetTexture = 0x52CE211B,
    ColorRamp = 0x581835D6,
    DiffuseMap = 0x6CC0FD85,
    SelfIlluminationMap = 0x6E067554,
    NormalMap = 0x6E56548A,
    HaloRamp = 0x84F6E0FB,
    DetailMap = 0x9205DAA8,
    SpecularMap = 0xAD528A60,
    AmbientOcclusionMap = 0xB01CBA60,
    AlphaMap = 0xC3FAAC4F,
    MultiplyMap = 0xCD869A45,
    SpecCompositeTexture = 0xD652FADE,
    NoiseMap = 0xE19FD579,
    RoomLightMap = 0xE7CA9166,
    EmissionMap = 0xF303D152,
    RevealMap = 0xF3F22AC4,

    // TextureKey parameters (full TGI inline + 4 padding bytes)
    ImposterTextureAOandSI = 0x15C9D298,
    ImpostorDetailTexture = 0x56E1C6B2,
    ImposterTexture = 0xBDCF71C5,
    ImposterTextureWater = 0xBF3FB9FA,
}

/// <summary>
/// Shader data element data type identifier.
/// </summary>
public enum ShaderDataType : uint
{
    Unknown = 0,
    Float = 1,
    Int = 2,
    Texture = 4,
    ImageMap = 0x00010004,
    ImageMap2 = 0x00040004,
}

/// <summary>
/// Base class for typed shader data entries. Each entry has a <see cref="Field"/>
/// identifying the parameter and a typed value.
/// </summary>
public abstract class ShaderData
{
    public ShaderFieldType Field { get; set; }
    public abstract ShaderDataType DataType { get; }
    public abstract int ElementCount { get; }

    /// <summary>
    /// Read the data payload from the reader (positioned at the data offset).
    /// </summary>
    public abstract void ReadData(BinaryReader reader);

    /// <summary>
    /// Factory method: reads a shader data entry header from the stream at the current
    /// position, then seeks to the data offset (relative to <paramref name="startPosition"/>)
    /// to read the payload, and returns the cursor to just after the header.
    /// </summary>
    /// <param name="reader">The binary reader.</param>
    /// <param name="startPosition">The base offset used to resolve data pointers (start of the MTRL/MTNF block).</param>
    /// <param name="isGeom">If true, texture references use a TGI index instead of inline ResourceKey.</param>
    public static ShaderData CreateEntry(BinaryReader reader, long startPosition, bool isGeom = false)
    {
        var field = (ShaderFieldType)reader.ReadUInt32();
        var dataType = (ShaderDataType)reader.ReadUInt32();
        int count = reader.ReadInt32();
        uint offset = reader.ReadUInt32();

        long savedPos = reader.BaseStream.Position;
        reader.BaseStream.Position = startPosition + offset;

        ShaderData entry;
        try
        {
            entry = dataType switch
            {
                ShaderDataType.Float => count switch
                {
                    1 => new ShaderFloat(),
                    2 => new ShaderFloat2(),
                    3 => new ShaderFloat3(),
                    4 => new ShaderFloat4(),
                    _ => throw new InvalidDataException($"Invalid count {count} for Float at 0x{reader.BaseStream.Position:X8}"),
                },
                ShaderDataType.Int => count switch
                {
                    1 => new ShaderInt(),
                    _ => throw new InvalidDataException($"Invalid count {count} for Int at 0x{reader.BaseStream.Position:X8}"),
                },
                ShaderDataType.Texture => count switch
                {
                    4 => isGeom ? new ShaderTextureIndex() : new ShaderTextureRef(),
                    5 => new ShaderTextureKey(),
                    _ => throw new InvalidDataException($"Invalid count {count} for Texture at 0x{reader.BaseStream.Position:X8}"),
                },
                ShaderDataType.ImageMap or ShaderDataType.ImageMap2 => count switch
                {
                    4 => new ShaderImageMapKey(),
                    _ => throw new InvalidDataException($"Invalid count {count} for ImageMap at 0x{reader.BaseStream.Position:X8}"),
                },
                _ => throw new InvalidDataException($"Unknown DataType 0x{(uint)dataType:X8} with count {count} at 0x{reader.BaseStream.Position:X8}"),
            };

            entry.Field = field;
            entry.ReadData(reader);
        }
        finally
        {
            reader.BaseStream.Position = savedPos;
        }

        return entry;
    }
}

/// <summary>
/// A single float shader parameter.
/// </summary>
public class ShaderFloat : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.Float;
    public override int ElementCount => 1;
    public float Value { get; set; }

    public override void ReadData(BinaryReader reader) => Value = reader.ReadSingle();
}

/// <summary>
/// A float2 (two floats) shader parameter.
/// </summary>
public class ShaderFloat2 : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.Float;
    public override int ElementCount => 2;
    public float X { get; set; }
    public float Y { get; set; }

    public override void ReadData(BinaryReader reader)
    {
        X = reader.ReadSingle();
        Y = reader.ReadSingle();
    }
}

/// <summary>
/// A float3 (three floats) shader parameter.
/// </summary>
public class ShaderFloat3 : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.Float;
    public override int ElementCount => 3;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public override void ReadData(BinaryReader reader)
    {
        X = reader.ReadSingle();
        Y = reader.ReadSingle();
        Z = reader.ReadSingle();
    }
}

/// <summary>
/// A float4 (four floats) shader parameter.
/// </summary>
public class ShaderFloat4 : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.Float;
    public override int ElementCount => 4;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }

    public override void ReadData(BinaryReader reader)
    {
        X = reader.ReadSingle();
        Y = reader.ReadSingle();
        Z = reader.ReadSingle();
        W = reader.ReadSingle();
    }
}

/// <summary>
/// An integer shader parameter.
/// </summary>
public class ShaderInt : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.Int;
    public override int ElementCount => 1;
    public int Value { get; set; }

    public override void ReadData(BinaryReader reader) => Value = reader.ReadInt32();
}

/// <summary>
/// A texture reference shader parameter (TS4 variant).
/// Reads an inline ITG-ordered ResourceKey (Instance:8 + Type:4 + Group:4 = 16 bytes).
/// </summary>
public class ShaderTextureRef : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.Texture;
    public override int ElementCount => 4;
    public ResourceKey Key { get; set; }

    public override void ReadData(BinaryReader reader)
    {
        // ITG order: Instance (8), Type (4), Group (4)
        ulong instance = reader.ReadUInt64();
        uint type = reader.ReadUInt32();
        uint group = reader.ReadUInt32();
        Key = new ResourceKey((ResourceType)type, group, instance);
    }
}

/// <summary>
/// A texture index shader parameter used in GEOM context.
/// The index refers to the external TGI reference list of the RCOL container.
/// Reads: index (4 bytes) + 12 bytes of zeros = 16 bytes total.
/// </summary>
public class ShaderTextureIndex : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.Texture;
    public override int ElementCount => 4;
    public int Index { get; set; }

    public override void ReadData(BinaryReader reader)
    {
        Index = reader.ReadInt32();
        reader.ReadBytes(12); // padding zeros
    }
}

/// <summary>
/// A texture key shader parameter (TextureKey variant with count=5).
/// Reads an inline ITG-ordered ResourceKey (16 bytes) + 4 bytes padding.
/// </summary>
public class ShaderTextureKey : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.Texture;
    public override int ElementCount => 5;
    public ResourceKey Key { get; set; }

    public override void ReadData(BinaryReader reader)
    {
        // ITG order: Instance (8), Type (4), Group (4)
        ulong instance = reader.ReadUInt64();
        uint type = reader.ReadUInt32();
        uint group = reader.ReadUInt32();
        Key = new ResourceKey((ResourceType)type, group, instance);
        reader.ReadBytes(4); // padding zeros
    }
}

/// <summary>
/// An image map key shader parameter (DataType 0x00010004, count=4).
/// Reads an inline ITG-ordered ResourceKey (16 bytes).
/// </summary>
public class ShaderImageMapKey : ShaderData
{
    public override ShaderDataType DataType => ShaderDataType.ImageMap;
    public override int ElementCount => 4;
    public ResourceKey Key { get; set; }

    public override void ReadData(BinaryReader reader)
    {
        // ITG order: Instance (8), Type (4), Group (4)
        ulong instance = reader.ReadUInt64();
        uint type = reader.ReadUInt32();
        uint group = reader.ReadUInt32();
        Key = new ResourceKey((ResourceType)type, group, instance);
    }
}

/// <summary>
/// Helper to parse a list of <see cref="ShaderData"/> entries from a MTRL or MTNF block.
/// </summary>
public static class ShaderDataList
{
    /// <summary>
    /// Parse a shader data list from the given reader. The reader must be positioned
    /// at the entry count field. <paramref name="startPosition"/> is the base offset
    /// for resolving data pointers.
    /// </summary>
    /// <param name="reader">The binary reader.</param>
    /// <param name="startPosition">The base offset of the containing block (start of MTRL or MTNF).</param>
    /// <param name="expectedDataLength">Optional expected data length for validation (from MTNF header).</param>
    /// <param name="isGeom">True if parsing from a GEOM context.</param>
    public static List<ShaderData> Parse(BinaryReader reader, long startPosition, int? expectedDataLength = null, bool isGeom = false)
    {
        int count = reader.ReadInt32();
        var entries = new List<ShaderData>(count);
        int totalDataBytes = 0;

        for (int i = 0; i < count; i++)
        {
            var entry = ShaderData.CreateEntry(reader, startPosition, isGeom);
            entries.Add(entry);
            totalDataBytes += entry.ElementCount * 4;
        }

        if (expectedDataLength.HasValue && expectedDataLength.Value != totalDataBytes)
            throw new InvalidDataException($"Expected 0x{expectedDataLength.Value:X8} bytes of shader data, calculated 0x{totalDataBytes:X8}");

        // Skip past the data section
        reader.BaseStream.Position += totalDataBytes;

        return entries;
    }
}
