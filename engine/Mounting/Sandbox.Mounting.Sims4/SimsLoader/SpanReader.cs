// ============================================================================
// SpanReader — Zero-allocation binary reader over ReadOnlySpan<byte>
//
// All primitive reads are unchecked for maximum throughput on validated data.
// Call EnsureRemaining() at section boundaries for safe-fail semantics.
// ============================================================================

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Sims4.Dbpf;

/// <summary>
/// A mutable cursor over a <see cref="ReadOnlySpan{T}"/> of bytes.
/// All reads advance the position; slicing never allocates.
/// </summary>
public ref struct SpanReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _pos;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _pos = 0;
    }

    /// <summary>Current byte offset within the buffer.</summary>
    public readonly int Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pos;
    }

    /// <summary>Bytes remaining from the current position.</summary>
    public readonly int Remaining
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Length - _pos;
    }

    /// <summary>Total length of the underlying buffer.</summary>
    public readonly int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Length;
    }

    /// <summary>The full underlying buffer.</summary>
    public readonly ReadOnlySpan<byte> Buffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    // ---- Seek / Skip --------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Seek(int offset) => _pos = offset;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int count) => _pos += count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureRemaining(int count)
    {
        if (_pos + count > _buffer.Length)
            ThrowOutOfRange(count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private readonly void ThrowOutOfRange(int count) =>
        throw new InvalidOperationException(
            $"SpanReader: attempted to read {count} bytes at position {_pos}, " +
            $"but only {_buffer.Length - _pos} bytes remain (buffer length = {_buffer.Length}).");

    // ---- Little-endian primitives -------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadU8()
    {
        byte v = _buffer[_pos];
        _pos += 1;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadU16()
    {
        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.Slice(_pos));
        _pos += 2;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadU32()
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_pos));
        _pos += 4;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadU64()
    {
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_buffer.Slice(_pos));
        _pos += 8;
        return v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat()
    {
        float v = BinaryPrimitives.ReadSingleLittleEndian(_buffer.Slice(_pos));
        _pos += 4;
        return v;
    }

    // ---- Big-endian primitives (for GEOM mixed-endian fields) ---------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadU32BE()
    {
        uint v = BinaryPrimitives.ReadUInt32BigEndian(_buffer.Slice(_pos));
        _pos += 4;
        return v;
    }

    // ---- Bulk / Slice -------------------------------------------------------

    /// <summary>Returns a span of <paramref name="count"/> bytes without copying.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        var slice = _buffer.Slice(_pos, count);
        _pos += count;
        return slice;
    }

    /// <summary>
    /// Reinterprets the next sizeof(T)*count bytes as a span of unmanaged T.
    /// Zero-copy — the returned span points directly into the underlying buffer.
    /// Only valid for little-endian blittable types on LE architectures.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> ReadSpan<T>(int count) where T : unmanaged
    {
        int byteCount = count * Unsafe.SizeOf<T>();
        var bytes = _buffer.Slice(_pos, byteCount);
        _pos += byteCount;
        return MemoryMarshal.Cast<byte, T>(bytes);
    }

    /// <summary>Reads a blittable struct directly from the buffer. Zero-copy on LE.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T ReadStruct<T>() where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        T value = MemoryMarshal.Read<T>(_buffer.Slice(_pos));
        _pos += size;
        return value;
    }

    /// <summary>Reads a fixed-length ASCII/UTF-8 tag (e.g. "DBPF", "GEOM").</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadTag(int length)
    {
        return ReadBytes(length);
    }

    /// <summary>Reads a .NET 7-bit encoded length-prefixed UTF-8 string. Allocates a string.</summary>
    public string Read7BitString()
    {
        int length = Read7BitEncodedInt();
        if (length == 0) return string.Empty;
        var bytes = ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Decodes a 7-bit variable-length integer (same as BinaryReader).</summary>
    public int Read7BitEncodedInt()
    {
        int result = 0;
        int shift = 0;
        byte b;
        do
        {
            b = ReadU8();
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }

    /// <summary>Creates a sub-reader over a slice of the current buffer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly SpanReader Slice(int offset, int length) =>
        new(_buffer.Slice(offset, length));

    /// <summary>Creates a sub-reader from the current position for the given length.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanReader SliceFromHere(int length)
    {
        var sub = new SpanReader(_buffer.Slice(_pos, length));
        _pos += length;
        return sub;
    }
}
