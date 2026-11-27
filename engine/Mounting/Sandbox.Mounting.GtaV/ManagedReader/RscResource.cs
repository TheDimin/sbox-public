using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GtaV.ManagedReader;

// RSC7 layout derived from ragekit. See ThirdPartyNotices.txt.
internal sealed class RscResource : IDisposable
{
	internal const uint Magic = 0x37435352;
	internal const int HeaderSize = 16;
	internal const uint SystemBase = 0x50000000;
	internal const uint GraphicsBase = 0x60000000;
	const uint SegmentCapacity = 0x10000000;
	byte[] body;
	readonly int systemLength, totalLength;
	internal int Version { get; }

	internal RscResource( byte[] file )
		: this( OpenBytes( file, out var header ), header )
	{
	}

	internal RscResource( ArchiveContent content, in ArchiveIndex.FileEntry entry )
		: this( content.OpenResourceStream( entry, out var header ), header )
	{
	}

	static Stream OpenBytes( byte[] file, out RscHeader header )
	{
		header = RscHeader.Read( file );
		return new MemoryStream( file, HeaderSize, file.Length - HeaderSize, writable: false );
	}

	RscResource( Stream input, RscHeader header )
	{
		using var source = input;
		var (version, system, graphics) = header;
		Version = version;
		if ( system > SegmentCapacity || graphics > SegmentCapacity || system + graphics > (ulong)source.Length * 1032 + 65536 )
			throw new InvalidDataException( "Invalid RSC7 segment size." );
		systemLength = (int)system;
		totalLength = (int)(system + graphics);
		body = ArrayPool<byte>.Shared.Rent( totalLength );
		try
		{
			var output = body.AsSpan( 0, totalLength );
			using var decoder = new DeflateStream( source, CompressionMode.Decompress, leaveOpen: true );
			int count = decoder.ReadAtLeast( output, systemLength );
			// Keep the existing format behavior for a graphics segment with padding after the compressed bytes.
			if ( count < output.Length ) count += decoder.ReadAtLeast( output[count..], output.Length - count, false );
			output[count..].Clear();
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	internal ReadOnlySpan<byte> At( ulong pointer, int size )
	{
		var remaining = Remaining( pointer );
		if ( (uint)size > (uint)remaining.Length )
			throw new InvalidDataException( $"RSC7 pointer {pointer:X} and size {size} extend past their segment." );
		return remaining[..size];
	}

	internal ReadOnlySpan<byte> Remaining( ulong pointer )
	{
		ObjectDisposedException.ThrowIf( body is null, this );
		ReadOnlySpan<byte> segment = (pointer & ~(SegmentCapacity - 1ul)) switch
		{
			SystemBase => body.AsSpan( 0, systemLength ),
			GraphicsBase => body.AsSpan( systemLength, totalLength - systemLength ),
			_ => throw new InvalidDataException( $"Invalid RSC7 pointer {pointer:X}." ),
		};
		int offset = (int)(pointer & (SegmentCapacity - 1));
		if ( offset > segment.Length )
			throw new InvalidDataException( $"RSC7 pointer {pointer:X} extends past its segment." );
		return segment[offset..];
	}

	internal string ReadString( ulong pointer )
	{
		if ( pointer == 0 ) return string.Empty;
		var bytes = Remaining( pointer );
		int end = bytes.IndexOf( (byte)0 );
		return Encoding.UTF8.GetString( end < 0 ? bytes : bytes[..end] );
	}

	internal ReadOnlySpan<byte> Array( ulong pointer, int count, int stride ) =>
		pointer == 0 || count == 0 ? [] : At( pointer, checked(count * stride) );

	public void Dispose()
	{
		var buffer = body;
		body = null;
		if ( buffer is not null ) ArrayPool<byte>.Shared.Return( buffer );
	}

	internal static ulong PageSize( uint flags )
	{
		uint pages = ((flags >> 4) & 1) << 8;
		pages += ((flags >> 5) & 3) << 7;
		pages += ((flags >> 7) & 15) << 6;
		pages += ((flags >> 11) & 63) << 5;
		pages += ((flags >> 17) & 127) << 4;
		pages += ((flags >> 24) & 1) << 3;
		pages += ((flags >> 25) & 1) << 2;
		pages += ((flags >> 26) & 1) << 1;
		pages += (flags >> 27) & 1;
		return (ulong)pages * (512u << (int)(flags & 15));
	}
}
