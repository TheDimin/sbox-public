// ============================================================================
// CobjReader — Parses decompressed COBJ (Catalog Object) resources (0x319E4F1D).
//
// COBJ links an OBJD to catalog metadata: price, tags, thumbnails, etc.
// Highly version-dependent — fields are gated on version thresholds.
// ============================================================================

using System;
using Sims4.Dbpf.Structures;

namespace Sims4.Dbpf.Readers;

// ---- Enums ------------------------------------------------------------------

public enum TopicType : uint
{
    NoEntry = 0, Environment = 1, Hunger = 2, Bladder = 3, Energy = 4,
    StressRelief = 5, Fun = 6, Hygiene = 7, Logic = 8, Charisma = 9,
    Cooking = 0xA, Athletic = 0xB, Painting = 0xC, Guitar = 0xD,
    Handiness = 0xE, GroupActivity = 0xF, Upgradable = 0x10,
    LearnCookFaster = 0x11, ChildOnly = 0x12, NoEntry2 = 0x13,
    Gardening = 0x14, Fishing = 0x15, SelfCleaning = 0x16,
    NeverBreaks = 0x17, Portable = 0x18, Speed = 0x19,
}

// ---- Sub-structures ---------------------------------------------------------

public readonly struct Tag32
{
    public readonly uint Category;
    public readonly uint Value;
    public Tag32(uint cat, uint val) { Category = cat; Value = val; }
}

public readonly struct TopicRating
{
    public readonly TopicType Topic;
    public readonly uint Rating;
    public TopicRating(TopicType topic, uint rating) { Topic = topic; Rating = rating; }
}

public readonly struct LocaleEntry
{
    public readonly byte LocaleId;
    public readonly uint NameHash;
    public readonly uint DescHash;
    public LocaleEntry(byte id, uint name, uint desc) { LocaleId = id; NameHash = name; DescHash = desc; }
}

public readonly struct StyleItem
{
    public readonly ResourceKey Key;
    public readonly uint Unknown;
    public StyleItem(ResourceKey key, uint unknown) { Key = key; Unknown = unknown; }
}

public readonly struct WallMaskEntry
{
    public readonly float BoundsMinX, BoundsMinZ, BoundsMaxX, BoundsMaxZ;
    public readonly uint LevelOffset;
    public readonly byte WallMaskIndex;

    public WallMaskEntry(float minX, float minZ, float maxX, float maxZ, uint level, byte idx)
    {
        BoundsMinX = minX; BoundsMinZ = minZ;
        BoundsMaxX = maxX; BoundsMaxZ = maxZ;
        LevelOffset = level; WallMaskIndex = idx;
    }
}

public sealed class AuralProperties
{
    public uint Version;
    public uint Hash1, Hash2, Hash3, Hash4;
    public byte Extra;
}

// ---- CatalogCommon ----------------------------------------------------------

public sealed class CatalogCommon
{
    public uint BlockVersion;
    public uint NameHash;
    public uint DescriptionHash;
    public uint SimoleonPrice;
    public ulong ThumbnailHash;
    public uint DevCategoryFlags;
    public LocaleEntry[] Locales = Array.Empty<LocaleEntry>();
    public StyleItem[] Styles = Array.Empty<StyleItem>();
    public AuralProperties? Aural;
    public uint BuildBuyStatusFlags;
    public ulong PackNameHash;
    public ulong PackDescHash;
    public byte PackIconIndex;
}

// ---- CobjResource -----------------------------------------------------------

public sealed class CobjResource
{
    public uint Version;
    public CatalogCommon Common = new();

    // Tags
    public Tag32[] Tags = Array.Empty<Tag32>();

    // Object properties
    public ushort SelectionGroup;
    public ushort ObjectType;
    public uint ObjectTypeFlags;
    public uint WallPlacementFlags;
    public uint MovementFlags;
    public uint CutoutTilesPerLevel;
    public uint NumLevels;
    public WallMaskEntry[] WallMasks = Array.Empty<WallMaskEntry>();
    public byte ScriptEnabled;
    public uint DiagonalIndex;
    public uint AmbienceTypeHash;

    // Sort flags
    public uint RoomCategoryFlags;
    public uint FunctionCategoryFlags;
    public ulong SubCategoryFlags;
    public ulong SubRoomFlags;
    public uint BuildCategoryFlags;
    public uint SlotPlacementFlags;

    // Strings
    public string SurfaceType = string.Empty;
    public string SourceMaterial = string.Empty;

    // Moodlet
    public uint MoodletGiven;
    public uint MoodletScore;
    public TopicRating[] TopicRatings = Array.Empty<TopicRating>();

    // Version-dependent
    public uint FallbackIndex;
    public uint ModularArchEndEastIndex;
    public uint ModularArchEndWestIndex;
    public uint ModularArchConnectingIndex;
    public uint ModularArchSingleIndex;

    // TGI references
    public ResourceKey[] TgiReferences = Array.Empty<ResourceKey>();
}

// ---- Reader -----------------------------------------------------------------

public static class CobjReader
{
    public static CobjResource Read(ReadOnlySpan<byte> data)
    {
        var r = new SpanReader(data);
        var cobj = new CobjResource();

        cobj.Version = r.ReadU32();

        // ---- CatalogCommon ----
        ReadCommon(ref r, cobj.Common);

        // ---- Tags ----
        int tagCount = (int)r.ReadU32();
        cobj.Tags = new Tag32[tagCount];
        for (int i = 0; i < tagCount; i++)
            cobj.Tags[i] = new Tag32(r.ReadU32(), r.ReadU32());

        // ---- Object properties ----
        cobj.SelectionGroup = r.ReadU16();
        cobj.ObjectType = r.ReadU16();
        cobj.ObjectTypeFlags = r.ReadU32();
        cobj.WallPlacementFlags = r.ReadU32();
        cobj.MovementFlags = r.ReadU32();
        cobj.CutoutTilesPerLevel = r.ReadU32();
        cobj.NumLevels = r.ReadU32();

        int wallMaskCount = r.ReadU8();
        cobj.WallMasks = new WallMaskEntry[wallMaskCount];
        for (int i = 0; i < wallMaskCount; i++)
        {
            float minX = r.ReadFloat(), minZ = r.ReadFloat();
            float maxX = r.ReadFloat(), maxZ = r.ReadFloat();
            uint level = r.ReadU32();
            byte idx = r.ReadU8();
            cobj.WallMasks[i] = new WallMaskEntry(minX, minZ, maxX, maxZ, level, idx);
        }

        cobj.ScriptEnabled = r.ReadU8();
        cobj.DiagonalIndex = r.ReadU32();
        cobj.AmbienceTypeHash = r.ReadU32();

        // ---- Sort flags ----
        cobj.RoomCategoryFlags = r.ReadU32();
        cobj.FunctionCategoryFlags = r.ReadU32();
        cobj.SubCategoryFlags = r.ReadU64();
        cobj.SubRoomFlags = r.ReadU64();
        cobj.BuildCategoryFlags = r.ReadU32();
        cobj.SlotPlacementFlags = r.ReadU32();

        // ---- Strings ----
        cobj.SurfaceType = r.Read7BitString();
        cobj.SourceMaterial = r.Read7BitString();

        // ---- Moodlet ----
        cobj.MoodletGiven = r.ReadU32();
        cobj.MoodletScore = r.ReadU32();

        int topicCount = (int)r.ReadU32();
        cobj.TopicRatings = new TopicRating[topicCount];
        for (int i = 0; i < topicCount; i++)
            cobj.TopicRatings[i] = new TopicRating((TopicType)r.ReadU32(), r.ReadU32());

        // ---- Version-dependent fields ----
        if (cobj.Version >= 0x0E)
            cobj.FallbackIndex = r.ReadU32();

        if (cobj.Version >= 0x10)
        {
            cobj.ModularArchEndEastIndex = r.ReadU32();
            cobj.ModularArchEndWestIndex = r.ReadU32();
            cobj.ModularArchConnectingIndex = r.ReadU32();
            cobj.ModularArchSingleIndex = r.ReadU32();
        }

        if (cobj.Version >= 0x12 && r.Remaining >= 1)
            r.Skip(1); // unknown1

        if (cobj.Version >= 0x13 && r.Remaining >= 4)
            r.Skip(4); // unknown2

        if (cobj.Version >= 0x16 && r.Remaining >= 8)
            r.Skip(8); // unknown3

        if (cobj.Version >= 0x19 && r.Remaining >= 4)
            r.Skip(4); // unknown4

        // ---- TGI References ----
        if (r.Remaining >= 1)
        {
            int tgiCount = r.ReadU8();
            cobj.TgiReferences = new ResourceKey[tgiCount];
            for (int i = 0; i < tgiCount; i++)
                cobj.TgiReferences[i] = ResourceKey.ReadITG(ref r);
        }

        return cobj;
    }

    private static void ReadCommon(ref SpanReader r, CatalogCommon c)
    {
        c.BlockVersion = r.ReadU32();
        c.NameHash = r.ReadU32();
        c.DescriptionHash = r.ReadU32();
        c.SimoleonPrice = r.ReadU32();
        c.ThumbnailHash = r.ReadU64();
        c.DevCategoryFlags = r.ReadU32();

        // Locale overrides (version >= 0x09)
        if (c.BlockVersion >= 0x09)
        {
            int localeCount = r.ReadU16();
            c.Locales = new LocaleEntry[localeCount];
            for (int i = 0; i < localeCount; i++)
                c.Locales[i] = new LocaleEntry(r.ReadU8(), r.ReadU32(), r.ReadU32());
        }

        // Style items
        int styleCount = r.ReadU16();
        c.Styles = new StyleItem[styleCount];
        for (int i = 0; i < styleCount; i++)
        {
            var key = ResourceKey.ReadITG(ref r);
            uint unk = r.ReadU32();
            c.Styles[i] = new StyleItem(key, unk);
        }

        // Aural properties (version >= 0x0B)
        if (c.BlockVersion >= 0x0B)
        {
            var aural = new AuralProperties();
            aural.Version = r.ReadU32();
            aural.Hash1 = r.ReadU32();
            aural.Hash2 = r.ReadU32();
            if (aural.Version >= 0x02) aural.Hash3 = r.ReadU32();
            if (aural.Version >= 0x03) aural.Hash4 = r.ReadU32();
            if (aural.Version >= 0x04) aural.Extra = r.ReadU8();
            c.Aural = aural;
        }

        c.BuildBuyStatusFlags = r.ReadU32();
        c.PackNameHash = r.ReadU64();
        c.PackDescHash = r.ReadU64();

        if (c.BlockVersion >= 0x0C)
            c.PackIconIndex = r.ReadU8();
    }
}
