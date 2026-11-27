using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GtaV.ManagedReader;
using Sandbox;

namespace GtaV;

/// <summary>Keeps the resource catalog and its requests in the same managed file ID space.</summary>
internal sealed class MountArchive( string directory
#if GTAV_BOREALIS_GEOMETRY
	, bool storedGeometry
#endif
 ) : IDisposable
{
	readonly ManagedArchive archive = new( directory );
	readonly ConcurrentDictionary<uint, Lazy<Dictionary<uint, List<ArchetypeExtension>>>> extensions = new();

	internal BinaryReader OpenCatalog( bool physicsOnly ) => archive.OpenCatalog( physicsOnly );
	internal string CollisionDigest( uint file ) => archive.CollisionDigest( file );
	internal void ReadCollisionTriangles( uint file, Action<System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3> triangle, Action endLeaf )
		=> archive.ReadCollisionTriangles( file, triangle, endLeaf );

	internal BinaryReader Read( int kind, uint file, uint member, string path = null )
	{
		bool measure = StreamMeasurements.Archive.Enabled && ThreadSafe.IsMainThread;
		long start = measure ? Stopwatch.GetTimestamp() : 0;
		using var stage = new StreamMeasurements.Stage( "archive_access" );
		try { return archive.Read( kind, file, member ); }
		finally
		{
			if ( measure ) StreamMeasurements.RecordArchiveRead( kind, file, member, path, start );
		}
	}

	internal BinaryReader Read( Entry entry ) => Read( (int)entry.Kind, entry.File, entry.Member, entry.Name );

	internal PreparedModelData ReadModel( Entry entry )
	{
		if ( entry.Kind == Kind.Collision ) return new( Read( entry ) );
		using var stage = new StreamMeasurements.Stage( "model_decode" );
		var drawable = archive.ReadDrawable( entry );
#if GTAV_BOREALIS_GEOMETRY
		if ( storedGeometry ) return new( drawable );
#endif
		using ( drawable ) return new( ModelRenderPacket.Read( drawable ) );
	}

	internal void ReadDictionaryModels( DictionaryModelRead request )
	{
		using var stage = new StreamMeasurements.Stage( "model_dictionary_batch" );
		archive.ReadDictionaryModels( request
#if GTAV_BOREALIS_GEOMETRY
			, storedGeometry
#endif
		);
	}

	internal Dictionary<uint, ArraySegment<byte>> ReadTextures( uint file, uint[] members, out long inflated )
	{
		using var stage = new StreamMeasurements.Stage( "texture.total" );
		return archive.ReadTextures( file, members, out inflated );
	}

	internal IReadOnlyList<ArchetypeExtension> GetExtensions( uint file, uint archetype )
	{
		if ( file == uint.MaxValue ) return [];
		if ( !extensions.TryGetValue( file, out var entry ) )
			entry = extensions.GetOrAdd( file, key => new( () => ReadExtensions( key ) ) );
		try
		{
			return entry.Value.TryGetValue( archetype, out var items ) ? items : [];
		}
		catch
		{
			extensions.TryRemove( new( file, entry ) );
			throw;
		}
	}

	Dictionary<uint, List<ArchetypeExtension>> ReadExtensions( uint file )
	{
		using var data = Read( -5, file, 0 );
		return ArchetypeExtension.ReadDictionary( data );
	}

	public void Dispose() => archive.Dispose();
}
