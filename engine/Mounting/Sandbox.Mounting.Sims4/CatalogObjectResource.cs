namespace Sims4Reader.Resources;

/// <summary>
/// A tag/category pair used for catalog filtering.
/// </summary>
public struct CatalogTag
{
    public uint Category;
    public uint Value;
}

/// <summary>
/// COBJ (Catalog Object) resource - defines catalog properties for buyable/buildable
/// objects in The Sims 4. Links OBJD to catalog metadata including price, tags,
/// thumbnails, and resource references.
/// </summary>
public class CatalogObjectResource : IResource
{
    public uint Version { get; set; }

    // Common block
    public uint CommonBlockVersion { get; set; }
    public uint NameHash { get; set; }
    public uint DescriptionHash { get; set; }
    public uint SimoleonPrice { get; set; }
    public ulong ThumbnailHash { get; set; }
    public uint DevCategoryFlags { get; set; }
    public uint BuildBuyStatusFlags { get; set; }

    // COBJ-specific
    public CatalogTag[] Tags { get; set; } = Array.Empty<CatalogTag>();
    public ushort SelectionGroup { get; set; }
    public ushort ObjectType { get; set; }
    public uint ObjectTypeFlags { get; set; }
    public uint WallPlacementFlags { get; set; }
    public uint MovementFlags { get; set; }

    /// <summary>
    /// TGI reference list at end of resource (links to OBJD, models, thumbnails, etc.)
    /// </summary>
    public ResourceKey[] TgiReferences { get; set; } = Array.Empty<ResourceKey>();

    public void Parse(ReadOnlyMemory<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(ms);

        Version = reader.ReadUInt32();

        // Common block
        CommonBlockVersion = reader.ReadUInt32();
        NameHash = reader.ReadUInt32();
        DescriptionHash = reader.ReadUInt32();
        SimoleonPrice = reader.ReadUInt32();
        ThumbnailHash = reader.ReadUInt64();
        DevCategoryFlags = reader.ReadUInt32();

        // Locale overrides (version >= 0x09)
        if (CommonBlockVersion >= 0x09)
        {
            ushort localeCount = reader.ReadUInt16();
            for (int i = 0; i < localeCount; i++)
            {
                reader.ReadByte();   // localeId
                reader.ReadUInt32(); // nameHash
                reader.ReadUInt32(); // descHash
            }
        }

        // Style items
        ushort styleCount = reader.ReadUInt16();
        for (int i = 0; i < styleCount; i++)
        {
            reader.ReadBytes(16); // TGI reference (ITG)
            reader.ReadUInt32();  // unknown1
        }

        // Aural properties (version >= 0x0B)
        if (CommonBlockVersion >= 0x0B)
        {
            uint auralVersion = reader.ReadUInt32();
            reader.ReadUInt32(); // auralHash1
            reader.ReadUInt32(); // auralHash2
            if (auralVersion >= 0x02) reader.ReadUInt32(); // auralHash3
            if (auralVersion >= 0x03) reader.ReadUInt32(); // auralHash4
            if (auralVersion >= 0x04) reader.ReadByte();   // auralExtra
        }

        BuildBuyStatusFlags = reader.ReadUInt32();
        reader.ReadUInt64(); // packNameHash
        reader.ReadUInt64(); // packDescHash

        if (CommonBlockVersion >= 0x0C)
            reader.ReadByte(); // packIconIndex

        // COBJ-specific fields
        uint tagCount = reader.ReadUInt32();
        Tags = new CatalogTag[tagCount];
        for (int i = 0; i < tagCount; i++)
        {
            Tags[i] = new CatalogTag
            {
                Category = reader.ReadUInt32(),
                Value = reader.ReadUInt32(),
            };
        }

        SelectionGroup = reader.ReadUInt16();
        ObjectType = reader.ReadUInt16();
        ObjectTypeFlags = reader.ReadUInt32();
        WallPlacementFlags = reader.ReadUInt32();
        MovementFlags = reader.ReadUInt32();

        reader.ReadUInt32(); // cutoutTilesPerLevel
        reader.ReadUInt32(); // numLevels

        // Wall masks
        byte wallMaskCount = reader.ReadByte();
        for (int i = 0; i < wallMaskCount; i++)
            reader.ReadBytes(21); // 4+4+4+4+4+1

        reader.ReadByte();   // scriptEnabled
        reader.ReadUInt32(); // diagonalIndex
        reader.ReadUInt32(); // ambienceTypeHash
        reader.ReadUInt32(); // roomCategoryFlags
        reader.ReadUInt32(); // functionCategoryFlags
        reader.ReadUInt64(); // subCategoryFlags
        reader.ReadUInt64(); // subRoomFlags
        reader.ReadUInt32(); // buildCategoryFlags
        reader.ReadUInt32(); // slotPlacementFlags

        // 7-bit encoded strings
        ReadString7Bit(reader); // surfaceType
        ReadString7Bit(reader); // sourceMaterial

        reader.ReadUInt32(); // moodletGiven
        reader.ReadUInt32(); // moodletScore

        // Topic/Rating pairs
        uint topicRatingCount = reader.ReadUInt32();
        for (int i = 0; i < topicRatingCount; i++)
        {
            reader.ReadUInt32(); // topic
            reader.ReadUInt32(); // rating
        }

        // Version-dependent fields
        if (Version >= 0x0E) reader.ReadUInt32(); // fallbackIndex
        if (Version >= 0x10)
        {
            reader.ReadUInt32(); // modularArchEndEastIndex
            reader.ReadUInt32(); // modularArchEndWestIndex
            reader.ReadUInt32(); // modularArchConnectingIndex
            reader.ReadUInt32(); // modularArchSingleIndex
        }
        if (Version >= 0x12) reader.ReadByte();   // unknown1
        if (Version >= 0x13) reader.ReadUInt32(); // unknown2
        if (Version >= 0x16) reader.ReadUInt64(); // unknown3
        if (Version >= 0x19) reader.ReadUInt32(); // unknown4

        // TGI reference list
        if (ms.Position < ms.Length)
        {
            byte tgiCount = reader.ReadByte();
            TgiReferences = new ResourceKey[tgiCount];
            for (int i = 0; i < tgiCount; i++)
            {
                ulong instance = reader.ReadUInt64();
                uint group = reader.ReadUInt32();
                uint type = reader.ReadUInt32();
                TgiReferences[i] = new ResourceKey((ResourceType)type, group, instance);
            }
        }
    }

    private static void ReadString7Bit(BinaryReader reader)
    {
        byte length = reader.ReadByte();
        if (length > 0)
            reader.ReadBytes(length);
    }
}
