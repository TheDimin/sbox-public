using System;
using System.Buffers.Binary;
using System.IO;
using static GtaV.ManagedReader.MetaResource;

namespace GtaV.ManagedReader;

// Texture layouts and DDS encoding derived from ragekit. See ThirdPartyNotices.txt.
internal readonly record struct TextureDds( int Width, int Height, int Mips, TextureDds.Format PixelFormat )
{
	internal enum Format : uint
	{
		Unknown = 0, Bgra = 21, Bgrx = 22, Bgr5551 = 25, Alpha8 = 28, Rgba = 32, Luminance8 = 50, Luminance16 = 81,
		Bc1 = 0x31545844, Bc2 = 0x33545844, Bc3 = 0x35545844, Bc4 = 0x31495441, Bc5 = 0x32495441, Bc7 = 0x20374342
	}

	internal int HeaderSize => PixelFormat == Format.Bc7 ? 148 : 128;
	internal static int DescriptorSize( bool enhanced ) => enhanced ? 0x40 : 0x78;

	internal static TextureDds Describe( ReadOnlySpan<byte> data, bool enhanced, out ulong address, out ulong size )
	{
		if ( enhanced )
		{
			address = Pointer( data, 0x38 );
			size = (ulong)Word( data, 8 ) * Word( data, 12 );
			return new TextureDds( Short( data, 0x18 ), Short( data, 0x1A ), data[0x22], EnhancedFormat( data[0x1F] ) );
		}

		var result = new TextureDds( Short( data, 0x50 ), Short( data, 0x52 ), data[0x5D], (Format)Word( data, 0x58 ) );
		address = Pointer( data, 0x70 );
		size = address == 0 ? 0 : result.LegacyChainSize( Short( data, 0x54 ) );
		return result;
	}

	bool Compressed => PixelFormat is Format.Bc1 or Format.Bc2 or Format.Bc3 or Format.Bc4 or Format.Bc5 or Format.Bc7;
	int BytesPerUnit => PixelFormat switch
	{
		Format.Bc1 or Format.Bc4 => 8,
		Format.Bc2 or Format.Bc3 or Format.Bc5 or Format.Bc7 => 16,
		Format.Bgra or Format.Bgrx or Format.Rgba => 4,
		Format.Bgr5551 or Format.Luminance16 => 2,
		Format.Alpha8 or Format.Luminance8 => 1,
		_ => throw new InvalidDataException( "Unknown GTA texture format." )
	};

	static Format EnhancedFormat( byte code ) => code switch
	{
		0x57 or 0x5A or 0x5B => Format.Bgra,
		0x56 => Format.Bgr5551,
		0x41 => Format.Alpha8,
		0x1B or 0x1C or 0x1D => Format.Rgba,
		0x3C or 0x3D => Format.Luminance8,
		0x35 or 0x38 => Format.Luminance16,
		0x46 or 0x47 or 0x48 => Format.Bc1,
		0x49 or 0x4A or 0x4B => Format.Bc2,
		0x4C or 0x4D or 0x4E => Format.Bc3,
		0x4F or 0x50 => Format.Bc4,
		0x52 or 0x53 => Format.Bc5,
		0x61 or 0x62 or 0x63 => Format.Bc7,
		_ => Format.Unknown,
	};

	ulong LegacyChainSize( int depth )
	{
		ulong length = 0;
		int width = Width, height = Height;
		for ( int mip = 0; mip < Mips; mip++ )
		{
			length += (ulong)Units( width ) * (uint)Units( height ) * (uint)BytesPerUnit;
			width = Math.Max( 1, width / 2 );
			height = Math.Max( 1, height / 2 );
		}
		return length * (uint)Math.Max( 1, depth );
	}

	internal void WriteHeader( Span<byte> target )
	{
		var header = target[..HeaderSize];
		header.Clear();
		bool mipmapped = Mips > 1;
		bool compressed = Compressed;
		"DDS "u8.CopyTo( header );
		Put( header, 4, 124 );
		Put( header, 8, 0x1007u | (mipmapped ? 0x20000u : 0) | (compressed ? 0x80000u : 8) );
		Put( header, 12, (uint)Height ); Put( header, 16, (uint)Width );
		uint pitch = (uint)Units( Width ) * (uint)BytesPerUnit;
		Put( header, 20, compressed ? pitch * (uint)Units( Height ) : pitch );
		Put( header, 28, (uint)Mips );
		Put( header, 76, 32 );
		Put( header, 108, mipmapped ? 0x401008u : 0x1000 );
		if ( compressed )
		{
			Put( header, 80, 4 );
			Put( header, 84, PixelFormat == Format.Bc7 ? 0x30315844u : (uint)PixelFormat );
			if ( PixelFormat == Format.Bc7 )
			{
				Put( header, 128, 98 ); // DXGI_FORMAT_BC7_UNORM
				Put( header, 132, 3 );  // DDS_DIMENSION_TEXTURE2D
				Put( header, 140, 1 );
			}
			return;
		}
		var (flags, bits, red, green, blue, alpha) = PixelFormat switch
		{
			Format.Bgra => (0x41u, 32u, 0xFF0000u, 0xFF00u, 0xFFu, 0xFF000000u),
			Format.Bgrx => (0x40u, 32u, 0xFF0000u, 0xFF00u, 0xFFu, 0u),
			Format.Rgba => (0x41u, 32u, 0xFFu, 0xFF00u, 0xFF0000u, 0xFF000000u),
			Format.Bgr5551 => (0x41u, 16u, 0x7C00u, 0x03E0u, 0x1Fu, 0x8000u),
			Format.Alpha8 => (2u, 8u, 0u, 0u, 0u, 0xFFu),
			Format.Luminance8 => (0x20000u, 8u, 0xFFu, 0u, 0u, 0u),
			Format.Luminance16 => (0x20000u, 16u, 0xFFFFu, 0u, 0u, 0u),
			_ => throw new InvalidDataException( "Unknown GTA texture format." ),
		};
		Put( header, 80, flags ); Put( header, 88, bits ); Put( header, 92, red );
		Put( header, 96, green ); Put( header, 100, blue ); Put( header, 104, alpha );
	}

	int Units( int size ) => Compressed ? (Math.Max( 1, size ) + 3) / 4 : Math.Max( 1, size );
	static void Put( Span<byte> bytes, int offset, uint value ) => BinaryPrimitives.WriteUInt32LittleEndian( bytes[offset..], value );
}
