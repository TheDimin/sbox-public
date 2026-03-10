namespace Sims4Reader.Rcol;

/// <summary>
/// VPXY (Vertex Proxy) RCOL chunk. Contains TGI references to actual mesh/texture
/// resources along with bounding box data and optional footprint reference.
///
/// On-disk layout:
///   Tag:          4 bytes ("VPXY" = 0x56505859)
///   Version:      4 bytes (uint, expected 4)
///   TgiOffset:    4 bytes (uint, byte offset from current position to start of TGI block list)
///   TgiSize:      4 bytes (uint, byte size of the TGI block list)
///   EntryCount:   1 byte
///   Entries[EntryCount]:
///     EntryType:  1 byte (0x00 or 0x01)
///     If 0x00:
///       EntryId:      1 byte
///       TgiRefCount:  1 byte
///       TgiIndices[TgiRefCount]: each 1 byte (index into the TGI block list)
///     If 0x01:
///       TgiIndex:     4 bytes (int32, index into the TGI block list)
///   TC02:         1 byte (always 0x02)
///   BoundsMin:    3 x float (X, Y, Z)
///   BoundsMax:    3 x float (X, Y, Z)
///   Unused:       4 bytes
///   Modular:      1 byte (non-zero = has FTPT index)
///   FtptIndex:    4 bytes (int32, only present when Modular != 0)
///   TgiBlocks[]:  at TgiOffset position, each 16 bytes: Type(4) + Group(4) + Instance(8)
/// </summary>
public class VpxyChunk : RcolChunk
{
    private const uint VpxyTag = 0x59585056; // "VPXY" in little-endian

    public uint Version { get; set; }
    public List<VpxyEntry> Entries { get; set; } = new();
    public byte TC02 { get; set; }
    public float BoundsMinX { get; set; }
    public float BoundsMinY { get; set; }
    public float BoundsMinZ { get; set; }
    public float BoundsMaxX { get; set; }
    public float BoundsMaxY { get; set; }
    public float BoundsMaxZ { get; set; }
    public byte[] Unused { get; set; } = new byte[4];
    public bool Modular { get; set; }
    public int FtptIndex { get; set; }

    /// <summary>
    /// TGI block list referenced by indices in the entries.
    /// </summary>
    public List<ResourceKey> TgiBlocks { get; set; } = new();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        long basePosition = reader.BaseStream.Position;

        uint tag = reader.ReadUInt32();
        if (tag != VpxyTag)
            throw new InvalidDataException(
                $"Invalid VPXY tag: 0x{tag:X8} at 0x{reader.BaseStream.Position:X8}; expected 'VPXY'");

        Tag = tag;
        Version = reader.ReadUInt32();

        // TGI offset is relative to the current stream position (after reading the offset itself)
        uint tgiOffset = reader.ReadUInt32();
        long tgiOffsetBase = reader.BaseStream.Position;
        uint tgiSize = reader.ReadUInt32();
        long tgiAbsolutePosition = tgiOffsetBase + tgiOffset;

        // Read entry list
        byte entryCount = reader.ReadByte();
        Entries = new List<VpxyEntry>(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            byte entryType = reader.ReadByte();
            if (entryType == 0x00)
            {
                byte entryId = reader.ReadByte();
                byte tgiRefCount = reader.ReadByte();
                var indices = new List<byte>(tgiRefCount);
                for (int j = 0; j < tgiRefCount; j++)
                    indices.Add(reader.ReadByte());

                Entries.Add(new VpxyEntry00
                {
                    EntryId = entryId,
                    TgiIndices = indices,
                });
            }
            else if (entryType == 0x01)
            {
                int tgiIndex = reader.ReadInt32();
                Entries.Add(new VpxyEntry01
                {
                    TgiIndex = tgiIndex,
                });
            }
            else
            {
                throw new InvalidDataException(
                    $"Unknown VPXY entry type 0x{entryType:X2} at 0x{reader.BaseStream.Position:X8}");
            }
        }

        // TC02 byte
        TC02 = reader.ReadByte();

        // Bounding box: Min(X,Y,Z), Max(X,Y,Z)
        BoundsMinX = reader.ReadSingle();
        BoundsMinY = reader.ReadSingle();
        BoundsMinZ = reader.ReadSingle();
        BoundsMaxX = reader.ReadSingle();
        BoundsMaxY = reader.ReadSingle();
        BoundsMaxZ = reader.ReadSingle();

        // Unused 4 bytes
        Unused = reader.ReadBytes(4);

        // Modular flag and optional FTPT index
        byte modularByte = reader.ReadByte();
        Modular = modularByte != 0;
        if (Modular)
            FtptIndex = reader.ReadInt32();
        else
            FtptIndex = 0;

        // Read TGI block list at the specified offset
        reader.BaseStream.Position = tgiAbsolutePosition;
        int tgiCount = (int)(tgiSize / 16);
        TgiBlocks = new List<ResourceKey>(tgiCount);
        for (int i = 0; i < tgiCount; i++)
        {
            uint resType = reader.ReadUInt32();
            uint resGroup = reader.ReadUInt32();
            ulong resInstance = reader.ReadUInt64();
            TgiBlocks.Add(new ResourceKey((ResourceType)resType, resGroup, resInstance));
        }
    }
}

/// <summary>
/// Base class for VPXY entry types.
/// </summary>
public abstract class VpxyEntry
{
    public abstract byte EntryType { get; }
}

/// <summary>
/// Type 0x00 entry: an entry ID with a list of TGI index references (each stored as a byte).
/// </summary>
public class VpxyEntry00 : VpxyEntry
{
    public override byte EntryType => 0x00;

    /// <summary>
    /// Entry identifier byte.
    /// </summary>
    public byte EntryId { get; set; }

    /// <summary>
    /// List of byte-sized indices into the parent VPXY TgiBlocks list.
    /// </summary>
    public List<byte> TgiIndices { get; set; } = new();
}

/// <summary>
/// Type 0x01 entry: a single int32 TGI index (slot reference).
/// </summary>
public class VpxyEntry01 : VpxyEntry
{
    public override byte EntryType => 0x01;

    /// <summary>
    /// Index into the parent VPXY TgiBlocks list.
    /// </summary>
    public int TgiIndex { get; set; }
}
