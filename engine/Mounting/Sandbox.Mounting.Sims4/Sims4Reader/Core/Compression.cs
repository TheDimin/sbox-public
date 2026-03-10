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
            // DEFLATE (zlib) — skip 2-byte zlib header
            using var source = new MemoryStream(compressed[2..].ToArray());
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
