using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Sandbox;

namespace GtaV;

/// <summary>A rectangular part of the authored water surface.</summary>
internal readonly record struct Shoreline( BBox Bounds, float Height = 0, float Heading = 0 )
{
	internal static Shoreline[] Read( BinaryReader reader )
	{
		int length = reader.ReadInt32();
		if ( length == 0 ) return [];
		if ( length != reader.BaseStream.Length - reader.BaseStream.Position )
			throw new InvalidDataException( "Invalid water definition length." );
		var root = XDocument.Load( reader.BaseStream ).Root;
		var quads = root?.Element( "WaterQuads" );
		if ( quads is null ) return [];

		var regions = new List<Shoreline>();
		foreach ( var quad in quads.Elements( "Item" ) )
		{
			float? Number( string field ) => (float?)quad.Element( field )?.Attribute( "value" );
			if ( Number( "minX" ) is not { } west || Number( "maxX" ) is not { } east ||
				Number( "minY" ) is not { } south || Number( "maxY" ) is not { } north ||
				Number( "z" ) is not { } z || west >= east || south >= north ||
				Number( "Type" ) is not { } type || type is < 0 or > 4 || (bool?)quad.Element( "IsInvisible" )?.Attribute( "value" ) == true ||
				(bool?)quad.Element( "HasLimitedDepth" )?.Attribute( "value" ) == true ) continue;

			if ( type == 0 )
			{
				regions.Add( new( new BBox( new Vector3( west, south, z ), new Vector3( east, north, z ) ) ) );
				continue;
			}

			// WaterBody has rectangular render and physics bounds. Approximate a diagonal
			// with equal-area strips, using the same footprint for swimming and rendering.
			int strips = Math.Clamp( (int)MathF.Ceiling( (north - south) / 16f ), 2, 16 );
			for ( int i = 0; i < strips; i++ )
			{
				float fraction = (i + 0.5f) / strips;
				float width = (east - west) * (type is 1 or 4 ? 1 - fraction : fraction);
				float left = type is 3 or 4 ? east - width : west;
				float bottom = south + (north - south) * i / strips;
				float top = south + (north - south) * (i + 1) / strips;
				regions.Add( new( new BBox( new Vector3( left, bottom, z ), new Vector3( left + width, top, z ) ) ) );
			}
		}
		var waves = Zones( root.Element( "WaveQuads" ), "Amplitude" );
		var calming = Zones( root.Element( "CalmingQuads" ), "fDampening" );
		var pieces = new List<Shoreline>();
		foreach ( var region in regions )
		{
			var bounds = region.Bounds;
			// The authored zones use XY coordinates. In this mount they drive the sea.
			if ( bounds.Center.z != 0 )
			{
				pieces.Add( region with { Height = 0.025f } );
				continue;
			}

			var xs = new SortedSet<float> { bounds.Mins.x, bounds.Maxs.x };
			var ys = new SortedSet<float> { bounds.Mins.y, bounds.Maxs.y };
			foreach ( var zone in waves.Concat( calming ) )
			{
				var lo = Vector3.Max( bounds.Mins, zone.Bounds.Mins );
				var hi = Vector3.Min( bounds.Maxs, zone.Bounds.Maxs );
				if ( lo.x >= hi.x || lo.y >= hi.y ) continue;
				xs.Add( lo.x );
				xs.Add( hi.x );
				ys.Add( lo.y );
				ys.Add( hi.y );
			}

			var x = xs.ToArray();
			var y = ys.ToArray();
			for ( int j = 1; j < y.Length; j++ )
			for ( int i = 1; i < x.Length; i++ )
			{
				var cell = new BBox( new Vector3( x[i - 1], y[j - 1], 0 ), new Vector3( x[i], y[j], 0 ) );
				float height = 0.20f, heading = 0, damping = 1;
				foreach ( var zone in waves )
				{
					if ( !zone.Bounds.Contains( cell.Center ) ) continue;
					height = zone.Strength;
					heading = zone.Heading;
				}
				foreach ( var zone in calming )
					if ( zone.Bounds.Contains( cell.Center ) ) damping = Math.Min( damping, zone.Strength );
				pieces.Add( new( cell, height * damping, heading ) );
			}
		}

		// Remove internal mesh borders where the union is a rectangle with one wave profile.
		var tiles = pieces.Distinct().ToArray();
		int count;
		do
		{
			count = tiles.Length;
			tiles = Join( Join( tiles, true ), false );
		} while ( tiles.Length < count );
		return tiles;
	}

	static Shoreline[] Join( Shoreline[] tiles, bool alongX )
	{
		var result = new List<Shoreline>();
		foreach ( var row in tiles.GroupBy( t => (t.Height, t.Heading, t.Bounds.Mins.z,
			alongX ? t.Bounds.Mins.y : t.Bounds.Mins.x, alongX ? t.Bounds.Maxs.y : t.Bounds.Maxs.x) ) )
		{
			Shoreline? previous = null;
			foreach ( var tile in row.OrderBy( t => alongX ? t.Bounds.Mins.x : t.Bounds.Mins.y ) )
			{
				if ( previous is { } p )
				{
					if ( alongX ? p.Bounds.Maxs.x == tile.Bounds.Mins.x : p.Bounds.Maxs.y == tile.Bounds.Mins.y )
					{
						previous = p with { Bounds = p.Bounds.AddBBox( tile.Bounds ) };
						continue;
					}
					result.Add( p );
				}
				previous = tile;
			}
			if ( previous is { } last ) result.Add( last );
		}
		return result.ToArray();
	}
	static (BBox Bounds, float Strength, float Heading)[] Zones( XElement list, string strength )
	{
		if ( list is null ) return [];
		return list.Elements( "Item" ).Select( item =>
		{
			float Field( string name ) => (float?)item.Element( name )?.Attribute( "value" ) ?? 0f;
			var bounds = new BBox( new Vector3( Field( "minX" ), Field( "minY" ), -1 ),
				new Vector3( Field( "maxX" ), Field( "maxY" ), 1 ) );
			return (bounds, Field( strength ), MathF.Atan2( Field( "YDirection" ), Field( "XDirection" ) ) * 180 / MathF.PI);
		} ).ToArray();
	}

	internal GameObject Create( GameObject parent, Material material, WaterSurfaceBatch batch )
	{
		var obj = new GameObject( parent, false, Bounds.Center.z == 0 ? "Coastal water" : "Inland water" );
		obj.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		obj.NetworkMode = NetworkMode.Never;
		obj.WorldPosition = Bounds.Center * PacketReader.UnitsPerMetre;
		var water = obj.Components.Create<WaterBody>();
		// Adjacent strips share one sea surface. A scene capture per strip exhausts GPU memory.
		water.CaptureRefraction = false;
		water.SurfaceBatch = batch;
		if ( material is not null ) water.Material = material;
		water.SharedSwellHeight = Bounds.Center.z == 0 ? 0.12f * PacketReader.UnitsPerMetre : 0;
		water.Waves = Height > 0 || water.SharedSwellHeight > 0;
		water.WaveHeight = Height * PacketReader.UnitsPerMetre;
		water.WaveDirection = Heading;
		water.WaveEdgeFade = Math.Min( 12f, 0.25f * Math.Min( Bounds.Size.x, Bounds.Size.y ) ) * PacketReader.UnitsPerMetre;
		water.WaveLength = 28f * PacketReader.UnitsPerMetre;
		water.Size = new Vector2( Bounds.Size.x, Bounds.Size.y ) * PacketReader.UnitsPerMetre;
		// This open-sea quad has no authored bottom. Use a finite 100 m physics envelope.
		water.Depth = 100f * PacketReader.UnitsPerMetre;
		obj.Enabled = true;
		return obj;
	}
}
