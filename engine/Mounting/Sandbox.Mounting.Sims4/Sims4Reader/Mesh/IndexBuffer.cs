using Sims4Reader.Rcol;

namespace Sims4Reader.Mesh;

/// <summary>
/// IBUF RCOL chunk - raw triangle index data buffer.
/// </summary>
public class IndexBuffer : RcolChunk
{
    [Flags]
    public enum FormatFlags : uint
    {
        None = 0x0,
        DifferencedIndices = 0x1,
        Uses32BitIndices = 0x2,
        IsDisplayList = 0x4,
    }

    public uint Version { get; set; }
    public FormatFlags Flags { get; set; }
    public uint DisplayListUsage { get; set; }

    /// <summary>
    /// The decoded index values. Always stored as int[] internally,
    /// regardless of whether the on-disk format was 16-bit or 32-bit.
    /// </summary>
    public int[] Indices { get; set; } = Array.Empty<int>();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        Tag = reader.ReadUInt32();
        Version = reader.ReadUInt32();
        Flags = (FormatFlags)reader.ReadUInt32();
        DisplayListUsage = reader.ReadUInt32();

        bool is32Bit = (Flags & FormatFlags.Uses32BitIndices) != 0;
        long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        int count = (int)(remaining / (is32Bit ? 4 : 2));

        Indices = new int[count];
        int last = 0;
        for (int i = 0; i < count; i++)
        {
            int cur = is32Bit ? reader.ReadInt32() : reader.ReadInt16();
            if ((Flags & FormatFlags.DifferencedIndices) != 0)
                cur += last;
            Indices[i] = cur;
            last = cur;
        }
    }

    /// <summary>
    /// Get a slice of the index buffer starting at the given index for the given count of primitives.
    /// Each triangle primitive uses 3 indices.
    /// </summary>
    /// <param name="startIndex">Start offset into the index array.</param>
    /// <param name="primitiveCount">Number of triangles.</param>
    public int[] GetIndices(int startIndex, int primitiveCount)
    {
        int totalIndices = primitiveCount * 3;
        var output = new int[totalIndices];
        Array.Copy(Indices, startIndex, output, 0, totalIndices);
        return output;
    }
}
