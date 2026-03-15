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
}
