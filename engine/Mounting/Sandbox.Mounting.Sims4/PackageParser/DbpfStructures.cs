using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Sims4;

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal unsafe struct DbpfHeader
{
	public uint Magic;
	public uint MajorVersion;
	public uint MinorVersion;
	public uint Unknown1;
	public uint Unknown2;
	public uint Unknown3;
	public uint DateCreated;
	public uint DateModified;
	public uint IndexMajorVersion;
	public uint IndexEntryCount;
	public uint IndexFirstEntryOffset;
	public uint IndexSize;
	public uint HoleEntryCount;
	public uint HoleOffset;
	public uint HoleSize;
	public uint IndexMinorVersion;
	public uint IndexOffset;
	public uint Unknown4;
	public fixed byte Reserved[24];

	public const uint ExpectedMagic = 0x46504244; // "DBPF" (little-endian: D=0x44, B=0x42, P=0x50, F=0x46)

	public static unsafe int Sizeof() => sizeof( DbpfHeader );
}

public sealed record DbpfRecord(
	ResourceType ResourceType,
	uint ResourceGroup,
	ulong InstanceId,
	uint FileOffset,
	uint CompressedSize,
	uint DecompressedSize,
	CompressionType CompressionType,
	ushort UnknownCompressionWord )
{
	public bool IsCompressed => CompressionType == CompressionType.Zlib;
	public uint TypeId => (uint)ResourceType;
	public uint GroupId => ResourceGroup;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct ResourceKey
{
	public ulong Instance;
	public uint TypeId;
	public uint GroupId;
}

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct ObjectData
{
	public uint Position;
	public uint Length;
}

internal static class StructUtil
{
	public static T ReadStruct<T>( ReadOnlySpan<byte> data ) where T : unmanaged
	{
		return MemoryMarshal.Read<T>( data );
	}

	public static ReadOnlySpan<T> CastSpan<T>( ReadOnlySpan<byte> data ) where T : unmanaged
	{
		return MemoryMarshal.Cast<byte, T>( data );
	}

	public static int SizeOf<T>() where T : unmanaged => Unsafe.SizeOf<T>();
}
