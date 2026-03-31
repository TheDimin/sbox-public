using Sandbox;

namespace Sims4Reader.Resources;

/// <summary>
/// Build/buy product status flags from the COBJ common block.
/// Controls catalog visibility and object classification.
/// </summary>
[Flags]
public enum DevCategoryFlags : uint
{
    None = 0,
    Debug           = 1 << 0,
    Modded          = 1 << 1,
    ShippingOnly    = 1 << 2,
    ShowInCatalog   = 1 << 3,
}

/// <summary>
/// Placement flags from the COBJ body — controls where the object can be placed.
/// Stored on disk as two uint32 fields (High, Low) forming a 64-bit bitfield.
/// Flag names sourced from decompiled game Python enumerables.
/// </summary>
[Flags]
public enum PlacementFlags : ulong
{
    None = 0,
    CenterOnWall          = 1UL << 0,
    EdgeAgainstWall       = 1UL << 1,
    AdjustHeightOnWall    = 1UL << 2,
    Ceiling               = 1UL << 3,
    ImmovableByUser       = 1UL << 4,
    Diagonal              = 1UL << 5,
    Roof                  = 1UL << 6,
    RequiresFence         = 1UL << 7,
    ShowObjIfWallDown     = 1UL << 8,
    SlottedToFence        = 1UL << 9,
    RequiresSlot          = 1UL << 10,
    AllowedOnSlope        = 1UL << 11,
    RepeatPlacement       = 1UL << 12,
    NonDeleteable         = 1UL << 13,
    NonInventoryable      = 1UL << 14,
    NonAbandonable        = 1UL << 15,
    RequiresTerrain       = 1UL << 16,
    EncourageIndoor       = 1UL << 17,
    EncourageOutdoor      = 1UL << 18,
    NonDeletableByUser    = 1UL << 19,
    NonInventoryableByUser = 1UL << 20,
    RequiresWaterSurface  = 1UL << 21,
    AllowedInFountain     = 1UL << 22,
    GroundedAgainstWall   = 1UL << 23,
    NotBlueprintable      = 1UL << 24,
    IsHuman               = 1UL << 25,
    AllowedOnWaterSurface = 1UL << 26,
    AllowedInPool         = 1UL << 27,
    OnWallTop             = 1UL << 28,
    ForceDesignable       = 1UL << 29,
    AlwaysBlueprintable   = 1UL << 30,
    WallOptional          = 1UL << 31,
}

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
    public DevCategoryFlags DevCategoryFlags { get; set; }
    public short PackId { get; set; }

    // Tags from common block
    public CatalogTag[] Tags { get; set; } = Array.Empty<CatalogTag>();

    // Swatch metadata from common block
    public ushort SwatchColorsSortPriority { get; set; }
    public ulong VariantThumbImageHash { get; set; }

    // Placement
    public uint PlacementFlagsHigh { get; set; }
    public uint PlacementFlagsLow { get; set; }

    /// <summary>
    /// Combined 64-bit placement flags. High word stored in bits 32..63, Low in bits 0..31.
    /// </summary>
    public PlacementFlags Placement => (PlacementFlags)( ((ulong)PlacementFlagsHigh << 32) | PlacementFlagsLow );

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
        DevCategoryFlags = (DevCategoryFlags)reader.ReadUInt32();

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

        PlacementFlagsHigh = reader.ReadUInt32();
        PlacementFlagsLow = reader.ReadUInt32();
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
