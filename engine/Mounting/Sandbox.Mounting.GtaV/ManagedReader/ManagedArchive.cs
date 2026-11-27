using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GtaV.ManagedReader;

/// <summary>Keeps resource registration and requests on the same managed file IDs.</summary>
internal sealed class ManagedArchive : IDisposable
{
	readonly ArchiveIndex index;
	readonly ArchiveContent content;
	readonly MountCatalog catalog;
	readonly WorldMetadata world;
	readonly TextureDictionaryReader textures;
	readonly CollisionReader collision;
	int disposed;

	internal ManagedArchive( string directory, string cacheDirectory = null )
	{
		index = ArchiveIndex.Open( directory, cacheDirectory: cacheDirectory );
		content = new ArchiveContent( directory, index );
		catalog = new MountCatalog( index, content );
		world = new WorldMetadata( index, content );
		textures = new TextureDictionaryReader( content );
		collision = new CollisionReader( index, content );
	}

	internal BinaryReader OpenCatalog( bool physicsOnly )
	{
		CheckOpen();
		return catalog.Read( physicsOnly );
	}

	internal string CollisionDigest( uint file )
	{
		CheckOpen();
		using var data = content.OpenResourceStream( FileAt( file ), out var header );
		return $"{header.Version:x}-{header.System:x}-{header.Graphics:x}-{Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( data ) )}";
	}

	internal void ReadCollisionTriangles( uint file, Action<System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3> triangle, Action endLeaf )
	{
		CheckOpen();
		collision.ReadTriangles( FileAt( file ), triangle, endLeaf );
	}

	internal BinaryReader Read( int kind, uint file, uint member )
	{
		CheckOpen();
		return kind switch
		{
			-3 => world.ReadWorldCatalog(),
			-4 => world.ReadPlacements( FileAt( file ) ),
			-5 => world.ReadExtensions( FileAt( file ) ),
			-6 => collision.ReadObject( FileAt( file ), member ),
			(int)Kind.Scene => world.ReadScene( FileAt( file ) ),
			(int)Kind.Prefab => world.ReadPrefab( member ),
			(int)Kind.Texture => ReadTexture( file, member ),
			(int)Kind.Collision => collision.Read( FileAt( file ) ),
			_ => throw new InvalidDataException( "Use the drawable reader for a model." ),
		};
	}

	BinaryReader ReadTexture( uint file, uint member )
	{
		Span<uint> ids = stackalloc uint[1] { member };
		var data = ReadTextures( file, ids, out _ )[member];
		return new BinaryReader( new MemoryStream( data.Array, data.Offset, data.Count, writable: false, publiclyVisible: true ) );
	}

	internal Dictionary<uint, ArraySegment<byte>> ReadTextures( uint file, ReadOnlySpan<uint> members, out long inflated )
	{
		CheckOpen();
		return textures.ReadTextures( FileAt( file ), members, out inflated );
	}

	internal MountedDrawable ReadDrawable( Entry entry )
	{
		CheckOpen();
		return new MountedDrawable( index, content, collision, FileAt( entry.File ), entry );
	}

	internal void ReadDictionaryModels( DictionaryModelRead request
#if GTAV_BOREALIS_GEOMETRY
		, bool storedGeometry
#endif
	)
	{
#if GTAV_BOREALIS_GEOMETRY
		var upload = storedGeometry ? new Borealis.Backend.StoredGeometryUpload() : null;
		List<(DrawableGeometry Geometry, Borealis.Backend.StoredGeometryUpload.Slice Slice)> slices = storedGeometry ? new() : null;
#endif
		try
		{
			CheckOpen();
			var file = FileAt( request.File );
			using var shared = new SharedDictionary( content, file );
			while ( request.Take() is { } member )
			{
				MountedDrawable drawable = null;
				try
				{
					shared.Retain();
#pragma warning disable CA2000 // A stored result transfers ownership to the placement; the finally disposes all other results.
					drawable = new MountedDrawable( index, content, collision, file, member.Entry, shared.Resource, shared.Dispose );
#pragma warning restore CA2000
#if GTAV_BOREALIS_GEOMETRY
					if ( storedGeometry )
					{
						drawable.StageGeometry( upload, slices );
						member.Completion.SetResult( new( drawable ) );
						drawable = null;
					}
					else
#endif
						member.Completion.SetResult( new( ModelRenderPacket.Read( drawable ) ) );
				}
				catch ( Exception error ) { member.Completion.TrySetException( error ); }
				finally { drawable?.Dispose(); }
			}
#if GTAV_BOREALIS_GEOMETRY
			if ( upload is not null )
			{
				upload.Allocate();
				foreach ( var (geometry, slice) in slices ) upload.Write( slice, geometry.Vertices, geometry.Indices );
				upload.Seal();
			}
#endif
		}
		catch ( Exception error )
		{
#if GTAV_BOREALIS_GEOMETRY
			upload?.Abandon();
#endif
			while ( request.Take() is { } member ) member.Completion.TrySetException( error );
		}
	}

	/// <summary>Each prepared member keeps its spans valid until it is activated or discarded.</summary>
	sealed class SharedDictionary( ArchiveContent source, ArchiveIndex.FileEntry file ) : IDisposable
	{
		internal readonly RscResource Resource = new( source, file );
		int references = 1;

		internal void Retain() => Interlocked.Increment( ref references );

		public void Dispose()
		{
			if ( Interlocked.Decrement( ref references ) == 0 ) Resource.Dispose();
		}
	}

	ArchiveIndex.FileEntry FileAt( uint file ) =>
		file < (uint)index.Files.Length ? index.Files[(int)file] : throw new InvalidDataException( "Mounted file ID is out of range." );

	void CheckOpen() => ObjectDisposedException.ThrowIf( Volatile.Read( ref disposed ) != 0, this );

	public void Dispose()
	{
		if ( Interlocked.Exchange( ref disposed, 1 ) != 0 ) return;
		textures.Dispose();
		content.Dispose();
	}
}

/// <summary>Worker-owned model data, released after the mesh is built or its request is cancelled.</summary>
internal sealed class MountedDrawable : IDisposable
{
	internal DrawableAsset Geometry { get; }
	internal BinaryReader Metadata { get; }
#if GTAV_BOREALIS_GEOMETRY
	internal Borealis.Backend.StoredGeometryUpload.Slice[] UploadSlices { get; private set; }

	internal void StageGeometry( Borealis.Backend.StoredGeometryUpload upload,
		List<(DrawableGeometry Geometry, Borealis.Backend.StoredGeometryUpload.Slice Slice)> batch )
	{
		var sections = Geometry.Sections;
		UploadSlices = new Borealis.Backend.StoredGeometryUpload.Slice[sections.Count];
		for ( int i = 0; i < sections.Count; i++ )
		{
			var geometry = sections[i].Geometry;
			var slice = upload.Reserve( geometry.Vertices.Length, geometry.Indices.Length );
			UploadSlices[i] = slice;
			batch.Add( (geometry, slice) );
		}
	}

#endif

	internal MountedDrawable( ArchiveIndex index, ArchiveContent content, CollisionReader collision, in ArchiveIndex.FileEntry file, Entry entry,
		RscResource shared = null, Action releaseShared = null )
	{
		try { Geometry = new DrawableAsset( index, content, file, entry.Member, entry.Kind, shared, releaseShared ); }
		catch
		{
			releaseShared?.Invoke();
			throw;
		}
		try { Metadata = collision.ModelMetadata( Geometry, file, entry ); }
		catch
		{
			Geometry.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		Metadata.Dispose();
		Geometry.Dispose();
	}
}
