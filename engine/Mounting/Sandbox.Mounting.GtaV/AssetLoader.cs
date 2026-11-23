using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

internal sealed class AssetLoader( Entry entry ) : ResourceLoader<GtaMount>
{
	BinaryReader prepared;
	internal Model PreparedModel { get; private set; }
	internal BinaryReader ReadPacket() => Host.Read( entry );

	internal bool TextureReady { get; private set; }

	internal void UploadTexture( BinaryReader packet )
	{
		if ( TextureReady ) return;
		prepared = packet;
		try { Texture.Load( Path ); }
		finally { prepared = null; }
	}

	// Read material headers on the engine thread. Skip geometry and embedded textures.
	internal Queue<AssetLoader> ExternalTextures( BinaryReader packet )
	{
		var result = new Queue<AssetLoader>();
		var seen = new HashSet<AssetLoader>();
		var stream = packet.BaseStream;
		var dictionaries = new string[packet.ReadInt32()];
		for ( int i = 0; i < dictionaries.Length; i++ ) dictionaries[i] = packet.Text();
		int vertices;
		while ( (vertices = packet.ReadInt32()) > 0 )
		{
			packet.ReadInt32();
			for ( int slot = 0; slot < 2; slot++ )
			{
				var name = packet.Text();
				int size = packet.ReadInt32();
				stream.Position += size;
				if ( size != 0 ) continue;
				foreach ( var dictionary in dictionaries )
				{
					if ( !Host.TexturePaths.TryGetValue( $"{dictionary}/{name}", out var path ) ) continue;
					if ( Host.GetByPath( path ) is AssetLoader asset && !asset.TextureReady && seen.Add( asset ) ) result.Enqueue( asset );
					break;
				}
			}
			stream.Position += (long)vertices * 12 * sizeof( float );
			int indices = packet.ReadInt32();
			stream.Position += (long)indices * sizeof( ushort );
		}
		stream.Position = 0;
		return result;
	}

	WeakReference<Model> physics;
	bool emptyBounds;
	internal bool TryGetPhysics( out Model model )
	{
		model = null;
		return emptyBounds || (physics is not null && physics.TryGetTarget( out model ) && model.IsValid);
	}

	internal BinaryReader ReadPhysics() => Host.ReadObjectCollision( entry );

	internal Model LoadPhysics( BinaryReader packet )
	{
		if ( TryGetPhysics( out var existing ) ) return existing;
		int count = checked(packet.ReadInt32() * 3);
		if ( count == 0 )
		{
			emptyBounds = true;
			return null;
		}

		var vertices = new Vector3[count];
		var indices = new int[count];
		for ( int i = 0; i < count; i++ )
		{
			vertices[i] = packet.Vector() * PacketReader.UnitsPerMetre;
			indices[i] = i;
		}
		var model = Model.Builder.WithName( entry.Name ).AddCollisionMesh( vertices.AsSpan(), indices.AsSpan() ).Create();
		physics = new( model );
		return model;
	}

	internal Model LoadPrepared( BinaryReader packet )
	{
		prepared = packet;
		try { return Model.Load( Path ); }
		finally { prepared = null; }
	}

	protected override object Load()
	{
		using var data = prepared ?? ReadPacket();
		if ( entry.Kind == Kind.Texture )
		{
			var texture = TextureLoader.FromDds( data.ReadBytes( checked((int)data.BaseStream.Length) ) );
			TextureReady = texture.IsValid();
			return texture;
		}

		var builder = Model.Builder.WithName( Path );
		if ( entry.Kind != Kind.Collision )
		{
			var dictionaries = new string[data.ReadInt32()];
			for ( int i = 0; i < dictionaries.Length; i++ )
				dictionaries[i] = data.Text();

			int meshIndex = 0;
			int count;
			while ( (count = data.ReadInt32()) > 0 )
			{
				int bucket = data.ReadInt32();
				var material = Material.Load( "materials/default/white.vmat" ).CreateCopy( $"gtav/materials/{entry.Name}_{meshIndex++}.vmat" );
				if ( bucket == 3 ) material.SetFeature( "F_ALPHA_TEST", 1 );
				if ( bucket is 1 or 2 ) material.SetFeature( "F_TRANSLUCENT", 1 );
				BindTexture( data, material, "g_tColor", dictionaries );
				BindTexture( data, material, "g_tNormal", dictionaries );

				var vertices = new Vertex[count];
				for ( int i = 0; i < count; i++ )
				{
					vertices[i] = new Vertex( data.Vector() * PacketReader.UnitsPerMetre );
					vertices[i].Normal = data.Vector().Normal;
					vertices[i].TexCoord0 = new Vector4( data.ReadSingle(), data.ReadSingle(), 0, 0 );
					var tangent = data.Vector();
					float sign = data.ReadSingle();
					if ( tangent.LengthSquared < 0.000001f )
					{
						var normal = vertices[i].Normal;
						tangent = Vector3.Cross( MathF.Abs( normal.z ) < 0.9f ? Vector3.Up : Vector3.Forward, normal );
					}
					vertices[i].Tangent = new Vector4( tangent.Normal, sign < 0 ? -1 : 1 );
				}
				var indices = new int[data.ReadInt32()];
				for ( int i = 0; i < indices.Length; i++ ) indices[i] = data.ReadUInt16();
				var meshPositions = vertices.Select( v => v.Position ).ToArray();
				builder.AddTraceMesh( meshPositions.AsSpan(), indices.AsSpan() );
				var mesh = new Mesh( material ) { Bounds = BBox.FromPoints( meshPositions ) };
				mesh.CreateVertexBuffer( count, vertices );
				mesh.CreateIndexBuffer( indices.Length, indices );
				builder.AddMesh( mesh );
			}
		}
		int triangleCount = data.ReadInt32();
		if ( triangleCount > 0 )
		{
			var positions = new Vector3[checked(triangleCount * 3)];
			var indices = new int[positions.Length];
			for ( int i = 0; i < positions.Length; i++ )
			{
				positions[i] = data.Vector() * PacketReader.UnitsPerMetre;
				indices[i] = i;
			}
			builder.AddCollisionMesh( positions.AsSpan(), indices.AsSpan() );
		}
		return PreparedModel = builder.Create();
	}

	void BindTexture( BinaryReader data, Material material, string parameter, string[] dictionaries )
	{
		var name = data.Text();
		var embedded = data.ReadBytes( data.ReadInt32() );
		if ( embedded.Length > 0 )
		{
			material.Set( parameter, TextureLoader.FromDds( embedded ) );
			return;
		}

		foreach ( var dictionary in dictionaries )
		{
			if ( !Host.TexturePaths.TryGetValue( $"{dictionary}/{name}", out var path ) ) continue;
			material.Set( parameter, Texture.Load( path ) );
			return;
		}
	}
}
