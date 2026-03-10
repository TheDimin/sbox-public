namespace Sims4Reader;

/// <summary>
/// Known Sims 4 resource type identifiers.
/// Use <see cref="ResourceTypeExtensions.ToResourceType"/> for unknown uint values.
/// </summary>
public enum ResourceType : uint
{
    Unknown = 0x00000000,

    // Geometry / Mesh
    Geometry = 0x015A1849,
    GeometryList = 0xAC16FBEC,

    // RCOL chunks (embedded in RCOL containers)
    Model = 0x01661233,           // MODL
    MaterialDefinition = 0x01D0E75D, // MATD
    Tree = 0x021D7E8C,            // TREE
    TkMk = 0x033260E3,
    SlotAdjust = 0x0355E0A6,      // BOND
    Light = 0x03B4C61D,           // LITE
    Animation = 0x63A33EA7,       // ANIM
    Vpxy = 0x736884F1,            // VPXY
    Rslt = 0xD3044521,            // RSLT
    Footprint = 0xD382BF57,       // FTPT
    MaterialState = 0x02019972,   // MTST

    // Skeleton / Rig
    Rig = 0x8EAF13DE,
    Bone = 0x00AE6C67,
    DeformerMap = 0xDB43E069,

    // Character Appearance
    CasPart = 0x034AEECB,
    SimOutfit = 0x025ED6F4,
    SkinTone = 0x0354796A,
    StyleLook = 0x71BDB8A2,
    UserCasPreset = 0x0591B1AF,

    // Text / Strings
    StringTable = 0x220557DA,     // STBL

    // Images - DST (shuffled DXT)
    DstImage = 0x00B2D882,

    // Images - RLE compressed
    RleImage = 0x3453CF95,
    RleImageAlt = 0xBA856C78,

    // Images - Thumbnails
    Thumbnail_0D = 0x0D338A3A,
    Thumbnail_16 = 0x16CCF748,
    Thumbnail_3B = 0x3BD45407,
    Thumbnail_3C = 0x3C1AF1F2,
    Thumbnail_3C2 = 0x3C2A8647,
    Thumbnail_5B = 0x5B282D45,
    Thumbnail_CD = 0xCD9DE247,
    Thumbnail_E1 = 0xE18CAEE2,
    Thumbnail_E2 = 0xE254AE6E,
    Thumbnail_16C = 0x16CA6BC4,

    // Images - PNG/JPG
    Image_0580A2B4 = 0x0580A2B4,
    Image_0580A2B5 = 0x0580A2B5,
    Image_0580A2B6 = 0x0580A2B6,
    Image_0580A2CD = 0x0580A2CD,
    Image_0580A2CE = 0x0580A2CE,
    Image_0580A2CF = 0x0580A2CF,
    Image_0589DC44 = 0x0589DC44,
    Image_0589DC45 = 0x0589DC45,
    Image_0589DC46 = 0x0589DC46,
    Image_0589DC47 = 0x0589DC47,
    Image_05B17698 = 0x05B17698,
    Image_05B17699 = 0x05B17699,
    Image_05B1769A = 0x05B1769A,
    Image_05B1B524 = 0x05B1B524,
    Image_05B1B525 = 0x05B1B525,
    Image_05B1B526 = 0x05B1B526,
    Image_0668F635 = 0x0668F635,
    Image_2653E3C8 = 0x2653E3C8,
    Image_2653E3C9 = 0x2653E3C9,
    Image_2653E3CA = 0x2653E3CA,
    Image_2D4284F0 = 0x2D4284F0,
    Image_2D4284F1 = 0x2D4284F1,
    Image_2D4284F2 = 0x2D4284F2,
    Image_2E75C764 = 0x2E75C764,
    Image_2E75C765 = 0x2E75C765,
    Image_2E75C766 = 0x2E75C766,
    Image_2E75C767 = 0x2E75C767,
    Image_2F7D0002 = 0x2F7D0002,
    Image_2F7D0004 = 0x2F7D0004,
    Image_5DE9DBA0 = 0x5DE9DBA0,
    Image_5DE9DBA1 = 0x5DE9DBA1,
    Image_5DE9DBA2 = 0x5DE9DBA2,
    Image_626F60CC = 0x626F60CC,
    Image_626F60CD = 0x626F60CD,
    Image_626F60CE = 0x626F60CE,
    Image_6B6D837D = 0x6B6D837D,
    Image_6B6D837E = 0x6B6D837E,
    Image_6B6D837F = 0x6B6D837F,
    Image_9C925813 = 0x9C925813,
    Image_AD366F95 = 0xAD366F95,
    Image_AD366F96 = 0xAD366F96,
    Image_D84E7FC5 = 0xD84E7FC5,
    Image_D84E7FC6 = 0xD84E7FC6,
    Image_D84E7FC7 = 0xD84E7FC7,
    Image_FCEAB65B = 0xFCEAB65B,
    SkyBoxTexture = 0x71A449C9,

    // Catalog
    CatalogObject = 0x319E4F1D,     // COBJ
    CatalogFence = 0x0418FE2A,      // CFEN
    CatalogFloor = 0xB4F762C9,      // CFLR
    CatalogFlooring = 0x84C23219,   // CFLT
    CatalogFoundation = 0x2FAE983E, // CFND
    CatalogFreeze = 0xA057811C,     // CFRZ
    CatalogWall = 0xD5F0F921,       // CWAL
    CatalogBlock = 0x07936CE0,      // CBLK
    CatalogPlant = 0xA5DFFCF3,      // CPLT
    CatalogRoofStyle = 0x91EDBD3E,
    CatalogTerrain = 0xEBCBB16C,    // CTPT
    CatalogStair = 0x9F5CFF10,      // CSTL
    CatalogStairRail = 0x9A20CD1C,  // CSTR
    CatalogSpawn = 0x3F0C529A,      // CSPN
    CatalogA8 = 0xA8F7B517,
    Catalog48 = 0x48C28979,
    CatalogB0 = 0xB0311D0F,        // CRTR
    CatalogF1 = 0xF1EDBD86,        // CRPT
    CatalogE7 = 0xE7ADA79D,        // CFTR
    Catalog1C = 0x1C1CF1F7,        // CRAL
    CatalogStlr = 0x74050B1F,
    CatalogColorList = 0x1D6DF1CF,  // CCOL
    ObjectDefinition = 0xC0DB5AE7,

    // Misc
    NameMap = 0x0166038C,
    NgmpHashMap = 0xF3A38370,
    ObjKey = 0x02DC343F,
    Complate = 0x044AE110,
    Data = 0x545AC67A,
    Script = 0x073FAA07,
    Modular = 0xCF9A4ACE,
    TextureCompositor = 0x033A1435,
    TextureCompositorAlt = 0x0341ACC9,
    AudioEffect = 0xBDD82221,
    SimModifier = 0xC5F6763E,
    ModelTable = 0x81CA1A10,
    Timeline = 0xB0118C15,
    Trim = 0x76BCF80C,
    WorldColorTimeline = 0x19301120,
}

public static class ResourceTypeExtensions
{
    /// <summary>
    /// Convert a raw uint to ResourceType. Returns the typed enum if known, otherwise casts directly.
    /// </summary>
    public static ResourceType ToResourceType(this uint value)
    {
        return (ResourceType)value;
    }

    /// <summary>
    /// Returns true if this is a known thumbnail resource type.
    /// </summary>
    public static bool IsThumbnail(this ResourceType type)
    {
        return type is ResourceType.Thumbnail_0D or ResourceType.Thumbnail_16
            or ResourceType.Thumbnail_3B or ResourceType.Thumbnail_3C
            or ResourceType.Thumbnail_3C2 or ResourceType.Thumbnail_5B
            or ResourceType.Thumbnail_CD or ResourceType.Thumbnail_E1
            or ResourceType.Thumbnail_E2 or ResourceType.Thumbnail_16C;
    }

    /// <summary>
    /// Returns true if this is a known image resource type (PNG/JPG/DDS/RLE/DST).
    /// </summary>
    public static bool IsImage(this ResourceType type)
    {
        var val = (uint)type;
        return type == ResourceType.DstImage
            || type == ResourceType.RleImage
            || type == ResourceType.RleImageAlt
            || type == ResourceType.SkyBoxTexture
            || type.IsThumbnail()
            || (val >= 0x0580A2B4 && val <= 0x0580A2CF)
            || (val >= 0x0589DC44 && val <= 0x0589DC47)
            || (val >= 0x05B17698 && val <= 0x05B1769A)
            || (val >= 0x05B1B524 && val <= 0x05B1B526)
            || val == 0x0668F635
            || (val >= 0x2653E3C8 && val <= 0x2653E3CA)
            || (val >= 0x2D4284F0 && val <= 0x2D4284F2)
            || (val >= 0x2E75C764 && val <= 0x2E75C767)
            || val == 0x2F7D0002 || val == 0x2F7D0004
            || (val >= 0x5DE9DBA0 && val <= 0x5DE9DBA2)
            || (val >= 0x626F60CC && val <= 0x626F60CE)
            || (val >= 0x6B6D837D && val <= 0x6B6D837F)
            || val == 0x9C925813
            || val == 0xAD366F95 || val == 0xAD366F96
            || (val >= 0xD84E7FC5 && val <= 0xD84E7FC7)
            || val == 0xFCEAB65B;
    }
}
