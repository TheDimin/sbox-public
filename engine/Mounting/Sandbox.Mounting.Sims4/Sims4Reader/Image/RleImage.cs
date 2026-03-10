namespace Sims4Reader.Image;

/// <summary>
/// RLE version identifiers for Sims 4 RLE-compressed images.
/// </summary>
public enum RleVersion : uint
{
    RLE2 = 0x32454C52,
    RLES = 0x53454C52,
}

/// <summary>
/// Mipmap header entry for RLE image data.
/// </summary>
public class RleMipHeader
{
    public int CommandOffset { get; set; }
    public int Offset0 { get; set; }
    public int Offset1 { get; set; }
    public int Offset2 { get; set; }
    public int Offset3 { get; set; }
    public int Offset4 { get; set; }
}

/// <summary>
/// RLE header information (Sims 4 custom format, not a standard DDS header).
/// The on-disk format is: FourCC(4) + Version(4) + Width(2) + Height(2) + MipCount(2) + Unknown(2) = 16 bytes,
/// followed by per-mip headers.
/// </summary>
public class RleHeader
{
    public FourCC Fourcc { get; set; } = FourCC.DXT5;
    public RleVersion Version { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int MipCount { get; set; }
    public ushort Unknown0E { get; set; }

    public bool HasSpecular => Version == RleVersion.RLES;

    public void Parse(BinaryReader reader)
    {
        uint fourcc = reader.ReadUInt32();
        if (fourcc != (uint)FourCC.DXT5)
            throw new InvalidDataException($"Expected RLE FourCC 0x{(uint)FourCC.DXT5:X8}, read 0x{fourcc:X8}");

        Version = (RleVersion)reader.ReadUInt32();
        Width = reader.ReadUInt16();
        Height = reader.ReadUInt16();
        MipCount = reader.ReadUInt16();
        Unknown0E = reader.ReadUInt16();

        if (Unknown0E != 0)
            throw new InvalidDataException($"Expected 0 for Unknown0E, read 0x{Unknown0E:X4}");
    }
}

/// <summary>
/// RLE-compressed image resource used by The Sims 4.
/// Supports both RLE2 and RLES (specular) variants.
/// </summary>
public class RleImage : IResource
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int MipCount { get; private set; }
    public RleVersion Version { get; private set; }
    public bool HasSpecular { get; private set; }
    public byte[] RawData { get; private set; } = Array.Empty<byte>();
    public RleMipHeader[] MipHeaders { get; private set; } = Array.Empty<RleMipHeader>();

    private RleHeader? header;

    public void Parse(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) { RawData = Array.Empty<byte>(); return; }

        var bytes = data.ToArray();
        using var stream = new MemoryStream(bytes);
        var reader = new BinaryReader(stream);

        header = new RleHeader();
        header.Parse(reader);

        Width = header.Width;
        Height = header.Height;
        MipCount = header.MipCount;
        Version = header.Version;
        HasSpecular = header.HasSpecular;

        MipHeaders = new RleMipHeader[MipCount + 1];

        for (int i = 0; i < MipCount; i++)
        {
            var mh = new RleMipHeader
            {
                CommandOffset = reader.ReadInt32(),
                Offset2 = reader.ReadInt32(),
                Offset3 = reader.ReadInt32(),
                Offset0 = reader.ReadInt32(),
                Offset1 = reader.ReadInt32(),
            };
            if (Version == RleVersion.RLES)
                mh.Offset4 = reader.ReadInt32();

            MipHeaders[i] = mh;
        }

        // Sentinel entry
        MipHeaders[MipCount] = new RleMipHeader
        {
            CommandOffset = MipHeaders[0].Offset2,
            Offset2 = MipHeaders[0].Offset3,
            Offset3 = MipHeaders[0].Offset0,
            Offset0 = MipHeaders[0].Offset1,
        };

        if (Version == RleVersion.RLES)
        {
            MipHeaders[MipCount].Offset1 = MipHeaders[0].Offset4;
            MipHeaders[MipCount].Offset4 = bytes.Length;
        }
        else
        {
            MipHeaders[MipCount].Offset1 = bytes.Length;
        }

        RawData = bytes;
    }

    /// <summary>
    /// Decompresses the RLE data into a standard DDS file byte array.
    /// Returns null if no data is loaded.
    /// </summary>
    public byte[]? ToDds()
    {
        if (header == null || RawData.Length == 0) return null;

        using var output = new MemoryStream();
        var writer = new BinaryWriter(output);

        // Write DDS header
        WriteDdsHeader(writer, header);

        var fullTransparentAlpha = new byte[] { 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var fullOpaqueAlpha = new byte[] { 0x00, 0x05, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        if (Version == RleVersion.RLE2)
        {
            DecompressRle2(writer, fullTransparentAlpha, fullOpaqueAlpha);
        }
        else
        {
            DecompressRles(writer, fullTransparentAlpha, fullOpaqueAlpha);
        }

        return output.ToArray();
    }

    private void DecompressRle2(BinaryWriter writer, byte[] fullTransparentAlpha, byte[] fullOpaqueAlpha)
    {
        for (int i = 0; i < MipCount; i++)
        {
            var mipHeader = MipHeaders[i];
            var nextMipHeader = MipHeaders[i + 1];

            int blockOffset2 = mipHeader.Offset2;
            int blockOffset3 = mipHeader.Offset3;
            int blockOffset0 = mipHeader.Offset0;
            int blockOffset1 = mipHeader.Offset1;

            for (int commandOffset = mipHeader.CommandOffset;
                commandOffset < nextMipHeader.CommandOffset;
                commandOffset += 2)
            {
                var command = BitConverter.ToUInt16(RawData, commandOffset);
                int op = command & 3;
                int count = command >> 2;

                if (op == 0)
                {
                    for (int j = 0; j < count; j++)
                    {
                        writer.Write(fullTransparentAlpha, 0, 8);
                        writer.Write(fullTransparentAlpha, 0, 8);
                    }
                }
                else if (op == 1)
                {
                    for (int j = 0; j < count; j++)
                    {
                        writer.Write(RawData, blockOffset0, 2);
                        writer.Write(RawData, blockOffset1, 6);
                        writer.Write(RawData, blockOffset2, 4);
                        writer.Write(RawData, blockOffset3, 4);
                        blockOffset2 += 4;
                        blockOffset3 += 4;
                        blockOffset0 += 2;
                        blockOffset1 += 6;
                    }
                }
                else if (op == 2)
                {
                    for (int j = 0; j < count; j++)
                    {
                        writer.Write(fullOpaqueAlpha, 0, 8);
                        writer.Write(RawData, blockOffset2, 4);
                        writer.Write(RawData, blockOffset3, 4);
                        blockOffset2 += 4;
                        blockOffset3 += 4;
                    }
                }
                else
                {
                    throw new NotSupportedException($"Unsupported RLE2 opcode: {op}");
                }
            }

            if (blockOffset0 != nextMipHeader.Offset0 ||
                blockOffset1 != nextMipHeader.Offset1 ||
                blockOffset2 != nextMipHeader.Offset2 ||
                blockOffset3 != nextMipHeader.Offset3)
            {
                throw new InvalidOperationException("RLE2 block offset mismatch after decompression.");
            }
        }
    }

    private void DecompressRles(BinaryWriter writer, byte[] fullTransparentAlpha, byte[] fullOpaqueAlpha)
    {
        for (int i = 0; i < MipCount; i++)
        {
            var mipHeader = MipHeaders[i];
            var nextMipHeader = MipHeaders[i + 1];

            int blockOffset2 = mipHeader.Offset2;
            int blockOffset3 = mipHeader.Offset3;
            int blockOffset0 = mipHeader.Offset0;
            int blockOffset1 = mipHeader.Offset1;
            int blockOffset4 = mipHeader.Offset4;

            for (int commandOffset = mipHeader.CommandOffset;
                commandOffset < nextMipHeader.CommandOffset;
                commandOffset += 2)
            {
                var command = BitConverter.ToUInt16(RawData, commandOffset);
                int op = command & 3;
                int count = command >> 2;

                if (op == 0)
                {
                    for (int j = 0; j < count; j++)
                    {
                        writer.Write(fullTransparentAlpha, 0, 8);
                        writer.Write(fullTransparentAlpha, 0, 8);
                    }
                }
                else if (op == 1)
                {
                    for (int j = 0; j < count; j++)
                    {
                        writer.Write(RawData, blockOffset0, 2);
                        writer.Write(RawData, blockOffset1, 6);
                        blockOffset0 += 2;
                        blockOffset1 += 6;

                        writer.Write(RawData, blockOffset2, 4);
                        writer.Write(RawData, blockOffset3, 4);
                        blockOffset2 += 4;
                        blockOffset3 += 4;

                        blockOffset4 += 16;
                    }
                }
                else if (op == 2)
                {
                    for (int j = 0; j < count; j++)
                    {
                        writer.Write(RawData, blockOffset0, 2);
                        writer.Write(RawData, blockOffset1, 6);
                        writer.Write(RawData, blockOffset2, 4);
                        writer.Write(RawData, blockOffset3, 4);
                        blockOffset2 += 4;
                        blockOffset3 += 4;
                        blockOffset0 += 2;
                        blockOffset1 += 6;
                    }
                }
                else
                {
                    throw new NotSupportedException($"Unsupported RLES opcode: {op}");
                }
            }

            if (blockOffset0 != nextMipHeader.Offset0 ||
                blockOffset1 != nextMipHeader.Offset1 ||
                blockOffset2 != nextMipHeader.Offset2 ||
                blockOffset3 != nextMipHeader.Offset3 ||
                blockOffset4 != nextMipHeader.Offset4)
            {
                throw new InvalidOperationException("RLES block offset mismatch after decompression.");
            }
        }
    }

    private static void WriteDdsHeader(BinaryWriter writer, RleHeader rleHeader)
    {
        // Write a standard DDS header based on the RLE header info.
        writer.Write(DdsHeader.Signature); // "DDS "

        var ddsHeader = new DdsHeader
        {
            Flags = HeaderFlags.Texture,
            Height = rleHeader.Height,
            Width = rleHeader.Width,
            PitchOrLinearSize = 0,
            Depth = 1,
            MipMapCount = (uint)rleHeader.MipCount,
            PixelFormat = new DdsPixelFormat
            {
                Flags = PixelFormatFlags.FourCC,
                Fourcc = FourCC.DXT5,
            },
        };

        ddsHeader.Write(writer);
    }
}
