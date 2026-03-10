using Sims4Reader.Rcol;

namespace Sims4Reader.Mesh;

/// <summary>
/// A bone entry with a name hash and a 4x3 inverse bind pose matrix.
/// The matrix is stored as 12 floats in row-major order (3 rows x 4 columns).
/// </summary>
public class Bone
{
    public uint NameHash { get; set; }

    /// <summary>
    /// 4x3 inverse bind pose matrix stored as 12 floats.
    /// Layout: [row0col0, row0col1, row0col2, row0col3,
    ///          row1col0, row1col1, row1col2, row1col3,
    ///          row2col0, row2col1, row2col2, row2col3]
    /// </summary>
    public float[] InverseBindPose { get; set; } = new float[12];
}

/// <summary>
/// SKIN RCOL chunk - bone binding data with inverse bind pose matrices.
/// The on-disk format stores all bone name hashes first, then all matrices sequentially.
/// </summary>
public class SkinController : RcolChunk
{
    public uint Version { get; set; }
    public List<Bone> Bones { get; set; } = new();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        Tag = reader.ReadUInt32();
        Version = reader.ReadUInt32();

        int count = reader.ReadInt32();

        // Read all bone name hashes first
        var nameHashes = new uint[count];
        for (int i = 0; i < count; i++)
            nameHashes[i] = reader.ReadUInt32();

        // Then read all inverse bind pose matrices (12 floats each)
        Bones = new List<Bone>(count);
        for (int i = 0; i < count; i++)
        {
            var bone = new Bone { NameHash = nameHashes[i] };
            for (int j = 0; j < 12; j++)
                bone.InverseBindPose[j] = reader.ReadSingle();
            Bones.Add(bone);
        }
    }
}
