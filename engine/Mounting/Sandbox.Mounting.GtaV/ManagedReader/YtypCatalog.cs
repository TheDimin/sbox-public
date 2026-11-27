using System;
using System.IO;
using NumericsVector3 = System.Numerics.Vector3;

namespace GtaV.ManagedReader;

internal static class YtypCatalog
{
	static readonly uint MapTypes = ArchiveIndex.HashName( "CMapTypes", lower: false );
	static readonly uint BaseArchetype = ArchiveIndex.HashName( "CBaseArchetypeDef", lower: false );
	static readonly uint TimeArchetype = ArchiveIndex.HashName( "CTimeArchetypeDef", lower: false );
	static readonly uint InteriorArchetype = ArchiveIndex.HashName( "CMloArchetypeDef", lower: false );

	internal enum Kind : byte { Base, Time, Interior }
	internal readonly record struct Archetype( uint Name, uint Asset, uint Texture, uint Drawable, uint Physics, uint Clip, uint Flags,
		uint Ytyp, float LodDistance, NumericsVector3 Min, NumericsVector3 Max, Kind Type, sbyte AssetType );

	internal static Archetype[] Read( byte[] bytes )
	{
		using var resource = new RscResource( bytes );
		var meta = new MetaResource( resource );
		var root = meta.Root( MapTypes, 80 );
		uint ytyp = MetaResource.Word( root, 40 );
		var pointers = meta.Array( root[24..], sizeof(ulong) );
		var archetypes = new Archetype[pointers.Length / sizeof(ulong)];
		for ( int i = 0; i < archetypes.Length; i++ )
		{
			ulong pointer = MetaResource.Pointer( pointers, i * sizeof(ulong) );
			uint type = meta.TypeAt( pointer );
			var (kind, expected) = type == BaseArchetype ? (Kind.Base, 144) : type == TimeArchetype ? (Kind.Time, 160)
				: type == InteriorArchetype ? (Kind.Interior, 240) : throw new InvalidDataException( $"Unknown archetype type {type:X8}." );
			int size = meta.StructSize( type, expected );
			bool oldBase = kind == Kind.Base && size == 128;
			if ( size != expected && !oldBase ) throw new InvalidDataException( $"Unexpected {kind} archetype size {size}." );
			var row = meta.At( pointer, size );
			archetypes[i] = new Archetype( MetaResource.Word( row, 88 ), oldBase ? 0 : MetaResource.Word( row, 112 ), MetaResource.Word( row, 92 ),
				MetaResource.Word( row, 100 ), MetaResource.Word( row, 104 ), MetaResource.Word( row, 96 ), MetaResource.Word( row, 12 ),
				ytyp, MetaResource.Float( row, 8 ), Vector( row[32..] ), Vector( row[48..] ), kind, oldBase ? (sbyte)0 : unchecked((sbyte)MetaResource.Word( row, 108 )) );
		}
		return archetypes;
	}

	static NumericsVector3 Vector( ReadOnlySpan<byte> bytes ) => new( MetaResource.Float( bytes, 0 ), MetaResource.Float( bytes, 4 ), MetaResource.Float( bytes, 8 ) );
}
