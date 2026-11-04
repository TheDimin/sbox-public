using Sandbox.Gltf;

namespace Sandbox;

/// <summary>Managed glTF geometry upload and draw submission, independent of the native model compiler.</summary>
internal sealed class GltfGeometryBuffer : IDisposable
{
	internal struct GltfVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.Color] public Vector4 Color;
		[VertexLayout.TexCoord( 0 )] public Vector2 BaseColorUv;
		[VertexLayout.TexCoord( 1 )] public Vector2 MetallicRoughnessUv;
		[VertexLayout.TexCoord( 2 )] public Vector2 NormalUv;
		[VertexLayout.TexCoord( 3 )] public Vector2 OcclusionUv;
		[VertexLayout.TexCoord( 4 )] public Vector2 EmissiveUv;
	}

	internal GpuBuffer<GltfVertex> Vertices { get; private set; }
	internal GpuBuffer<uint> Indices { get; private set; }
	internal Graphics.PrimitiveType PrimitiveType { get; }
	internal bool HasNormals { get; }
	internal bool HasTangents { get; }

	internal GltfGeometryBuffer( GltfDrawData data, GltfMaterial material = null )
	{
		var vertices = PackVertices( data, material );
		// Validate topology and indices before asking the renderer to allocate anything.
		if ( data.Mode is not (0 or 1 or 4) ) throw new GltfException( "GPU geometry requires point, line or triangle lists." );
		_ = GltfGeometry.ExpandIndices( data.Indices.ToArray(), data.Mode );
		foreach ( var index in data.Indices.Span )
			if ( index >= vertices.Length ) throw new GltfException( "GPU geometry index is outside the vertex stream." );
		PrimitiveType = data.Mode switch { 0 => Graphics.PrimitiveType.Points, 1 => Graphics.PrimitiveType.Lines, _ => Graphics.PrimitiveType.Triangles };
		HasNormals = !data.Normals.IsEmpty;
		HasTangents = !data.Tangents.IsEmpty;
		try
		{
			Vertices = new GpuBuffer<GltfVertex>( vertices.Length, GpuBuffer.UsageFlags.Vertex, "glTF vertices" );
			Indices = new GpuBuffer<uint>( data.Indices.Length, GpuBuffer.UsageFlags.Index, "glTF indices" );
			Vertices.SetData( vertices );
			Indices.SetData( data.Indices.Span );
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	internal static GltfVertex[] PackVertices( GltfDrawData data, GltfMaterial material = null )
	{
		ArgumentNullException.ThrowIfNull( data );
		var count = data.Positions.Length;
		if ( count == 0 || data.Colors.Length != count ||
			!data.Normals.IsEmpty && data.Normals.Length != count || !data.Tangents.IsEmpty && data.Tangents.Length != count )
			throw new GltfException( "GPU vertex streams have inconsistent lengths." );
		var baseUv = Coordinates( data, material?.BaseColorTexture );
		var metalUv = Coordinates( data, material?.MetallicRoughnessTexture );
		var normalUv = Coordinates( data, material?.NormalTexture );
		var occlusionUv = Coordinates( data, material?.OcclusionTexture );
		var emissiveUv = Coordinates( data, material?.EmissiveTexture );
		var vertices = new GltfVertex[count];
		for ( var i = 0; i < count; i++ )
		{
			vertices[i] = new GltfVertex
			{
				Position = data.Positions.Span[i],
				Normal = data.Normals.IsEmpty ? Vector3.Zero : (Vector3)data.Normals.Span[i],
				Tangent = data.Tangents.IsEmpty ? Vector4.Zero : (Vector4)data.Tangents.Span[i],
				Color = data.Colors.Span[i],
				BaseColorUv = Uv( baseUv, material?.BaseColorTexture, i ),
				MetallicRoughnessUv = Uv( metalUv, material?.MetallicRoughnessTexture, i ),
				NormalUv = Uv( normalUv, material?.NormalTexture, i ),
				OcclusionUv = Uv( occlusionUv, material?.OcclusionTexture, i ),
				EmissiveUv = Uv( emissiveUv, material?.EmissiveTexture, i )
			};
		}
		return vertices;
	}

	private static ReadOnlyMemory<System.Numerics.Vector2> Coordinates( GltfDrawData data, GltfTextureInfo texture )
	{
		var set = texture?.TexCoord ?? 0;
		if ( data.TexCoords is null || !data.TexCoords.TryGetValue( set, out var coordinates ) )
		{
			if ( texture is not null ) throw new GltfException( $"Material requires missing TEXCOORD_{set}." );
			return default;
		}
		if ( coordinates.Length != data.Positions.Length ) throw new GltfException( $"TEXCOORD_{set} has an inconsistent vertex count." );
		return coordinates;
	}

	private static Vector2 Uv( ReadOnlyMemory<System.Numerics.Vector2> coordinates, GltfTextureInfo texture, int vertex )
	{
		if ( coordinates.IsEmpty ) return Vector2.Zero;
		var uv = coordinates.Span[vertex];
		return texture is null ? uv : texture.TransformUv( uv );
	}

	/// <summary>Call only inside an engine render scope. Materials and textures remain caller-owned.</summary>
	internal void Draw( Material material, RenderAttributes attributes = null )
	{
		ObjectDisposedException.ThrowIf( Vertices is null, this );
		Graphics.Draw( Vertices, Indices, material, attributes: attributes, primitiveType: PrimitiveType );
	}

	public void Dispose()
	{
		Vertices?.Dispose();
		Indices?.Dispose();
		Vertices = null;
		Indices = null;
	}
}
