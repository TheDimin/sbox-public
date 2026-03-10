namespace Sims4Reader.Image;

/// <summary>
/// DST (shuffled DXT) image resource. The pixel data blocks are rearranged relative
/// to standard DDS/DXT format. Use <see cref="ToDds"/> to unshuffle back to a standard DDS byte array.
/// </summary>
public class DstImage : IResource
{
    private const int DdsHeaderSize = 128; // 4-byte signature + 124-byte header

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsShuffled { get; private set; }
    public FourCC Format { get; private set; }
    public byte[] RawData { get; private set; } = Array.Empty<byte>();

    private DdsHeader? header;

    public void Parse(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty) { RawData = Array.Empty<byte>(); return; }

        using var stream = new MemoryStream(data.ToArray());
        header = new DdsHeader();
        header.Parse(stream);

        Format = header.PixelFormat.Fourcc;
        IsShuffled = Format is FourCC.DST1 or FourCC.DST3 or FourCC.DST5;

        Width = header.Width;
        Height = header.Height;

        stream.Position = 0;
        var reader = new BinaryReader(stream);
        RawData = reader.ReadBytes((int)stream.Length);
    }

    /// <summary>
    /// Converts DST data to standard DDS format by unshuffling the pixel blocks.
    /// Returns the full DDS file as a byte array, or null if no data is loaded.
    /// </summary>
    public byte[]? ToDds()
    {
        if (RawData.Length == 0 || header == null) return null;
        if (!IsShuffled) return (byte[])RawData.Clone();

        return Unshuffle(header, RawData);
    }

    private static byte[] Unshuffle(DdsHeader header, byte[] sourceData)
    {
        int dataSize = sourceData.Length - DdsHeaderSize;
        var temp = new byte[dataSize];
        Array.Copy(sourceData, DdsHeaderSize, temp, 0, dataSize);

        using var result = new MemoryStream();
        var writer = new BinaryWriter(result);

        // Clone the header and change the FourCC from DST* to DXT*
        var outputPixelFormat = new DdsPixelFormat
        {
            Flags = header.PixelFormat.Flags,
            Fourcc = header.PixelFormat.Fourcc,
            RGBBitCount = header.PixelFormat.RGBBitCount,
            RedBitMask = header.PixelFormat.RedBitMask,
            GreenBitMask = header.PixelFormat.GreenBitMask,
            BlueBitMask = header.PixelFormat.BlueBitMask,
            AlphaBitMask = header.PixelFormat.AlphaBitMask,
        };

        var outputHeader = new DdsHeader
        {
            Flags = header.Flags,
            Height = header.Height,
            Width = header.Width,
            PitchOrLinearSize = header.PitchOrLinearSize,
            Depth = header.Depth,
            MipMapCount = header.MipMapCount,
            Reserved1 = header.Reserved1,
            PixelFormat = outputPixelFormat,
            SurfaceFlags = header.SurfaceFlags,
            CubemapFlags = header.CubemapFlags,
            Reserved2 = header.Reserved2,
        };

        if (header.PixelFormat.Fourcc == FourCC.DST1)
        {
            outputHeader.PixelFormat.Fourcc = FourCC.DXT1;
            writer.Write(DdsHeader.Signature);
            outputHeader.Write(writer);

            int blockOffset2 = 0;
            int blockOffset3 = dataSize >> 1;
            int count = (blockOffset3 - blockOffset2) / 4;

            for (int i = 0; i < count; i++)
            {
                result.Write(temp, blockOffset2, 4);
                result.Write(temp, blockOffset3, 4);
                blockOffset2 += 4;
                blockOffset3 += 4;
            }
        }
        else if (header.PixelFormat.Fourcc == FourCC.DST3)
        {
            outputHeader.PixelFormat.Fourcc = FourCC.DXT3;
            writer.Write(DdsHeader.Signature);
            outputHeader.Write(writer);
            throw new NotSupportedException("DST3 unshuffling is not yet implemented (no samples available).");
        }
        else if (header.PixelFormat.Fourcc == FourCC.DST5)
        {
            outputHeader.PixelFormat.Fourcc = FourCC.DXT5;
            writer.Write(DdsHeader.Signature);
            outputHeader.Write(writer);

            int blockOffset0 = 0;
            int blockOffset2 = blockOffset0 + (dataSize >> 3);
            int blockOffset1 = blockOffset2 + (dataSize >> 2);
            int blockOffset3 = blockOffset1 + (6 * dataSize >> 4);
            int count = (blockOffset2 - blockOffset0) / 2;

            for (int i = 0; i < count; i++)
            {
                result.Write(temp, blockOffset0, 2);
                result.Write(temp, blockOffset1, 6);
                result.Write(temp, blockOffset2, 4);
                result.Write(temp, blockOffset3, 4);

                blockOffset0 += 2;
                blockOffset1 += 6;
                blockOffset2 += 4;
                blockOffset3 += 4;
            }
        }

        return result.ToArray();
    }
}
