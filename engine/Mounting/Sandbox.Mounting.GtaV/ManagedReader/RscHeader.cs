using System;
using System.Buffers.Binary;
using System.IO;

namespace GtaV.ManagedReader;

internal readonly record struct RscHeader( int Version, ulong System, ulong Graphics )
{
	internal static RscHeader Read( ReadOnlySpan<byte> file )
	{
		if ( file.Length < RscResource.HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian( file ) != RscResource.Magic )
			throw new InvalidDataException( "Invalid RSC7 header." );
		return new RscHeader( BinaryPrimitives.ReadInt32LittleEndian( file[4..] ),
			RscResource.PageSize( BinaryPrimitives.ReadUInt32LittleEndian( file[8..] ) ),
			RscResource.PageSize( BinaryPrimitives.ReadUInt32LittleEndian( file[12..] ) ) );
	}
}
