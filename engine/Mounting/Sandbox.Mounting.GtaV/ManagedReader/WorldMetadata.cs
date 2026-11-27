using System;
using System.IO;
using static GtaV.ManagedReader.MetaResource;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace GtaV.ManagedReader;

/// <summary>Reads the YMAP and interior fields used by the mount's scene and streaming loaders.</summary>
internal sealed partial class WorldMetadata
{
	readonly ArchiveIndex index;
	readonly ArchiveContent content;

	internal WorldMetadata( ArchiveIndex index, ArchiveContent content )
	{
		this.index = index;
		this.content = content;
		audio = new( () => ReadDestructionAudio( index, content ) );
	}

	static readonly uint MapData = ArchiveIndex.HashName( "CMapData", lower: false );
	static readonly uint EntityType = ArchiveIndex.HashName( "CEntityDef", lower: false );
	static readonly uint InteriorEntityType = ArchiveIndex.HashName( "CMloInstanceDef", lower: false );
	const uint Fixed = 1u << 5;
	const uint Object = 1u << 17;
	const uint Door = 1u << 26;

	readonly record struct Entity( uint Hash, uint Flags, NumericsVector3 Position, NumericsVector4 Rotation,
		float ScaleXY, float ScaleZ, int Parent, float Lod, float ChildLod, int Level, uint Children, int Priority, bool Interior );
	internal readonly record struct MapHeader( uint Parent, uint Flags, uint Content,
		NumericsVector3 StreamingMin, NumericsVector3 StreamingMax, NumericsVector3 EntitiesMin, NumericsVector3 EntitiesMax );

	internal MapHeader ReadMapHeader( in ArchiveIndex.FileEntry file )
	{
		using var resource = new RscResource( content.Extract( file ) );
		var meta = new MetaResource( resource );
		var row = meta.Root( MapData, 512 );
		return new MapHeader( Word( row, 12 ), Word( row, 16 ), Word( row, 20 ), Vector( row, 32 ), Vector( row, 48 ), Vector( row, 64 ), Vector( row, 80 ) );
	}

	internal BinaryReader ReadScene( in ArchiveIndex.FileEntry file )
	{
		using var resource = new RscResource( content.Extract( file ) );
		var meta = new MetaResource( resource );
		var root = meta.Root( MapData, 512 );
		using var packet = new MountPacket();
		var writer = packet.Writer;
		WritePlacements( writer, meta, root[96..] );
		var collisions = meta.Array( root[160..], sizeof(uint) );
		writer.Write( collisions.Length / sizeof(uint) );
		for ( int at = 0; at < collisions.Length; at += sizeof(uint) ) WriteText( writer, index.NameOf( Word( collisions, at ) ) );
		WriteSceneLights( writer, meta, root );
		return packet.Finish();
	}

	internal BinaryReader ReadPlacements( in ArchiveIndex.FileEntry file )
	{
		using var resource = new RscResource( content.Extract( file ) );
		var meta = new MetaResource( resource );
		var root = meta.Root( MapData, 512 );
		var entities = meta.Array( root[96..], sizeof(ulong) );
		int count = entities.Length / sizeof(ulong);
		Span<uint> masks = count <= 256 ? stackalloc uint[count] : new uint[count];
		using var packet = new MountPacket( count * 84 + 12 );
		var writer = packet.Writer;
		writer.Write( count );
		for ( int at = 0; at < entities.Length; at += sizeof(ulong) )
		{
			var row = EntityAt( meta, Pointer( entities, at ), out var entity );
			var archetype = index.FindArchetype( entity.Hash );
			var a = archetype.GetValueOrDefault().Data;
			masks[at / sizeof(ulong)] = a.Flags;
			bool isStatic = archetype.HasValue && IsStatic( a );
			float width = MathF.Abs( (a.Max.X - a.Min.X) * entity.ScaleXY );
			float depth = MathF.Abs( (a.Max.Y - a.Min.Y) * entity.ScaleXY );
			float height = MathF.Abs( (a.Max.Z - a.Min.Z) * entity.ScaleZ );
			writer.Write( entity.Hash );
			WriteVector( writer, entity.Position );
			WriteRotation( writer, entity.Rotation );
			writer.Write( entity.ScaleXY ); writer.Write( entity.ScaleZ );
			writer.Write( entity.Parent ); writer.Write( entity.Level );
			writer.Write( entity.Lod > 0 || !archetype.HasValue ? entity.Lod : a.LodDistance );
			writer.Write( entity.ChildLod ); writer.Write( entity.Children ); writer.Write( entity.Flags );
			writer.Write( (entity.Flags & Fixed) != 0 || isStatic );
			writer.Write( (a.Flags & Object) != 0 );
			writer.Write( entity.Priority ); writer.Write( entity.Interior );
			writer.Write( entity.Interior || (isStatic && MathF.Max( width, depth ) >= 8 && MathF.Min( width, depth ) >= 3) );
			writer.Write( archetype.HasValue ? 0.5f * MathF.Sqrt( width * width + depth * depth + height * height ) : 0f );
			writer.Write( archetype.HasValue ? (uint)archetype.Value.File : uint.MaxValue );
			WriteText( writer, archetype.HasValue && a.Type != YtypCatalog.Kind.Interior ? index.DrawablePath( a ) : string.Empty );
			if ( entity.Interior ) WriteWords( writer, meta.Array( row[136..], sizeof(uint) ) );
		}
		WriteWords( writer, meta.Array( root[160..], sizeof(uint) ) );
		WriteInstances( writer, meta, root );
		foreach ( uint mask in masks ) writer.Write( mask );
		return packet.Finish();
	}

	static ReadOnlySpan<byte> EntityAt( MetaResource meta, ulong pointer, out Entity entity )
	{
		uint type = meta.TypeAt( pointer );
		bool interior = type == InteriorEntityType;
		if ( type != EntityType && !interior ) throw new InvalidDataException( $"Unknown placement type {type:X8}." );
		var row = meta.At( pointer, interior ? 160 : 128 );
		var rotation = Vector4( row, 48 );
		if ( !interior ) rotation = new( -rotation.X, -rotation.Y, -rotation.Z, rotation.W );
		entity = new Entity( Word( row, 8 ), Word( row, 12 ), Vector( row, 32 ), rotation, Float( row, 64 ), Float( row, 68 ), Int( row, 72 ),
			Float( row, 76 ), Float( row, 80 ), Int( row, 84 ), Word( row, 88 ), Int( row, 92 ), interior );
		return row;
	}

	static void WritePlacements( BinaryWriter writer, MetaResource meta, ReadOnlySpan<byte> descriptor )
	{
		var entities = meta.Array( descriptor, sizeof(ulong) );
		writer.Write( entities.Length / sizeof(ulong) );
		for ( int at = 0; at < entities.Length; at += sizeof(ulong) )
		{
			EntityAt( meta, Pointer( entities, at ), out var entity );
			writer.Write( entity.Hash ); writer.Write( entity.Flags );
			WriteVector( writer, entity.Position );
			WriteRotation( writer, entity.Rotation );
			writer.Write( entity.ScaleXY ); writer.Write( entity.ScaleZ ); writer.Write( (entity.Flags & Fixed) != 0 );
		}
	}

	void WriteSceneLights( BinaryWriter writer, MetaResource meta, ReadOnlySpan<byte> root )
	{
		var local = root[256..392];
		var distant = root[392..440];
		var falloff = meta.Array( local[24..], sizeof(float) );
		var positions = meta.Array( distant[8..], 12 );
		var colors = meta.Array( distant[24..], sizeof(uint) );
		int count = Math.Max( positions.Length / 12, meta.Array( local[8..], 12 ).Length / 12 );
		if ( !HasArray( distant[8..] ) && !HasArray( distant[24..] ) )
		{
			uint parentName = Word( root, 12 );
			if ( count > 0 && parentName != 0 && index.Find( parentName, ".ymap" ) is { } parent )
			{
				using var resource = new RscResource( content.Extract( parent ) );
				var parentMeta = new MetaResource( resource );
				var parentRoot = parentMeta.Root( MapData, 512 );
				var parentPositions = parentMeta.Array( parentRoot[400..], 12 );
				var parentColors = parentMeta.Array( parentRoot[416..], sizeof(uint) );
				int available = parentPositions.IsEmpty && parentColors.IsEmpty ? 0 :
					Math.Max( parentPositions.Length / 12, parentMeta.Array( parentRoot[264..], 12 ).Length / 12 );
				WriteLights( writer, parentPositions, parentColors, falloff, available, count );
				return;
			}
			writer.Write( 0 );
			return;
		}
		WriteLights( writer, positions, colors, falloff, count, count );
	}

	static void WriteLights( BinaryWriter writer, ReadOnlySpan<byte> positions, ReadOnlySpan<byte> colors, ReadOnlySpan<byte> falloff, int available, int requested )
	{
		int count = Math.Min( available, requested );
		writer.Write( count );
		for ( int i = 0; i < count; i++ )
		{
			WriteVector( writer, i < positions.Length / 12 ? Vector( positions, 12 * i ) : default );
			writer.Write( i < colors.Length / 4 ? Word( colors, 4 * i ) : 0 );
			writer.Write( i < falloff.Length / 4 ? Float( falloff, 4 * i ) : 0f );
		}
	}

	static bool IsStatic( in YtypCatalog.Archetype archetype ) => (archetype.Flags & Fixed) != 0 ||
		(archetype.Type is YtypCatalog.Kind.Base or YtypCatalog.Kind.Time && (archetype.Flags & Object) == 0);

	static void WriteWords( BinaryWriter writer, ReadOnlySpan<byte> words )
	{
		writer.Write( words.Length / sizeof(uint) );
		writer.Write( words );
	}

	static void WriteVector( BinaryWriter writer, NumericsVector3 v ) { writer.Write( v.X ); writer.Write( v.Y ); writer.Write( v.Z ); }
	static void WriteRotation( BinaryWriter writer, NumericsVector4 v ) { writer.Write( v.X ); writer.Write( v.Y ); writer.Write( v.Z ); writer.Write( v.W ); }
	static void WriteText( BinaryWriter writer, string value ) => MountPacket.WriteText( writer, value );
}
