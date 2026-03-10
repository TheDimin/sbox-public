namespace Sims4Reader;

/// <summary>
/// A single entry in the package index. Points to compressed data on disk.
/// </summary>
public readonly struct ResourceEntry
{
    public readonly ResourceKey Key;
    public readonly uint ChunkOffset;
    public readonly uint FileSize;    // Size on disk (compressed)
    public readonly uint MemSize;     // Size in memory (uncompressed)
    public readonly ushort Compressed; // 0x5A42 = DEFLATE, 0x0000 = none
    public readonly int Index;        // Position in the Entries array

    internal ResourceEntry(ResourceKey key, uint chunkOffset, uint fileSize, uint memSize, ushort compressed, int index)
    {
        Key = key;
        ChunkOffset = chunkOffset;
        FileSize = fileSize;
        MemSize = memSize;
        Compressed = compressed;
        Index = index;
    }

    public bool IsCompressed => FileSize != MemSize;

    public override string ToString() => $"[{Index}] {Key} ({FileSize} bytes, {(IsCompressed ? "compressed" : "raw")})";
}
