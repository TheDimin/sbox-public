namespace Sims4Reader.Image;

/// <summary>
/// Sims 4 thumbnail image resource. These are JPEG images that may contain
/// an embedded PNG alpha channel in an ALFA block after byte offset 24.
///
/// No System.Drawing dependency: raw JPEG bytes and alpha PNG bytes are
/// exposed separately for the caller to composite as needed.
/// </summary>
public class ThumbnailImage : IResource
{
    private const uint AlfaSignature = 0x41464C41; // "ALFA"

    /// <summary>
    /// The raw bytes of the complete original resource (JPEG with embedded ALFA).
    /// </summary>
    public byte[] RawData { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// The raw JPEG image bytes (the full resource data, including any ALFA block).
    /// Use this to decode the color/RGB component. The JPEG decoder will ignore
    /// the ALFA extension block since it sits in an APP0 marker segment.
    /// </summary>
    public byte[] JpegData { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// The raw PNG bytes for the alpha channel, or null if there is no embedded alpha.
    /// When present, the PNG dimensions match the JPEG dimensions. The red channel
    /// of this grayscale/RGB PNG represents the alpha values.
    /// </summary>
    public byte[]? AlphaData { get; private set; }

    /// <summary>
    /// True if the thumbnail contains an embedded ALFA (PNG alpha channel) block.
    /// </summary>
    public bool HasAlpha { get; private set; }

    public void Parse(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            RawData = Array.Empty<byte>();
            JpegData = Array.Empty<byte>();
            AlphaData = null;
            HasAlpha = false;
            return;
        }

        RawData = data.ToArray();
        JpegData = RawData;

        // Try to find the ALFA block.
        // The original code reads 24 bytes (JPEG header), then checks for the ALFA signature.
        if (RawData.Length > 28)
        {
            using var stream = new MemoryStream(RawData);
            var reader = new BinaryReader(stream);
            stream.Position = 24;

            uint sig = reader.ReadUInt32();
            if (sig == AlfaSignature)
            {
                HasAlpha = true;

                // Length is stored big-endian (byte-swapped int32)
                int lengthRaw = reader.ReadInt32();
                int length = ((lengthRaw & unchecked((int)0xFF000000)) >> 24)
                           | ((lengthRaw & 0x00FF0000) >> 8)
                           | ((lengthRaw & 0x0000FF00) << 8)
                           | ((lengthRaw & 0x000000FF) << 24);

                if (length > 0 && stream.Position + length <= stream.Length)
                {
                    AlphaData = reader.ReadBytes(length);
                }
                else
                {
                    // Invalid length; mark as no alpha
                    HasAlpha = false;
                    AlphaData = null;
                }
            }
            else
            {
                HasAlpha = false;
                AlphaData = null;
            }
        }
    }
}
