using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static GtaV.ManagedReader.MetaResource;
using NumericsVector3 = System.Numerics.Vector3;

namespace GtaV.ManagedReader;

internal sealed partial class WorldMetadata
{
	readonly record struct CollisionBox( NumericsVector3 Min, NumericsVector3 Max );

	internal BinaryReader ReadWorldCatalog( bool rebuild = false )
	{
		string path = index.WorldCatalogCache;
		if ( path is not null && !rebuild && MountPacketCache.Read( path ) is { } cached )
			return MountPacket.Open( cached );
		var result = BuildWorldCatalog( out bool complete );
		if ( path is not null && complete ) MountPacketCache.Write( path, result.BaseStream );
		return result;
	}

	BinaryReader BuildWorldCatalog( out bool complete )
	{
		var selected = new WorldArchiveSelection( index, content );
		int[] maps = selected.Select( ".ymap" );
		int[] bounds = selected.Select( ".ybn" );
		bool manifestsRead = ReadManifests( out var groups, out var interiors );
		int unreadableMaps = 0;
		var headers = new MapHeader?[maps.Length];
		Parallel.For( 0, headers.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Min( 16, Environment.ProcessorCount ) }, i =>
		{
			try { headers[i] = ReadMapHeader( index.Files[maps[i]] ); }
			catch ( Exception e ) when ( IsCatalogReadError( e ) ) { Interlocked.Increment( ref unreadableMaps ); }
		} );
		var collision = ReadCollisionBoxes( bounds, out bool storesRead );
		complete = selected.Warnings.Count == 0 && manifestsRead && storesRead && unreadableMaps == 0 && collision.Count == bounds.Length;
		using var packet = new MountPacket( maps.Length * 70 + collision.Count * 64 );
		var writer = packet.Writer;
		writer.Write( maps.Length );
		for ( int i = 0; i < maps.Length; i++ )
		{
			uint name = index.Files[maps[i]].Hash;
			var row = headers[i].GetValueOrDefault();
			writer.Write( maps[i] ); writer.Write( name ); writer.Write( row.Parent ); writer.Write( row.Flags ); writer.Write( row.Content );
			writer.Write( groups.Contains( name ) ); writer.Write( headers[i].HasValue );
			WriteVector( writer, row.StreamingMin ); WriteVector( writer, row.StreamingMax );
			WriteVector( writer, row.EntitiesMin ); WriteVector( writer, row.EntitiesMax );
		}
		writer.Write( collision.Count );
		foreach ( var (id, box) in collision )
		{
			ref readonly var file = ref index.Files[id];
			writer.Write( id );
			WriteText( writer, file.Stem + file.Extension );
			writer.Write( interiors.GetValueOrDefault( file.Hash ) );
			WriteVector( writer, box.Min ); WriteVector( writer, box.Max );
		}
		byte[] water = ReadWaterMetadata();
		writer.Write( water.Length ); writer.Write( water );
		return packet.Finish();
	}

	bool ReadManifests( out HashSet<uint> groups, out Dictionary<uint, uint> interiors )
	{
		groups = new();
		interiors = new();
		bool complete = true;
		foreach ( ref readonly var file in index.Files )
		{
			if ( file.Extension != ".ymf" || !index.Ranks[file.Rank].Active ) continue;
			try
			{
				byte[] bytes = content.Extract( file );
				if ( PsoResource.HasHeader( bytes ) ) new PsoWorldManifest( bytes ).Read( groups, interiors );
			}
			catch ( Exception e ) when ( IsCatalogReadError( e ) ) { complete = false; }
		}
		return complete;
	}

	List<(int Id, CollisionBox Box)> ReadCollisionBoxes( int[] selected, out bool storesRead )
	{
		var cacheFiles = new List<int>();
		for ( int i = 0; i < index.Files.Length; i++ )
		{
			ref readonly var file = ref index.Files[i];
			if ( file.Extension == ".dat" && file.Stem.EndsWith( "_cache_y", StringComparison.Ordinal ) && index.Ranks[file.Rank].Active ) cacheFiles.Add( i );
		}
		var boxes = new Dictionary<uint, CollisionBox>();
		storesRead = true;
		foreach ( int id in cacheFiles.OrderBy( id => index.Ranks[index.Files[id].Rank] ) )
		{
			try { ReadBoundsStore( content.Extract( index.Files[id] ), boxes ); }
			catch ( Exception e ) when ( IsCatalogReadError( e ) ) { storesRead = false; }
		}
		var result = new List<(int, CollisionBox)>( selected.Length );
		foreach ( int id in selected )
		{
			ref readonly var file = ref index.Files[id];
			if ( !boxes.TryGetValue( file.Hash, out var box ) )
			{
				try
				{
					using var resource = new RscResource( content.Extract( file ) );
					if ( resource.Version != 43 ) continue;
					var header = resource.At( RscResource.SystemBase, 0x70 );
					box = new CollisionBox( Vector( header, 0x30 ), Vector( header, 0x20 ) );
				}
				catch ( Exception e ) when ( IsCatalogReadError( e ) ) { continue; }
			}
			result.Add( (id, box) );
		}
		return result;
	}

	static void ReadBoundsStore( ReadOnlySpan<byte> bytes, Dictionary<uint, CollisionBox> boxes )
	{
		ReadOnlySpan<byte> tag = "BoundsStore\n"u8;
		int position = bytes.IndexOf( tag );
		if ( position < 0 || bytes.Length - position - tag.Length < 4 ) return;
		var contents = bytes[(position + tag.Length)..];
		uint length = Word( contents, 0 );
		if ( length > contents.Length - 4 ) throw new InvalidDataException( "Truncated collision bounds store." );
		var rows = contents.Slice( 4, (int)length );
		for ( int at = 0; at <= rows.Length - 32; at += 32 )
		{
			uint hash = Word( rows, at );
			boxes[hash] = new CollisionBox( Vector( rows, at + 4 ), Vector( rows, at + 16 ) );
		}
	}

	byte[] ReadWaterMetadata()
	{
		uint hash = ArchiveIndex.HashName( "water" );
		var files = index.Files;
		for ( int i = files.Length - 1; i >= 0; i-- )
		{
			ref readonly var row = ref files[i];
			if ( row.Hash != hash || row.Extension != ".xml" ) continue;
			if ( row.Directory == "data/levels/gta5" || row.Directory.EndsWith( "/data/levels/gta5", StringComparison.Ordinal ) )
				return content.Extract( row );
		}
		return [];
	}

	static bool IsCatalogReadError( Exception error ) => error is IOException or InvalidDataException or OverflowException;

}
