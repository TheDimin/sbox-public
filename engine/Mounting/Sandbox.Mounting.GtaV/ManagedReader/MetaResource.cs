using System;
using System.Buffers.Binary;
using System.IO;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace GtaV.ManagedReader;

// The PRD0 layout is shared by the indexed portions of YTYP and YMAP resources.
internal readonly ref struct MetaResource
{
	readonly RscResource resource;
	readonly ReadOnlySpan<byte> blocks, structures;
	readonly int rootId;

	internal MetaResource( RscResource resource )
	{
		this.resource = resource;
		var header = resource.At( RscResource.SystemBase, 112 );
		if ( Word( header, 16 ) != 0x50524430 ) throw new InvalidDataException( "Invalid PRD0 metadata signature." );
		rootId = (int)Word( header, 28 );
		int blockCount = Short( header, 76 );
		int structCount = Short( header, 72 );
		blocks = blockCount == 0 ? [] : resource.At( Pointer( header, 48 ), blockCount * 16 );
		structures = structCount == 0 ? [] : resource.At( Pointer( header, 32 ), structCount * 32 );
	}

	internal ReadOnlySpan<byte> Root( uint type, int size )
	{
		var descriptor = Block( rootId );
		if ( Word( descriptor, 0 ) != type ) throw new InvalidDataException( $"Expected metadata root {type:X8}." );
		return Read( descriptor, 0, size );
	}

	internal ReadOnlySpan<byte> Array( ReadOnlySpan<byte> descriptor, int stride )
	{
		ulong pointer = Pointer( descriptor, 0 );
		int count = Short( descriptor, 8 );
		return pointer == 0 || count == 0 ? [] : At( pointer, checked(count * stride) );
	}

	internal ReadOnlySpan<byte> At( ulong pointer, int size ) => Read( Block( BlockId( pointer ) ), (int)((pointer >> 12) & 0xFFFFF), size );
	internal uint TypeAt( ulong pointer ) => Word( Block( BlockId( pointer ) ), 0 );
	internal uint ArrayType( ReadOnlySpan<byte> descriptor ) => HasArray( descriptor ) ? TypeAt( Pointer( descriptor, 0 ) ) : 0;
	internal static bool HasArray( ReadOnlySpan<byte> descriptor ) => Pointer( descriptor, 0 ) != 0 && Short( descriptor, 8 ) != 0;
	internal bool HasBlock( uint type )
	{
		for ( int at = 0; at < blocks.Length; at += 16 )
			if ( Word( blocks, at ) == type ) return true;
		return false;
	}

	internal ReadOnlySpan<byte> StringBytes( ReadOnlySpan<byte> descriptor )
	{
		ulong pointer = Pointer( descriptor, 0 );
		if ( pointer == 0 ) return [];
		int count = Short( descriptor, 8 );
		if ( count != 0 ) return At( pointer, count );
		var block = Block( BlockId( pointer ) );
		int offset = (int)((pointer >> 12) & 0xFFFFF);
		var bytes = Read( block, offset, checked((int)Word( block, 4 ) - offset) );
		int end = bytes.IndexOf( (byte)0 );
		return end < 0 ? bytes : bytes[..end];
	}

	internal int StructSize( uint type, int defaultSize )
	{
		for ( int offset = 0; offset < structures.Length; offset += 32 )
			if ( Word( structures, offset ) == type ) return (int)Word( structures, offset + 24 );
		return defaultSize;
	}

	internal int FindField( uint structure, uint field, int type = -1 )
	{
		for ( int offset = 0; offset < structures.Length; offset += 32 )
		{
			if ( Word( structures, offset ) != structure ) continue;
			int count = Short( structures, offset + 30 );
			if ( count == 0 ) return -1;
			var entries = resource.At( Pointer( structures, offset + 16 ), count * 16 );
			for ( int at = 0; at < entries.Length; at += 16 )
				if ( Word( entries, at ) == field && (type < 0 || entries[at + 8] == type) ) return Int( entries, at + 4 );
			return -1;
		}
		return -1;
	}

	ReadOnlySpan<byte> Block( int id )
	{
		if ( id < 1 || id > blocks.Length / 16 ) throw new InvalidDataException( $"Invalid metadata block {id}." );
		return blocks.Slice( (id - 1) * 16, 16 );
	}

	ReadOnlySpan<byte> Read( ReadOnlySpan<byte> descriptor, int offset, int count )
	{
		uint size = Word( descriptor, 4 );
		if ( (uint)offset > size || (uint)count > size - offset ) throw new InvalidDataException( "Metadata read extends past its block." );
		return resource.At( checked(Pointer( descriptor, 8 ) + (uint)offset), count );
	}

	static int BlockId( ulong pointer ) => (int)(pointer & 0xFFF);
	internal static ulong Pointer( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadUInt64LittleEndian( bytes[offset..] );
	internal static uint Word( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( bytes[offset..] );
	internal static ushort Short( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( bytes[offset..] );
	internal static float Float( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( bytes[offset..] );
	internal static int Int( ReadOnlySpan<byte> bytes, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( bytes[offset..] );
	internal static NumericsVector3 Vector( ReadOnlySpan<byte> bytes, int offset ) => new( Float( bytes, offset ), Float( bytes, offset + 4 ), Float( bytes, offset + 8 ) );
	internal static NumericsVector4 Vector4( ReadOnlySpan<byte> bytes, int offset ) => new( Vector( bytes, offset ), Float( bytes, offset + 12 ) );
}
