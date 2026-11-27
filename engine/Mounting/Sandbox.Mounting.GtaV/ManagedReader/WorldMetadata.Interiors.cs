using System;
using System.IO;
using static GtaV.ManagedReader.MetaResource;

namespace GtaV.ManagedReader;

internal sealed partial class WorldMetadata
{
	static readonly uint MapTypes = ArchiveIndex.HashName( "CMapTypes", lower: false );

	internal BinaryReader ReadPrefab( uint archetypeName )
	{
		var definition = index.FindArchetype( archetypeName ) ?? throw new InvalidDataException( $"Missing archetype {archetypeName:X8}." );
		var archetype = definition.Data;
		using var packet = new MountPacket();
		var writer = packet.Writer;
		WriteText( writer, index.DrawablePath( archetype ) );
		writer.Write( archetype.Flags );
		bool door = (archetype.Flags & Door) != 0;
		bool interior = archetype.Type == YtypCatalog.Kind.Interior;
		if ( !door && !interior )
		{
			writer.Write( 0 ); writer.Write( IsStatic( archetype ) ); writer.Write( 0 );
			return packet.Finish();
		}

		using var resource = new RscResource( content.Extract( index.Files[definition.File] ) );
		var meta = new MetaResource( resource );
		var root = meta.Root( MapTypes, 80 );
		var definitions = meta.Array( root[24..], sizeof(ulong) );
		for ( int at = 0; at < definitions.Length; at += sizeof(ulong) )
		{
			ulong pointer = Pointer( definitions, at );
			var row = meta.At( pointer, 92 );
			if ( Word( row, 88 ) != archetypeName ) continue;
			writer.Write( door ? Word( row, 16 ) : 0 );
			writer.Write( IsStatic( archetype ) );
			if ( interior ) WriteInterior( writer, meta, meta.At( pointer, 240 ) );
			else writer.Write( 0 );
			return packet.Finish();
		}
		throw new InvalidDataException( $"Archetype {archetypeName:X8} is missing from its YTYP." );
	}

	static void WriteInterior( BinaryWriter writer, MetaResource meta, ReadOnlySpan<byte> row )
	{
		WritePlacements( writer, meta, row[152..] );
		var rooms = meta.Array( row[168..], 112 );
		writer.Write( rooms.Length / 112 );
		for ( int at = 0; at < rooms.Length; at += 112 )
		{
			var room = rooms.Slice( at, 112 );
			WriteVector( writer, Vector( room, 32 ) );
			WriteVector( writer, Vector( room, 48 ) );
			WriteWords( writer, meta.Array( room[96..], sizeof(uint) ) );
		}
		var portals = meta.Array( row[184..], 64 );
		writer.Write( portals.Length / 64 );
		for ( int at = 0; at < portals.Length; at += 64 )
		{
			var portal = portals.Slice( at, 64 );
			writer.Write( Word( portal, 8 ) ); writer.Write( Word( portal, 12 ) );
			var corners = meta.Array( portal[32..], 16 );
			writer.Write( corners.Length / 16 );
			for ( int i = 0; i < corners.Length; i += 16 ) WriteVector( writer, Vector( corners, i ) );
			WriteWords( writer, meta.Array( portal[48..], sizeof(uint) ) );
		}
		var sets = meta.Array( row[200..], 48 );
		writer.Write( sets.Length / 48 );
		for ( int at = 0; at < sets.Length; at += 48 )
		{
			var set = sets.Slice( at, 48 );
			writer.Write( Word( set, 8 ) );
			WriteWords( writer, meta.Array( set[16..], sizeof(uint) ) );
			WritePlacements( writer, meta, set[32..] );
		}
	}
}
