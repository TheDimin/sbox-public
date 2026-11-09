using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>Independent literal expectations for the adversarial upload fixture.</summary>
internal static class GltfGpuRawTriangleChecks
{
	public static object Validate( GltfGpuInstance instance )
	{
		if ( instance.DrawCalls <= 0 || instance.SubmittedRevision != instance.PoseRevision || instance.CallbackErrors.Length != 0 ) throw new InvalidOperationException( "No successful current-pose raw GPU submission." );
		var rows = instance.Readback();
		if ( rows.Count != 1 ) throw new InvalidOperationException( "Triangle should produce exactly one primitive." );
		var row = rows[0];
		if ( row.IndexDataOrigin != "gpu-readback" ) throw new InvalidOperationException( "Index checks require actual bound GPU index data." );
		if ( row.Node != 0 || row.Mesh != 0 || row.Primitive != 0 || row.VertexCount != 3 ) throw new InvalidOperationException( "Triangle provenance/count mismatch." );
		if ( !row.SourceVertices.SequenceEqual( new uint[] { 0, 1, 2 } ) || !row.Indices.SequenceEqual( new uint[] { 0, 1, 2 } ) ) throw new InvalidOperationException( "Triangle topology/source map mismatch." );
		var fields = row.Fields.ToDictionary( x => (x.Semantic, x.SemanticIndex) );
		var positions = new[] { new[] { 0f, 0f, 0f }, new[] { 0f, 1f / .0254f, 0f }, new[] { 0f, 0f, 1f / .0254f } };
		var normal = new[] { 1f, 0f, 0f }; var tangent = new[] { 0f, 1f, 0f, 1f };
		var color = new[] { 64f / 255f, 128f / 255f, 192f / 255f, 1f }; var uv = new[] { 0f, 0f };
		var errors = new Dictionary<string, float>( StringComparer.Ordinal );
		void Check( string semantic, int vertex, float[] expected )
		{
			var field = fields[(semantic, 0)];
			if ( field.ComponentType != 5126 || field.Components != expected.Length || field.Offset < 0 || field.Offset + field.Components * 4 > row.VertexStride ) throw new InvalidOperationException( "Incorrect runtime output descriptor: " + semantic );
			for ( var component = 0; component < expected.Length; component++ )
			{
				var actual = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + component * 4, 4 ) );
				if ( !float.IsFinite( actual ) ) throw new InvalidOperationException( "Nonfinite GPU triangle output: " + semantic );
				var error = MathF.Abs( actual - expected[component] ); errors[semantic] = Math.Max( errors.GetValueOrDefault( semantic ), error );
			}
		}
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			Check( "POSITION", vertex, positions[vertex] ); Check( "NORMAL", vertex, normal ); Check( "TANGENT", vertex, tangent ); Check( "COLOR", vertex, color ); Check( "TEXCOORD", vertex, uv );
		}
		// Arithmetic bounds for this literal unit fixture only, not approved
		// approximation tolerances for arbitrary model animation or rendering.
		const float epsilon = 1.1920928955078125e-7f;
		if ( errors["POSITION"] > 4 * epsilon / .0254f || errors.Where( x => x.Key != "POSITION" ).Any( x => x.Value > 4 * epsilon ) ) throw new InvalidOperationException( "Raw GPU descriptor read/deformation disagrees with the literal triangle: " + string.Join( ", ", errors.Select( x => x.Key + "=" + x.Value ) ) );
		return new { passed = true, scope = "Actual current-revision raw GPU outputs for literal interleaved source data. Rendering/capture/lifetime checks are separate.",
			row.VertexCount, row.VertexStride, fields = row.Fields, max_component_errors = errors, instance.DrawCalls, instance.PoseRevision, instance.SubmittedRevision };
	}
}
