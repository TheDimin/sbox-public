using System;
using System.IO;
using GtaV.ManagedReader;
using NativeEngine;
using Sandbox;
using Sandbox.Resources;

namespace GtaV;

/// <summary>Derived physics resources for the static world. A manifest records the chunk ordering for one source file.</summary>
internal static class PhysicsChunkCache
{
	const string DirectoryName = "gtav-world-collision-v1";
	static readonly string Root = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "sbox", "gtav", "resources" );
	static bool searchPathAdded;

	[ConVar( "gtav_vphys_cache", Help = "Reuse compiled physics resources for GTA world collision." )]
	internal static bool Enabled { get; set; }

	internal static string Key( MountArchive archive, uint file )
	{
		using var stage = new StreamMeasurements.Stage( "collision.checksum" );
		return $"{typeof(PhysicsGroupDescription).Module.ModuleVersionId:N}-{archive.CollisionDigest( file )}";
	}

	static string PathFor( string key, string file ) => Path.Combine( Root, DirectoryName, key, file );
	static string RelativeName( string key, int index ) => $"{DirectoryName}/{key}/{index}.vphys";

	internal static (BBox Bounds, int Vertices)[] ReadBounds( string key )
	{
		using var stage = new StreamMeasurements.Stage( "collision.cache_index" );
		try
		{
			if ( MountPacketCache.Read( PathFor( key, "bounds.bin" ) ) is not { } packet ) return null;
			using var reader = MountPacket.Open( packet );
			int count = reader.ReadInt32();
			if ( count < 0 || packet.Count != sizeof(int) + count * 28L ) return null;
			var bounds = new (BBox, int)[count];
			for ( int i = 0; i < count; i++ )
			{
				var mins = reader.Vector();
				var maxs = reader.Vector();
				if ( !Finite( mins ) || !Finite( maxs ) || mins.x > maxs.x || mins.y > maxs.y || mins.z > maxs.z ) return null;
				int vertices = reader.ReadInt32();
				if ( vertices <= 0 || vertices % 3 != 0 ) return null;
				bounds[i] = (new( mins, maxs ), vertices);
			}
			return bounds;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException ) { return null; }
	}

	static bool Finite( Vector3 v ) => float.IsFinite( v.x ) && float.IsFinite( v.y ) && float.IsFinite( v.z );

	internal static void WriteBounds( string key, ReadOnlySpan<(BBox Bounds, int Vertices)> bounds )
	{
		using var data = new MemoryStream( sizeof(int) + bounds.Length * 28 );
		using var writer = new BinaryWriter( data );
		writer.Write( bounds.Length );
		foreach ( var (box, vertices) in bounds )
		{
			writer.Write( box.Mins.x );
			writer.Write( box.Mins.y );
			writer.Write( box.Mins.z );
			writer.Write( box.Maxs.x );
			writer.Write( box.Maxs.y );
			writer.Write( box.Maxs.z );
			writer.Write( vertices );
		}
		data.Position = 0;
		MountPacketCache.Write( PathFor( key, "bounds.bin" ), data );
	}

	internal static bool HasShape( string key, int index ) => File.Exists( PathFor( key, $"{index}.vphys_c" ) );

	internal static PhysicsGroupDescription Load( string key, int index )
	{
		using var stage = new StreamMeasurements.Stage( "collision.load_vphys" );
		if ( !searchPathAdded )
		{
			EngineGlue.AddSearchPath( Root, "GAME", false );
			searchPathAdded = true;
		}
		return PhysicsGroupDescription.Load( RelativeName( key, index ) );
	}

	internal static PhysicsGroupDescription Cook( string key, int index, PhysicsBodyBuilder body )
	{
		using var stage = new StreamMeasurements.Stage( "collision.cook_vphys" );
		var descs = CPhysBodyDescArray.Create( [body] );
		try
		{
			var native = MeshGlue.BuildAggregateData( descs );
			if ( native.IsNull || !native.IsStrongHandleValid() ) return null;
			// The live result and the file have separate resource identities. Loading the file
			// later must go through the resource loader, even if this result is still live.
			var physics = PhysicsGroupDescription.FromNative( native, $"gtav/{key}/{index}.vphys" );
			var bytes = new VPhysWriter( native ).Write();
			Write( PathFor( key, $"{index}.vphys_c" ), bytes );
			return physics;
		}
		finally
		{
			descs.DeleteThis();
		}
	}

	static void Write( string path, ReadOnlySpan<byte> data )
	{
		string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
		try
		{
			System.IO.Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			using ( var stream = File.Open( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) )
				stream.Write( data );
			File.Move( temporary, path, overwrite: true );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			try { File.Delete( temporary ); }
			catch ( Exception cleanup ) when ( cleanup is IOException or UnauthorizedAccessException ) { }
		}
	}
}
