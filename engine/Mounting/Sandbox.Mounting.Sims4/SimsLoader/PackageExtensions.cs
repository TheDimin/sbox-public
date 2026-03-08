// ============================================================================
// PackageExtensions — Convenience methods that compose DbpfPackage with
// the resource-specific readers for one-call ergonomics.
// ============================================================================

using System;
using Sims4.Dbpf.Enums;
using Sims4.Dbpf.Readers;
using Sims4.Dbpf.Resources;
using Sims4.Dbpf.Structures;

namespace Sims4.Dbpf;

public static class PackageExtensions
{
    /// <summary>
    /// Decompresses and parses a GEOM resource from the given entry.
    /// The returned <see cref="GeomResource"/> holds references into a pooled buffer
    /// that is managed by the caller (the ResourceData must be kept alive).
    /// </summary>
    /// <remarks>
    /// For zero-copy usage, keep the ResourceData alive:
    /// <code>
    /// using var data = package.GetResourceData(in entry);
    /// var geom = GeomReader.Read(data.Span, data.AsMemory());
    /// </code>
    /// This helper allocates a byte[] copy for simplicity.
    /// </remarks>
    public static GeomResource ReadGeom(this DbpfPackage package, in DbpfEntry entry)
    {
        using var data = package.GetResourceData(in entry);
        byte[] copy = data.Span.ToArray(); // copy so GeomResource can hold Memory<byte>
        return GeomReader.Read(copy, copy);
    }

    /// <summary>Decompresses and parses a STBL string table from the given entry.</summary>
    public static StblResource ReadStbl(this DbpfPackage package, in DbpfEntry entry)
    {
        using var data = package.GetResourceData(in entry);
        return StblReader.Read(data.Span);
    }

    /// <summary>Decompresses and parses a COBJ catalog object from the given entry.</summary>
    public static CobjResource ReadCobj(this DbpfPackage package, in DbpfEntry entry)
    {
        using var data = package.GetResourceData(in entry);
        return CobjReader.Read(data.Span);
    }

    /// <summary>Decompresses and parses an RCOL container (MODL, MLOD, etc.).</summary>
    public static RcolResource ReadRcol(this DbpfPackage package, in DbpfEntry entry)
    {
        using var data = package.GetResourceData(in entry);
        byte[] copy = data.Span.ToArray();
        return RcolReader.Read(copy, copy);
    }

    /// <summary>
    /// Returns the first entry matching the given resource type, or null if none found.
    /// </summary>
    public static DbpfEntry? FindFirst(this DbpfPackage package, ResourceType type)
    {
        foreach (ref readonly var entry in package.EntriesOfType(type))
            return entry;
        return null;
    }

    /// <summary>Counts how many entries match the given resource type.</summary>
    public static int CountOf(this DbpfPackage package, ResourceType type)
    {
        int count = 0;
        foreach (ref readonly var _ in package.EntriesOfType(type))
            count++;
        return count;
    }
}
