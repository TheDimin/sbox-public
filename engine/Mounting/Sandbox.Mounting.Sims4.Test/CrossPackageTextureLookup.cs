using Sims4Reader;
using Sims4Reader.Image;

namespace Sims4MountTest;

/// <summary>
/// Cross-package texture lookup: find specific DstImage textures across all packages
/// and report their DST format (DST1/DXT1 vs DST5/DXT5).
/// </summary>
[TestClass]
public class CrossPackageTextureLookup
{
    [TestMethod]
    public void LookupPlantAndRugTextures()
    {
        // Texture instance IDs from the DiagnosePlantAndRugAlpha output
        var targetInstances = new HashSet<ulong>
        {
            // rugTUD2x1_01_set1 diffuse+alpha
            0x219404442C65501B,
            // treeAppleRural_01 diffuse mesh#0
            0xCEF61D5D38567956,
            // treeAppleRural_01 diffuse+alpha mesh#1
            0xFA17C94FE082D3B3,
            // rug3x2_SDX040GEN_set1 diffuse+alpha
            0x6047FBCEFF574CD4,
        };

        var packagePaths = TestHelper.GetAllPackagePaths();
        if (packagePaths.Count == 0)
            packagePaths = new List<string> { TestHelper.GetPackagePath() };

        int found = 0;

        foreach (var packagePath in packagePaths)
        {
            using var package = DbpfPackage.Open(packagePath);

            foreach (var entry in package.FindAll(ResourceType.DstImage))
            {
                if (!targetInstances.Contains(entry.Key.Instance))
                    continue;

                found++;
                try
                {
                    var dst = package.GetResource<DstImage>(entry);
                    Console.WriteLine($"FOUND: Instance=0x{entry.Key.Instance:X16} in {System.IO.Path.GetFileName(packagePath)}");
                    Console.WriteLine($"  Format:   {dst.Format}");
                    Console.WriteLine($"  Size:     {dst.Width}x{dst.Height}");
                    Console.WriteLine($"  Shuffled: {dst.IsShuffled}");
                    Console.WriteLine($"  HasAlpha: {dst.Format is FourCC.DST5 or FourCC.DXT5 or FourCC.DST3 or FourCC.DXT3}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FOUND BUT PARSE FAILED: 0x{entry.Key.Instance:X16}: {ex.Message}");
                }
            }
        }

        Console.WriteLine($"\nFound {found} of {targetInstances.Count} target textures.");

        // Also: sample the first 200 DstImage entries and tally DST1 vs DST5
        Console.WriteLine("\n=== DST FORMAT DISTRIBUTION (first package, first 500) ===");
        var formatCounts = new Dictionary<string, int>();
        using var mainPkg = DbpfPackage.Open(packagePaths[0]);
        foreach (var entry in mainPkg.FindAll(ResourceType.DstImage).Take(500))
        {
            try
            {
                var dst = mainPkg.GetResource<DstImage>(entry);
                var key = $"{dst.Format} ({(dst.Format is FourCC.DST5 or FourCC.DXT5 or FourCC.DST3 or FourCC.DXT3 ? "HAS ALPHA" : "NO ALPHA")})";
                formatCounts.TryGetValue(key, out var c);
                formatCounts[key] = c + 1;
            }
            catch { }
        }

        foreach (var kv in formatCounts.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {kv.Key}: {kv.Value}");
    }
}
