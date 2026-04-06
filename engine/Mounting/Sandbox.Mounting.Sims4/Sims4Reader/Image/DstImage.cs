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

        bool isDst1 = header.PixelFormat.Fourcc == FourCC.DST1;
        bool isDst5 = header.PixelFormat.Fourcc == FourCC.DST5;

        int bytesPerBlock = isDst1 ? 8 : 16;

        // Total blocks across all mips (from data size)
        int totalBlocks = dataSize / bytesPerBlock;

        // Compute mip0 block count
        int mip0Bw = Math.Max(1, (header.Width + 3) / 4);
        int mip0Bh = Math.Max(1, (header.Height + 3) / 4);
        int mip0Blocks = mip0Bw * mip0Bh;
        int mip0Bytes = mip0Blocks * bytesPerBlock;

        // Build output header: mip0 only, DXT* FourCC
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
            Flags = header.Flags & ~HeaderFlags.Mipmap,
            Height = header.Height,
            Width = header.Width,
            PitchOrLinearSize = (uint)mip0Bytes,
            Depth = header.Depth,
            MipMapCount = 1,
            Reserved1 = header.Reserved1,
            PixelFormat = outputPixelFormat,
            SurfaceFlags = header.SurfaceFlags,
            CubemapFlags = header.CubemapFlags,
            Reserved2 = header.Reserved2,
        };

        if (isDst1)
            outputHeader.PixelFormat.Fourcc = FourCC.DXT1;
        else if (header.PixelFormat.Fourcc == FourCC.DST3)
        {
            outputHeader.PixelFormat.Fourcc = FourCC.DXT3;
            throw new NotSupportedException("DST3 unshuffling is not yet implemented.");
        }
        else if (isDst5)
            outputHeader.PixelFormat.Fourcc = FourCC.DXT5;

        using var result = new MemoryStream(DdsHeaderSize + mip0Bytes);
        var writer = new BinaryWriter(result);
        writer.Write(DdsHeader.Signature);
        outputHeader.Write(writer);

        // Unshuffle only mip0 blocks using dataSize-based section offsets
        if (isDst1)
        {
            // DST1 sections (across all mips): [color_endpoints: 4B × N] [color_indices: 4B × N]
            int sec0 = 0;
            int sec1 = totalBlocks * 4;

            for (int i = 0; i < mip0Blocks; i++)
            {
                result.Write(temp, sec0 + i * 4, 4);
                result.Write(temp, sec1 + i * 4, 4);
            }
        }
        else if (isDst5)
        {
            // DST5 sections (across all mips): [alpha_ep: 2B × N] [color_ep: 4B × N] [alpha_idx: 6B × N] [color_idx: 4B × N]
            int sec0 = 0;                          // alpha endpoints
            int sec1 = totalBlocks * 2;             // color endpoints
            int sec2 = sec1 + totalBlocks * 4;      // alpha indices
            int sec3 = sec2 + totalBlocks * 6;      // color indices

            for (int i = 0; i < mip0Blocks; i++)
            {
                result.Write(temp, sec0 + i * 2, 2);   // alpha endpoints
                result.Write(temp, sec2 + i * 6, 6);   // alpha indices
                result.Write(temp, sec1 + i * 4, 4);   // color endpoints
                result.Write(temp, sec3 + i * 4, 4);   // color indices
            }
        }

        return result.ToArray();
    }
}
