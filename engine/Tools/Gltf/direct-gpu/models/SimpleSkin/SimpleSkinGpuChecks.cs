using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.Validation;

using NVector3 = System.Numerics.Vector3;

/// <summary>
/// Checks diagnostic bytes returned from a completed direct-GPU submission.
/// This is intentionally separate from CPU pose tests and final image comparison.
/// No position or normal expectation is taken from the producer's CPU evaluator.
/// </summary>
internal static class SimpleSkinGpuChecks
{
	public sealed record Measurement( int OutputVertices, int Indices, float SourceTime, float MaximumPositionErrorInches,
		float MaximumNormalError, bool SourceTopologyMatches, bool Finite, float[] ComponentPositionErrors );

	public static Measurement Measure( IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, float sourceTime, Matrix4x4 placement )
	{
		SimpleSkinCoreTests.Require( snapshots is not null && snapshots.Count == 1, "SimpleSkin requires exactly one node/primitive output." );
		var row = snapshots[0];
		SimpleSkinCoreTests.Require( row.Node == 0 && row.Mesh == 0 && row.Primitive == 0, "Output lost node/mesh/primitive identity." );
		SimpleSkinCoreTests.Require( row.VertexCount > 0 && row.VertexStride > 0 && row.Bytes is not null && row.Bytes.Length == checked(row.VertexCount * row.VertexStride), "Output buffer extent does not match its runtime stride/count." );
		SimpleSkinCoreTests.Require( row.SourceVertices is not null && row.SourceVertices.Length == row.VertexCount && row.SourceVertices.All( source => source < 10 ), "Flat-corner source mapping is missing or out of range." );
		SimpleSkinCoreTests.Require( row.IndexDataOrigin == "gpu-readback", "Topology acceptance requires the actual bound index buffer readback, not CPU submission metadata." );
		SimpleSkinCoreTests.Require( row.Indices is not null && row.Indices.Length == 24 && row.Indices.All( index => index < row.VertexCount ), "Output topology is missing or out of range." );
		var position = row.Fields.Single( field => field.Semantic == "POSITION" && field.SemanticIndex == 0 );
		var normal = row.Fields.Single( field => field.Semantic == "NORMAL" && field.SemanticIndex == 0 );
		SimpleSkinCoreTests.Require( position.ComponentType == 5126 && position.Components >= 3 && position.Offset >= 0 && position.Offset + 12 <= row.VertexStride, "Position readback is not the declared FLOAT3 output representation." );
		SimpleSkinCoreTests.Require( normal.ComponentType == 5126 && normal.Components >= 3 && normal.Offset >= 0 && normal.Offset + 12 <= row.VertexStride, "Generated normal readback is not the declared FLOAT3 output representation." );
		SimpleSkinCoreTests.Require( Matrix4x4.Invert( placement, out var inverse ), "Normal check requires an invertible test placement." );
		var expectedNormal = NVector3.Normalize( NVector3.TransformNormal( NVector3.UnitX, Matrix4x4.Transpose( inverse ) ) );
		var maximumPosition = 0f; var maximumNormal = 0f; var finite = true;
		var componentErrors = new float[3];
		for ( var vertex = 0; vertex < row.VertexCount; vertex++ )
		{
			var expected = NVector3.Transform( SimpleSkinReference.ToEngine( SimpleSkinReference.Position( checked((int)row.SourceVertices[vertex]), sourceTime ) ), placement );
			var actual = Read( row.Bytes, vertex * row.VertexStride + position.Offset );
			var observedNormal = Read( row.Bytes, vertex * row.VertexStride + normal.Offset );
			finite &= IsFinite( actual ) && IsFinite( observedNormal );
			var difference = NVector3.Abs( expected - actual );
			componentErrors[0] = MathF.Max( componentErrors[0], difference.X );
			componentErrors[1] = MathF.Max( componentErrors[1], difference.Y );
			componentErrors[2] = MathF.Max( componentErrors[2], difference.Z );
			maximumPosition = MathF.Max( maximumPosition, MathF.Max( difference.X, MathF.Max( difference.Y, difference.Z ) ) );
			maximumNormal = MathF.Max( maximumNormal, NVector3.Distance( expectedNormal, observedNormal ) );
		}
		var expectedIndices = (uint[])SimpleSkinReference.TriangleIndices.Clone();
		if ( placement.GetDeterminant() < 0 )
			for ( var triangle = 0; triangle < expectedIndices.Length; triangle += 3 )
				(expectedIndices[triangle], expectedIndices[triangle + 1]) = (expectedIndices[triangle + 1], expectedIndices[triangle]);
		var topologyMatches = CanonicalTriangles( row.Indices.Select( index => row.SourceVertices[index] ).ToArray() )
			.SequenceEqual( CanonicalTriangles( expectedIndices ) );
		return new Measurement( row.VertexCount, row.Indices.Length, sourceTime, maximumPosition, maximumNormal, topologyMatches, finite, componentErrors );
	}

	/// <summary>
	/// Tolerances must be supplied explicitly by the validation protocol after a
	/// measured error review. Measuring error alone is never a parity pass.
	/// </summary>
	public static void RequireWithin( Measurement measurement, float positionToleranceInches, float normalTolerance )
	{
		SimpleSkinCoreTests.Require( float.IsFinite( positionToleranceInches ) && positionToleranceInches >= 0 && float.IsFinite( normalTolerance ) && normalTolerance >= 0, "Invalid explicitly supplied numerical tolerances." );
		SimpleSkinCoreTests.Require( measurement.Finite && measurement.SourceTopologyMatches, "GPU output is nonfinite or changed source topology." );
		SimpleSkinCoreTests.Require( measurement.MaximumPositionErrorInches <= positionToleranceInches, $"GPU position error {measurement.MaximumPositionErrorInches:R} exceeds {positionToleranceInches:R} inches." );
		SimpleSkinCoreTests.Require( measurement.MaximumNormalError <= normalTolerance, $"GPU normal error {measurement.MaximumNormalError:R} exceeds {normalTolerance:R}." );
	}

	/// <summary>
	/// Literal CPU oracle geometry for an independent Graphics.Draw reference
	/// callback. This does not create a native Model or Mesh and must never be used
	/// as the implementation's actual mesh data or as a substitute GPU result.
	/// </summary>
	public static Sandbox.Vertex[] ReferenceTriangles( float sourceTime, Matrix4x4 placement )
	{
		SimpleSkinCoreTests.Require( Matrix4x4.Invert( placement, out var inverse ), "Reference needs invertible placement." );
		var normal = NVector3.Normalize( NVector3.TransformNormal( NVector3.UnitX, Matrix4x4.Transpose( inverse ) ) );
		var tangent = NVector3.Normalize( NVector3.TransformNormal( NVector3.UnitY, placement ) );
		var mirrored = placement.GetDeterminant() < 0;
		var output = new Sandbox.Vertex[SimpleSkinReference.TriangleIndices.Length];
		for ( var corner = 0; corner < output.Length; corner++ )
		{
			// The independent reference deliberately compensates mirrored winding;
			// identity-transform raw drawing cannot infer the baked placement sign.
			var sourceCorner = mirrored && corner % 3 != 2 ? corner + (corner % 3 == 0 ? 1 : -1) : corner;
			var position = NVector3.Transform( SimpleSkinReference.ToEngine( SimpleSkinReference.Position( checked((int)SimpleSkinReference.TriangleIndices[sourceCorner]), sourceTime ) ), placement );
			output[corner] = new Sandbox.Vertex( new global::Vector3( position.X, position.Y, position.Z ) )
			{
				Normal = new global::Vector3( normal.X, normal.Y, normal.Z ),
				Tangent = new global::Vector4( tangent.X, tangent.Y, tangent.Z, mirrored ? -1 : 1 ),
				Color = global::Color32.White
			};
		}
		return output;
	}

	private static NVector3 Read( byte[] bytes, int offset ) => new(
		BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset, 4 ) ),
		BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 4, 4 ) ),
		BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 8, 4 ) ) );

	private static bool IsFinite( NVector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );

	private static string[] CanonicalTriangles( uint[] indices )
	{
		var triangles = new string[indices.Length / 3];
		for ( var triangle = 0; triangle < triangles.Length; triangle++ )
		{
			var offset = triangle * 3;
			var a = indices[offset]; var b = indices[offset + 1]; var c = indices[offset + 2];
			// Cyclic rotation and triangle reordering preserve topology; swapping
			// two corners changes winding and must remain a detectable failure.
			if ( b < a && b < c ) (a, b, c) = (b, c, a);
			else if ( c < a && c < b ) (a, b, c) = (c, a, b);
			triangles[triangle] = $"{a}/{b}/{c}";
		}
		Array.Sort( triangles, StringComparer.Ordinal );
		return triangles;
	}
}
