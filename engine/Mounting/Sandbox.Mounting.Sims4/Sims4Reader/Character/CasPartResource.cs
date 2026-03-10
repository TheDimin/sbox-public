using System.Text;

namespace Sims4Reader.Character;

/// <summary>
/// Sims 4 CAS Part (CASP) resource. Resource type: 0x034AEECB.
///
/// Describes a single Create-A-Sim part: hair, clothing, accessory, skin detail, etc.
/// Contains LOD mesh references, texture keys, swatch colors, flag metadata,
/// and a trailing TGI block list that the byte-index fields point into.
/// </summary>
public class CasPartResource : INamedResource
{
    // -- Header ----------------------------------------------------------

    public uint Version { get; set; }
    public uint TgiOffset { get; set; }
    public uint PresetCount { get; set; }
    public string Name { get; set; } = string.Empty;

    // -- Sort / identity -------------------------------------------------

    public float SortPriority { get; set; }
    public ushort SecondarySortIndex { get; set; }
    public uint PropertyId { get; set; }
    public uint AuralMaterialHash { get; set; }

    // -- Part flags ------------------------------------------------------

    public ParmFlag PartFlags { get; set; }
    public ExcludePartFlag ExcludePartFlags { get; set; }
    public uint ExcludeModifierRegionFlags { get; set; }
    public List<CasFlag> FlagList { get; set; } = [];

    // -- Pricing / text --------------------------------------------------

    public uint SimoleonPrice { get; set; }
    public uint PartTitleKey { get; set; }
    public uint PartDescriptionKey { get; set; }

    // -- Body / age-gender -----------------------------------------------

    public byte UniqueTextureSpace { get; set; }
    public int BodyType { get; set; }
    public int Unused1 { get; set; }
    public AgeGenderFlags AgeGender { get; set; }
    public byte Unused2 { get; set; }
    public byte Unused3 { get; set; }

    // -- Swatch colors ---------------------------------------------------

    public List<uint> SwatchColorValues { get; set; } = [];

    // -- TGI index keys (byte indices into TgiList) ----------------------

    public byte BuffResKey { get; set; }
    public byte VariantThumbnailKey { get; set; }

    /// <summary>Only present when Version >= 0x1C.</summary>
    public ulong VoiceEffectHash { get; set; }

    public byte NakedKey { get; set; }
    public byte ParentKey { get; set; }
    public int SortLayer { get; set; }

    // -- LOD blocks ------------------------------------------------------

    public List<LodBlock> LodBlockList { get; set; } = [];

    // -- Remaining key indices -------------------------------------------

    public List<byte> SlotKeys { get; set; } = [];
    public byte DiffuseShadowKey { get; set; }
    public byte ShadowKey { get; set; }
    public byte CompositionMethod { get; set; }
    public byte RegionMapKey { get; set; }
    public byte Overrides { get; set; }
    public byte NormalMapKey { get; set; }
    public byte SpecularMapKey { get; set; }

    /// <summary>Only present when Version >= 0x1B.</summary>
    public uint SharedUVMapSpace { get; set; }

    // -- TGI block list (resource references) ----------------------------

    public List<ResourceKey> TgiList { get; set; } = [];

    // ====================================================================
    //  Parse
    // ====================================================================

    public void Parse(ReadOnlyMemory<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms, Encoding.UTF8);

        Version = r.ReadUInt32();
        TgiOffset = r.ReadUInt32() + 8; // offset is relative to byte 8
        PresetCount = r.ReadUInt32();
        Name = ReadBigEndianUnicodeString(r);

        SortPriority = r.ReadSingle();
        SecondarySortIndex = r.ReadUInt16();
        PropertyId = r.ReadUInt32();
        AuralMaterialHash = r.ReadUInt32();
        PartFlags = (ParmFlag)r.ReadByte();
        ExcludePartFlags = (ExcludePartFlag)r.ReadUInt64();
        ExcludeModifierRegionFlags = r.ReadUInt32();

        // Flag list (uint32 count, then ushort category + ushort value each)
        uint flagCount = r.ReadUInt32();
        FlagList = new List<CasFlag>((int)flagCount);
        for (int i = 0; i < flagCount; i++)
        {
            ushort category = r.ReadUInt16();
            ushort value = r.ReadUInt16();
            FlagList.Add(new CasFlag(category, value));
        }

        SimoleonPrice = r.ReadUInt32();
        PartTitleKey = r.ReadUInt32();
        PartDescriptionKey = r.ReadUInt32();
        UniqueTextureSpace = r.ReadByte();
        BodyType = r.ReadInt32();
        Unused1 = r.ReadInt32();
        AgeGender = (AgeGenderFlags)r.ReadUInt32();
        Unused2 = r.ReadByte();
        Unused3 = r.ReadByte();

        // Swatch color list (byte count, then int32 ARGB each)
        byte swatchCount = r.ReadByte();
        SwatchColorValues = new List<uint>(swatchCount);
        for (int i = 0; i < swatchCount; i++)
            SwatchColorValues.Add(r.ReadUInt32());

        BuffResKey = r.ReadByte();
        VariantThumbnailKey = r.ReadByte();
        if (Version >= 0x1C)
            VoiceEffectHash = r.ReadUInt64();
        NakedKey = r.ReadByte();
        ParentKey = r.ReadByte();
        SortLayer = r.ReadInt32();

        // ----------------------------------------------------------------
        // TGI block list lives at TgiOffset -- jump there first so the
        // LOD blocks can resolve their byte-index references.
        // ----------------------------------------------------------------
        long savedPosition = ms.Position;
        ms.Position = TgiOffset;
        byte tgiCount = r.ReadByte();
        TgiList = new List<ResourceKey>(tgiCount);
        for (int i = 0; i < tgiCount; i++)
        {
            // Format "IGT": Instance(8), Group(4), Type(4)
            ulong instance = r.ReadUInt64();
            uint group = r.ReadUInt32();
            uint type = r.ReadUInt32();
            TgiList.Add(new ResourceKey((ResourceType)type, group, instance));
        }
        ms.Position = savedPosition;

        // LOD block list
        byte lodCount = r.ReadByte();
        LodBlockList = new List<LodBlock>(lodCount);
        for (int i = 0; i < lodCount; i++)
            LodBlockList.Add(LodBlock.Read(r));

        // Slot key list
        byte slotCount = r.ReadByte();
        SlotKeys = new List<byte>(slotCount);
        for (int i = 0; i < slotCount; i++)
            SlotKeys.Add(r.ReadByte());

        DiffuseShadowKey = r.ReadByte();
        ShadowKey = r.ReadByte();
        CompositionMethod = r.ReadByte();
        RegionMapKey = r.ReadByte();
        Overrides = r.ReadByte();
        NormalMapKey = r.ReadByte();
        SpecularMapKey = r.ReadByte();

        if (Version >= 0x1B)
            SharedUVMapSpace = r.ReadUInt32();
    }

    // ====================================================================
    //  INamedResource
    // ====================================================================

    public IEnumerable<uint> GetNameHashes()
    {
        if (PartTitleKey != 0)
            yield return PartTitleKey;
        if (PartDescriptionKey != 0)
            yield return PartDescriptionKey;
    }

    /// <summary>
    /// Resolve a byte-index key into the actual <see cref="ResourceKey"/>
    /// from <see cref="TgiList"/>.  Returns null if the index is out of range.
    /// </summary>
    public ResourceKey? ResolveTgi(byte index)
    {
        return index < TgiList.Count ? TgiList[index] : null;
    }

    // ====================================================================
    //  BigEndianUnicode string helper (7-bit length prefix, BE-UTF16 body)
    // ====================================================================

    private static string ReadBigEndianUnicodeString(BinaryReader r)
    {
        // Read 7-bit encoded length (byte count of the string body).
        int byteLength = 0;
        int shift = 0;
        while (true)
        {
            byte b = r.ReadByte();
            byteLength |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        if (byteLength == 0) return string.Empty;
        byte[] bytes = r.ReadBytes(byteLength);
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    // ====================================================================
    //  Sub-types
    // ====================================================================

    /// <summary>
    /// A single CAS flag entry (category + value pair).
    /// </summary>
    public readonly record struct CasFlag(ushort Category, ushort Value);

    /// <summary>
    /// LOD asset metadata: sorting, specular level, shadow casting.
    /// </summary>
    public class LodAsset
    {
        public int Sorting { get; set; }
        public int SpecLevel { get; set; }
        public int CastShadow { get; set; }

        internal static LodAsset Read(BinaryReader r)
        {
            return new LodAsset
            {
                Sorting = r.ReadInt32(),
                SpecLevel = r.ReadInt32(),
                CastShadow = r.ReadInt32(),
            };
        }
    }

    /// <summary>
    /// A single LOD block containing level, asset metadata, and a list of
    /// byte-indices that point into the parent resource's TGI list.
    /// </summary>
    public class LodBlock
    {
        public byte Level { get; set; }
        public uint Unused { get; set; }
        public List<LodAsset> Assets { get; set; } = [];

        /// <summary>
        /// Byte-indices into the parent <see cref="CasPartResource.TgiList"/>.
        /// Each value can be resolved via <see cref="CasPartResource.ResolveTgi"/>.
        /// </summary>
        public List<byte> KeyIndices { get; set; } = [];

        internal static LodBlock Read(BinaryReader r)
        {
            var block = new LodBlock
            {
                Level = r.ReadByte(),
                Unused = r.ReadUInt32(),
            };

            byte assetCount = r.ReadByte();
            block.Assets = new List<LodAsset>(assetCount);
            for (int i = 0; i < assetCount; i++)
                block.Assets.Add(LodAsset.Read(r));

            byte keyCount = r.ReadByte();
            block.KeyIndices = new List<byte>(keyCount);
            for (int i = 0; i < keyCount; i++)
                block.KeyIndices.Add(r.ReadByte());

            return block;
        }
    }

    // ====================================================================
    //  Enums
    // ====================================================================

    [Flags]
    public enum ParmFlag : byte
    {
        DefaultForBodyType   = 1,
        DefaultThumbnailPart = 1 << 1,
        AllowForRandom       = 1 << 2,
        ShowInUI             = 1 << 3,
        ShowInSimInfoDemo    = 1 << 4,
        ShowInCASDemo        = 1 << 5,
    }

    [Flags]
    public enum AgeGenderFlags : uint
    {
        Baby       = 0x00000001,
        Toddler    = 0x00000002,
        Child      = 0x00000004,
        Teen       = 0x00000008,
        YoungAdult = 0x00000010,
        Adult      = 0x00000020,
        Elder      = 0x00000040,
        Male       = 0x00001000,
        Female     = 0x00002000,
    }

    [Flags]
    public enum ExcludePartFlag : ulong
    {
        None                              = 0,
        Hat                               = 1UL << 1,
        Hair                              = 1UL << 2,
        Head                              = 1UL << 3,
        Face                              = 1UL << 4,
        FullBody                          = 1UL << 5,
        UpperBody                         = 1UL << 6,
        LowerBody                         = 1UL << 7,
        Shoes                             = 1UL << 8,
        Accessories                       = 1UL << 9,
        Earrings                          = 1UL << 10,
        Glasses                           = 1UL << 11,
        Necklace                          = 1UL << 12,
        Gloves                            = 1UL << 13,
        WristLeft                         = 1UL << 14,
        WristRight                        = 1UL << 15,
        LipRingLeft                       = 1UL << 16,
        LipRingRight                      = 1UL << 17,
        NoseRingLeft                      = 1UL << 18,
        NoseRingRight                     = 1UL << 19,
        BrowRingLeft                      = 1UL << 20,
        BrowRingRight                     = 1UL << 21,
        IndexFingerLeft                   = 1UL << 22,
        IndexFingerRight                  = 1UL << 23,
        RingFingerLeft                    = 1UL << 24,
        RingFingerRight                   = 1UL << 25,
        MiddleFingerLeft                  = 1UL << 26,
        MiddleFingerRight                 = 1UL << 27,
        FacialHair                        = 1UL << 28,
        Lipstick                          = 1UL << 29,
        EyeShadow                         = 1UL << 30,
        Eyeliner                          = 1UL << 31,
        Blush                             = 1UL << 32,
        FacePaint                         = 1UL << 33,
        Eyebrows                          = 1UL << 34,
        EyeColor                          = 1UL << 35,
        Socks                             = 1UL << 36,
        Mascara                           = 1UL << 37,
        SkinDetailCreaseForehead          = 1UL << 38,
        SkinDetailFreckles                = 1UL << 39,
        SkinDetailDimpleLeft              = 1UL << 40,
        SkinDetailDimpleRight             = 1UL << 41,
        Tights                            = 1UL << 42,
        SkinDetailMoleLipLeft             = 1UL << 43,
        SkinDetailMoleLipRight            = 1UL << 44,
        TattooArmLowerLeft                = 1UL << 45,
        TattooArmUpperLeft                = 1UL << 46,
        TattooArmLowerRight               = 1UL << 47,
        TattooArmUpperRight               = 1UL << 48,
        TattooLegLeft                     = 1UL << 49,
        TattooLegRight                    = 1UL << 50,
        TattooTorsoBackLower              = 1UL << 51,
        TattooTorsoBackUpper              = 1UL << 52,
        TattooTorsoFrontLower             = 1UL << 53,
        TattooTorsoFrontUpper             = 1UL << 54,
        SkinDetailMoleCheekLeft           = 1UL << 55,
        SkinDetailMoleCheekRight          = 1UL << 56,
        SkinDetailCreaseMouth             = 1UL << 57,
    }
}
