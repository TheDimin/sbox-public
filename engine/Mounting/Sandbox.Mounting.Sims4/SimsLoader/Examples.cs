// ============================================================================
// Usage Examples — How to use the Sims4.Dbpf library.
//
// These are not runnable in this environment (no dotnet SDK), but demonstrate
// the API patterns you'd use in your own project.
// ============================================================================

using Sims4.Dbpf;
using Sims4.Dbpf.Enums;
using Sims4.Dbpf.Readers;
using Sims4.Dbpf.Resources;
using Sims4.Dbpf.Structures;

// ============================================================================
// Example 1: Open a package and list all resources
// ============================================================================
static void ListResources(string packagePath)
{
    using var package = DbpfPackage.Open(packagePath);

    Console.WriteLine(package.Header);
    Console.WriteLine($"Total resources: {package.Count}");

    foreach (ref readonly DbpfEntry entry in package.Entries.AsSpan())
    {
        Console.WriteLine($"  {entry}");
    }
}

// ============================================================================
// Example 2: Extract all string tables
// ============================================================================
static void DumpStringTables(string packagePath)
{
    using var package = DbpfPackage.Open(packagePath);

    foreach (ref readonly DbpfEntry entry in package.EntriesOfType(ResourceType.STBL))
    {
        StblResource stbl = package.ReadStbl(in entry);

        Console.WriteLine($"STBL {entry.Instance:X16} — {stbl.Entries.Length} strings:");
        foreach (ref readonly StblEntry str in stbl.Entries.AsSpan())
        {
            Console.WriteLine($"  0x{str.Key:X8} = {str.Value}");
        }
    }
}

// ============================================================================
// Example 3: Read GEOM mesh data (vertices, faces, bones)
// ============================================================================
static void InspectGeometry(string packagePath)
{
    using var package = DbpfPackage.Open(packagePath);

    foreach (ref readonly DbpfEntry entry in package.EntriesOfType(ResourceType.GEOM))
    {
        GeomResource geom = package.ReadGeom(in entry);

        Console.WriteLine($"GEOM v{geom.Version}: {geom.VertexCount} verts, stride={geom.VertexStride}");
        Console.WriteLine($"  Elements: {geom.Elements.Length}");
        foreach (var elem in geom.Elements)
            Console.WriteLine($"    {elem.Type} — {elem.ByteSize} bytes (sub={elem.SubType})");

        Console.WriteLine($"  Face groups: {geom.FaceGroups.Length}");
        foreach (var fg in geom.FaceGroups)
            Console.WriteLine($"    {fg.TriangleCount} triangles ({fg.IndexCount} indices, {fg.BytesPerIndex} bytes/idx)");

        Console.WriteLine($"  Bones: {geom.BoneHashes.Length}");
        Console.WriteLine($"  UV map entries: {geom.UvMapEntries.Length}");
        Console.WriteLine($"  Seam stitches: {geom.SeamStitchCount}");
        Console.WriteLine($"  TGI refs: {geom.TgiReferences.Length}");

        // If standard 64-byte layout, you can get typed vertex access:
        if (geom.VertexStride == 64)
        {
            ReadOnlySpan<Vertex64> verts = geom.GetVertices64();
            Console.WriteLine($"  First vertex: pos={verts[0].Position}, norm={verts[0].Normal}");
        }
    }
}

// ============================================================================
// Example 4: Read catalog objects (COBJ) — price, tags, categories
// ============================================================================
static void DumpCatalogObjects(string packagePath)
{
    using var package = DbpfPackage.Open(packagePath);

    foreach (ref readonly DbpfEntry entry in package.EntriesOfType(ResourceType.COBJ))
    {
        CobjResource cobj = package.ReadCobj(in entry);

        Console.WriteLine($"COBJ v{cobj.Version}: §{cobj.Common.SimoleonPrice}");
        Console.WriteLine($"  Name hash: 0x{cobj.Common.NameHash:X8}");
        Console.WriteLine($"  Tags: {cobj.Tags.Length}");
        foreach (var tag in cobj.Tags)
            Console.WriteLine($"    Cat=0x{tag.Category:X8} Val=0x{tag.Value:X8}");
        Console.WriteLine($"  Surface: {cobj.SurfaceType}");
        Console.WriteLine($"  Material: {cobj.SourceMaterial}");
        Console.WriteLine($"  TGI refs: {cobj.TgiReferences.Length}");
    }
}

// ============================================================================
// Example 5: Zero-copy raw data access (for custom parsers)
// ============================================================================
static void RawAccess(string packagePath)
{
    using var package = DbpfPackage.Open(packagePath);

    // Look up by TGI key
    var key = new ResourceKey(ResourceType.STBL, 0x80000000, 0x001234567890ABCD);
    if (package.TryGetEntry(key, out DbpfEntry entry))
    {
        // Get decompressed data (pooled buffer for compressed, zero-copy for raw)
        using ResourceData data = package.GetResourceData(in entry);
        ReadOnlySpan<byte> bytes = data.Span;

        // Feed to your own parser...
        Console.WriteLine($"Resource is {bytes.Length} bytes decompressed");
    }
}

// ============================================================================
// Example 6: Efficient type filtering without LINQ allocations
// ============================================================================
static void CountByType(string packagePath)
{
    using var package = DbpfPackage.Open(packagePath);

    // Allocation-free enumeration
    int geomCount = package.CountOf(ResourceType.GEOM);
    int stblCount = package.CountOf(ResourceType.STBL);
    int cobjCount = package.CountOf(ResourceType.COBJ);

    Console.WriteLine($"GEOM: {geomCount}, STBL: {stblCount}, COBJ: {cobjCount}");
}
