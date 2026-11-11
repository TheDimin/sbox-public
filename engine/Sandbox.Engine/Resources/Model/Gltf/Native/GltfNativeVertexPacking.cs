using System.Buffers.Binary;
using NativeEngine;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

/// <summary>
/// Import-time packing of baked vertices into the compact layout the native mesh stores. The vertex input unit
/// converts each format to the float the shader reads, so the shaders do not change:
/// position float3, normal and tangent SNORM16x4, colour UNORM16x4, used UV slots float2 and unused UV slots half2.
/// </summary>
internal static class GltfNativeVertexPacking
{
	private enum Kind { Float3, Snorm4, Unorm4, Float2, Half2 }

	/// <summary>Skin influences per stored vertex: four bone indices and four weights that sum to 255.</summary>
	internal sealed record Skin( ushort[] Bones, byte[] Weights );

	internal static (byte[] Bytes, GltfNativeVertexField[] Fields, int Stride) Pack( byte[] baked, GltfGpuPrimitivePlan plan, Skin skin = null )
	{
		var usedSlots = plan.UvProjection?.Sources.Count ?? int.MaxValue;
		var layout = new List<(GltfGpuField Source, Kind Kind, int Offset)>();
		var stride = 0; var slot = 0;
		foreach ( var field in plan.Fields )
		{
			var kind = field.Semantic switch
			{
				"POSITION" => Kind.Float3,
				"NORMAL" or "TANGENT" => Kind.Snorm4,
				"COLOR" => Kind.Unorm4,
				"TEXCOORD" => slot++ < usedSlots ? Kind.Float2 : Kind.Half2,
				_ => throw new GltfException( $"The native vertex packer has no format for {field.Semantic}." )
			};
			layout.Add( (field, kind, stride) );
			stride += kind switch { Kind.Float3 => 12, Kind.Snorm4 or Kind.Unorm4 => 8, Kind.Float2 => 8, _ => 4 };
		}
		var skinOffset = stride;
		if ( skin is not null ) stride += 12;
		var count = plan.SourceVertices.Length;
		var output = new byte[checked(count * stride)];
		for ( var vertex = 0; vertex < count; vertex++ )
		{
			var sourceStart = vertex * plan.VertexStride;
			var target = output.AsSpan( vertex * stride, stride );
			foreach ( var (field, kind, offset) in layout )
			{
				var fieldStart = sourceStart + field.Offset; var components = field.Components;
				float Read( int component ) => component < components ? BinaryPrimitives.ReadSingleLittleEndian( baked.AsSpan( fieldStart + component * 4 ) ) : 0;
				var to = target[offset..];
				switch ( kind )
				{
					case Kind.Float3:
						for ( var c = 0; c < 3; c++ ) BinaryPrimitives.WriteSingleLittleEndian( to[(c * 4)..], Read( c ) );
						break;
					case Kind.Float2:
						for ( var c = 0; c < 2; c++ ) BinaryPrimitives.WriteSingleLittleEndian( to[(c * 4)..], Read( c ) );
						break;
					case Kind.Half2:
						for ( var c = 0; c < 2; c++ ) BinaryPrimitives.WriteUInt16LittleEndian( to[(c * 2)..], BitConverter.HalfToUInt16Bits( (Half)Read( c ) ) );
						break;
					case Kind.Snorm4:
						for ( var c = 0; c < 4; c++ ) BinaryPrimitives.WriteInt16LittleEndian( to[(c * 2)..], (short)MathF.Round( Math.Clamp( Read( c ), -1f, 1f ) * 32767f ) );
						break;
					case Kind.Unorm4:
						for ( var c = 0; c < 4; c++ ) BinaryPrimitives.WriteUInt16LittleEndian( to[(c * 2)..], (ushort)MathF.Round( Math.Clamp( Read( c ), 0f, 1f ) * 65535f ) );
						break;
				}
			}
			if ( skin is not null )
			{
				for ( var c = 0; c < 4; c++ ) BinaryPrimitives.WriteUInt16LittleEndian( target[(skinOffset + c * 2)..], skin.Bones[vertex * 4 + c] );
				skin.Weights.AsSpan( vertex * 4, 4 ).CopyTo( target[(skinOffset + 8)..] );
			}
		}
		var fields = layout.Select( x => new GltfNativeVertexField( x.Source.Semantic.ToLowerInvariant(), x.Source.SemanticIndex, x.Offset, x.Kind switch
		{
			Kind.Float3 => ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT,
			Kind.Snorm4 => ColorFormat.COLOR_FORMAT_R16G16B16A16_SNORM,
			Kind.Unorm4 => ColorFormat.COLOR_FORMAT_R16G16B16A16_UNORM,
			Kind.Float2 => ColorFormat.COLOR_FORMAT_R32G32_FLOAT,
			_ => ColorFormat.COLOR_FORMAT_R16G16_FLOAT
		} ) ).ToList();
		if ( skin is not null )
		{
			fields.Add( new GltfNativeVertexField( "blendindices", 0, skinOffset, ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT ) );
			fields.Add( new GltfNativeVertexField( "blendweight", 0, skinOffset + 8, ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM ) );
		}
		return (output, fields.ToArray(), stride);
	}

	/// <summary>
	/// The four strongest influences of each stored vertex, renormalized and quantized to weights that sum to 255.
	/// <paramref name="influences"/> gives, per source vertex, every (bone, weight) pair of every JOINTS/WEIGHTS set.
	/// Returns whether any vertex had more than four influences.
	/// </summary>
	internal static (Skin Skin, bool Truncated) Quantize( uint[] sourceVertices, Func<int, IEnumerable<(ushort Bone, float Weight)>> influences )
	{
		var bones = new ushort[sourceVertices.Length * 4];
		var weights = new byte[sourceVertices.Length * 4];
		var truncated = false;
		for ( var vertex = 0; vertex < sourceVertices.Length; vertex++ )
		{
			var all = influences( (int)sourceVertices[vertex] ).Where( x => x.Weight > 0 ).GroupBy( x => x.Bone )
				.Select( g => (Bone: g.Key, Weight: g.Sum( x => x.Weight )) ).OrderByDescending( x => x.Weight ).ToList();
			if ( all.Count > 4 ) truncated = true;
			var top = all.Take( 4 ).ToList();
			var sum = top.Sum( x => x.Weight );
			if ( sum <= 0 ) { top = [(0, 1f)]; sum = 1; }
			var quantized = top.Select( x => (int)MathF.Round( x.Weight / sum * 255f ) ).ToArray();
			quantized[0] += 255 - quantized.Sum();
			for ( var i = 0; i < top.Count; i++ ) { bones[vertex * 4 + i] = top[i].Bone; weights[vertex * 4 + i] = (byte)Math.Clamp( quantized[i], 0, 255 ); }
		}
		return (new Skin( bones, weights ), truncated);
	}

	/// <summary>
	/// Merge vertices whose packed bytes are equal. Generated tangents and flat normals need one vertex per triangle
	/// corner while they are computed; afterwards most corners are equal again. Returns the welded vertices and, for
	/// each input vertex, its index in the output.
	/// </summary>
	internal static (byte[] Bytes, int[] Remap) Weld( byte[] vertices, int stride )
	{
		var count = vertices.Length / stride;
		var remap = new int[count];
		var first = new Dictionary<VertexKey, int>( count, new VertexComparer( vertices, stride ) );
		var unique = new List<int>( count );
		for ( var vertex = 0; vertex < count; vertex++ )
		{
			var key = new VertexKey( vertex, Hash( vertices.AsSpan( vertex * stride, stride ) ) );
			if ( first.TryGetValue( key, out var existing ) ) { remap[vertex] = existing; continue; }
			remap[vertex] = unique.Count; first.Add( key, unique.Count ); unique.Add( vertex );
		}
		if ( unique.Count == count ) return (vertices, remap);
		var output = new byte[unique.Count * stride];
		for ( var i = 0; i < unique.Count; i++ ) vertices.AsSpan( unique[i] * stride, stride ).CopyTo( output.AsSpan( i * stride ) );
		return (output, remap);
	}

	private static int Hash( ReadOnlySpan<byte> bytes )
	{
		var hash = new HashCode();
		hash.AddBytes( bytes );
		return hash.ToHashCode();
	}

	private readonly record struct VertexKey( int Vertex, int Hash );

	private sealed class VertexComparer( byte[] vertices, int stride ) : IEqualityComparer<VertexKey>
	{
		public bool Equals( VertexKey a, VertexKey b ) => a.Hash == b.Hash &&
			vertices.AsSpan( a.Vertex * stride, stride ).SequenceEqual( vertices.AsSpan( b.Vertex * stride, stride ) );
		public int GetHashCode( VertexKey key ) => key.Hash;
	}
}
