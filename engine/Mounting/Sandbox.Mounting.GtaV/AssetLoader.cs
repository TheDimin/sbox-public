using System;
using System.IO;
using System.Linq;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

internal sealed class AssetLoader( Entry entry ) : ResourceLoader<GtaMount>
{
	protected override object Load()
	{
		using var data = Host.Read( entry );
		if ( entry.Kind == Kind.Texture )
			return TextureLoader.FromDds( data.ReadBytes( checked((int)data.BaseStream.Length) ) );

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
				var textureName = data.Text();
				var embedded = data.ReadBytes( data.ReadInt32() );
				var material = Material.Load( "materials/default/white.vmat" ).CreateCopy( $"gtav/materials/{entry.Name}_{meshIndex++}.vmat" );
				if ( bucket == 3 ) material.SetFeature( "F_ALPHA_TEST", 1 );
				if ( bucket is 1 or 2 ) material.SetFeature( "F_TRANSLUCENT", 1 );
				if ( embedded.Length > 0 )
					material.Set( "g_tColor", TextureLoader.FromDds( embedded ) );
				else
				{
					foreach ( var dictionary in dictionaries )
					{
						if ( !Host.TexturePaths.TryGetValue( $"{dictionary}/{textureName}", out var texturePath ) ) continue;
						material.Set( "g_tColor", Texture.Load( texturePath ) );
						break;
					}
				}

				var vertices = new Vertex[count];
				for ( int i = 0; i < count; i++ )
				{
					vertices[i] = new Vertex( data.Vector() * PacketReader.UnitsPerMetre );
					vertices[i].Normal = data.Vector();
					vertices[i].TexCoord0 = new Vector4( data.ReadSingle(), data.ReadSingle(), 0, 0 );
				}
				var indices = new int[data.ReadInt32()];
				for ( int i = 0; i < indices.Length; i++ ) indices[i] = data.ReadUInt16();
				var mesh = new Mesh( material ) { Bounds = BBox.FromPoints( vertices.Select( v => v.Position ) ) };
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
		return builder.Create();
	}
}
