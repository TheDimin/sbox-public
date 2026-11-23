using System;
using System.IO;
using System.Linq;
using Sandbox;
using Sandbox.Mounting;
using Sandbox.ModelEditor.Nodes;

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
		for ( int hull = 0; hull < -triangleCount; hull++ )
		{
			var points = new Vector3[data.ReadInt32()];
			for ( int i = 0; i < points.Length; i++ )
				points[i] = data.Vector() * PacketReader.UnitsPerMetre;
			builder.AddCollisionHull( points.AsSpan() );
		}
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
		if ( entry.Kind == Kind.Fragment )
		{
			var count = data.ReadInt32();
			var pieces = new System.Collections.Generic.List<ModelBreakPiece>( count );
			float weakest = float.PositiveInfinity;
			for ( int i = 0; i < count; i++ )
			{
				uint id = data.ReadUInt32();
				var piece = new ModelBreakPiece
				{
					PieceName = data.Text(),
					Model = $"mount://{Host.Ident}/models/{entry.Name}/gibs/{id}.vmdl",
					CollisionTags = "",
					RandomSpawnChance = 1f,
					IsEssential = true
				};
				var strength = data.ReadSingle();
				if ( Host.GetByPath( piece.Model ) is null ) continue;
				pieces.Add( piece );
				weakest = Math.Min( weakest, strength );
			}
			if ( pieces.Count > 0 )
			{
				builder.WithData( pieces.ToArray() );
				builder.WithData( new ModelPropData { Health = Math.Max( 1f, weakest ) } );
			}
		}
		if ( entry.Kind is Kind.Fragment or Kind.FragmentGroup )
		{
			float mass = data.ReadSingle();
			if ( float.IsFinite( mass ) && mass > 0 ) builder.WithMass( mass );
		}
		return builder.Create();
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
