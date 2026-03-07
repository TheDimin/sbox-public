using System;
using System.Buffers.Binary;
using System.IO;

namespace Sandbox.Mounting.Sims4;

/// <summary>
/// Lightweight span reader for DBPF parsing with bounds checks and zero allocations.
/// </summary>
internal ref struct SpanReader
{
    private ReadOnlySpan<byte> _span;
    private int _offset;

    public SpanReader(ReadOnlySpan<byte> span)
    {
        _span = span;
        _offset = 0;
    }

    public int Offset => _offset;
    public int Remaining => _span.Length - _offset;

    public void Seek(int offset)
    {
        if ((uint)offset > (uint)_span.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _offset = offset;
    }

    public void Skip(int count)
    {
        if (Remaining < count)
            throw new ArgumentOutOfRangeException(nameof(count));
        _offset += count;
    }

    public byte ReadByte()
    {
        Ensure(sizeof(byte));
        return _span[_offset++];
    }

    public ushort ReadUInt16()
    {
        Ensure(sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_span.Slice(_offset));
        _offset += sizeof(ushort);
        return value;
    }

    public uint ReadUInt32()
    {
        Ensure(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_span.Slice(_offset));
        _offset += sizeof(uint);
        return value;
    }

    public ulong ReadUInt64()
    {
        Ensure(sizeof(ulong));
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_span.Slice(_offset));
        _offset += sizeof(ulong);
        return value;
    }

    public ReadOnlySpan<byte> ReadSpan(int length)
    {
        Ensure(length);
        var slice = _span.Slice(_offset, length);
        _offset += length;
        return slice;
    }

    private void Ensure(int needed)
    {
        if (Remaining < needed)
            throw new EndOfStreamException($"Requested {needed} bytes but only {Remaining} remain");
    }
}
