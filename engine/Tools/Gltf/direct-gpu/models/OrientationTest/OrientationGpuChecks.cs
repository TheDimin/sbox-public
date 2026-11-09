using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Positive acceptance tests for real GPU readback. Expected geometry comes
/// from the independently generated source oracle, never production decode,
/// basis, pose or deformation helpers. This file preceded backend implementation.
/// </summary>
internal static class OrientationGpuChecks
{
	public sealed record Result( int Node, int Mesh, int Primitive, int Vertices, int Indices,
		float MaximumPositionErrorInches, float MaximumNormalError, bool Passed, string Error );

	public static Result[] ValidateCurrent( GltfGpuInstance instance, JsonElement expected, NMatrix placement )
	{
		OrientationCoreTests.Require( instance.DrawCalls > 0, "GPU model has not received a draw callback." );
		OrientationCoreTests.Require( instance.CallbackErrors.Length == 0, "GPU callback failed: " + string.Join( "\n", instance.CallbackErrors ) );
		OrientationCoreTests.Require( instance.SubmittedRevision == instance.PoseRevision, "Readback does not belong to the current placement/pose revision." );
		return Validate( instance.Readback(), expected, placement );
	}

	public static Result[] Validate( IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, JsonElement expected, NMatrix placement )
	{
		var reference = expected.GetProperty( "primitives" ).EnumerateArray().ToArray();
		OrientationCoreTests.Require( snapshots.Count == reference.Length, "GPU path lost or duplicated node instances." );
		var results = new List<Result>();
		foreach ( var row in reference )
		{
			var node = row.GetProperty( "node" ).GetInt32();
			var mesh = row.GetProperty( "mesh" ).GetInt32();
			var primitive = row.GetProperty( "primitive" ).GetInt32();
			var maxPosition = 0f;
			var maxNormal = 0f;
			var vertices = 0;
			var indices = 0;
			try
			{
				var actual = snapshots.Single( value => value.Node == node && value.Mesh == mesh && value.Primitive == primitive );
				OrientationCoreTests.Require( actual.IndexDataOrigin == "gpu-readback", "Topology assertions require actual bound index-buffer readback, not CPU submission metadata." );
				vertices = actual.VertexCount;
				indices = actual.Indices.Length;
				OrientationCoreTests.Require( actual.SourceVertices.Length == actual.VertexCount, "Output-to-source vertex mapping is incomplete." );
				OrientationCoreTests.Require( actual.Bytes.Length >= checked(actual.VertexCount * actual.VertexStride), "GPU readback is truncated." );
				var position = actual.Fields.Single( field => field.Semantic == "POSITION" && field.SemanticIndex == 0 );
				var normal = actual.Fields.Single( field => field.Semantic == "NORMAL" && field.SemanticIndex == 0 );
				var referencePositions = row.GetProperty( "engine_world_positions" );
				var referenceNormals = row.GetProperty( "engine_world_normals" );
				for ( var vertex = 0; vertex < actual.VertexCount; vertex++ )
				{
					var source = checked((int)actual.SourceVertices[vertex]);
					OrientationCoreTests.Require( (uint)source < (uint)referencePositions.GetArrayLength(), "GPU source-vertex map is out of range." );
					var expectedPosition = NVector.Transform( OrientationCoreTests.JsonVector( referencePositions[source] ), placement );
					var expectedNormal = TransformNormal( OrientationCoreTests.JsonVector( referenceNormals[source] ), placement );
					var actualPosition = Float3( actual, position, vertex );
					var actualNormal = Float3( actual, normal, vertex );
					maxPosition = MathF.Max( maxPosition, MaximumComponentError( expectedPosition, actualPosition ) );
					maxNormal = MathF.Max( maxNormal, MaximumComponentError( expectedNormal, actualNormal ) );
				}
				var sourceIndices = row.GetProperty( "indices" ).EnumerateArray().Select( value => value.GetUInt32() ).ToArray();
				if ( placement.GetDeterminant() < 0 )
					for ( var triangle = 0; triangle < sourceIndices.Length; triangle += 3 )
						(sourceIndices[triangle + 1], sourceIndices[triangle + 2]) = (sourceIndices[triangle + 2], sourceIndices[triangle + 1]);
				OrientationCoreTests.Require( actual.Indices.Length == sourceIndices.Length, "GPU topology count differs from source triangles." );
				for ( var index = 0; index < actual.Indices.Length; index++ )
				{
					var output = actual.Indices[index];
					OrientationCoreTests.Require( output < actual.SourceVertices.Length, "GPU output index is out of range." );
					OrientationCoreTests.Require( actual.SourceVertices[output] == sourceIndices[index], "GPU source triangle winding or source-vertex mapping changed." );
				}
				OrientationCoreTests.Require( maxPosition <= .001f, $"GPU world position error {maxPosition:R} inches exceeds .001." );
				OrientationCoreTests.Require( maxNormal <= .00001f, $"GPU normal component error {maxNormal:R} exceeds .00001." );
				results.Add( new Result( node, mesh, primitive, vertices, indices, maxPosition, maxNormal, true, null ) );
			}
			catch ( Exception error )
			{
				results.Add( new Result( node, mesh, primitive, vertices, indices, maxPosition, maxNormal, false, error.ToString() ) );
			}
		}
		return results.ToArray();
	}

	private static NVector Float3( GltfGpuPrimitiveSnapshot row, GltfGpuField field, int vertex )
	{
		// The field declares the computed output representation. It is deliberately
		// separate from source accessor formats and no GltfVertex layout is assumed.
		OrientationCoreTests.Require( field.ComponentType == 5126 && field.Components >= 3, "Expected declared FLOAT3-or-larger GPU output field." );
		OrientationCoreTests.Require( field.Offset >= 0 && field.Offset + 12 <= row.VertexStride, "Output field exceeds runtime vertex stride." );
		var offset = checked(vertex * row.VertexStride + field.Offset);
		var bytes = row.Bytes.AsSpan( offset, 12 );
		return new NVector( BinaryPrimitives.ReadSingleLittleEndian( bytes[..4] ), BinaryPrimitives.ReadSingleLittleEndian( bytes.Slice( 4, 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.Slice( 8, 4 ) ) );
	}

	private static float MaximumComponentError( NVector expected, NVector actual )
	{
		OrientationCoreTests.Require( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ), "GPU output contains a nonfinite value." );
		return MathF.Max( MathF.Abs( expected.X - actual.X ), MathF.Max( MathF.Abs( expected.Y - actual.Y ), MathF.Abs( expected.Z - actual.Z ) ) );
	}

	internal static NVector TransformNormal( NVector normal, NMatrix matrix )
	{
		// Independent cofactor calculation for System.Numerics row-vector placement.
		// No production inverse-normal helper or TRS decomposition is consumed.
		var a = new NVector( matrix.M11, matrix.M12, matrix.M13 );
		var b = new NVector( matrix.M21, matrix.M22, matrix.M23 );
		var c = new NVector( matrix.M31, matrix.M32, matrix.M33 );
		var det = NVector.Dot( a, NVector.Cross( b, c ) );
		OrientationCoreTests.Require( det != 0 && float.IsFinite( det ), "Normal reference requires finite nonsingular placement." );
		return NVector.Normalize( (NVector.Cross( b, c ) * normal.X + NVector.Cross( c, a ) * normal.Y + NVector.Cross( a, b ) * normal.Z) / det );
	}
}
