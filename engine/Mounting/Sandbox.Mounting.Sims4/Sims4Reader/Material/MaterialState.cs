using Sims4Reader.Rcol;

namespace Sims4Reader.Material;

/// <summary>
/// Known material states used in MTST entries.
/// </summary>
public enum MaterialStateType : uint
{
    Default = 0x2EA8FB98,
    Dirty = 0xEEAB4327,
    VeryDirty = 0x2E5DF9BB,
    Burnt = 0xC3867C32,
    Clogged = 0x257FB026,
    CarLightsOff = 0xE4AF52C1,
}

/// <summary>
/// Version 0x300+ MTST entry: maps a material state and variant to a MATD chunk reference.
/// </summary>
public class MaterialStateEntry300
{
    /// <summary>
    /// Chunk reference index into the RCOL chunk list (a uint index to MATD).
    /// </summary>
    public uint MatdIndex { get; set; }

    public MaterialStateType State { get; set; }

    public uint MaterialVariant { get; set; }

    public void Parse(BinaryReader reader)
    {
        MatdIndex = reader.ReadUInt32();
        State = (MaterialStateType)reader.ReadUInt32();
        MaterialVariant = reader.ReadUInt32();
    }
}

/// <summary>
/// Version 0x200 MTST entry: maps a material state to a MATD chunk reference (no variant field).
/// </summary>
public class MaterialStateEntry200
{
    /// <summary>
    /// Chunk reference index into the RCOL chunk list (a uint index to MATD).
    /// </summary>
    public uint MatdIndex { get; set; }

    public MaterialStateType State { get; set; }

    public void Parse(BinaryReader reader)
    {
        MatdIndex = reader.ReadUInt32();
        State = (MaterialStateType)reader.ReadUInt32();
    }
}

/// <summary>
/// MTST (Material State) RCOL chunk. Maps material states (Default, Dirty, Burnt, etc.)
/// to MATD material definition chunk references.
///
/// On-disk layout:
///   Tag:       4 bytes ("MTST")
///   Version:   4 bytes (uint, typically 0x200 or 0x300)
///   NameHash:  4 bytes (uint)
///   Index:     4 bytes (uint, chunk reference to default MATD)
///
///   If version &lt; 768 (0x300):
///     Count:   4 bytes (int)
///     Entry[]: count * Type200Entry (8 bytes each: MatdIndex + State)
///
///   If version >= 768 (0x300):
///     Count:   4 bytes (int)
///     Entry[]: count * Type300Entry (12 bytes each: MatdIndex + State + Variant)
/// </summary>
public class MaterialState : RcolChunk
{
    private const uint MtstTag = 0x5453544D; // "MTST"

    public uint Version { get; set; }
    public uint NameHash { get; set; }

    /// <summary>
    /// Chunk reference index to the default MATD.
    /// </summary>
    public uint DefaultMatdIndex { get; set; }

    /// <summary>
    /// Entries for version &lt; 0x300 (no material variant).
    /// Null if version >= 0x300.
    /// </summary>
    public List<MaterialStateEntry200>? Entries200 { get; set; }

    /// <summary>
    /// Entries for version >= 0x300 (includes material variant).
    /// Null if version &lt; 0x300.
    /// </summary>
    public List<MaterialStateEntry300>? Entries300 { get; set; }

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        uint tag = reader.ReadUInt32();
        if (tag != MtstTag)
            throw new InvalidDataException(
                $"Invalid MTST tag: 0x{tag:X8} at 0x{reader.BaseStream.Position:X8}; expected 'MTST'");

        Tag = tag;
        Version = reader.ReadUInt32();
        NameHash = reader.ReadUInt32();
        DefaultMatdIndex = reader.ReadUInt32();

        if (Version < 768u)
        {
            int count = reader.ReadInt32();
            Entries200 = new List<MaterialStateEntry200>(count);
            for (int i = 0; i < count; i++)
            {
                var entry = new MaterialStateEntry200();
                entry.Parse(reader);
                Entries200.Add(entry);
            }
            Entries300 = null;
        }
        else
        {
            int count = reader.ReadInt32();
            Entries300 = new List<MaterialStateEntry300>(count);
            for (int i = 0; i < count; i++)
            {
                var entry = new MaterialStateEntry300();
                entry.Parse(reader);
                Entries300.Add(entry);
            }
            Entries200 = null;
        }
    }
}
