using Sandbox;

namespace Sims4Reader.Resources;

/// <summary>
/// A tag/category pair used for catalog filtering.
/// </summary>
public struct CatalogTag
{
    public ushort Category;
    public ushort Value;
}

/// <summary>
/// COBJ (Catalog Object) resource - defines catalog properties for buyable/buildable
/// objects in The Sims 4. Links to catalog metadata including price, tags,
/// thumbnails, and resource references.
///
/// Format based on s4pi CatalogCommon + AbstractCatalogResource.
/// </summary>
public class CatalogObjectResource : IResource
{
    public uint Version { get; set; }

    // Common block (CatalogCommon)
    public uint CommonBlockVersion { get; set; }
    public uint NameHash { get; set; }
    public uint DescriptionHash { get; set; }
    public uint SimoleonPrice { get; set; }
    public ulong ThumbnailHash { get; set; }
    public uint DevCategoryFlags { get; set; }
    public short PackId { get; set; }

    // Tags from common block
    public CatalogTag[] Tags { get; set; } = Array.Empty<CatalogTag>();

    // Swatch metadata from common block
    public ushort SwatchColorsSortPriority { get; set; }
    public ulong VariantThumbImageHash { get; set; }

    // Body fields
    public uint[] CatalogFilterColors { get; set; } = Array.Empty<uint>();
    public bool IsStackable { get; set; }
    public bool CanDepreciate { get; set; }

    /// <summary>
    /// Whether the full COBJ body was parsed, or only the common header was readable.
    /// </summary>
    public bool IsFullyParsed { get; private set; }

    /// <summary>
    /// TGI reference list (links to OBJD, models, thumbnails, etc.)
    /// </summary>
    public ResourceKey[] TgiReferences { get; set; } = Array.Empty<ResourceKey>();

    public void Parse(ReadOnlyMemory<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(ms);

        try
        {
            Version = reader.ReadUInt32();
            ParseCatalogCommon(reader);
            ParseAbstractCatalogBody(reader, ms);
            IsFullyParsed = true;
        }
        catch (EndOfStreamException)
        {
            // Format mismatch for this version — common block tags are still usable
        }
    }

    /// <summary>
    /// Parse the CatalogCommon block (s4pi CatalogCommon.cs).
    /// </summary>
    private void ParseCatalogCommon(BinaryReader reader)
    {
        CommonBlockVersion = reader.ReadUInt32();
        NameHash = reader.ReadUInt32();
        DescriptionHash = reader.ReadUInt32();
        SimoleonPrice = reader.ReadUInt32();
        ThumbnailHash = reader.ReadUInt64();
        DevCategoryFlags = reader.ReadUInt32();

        // ProductStyles: byte count + TGI blocks (16 bytes each)
        byte styleCount = reader.ReadByte();
        for (int i = 0; i < styleCount; i++)
            reader.ReadBytes(16); // TGI block (ITG format)

        if (CommonBlockVersion >= 10)
        {
            PackId = reader.ReadInt16();
            reader.ReadByte();   // PackFlags
            reader.ReadBytes(9); // ReservedBytes
        }
        else
        {
            byte unused2 = reader.ReadByte();
            if (unused2 > 0)
                reader.ReadByte(); // Unused3
        }

        // Tags
        if (CommonBlockVersion >= 11)
        {
            // Tag list parsed as full tag objects
            uint tagCount = reader.ReadUInt32();
            if (tagCount > 1000) tagCount = 0; // sanity check
            Tags = new CatalogTag[tagCount];
            for (int i = 0; i < tagCount; i++)
            {
                Tags[i] = new CatalogTag
                {
                    Category = reader.ReadUInt16(),
                    Value = reader.ReadUInt16(),
                };
            }
        }
        else
        {
            // Older format: uint32 count, each tag as pair of uint16
            uint tagCount = reader.ReadUInt32();
            if (tagCount > 1000) tagCount = 0;
            Tags = new CatalogTag[tagCount];
            for (int i = 0; i < tagCount; i++)
            {
                Tags[i] = new CatalogTag
                {
                    Category = reader.ReadUInt16(),
                    Value = reader.ReadUInt16(),
                };
            }
        }

        // SellingPoints
        uint sellingPointCount = reader.ReadUInt32();
        if (sellingPointCount > 1000) sellingPointCount = 0;
        for (int i = 0; i < sellingPointCount; i++)
        {
            reader.ReadUInt16(); // type
            reader.ReadInt32();  // points
        }

        reader.ReadUInt32(); // UnlockByHash
        reader.ReadUInt32(); // UnlockedByHash
        SwatchColorsSortPriority = reader.ReadUInt16();
        VariantThumbImageHash = reader.ReadUInt64();
    }

    /// <summary>
    /// Parse AbstractCatalogResource body fields after the common block.
    /// </summary>
    private void ParseAbstractCatalogBody(BinaryReader reader, MemoryStream ms)
    {
        // Aural materials
        uint auralMaterialsVersion = reader.ReadUInt32();
        reader.ReadUInt32(); // auralMaterials1
        reader.ReadUInt32(); // auralMaterials2
        reader.ReadUInt32(); // auralMaterials3

        // Aural properties
        uint auralPropertiesVersion = reader.ReadUInt32();
        reader.ReadUInt32(); // auralQuality
        if (auralPropertiesVersion > 1)
            reader.ReadUInt32(); // auralAmbientObject
        if (auralPropertiesVersion == 3)
        {
            reader.ReadUInt64(); // ambienceFileInstanceId
            reader.ReadByte();   // isOverrideAmbience
        }
        if (auralPropertiesVersion == 4)
            reader.ReadByte(); // unknown01

        reader.ReadUInt32(); // unused0
        reader.ReadUInt32(); // unused1
        reader.ReadUInt32(); // unused2

        reader.ReadUInt32(); // placementFlagsHigh
        reader.ReadUInt32(); // placementFlagsLow
        reader.ReadUInt64(); // slotTypeSet
        reader.ReadByte();   // slotDecoSize
        reader.ReadUInt64(); // catalogGroup
        reader.ReadByte();   // stateUsage

        // Catalog filter colors (dominant colors for buy-mode filtering)
        byte colorCount = reader.ReadByte();
        CatalogFilterColors = new uint[colorCount];
        for (int i = 0; i < colorCount; i++)
            CatalogFilterColors[i] = reader.ReadUInt32(); // ARGB color

        reader.ReadUInt32(); // fenceHeight
        IsStackable = reader.ReadByte() != 0;
        CanDepreciate = reader.ReadByte() != 0;

        if (Version >= 0x19)
            reader.ReadBytes(16); // fallbackObjectKey TGI

        // Try to read any remaining TGI references at the end
        ParseTgiReferences(reader, ms);
    }

    private void ParseTgiReferences(BinaryReader reader, MemoryStream ms)
    {
        if (ms.Position >= ms.Length)
            return;

        // Try reading as byte count + ITG entries
        long remaining = ms.Length - ms.Position;
        if (remaining < 1)
            return;

        byte tgiCount = reader.ReadByte();
        long tgiSize = tgiCount * 16L;

        if (tgiSize > ms.Length - ms.Position)
        {
            // Not a valid TGI block, skip
            return;
        }

        TgiReferences = new ResourceKey[tgiCount];
        for (int i = 0; i < tgiCount; i++)
        {
            ulong instance = reader.ReadUInt64();
            uint type = reader.ReadUInt32();
            uint group = reader.ReadUInt32();
            TgiReferences[i] = new ResourceKey((ResourceType)type, group, instance);
        }
    }
}
