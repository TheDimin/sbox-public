using System.Buffers;
using System.IO.Compression;

namespace Sims4Reader;

/// <summary>
/// Handles DEFLATE (zlib) and legacy Sims 3 decompression of DBPF resources.
/// Uses built-in System.IO.Compression — no NuGet dependencies.
/// </summary>
internal static class Compression
{
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, int memSize)
    {
        if (compressed.Length < 2)
            throw new InvalidDataException("Compressed data too short");

        byte[] output = new byte[memSize];

        if (compressed[0] == 0x78)
        {
            // DEFLATE (zlib) — skip 2-byte zlib header, write directly to avoid ToArray() copy
            using var source = new MemoryStream(compressed.Length - 2);
            source.Write(compressed[2..]);
            source.Position = 0;
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            int totalRead = 0;
            while (totalRead < memSize)
            {
                int read = deflate.Read(output, totalRead, memSize - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
        }
        else if (compressed[1] == 0xFB)
        {
            // Legacy Sims 3 compression
            DecompressLegacy(compressed, output, compressed[0]);
        }
        else
        {
            throw new InvalidDataException($"Unrecognized compression header: 0x{compressed[0]:X2}{compressed[1]:X2}");
        }

        return output;
    }

    /// <summary>
    /// Zero-copy overload: wraps the existing byte[] in a MemoryStream without allocating
    /// an internal buffer copy. Use when the compressed data is already in a byte[].
    /// </summary>
    public static byte[] Decompress(byte[] compressed, int offset, int length, int memSize)
    {
        if (length < 2)
            throw new InvalidDataException("Compressed data too short");

        byte[] output = new byte[memSize];

        if (compressed[offset] == 0x78)
        {
            // DEFLATE (zlib) — skip 2-byte zlib header, wrap array directly (no copy)
            using var source = new MemoryStream(compressed, offset + 2, length - 2, writable: false);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            int totalRead = 0;
            while (totalRead < memSize)
            {
                int read = deflate.Read(output, totalRead, memSize - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
        }
        else if (compressed[offset + 1] == 0xFB)
        {
            DecompressLegacy(compressed.AsSpan(offset, length), output, compressed[offset]);
        }
        else
        {
            throw new InvalidDataException($"Unrecognized compression header: 0x{compressed[offset]:X2}{compressed[offset + 1]:X2}");
        }

        return output;
    }

    /// <summary>
    /// Decompress directly from a raw memory pointer (memory-mapped file).
    /// Eliminates all intermediate buffer allocations — reads compressed data
    /// straight from mapped pages via UnmanagedMemoryStream.
    /// </summary>
    /// <summary>
    /// Decompress directly from a raw memory pointer (memory-mapped file).
    /// Uses UnmanagedMemoryStream + ZLibStream for zero-copy decompression
    /// of zlib data straight from mapped pages.
    /// </summary>
    public static unsafe byte[] DecompressMapped(byte* compressed, int length, int memSize)
    {
        if (length < 2)
            throw new InvalidDataException("Compressed data too short");

        byte[] output = new byte[memSize];

        if (compressed[0] == 0x78)
        {
            // ZLibStream handles the zlib header natively — no manual skip needed
            using var source = new UnmanagedMemoryStream(compressed, length);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            int totalRead = 0;
            while (totalRead < memSize)
            {
                int read = zlib.Read(output, totalRead, memSize - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
        }
        else if (compressed[1] == 0xFB)
        {
            var span = new ReadOnlySpan<byte>(compressed, length);
            DecompressLegacy(span, output, compressed[0]);
        }
        else
        {
            throw new InvalidDataException($"Unrecognized compression header: 0x{compressed[0]:X2}{compressed[1]:X2}");
        }

        return output;
    }

    /// <summary>
    /// Partial decompression from mapped memory — only decompresses up to maxOutput bytes.
    /// Much faster than full decompression when only the header portion of data is needed.
    /// </summary>
    public static unsafe byte[] DecompressPartial(byte* compressed, int length, int maxOutput)
    {
        if (length < 2)
            throw new InvalidDataException("Compressed data too short");

        byte[] output = new byte[maxOutput];

        if (compressed[0] == 0x78)
        {
            using var source = new UnmanagedMemoryStream(compressed, length);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            int totalRead = 0;
            while (totalRead < maxOutput)
            {
                int read = zlib.Read(output, totalRead, maxOutput - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
            if (totalRead < maxOutput)
                return output.AsSpan(0, totalRead).ToArray();
        }
        else if (compressed[1] == 0xFB)
        {
            var span = new ReadOnlySpan<byte>(compressed, length);
            DecompressLegacy(span, output, compressed[0]);
        }
        else
        {
            throw new InvalidDataException($"Unrecognized compression header: 0x{compressed[0]:X2}{compressed[1]:X2}");
        }

        return output;
    }

    private static void DecompressLegacy(ReadOnlySpan<byte> compressed, byte[] output, byte compressionType)
    {
        bool type = compressionType != 0x80;
        int pos = 2; // Past the 2-byte header already read

        // Read uncompressed size (big-endian, 3 or 4 bytes)
        int sizeBytes = type ? 3 : 4;
        int uncompressedSize = 0;
        for (int i = 0; i < sizeBytes; i++)
            uncompressedSize = (uncompressedSize << 8) | compressed[pos++];

        int outPos = 0;
        while (outPos < output.Length && pos < compressed.Length)
        {
            byte byte0 = compressed[pos++];

            if (byte0 <= 0x7F)
            {
                byte byte1 = compressed[pos++];
                int numPlain = byte0 & 0x03;
                int numCopy = ((byte0 & 0x1C) >> 2) + 3;
                int copyOffset = ((byte0 & 0x60) << 3) + byte1 + 1;

                CopyPlain(compressed, ref pos, output, ref outPos, numPlain);
                CopyBack(output, ref outPos, numCopy, copyOffset);
            }
            else if (byte0 <= 0xBF)
            {
                byte byte1 = compressed[pos++];
                byte byte2 = compressed[pos++];
                int numPlain = ((byte1 & 0xC0) >> 6) & 0x03;
                int numCopy = (byte0 & 0x3F) + 4;
                int copyOffset = ((byte1 & 0x3F) << 8) + byte2 + 1;

                CopyPlain(compressed, ref pos, output, ref outPos, numPlain);
                CopyBack(output, ref outPos, numCopy, copyOffset);
            }
            else if (byte0 <= 0xDF)
            {
                byte byte1 = compressed[pos++];
                byte byte2 = compressed[pos++];
                byte byte3 = compressed[pos++];
                int numPlain = byte0 & 0x03;
                int numCopy = ((byte0 & 0x0C) << 6) + byte3 + 5;
                int copyOffset = ((byte0 & 0x10) << 12) + (byte1 << 8) + byte2 + 1;

                CopyPlain(compressed, ref pos, output, ref outPos, numPlain);
                CopyBack(output, ref outPos, numCopy, copyOffset);
            }
            else if (byte0 <= 0xFB)
            {
                int numPlain = ((byte0 & 0x1F) << 2) + 4;
                CopyPlain(compressed, ref pos, output, ref outPos, numPlain);
            }
            else
            {
                int numPlain = byte0 & 0x03;
                CopyPlain(compressed, ref pos, output, ref outPos, numPlain);
            }
        }
    }

    private static void CopyPlain(ReadOnlySpan<byte> src, ref int srcPos, byte[] dst, ref int dstPos, int count)
    {
        for (int i = 0; i < count && srcPos < src.Length && dstPos < dst.Length; i++)
            dst[dstPos++] = src[srcPos++];
    }

    private static void CopyBack(byte[] data, ref int pos, int count, int offset)
    {
        int srcPos = pos - offset;
        for (int i = 0; i < count && pos < data.Length; i++)
            data[pos++] = data[srcPos + i];
    }
}
