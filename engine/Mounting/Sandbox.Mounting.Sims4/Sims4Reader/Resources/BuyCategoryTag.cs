namespace Sims4Reader.Resources;

/// <summary>
/// Maps CatalogTag values to buy/build category folder names.
///
/// Tag values come from the TS4 Tag enum (CommonGameTag). Each tag is an integer
/// identifying a specific catalog subcategory. Related subcategories share a
/// contiguous range that maps to one buy mode section:
///
///   BUY_CAT_EE (Electronics/Entertainment): 161–179  (0x00A1–0x00B3)
///   BUY_CAT_PA (Plumbing/Appliances):      180–193  (0x00B4–0x00C1)
///   BUY_CAT_LD (Lighting/Decor):            194–209  (0x00C2–0x00D1)
///   BUY_CAT_SS (Seating/Surfaces):          210–230  (0x00D2–0x00E6)
///   BUILD_* (Build mode items):             535–561  (0x0217–0x0231)
/// </summary>
public static class BuyCategoryTag
{
    /// <summary>
    /// Get a folder-friendly category name for a tag value.
    /// Returns null if the tag is not a known buy/build category.
    /// </summary>
    public static string? GetCategoryName(ushort tagValue) => tagValue switch
    {
        >= 161 and <= 179 => "electronics",  // BUY_CAT_EE
        >= 180 and <= 193 => "appliances",   // BUY_CAT_PA
        >= 194 and <= 209 => "lighting",     // BUY_CAT_LD
        >= 210 and <= 230 => "furniture",    // BUY_CAT_SS
        >= 535 and <= 561 => "building",     // BUILD_*
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
