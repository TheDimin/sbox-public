using System;
using System.Runtime.InteropServices;
using Sandbox;
using Sims4Reader.Mesh;

namespace Mounting.Sims4;

/// <summary>
/// Converts parsed GEOM mesh data into sandbox <see cref="Model"/> instances
/// via the procedural <see cref="Mesh"/> and <see cref="ModelBuilder"/> APIs.
/// </summary>
public static class GeomModelBuilder
{
	[StructLayout( LayoutKind.Sequential )]
	public struct GeomVertex
	{
		public static readonly VertexAttribute[] Layout =
		{
			new VertexAttribute( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
			new VertexAttribute( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
			new VertexAttribute( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4 ),
			new VertexAttribute( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 0 ),
			new VertexAttribute( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 1 ),
			new VertexAttribute( VertexAttributeType.Color, VertexAttributeFormat.Float32, 4 ),
		};

		[VertexLayout.Position]
		public Vector3 Position;
		[VertexLayout.Normal]
		public Vector3 Normal;
		[VertexLayout.Tangent]
		public Vector4 Tangent;
		[VertexLayout.TexCoord( 0 )]
		public Vector2 TexCoord0;
		[VertexLayout.TexCoord( 1 )]
		public Vector2 TexCoord1;
		[VertexLayout.Color]
		public Vector4 Color;
	}

	/// <summary>
	/// Builds a sandbox <see cref="Model"/> from a parsed GeometryResource.
	/// </summary>
	/// <param name="geom">The parsed GEOM mesh data.</param>
	/// <param name="material">Material to apply. Pass null for default white.</param>
	/// <param name="scale">Uniform scale. Default 39.37 converts meters to Source 2 inches.</param>
	/// <param name="addCollision">Whether to add a collision mesh.</param>
	public static Model? Build(
		GeometryResource geom,
		Material? material = null,
		float scale = 39.37f,
		bool addCollision = true )
	{
		material ??= Material.Load( "materials/default/white.vmat" );

		var srcVertices = geom.GetVertices();
		var srcIndices = geom.GetIndices();

		if ( srcVertices.Length < 3 || srcIndices.Length < 3 )
			return null;

		var vertices = ExtractVertices( srcVertices, scale );
		FlipWinding( srcIndices );

		var bounds = ComputeBounds( vertices );
		var mesh = CreateMesh( vertices, srcIndices, bounds, material );
		var builder = new ModelBuilder();

		builder.AddMesh( mesh );

		if ( addCollision )
			AddCollision( builder, vertices, srcIndices );

		return builder.Create();
	}

	private static Mesh CreateMesh( GeomVertex[] vertices, int[] indices, BBox bounds, Material material )
	{
		var mesh = new Mesh( material );

		mesh.CreateVertexBuffer<GeomVertex>( vertices.Length );
		mesh.SetVertexBufferSize( vertices.Length );
		mesh.SetVertexBufferData( vertices );

		mesh.CreateIndexBuffer( indices.Length );
		mesh.SetIndexBufferSize( indices.Length );
		mesh.SetIndexBufferData( indices );

		mesh.Bounds = bounds;

		return mesh;
	}

	private static void AddCollision( ModelBuilder builder, GeomVertex[] vertices, int[] indices )
	{
		if ( indices.Length < 3 || vertices.Length < 3 )
			return;

		var positions = new Vector3[vertices.Length];
		for ( int i = 0; i < vertices.Length; i++ )
			positions[i] = vertices[i].Position;

		builder.AddCollisionMesh( positions, indices );
	}

	private static GeomVertex[] ExtractVertices( Sims4Reader.Mesh.Vertex[] srcVertices, float scale )
	{
		var output = new GeomVertex[srcVertices.Length];

		for ( int i = 0; i < srcVertices.Length; i++ )
		{
			ref readonly var s = ref srcVertices[i];

			var vert = new GeomVertex { Color = Vector4.One };

			if ( s.Position != null && s.Position.Length >= 3 )
				vert.Position = SwapYZ( s.Position[0], s.Position[1], s.Position[2] ) * scale;

			if ( s.Normal != null && s.Normal.Length >= 3 )
				vert.Normal = SwapYZ( s.Normal[0], s.Normal[1], s.Normal[2] ).Normal;

			if ( s.Tangent != null && s.Tangent.Length >= 3 )
				vert.Tangent = new Vector4( s.Tangent[0], s.Tangent[2], s.Tangent[1], 1f );

			if ( s.UV != null )
			{
				if ( s.UV.Length > 0 && s.UV[0] != null && s.UV[0].Length >= 2 )
					vert.TexCoord0 = new Vector2( s.UV[0][0], s.UV[0][1] );
				if ( s.UV.Length > 1 && s.UV[1] != null && s.UV[1].Length >= 2 )
					vert.TexCoord1 = new Vector2( s.UV[1][0], s.UV[1][1] );
			}

			if ( s.HasColor )
			{
				vert.Color = new Vector4(
					(s.Color & 0xFF) / 255f,
					((s.Color >> 8) & 0xFF) / 255f,
					((s.Color >> 16) & 0xFF) / 255f,
					((s.Color >> 24) & 0xFF) / 255f );
			}

			output[i] = vert;
		}

		return output;
	}

	/// <summary>
	/// Reverses the winding order of every triangle for handedness change.
	/// </summary>
	private static void FlipWinding( int[] indices )
	{
		for ( int i = 0; i + 2 < indices.Length; i += 3 )
			(indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
	}

	private static Vector3 SwapYZ( float x, float y, float z ) => new( x, z, y );

	private static BBox ComputeBounds( GeomVertex[] vertices )
	{
		if ( vertices.Length == 0 )
			return default;

		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );

		for ( int i = 0; i < vertices.Length; i++ )
		{
			mins = Vector3.Min( mins, vertices[i].Position );
			maxs = Vector3.Max( maxs, vertices[i].Position );
		}

		return new BBox( mins, maxs );
	}
}
