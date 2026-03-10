namespace Sims4Reader.Image;

/// <summary>
/// DDS FourCC compression format identifiers.
/// </summary>
public enum FourCC : uint
{
    DST1 = 0x31545344,
    DST3 = 0x33545344,
    DST5 = 0x35545344,
    DXT1 = 0x31545844,
    DXT3 = 0x33545844,
    DXT5 = 0x35545844,
    None = 0x00000000,
}

/// <summary>
/// Flags describing the contents of a DDS pixel format structure.
/// </summary>
[Flags]
public enum PixelFormatFlags : uint
{
    FourCC = 0x00000004,
    RGB = 0x00000040,
    RGBA = 0x00000041,
    Luminance = 0x00020000,
}

/// <summary>
/// DDS header flags.
/// </summary>
[Flags]
public enum HeaderFlags : uint
{
    Texture = 0x00001007,    // DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT
    Mipmap = 0x00020000,     // DDSD_MIPMAPCOUNT
    Volume = 0x00800000,     // DDSD_DEPTH
    Pitch = 0x00000008,      // DDSD_PITCH
    LinearSize = 0x00080000, // DDSD_LINEARSIZE
}

/// <summary>
/// DDS pixel format structure (32 bytes on disk).
/// </summary>
public class DdsPixelFormat
{
    public const uint StructureSize = 32; // 8 * 4

    public uint Size => StructureSize;
    public PixelFormatFlags Flags { get; set; } = PixelFormatFlags.FourCC;
    public FourCC Fourcc { get; set; } = FourCC.DXT5;
    public uint RGBBitCount { get; set; }
    public uint RedBitMask { get; set; }
    public uint GreenBitMask { get; set; }
    public uint BlueBitMask { get; set; }
    public uint AlphaBitMask { get; set; }

    public void Parse(BinaryReader reader)
    {
        uint size = reader.ReadUInt32();
        if (size != StructureSize)
            throw new InvalidDataException($"Expected pixel format size 0x{StructureSize:X8}, read 0x{size:X8}");

        uint flags = reader.ReadUInt32();
        if (!Enum.IsDefined(typeof(PixelFormatFlags), flags))
            throw new InvalidDataException($"Bad pixel format flag: 0x{flags:X8}");
        Flags = (PixelFormatFlags)flags;

        uint fourCC = reader.ReadUInt32();
        if (!Enum.IsDefined(typeof(FourCC), fourCC))
            throw new InvalidDataException($"Unexpected FourCC value: 0x{fourCC:X8}");
        Fourcc = (FourCC)fourCC;

        RGBBitCount = reader.ReadUInt32();
        RedBitMask = reader.ReadUInt32();
        GreenBitMask = reader.ReadUInt32();
        BlueBitMask = reader.ReadUInt32();
        AlphaBitMask = reader.ReadUInt32();
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Size);
        writer.Write((uint)Flags);
        writer.Write((uint)Fourcc);
        writer.Write(RGBBitCount);
        writer.Write(RedBitMask);
        writer.Write(GreenBitMask);
        writer.Write(BlueBitMask);
        writer.Write(AlphaBitMask);
    }
}

/// <summary>
/// Standard DDS file header (124 bytes on disk, excluding the 4-byte magic number).
/// </summary>
public class DdsHeader
{
    public const uint Signature = 0x20534444; // "DDS "

    /// <summary>Header size: 18 DWORDs + pixel format (8 DWORDs) + 5 DWORDs = 31 DWORDs = 124 bytes.</summary>
    public uint Size => (18 * 4) + DdsPixelFormat.StructureSize + (5 * 4);
    public HeaderFlags Flags { get; set; }
    public int Height { get; set; }
    public int Width { get; set; }
    public uint PitchOrLinearSize { get; set; }
    public int Depth { get; set; } = 1;
    public uint MipMapCount { get; set; }
    public byte[] Reserved1 { get; set; } = new byte[11 * 4];
    public DdsPixelFormat PixelFormat { get; set; } = new();
    public uint SurfaceFlags { get; set; }
    public uint CubemapFlags { get; set; }
    public byte[] Reserved2 { get; set; } = new byte[3 * 4];

    /// <summary>
    /// Parse a full DDS header from the stream (expects the 4-byte signature has NOT been consumed yet).
    /// </summary>
    public void Parse(Stream stream)
    {
        var reader = new BinaryReader(stream);
        uint sig = reader.ReadUInt32();
        if (sig != Signature)
            throw new InvalidDataException($"Expected DDS signature 0x{Signature:X8}, read 0x{sig:X8}");

        uint size = reader.ReadUInt32();
        if (size != Size)
            throw new InvalidDataException($"Expected header size 0x{Size:X8}, read 0x{size:X8}");

        Flags = (HeaderFlags)reader.ReadUInt32();
        if ((Flags & HeaderFlags.Texture) != HeaderFlags.Texture)
            throw new InvalidDataException($"Expected texture flags 0x{(uint)HeaderFlags.Texture:X8}, read 0x{(uint)Flags:X8}");

        Height = reader.ReadInt32();
        Width = reader.ReadInt32();
        if (Height > ushort.MaxValue || Width > ushort.MaxValue)
            throw new InvalidDataException("Invalid width or height");

        PitchOrLinearSize = reader.ReadUInt32();
        Depth = reader.ReadInt32();
        if (Depth != 0 && Depth != 1)
            throw new InvalidDataException($"Expected depth 0 or 1, read 0x{Depth:X8}");

        MipMapCount = reader.ReadUInt32();
        if (MipMapCount > 16)
            throw new InvalidDataException($"Expected mipmap count <= 16, read 0x{MipMapCount:X8}");

        Reserved1 = reader.ReadBytes(11 * 4);
        PixelFormat = new DdsPixelFormat();
        PixelFormat.Parse(reader);
        SurfaceFlags = reader.ReadUInt32();
        CubemapFlags = reader.ReadUInt32();
        Reserved2 = reader.ReadBytes(3 * 4);
    }

    /// <summary>
    /// Write the header (excluding signature) to the stream.
    /// </summary>
    public void Write(BinaryWriter writer)
    {
        writer.Write(Size);
        writer.Write((uint)Flags);
        writer.Write(Height);
        writer.Write(Width);
        writer.Write(PitchOrLinearSize);
        writer.Write(Depth);
        writer.Write(MipMapCount);
        writer.Write(Reserved1);
        PixelFormat.Write(writer);
        writer.Write(SurfaceFlags);
        writer.Write(CubemapFlags);
        writer.Write(Reserved2);
    }
}

/// <summary>
/// Represents a parsed DDS image file. Exposes header metadata and raw pixel data.
/// This is a helper type used by DstImage and RleImage for DDS conversion output.
/// </summary>
public class DdsImage
{
    public int Width { get; set; }
    public int Height { get; set; }
    public FourCC Format { get; set; }
    public byte[] RawData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Parse a full DDS file from raw bytes.
    /// </summary>
    public void Parse(byte[] ddsFileBytes)
    {
        using var stream = new MemoryStream(ddsFileBytes);
        var header = new DdsHeader();
        header.Parse(stream);

        Width = header.Width;
        Height = header.Height;
        Format = header.PixelFormat.Fourcc;

        var reader = new BinaryReader(stream);
        RawData = reader.ReadBytes((int)(stream.Length - stream.Position));
    }

    /// <summary>
    /// The complete DDS file bytes (header + pixel data).
    /// </summary>
    public byte[] ToDdsFileBytes()
    {
        return RawData; // Caller is expected to hold the full file bytes if needed
    }
}
