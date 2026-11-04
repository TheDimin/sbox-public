using Sandbox.Gltf;

namespace Sandbox;

/// <summary>
/// Stages a single decoded static primitive through the native runtime model builder.
/// This is not a complete glTF model loader or an optimized compiled-resource path.
/// </summary>
internal static class GltfModelPrimitiveBuilder
{
	internal struct StockVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.Color] public Vector4 Color;
		[VertexLayout.TexCoord( 0 )] public Vector2 TexCoord0;
		[VertexLayout.TexCoord( 1 )] public Vector2 TexCoord1;
	}

	internal sealed class PreparedPrimitive
	{
		internal StockVertex[] Vertices { get; init; }
		internal int[] Indices { get; init; }
		internal MeshPrimitiveType PrimitiveType { get; init; }
		internal BBox Bounds { get; init; }
	}

	/// <summary>
	/// The caller owns the material and must select appropriate stock material and
	/// texture behavior. Skinning and morphs are deliberately rejected for now.
	/// </summary>
	internal static Model Build( GltfDrawData data, Material suppliedMaterial, string name = null )
	{
		if ( suppliedMaterial is null || !suppliedMaterial.IsValid ) throw new ArgumentException( "A valid caller-supplied material is required.", nameof( suppliedMaterial ) );
		var prepared = Prepare( data );
		var mesh = new Mesh( name, suppliedMaterial, prepared.PrimitiveType );
		mesh.CreateVertexBuffer( prepared.Vertices.Length, prepared.Vertices.AsSpan() );
		mesh.CreateIndexBuffer( prepared.Indices.Length, prepared.Indices.AsSpan() );
		mesh.Bounds = prepared.Bounds;
		var builder = Model.Builder.AddMesh( mesh ).WithViewBounds( prepared.Bounds );
		if ( !string.IsNullOrWhiteSpace( name ) ) builder.WithName( name );
		return builder.Create();
	}

	/// <summary>Validates and stages managed buffers before any native allocation.</summary>
	internal static PreparedPrimitive Prepare( GltfDrawData data )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( data.HasSkinning || data.HasMorphTargets ) throw new GltfException( "Skinned and morphed primitives require a deformation-capable model path." );
		var count = data.Positions.Length;
		if ( count == 0 || !data.Normals.IsEmpty && data.Normals.Length != count ||
			!data.Tangents.IsEmpty && data.Tangents.Length != count || !data.Colors.IsEmpty && data.Colors.Length != count )
			throw new GltfException( "Primitive vertex streams have inconsistent lengths." );
		var mode = data.Mode switch
		{
			0 => MeshPrimitiveType.Points,
			1 => MeshPrimitiveType.Lines,
			4 => MeshPrimitiveType.Triangles,
			_ => throw new GltfException( "Primitive must have expanded point, line or triangle topology." )
		};
		var indexCount = data.Indices.Length;
		if ( indexCount == 0 || data.Mode == 1 && indexCount % 2 != 0 || data.Mode == 4 && indexCount % 3 != 0 )
			throw new GltfException( "Primitive index count is invalid for its topology." );
		var outputCount = data.Mode == 4 && data.Normals.IsEmpty ? indexCount : count;
		var vertexSize = System.Runtime.InteropServices.Marshal.SizeOf<StockVertex>();
		if ( (long)count * vertexSize > int.MaxValue || (long)outputCount * vertexSize > int.MaxValue || (long)indexCount * sizeof(int) > int.MaxValue )
			throw new GltfException( "Primitive exceeds supported native buffer sizes." );
		var uv0 = TexCoords( data, 0 );
		var uv1 = TexCoords( data, 1 );
		var sourceVertices = new StockVertex[count];
		for ( var i = 0; i < count; i++ )
		{
			var position = data.Positions.Span[i];
			var normal = data.Normals.IsEmpty ? System.Numerics.Vector3.Zero : data.Normals.Span[i];
			// Tangents have no meaning when normals are absent (glTF core).
			var tangent = data.Normals.IsEmpty || data.Tangents.IsEmpty ? System.Numerics.Vector4.Zero : data.Tangents.Span[i];
			var color = data.Colors.IsEmpty ? System.Numerics.Vector4.One : data.Colors.Span[i];
			var firstUv = uv0.IsEmpty ? System.Numerics.Vector2.Zero : uv0.Span[i];
			var secondUv = uv1.IsEmpty ? System.Numerics.Vector2.Zero : uv1.Span[i];
			if ( !Finite( position ) || !Finite( normal ) || !Finite( tangent ) || !Finite( color ) || !Finite( firstUv ) || !Finite( secondUv ) )
				throw new GltfException( "Primitive vertex streams contain non-finite values." );
			sourceVertices[i] = new StockVertex { Position = position, Normal = normal, Tangent = tangent, Color = color, TexCoord0 = firstUv, TexCoord1 = secondUv };
		}
		var indices = new int[indexCount];
		for ( var i = 0; i < indexCount; i++ )
		{
			var value = data.Indices.Span[i];
			if ( value >= (uint)count || value > int.MaxValue ) throw new GltfException( "Primitive index exceeds the vertex stream." );
			indices[i] = (int)value;
		}
		var vertices = sourceVertices;
		if ( data.Mode == 4 && data.Normals.IsEmpty )
		{
			// Flat normals may differ across faces sharing a source vertex, so duplicate
			// corners while carrying every supported vertex attribute with them.
			vertices = new StockVertex[indexCount];
			for ( var i = 0; i < indexCount; i += 3 )
			{
				var a = data.Positions.Span[indices[i]];
				var b = data.Positions.Span[indices[i + 1]];
				var c = data.Positions.Span[indices[i + 2]];
				var ux = (double)b.X - a.X; var uy = (double)b.Y - a.Y; var uz = (double)b.Z - a.Z;
				var vx = (double)c.X - a.X; var vy = (double)c.Y - a.Y; var vz = (double)c.Z - a.Z;
				var nx = uy * vz - uz * vy; var ny = uz * vx - ux * vz; var nz = ux * vy - uy * vx;
				var length = Math.Sqrt( nx * nx + ny * ny + nz * nz );
				var normal = length > 0 ? new System.Numerics.Vector3( (float)(nx / length), (float)(ny / length), (float)(nz / length) ) : System.Numerics.Vector3.Zero;
				for ( var corner = 0; corner < 3; corner++ )
				{
					vertices[i + corner] = sourceVertices[indices[i + corner]];
					vertices[i + corner].Normal = normal;
					indices[i + corner] = i + corner;
				}
			}
		}
		var mins = vertices[0].Position;
		var maxs = mins;
		for ( var i = 1; i < vertices.Length; i++ )
		{
			var position = vertices[i].Position;
			mins = new Vector3( MathF.Min( mins.x, position.x ), MathF.Min( mins.y, position.y ), MathF.Min( mins.z, position.z ) );
			maxs = new Vector3( MathF.Max( maxs.x, position.x ), MathF.Max( maxs.y, position.y ), MathF.Max( maxs.z, position.z ) );
		}
		return new PreparedPrimitive { Vertices = vertices, Indices = indices, PrimitiveType = mode, Bounds = new BBox( mins, maxs ) };
	}

	private static ReadOnlyMemory<System.Numerics.Vector2> TexCoords( GltfDrawData data, int set )
	{
		if ( data.TexCoords is null || !data.TexCoords.TryGetValue( set, out var values ) ) return default;
		if ( values.Length != data.Positions.Length ) throw new GltfException( $"TEXCOORD_{set} has an inconsistent vertex count." );
		return values;
	}

	private static bool Finite( System.Numerics.Vector2 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y );
	private static bool Finite( System.Numerics.Vector3 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y ) && float.IsFinite( v.Z );
	private static bool Finite( System.Numerics.Vector4 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y ) && float.IsFinite( v.Z ) && float.IsFinite( v.W );
}
