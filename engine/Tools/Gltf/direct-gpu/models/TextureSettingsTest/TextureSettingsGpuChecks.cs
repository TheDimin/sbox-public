using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

using V3 = System.Numerics.Vector3;
using Matrix = System.Numerics.Matrix4x4;

/// <summary>Actual model GPU output oracle, independent of producer pose/material evaluation.</summary>
internal static class TextureSettingsGpuChecks
{
	public sealed record Measurement( int Primitives, int Vertices, int Indices, float PositionErrorInches, float NormalError, float UvError,
		bool Finite, bool CorrectIndices, int OutsideUvVertices, string Scope = "Actual GPU vertex/index data; material pixels and double-sided appearance are separate gates." );

	public static Measurement MeasureCurrent( string checkout, GltfGpuInstance instance, Matrix placement )
	{
		Require( instance.SubmittedRevision == instance.PoseRevision && instance.DrawCalls > 0 && instance.CallbackErrors.Length == 0, "No successful current GPU model submission." );
		return MeasureSnapshots( checkout, instance.Readback(), placement );
	}

	public static Measurement MeasureSnapshots( string checkout, IReadOnlyList<GltfGpuPrimitiveSnapshot> rows, Matrix placement )
	{
		using var reference = new TextureSettingsReference( checkout );
		Require( rows.Count == 10 && rows.Select( row => row.Node ).Distinct().Count() == 10, "Expected ten source mesh nodes." );
		Require( Matrix.Invert( placement, out var inverse ), "Normal oracle requires invertible placement." );
		var normalMatrix = Matrix.Transpose( inverse ); var positionError = 0f; var normalError = 0f; var uvError = 0f;
		var vertices = 0; var indices = 0; var outside = 0; var finite = true; var correctIndices = true;
		foreach ( var row in rows )
		{
			Require( row.Node is >= 1 and <= 10 && row.Mesh == row.Node - 1 && row.Primitive == 0, "Pinned node/mesh identity changed." );
			Require( row.IndexDataOrigin == "gpu-readback", "Index bytes must come from the actually bound GPU buffer." );
			var positions = reference.Attribute( row.Mesh, "POSITION" ); var normals = reference.Attribute( row.Mesh, "NORMAL" );
			var uvs = row.Mesh == 4 ? null : reference.Attribute( row.Mesh, "TEXCOORD_0" );
			Require( row.VertexCount == positions.Length && row.SourceVertices.Length == row.VertexCount && row.Bytes.Length == row.VertexCount * row.VertexStride, "Unexpected output extent or authored-normal expansion." );
			var p = Field( row, "POSITION", 3 ); var n = Field( row, "NORMAL", 3 ); var uv = Field( row, "TEXCOORD", 2 );
			for ( var vertex = 0; vertex < row.VertexCount; vertex++ )
			{
				Require( row.SourceVertices[vertex] == vertex, "This explicitly normaled source must retain vertex identity." );
				var s = positions[vertex]; var sn = normals[vertex];
				// Literal authored root: (x,y,z)->(x,z,-y); engine basis ->(-y,x,z).
				var expectedP = V3.Transform( new V3( -s[1], s[0], s[2] ) / .0254f, placement );
				var expectedN = V3.Normalize( V3.TransformNormal( new V3( -sn[1], sn[0], sn[2] ), normalMatrix ) );
				var actualP = Read3( row, vertex, p ); var actualN = Read3( row, vertex, n );
				finite &= Finite( actualP ) && Finite( actualN );
				positionError = MathF.Max( positionError, V3.Distance( expectedP, actualP ) ); normalError = MathF.Max( normalError, V3.Distance( expectedN, actualN ) );
				for ( var component = 0; component < 2; component++ )
				{
					var expected = uvs is null ? 0 : uvs[vertex][component]; var actual = Read( row, vertex, uv.Offset + component * 4 );
					finite &= float.IsFinite( actual ); uvError = MathF.Max( uvError, MathF.Abs( expected - actual ) );
				}
				if ( uvs is not null && uvs[vertex].Any( x => x < 0 || x > 1 ) ) outside++;
			}
			var expectedIndices = reference.Indices( row.Mesh );
			if ( placement.GetDeterminant() < 0 ) for ( var triangle = 0; triangle < expectedIndices.Length; triangle += 3 )
				(expectedIndices[triangle + 1], expectedIndices[triangle + 2]) = (expectedIndices[triangle + 2], expectedIndices[triangle + 1]);
			correctIndices &= expectedIndices.SequenceEqual( row.Indices ); vertices += row.VertexCount; indices += row.Indices.Length;
		}
		Require( vertices == 144 && indices == 216 && outside > 0, "Pinned source topology or out-of-range sampler witnesses were lost." );
		return new( rows.Count, vertices, indices, positionError, normalError, uvError, finite, correctIndices, outside );
	}

	private static GltfGpuField Field( GltfGpuPrimitiveSnapshot row, string semantic, int components )
	{
		var field = row.Fields.Single( f => f.Semantic == semantic && f.SemanticIndex == 0 );
		Require( field.ComponentType == 5126 && field.Components >= components && field.Offset >= 0 && field.Offset + components * 4 <= row.VertexStride, "Unexpected output field representation." ); return field;
	}
	private static float Read( GltfGpuPrimitiveSnapshot row, int vertex, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + offset, 4 ) );
	private static V3 Read3( GltfGpuPrimitiveSnapshot row, int vertex, GltfGpuField field ) => new( Read( row, vertex, field.Offset ), Read( row, vertex, field.Offset + 4 ), Read( row, vertex, field.Offset + 8 ) );
	private static bool Finite( V3 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y ) && float.IsFinite( v.Z );
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
