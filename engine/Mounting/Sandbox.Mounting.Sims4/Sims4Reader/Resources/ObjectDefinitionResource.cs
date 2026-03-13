using System.Text;

namespace Sims4Reader.Resources;

/// <summary>
/// Well-known property IDs for ObjectDefinition (OBJD) resources.
/// Properties are identified by FNV hashes in the property table.
/// </summary>
public enum ObjdPropertyId : uint
{
    Name = 0xE7F07786,
    Tuning = 0x790FA4BC,
    TuningId = 0xB994039B,
    Icon = 0xCADED888,
    Rig = 0xE206AE4F,
    Slot = 0x8A85AFF3,
    Model = 0x8D20ACC6,
    Footprint = 0x6C737AD8,
    Components = 0xE6E421FB,
    MaterialVariant = 0xECD5A95F,
    Unknown1 = 0xAC8E1BC0,
    SimoleonPrice = 0xE4F4FAA4,
    PositiveEnvironmentScore = 0x7236BEEA,
    NegativeEnvironmentScore = 0x44FC7512,
    ThumbnailGeometryState = 0x4233F8A0,
    Unknown2 = 0xEC3712E6,
    EnvironmentScoreEmotionTags = 0x2172AEBE,
    EnvironmentScores = 0xDCD08394,
    Unknown3 = 0x52F7F4BC,
    IsBaby = 0xAEE67A1C,
    Unknown4 = 0xF3936A90,
}

/// <summary>
/// OBJD (Object Definition) resource — the core game object record that links
/// catalog metadata to the actual model, rig, slot, and footprint resources.
///
/// Binary format (property-table architecture):
///   Header:  Version (uint16) + TablePosition (uint32)
///   Data:    Property values at variable offsets
///   Table:   At TablePosition: EntryCount (uint16) + [PropertyID (uint32), Offset (uint32)] x N
///
/// TGI block lists (Model, Icon, Rig, Slot, Footprint) use ITG order
/// with an instance-swap: high/low 32-bit halves of instance are swapped on disk.
///
/// Reference: s4pi ObjectDefinitionResource.cs
/// </summary>
public class ObjectDefinitionResource : IResource
{
    public ushort Version { get; set; }

    /// <summary>Object name (ASCII string).</summary>
    public string? Name { get; set; }

    /// <summary>Tuning reference string (links to tuning XML).</summary>
    public string? Tuning { get; set; }

    /// <summary>Tuning ID hash.</summary>
    public ulong TuningId { get; set; }

    /// <summary>Model (MODL) resource references — the actual 3D model TGI keys.</summary>
    public ResourceKey[] Models { get; set; } = Array.Empty<ResourceKey>();

    /// <summary>Icon resource references.</summary>
    public ResourceKey[] Icons { get; set; } = Array.Empty<ResourceKey>();

    /// <summary>Rig resource references.</summary>
    public ResourceKey[] Rigs { get; set; } = Array.Empty<ResourceKey>();

    /// <summary>Slot resource references.</summary>
    public ResourceKey[] Slots { get; set; } = Array.Empty<ResourceKey>();

    /// <summary>Footprint resource references.</summary>
    public ResourceKey[] Footprints { get; set; } = Array.Empty<ResourceKey>();

    /// <summary>Component hash list (game system components attached to this object).</summary>
    public uint[] Components { get; set; } = Array.Empty<uint>();

    /// <summary>Material variant name.</summary>
    public string? MaterialVariant { get; set; }

    /// <summary>Price in simoleons.</summary>
    public uint SimoleonPrice { get; set; }

    /// <summary>Positive environment score contribution.</summary>
    public float PositiveEnvironmentScore { get; set; }

    /// <summary>Negative environment score contribution.</summary>
    public float NegativeEnvironmentScore { get; set; }

    /// <summary>Geometry state used for thumbnail rendering.</summary>
    public uint ThumbnailGeometryState { get; set; }

    /// <summary>Whether this object is a baby object.</summary>
    public bool IsBaby { get; set; }

    /// <summary>Which properties were present in the property table.</summary>
    public HashSet<ObjdPropertyId> PresentProperties { get; set; } = new();

    /// <summary>Whether the full resource was parsed without errors.</summary>
    public bool IsFullyParsed { get; set; }

    public void Parse(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 6)
            return;

        using var ms = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(ms);

        try
        {
            Version = reader.ReadUInt16();
            uint tablePosition = reader.ReadUInt32();

            if (tablePosition >= ms.Length)
                return;

            // Jump to the property table
            ms.Position = tablePosition;
            ushort entryCount = reader.ReadUInt16();

            // Read each property from the table
            for (int i = 0; i < entryCount; i++)
            {
                uint propertyId = reader.ReadUInt32();
                uint offset = reader.ReadUInt32();
                long nextPosition = ms.Position;

                var id = (ObjdPropertyId)propertyId;
                PresentProperties.Add(id);

                ms.Position = offset;

                switch (id)
                {
                    case ObjdPropertyId.Name:
                        Name = ReadString(reader);
                        break;
                    case ObjdPropertyId.Tuning:
                        Tuning = ReadString(reader);
                        break;
                    case ObjdPropertyId.TuningId:
                        TuningId = reader.ReadUInt64();
                        break;
                    case ObjdPropertyId.Model:
                        Models = ReadTgiBlockList(reader);
                        break;
                    case ObjdPropertyId.Icon:
                        Icons = ReadTgiBlockList(reader);
                        break;
                    case ObjdPropertyId.Rig:
                        Rigs = ReadTgiBlockList(reader);
                        break;
                    case ObjdPropertyId.Slot:
                        Slots = ReadTgiBlockList(reader);
                        break;
                    case ObjdPropertyId.Footprint:
                        Footprints = ReadTgiBlockList(reader);
                        break;
                    case ObjdPropertyId.Components:
                        int componentCount = reader.ReadInt32();
                        Components = new uint[componentCount];
                        for (int j = 0; j < componentCount; j++)
                            Components[j] = reader.ReadUInt32();
                        break;
                    case ObjdPropertyId.MaterialVariant:
                        MaterialVariant = ReadString(reader);
                        break;
                    case ObjdPropertyId.SimoleonPrice:
                        SimoleonPrice = reader.ReadUInt32();
                        break;
                    case ObjdPropertyId.PositiveEnvironmentScore:
                        PositiveEnvironmentScore = reader.ReadSingle();
                        break;
                    case ObjdPropertyId.NegativeEnvironmentScore:
                        NegativeEnvironmentScore = reader.ReadSingle();
                        break;
                    case ObjdPropertyId.ThumbnailGeometryState:
                        ThumbnailGeometryState = reader.ReadUInt32();
                        break;
                    case ObjdPropertyId.IsBaby:
                        IsBaby = reader.ReadBoolean();
                        break;
                    // Skip unknown/unneeded properties
                    default:
                        break;
                }

                ms.Position = nextPosition;
            }

            IsFullyParsed = true;
        }
        catch (EndOfStreamException)
        {
            // Partial parse — return what we have
        }
    }

    /// <summary>
    /// Read a TGI block list in ITG order with instance swap (s4pi convention).
    /// The count field stores bytes (count / 4 = number of 16-byte TGI entries).
    /// Instance halves are swapped on disk: stored as (low32 | high32), read as (high32 | low32).
    /// </summary>
    private static ResourceKey[] ReadTgiBlockList(BinaryReader reader)
    {
        int byteCount = reader.ReadInt32();
        // Each TGI entry is 16 bytes (8 instance + 4 type + 4 group)
        // But s4pi divides by 4, not 16 — this gives "element count" in their terms
        // Actually: byteCount / 4 gives the number of uint32-sized units,
        // and each TGI block is 4 units (16 bytes). So entries = byteCount / 4 / 4 = byteCount / 16.
        // However, s4pi code does: count = ReadInt32() / 4, then reads 'count' TGI blocks.
        // This means byteCount is actually (numEntries * 4), not (numEntries * 16).
        // Matching s4pi behavior exactly:
        int count = byteCount / 4;

        if (count <= 0 || count > 1000)
            return Array.Empty<ResourceKey>();

        var keys = new ResourceKey[count];
        for (int i = 0; i < count; i++)
        {
            ulong instance = reader.ReadUInt64();
            // Swap high/low 32-bit halves (s4pi convention for OBJD TGI blocks)
            instance = (instance << 32) | (instance >> 32);
            uint type = reader.ReadUInt32();
            uint group = reader.ReadUInt32();
            keys[i] = new ResourceKey((ResourceType)type, group, instance);
        }
        return keys;
    }

    /// <summary>
    /// Read a length-prefixed ASCII string (int32 length + bytes).
    /// </summary>
    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0 || length > 10000)
            return string.Empty;
        return Encoding.ASCII.GetString(reader.ReadBytes(length));
    }
}
