namespace Sims4Reader.Resources;

/// <summary>
/// Maps CatalogTag values to buy/build category folder names.
///
/// Tag values come from the TS4 Tag enum (CommonGameTag) as defined in the
/// game's tag tuning (S4_03B33DDF). Tags use two levels:
///   - TagCategory: the high-level category (e.g. BuyCatEE=86, BuyCatLD=88)
///   - Tag: specific subcategory value (e.g. BuyCatLD_TableLamp=203)
///
/// We match on the specific Tag values for granular categorization, with
/// fallback ranges for the core buy mode sections.
///
/// Source: Sims4Tools S4_03B33DDF_00000000_D89CB9186B79ACB7.xml
/// </summary>
public static class BuyCategoryTag
{
    /// <summary>
    /// Get a folder-friendly category name for a tag value.
    /// Tries specific subcategory tags first, then falls back to range-based matching.
    /// Returns null if the tag is not a known buy/build category.
    /// </summary>
    public static string? GetCategoryName(ushort tagValue) => tagValue switch
    {
        // === BUY_CAT_EE: Electronics & Entertainment (161-179, plus expansions) ===
        161 => "electronics",          // BuyCatEE_TV
        162 => "electronics",          // BuyCatEE_Computer
        163 => "electronics",          // BuyCatEE_Audio
        164 => "electronics",          // BuyCatEE_TVSets
        165 or 166 => "activities",    // BuyCatEE_HobbySkill, Party
        167 or 168 or 179 => "kids",   // BuyCatEE_KidFurniture, KidToy, MiscKids
        169 => "electronics",          // BuyCatEE_Alarm
        171 => "decor",                // BuyCatEE_Clock
        172 => "kids",                 // BuyCatEE_Toddlers
        173 => "activities",           // BuyCatEE_IndoorActivity
        174 => "kids",                 // BuyCatEE_KidActivity
        175 => "outdoor",              // BuyCatEE_OutdoorActivity
        176 => "appliances",           // BuyCatEE_Bar
        177 => "electronics",          // BuyCatEE_MiscElectronics
        178 => "activities",           // BuyCatEE_MiscEntertainment
        456 or 457 or 458 => "activities", // Basketball, Chess, MonkeyBars
        968 or 969 or 970 => "activities", // CreativeActivity, KnowledgeActivity, ActiveActivity
        1122 => "electronics",         // BuyCatEE_TVStand
        1944 or 1948 or 2014 => "pets", // PetToys, PetMisc, PetActivityToys
        1947 => "pets",                // BuyCatEE_PetVet
        2075 => "outdoor",             // BuyCatEE_Gardening
        2237 => "vehicles",            // BuyCatEE_Transportation
        55356 => "electronics",        // BuyCatEE_VideoGameConsole

        // === BUY_CAT_PA: Plumbing & Appliances (180-193, plus expansions) ===
        180 or 182 or 920 => "plumbing",   // Sink, SinkFreestanding, SinkCounter
        181 => "plumbing",                  // Toilet
        183 or 184 => "plumbing",           // Shower, Tub
        185 => "appliances",                // LargeAppliance
        186 or 191 or 193 => "appliances",  // SmallAppliance, MiscSmallAppliance, MiscAppliance
        187 => "appliances",                // Stove
        188 => "appliances",                // Disposable
        189 => "appliances",                // Refrigerator
        190 => "outdoor",                   // OutdoorCooking
        192 => "plumbing",                  // MiscPlumbing
        913 => "appliances",                // StoveHood
        966 or 967 => "appliances",         // CoffeeMaker, Microwave
        972 or 973 => "appliances",         // DisposalIndoor, DisposalOutdoor
        1945 => "pets",                     // BuyCatPA_PetCare
        1976 => "pets",                     // BuyCatPA_PetFood
        1978 => "pets",                     // BuyCatPA_LitterBox
        2042 => "plumbing",                 // PublicRestroom

        // === BUY_CAT_LD: Lighting & Decor (194-209, plus expansions) ===
        194 => "decor",            // BathroomAccent
        195 => "outdoor",          // LawnOrnament
        196 => "kids",             // KidDecoration
        197 => "decor",            // WindowTreatment
        198 => "decor",            // Rug
        199 => "outdoor",          // FountainDecoration
        200 => "decor",            // Sculpture
        201 => "decor",            // WallDecoration
        202 => "plants",           // Plant
        203 => "lights",           // TableLamp
        204 => "lights",           // FloorLamp
        205 => "lights",           // CeilingLight
        206 => "lights",           // OutdoorLight
        207 => "decor",            // Mirror
        208 => "lights",           // MiscLight
        209 => "decor",            // MiscDecoration
        231 => "outdoor",          // FountainEmitter
        252 => "outdoor",          // FountainObjects
        310 => "lights",           // WallLight
        785 => "decor",            // Fireplace
        823 => "decor",            // Clutter
        824 => "decor",            // WallSculpture
        964 or 965 => "decor",     // MirrorWall, MirrorFreestanding
        978 or 979 => "decor",     // CurtainBlind, Awning
        1228 or 1246 => "outdoor", // PoolObjects, PoolDecorations
        1496 => "decor",           // RugManaged
        1718 => "lights",          // NightLight
        2188 => "decor",           // CeilingDecoration
        2211 => "outdoor",         // PoolObjectsInventoryable

        // === BUY_CAT_SS: Seating & Surfaces (210-230, plus expansions) ===
        210 => "furniture",        // Counter
        211 => "furniture",        // Cabinet
        212 or 962 or 963 => "furniture", // DiningTable, DiningTableShort, DiningTableLong
        213 => "furniture",        // EndTable
        214 => "furniture",        // CoffeeTable
        215 => "furniture",        // Desk
        216 => "furniture",        // Display
        217 => "furniture",        // DiningChair
        218 => "furniture",        // Sofa
        219 => "furniture",        // LoveSeat
        220 => "outdoor",          // OutdoorChair
        221 => "furniture",        // LivingChair
        222 => "furniture",        // DeskChair
        223 or 916 or 917 => "outdoor", // OutdoorSeating, OutdoorBench, OutdoorTable
        224 => "furniture",        // Barstool
        225 or 914 or 971 => "furniture", // Bed, BedDouble, BedSingle
        226 => "furniture",        // Bookshelf
        227 => "furniture",        // Dresser
        228 => "furniture",        // MiscSurface
        229 => "furniture",        // MiscComfort
        230 => "furniture",        // MiscStorage
        1071 or 1072 => "decor",   // PostcardBoard, ElementDisplay
        1123 or 1126 => "furniture", // AccentTable, HallwayTable
        1946 or 1977 or 1979 => "pets", // PetFurniture, PetBed, ScratchingPost

        // === BUY_CAT_VO: Venue Objects (tag category 90) ===
        // (venue-specific objects go to their functional category)

        // === BUY_CAT_MAG: Magazine/Room categories (270-276, plus expansions) ===
        270 => "furniture",        // BuyCatMAG_LivingRoom
        271 => "plumbing",         // BuyCatMAG_Bathroom
        272 => "furniture",        // BuyCatMAG_Bedroom
        273 => "furniture",        // BuyCatMAG_DiningRoom
        274 => "appliances",       // BuyCatMAG_Kitchen
        275 => "outdoor",          // BuyCatMAG_Outdoor
        276 => "furniture",        // BuyCatMAG_Study
        407 => "objects",          // BuyCatMAG_Misc
        468 => "objects",          // BuyCatMAG_Career
        864 => "kids",             // BuyCatMAG_Kids

        // === BUILD mode items (535-561, plus expansions) ===
        535 => "building",         // Door
        536 => "building",         // Window
        537 => "building",         // Gate
        538 => "building",         // Column
        539 or 540 => "building",  // RoofAttachment, Roof
        541 => "building",         // FloorPattern
        542 => "building",         // WallPattern
        543 => "building",         // RoofPattern
        544 => "building",         // Fence
        545 => "building",         // Spandrel
        546 => "building",         // Stair
        547 => "building",         // Railing
        548 => "building",         // Block
        549 => "building",         // Style
        550 => "building",         // Frieze
        551 => "building",         // RoofTrim
        552 => "building",         // Foundation
        554 => "building",         // FloorTrim
        555 => "building",         // WallAttachment
        556 => "plants",           // Flower
        557 => "plants",           // Shrub
        558 => "plants",           // Tree
        559 => "decor",            // Rug (build mode)
        560 => "outdoor",          // Rock
        561 => "building",         // Arch
        653 => "building",         // WallTool
        782 => "building",         // Post
        787 => "outdoor",          // Buy_World_Objects
        906 => "building",         // RoofDiagonal
        915 => "building",         // GateDouble
        918 => "building",         // DoorDouble
        919 => "building",         // RoofChimney
        974 => "building",         // DoorSingle
        975 => "building",         // RoofAttachmentMisc
        976 => "building",         // GateSingle
        977 => "building",         // RoofOrthogonal
        981 => "decor",            // WeddingArch
        1062 or 1063 or 1064 or 1070 => "building", // Deck, WithWalls, NoWalls, Diagonal
        1065 or 1066 => "plants",  // Bush, Cactus
        1067 or 1068 or 1069 => "plants", // GroundCover, FlowerBush, FlowerMisc
        1081 => "building",        // FountainTrim
        1226 or 1227 => "building", // Pool, PoolTool
        1441 or 1442 => "building", // HalfWall, HalfWallTrim
        1611 => "building",        // Elevator
        2425 => "building",        // Ladder

        // === Off-The-Grid (OTG) ===
        2380 => "appliances",      // OTG_Appliances
        2381 => "activities",      // OTG_Crafting
        2382 => "lights",          // OTG_Lighting
        2384 => "outdoor",         // OTG_OutdoorActivities
        2385 => "plumbing",        // OTG_Plumbing

        // === Collections ===
        >= 1041 and <= 1053 => "collectibles", // Collection items
        1159 => "collectibles",    // Collection_Gardening
        2043 => "collectibles",    // Collection_Treasure

        // === Holiday ===
        2084 or 2085 => "decor",   // Holiday_All, Holiday_Decor_All

        _ => null,
    };

    /// <summary>
    /// Get a specific subcategory name for a tag value (e.g. "TV", "TableLamp", "DiningChair").
    /// Returns null if the tag is not a known buy/build subcategory.
    /// </summary>
    public static string? GetSubCategoryName(ushort tagValue) => tagValue switch
    {
        // === BUY_CAT_EE: Electronics & Entertainment ===
        161 => "TV",
        162 => "Computer",
        163 => "Audio",
        164 => "TVSets",
        165 => "HobbySkill",
        166 => "Party",
        167 => "KidFurniture",
        168 => "KidToy",
        169 => "Alarm",
        171 => "Clock",
        172 => "Toddlers",
        173 => "IndoorActivity",
        174 => "KidActivity",
        175 => "OutdoorActivity",
        176 => "Bar",
        177 => "MiscElectronics",
        178 => "MiscEntertainment",
        179 => "MiscKids",
        456 => "Basketball",
        457 => "Chess",
        458 => "MonkeyBars",
        968 => "CreativeActivity",
        969 => "KnowledgeActivity",
        970 => "ActiveActivity",
        1122 => "TVStand",
        1944 => "PetToys",
        1947 => "PetVet",
        1948 => "PetMisc",
        2014 => "PetActivityToys",
        2075 => "Gardening",
        2237 => "Transportation",
        55356 => "VideoGameConsole",

        // === BUY_CAT_PA: Plumbing & Appliances ===
        180 => "Sink",
        181 => "Toilet",
        182 => "SinkFreestanding",
        183 => "Shower",
        184 => "Tub",
        185 => "LargeAppliance",
        186 => "SmallAppliance",
        187 => "Stove",
        188 => "Disposable",
        189 => "Refrigerator",
        190 => "OutdoorCooking",
        191 => "MiscSmallAppliance",
        192 => "MiscPlumbing",
        193 => "MiscAppliance",
        913 => "StoveHood",
        920 => "SinkCounter",
        966 => "CoffeeMaker",
        967 => "Microwave",
        972 => "DisposalIndoor",
        973 => "DisposalOutdoor",
        1945 => "PetCare",
        1976 => "PetFood",
        1978 => "LitterBox",
        2042 => "PublicRestroom",

        // === BUY_CAT_LD: Lighting & Decor ===
        194 => "BathroomAccent",
        195 => "LawnOrnament",
        196 => "KidDecoration",
        197 => "WindowTreatment",
        198 => "Rug",
        199 => "FountainDecoration",
        200 => "Sculpture",
        201 => "WallDecoration",
        202 => "Plant",
        203 => "TableLamp",
        204 => "FloorLamp",
        205 => "CeilingLight",
        206 => "OutdoorLight",
        207 => "Mirror",
        208 => "MiscLight",
        209 => "MiscDecoration",
        231 => "FountainEmitter",
        252 => "FountainObjects",
        310 => "WallLight",
        785 => "Fireplace",
        823 => "Clutter",
        824 => "WallSculpture",
        964 => "MirrorWall",
        965 => "MirrorFreestanding",
        978 => "CurtainBlind",
        979 => "Awning",
        1228 => "PoolObjects",
        1246 => "PoolDecorations",
        1496 => "RugManaged",
        1718 => "NightLight",
        2188 => "CeilingDecoration",
        2211 => "PoolObjectsInventoryable",

        // === BUY_CAT_SS: Seating & Surfaces ===
        210 => "Counter",
        211 => "Cabinet",
        212 => "DiningTable",
        213 => "EndTable",
        214 => "CoffeeTable",
        215 => "Desk",
        216 => "Display",
        217 => "DiningChair",
        218 => "Sofa",
        219 => "LoveSeat",
        220 => "OutdoorChair",
        221 => "LivingChair",
        222 => "DeskChair",
        223 => "OutdoorSeating",
        224 => "Barstool",
        225 => "Bed",
        226 => "Bookshelf",
        227 => "Dresser",
        228 => "MiscSurface",
        229 => "MiscComfort",
        230 => "MiscStorage",
        914 => "BedDouble",
        916 => "OutdoorBench",
        917 => "OutdoorTable",
        962 => "DiningTableShort",
        963 => "DiningTableLong",
        971 => "BedSingle",
        1071 => "PostcardBoard",
        1072 => "ElementDisplay",
        1123 => "AccentTable",
        1126 => "HallwayTable",
        1946 => "PetFurniture",
        1977 => "PetBed",
        1979 => "ScratchingPost",

        // === BUY_CAT_MAG: Magazine/Room categories ===
        270 => "LivingRoom",
        271 => "Bathroom",
        272 => "Bedroom",
        273 => "DiningRoom",
        274 => "Kitchen",
        275 => "Outdoor",
        276 => "Study",
        407 => "Misc",
        468 => "Career",
        864 => "Kids",

        // === BUILD mode items ===
        535 => "Door",
        536 => "Window",
        537 => "Gate",
        538 => "Column",
        539 => "RoofAttachment",
        540 => "Roof",
        541 => "FloorPattern",
        542 => "WallPattern",
        543 => "RoofPattern",
        544 => "Fence",
        545 => "Spandrel",
        546 => "Stair",
        547 => "Railing",
        548 => "Block",
        549 => "Style",
        550 => "Frieze",
        551 => "RoofTrim",
        552 => "Foundation",
        554 => "FloorTrim",
        555 => "WallAttachment",
        556 => "Flower",
        557 => "Shrub",
        558 => "Tree",
        559 => "Rug",
        560 => "Rock",
        561 => "Arch",
        653 => "WallTool",
        782 => "Post",
        787 => "WorldObjects",
        906 => "RoofDiagonal",
        915 => "GateDouble",
        918 => "DoorDouble",
        919 => "RoofChimney",
        974 => "DoorSingle",
        975 => "RoofAttachmentMisc",
        976 => "GateSingle",
        977 => "RoofOrthogonal",
        981 => "WeddingArch",
        1062 => "Deck",
        1063 => "DeckWithWalls",
        1064 => "DeckNoWalls",
        1065 => "Bush",
        1066 => "Cactus",
        1067 => "GroundCover",
        1068 => "FlowerBush",
        1069 => "FlowerMisc",
        1070 => "DeckDiagonal",
        1081 => "FountainTrim",
        1226 => "Pool",
        1227 => "PoolTool",
        1441 => "HalfWall",
        1442 => "HalfWallTrim",
        1611 => "Elevator",
        2425 => "Ladder",

        // === Off-The-Grid ===
        2380 => "OTG_Appliances",
        2381 => "OTG_Crafting",
        2382 => "OTG_Lighting",
        2384 => "OTG_OutdoorActivities",
        2385 => "OTG_Plumbing",

        // === Collections ===
        1041 => "Collection",
        1159 => "Collection_Gardening",
        2043 => "Collection_Treasure",

        // === Holiday ===
        2084 => "Holiday_All",
        2085 => "Holiday_Decor",

        _ => null,
    };

    /// <summary>
    /// Get the high-level tag category group name from a Category field ID.
    /// These are the group IDs used in the Category field of CatalogTag.
    /// </summary>
    public static string? GetTagCategoryGroupName(ushort categoryId) => categoryId switch
    {
        86 => "Electronics & Entertainment",
        87 => "Plumbing & Appliances",
        88 => "Lighting & Decor",
        89 => "Seating & Surfaces",
        90 => "Venue Objects",
        91 => "Magazine",
        92 => "Build",
        93 => "Styles",
        94 => "Collections",
        95 => "Off-The-Grid",
        96 => "Holiday",
        _ => null,
    };

    /// <summary>
    /// Get a user-friendly tag string like "Lighting: TableLamp" or "Seating: Sofa".
    /// Falls back to partial info if only category or value is known, returns null if neither resolves.
    /// </summary>
    public static string? GetFriendlyTagName(CatalogTag tag)
    {
        var groupName = GetTagCategoryGroupName(tag.Category);
        var subName = GetSubCategoryName(tag.Value);

        if (groupName != null && subName != null)
            return $"{groupName}: {subName}";
        if (groupName != null)
            return groupName;
        if (subName != null)
            return subName;

        return null;
    }

    /// <summary>
    /// Determine the best category name from a set of catalog tags.
    /// Checks both Category and Value fields of each tag, returning
    /// the first matching buy/build category.
    /// </summary>
    public static string? GetCategory(CatalogTag[] tags)
    {
        foreach (var tag in tags)
        {
            var name = GetCategoryName(tag.Category);
            if (name != null)
                return name;

            name = GetCategoryName(tag.Value);
            if (name != null)
                return name;
        }
        return null;
    }

    /// <summary>
    /// Determine the best subcategory name from a set of catalog tags.
    /// Returns the specific item type (e.g. "TableLamp", "DiningChair").
    /// </summary>
    public static string? GetSubCategory(CatalogTag[] tags)
    {
        foreach (var tag in tags)
        {
            var name = GetSubCategoryName(tag.Category);
            if (name != null)
                return name;

            name = GetSubCategoryName(tag.Value);
            if (name != null)
                return name;
        }
        return null;
    }
}
