using Sims4Reader.Rcol;

namespace Sims4Reader.Material;

/// <summary>
/// MTRL sub-structure found inside older MATD chunks (version &lt; 0x103).
///
/// On-disk layout:
///   Tag:       4 bytes ("MTRL")
///   Unknown1:  4 bytes (uint)
///   Unknown2:  2 bytes (ushort)
///   Unknown3:  2 bytes (ushort)
///   [ShaderData list -- no explicit data length field, reads until count exhausted]
/// </summary>
public class MtrlBlock
{
    private const uint MtrlTag = 0x4C52544D; // "MTRL"

    public uint Unknown1 { get; set; }
    public ushort Unknown2 { get; set; }
    public ushort Unknown3 { get; set; }
    public List<ShaderData> ShaderEntries { get; set; } = new();

    public void Parse(BinaryReader reader)
    {
        long start = reader.BaseStream.Position;

        uint tag = reader.ReadUInt32();
        if (tag != MtrlTag)
            throw new InvalidDataException(
                $"Invalid MTRL tag: 0x{tag:X8} at 0x{reader.BaseStream.Position:X8}; expected 'MTRL'");

        Unknown1 = reader.ReadUInt32();
        Unknown2 = reader.ReadUInt16();
        Unknown3 = reader.ReadUInt16();

        // MTRL uses the old-style ShaderDataList parse with no explicit data length
        ShaderEntries = ShaderDataList.Parse(reader, start);
    }
}

/// <summary>
/// MATD (Material Definition) RCOL chunk.
///
/// On-disk layout:
///   Tag:             4 bytes ("MATD")
///   Version:         4 bytes (uint)
///   MaterialNameHash: 4 bytes (uint)
///   Shader:          4 bytes (ShaderType enum)
///   DataLength:      4 bytes (uint, byte length of the following sub-block)
///
///   If version &lt; 0x103:
///     MTRL sub-block
///   If version >= 0x103:
///     IsVideoSurface:    4 bytes (int, non-zero = true)
///     IsPaintingSurface:  4 bytes (int, non-zero = true)
///     MTNF sub-block (MaterialInfo)
/// </summary>
public class MaterialDefinition : RcolChunk
{
    private const uint MatdTag = 0x4454414D; // "MATD"

    public uint Version { get; set; }
    public uint MaterialNameHash { get; set; }
    public ShaderType Shader { get; set; }

    /// <summary>Only set when version >= 0x103.</summary>
    public bool IsVideoSurface { get; set; }

    /// <summary>Only set when version >= 0x103.</summary>
    public bool IsPaintingSurface { get; set; }

    /// <summary>
    /// The MTRL sub-block (only for version &lt; 0x103).
    /// Null when version >= 0x103.
    /// </summary>
    public MtrlBlock? Mtrl { get; set; }

    /// <summary>
    /// The MTNF (MaterialInfo) sub-block (only for version >= 0x103).
    /// Null when version &lt; 0x103.
    /// </summary>
    public MaterialInfo? Mtnf { get; set; }

    /// <summary>
    /// All shader data entries, regardless of which sub-block format was used.
    /// Convenience accessor that returns entries from either Mtrl or Mtnf.
    /// </summary>
    public List<ShaderData> ShaderEntries =>
        Mtnf?.ShaderEntries ?? Mtrl?.ShaderEntries ?? new List<ShaderData>();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        uint tag = reader.ReadUInt32();
        if (tag != MatdTag)
            throw new InvalidDataException(
                $"Invalid MATD tag: 0x{tag:X8} at 0x{reader.BaseStream.Position:X8}; expected 'MATD'");

        Tag = tag;
        Version = reader.ReadUInt32();
        MaterialNameHash = reader.ReadUInt32();
        Shader = (ShaderType)reader.ReadUInt32();
        uint dataLength = reader.ReadUInt32();

        long dataStart;
        if (Version < 0x00000103)
        {
            dataStart = reader.BaseStream.Position;
            Mtrl = new MtrlBlock();
            Mtrl.Parse(reader);
            Mtnf = null;
        }
        else
        {
            IsVideoSurface = reader.ReadInt32() != 0;
            IsPaintingSurface = reader.ReadInt32() != 0;
            dataStart = reader.BaseStream.Position;
            Mtnf = new MaterialInfo();
            Mtnf.Parse(reader);
            Mtrl = null;
        }
    }
}
