using System.Buffers;
using System.Buffers.Binary;

namespace Sims4Reader;

/// <summary>
/// High-performance, read-only DBPF package reader.
/// Opens Sims 4 .package files and provides O(1) resource lookup by TGI key.
/// </summary>
public sealed class DbpfPackage : IDisposable
{
    private const int HeaderSize = 96;
    private const string Magic = "DBPF";

    private Stream _stream;
    private readonly bool _ownsStream;
    private ResourceEntry[] _entries;
    private Dictionary<ResourceKey, int> _index;
    private Dictionary<ResourceType, List<int>> _typeIndex;
    private StringTable? _stringTable;
    private bool _stringTableLoaded;

    private DbpfPackage(Stream stream, bool ownsStream)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _entries = Array.Empty<ResourceEntry>();
        _index = new Dictionary<ResourceKey, int>();
        _typeIndex = new Dictionary<ResourceType, List<int>>();
        ReadPackage();
    }

    /// <summary>
    /// Open a .package file from disk (read-only).
    /// </summary>
    public static DbpfPackage Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new DbpfPackage(fs, ownsStream: true);
    }

    /// <summary>
    /// Open a .package from an existing stream. The caller owns the stream lifetime.
    /// </summary>
    public static DbpfPackage Open(Stream stream)
    {
        return new DbpfPackage(stream, ownsStream: false);
    }

    /// <summary>
    /// All resource entries in the package.
    /// </summary>
    public IReadOnlyList<ResourceEntry> Entries => _entries;

    /// <summary>
    /// O(1) lookup by full TGI key. Returns null if not found.
    /// </summary>
    public ResourceEntry? Find(ResourceKey key)
    {
        return _index.TryGetValue(key, out int idx) ? _entries[idx] : null;
    }

    /// <summary>
    /// O(1) lookup by Type, Group, Instance.
    /// </summary>
    public ResourceEntry? Find(ResourceType type, uint group, ulong instance)
    {
        return Find(new ResourceKey(type, group, instance));
    }

    /// <summary>
    /// Find all entries of a given resource type.
    /// </summary>
    public IEnumerable<ResourceEntry> FindAll(ResourceType type)
    {
        if (_typeIndex.TryGetValue(type, out var indices))
        {
            foreach (int idx in indices)
                yield return _entries[idx];
        }
    }

    /// <summary>
    /// Get the raw decompressed bytes for a resource entry.
    /// </summary>
    public byte[] GetBytes(ResourceEntry entry)
    {
        if (entry.ChunkOffset == 0xFFFFFFFF) return Array.Empty<byte>();
        if (entry.FileSize == 1 && entry.MemSize == 0xFFFFFFFF) return Array.Empty<byte>();

        _stream.Position = entry.ChunkOffset;
        byte[] fileData = new byte[entry.FileSize];
        _stream.ReadExactly(fileData);

        if (!entry.IsCompressed)
            return fileData;

        return Compression.Decompress(fileData, (int)entry.MemSize);
    }

    /// <summary>
    /// Get a typed resource parsed from the entry's data.
    /// </summary>
    public T GetResource<T>(ResourceEntry entry) where T : IResource, new()
    {
        byte[] data = GetBytes(entry);
        var resource = new T();
        resource.Parse(data);
        return resource;
    }

    /// <summary>
    /// Get the first string table in the package (cached).
    /// </summary>
    public StringTable? GetStringTable()
    {
        if (_stringTableLoaded) return _stringTable;
        _stringTableLoaded = true;

        foreach (var entry in FindAll(ResourceType.StringTable))
        {
            _stringTable = GetResource<StringTable>(entry);
            break;
        }
        return _stringTable;
    }

    /// <summary>
    /// Try to resolve a display name for a resource by cross-referencing string tables.
    /// Returns null if no name is found.
    /// </summary>
    public string? GetDisplayName(ResourceEntry entry)
    {
        var stbl = GetStringTable();
        if (stbl == null) return null;

        // Try the instance hash as a string table key
        uint hash = (uint)(entry.Key.Instance & 0xFFFFFFFF);
        return stbl.GetString(hash);
    }

    private void ReadPackage()
    {
        Span<byte> headerBuf = stackalloc byte[HeaderSize];
        _stream.Position = 0;
        _stream.ReadExactly(headerBuf);

        // Validate magic
        if (headerBuf[0] != 'D' || headerBuf[1] != 'B' || headerBuf[2] != 'P' || headerBuf[3] != 'F')
            throw new InvalidDataException("Not a DBPF package (invalid magic bytes)");

        int major = BinaryPrimitives.ReadInt32LittleEndian(headerBuf[4..]);
        if (major != 2)
            throw new InvalidDataException($"Unsupported DBPF major version: {major} (expected 2)");

        int indexCount = BinaryPrimitives.ReadInt32LittleEndian(headerBuf[36..]);
        int indexSize = BinaryPrimitives.ReadInt32LittleEndian(headerBuf[44..]);
        int indexPosition = BinaryPrimitives.ReadInt32LittleEndian(headerBuf[64..]);
        if (indexPosition == 0)
            indexPosition = BinaryPrimitives.ReadInt32LittleEndian(headerBuf[40..]);

        if (indexCount == 0 || indexPosition == 0)
        {
            _entries = Array.Empty<ResourceEntry>();
            return;
        }

        ReadIndex(indexPosition, indexSize, indexCount);
    }

    private void ReadIndex(int indexPosition, int indexSize, int indexCount)
    {
        _stream.Position = indexPosition;

        byte[] indexData = ArrayPool<byte>.Shared.Rent(indexSize);
        try
        {
            _stream.ReadExactly(indexData, 0, indexSize);
            var span = indexData.AsSpan(0, indexSize);

            int pos = 0;
            uint indexType = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
            pos += 4;

            // Read shared header values
            uint sharedType = 0, sharedGroup = 0, sharedInstanceHigh = 0;
            if ((indexType & 0x01) != 0) { sharedType = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]); pos += 4; }
            if ((indexType & 0x02) != 0) { sharedGroup = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]); pos += 4; }
            if ((indexType & 0x04) != 0) { sharedInstanceHigh = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]); pos += 4; }

            _entries = new ResourceEntry[indexCount];
            _index = new Dictionary<ResourceKey, int>(indexCount);
            _typeIndex = new Dictionary<ResourceType, List<int>>();

            for (int i = 0; i < indexCount; i++)
            {
                uint type = (indexType & 0x01) != 0 ? sharedType : BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
                if ((indexType & 0x01) == 0) pos += 4;

                uint group = (indexType & 0x02) != 0 ? sharedGroup : BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
                if ((indexType & 0x02) == 0) pos += 4;

                uint instanceHigh = (indexType & 0x04) != 0 ? sharedInstanceHigh : BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]);
                if ((indexType & 0x04) == 0) pos += 4;

                uint instanceLow = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]); pos += 4;
                uint chunkOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]); pos += 4;
                uint fileSize = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]) & 0x7FFFFFFF; pos += 4;
                uint memSize = BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]); pos += 4;
                ushort compressed = BinaryPrimitives.ReadUInt16LittleEndian(span[pos..]); pos += 2;
                pos += 2; // skip Unknown2

                ulong instance = ((ulong)instanceHigh << 32) | instanceLow;
                var resourceType = (ResourceType)type;
                var key = new ResourceKey(resourceType, group, instance);

                _entries[i] = new ResourceEntry(key, chunkOffset, fileSize, memSize, compressed, i);
                _index[key] = i;

                if (!_typeIndex.TryGetValue(resourceType, out var list))
                {
                    list = new List<int>();
                    _typeIndex[resourceType] = list;
                }
                list.Add(i);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(indexData);
        }
    }

    public void Dispose()
    {
        if (_ownsStream)
            _stream?.Dispose();
    }
}
