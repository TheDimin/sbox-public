using Sims4Reader.Rcol;

namespace Sims4Reader.Mesh;

/// <summary>
/// Axis-aligned bounding box: min and max corners in 3D space.
/// </summary>
public struct BoundingBox
{
    public float MinX, MinY, MinZ;
    public float MaxX, MaxY, MaxZ;

    public void Parse(BinaryReader reader)
    {
        MinX = reader.ReadSingle();
        MinY = reader.ReadSingle();
        MinZ = reader.ReadSingle();
        MaxX = reader.ReadSingle();
        MaxY = reader.ReadSingle();
        MaxZ = reader.ReadSingle();
    }
}

/// <summary>
/// LOD detail level identifiers used in MODL resources.
/// </summary>
public enum LodId : uint
{
    HighDetail = 0x00000000,
    MediumDetail = 0x00000001,
    LowDetail = 0x00000002,
    HighDetailShadow = 0x00010000,
    MediumDetailShadow = 0x00010001,
    LowDetailShadow = 0x00010002,
}

/// <summary>
/// Flags on LOD entries in MODL resources.
/// </summary>
[Flags]
public enum LodInfoFlags : uint
{
    None = 0x00000000,
    Portal = 0x00000001,
    Door = 0x00000002,
}

/// <summary>
/// Type of reference in an RCOL chunk reference uint32.
/// The upper 4 bits encode the reference type.
/// </summary>
public enum ChunkReferenceType : byte
{
    /// <summary>Public: index directly into ChunkEntries[0..PublicChunks-1].</summary>
    Public = 0x0,
    /// <summary>Private: index offset by PublicChunks into ChunkEntries.</summary>
    Private = 0x1,
    /// <summary>External: unused in known Sims 4 data.</summary>
    External = 0x2,
    /// <summary>Delayed: index into ExternalReferences (separate resource in package).</summary>
    Delayed = 0x3,
}

/// <summary>
/// Utility for encoding/decoding RCOL chunk references.
/// A chunk reference is a uint32 where:
///   - Bits 0-27: TGI block index + 1 (0 = null reference)
///   - Bits 28-31: ChunkReferenceType
/// </summary>
public static class ChunkReference
{
    /// <summary>
    /// Decode a raw uint32 chunk reference to get the local index (0-based).
    /// Returns -1 if the reference is null (index part is 0).
    /// The meaning of this index depends on the reference type:
    ///   Public:  ChunkEntries[index]
    ///   Private: ChunkEntries[index + PublicChunks]
    ///   Delayed: ExternalReferences[index]
    /// </summary>
    public static int GetTgiIndex(uint raw)
    {
        uint indexPart = raw & 0x0FFFFFFF;
        return indexPart == 0 ? -1 : (int)(indexPart - 1);
    }

    /// <summary>
    /// Get the reference type from a raw chunk reference.
    /// </summary>
    public static ChunkReferenceType GetReferenceType(uint raw)
    {
        return (ChunkReferenceType)((raw >> 28) & 0x0F);
    }

    /// <summary>
    /// Resolve a chunk reference to an absolute index into the RCOL ChunkEntries array.
    /// Returns -1 if the reference is null or delayed (external resource).
    /// </summary>
    public static int ResolveChunkIndex(uint raw, int publicChunks)
    {
        int idx = GetTgiIndex(raw);
        if (idx < 0) return -1;

        var refType = GetReferenceType(raw);
        return refType switch
        {
            ChunkReferenceType.Public => idx,
            ChunkReferenceType.Private => idx + publicChunks,
            _ => -1, // Delayed/External resolve via ExternalReferences
        };
    }

    /// <summary>
    /// Returns true if this chunk reference points to an external resource (Delayed type).
    /// </summary>
    public static bool IsDelayed(uint raw)
    {
        return GetReferenceType(raw) == ChunkReferenceType.Delayed;
    }
}

/// <summary>
/// A single LOD (Level of Detail) entry within a MODL chunk.
/// Each LOD references an MLOD chunk via a chunk reference index.
///
/// On-disk layout (20 bytes):
///   ChunkReference: 4 bytes (uint, reference to MLOD chunk)
///   Flags:          4 bytes (LodInfoFlags)
///   Id:             4 bytes (LodId enum)
///   MinZValue:      4 bytes (float)
///   MaxZValue:      4 bytes (float)
/// </summary>
public class LodEntry
{
    /// <summary>
    /// Raw chunk reference to the MLOD chunk for this LOD level.
    /// Use <see cref="ChunkReference.GetTgiIndex"/> to decode the index.
    /// </summary>
    public uint MlodChunkRef { get; set; }

    /// <summary>
    /// The TGI index decoded from MlodChunkRef (-1 if null reference).
    /// </summary>
    public int MlodIndex => ChunkReference.GetTgiIndex(MlodChunkRef);

    public LodInfoFlags Flags { get; set; }
    public LodId Id { get; set; }
    public float MinZValue { get; set; }
    public float MaxZValue { get; set; }

    public void Parse(BinaryReader reader)
    {
        MlodChunkRef = reader.ReadUInt32();
        Flags = (LodInfoFlags)reader.ReadUInt32();
        Id = (LodId)reader.ReadUInt32();
        MinZValue = reader.ReadSingle();
        MaxZValue = reader.ReadSingle();
    }
}

/// <summary>
/// MODL (Model) RCOL chunk. Top-level model resource that contains
/// LOD entries, each referencing an MLOD chunk for mesh data.
///
/// The full loading pipeline:
///   MODL → LODEntry → MLOD → LodMesh → VBUF/IBUF/VRTF (geometry)
///                                      + MATD (material) → texture TGI refs
///
/// On-disk layout:
///   Tag:       4 bytes ("MODL", 0x4C444F4D)
///   Version:   4 bytes (uint, typically 0x200-0x303)
///   LODCount:  4 bytes (int)
///   Bounds:    24 bytes (BoundingBox: 6 floats)
///
///   If version >= 0x102 and version &lt; 0x300:
///     ExtraBoundsCount: 4 bytes (int)
///     ExtraBounds[]:    count * 24 bytes (BoundingBox each)
///     FadeType:         4 bytes (uint)
///     CustomFadeDistance: 4 bytes (float)
///
///   If version >= 0x300:
///     UnknownData:      20 bytes (raw)
///
///   LODEntries[LODCount]: each 20 bytes (ChunkRef + Flags + Id + MinZ + MaxZ)
/// </summary>
public class ModlChunk : RcolChunk
{
    private const uint ModlTag = 0x4C444F4D; // "MODL" little-endian

    public uint Version { get; set; }
    public BoundingBox Bounds { get; set; }
    public List<BoundingBox> ExtraBounds { get; set; } = new();
    public uint FadeType { get; set; }
    public float CustomFadeDistance { get; set; }

    /// <summary>Raw 20 bytes present when version >= 0x300.</summary>
    public byte[] UnknownV3Data { get; set; } = Array.Empty<byte>();

    public List<LodEntry> LodEntries { get; set; } = new();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        uint tag = reader.ReadUInt32();
        if (tag != ModlTag)
            throw new InvalidDataException(
                $"Invalid MODL tag: 0x{tag:X8} at 0x{reader.BaseStream.Position:X8}; expected 'MODL'");

        Tag = tag;
        Version = reader.ReadUInt32();

        int lodCount = reader.ReadInt32();

        // Bounding box: min XYZ, max XYZ (24 bytes)
        var bounds = new BoundingBox();
        bounds.Parse(reader);
        Bounds = bounds;

        // Version-dependent extra data
        if (Version >= 0x00000102 && Version < 0x00000300)
        {
            int extraCount = reader.ReadInt32();
            ExtraBounds = new List<BoundingBox>(extraCount);
            for (int i = 0; i < extraCount; i++)
            {
                var eb = new BoundingBox();
                eb.Parse(reader);
                ExtraBounds.Add(eb);
            }

            FadeType = reader.ReadUInt32();
            CustomFadeDistance = reader.ReadSingle();
        }
        else if (Version >= 0x00000300)
        {
            UnknownV3Data = reader.ReadBytes(20);
        }

        // LOD entries
        LodEntries = new List<LodEntry>(lodCount);
        for (int i = 0; i < lodCount; i++)
        {
            var lod = new LodEntry();
            lod.Parse(reader);
            LodEntries.Add(lod);
        }
    }

    /// <summary>
    /// Get the LOD entry for a specific detail level, or null if not present.
    /// </summary>
    public LodEntry? GetLod(LodId id)
    {
        foreach (var lod in LodEntries)
            if (lod.Id == id)
                return lod;
        return null;
    }

    /// <summary>
    /// Get the highest available detail LOD (prefers HighDetail, falls back to Medium, then Low).
    /// </summary>
    public LodEntry? GetBestLod()
    {
        return GetLod(LodId.HighDetail)
            ?? GetLod(LodId.MediumDetail)
            ?? GetLod(LodId.LowDetail);
    }
}
