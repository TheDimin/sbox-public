using System;
using System.IO;
using System.Security.Cryptography;

namespace GtaV.ManagedReader;

internal static class MountPacketCache
{
	internal static ArraySegment<byte>? Read( string path )
	{
		try
		{
			var bytes = File.ReadAllBytes( path );
			if ( bytes.Length < SHA256.HashSizeInBytes ) return null;
			int length = bytes.Length - SHA256.HashSizeInBytes;
			Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
			SHA256.HashData( bytes.AsSpan( 0, length ), digest );
			return digest.SequenceEqual( bytes.AsSpan( length ) ) ? new ArraySegment<byte>( bytes, 0, length ) : null;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException ) { return null; }
	}

	internal static void Write( string path, Stream data )
	{
		string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			using ( var output = File.Open( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
			{
				data.CopyTo( output );
				data.Position = 0;
				output.Write( SHA256.HashData( data ) );
			}
			File.Move( temporary, path, overwrite: true );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException ) { }
		finally
		{
			data.Position = 0;
			try { File.Delete( temporary ); }
			catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException ) { }
		}
	}
}
