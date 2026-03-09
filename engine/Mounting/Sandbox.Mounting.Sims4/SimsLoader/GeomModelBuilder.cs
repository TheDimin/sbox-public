// ============================================================================
// GeomModelBuilder — Converts a parsed Sims 4 GEOM resource into an sandbox Model.
//
// Uses the sandbox procedural Mesh API:
//   new Mesh( material )
//   mesh.CreateVertexBuffer<T>( count, T.Layout )
//   mesh.SetVertexBufferData( data )
//   mesh.CreateIndexBuffer( count )
//   mesh.SetIndexBufferData( data )
//   mesh.SetBounds( mins, maxs )
//   new ModelBuilder().AddMesh( mesh ).Create()
//
// This file lives in your sandbox game project and references Sims4.Dbpf.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Sandbox;
using Sims4.Dbpf.Resources;

namespace Sims4.Sbox;

/// <summary>
/// Converts parsed GEOM mesh data into sandbox <see cref="Model"/> instances
/// via the procedural <see cref="Mesh"/> and <see cref="ModelBuilder"/> APIs.
/// </summary>
public static class GeomModelBuilder
{
	// =========================================================================
	// Vertex layout — decorated for the sandbox GPU vertex buffer.
	// =========================================================================

	[StructLayout( LayoutKind.Sequential )]
	public struct GeomVertex
	{
		/// <summary>Vertex buffer layout descriptor for sandbox's Mesh.CreateVertexBuffer.</summary>
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

	// =========================================================================
	// Build API
	// =========================================================================

	/// <summary>
	/// Builds an sandbox <see cref="Model"/> from a parsed GEOM resource.
	/// </summary>
	/// <param name="geom">The parsed GEOM mesh data.</param>
	/// <param name="material">Material to apply. Pass null for default white.</param>
	/// <param name="scale">Uniform scale. Default 39.37 converts meters → Source 2 inches.</param>
	/// <param name="addCollision">Whether to add a collision mesh.</param>
	public static Model Build(
		GeomResource geom,
		Material material = null,
		float scale = 39.37f,
		bool addCollision = true )
	{
		material ??= Material.Load( "materials/default/white.vmat" );

		var vertices = ExtractVertices( geom, scale );
		var indices = ExtractIndices( geom );

		if ( vertices.Length < 3 || indices.Length < 3 )
			return null;

		var bounds = ComputeBounds( vertices );
		var mesh = CreateMesh( vertices, indices, bounds, material );
		var builder = new ModelBuilder();

		builder.AddMesh( mesh );

		if ( addCollision )
			AddCollision( builder, vertices, indices );

		return builder.Create();
	}

	/// <summary>
	/// Builds a Model with separate material per face group.
	/// </summary>
	public static Model BuildMultiMaterial(
		GeomResource geom,
		Material[] materials,
		float scale = 39.37f,
		bool addCollision = true )
	{
		var vertices = ExtractVertices( geom, scale );
		if ( vertices.Length < 3 )
			return null;

		var bounds = ComputeBounds( vertices );
		var builder = new ModelBuilder();

		for ( int g = 0; g < geom.FaceGroups.Length; g++ )
		{
			var mat = g < materials.Length
				? materials[g]
				: Material.Load( "materials/default/white.vmat" );

			var indices = ExtractFaceGroupIndices( geom.FaceGroups[g] );
			if ( indices.Length < 3 )
				continue;

			builder.AddMesh( CreateMesh( vertices, indices, bounds, mat ) );
		}

		if ( addCollision )
			AddCollision( builder, vertices, ExtractIndices( geom ) );

		return builder.Create();
	}

	// =========================================================================
	// Mesh Construction
	// =========================================================================

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

	// =========================================================================
	// Vertex Extraction
	// =========================================================================

	private static GeomVertex[] ExtractVertices( GeomResource geom, float scale )
	{
		int count = geom.VertexCount;
		if ( count == 0 )
			return Array.Empty<GeomVertex>();

		var output = new GeomVertex[count];

		if ( geom.VertexStride == 64 )
			ExtractFromVertex64( geom, output, scale );
		else
			ExtractFromElements( geom, output, scale );

		return output;
	}

	/// <summary>
	/// Fast path: standard 64-byte vertex layout.
	/// Sims 4 is Y-up; Source 2 / sandbox is Z-up → swap Y↔Z.
	/// </summary>
	private static void ExtractFromVertex64( GeomResource geom, GeomVertex[] output, float scale )
	{
		var verts = geom.GetVertices64();

		for ( int i = 0; i < verts.Length; i++ )
		{
			ref readonly var s = ref verts[i];

			output[i] = new GeomVertex
			{
				Position = SwapYZ( s.Position.X, s.Position.Y, s.Position.Z ) * scale,
				Normal = SwapYZ( s.Normal.X, s.Normal.Y, s.Normal.Z ).Normal,
				Tangent = new Vector4( s.Tangent.X, s.Tangent.Z, s.Tangent.Y, 1f ),
				TexCoord0 = new Vector2( s.UV0.U, s.UV0.V ),
				TexCoord1 = new Vector2( s.UV1.U, s.UV1.V ),
				Color = Vector4.One,
			};
		}
	}

	/// <summary>
	/// Slow path: arbitrary vertex layout parsed per-element.
	/// </summary>
	private static void ExtractFromElements( GeomResource geom, GeomVertex[] output, float scale )
	{
		var data = geom.VertexData.Span;
		int stride = geom.VertexStride;

		// Precompute byte offsets
		var offsets = new int[geom.Elements.Length];
		int running = 0;
		for ( int e = 0; e < geom.Elements.Length; e++ )
		{
			offsets[e] = running;
			running += (int)geom.Elements[e].ByteSize;
		}

		for ( int v = 0; v < geom.VertexCount; v++ )
		{
			int baseOff = v * stride;
			var vert = new GeomVertex { Color = Vector4.One };
			int uvChannel = 0;

			for ( int e = 0; e < geom.Elements.Length; e++ )
			{
				int off = baseOff + offsets[e];
				ref readonly var elem = ref geom.Elements[e];

				switch ( elem.Type )
				{
					case VertexElementType.Position:
						{
							var p = ReadFloat3( data, off );
							vert.Position = SwapYZ( p.x, p.y, p.z ) * scale;
							break;
						}
					case VertexElementType.Normal:
						{
							var n = ReadFloat3( data, off );
							vert.Normal = SwapYZ( n.x, n.y, n.z ).Normal;
							break;
						}
					case VertexElementType.Tangent:
						{
							var t = ReadFloat3( data, off );
							vert.Tangent = new Vector4( t.x, t.z, t.y, 1f );
							break;
						}
					case VertexElementType.UV:
						{
							var uv = ReadFloat2( data, off );
							if ( uvChannel == 0 ) vert.TexCoord0 = uv;
							else if ( uvChannel == 1 ) vert.TexCoord1 = uv;
							uvChannel++;
							break;
						}
					case VertexElementType.TagValue:
						{
							vert.Color = new Vector4(
								data[off] / 255f, data[off + 1] / 255f,
								data[off + 2] / 255f, data[off + 3] / 255f );
							break;
						}
				}
			}

			output[v] = vert;
		}
	}

	// =========================================================================
	// Index Extraction
	// =========================================================================

	private static int[] ExtractIndices( GeomResource geom )
	{
		if ( geom.FaceGroups.Length == 0 )
			return Array.Empty<int>();

		if ( geom.FaceGroups.Length == 1 )
			return ExtractFaceGroupIndices( geom.FaceGroups[0] );

		int total = geom.FaceGroups.Sum( fg => fg.IndexCount );
		var all = new int[total];
		int pos = 0;

		foreach ( var fg in geom.FaceGroups )
		{
			var chunk = ExtractFaceGroupIndices( fg );
			chunk.CopyTo( all, pos );
			pos += chunk.Length;
		}

		return all;
	}

	private static int[] ExtractFaceGroupIndices( FaceGroup fg )
	{
		var data = fg.IndexData.Span;
		var indices = new int[fg.IndexCount];

		switch ( fg.BytesPerIndex )
		{
			case 1:
				for ( int i = 0; i < fg.IndexCount; i++ )
					indices[i] = data[i];
				break;

			case 2:
				var u16 = MemoryMarshal.Cast<byte, ushort>( data );
				for ( int i = 0; i < fg.IndexCount; i++ )
					indices[i] = u16[i];
				break;

			case 4:
				MemoryMarshal.Cast<byte, int>( data )
					.Slice( 0, fg.IndexCount )
					.CopyTo( indices );
				break;

			default:
				throw new InvalidOperationException( $"Unsupported index size: {fg.BytesPerIndex}" );
		}

		FlipWinding( indices );

		return indices;
	}

	// =========================================================================
	// Helpers
	// =========================================================================

	/// <summary>
	/// Reverses the winding order of every triangle by swapping the 2nd and 3rd
	/// index. This corrects for the handedness change from Y-up → Z-up swap.
	/// </summary>
	private static void FlipWinding( int[] indices )
	{
		for ( int i = 0; i + 2 < indices.Length; i += 3 )
		{
			(indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
		}
	}

	/// <summary>Swaps Y↔Z for Sims 4 (Y-up) → Source 2 (Z-up) conversion.</summary>
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

	private static Vector3 ReadFloat3( ReadOnlySpan<byte> d, int o ) => new(
		BitConverter.ToSingle( d.Slice( o, 4 ) ),
		BitConverter.ToSingle( d.Slice( o + 4, 4 ) ),
		BitConverter.ToSingle( d.Slice( o + 8, 4 ) ) );

	private static Vector2 ReadFloat2( ReadOnlySpan<byte> d, int o ) => new(
		BitConverter.ToSingle( d.Slice( o, 4 ) ),
		BitConverter.ToSingle( d.Slice( o + 4, 4 ) ) );
}
