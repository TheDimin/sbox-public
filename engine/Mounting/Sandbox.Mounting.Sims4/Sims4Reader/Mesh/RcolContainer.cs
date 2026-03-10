using Sims4Reader.Rcol;

namespace Sims4Reader.Mesh;

/// <summary>
/// An RCOL container resource that holds one or more typed chunks (GEOM, MATD, VPXY, etc.)
/// plus external TGI references.
///
/// On-disk layout:
///   Version (4 bytes)
///   PublicChunks (4 bytes) - number of "public" chunks
///   Unused (4 bytes)
///   ExternalResourceCount (4 bytes)
///   ChunkCount (4 bytes)
///   ChunkTGIBlocks[ChunkCount] - each: Instance(8) + Type(4) + Group(4) = 16 bytes (ITG order)
///   ExternalResources[ExternalResourceCount] - each: Instance(8) + Type(4) + Group(4) = 16 bytes
///   ChunkIndex[ChunkCount] - each: Position(4) + Length(4) = 8 bytes
///   ChunkData - the actual chunk bytes at the indexed positions
/// </summary>
public class RcolContainer : IResource
{
    /// <summary>Registry of known chunk tag strings to factory functions.</summary>
    private static readonly Dictionary<string, Func<RcolChunk>> ChunkFactories = new()
    {
        ["GEOM"] = () => new GeometryRcolChunk(),
        ["MLOD"] = () => new MeshLod(),
        ["SKIN"] = () => new SkinController(),
        ["VBUF"] = () => new VertexBuffer(),
        ["IBUF"] = () => new IndexBuffer(),
        ["VRTF"] = () => new VertexFormat(),
        ["VPXY"] = () => new VpxyChunk(),
        ["FTPT"] = () => new FootprintChunk(),
        ["LITE"] = () => new LightChunk(),
        // Additional tags produce generic (raw) chunks
    };

    public uint Version { get; set; }
    public int PublicChunks { get; set; }
    public uint Unused { get; set; }
    public ResourceKey[] ExternalReferences { get; set; } = Array.Empty<ResourceKey>();
    public List<RcolChunkEntry> ChunkEntries { get; set; } = new();

    public void Parse(ReadOnlyMemory<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var reader = new BinaryReader(ms);

        Version = reader.ReadUInt32();
        PublicChunks = reader.ReadInt32();
        Unused = reader.ReadUInt32();
        int externalCount = reader.ReadInt32();
        int chunkCount = reader.ReadInt32();

        // Read chunk TGI blocks (ITG order)
        var chunkKeys = new ResourceKey[chunkCount];
        for (int i = 0; i < chunkCount; i++)
        {
            ulong instance = reader.ReadUInt64();
            uint type = reader.ReadUInt32();
            uint group = reader.ReadUInt32();
            chunkKeys[i] = new ResourceKey((ResourceType)type, group, instance);
        }

        // Read external resource TGI blocks (ITG order)
        ExternalReferences = new ResourceKey[externalCount];
        for (int i = 0; i < externalCount; i++)
        {
            ulong instance = reader.ReadUInt64();
            uint type = reader.ReadUInt32();
            uint group = reader.ReadUInt32();
            ExternalReferences[i] = new ResourceKey((ResourceType)type, group, instance);
        }

        // Read chunk index (position + length)
        var positions = new uint[chunkCount];
        var lengths = new int[chunkCount];
        for (int i = 0; i < chunkCount; i++)
        {
            positions[i] = reader.ReadUInt32();
            lengths[i] = reader.ReadInt32();
        }

        // Special case: single chunk with position/type = 0
        if (chunkCount == 1)
        {
            positions[0] = (uint)(0x2C + externalCount * 16);
            lengths[0] = (int)(ms.Length - positions[0]);

            if ((uint)chunkKeys[0].Type == 0)
            {
                // Try to detect the type from the first 4 bytes of the chunk data
                long savedPos = ms.Position;
                ms.Position = positions[0];
                if (ms.Position + 4 <= ms.Length)
                {
                    byte[] tagBytes = reader.ReadBytes(4);
                    string tagStr = System.Text.Encoding.ASCII.GetString(tagBytes);
                    // Try to identify the resource type from the tag
                    uint detectedType = DetectResourceType(tagStr);
                    if (detectedType != 0)
                        chunkKeys[0] = new ResourceKey((ResourceType)detectedType, chunkKeys[0].Group, chunkKeys[0].Instance);
                }
                ms.Position = savedPos;
            }
        }

        // Parse each chunk
        ChunkEntries = new List<RcolChunkEntry>(chunkCount);
        for (int i = 0; i < chunkCount; i++)
        {
            ms.Position = positions[i];
            byte[] chunkData = reader.ReadBytes(lengths[i]);

            // Determine the tag from the first 4 bytes of the chunk data
            string chunkTag = chunkData.Length >= 4
                ? System.Text.Encoding.ASCII.GetString(chunkData, 0, 4)
                : "";

            RcolChunk chunk;
            if (ChunkFactories.TryGetValue(chunkTag, out var factory))
            {
                chunk = factory();
            }
            else
            {
                chunk = new RawRcolChunk();
            }

            using var chunkMs = new MemoryStream(chunkData);
            using var chunkReader = new BinaryReader(chunkMs);
            chunk.Parse(chunkReader, Version, ExternalReferences);

            ChunkEntries.Add(new RcolChunkEntry
            {
                Key = chunkKeys[i],
                Chunk = chunk,
            });
        }
    }

    /// <summary>
    /// Get the first chunk of type T, or null if not found.
    /// </summary>
    public T? GetChunk<T>() where T : RcolChunk
    {
        foreach (var entry in ChunkEntries)
            if (entry.Chunk is T typed)
                return typed;
        return null;
    }

    /// <summary>
    /// Get all chunks of type T.
    /// </summary>
    public IEnumerable<T> GetChunks<T>() where T : RcolChunk
    {
        foreach (var entry in ChunkEntries)
            if (entry.Chunk is T typed)
                yield return typed;
    }

    private static uint DetectResourceType(string tag)
    {
        return tag switch
        {
            "GEOM" => 0x015A1849,
            "MATD" => 0x01D0E75D,
            "VPXY" => 0x736884F1,
            "LITE" => 0x03B4C61D,
            "FTPT" => 0xD382BF57,
            "MTST" => 0x02019972,
            "MLOD" => 0x01D10F34,
            "MODL" => 0x01661233,
            "SKIN" => 0x01D0E76B,
            "VBUF" => 0x01D0E6FB,
            "IBUF" => 0x01D0E70F,
            "VRTF" => 0x01D0E723,
            _ => 0,
        };
    }
}

/// <summary>
/// Pairs a resource key with its parsed RCOL chunk.
/// </summary>
public class RcolChunkEntry
{
    public ResourceKey Key { get; set; }
    public RcolChunk Chunk { get; set; } = null!;
}

/// <summary>
/// A GEOM chunk inside an RCOL container. This is different from the standalone
/// GeometryResource - it appears as an RCOL chunk with the same GEOM binary format.
/// Delegates to GeometryResource for the actual parsing.
/// </summary>
public class GeometryRcolChunk : RcolChunk
{
    public GeometryResource Geometry { get; set; } = new();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        // The chunk data IS the full GEOM resource (tag + data)
        long startPos = reader.BaseStream.Position;
        byte[] allData = reader.ReadBytes((int)(reader.BaseStream.Length - startPos));
        Geometry.Parse(allData);
        Tag = 0x4D4F4547; // "GEOM"
    }
}

/// <summary>
/// A raw (unparsed) RCOL chunk for unknown or unsupported chunk types.
/// Stores the raw bytes for passthrough.
/// </summary>
public class RawRcolChunk : RcolChunk
{
    public byte[] RawData { get; set; } = Array.Empty<byte>();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        long startPos = reader.BaseStream.Position;
        RawData = reader.ReadBytes((int)(reader.BaseStream.Length - startPos));

        // Try to extract tag from first 4 bytes
        if (RawData.Length >= 4)
            Tag = BitConverter.ToUInt32(RawData, 0);
    }
}
