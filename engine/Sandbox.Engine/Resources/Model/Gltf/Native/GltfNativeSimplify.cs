using System.Buffers.Binary;
using System.Text.Json;
using Evergine.Bindings.MeshOptimizer;
using NativeEngine;

namespace Sandbox;

/// <summary>
/// How a generated level of detail is made from its mesh: keep about <see cref="Ratio"/> of the triangles, but move no
/// surface by more than <see cref="Error"/> of the mesh size. <see cref="LockBorder"/> keeps open edges in place, so
/// meshes that meet stay closed. Stored on a node as <c>extras.sbox_lod</c>.
/// </summary>
internal sealed record GltfNativeLodSettings( float Ratio, float Error, bool LockBorder )
{
	internal const string Extra = "sbox_lod";

	internal static GltfNativeLodSettings Read( JsonElement node )
	{
		if ( !node.TryGetProperty( "extras", out var extras ) || extras.ValueKind != JsonValueKind.Object || !extras.TryGetProperty( Extra, out var lod ) || lod.ValueKind != JsonValueKind.Object ) return null;
		float Number( string name, float fallback ) => lod.TryGetProperty( name, out var value ) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : fallback;
		var lockBorder = lod.TryGetProperty( "lockBorder", out var flag ) && flag.ValueKind == JsonValueKind.True;
		return new GltfNativeLodSettings( Math.Clamp( Number( "ratio", 0.5f ), 0.001f, 1f ), Math.Clamp( Number( "error", 0.01f ), 0f, 1f ), lockBorder );
	}

	internal string Key => $"lod:{Ratio}:{Error}:{LockBorder}";
}

/// <summary>Import-time triangle reduction of a baked mesh with meshoptimizer. The vertices stay; only the index buffer changes.</summary>
internal static class GltfNativeSimplify
{
	/// <summary>
	/// Reduced triangle indices for the packed vertices of a baked row. Normals and the first UV set weigh in, so
	/// shading and texture seams survive. Returns the input when the row is not triangles.
	/// </summary>
	internal static unsafe int[] Indices( byte[] vertices, int stride, IReadOnlyList<GltfNativeVertexField> fields, int[] indices, int mode, GltfNativeLodSettings settings )
	{
		if ( mode != 4 || indices.Length < 6 || settings.Ratio >= 1 ) return indices;
		var position = fields.First( x => x.Semantic == "position" ).Offset;
		var normal = fields.FirstOrDefault( x => x.Semantic == "normal" );
		var uv = fields.FirstOrDefault( x => x.Semantic == "texcoord" && x.Format == ColorFormat.COLOR_FORMAT_R32G32_FLOAT );
		var count = vertices.Length / stride;
		var positions = new float[count * 3];
		var attributes = new float[count * 5];
		for ( var v = 0; v < count; v++ )
		{
			var at = v * stride;
			for ( var c = 0; c < 3; c++ ) positions[v * 3 + c] = BinaryPrimitives.ReadSingleLittleEndian( vertices.AsSpan( at + position + c * 4 ) );
			if ( normal is not null )
				for ( var c = 0; c < 3; c++ ) attributes[v * 5 + c] = BinaryPrimitives.ReadInt16LittleEndian( vertices.AsSpan( at + normal.Offset + c * 2 ) ) / 32767f;
			if ( uv is not null )
				for ( var c = 0; c < 2; c++ ) attributes[v * 5 + 3 + c] = BinaryPrimitives.ReadSingleLittleEndian( vertices.AsSpan( at + uv.Offset + c * 4 ) );
		}
		float[] weights = [normal is null ? 0 : 0.5f, normal is null ? 0 : 0.5f, normal is null ? 0 : 0.5f, uv is null ? 0 : 1, uv is null ? 0 : 1];
		var target = Math.Max( 3, (int)(indices.Length * settings.Ratio) / 3 * 3 );
		var output = new uint[indices.Length];
		var options = settings.LockBorder ? (uint)SimplifyOptions.LockBorder : 0;
		nuint written;
		float error;
		fixed ( int* source = indices )
		fixed ( uint* destination = output )
		fixed ( float* p = positions )
		fixed ( float* a = attributes )
		fixed ( float* w = weights )
			written = MeshOptimizer.SimplifyWithAttributes( destination, (uint*)source, (nuint)indices.Length, p, (nuint)count, 12, a, 20, w, 5, null, (nuint)target, settings.Error, options, &error );
		var result = new uint[(int)written];
		fixed ( uint* from = output )
		fixed ( uint* to = result )
			MeshOptimizer.OptimizeVertexCache( to, from, written, (nuint)count );
		return result.Select( x => (int)x ).ToArray();
	}
}
