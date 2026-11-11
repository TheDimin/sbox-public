using System.Buffers.Binary;
using System.IO;
using System.Text;
using Sandbox.Compression;

namespace Sandbox;

internal sealed record ModelSaveMaterialGroup( string Name, string[] Materials );

/// <summary>Restores authored fields omitted by the prebuilt DATA serializer.</summary>
internal static class VmdlMeshGroupDefaults
{
	private sealed record Edit( int Lane, int Start, int Length, byte[] Bytes );
	private sealed record Field( int Kind, int TypeStart, int TypeEnd, int ByteStart, int ByteEnd, int IntStart, int IntEnd, int LongStart, int LongEnd, int Objects, int Arrays );

	internal static byte[] Preserve( byte[] serialized, ulong mask, string[] meshGroups = null, ModelSaveMaterialGroup[] materialGroups = null )
	{
		if ( serialized.Length < 64 || U32( serialized, 0 ) != 0x4b563302 || I32( serialized, 20 ) != 1 || I32( serialized, 56 ) != 0 || I32( serialized, 60 ) != 0 ) throw new InvalidDataException( "Unsupported native model DATA encoding." );
		var count1 = I32( serialized, 28 ); var count4 = I32( serialized, 32 ); var count8 = I32( serialized, 36 ); var stringsAndTypes = I32( serialized, 40 ); var length = I32( serialized, 48 ); var compressed = I32( serialized, 52 );
		if ( length < 4 || length > 128 * 1024 * 1024 || compressed < 1 || compressed != serialized.Length - 64 || count1 < 0 || count4 < 1 || count8 < 0 || stringsAndTypes < 1 ) throw new InvalidDataException( "Invalid native model DATA limits." );
		if ( meshGroups?.Length > 64 || meshGroups?.Any( s => string.IsNullOrWhiteSpace( s ) || s.Length > 1024 ) == true || materialGroups?.Length > 256 || materialGroups?.Any( g => string.IsNullOrWhiteSpace( g.Name ) || g.Name.Length > 1024 || g.Materials == null || g.Materials.Any( p => string.IsNullOrWhiteSpace( p ) || p.Length > 4096 ) ) == true || materialGroups?.Sum( g => (long)g.Materials.Length ) > 4096 ) throw new InvalidDataException( "Authored DATA groups exceed limits." );
		var payload = new byte[length];
		if ( LZ4.DecompressBlock( serialized.AsSpan( 64 ), payload ) != length ) throw new InvalidDataException( "Native DATA decompression differs." );
		var lane4Start = checked((count1 + 3) & ~3); var lane4End = checked(lane4Start + count4 * 4); var lane8Start = checked((lane4End + 7) & ~7); var lane8End = checked(lane8Start + count8 * 8);
		if ( lane8End > length - 4 ) throw new InvalidDataException( "Invalid native DATA lanes." );
		var p1 = 0; var p4 = lane4Start; var p8 = lane8Start; var stringCount = Int4();
		if ( stringCount < 1 || stringCount > 1000000 ) throw new InvalidDataException( "Invalid native DATA string count." );
		var types = lane8End; var strings = new string[stringCount]; var stringIds = new Dictionary<string, int>( StringComparer.Ordinal );
		for ( int i = 0; i < stringCount; i++ )
		{
			var end = Array.IndexOf( payload, (byte)0, types );
			if ( end < types || end - types > 16 * 1024 * 1024 ) throw new InvalidDataException( "Invalid native DATA string." );
			strings[i] = Encoding.UTF8.GetString( payload, types, end - types ); stringIds.TryAdd( strings[i], i ); types = end + 1;
		}
		var typesStart = types; var typesEnd = checked(lane8End + stringsAndTypes);
		if ( typesEnd != length - 4 || U32( payload, typesEnd ) != 0xffeedd00 ) throw new InvalidDataException( "Invalid native DATA type section." );
		var fields = new Dictionary<string, Field>( StringComparer.Ordinal ); int nodes = 0, objects = 0, arrays = 0;
		var root = Type(); if ( root.Kind != 9 ) throw new InvalidDataException( "Native model DATA root is not an object." ); Value( root.Kind, 0 );
		if ( p1 != count1 || p4 != lane4End || p8 != lane8End || types != typesEnd ) throw new InvalidDataException( "Native model DATA traversal differs." );
		var edits = new List<Edit>(); using var addedStrings = new MemoryStream(); int nextStringId = stringCount;
		Set( "m_nDefaultMeshGroupMask", new byte[] { 4 }, Array.Empty<byte>(), Array.Empty<byte>(), BitConverter.GetBytes( mask ), 0, 0 );
		if ( meshGroups != null )
		{
			using var ints = new MemoryStream(); using var writer = new BinaryWriter( ints ); writer.Write( meshGroups.Length ); foreach ( var name in meshGroups ) writer.Write( StringId( name ) );
			Set( "m_meshGroups", new byte[] { 8 }.Concat( Enumerable.Repeat( (byte)6, meshGroups.Length ) ).ToArray(), Array.Empty<byte>(), ints.ToArray(), Array.Empty<byte>(), 0, 1 );
		}
		if ( materialGroups != null )
		{
			using var ints = new MemoryStream(); using var writer = new BinaryWriter( ints ); var valueTypes = new List<byte> { 8 }; writer.Write( materialGroups.Length );
			foreach ( var group in materialGroups )
			{
				valueTypes.AddRange( new byte[] { 9, 6, 8 } ); writer.Write( 2 ); writer.Write( StringId( "m_name" ) ); writer.Write( StringId( group.Name ) ); writer.Write( StringId( "m_materials" ) ); writer.Write( group.Materials.Length );
				foreach ( var path in group.Materials ) { valueTypes.Add( 0x86 ); valueTypes.Add( 1 ); writer.Write( StringId( path ) ); }
			}
			Set( "m_materialGroups", valueTypes.ToArray(), Array.Empty<byte>(), ints.ToArray(), Array.Empty<byte>(), materialGroups.Length, 1 + materialGroups.Length );
		}
		edits.Add( new( 4, lane4Start, 4, BitConverter.GetBytes( nextStringId ) ) );
		var bytes1 = Lane( 1, 0, count1 ); var bytes4 = Lane( 4, lane4Start, lane4End ); var bytes8 = Lane( 8, lane8Start, lane8End ); var typeBytes = Lane( 0, typesStart, typesEnd );
		using var body = new MemoryStream(); body.Write( bytes1 ); Align( body, 4 ); body.Write( bytes4 ); Align( body, 8 ); body.Write( bytes8 ); body.Write( payload.AsSpan( lane8End, typesStart - lane8End ) ); body.Write( addedStrings.ToArray() ); body.Write( typeBytes ); body.Write( payload.AsSpan( typesEnd, 4 ) );
		var expanded = body.ToArray(); if ( expanded.Length > 128 * 1024 * 1024 || objects > ushort.MaxValue || arrays > ushort.MaxValue ) throw new InvalidDataException( "Patched DATA exceeds limits." );
		var encoded = LZ4.CompressBlock( expanded ); var output = new byte[checked(64 + encoded.Length)]; serialized.AsSpan( 0, 64 ).CopyTo( output ); encoded.CopyTo( output, 64 );
		Write( 28, bytes1.Length ); Write( 32, bytes4.Length / 4 ); Write( 36, bytes8.Length / 8 ); Write( 40, checked(typesStart - lane8End + (int)addedStrings.Length + typeBytes.Length) ); BinaryPrimitives.WriteUInt16LittleEndian( output.AsSpan( 44 ), checked((ushort)objects) ); BinaryPrimitives.WriteUInt16LittleEndian( output.AsSpan( 46 ), checked((ushort)arrays) ); Write( 48, expanded.Length ); Write( 52, encoded.Length ); return output;

		int StringId( string text ) { if ( stringIds.TryGetValue( text, out var id ) ) return id; id = nextStringId++; stringIds.Add( text, id ); addedStrings.Write( Encoding.UTF8.GetBytes( text ) ); addedStrings.WriteByte( 0 ); return id; }
		void Write( int offset, int value ) => BinaryPrimitives.WriteInt32LittleEndian( output.AsSpan( offset, 4 ), value );
		void Set( string name, byte[] valueTypes, byte[] value1, byte[] value4, byte[] value8, int newObjects, int newArrays )
		{
			if ( !fields.TryGetValue( name, out var f ) ) throw new InvalidDataException( "Required native DATA field is absent: " + name );
			edits.Add( new( 0, f.TypeStart, f.TypeEnd - f.TypeStart, valueTypes ) ); edits.Add( new( 1, f.ByteStart, f.ByteEnd - f.ByteStart, value1 ) ); edits.Add( new( 4, f.IntStart, f.IntEnd - f.IntStart, value4 ) ); edits.Add( new( 8, f.LongStart, f.LongEnd - f.LongStart, value8 ) ); objects += newObjects - f.Objects; arrays += newArrays - f.Arrays;
		}
		byte[] Lane( int lane, int start, int end )
		{
			using var stream = new MemoryStream(); var previous = start;
			foreach ( var edit in edits.Where( e => e.Lane == lane && e.Start >= start && e.Start <= end && (e.Length > 0 || e.Bytes.Length > 0) ).OrderBy( e => e.Start ) )
			{
				if ( edit.Start < previous || edit.Start + edit.Length > end ) throw new InvalidDataException( "DATA edits overlap lanes." ); stream.Write( payload.AsSpan( previous, edit.Start - previous ) ); stream.Write( edit.Bytes ); previous = edit.Start + edit.Length;
			}
			stream.Write( payload.AsSpan( previous, end - previous ) ); return stream.ToArray();
		}
		int Int4() { if ( p4 > lane4End - 4 ) throw new InvalidDataException( "Native DATA int lane ended." ); var n = I32( payload, p4 ); p4 += 4; return n; }
		(int Kind, int Offset) Type() { if ( types >= typesEnd ) throw new InvalidDataException( "Native DATA types ended." ); var at = types; var type = payload[types++]; if ( (type & 128) != 0 ) { if ( types >= typesEnd ) throw new InvalidDataException( "Native DATA flags ended." ); types++; } return (type & 127, at); }
		void Value( int kind, int depth )
		{
			if ( ++nodes > 1000000 || depth > 64 ) throw new InvalidDataException( "Native DATA traversal exceeds limits." );
			switch ( kind )
			{
				case 1: case 13: case 14: case 15: case 16: case 17: case 18: case 26: case 27: return;
				case 2: if ( p1 >= count1 ) throw new InvalidDataException( "Native DATA byte lane ended." ); p1++; return;
				case 3: case 4: case 5: if ( p8 > lane8End - 8 ) throw new InvalidDataException( "Native DATA long lane ended." ); p8 += 8; return;
				case 6: case 11: case 12: case 19: Int4(); return;
				case 8: { arrays++; var n = Int4(); Count( n ); for ( int i = 0; i < n; i++ ) Value( Type().Kind, depth + 1 ); return; }
				case 10: case 24: { arrays++; int n; if ( kind == 10 ) n = Int4(); else { if ( p1 >= count1 ) throw new InvalidDataException( "Native DATA array lane ended." ); n = payload[p1++]; } Count( n ); var subtype = Type(); for ( int i = 0; i < n; i++ ) Value( subtype.Kind, depth + 1 ); return; }
				case 9:
					{
						objects++; var n = Int4(); Count( n );
						for ( int i = 0; i < n; i++ )
						{
							var type = Type(); var name = Int4(); if ( name < 0 || name >= stringCount ) throw new InvalidDataException( "Invalid native DATA member name." ); var s1 = p1; var s4 = p4; var s8 = p8; var so = objects; var sa = arrays; Value( type.Kind, depth + 1 );
							if ( depth == 0 && !fields.TryAdd( strings[name], new( type.Kind, type.Offset, types, s1, p1, s4, p4, s8, p8, objects - so, arrays - sa ) ) ) throw new InvalidDataException( "Duplicate native DATA root field." );
						}
						return;
					}
				default: throw new InvalidDataException( "Unsupported native DATA node type." );
			}
		}
		void Count( int n ) { if ( n < 0 || n > 1000000 - nodes ) throw new InvalidDataException( "Native DATA container exceeds limits." ); }
	}
	private static void Align( Stream stream, int alignment ) { while ( stream.Position % alignment != 0 ) stream.WriteByte( 0 ); }
	private static int I32( byte[] data, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset, 4 ) );
	private static uint U32( byte[] data, int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset, 4 ) );
}
