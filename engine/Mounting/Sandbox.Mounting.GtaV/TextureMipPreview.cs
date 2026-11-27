using System;
using System.Buffers.Binary;
using System.IO;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

internal static class TextureMipPreview
{
	internal static Texture Create( BinaryReader reader, int limit, out int resolution, string path = null )
	{
		resolution = 0;
		var stream = reader.BaseStream;
		Span<byte> header = stackalloc byte[128];
		try
		{
			if ( stream.Length < header.Length ) return null;
			stream.ReadExactly( header );
			int width = Read( header, 16 ), height = Read( header, 12 ), levels = Read( header, 28 );
			if ( Read( header, 0 ) != 0x20534444 || Read( header, 4 ) != 124 || Read( header, 76 ) != 32 ||
				Read( header, 112 ) != 0 || (Read( header, 8 ) & 0x20000) == 0 ||
				width is <= 0 or > 32768 || height is <= 0 or > 32768 || levels is < 2 or > 16 ||
				Math.Max( width, height ) <= limit ) return null;
			int block = Read( header, 84 ) switch
			{
				0x31545844 => 8, // DXT1
				0x33545844 or 0x35545844 => 16, // DXT3 and DXT5
				_ => 0
			};
			if ( block == 0 ) return null;
			long offset = 128;
			while ( Math.Max( width, height ) > limit && levels > 1 )
			{
				offset += Size( width, height, block );
				width = Math.Max( 1, width / 2 );
				height = Math.Max( 1, height / 2 );
				levels--;
			}
			if ( Math.Max( width, height ) > limit ) return null;
			int length = 0;
			for ( int i = 0; i < levels; i++ )
				length = checked(length + Size( Math.Max( 1, width >> i ), Math.Max( 1, height >> i ), block ));
			if ( offset + length != stream.Length ) return null;
			var bytes = new byte[128 + length];
			header.CopyTo( bytes );
			Write( bytes, 12, height );
			Write( bytes, 16, width );
			Write( bytes, 20, Size( width, height, block ) );
			Write( bytes, 28, levels );
			stream.Position = offset;
			stream.ReadExactly( bytes.AsSpan( 128 ) );
			resolution = Math.Max( width, height );
			return StreamMeasurements.FromDds( bytes, path );
		}
		finally { stream.Position = 0; }
	}

	static int Size( int w, int h, int block ) => ((w + 3) / 4) * ((h + 3) / 4) * block;
	static int Read( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( bytes[offset..] );
	static void Write( Span<byte> bytes, int offset, int value ) => BinaryPrimitives.WriteInt32LittleEndian( bytes[offset..], value );
}
