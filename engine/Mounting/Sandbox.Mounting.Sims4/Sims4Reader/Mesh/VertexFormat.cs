using Sims4Reader.Rcol;

namespace Sims4Reader.Mesh;

/// <summary>
/// Vertex element usage type, defining what the element represents.
/// </summary>
public enum ElementUsage : byte
{
    Position = 0,
    Normal = 1,
    UV = 2,
    BlendIndex = 3,
    BlendWeight = 4,
    Tangent = 5,
    Colour = 6,
}

/// <summary>
/// Vertex element data format, defining how the element is encoded.
/// </summary>
public enum ElementFormat : byte
{
    Float1 = 0,
    Float2 = 1,
    Float3 = 2,
    Float4 = 3,
    UByte4 = 4,
    ColorUByte4 = 5,
    Short2 = 6,
    Short4 = 7,
    UByte4N = 8,
    Short2N = 9,
    Short4N = 10,
    UShort2N = 11,
    UShort4N = 12,
    Dec3N = 13,
    UDec3N = 14,
    Float16_2 = 15,
    Float16_4 = 16,
    Short4_DropShadow = 0xFF,
}

/// <summary>
/// A single element layout entry within a vertex format definition.
/// </summary>
public struct VertexElement
{
    public ElementUsage Usage;
    public byte UsageIndex;
    public ElementFormat Format;
    public byte Offset;
}

/// <summary>
/// VRTF RCOL chunk - defines the layout of vertex data in a vertex buffer.
/// </summary>
public class VertexFormat : RcolChunk
{
    private const uint VrtfTag = 0x46545256; // "VRTF"

    public uint Version { get; set; }
    public int Stride { get; set; }
    public bool ExtendedFormat { get; set; }
    public List<VertexElement> Elements { get; set; } = new();

    public override void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences)
    {
        Tag = reader.ReadUInt32();
        Version = reader.ReadUInt32();
        Stride = reader.ReadInt32();
        int count = reader.ReadInt32();
        ExtendedFormat = reader.ReadUInt32() > 0;

        Elements = new List<VertexElement>(count);
        for (int i = 0; i < count; i++)
        {
            var el = new VertexElement
            {
                Usage = (ElementUsage)reader.ReadByte(),
                UsageIndex = reader.ReadByte(),
                Format = (ElementFormat)reader.ReadByte(),
                Offset = reader.ReadByte(),
            };
            Elements.Add(el);
        }
    }

    /// <summary>
    /// Returns the number of logical float values for a given element format.
    /// </summary>
    public static int FloatCountFromFormat(ElementFormat format)
    {
        return format switch
        {
            ElementFormat.Float1 => 1,
            ElementFormat.Float2 or ElementFormat.UShort2N or ElementFormat.Short2 => 2,
            ElementFormat.Short4 or ElementFormat.Short4N or ElementFormat.UByte4N
                or ElementFormat.UShort4N or ElementFormat.Float3 => 3,
            ElementFormat.ColorUByte4 or ElementFormat.Float4 or ElementFormat.Short4_DropShadow => 4,
            _ => throw new NotSupportedException($"Unsupported element format: {format}"),
        };
    }

    /// <summary>
    /// Returns the byte size of a given element format.
    /// </summary>
    public static int ByteSizeFromFormat(ElementFormat format)
    {
        return format switch
        {
            ElementFormat.Float1 or ElementFormat.UByte4 or ElementFormat.ColorUByte4
                or ElementFormat.UByte4N or ElementFormat.UShort2N or ElementFormat.Short2 => 4,
            ElementFormat.UShort4N or ElementFormat.Float2 or ElementFormat.Short4
                or ElementFormat.Short4N or ElementFormat.Short4_DropShadow => 8,
            ElementFormat.Float3 => 12,
            ElementFormat.Float4 => 16,
            _ => throw new NotSupportedException($"Unsupported element format: {format}"),
        };
    }
}
