namespace Sims4Reader.Material;

/// <summary>
/// MTNF (Material Info) block. This is the newer material format used in
/// Sims 4 MATD chunks (version >= 0x103) and also parsed inline from GEOM resources.
///
/// On-disk layout:
///   Tag:        4 bytes ("MTNF" or "MTRL" accepted)
///   Unknown1:   4 bytes (uint)
///   DataLength: 4 bytes (uint) -- byte length of the shader data payload
///   [ShaderData entries]
///
/// This is NOT a standalone IResource. It is parsed inline from MATD or GEOM.
/// </summary>
public class MaterialInfo
{
    private const uint MtnfTag = 0x464E544D; // "MTNF"
    private const uint MtrlTag = 0x4C52544D; // "MTRL"

    public uint Unknown1 { get; set; }
    public List<ShaderData> ShaderEntries { get; set; } = new();

    /// <summary>
    /// Parse a MTNF block from the reader. The reader should be positioned at the
    /// start of the MTNF tag.
    /// </summary>
    /// <param name="reader">The binary reader.</param>
    /// <param name="isGeom">True if being parsed from a GEOM resource context (affects texture ref parsing).</param>
    public void Parse(BinaryReader reader, bool isGeom = false)
    {
        long start = reader.BaseStream.Position;

        uint tag = reader.ReadUInt32();
        if (tag != MtnfTag && tag != MtrlTag)
            throw new InvalidDataException(
                $"Invalid MTNF tag: 0x{tag:X8} at 0x{reader.BaseStream.Position:X8}; expected 'MTNF' or 'MTRL'");

        Unknown1 = reader.ReadUInt32();
        int dataLength = reader.ReadInt32();

        ShaderEntries = ShaderDataList.Parse(reader, start, dataLength, isGeom);
    }
}
