using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

/// <summary>The receipt must declare the actual shader/material UV mapping, never infer it from a passing test.</summary>
public enum TextureTransformUvRepresentation { RawSource, BakedBaseColor }

/// <summary>Independent source-based output checks. A raw UV pass is not a texture-transform/material pass.</summary>
internal static class TextureTransformGpuChecks
{
	public sealed record Measurement( int Primitives, int Vertices, TextureTransformUvRepresentation UvRepresentation,
		float MaximumPositionErrorInches, float MaximumNormalError, double MaximumUvError, bool Finite, bool WindingCorrect,
		string Scope = "GPU data only; raw source UV or explicitly declared baked base-color coordinates. Texture binding, sampling and image appearance require separate evidence." );

	public static Measurement MeasureCurrent( GltfGpuInstance instance, NMatrix placement, TextureTransformUvRepresentation uvRepresentation )
	{
		ArgumentNullException.ThrowIfNull( instance );
		Require( instance.SubmittedRevision == instance.PoseRevision && instance.DrawCalls > 0 && instance.CallbackErrors.Length == 0, "No successful current direct GPU submission, or callback error." );
		return MeasureSnapshots( instance.Readback(), placement, uvRepresentation );
	}

	public static Measurement MeasureSnapshots( IReadOnlyList<GltfGpuPrimitiveSnapshot> rows, NMatrix placement, TextureTransformUvRepresentation uvRepresentation )
	{
		Require( rows is not null && rows.Count == 12 && rows.Select( row => row.Node ).Distinct().Count() == 12, "Expected all 12 independently instanced node primitives." );
		Require( uvRepresentation is TextureTransformUvRepresentation.RawSource or TextureTransformUvRepresentation.BakedBaseColor, "Unknown UV representation." );
		Require( NMatrix.Invert( placement, out var inverse ), "This model's normal oracle requires an invertible placement." );
		var normal = NVector3.Normalize( NVector3.TransformNormal( NVector3.UnitX, NMatrix.Transpose( inverse ) ) );
		var finite = true; var correctWinding = true; var vertices = 0; var positionError = 0f; var normalError = 0f; double uvError = 0;
		foreach ( var row in rows )
		{
			Require( row.Node >= 0 && row.Node < 12 && row.Mesh == TextureTransformReference.NodeMeshes[row.Node] && row.Primitive == 0, "Node/mesh/primitive identity changed." );
			Require( row.VertexCount >= 4 && row.VertexStride > 0 && row.Bytes.Length == checked(row.VertexCount * row.VertexStride), "Malformed output buffer extent." );
			Require( row.SourceVertices.Length == row.VertexCount && row.SourceVertices.All( index => index < 4 ), "Missing flat-corner source provenance." );
			Require( row.Indices.Length == 6 && row.Indices.All( index => index < row.VertexCount ), "Malformed quad topology." );
			var position = Field( row, "POSITION", 3 ); var direction = Field( row, "NORMAL", 3 ); var uv = Field( row, "TEXCOORD", 2 );
			for ( var output = 0; output < row.VertexCount; output++ )
			{
				var source = (int)row.SourceVertices[output];
				var expectedPosition = NVector3.Transform( TextureTransformReference.EnginePosition( row.Node, source ), placement );
				var actualPosition = Float3( row, output, position ); var actualNormal = Float3( row, output, direction );
				var expectedSourceUv = TextureTransformReference.SourceUv( row.Mesh, source );
				var expectedUv = uvRepresentation == TextureTransformUvRepresentation.BakedBaseColor
					? TextureTransformReference.SampleUv( row.Mesh, expectedSourceUv.X, expectedSourceUv.Y ) : new[] { (double)expectedSourceUv.X, expectedSourceUv.Y };
				var actualUv = Float2( row, output, uv );
				finite &= IsFinite( actualPosition ) && IsFinite( actualNormal ) && float.IsFinite( actualUv.X ) && float.IsFinite( actualUv.Y );
				positionError = MathF.Max( positionError, NVector3.Distance( actualPosition, expectedPosition ) );
				normalError = MathF.Max( normalError, NVector3.Distance( actualNormal, normal ) );
				uvError = Math.Max( uvError, Math.Max( Math.Abs( expectedUv[0] - actualUv.X ), Math.Abs( expectedUv[1] - actualUv.Y ) ) );
			}
			var expectedIndices = (uint[])TextureTransformReference.Indices.Clone();
			if ( placement.GetDeterminant() < 0 ) for ( var triangle = 0; triangle < expectedIndices.Length; triangle += 3 )
				(expectedIndices[triangle + 1], expectedIndices[triangle + 2]) = (expectedIndices[triangle + 2], expectedIndices[triangle + 1]);
			correctWinding &= CanonicalTriangles( expectedIndices ).SequenceEqual( CanonicalTriangles( row.Indices.Select( index => row.SourceVertices[index] ).ToArray() ) );
			vertices += row.VertexCount;
		}
		return new Measurement( rows.Count, vertices, uvRepresentation, positionError, normalError, uvError, finite, correctWinding );
	}

	public static void RequireWithin( Measurement value, float positionToleranceInches, float normalTolerance, double uvTolerance )
	{
		Require( float.IsFinite( positionToleranceInches ) && positionToleranceInches >= 0 && float.IsFinite( normalTolerance ) && normalTolerance >= 0 && double.IsFinite( uvTolerance ) && uvTolerance >= 0, "Invalid explicitly reviewed tolerance." );
		Require( value.Finite && value.WindingCorrect, "Nonfinite or incorrectly wound GPU output." );
		Require( value.MaximumPositionErrorInches <= positionToleranceInches && value.MaximumNormalError <= normalTolerance && value.MaximumUvError <= uvTolerance,
			$"GPU error exceeds reviewed limits: position={value.MaximumPositionErrorInches:R} inches, normal={value.MaximumNormalError:R}, UV={value.MaximumUvError:R}." );
	}

	private static GltfGpuField Field( GltfGpuPrimitiveSnapshot row, string semantic, int components )
	{
		var field = row.Fields.Single( value => value.Semantic == semantic && value.SemanticIndex == 0 );
		Require( field.ComponentType == 5126 && field.Components >= components && field.Offset >= 0 && field.Offset + components * 4 <= row.VertexStride, "Unexpected declared diagnostic component representation." );
		return field;
	}
	private static NVector2 Float2( GltfGpuPrimitiveSnapshot row, int vertex, GltfGpuField field )
	{
		var bytes = row.Bytes.AsSpan( checked(vertex * row.VertexStride + field.Offset) ); return new NVector2( BinaryPrimitives.ReadSingleLittleEndian( bytes ), BinaryPrimitives.ReadSingleLittleEndian( bytes[4..] ) );
	}
	private static NVector3 Float3( GltfGpuPrimitiveSnapshot row, int vertex, GltfGpuField field )
	{
		var bytes = row.Bytes.AsSpan( checked(vertex * row.VertexStride + field.Offset) ); return new NVector3( BinaryPrimitives.ReadSingleLittleEndian( bytes ), BinaryPrimitives.ReadSingleLittleEndian( bytes[4..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[8..] ) );
	}
	private static bool IsFinite( NVector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );
	private static string[] CanonicalTriangles( uint[] values )
	{
		var result = new string[values.Length / 3];
		for ( var i = 0; i < result.Length; i++ )
		{
			var a = values[i * 3]; var b = values[i * 3 + 1]; var c = values[i * 3 + 2];
			if ( b < a && b < c ) (a, b, c) = (b, c, a); else if ( c < a && c < b ) (a, b, c) = (c, a, b);
			result[i] = $"{a}/{b}/{c}";
		}
		Array.Sort( result, StringComparer.Ordinal ); return result;
	}
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
