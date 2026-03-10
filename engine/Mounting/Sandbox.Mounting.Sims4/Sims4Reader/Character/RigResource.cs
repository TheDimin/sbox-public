using System.Numerics;
using System.Text;

namespace Sims4Reader.Character;

/// <summary>
/// Sims 4 Rig (skeleton) resource. Resource type: 0x8EAF13DE.
///
/// Contains a bone hierarchy with transforms, optional skeleton name,
/// and optional IK chain data.  Three on-disk formats exist:
///   - Clear  : structured bone data (major/minor version header)
///   - RawGranny / WrappedGranny : opaque Granny2 data stored as a byte blob
///
/// Only the "Clear" format is fully parsed; Granny formats are stored
/// as raw bytes in <see cref="RawData"/>.
/// </summary>
public class RigResource : IResource
{
    // ====================================================================
    //  Public properties
    // ====================================================================

    /// <summary>The detected on-disk format.</summary>
    public RigFormat Format { get; set; }

    /// <summary>
    /// Raw bytes for RawGranny / WrappedGranny formats.
    /// Null when <see cref="Format"/> is <see cref="RigFormat.Clear"/>.
    /// </summary>
    public byte[]? RawData { get; set; }

    /// <summary>Major version (Clear format only).</summary>
    public uint Major { get; set; }

    /// <summary>Minor version (Clear format only).</summary>
    public uint Minor { get; set; }

    /// <summary>
    /// Skeleton bone hierarchy. Empty for non-Clear formats.
    /// </summary>
    public List<Bone> Bones { get; set; } = [];

    /// <summary>
    /// Skeleton name string. Present when <see cref="Major"/> >= 4.
    /// </summary>
    public string? SkeletonName { get; set; }

    /// <summary>
    /// Inverse Kinematics chain data. Present for Sims 4 rigs and when
    /// <see cref="Major"/> >= 4.
    /// </summary>
    public List<IKChain>? IKChains { get; set; }

    // ====================================================================
    //  Parse
    // ====================================================================

    public void Parse(ReadOnlyMemory<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms, Encoding.UTF8);

        if (ms.Length < 8)
        {
            Format = RigFormat.RawGranny;
            RawData = data.ToArray();
            return;
        }

        uint dw1 = r.ReadUInt32();
        uint dw2 = r.ReadUInt32();
        ms.Position = 0;

        if (dw1 == 0x8EAF13DE && dw2 == 0x00000000)
        {
            // WrappedGranny -- treat as raw (implementation matches s4pi)
            Format = RigFormat.WrappedGranny;
            RawData = data.ToArray();
        }
        else if ((dw1 == 0x00000003 || dw1 == 0x00000004)
              && (dw2 == 0x00000001 || dw2 == 0x00000002))
        {
            Format = RigFormat.Clear;
            ParseClear(r);
        }
        else
        {
            Format = RigFormat.RawGranny;
            RawData = data.ToArray();
        }
    }

    private void ParseClear(BinaryReader r)
    {
        Major = r.ReadUInt32();
        Minor = r.ReadUInt32();

        // Bone list: int32 count, then each bone
        int boneCount = r.ReadInt32();
        Bones = new List<Bone>(boneCount);
        for (int i = 0; i < boneCount; i++)
            Bones.Add(Bone.Read(r));

        // Skeleton name (present when major >= 4; for TS4 files with
        // major < 4 the name is absent but IK chains are still present)
        if (Major >= 4)
        {
            int nameLen = r.ReadInt32();
            SkeletonName = new string(r.ReadChars(nameLen));
        }

        // IK chains (present for TS4 or when major >= 4)
        if (r.BaseStream.Position < r.BaseStream.Length)
        {
            int ikCount = r.ReadInt32();
            IKChains = new List<IKChain>(ikCount);
            for (int i = 0; i < ikCount; i++)
                IKChains.Add(IKChain.Read(r, Major));
        }
    }

    // ====================================================================
    //  Sub-types
    // ====================================================================

    public enum RigFormat
    {
        RawGranny,
        WrappedGranny,
        Clear,
    }

    /// <summary>
    /// A single bone in the skeleton hierarchy.
    /// </summary>
    public class Bone
    {
        /// <summary>Local position (X, Y, Z).</summary>
        public Vector3 Position { get; set; }

        /// <summary>Local rotation quaternion (X, Y, Z, W).</summary>
        public Quaternion Orientation { get; set; }

        /// <summary>Local scale (X, Y, Z).</summary>
        public Vector3 Scaling { get; set; }

        /// <summary>Bone name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Index of the opposing (mirrored) bone, or -1.</summary>
        public int OpposingBoneIndex { get; set; }

        /// <summary>Index of the parent bone, or -1 for roots.</summary>
        public int ParentBoneIndex { get; set; }

        /// <summary>FNV name hash of this bone.</summary>
        public uint NameHash { get; set; }

        /// <summary>Unknown flags / secondary hash.</summary>
        public uint Flags { get; set; }

        internal static Bone Read(BinaryReader r)
        {
            var bone = new Bone
            {
                // Vertex: 3 floats (X, Y, Z)
                Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                // Quaternion: 4 floats (X, Y, Z, W)
                Orientation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                // Vertex: 3 floats (X, Y, Z)
                Scaling = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            };

            int nameLen = r.ReadInt32();
            bone.Name = new string(r.ReadChars(nameLen));
            bone.OpposingBoneIndex = r.ReadInt32();
            bone.ParentBoneIndex = r.ReadInt32();
            bone.NameHash = r.ReadUInt32();
            bone.Flags = r.ReadUInt32();
            return bone;
        }
    }

    /// <summary>
    /// An Inverse Kinematics chain definition.
    /// </summary>
    public class IKChain
    {
        /// <summary>Bone indices that form this IK chain.</summary>
        public List<int> BoneIndices { get; set; } = [];

        // Info-node indices (only present when major >= 4)

        public int InfoNode0Index { get; set; }
        public int InfoNode1Index { get; set; }
        public int InfoNode2Index { get; set; }
        public int InfoNode3Index { get; set; }
        public int InfoNode4Index { get; set; }
        public int InfoNode5Index { get; set; }
        public int InfoNode6Index { get; set; }
        public int InfoNode7Index { get; set; }
        public int InfoNode8Index { get; set; }
        public int InfoNode9Index { get; set; }
        public int InfoNodeAIndex { get; set; }

        /// <summary>Whether info-node fields were present in the data.</summary>
        public bool HasInfoNodes { get; set; }

        public int PoleVectorIndex { get; set; }
        public int SlotInfoIndex { get; set; }
        public int SlotOffsetIndex { get; set; }
        public int RootIndex { get; set; }

        internal static IKChain Read(BinaryReader r, uint major)
        {
            var ik = new IKChain();

            // IntList: int32 count, then int32 values
            int boneCount = r.ReadInt32();
            ik.BoneIndices = new List<int>(boneCount);
            for (int i = 0; i < boneCount; i++)
                ik.BoneIndices.Add(r.ReadInt32());

            if (major >= 4)
            {
                ik.HasInfoNodes = true;
                ik.InfoNode0Index = r.ReadInt32();
                ik.InfoNode1Index = r.ReadInt32();
                ik.InfoNode2Index = r.ReadInt32();
                ik.InfoNode3Index = r.ReadInt32();
                ik.InfoNode4Index = r.ReadInt32();
                ik.InfoNode5Index = r.ReadInt32();
                ik.InfoNode6Index = r.ReadInt32();
                ik.InfoNode7Index = r.ReadInt32();
                ik.InfoNode8Index = r.ReadInt32();
                ik.InfoNode9Index = r.ReadInt32();
                ik.InfoNodeAIndex = r.ReadInt32();
            }

            ik.PoleVectorIndex = r.ReadInt32();

            if (major >= 4)
                ik.SlotInfoIndex = r.ReadInt32();

            ik.SlotOffsetIndex = r.ReadInt32();
            ik.RootIndex = r.ReadInt32();

            return ik;
        }
    }
}
