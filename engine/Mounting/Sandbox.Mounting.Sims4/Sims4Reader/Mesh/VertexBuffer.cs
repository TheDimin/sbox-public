using Sims4Reader.Rcol;

namespace Sims4Reader.Mesh;

/// <summary>
/// VBUF RCOL chunk - raw vertex data buffer that can be decoded using a VertexFormat layout.
/// </summary>
public class VertexBuffer : RcolChunk
{
    /// <summary>BGRA to RGBA channel mapping for ColorUByte4 blend weights.</summary>
    private static readonly int[] ColorUByte4Map = { 2, 1, 0, 3 };

    [Flags]
    public enum FormatFlags : uint
    {
        None = 0x0,
        Dynamic = 0x1,
        DifferencedVertices = 0x2,
        Collapsed = 0x4,
    }

    public uint Version { get; set; }
    public FormatFlags Flags { get; set; }
    public int SwizzleInfo { get; set; }
    public byte[] Buffer { get; set; } = Array.Empty<byte>();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        Tag = reader.ReadUInt32();
        Version = reader.ReadUInt32();
        Flags = (FormatFlags)reader.ReadUInt32();
        // ChunkReference is stored as a single int32 in the s4pi code
        SwizzleInfo = reader.ReadInt32();
        // Rest is the raw vertex buffer
        Buffer = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
    }

    /// <summary>
    /// Decode vertices from the raw buffer using the given format layout.
    /// </summary>
    /// <param name="format">The vertex format describing the layout.</param>
    /// <param name="offset">Byte offset into the buffer to start reading.</param>
    /// <param name="count">Number of vertices to read.</param>
    /// <param name="uvScales">Optional UV scale factors per channel. If null, defaults to 1.</param>
    public Vertex[] GetVertices(VertexFormat format, long offset, int count, float[]? uvScales = null)
    {
        using var ms = new MemoryStream(Buffer);
        ms.Seek(offset, SeekOrigin.Begin);

        var posLayout = format.Elements.FirstOrDefault(x => x.Usage == ElementUsage.Position);
        var normLayout = format.Elements.FirstOrDefault(x => x.Usage == ElementUsage.Normal);
        var uvLayouts = format.Elements.Where(x => x.Usage == ElementUsage.UV).ToArray();
        var blendIdxLayout = format.Elements.FirstOrDefault(x => x.Usage == ElementUsage.BlendIndex);
        var blendWtLayout = format.Elements.FirstOrDefault(x => x.Usage == ElementUsage.BlendWeight);
        var tangentLayout = format.Elements.FirstOrDefault(x => x.Usage == ElementUsage.Tangent);
        var colorLayout = format.Elements.FirstOrDefault(x => x.Usage == ElementUsage.Colour);

        bool hasPos = format.Elements.Any(x => x.Usage == ElementUsage.Position);
        bool hasNorm = format.Elements.Any(x => x.Usage == ElementUsage.Normal);
        bool hasBlendIdx = format.Elements.Any(x => x.Usage == ElementUsage.BlendIndex);
        bool hasBlendWt = format.Elements.Any(x => x.Usage == ElementUsage.BlendWeight);
        bool hasTangent = format.Elements.Any(x => x.Usage == ElementUsage.Tangent);
        bool hasColor = format.Elements.Any(x => x.Usage == ElementUsage.Colour);

        var vertices = new Vertex[count];
        uvScales ??= new float[] { 0f };

        for (int i = 0; i < count; i++)
        {
            var data = new byte[format.Stride];
            ms.Read(data, 0, format.Stride);

            var v = new Vertex();

            if (hasPos)
            {
                var pts = new float[VertexFormat.FloatCountFromFormat(posLayout.Format)];
                ReadFloatData(data, posLayout, ref pts);
                v.Position = pts;
            }

            if (hasNorm)
            {
                var pts = new float[VertexFormat.FloatCountFromFormat(normLayout.Format)];
                ReadFloatData(data, normLayout, ref pts);
                v.Normal = pts;
            }

            v.UV = new float[uvLayouts.Length][];
            for (int j = 0; j < uvLayouts.Length; j++)
            {
                var u = uvLayouts[j];
                var pts = new float[VertexFormat.FloatCountFromFormat(u.Format)];
                float scale = j < uvScales.Length && uvScales[j] != 0 ? uvScales[j] : (uvScales.Length > 0 ? uvScales[0] : 0f);
                ReadUVData(data, u, ref pts, scale);
                v.UV[j] = pts;
            }

            if (hasBlendIdx)
            {
                var blendBytes = new byte[VertexFormat.ByteSizeFromFormat(blendIdxLayout.Format)];
                System.Array.Copy(data, blendIdxLayout.Offset, blendBytes, 0, blendBytes.Length);
                v.BlendIndices = blendBytes;
            }

            if (hasBlendWt)
            {
                var pts = new float[VertexFormat.FloatCountFromFormat(blendWtLayout.Format)];
                ReadFloatData(data, blendWtLayout, ref pts);
                v.BlendWeights = pts;
            }

            if (hasTangent)
            {
                var pts = new float[VertexFormat.FloatCountFromFormat(tangentLayout.Format)];
                ReadFloatData(data, tangentLayout, ref pts);
                v.Tangent = pts;
            }

            if (hasColor)
            {
                var pts = new float[VertexFormat.FloatCountFromFormat(colorLayout.Format)];
                ReadFloatData(data, colorLayout, ref pts);
                // Pack into ARGB uint
                byte r = (byte)(pts.Length > 0 ? Math.Clamp(pts[0] * 255f, 0, 255) : 0);
                byte g = (byte)(pts.Length > 1 ? Math.Clamp(pts[1] * 255f, 0, 255) : 0);
                byte b = (byte)(pts.Length > 2 ? Math.Clamp(pts[2] * 255f, 0, 255) : 0);
                byte a = (byte)(pts.Length > 3 ? Math.Clamp(pts[3] * 255f, 0, 255) : 255);
                v.Color = (uint)((a << 24) | (r << 16) | (g << 8) | b);
                v.HasColor = true;
            }

            vertices[i] = v;
        }

        return vertices;
    }

    /// <summary>
    /// Read UV data from raw vertex bytes, applying scale factors for compressed formats.
    /// </summary>
    public static void ReadUVData(byte[] data, VertexElement layout, ref float[] output, float scale)
    {
        int byteSize = VertexFormat.ByteSizeFromFormat(layout.Format);
        var element = new byte[byteSize];
        System.Array.Copy(data, layout.Offset, element, 0, byteSize);

        switch (layout.Format)
        {
            case ElementFormat.Short2:
                for (int i = 0; i < output.Length; i++)
                    output[i] += BitConverter.ToInt16(element, i * sizeof(short)) * scale;
                break;
            case ElementFormat.Short4:
                for (int i = 0; i < output.Length; i++)
                    output[i] += BitConverter.ToInt16(element, i * sizeof(short)) / (float)short.MaxValue;
                break;
            case ElementFormat.Short4_DropShadow:
                for (int i = 0; i < output.Length - 1; i++)
                    output[i] += BitConverter.ToInt16(element, i * sizeof(short)) / (float)short.MaxValue;
                output[output.Length - 1] += BitConverter.ToInt16(element, (output.Length - 1) * sizeof(short)) / 511f;
                break;
            default:
                ReadFloatData(data, layout, ref output);
                break;
        }
    }

    /// <summary>
    /// Read float data from raw vertex bytes, decoding various compressed formats.
    /// </summary>
    public static void ReadFloatData(byte[] data, VertexElement layout, ref float[] output)
    {
        int byteSize = VertexFormat.ByteSizeFromFormat(layout.Format);
        var element = new byte[byteSize];
        System.Array.Copy(data, layout.Offset, element, 0, byteSize);

        switch (layout.Format)
        {
            case ElementFormat.Float1:
            case ElementFormat.Float2:
            case ElementFormat.Float3:
            case ElementFormat.Float4:
                for (int i = 0; i < output.Length; i++)
                    output[i] += BitConverter.ToSingle(element, i * sizeof(float));
                break;

            case ElementFormat.ColorUByte4:
                switch (layout.Usage)
                {
                    case ElementUsage.Colour:
                        for (int i = 0; i < output.Length; i++)
                            output[i] += element[i] / (float)byte.MaxValue;
                        break;
                    case ElementUsage.BlendWeight:
                        for (int i = 0; i < output.Length; i++)
                            output[i] += element[ColorUByte4Map[i]] / (float)byte.MaxValue;
                        break;
                    case ElementUsage.Normal:
                    case ElementUsage.Tangent:
                        // Sims 4 byte-packed normals: value * 2.00787401f - 1.00787401f
                        // which is (value - 128) / 127 when value is in [0..1] (i.e. byte/255)
                        for (int i = 0; i < output.Length - 1; i++)
                            output[i] += element[2 - i] / 255f * 2.00787401f - 1.00787401f;
                        // Handedness / W component — same remap
                        output[output.Length - 1] = element[3] / 255f * 2.00787401f - 1.00787401f;
                        break;
                }
                break;

            case ElementFormat.UByte4:
            case ElementFormat.UByte4N:
                switch (layout.Usage)
                {
                    case ElementUsage.Normal:
                    case ElementUsage.Tangent:
                        // Sims 4 byte-packed normals: value * 2.00787401f - 1.00787401f
                        for (int i = 0; i < Math.Min(output.Length, 3); i++)
                            output[i] += element[i] / 255f * 2.00787401f - 1.00787401f;
                        if (output.Length > 3)
                            output[3] = element[3] / 255f * 2.00787401f - 1.00787401f;
                        break;
                    case ElementUsage.BlendWeight:
                        for (int i = 0; i < output.Length; i++)
                            output[i] += element[i] / (float)byte.MaxValue;
                        break;
                    default:
                        for (int i = 0; i < output.Length; i++)
                            output[i] += element[i] / (float)byte.MaxValue;
                        break;
                }
                break;

            case ElementFormat.Short2:
                for (int i = 0; i < output.Length; i++)
                    output[i] += BitConverter.ToInt16(element, i * sizeof(short)) / (float)short.MaxValue;
                break;

            case ElementFormat.Short4:
            {
                float scalar = BitConverter.ToUInt16(element, 3 * sizeof(short));
                if (scalar == 0) scalar = short.MaxValue;
                for (int i = 0; i < output.Length; i++)
                    output[i] += BitConverter.ToInt16(element, i * sizeof(short)) / scalar;
                break;
            }

            case ElementFormat.UShort4N:
            {
                float scalar = BitConverter.ToUInt16(element, 3 * sizeof(ushort));
                if (scalar == 0) scalar = 511;
                for (int i = 0; i < output.Length; i++)
                    output[i] += BitConverter.ToInt16(element, i * sizeof(short)) / scalar;
                break;
            }
        }
    }
}
