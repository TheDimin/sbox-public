namespace Sims4Reader;

/// <summary>
/// A parsed resource from a DBPF package.
/// </summary>
public interface IResource
{
    /// <summary>
    /// Parse the resource from raw decompressed bytes.
    /// </summary>
    void Parse(ReadOnlyMemory<byte> data);
}

/// <summary>
/// A resource that can provide string table key hashes for display name resolution.
/// </summary>
public interface INamedResource : IResource
{
    /// <summary>
    /// Returns string table key hashes that can be resolved to display names.
    /// </summary>
    IEnumerable<uint> GetNameHashes();
}
