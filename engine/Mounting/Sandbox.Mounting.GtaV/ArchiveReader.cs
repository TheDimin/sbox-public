using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace GtaV;

internal sealed class ArchiveReader : SafeHandleZeroOrMinusOneIsInvalid
{
	private ArchiveReader() : base( true ) { }

	private const string Library = "gta_mount_reader";
	[DllImport( Library )] private static extern ArchiveReader gta_open( [MarshalAs( UnmanagedType.LPUTF8Str )] string directory );
	[DllImport( Library )] private static extern void gta_close( IntPtr handle );
	[DllImport( Library )] private static extern IntPtr gta_read( ArchiveReader handle, int kind, uint file, uint member, out IntPtr data, out int size );
	[DllImport( Library )] private static extern void gta_free( IntPtr packet );
	[DllImport( Library )] private static extern IntPtr gta_error();

	internal static ArchiveReader Open( string directory )
	{
		var result = gta_open( directory );
		if ( !result.IsInvalid ) return result;
		result.Dispose();
		throw Error();
	}

	internal BinaryReader Read( int kind, uint file, uint member )
	{
		lock ( this )
		{
			var packet = gta_read( this, kind, file, member, out var data, out int size );
			if ( packet == IntPtr.Zero ) throw Error();
			try
			{
				var bytes = GC.AllocateUninitializedArray<byte>( size );
				if ( size > 0 ) Marshal.Copy( data, bytes, 0, size );
				return new BinaryReader( new MemoryStream( bytes, false ) );
			}
			finally { gta_free( packet ); }
		}
	}

	[DllImport( Library )] private static extern IntPtr gta_index_key( ArchiveReader index );

	internal BinaryReader OpenCatalog()
	{
		var key = Marshal.PtrToStringUTF8( gta_index_key( this ) );
		var folder = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "sbox", "gtav", "catalog-v1" );
		var path = Path.Combine( folder, key + ".bin" );
		try
		{
			if ( File.Exists( path ) )
			{
				var bytes = File.ReadAllBytes( path );
				if ( bytes.Length >= SHA256.HashSizeInBytes )
				{
					var length = bytes.Length - SHA256.HashSizeInBytes;
					if ( SHA256.HashData( bytes.AsSpan( 0, length ) ).AsSpan().SequenceEqual( bytes.AsSpan( length ) ) )
						return new BinaryReader( new MemoryStream( bytes, 0, length, false ) );
				}
			}
		}
		catch ( IOException ) { }
		catch ( UnauthorizedAccessException ) { }

		var catalog = Read( -1, 0, 0 );
		var temporary = Path.Combine( folder, Guid.NewGuid().ToString( "N" ) + ".tmp" );
		try
		{
			Directory.CreateDirectory( folder );
			using ( var output = File.Create( temporary ) )
			{
				catalog.BaseStream.CopyTo( output );
				catalog.BaseStream.Position = 0;
				output.Write( SHA256.HashData( catalog.BaseStream ) );
			}
			File.Move( temporary, path, true );
		}
		catch ( IOException ) { }
		catch ( UnauthorizedAccessException ) { }
		finally
		{
			catalog.BaseStream.Position = 0;
			try { File.Delete( temporary ); }
			catch ( IOException ) { }
			catch ( UnauthorizedAccessException ) { }
		}
		return catalog;
	}

	private static InvalidDataException Error() => new( Marshal.PtrToStringUTF8( gta_error() ) );
	protected override bool ReleaseHandle()
	{
		gta_close( handle );
		return true;
	}
}

internal static class PacketReader
{
	internal static string Text( this BinaryReader reader ) => Encoding.UTF8.GetString( reader.ReadBytes( reader.ReadInt32() ) );
	internal static Vector3 Vector( this BinaryReader reader ) => new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	internal const float UnitsPerMetre = 1f / 0.0254f;
}
