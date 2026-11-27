using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace GtaV.ManagedReader;

// Reads fields described by a PSO's schema. See ThirdPartyNotices.txt.
internal readonly ref struct PsoResource
{
	readonly ReadOnlySpan<byte> file, data, blocks, schema;
	readonly int rootBlock;

	internal readonly record struct Item( uint Type, int Offset );
	internal readonly record struct ArrayInfo( Field Type, int Offset, int Count, bool Pointers );
	internal readonly record struct Field( byte Type, byte Subtype, int Offset, uint Reference );

	internal static bool HasHeader( ReadOnlySpan<byte> file ) => file.StartsWith( "PSIN"u8 );

	internal PsoResource( ReadOnlySpan<byte> file )
	{
		if ( !HasHeader( file ) ) throw new InvalidDataException( "Invalid PSO file header." );
		this.file = file;
		data = Section( file, "PSIN"u8 );
		var map = Section( file, "PMAP"u8 );
		schema = Section( file, "PSCH"u8 );
		rootBlock = checked((int)Word( map ));
		int count = unchecked((short)Half( map[4..] ));
		blocks = count > 0 ? Take( map, 8, count * 16 ) : Take( map, 16, Half( map[8..] ) * 16 );
	}

	internal Item Root => Locate( rootBlock, 0 );

	internal uint GetHash( Item item, uint name ) => unchecked((uint)(GetInteger( item, name ) ?? 0));

	internal uint GetArrayHash( ArrayInfo array, int index ) =>
		unchecked((uint)(ReadInteger( array.Type, checked(array.Offset + index * IntegerSize( array.Type )) ) ?? 0));

	internal long? GetInteger( Item item, uint name ) => Find( Fields( item.Type, out _ ), name ) is { } field
		? ReadInteger( field, checked(item.Offset + field.Offset) ) : null;

	internal string GetString( Item item, uint name )
	{
		if ( Find( Fields( item.Type, out _ ), name ) is not { Type: 0x0B } field ) return string.Empty;
		int offset = checked(item.Offset + field.Offset);
		switch ( field.Subtype )
		{
			case 0:
				var text = Take( data, offset, (int)(field.Reference >> 16) );
				if ( !text.Contains( (byte)0 ) ) return Encoding.UTF8.GetString( text );
				Span<byte> compact = text.Length < 256 ? stackalloc byte[text.Length] : new byte[text.Length];
				int count = 0;
				foreach ( byte value in text ) if ( value != 0 ) compact[count++] = value;
				return Encoding.UTF8.GetString( compact[..count] );
			case 1:
			case 2:
			case 3:
				var row = Take( data, offset, field.Subtype == 3 ? 16 : 8 );
				uint pointer = Word( row );
				int block = (int)(pointer & 0xFFF);
				if ( block == 0 ) return string.Empty;
				int at = Locate( block, (int)(pointer >> 12) ).Offset;
				if ( field.Subtype != 3 ) return CString( data[at..] );
				int size = Half( row[8..] );
				int capacity = Half( row[10..] );
				if ( capacity != 0 && capacity < size ) size = capacity;
				return Encoding.UTF8.GetString( Take( data, at, size ) );
			case 7:
			case 8:
				return Name( Word( data[offset..] ) );
			default:
				return string.Empty;
		}
	}

	string Name( uint hash )
	{
		if ( hash == 0 ) return string.Empty;
		return FindName( Section( file, "STRF"u8, required: false ), hash ) ?? FindName( Section( file, "STRS"u8, required: false ), hash )
			?? "hash_" + hash.ToString( "X8", CultureInfo.InvariantCulture );
	}

	static string FindName( ReadOnlySpan<byte> strings, uint hash )
	{
		while ( !strings.IsEmpty )
		{
			int end = strings.IndexOf( (byte)0 );
			if ( end < 0 ) end = strings.Length;
			string name = Encoding.UTF8.GetString( strings[..end] );
			if ( Hash( name ) == hash || ArchiveIndex.HashName( name ) == hash ) return name;
			strings = strings[Math.Min( strings.Length, end + 1 )..];
		}
		return null;
	}

	static string CString( ReadOnlySpan<byte> bytes )
	{
		int end = bytes.IndexOf( (byte)0 );
		return Encoding.UTF8.GetString( end < 0 ? bytes : bytes[..end] );
	}

	long? ReadInteger( Field field, int offset ) => IntegerSize( field ) switch
	{
		1 => field.Type == 1 ? unchecked((sbyte)Take( data, offset, 1 )[0]) : Take( data, offset, 1 )[0],
		2 => field.Type is 3 or 0x1E ? unchecked((short)Half( Take( data, offset, 2 ) )) : Half( Take( data, offset, 2 ) ),
		4 => field.Type is 5 or 0x0E || (field.Type == 6 && field.Subtype == 0)
			? unchecked((int)Word( data[offset..] )) : Word( data[offset..] ),
		8 => BinaryPrimitives.ReadInt64BigEndian( Take( data, offset, 8 ) ),
		_ => null,
	};

	static int IntegerSize( Field field ) => field.Type switch
	{
		0 or 1 or 2 => 1,
		3 or 4 or 0x1E => 2,
		5 or 6 => 4,
		0x0B when field.Subtype is 7 or 8 => 4,
		0x0E or 0x0F => field.Subtype is 1 or 2 ? 3 - field.Subtype : 4,
		0x20 => 8,
		_ => 0,
	};

	internal Item ObjectAt( ArrayInfo array, int index )
	{
		if ( array.Type.Type != 0x0C ) throw new InvalidDataException( "PSO array has no structure type." );
		if ( array.Pointers )
		{
			uint pointer = Word( Take( data, checked(array.Offset + 8 * index), 8 ) );
			var target = Locate( (int)(pointer & 0xFFF), (int)(pointer >> 12) );
			return Fields( target.Type, out _ ).IsEmpty ? target with { Type = array.Type.Reference } : target;
		}
		Fields( array.Type.Reference, out int stride );
		if ( stride <= 0 ) throw new InvalidDataException( "Invalid PSO structure size." );
		return new Item( array.Type.Reference, checked(array.Offset + stride * index) );
	}

	internal ArrayInfo Array( Item item, uint name )
	{
		var fields = Fields( item.Type, out _ );
		if ( Find( fields, name ) is not { } field ) return default;
		if ( field.Type != 0x0D ) throw new InvalidDataException( "Invalid PSO array." );
		int index = (int)(field.Reference & 0xFFFF);
		if ( index >= fields.Length / 12 ) index &= 0xFFF;
		var element = ReadField( Take( fields, index * 12, 12 ) );
		int at = checked(item.Offset + field.Offset);
		bool pointers = element.Type == 0x0C && element.Subtype == 3;
		if ( field.Subtype is 1 or 2 or 129 || field.Subtype == 4 && !pointers )
			return new ArrayInfo( element, at, (int)(field.Reference >> 16), pointers );
		if ( field.Subtype != 0 && field.Subtype != 4 ) throw new InvalidDataException( "Unsupported PSO array storage." );
		var row = Take( data, at, 16 );
		uint pointer = Word( row );
		int block = (int)(pointer & 0xFFF), count = Half( row[8..] );
		if ( block == 0 || count == 0 ) return default;
		return new ArrayInfo( element, Locate( block, (int)(pointer >> 12) ).Offset, count, pointers );
	}

	Item Locate( int block, int offset )
	{
		if ( block <= 0 || block > blocks.Length / 16 ) throw new InvalidDataException( "Invalid PSO block." );
		var row = blocks.Slice( (block - 1) * 16, 16 );
		int length = checked((int)Word( row[12..] ));
		if ( offset >= length ) offset >>= 8;
		int at = checked((int)Word( row[4..] ) + offset - 8);
		if ( (uint)at > (uint)data.Length ) throw new InvalidDataException( "PSO pointer is outside its data section." );
		return new Item( Word( row ), at );
	}

	ReadOnlySpan<byte> Fields( uint type, out int size )
	{
		uint count = Word( schema );
		if ( count > (schema.Length - 4) / 8 ) throw new InvalidDataException( "Invalid PSO schema size." );
		for ( int i = 0; i < count; i++ )
		{
			var entry = schema.Slice( 4 + i * 8, 8 );
			if ( Word( entry ) != type ) continue;
			int offset = checked((int)Word( entry[4..] ) - 8);
			var descriptor = Take( schema, offset, 12 );
			if ( descriptor[0] != 0 ) continue;
			size = checked((int)Word( descriptor[4..] ));
			return Take( schema, offset + 12, Half( descriptor[2..] ) * 12 );
		}
		size = 0;
		return [];
	}

	static Field? Find( ReadOnlySpan<byte> fields, uint name )
	{
		for ( int offset = 0; offset < fields.Length; offset += 12 )
			if ( Word( fields[offset..] ) == name ) return ReadField( fields.Slice( offset, 12 ) );
		return null;
	}

	static Field ReadField( ReadOnlySpan<byte> bytes ) => new( bytes[4], bytes[5], Half( bytes[6..] ), Word( bytes[8..] ) );
	internal static uint Hash( string name ) => ArchiveIndex.HashName( name, lower: false );
	static uint Word( ReadOnlySpan<byte> bytes ) => BinaryPrimitives.ReadUInt32BigEndian( Take( bytes, 0, 4 ) );
	static ushort Half( ReadOnlySpan<byte> bytes ) => BinaryPrimitives.ReadUInt16BigEndian( Take( bytes, 0, 2 ) );

	static ReadOnlySpan<byte> Take( ReadOnlySpan<byte> bytes, int offset, int size )
	{
		if ( (uint)offset > (uint)bytes.Length || (uint)size > (uint)(bytes.Length - offset) ) throw new InvalidDataException( "Truncated PSO section." );
		return bytes.Slice( offset, size );
	}

	static ReadOnlySpan<byte> Section( ReadOnlySpan<byte> file, ReadOnlySpan<byte> type, bool required = true )
	{
		for ( int at = 0; at <= file.Length - 8; )
		{
			int size = checked((int)Word( file[(at + 4)..] ));
			if ( size < 8 || size > file.Length - at ) break;
			if ( file.Slice( at, 4 ).SequenceEqual( type ) ) return file.Slice( at + 8, size - 8 );
			at += size;
		}
		return required ? throw new InvalidDataException( "Missing PSO section." ) : [];
	}
}
