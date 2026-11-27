using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GtaV.ManagedReader;

/// <summary>Reads dictionary tables in the system segment and then advances the graphics stream on demand.</summary>
internal sealed class RscDictionary : IDisposable
{
	const int MaxSystemSize = 256 * 1024 * 1024;
	const ulong CacheLimit = 8 * 1024 * 1024;
	readonly DeflateStream decoder;
	readonly RscHeader header;
	readonly ulong inflateLimit;
	byte[] system, prefix;
	ulong graphicsPosition;

	internal readonly record struct Member( uint Hash, ulong Pointer );
	internal readonly record struct Texture( uint Hash, ulong Offset, int Length, TextureDds Dds ) : IComparable<Texture>
	{
		public int CompareTo( Texture other )
		{
			int order = Offset.CompareTo( other.Offset );
			return order == 0 ? Hash.CompareTo( other.Hash ) : order;
		}
	}

	internal Member[] Members { get; }
	internal bool RetainPrefix { get; }
	internal ulong InflatedBytes => header.System + graphicsPosition;
	internal ulong DeclaredBytes => header.System + header.Graphics;

	internal RscDictionary( ArchiveContent content, in ArchiveIndex.FileEntry entry )
		: this( content.OpenResourceStream( entry, out var header ), header )
	{
	}

	RscDictionary( Stream source, RscHeader header )
	{
		this.header = header;
		try
		{
			inflateLimit = (ulong)source.Length * 1032 + 65536;
			if ( header.System < 64 || header.System > MaxSystemSize || header.System > inflateLimit )
				throw new InvalidDataException( "Invalid RSC dictionary system segment size." );
			RetainPrefix = (ulong)source.Length + RscResource.HeaderSize + DeclaredBytes <= CacheLimit;
			decoder = new DeflateStream( source, CompressionMode.Decompress );
			system = ArrayPool<byte>.Shared.Rent( (int)header.System );
			decoder.ReadExactly( system.AsSpan( 0, (int)header.System ) );
			var root = SystemAt( RscResource.SystemBase, 64 );
			var hashes = ReadTable( Pointer( root, 0x20 ), Short( root, 0x28 ), sizeof(uint) );
			var pointers = ReadTable( Pointer( root, 0x30 ), Short( root, 0x38 ), sizeof(ulong) );
			if ( hashes.Length / sizeof(uint) != pointers.Length / sizeof(ulong) ) throw new InvalidDataException( "Dictionary member count mismatch." );
			var members = new List<Member>( hashes.Length / sizeof(uint) );
			for ( int i = 0; i < pointers.Length / sizeof(ulong); i++ )
			{
				ulong pointer = Pointer( pointers, i * sizeof(ulong) );
				if ( pointer != 0 ) members.Add( new Member( Word( hashes, i * sizeof(uint) ), pointer ) );
			}
			Members = members.ToArray();
		}
		catch
		{
			if ( decoder is null ) source.Dispose();
			else Dispose();
			throw;
		}
	}

	internal string TextureName( in Member member )
	{
		ulong pointer = Pointer( SystemAt( member.Pointer, 0x30 ), 0x28 );
		if ( pointer == 0 ) return string.Empty;
		var bytes = SystemAt( pointer, 0 );
		int end = bytes.IndexOf( (byte)0 );
		return Encoding.UTF8.GetString( end < 0 ? bytes : bytes[..end] );
	}

	internal Texture TextureAt( uint hash )
	{
		foreach ( ref readonly var member in Members.AsSpan() )
			if ( member.Hash == hash ) return DescribeTexture( member );
		throw new InvalidDataException( $"Texture {hash:X8} is missing from the dictionary." );
	}

	Texture DescribeTexture( in Member member )
	{
		bool enhanced = header.Version == 5;
		var data = SystemAt( member.Pointer, TextureDds.DescriptorSize( enhanced ) );
		var dds = TextureDds.Describe( data, enhanced, out ulong address, out ulong size );
		if ( address < RscResource.GraphicsBase ) throw new InvalidDataException( "Texture has no graphics payload." );
		ulong offset = address - RscResource.GraphicsBase;
		if ( offset > header.Graphics || size > header.Graphics - offset || size > (ulong)(Array.MaxLength - dds.HeaderSize) || header.System + offset + size > inflateLimit )
			throw new InvalidDataException( "Texture payload exceeds the graphics segment." );
		return new Texture( member.Hash, offset, (int)size, dds );
	}

	internal void ReadTexture( in Texture texture, Span<byte> destination )
	{
		texture.Dds.WriteHeader( destination );
		var payload = destination.Slice( texture.Dds.HeaderSize, texture.Length );
		ulong end = texture.Offset + (uint)texture.Length;
		if ( RetainPrefix )
		{
			if ( end > graphicsPosition )
			{
				GrowPrefix( (int)end );
				decoder.ReadExactly( prefix.AsSpan( (int)graphicsPosition, (int)(end - graphicsPosition) ) );
				graphicsPosition = end;
			}
			prefix.AsSpan( (int)texture.Offset, texture.Length ).CopyTo( payload );
			return;
		}
		if ( texture.Offset < graphicsPosition ) throw new InvalidDataException( "Overlapping texture payloads." );
		if ( texture.Offset > graphicsPosition ) Skip( texture.Offset - graphicsPosition );
		decoder.ReadExactly( payload );
		graphicsPosition = end;
	}

	void GrowPrefix( int needed )
	{
		if ( (prefix?.Length ?? 0) >= needed ) return;
		var buffer = ArrayPool<byte>.Shared.Rent( needed );
		if ( prefix is not null )
		{
			prefix.AsSpan( 0, (int)graphicsPosition ).CopyTo( buffer );
			ArrayPool<byte>.Shared.Return( prefix );
		}
		prefix = buffer;
	}

	void Skip( ulong bytes )
	{
		var discard = ArrayPool<byte>.Shared.Rent( 64 * 1024 );
		try
		{
			while ( bytes > 0 )
			{
				int amount = (int)Math.Min( bytes, (uint)discard.Length );
				decoder.ReadExactly( discard.AsSpan( 0, amount ) );
				bytes -= (uint)amount;
			}
		}
		finally { ArrayPool<byte>.Shared.Return( discard ); }
	}

	ReadOnlySpan<byte> ReadTable( ulong pointer, int count, int size ) => pointer == 0 || count == 0 ? [] : SystemAt( pointer, count * size )[..(count * size)];

	ReadOnlySpan<byte> SystemAt( ulong pointer, int minimum )
	{
		if ( pointer < RscResource.SystemBase || pointer - RscResource.SystemBase > header.System || (uint)minimum > header.System - (pointer - RscResource.SystemBase) )
			throw new InvalidDataException( "Dictionary metadata pointer exceeds the system segment." );
		int offset = (int)(pointer - RscResource.SystemBase);
		return system.AsSpan( offset, (int)header.System - offset );
	}

	static uint Word( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( bytes[offset..] );
	static ulong Pointer( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadUInt64LittleEndian( bytes[offset..] );
	static ushort Short( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( bytes[offset..] );

	public void Dispose()
	{
		decoder.Dispose();
		if ( system is not null ) ArrayPool<byte>.Shared.Return( system );
		if ( prefix is not null ) ArrayPool<byte>.Shared.Return( prefix );
		system = prefix = null;
	}
}
