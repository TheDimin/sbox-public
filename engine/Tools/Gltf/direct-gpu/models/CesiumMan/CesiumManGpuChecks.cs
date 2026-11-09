using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.CesiumMan;

using NV3 = System.Numerics.Vector3;
using NMatrix = System.Numerics.Matrix4x4;

/// <summary>
/// Checks completed GPU output against independently authored source reference.
/// The coordinator owns rendering, readback synchronization and capture controls.
/// </summary>
public static class CesiumManGpuChecks
{
	// Provisional numerical gates, not visual comparison waivers. Record observed
	// errors and review the gates after first measured native/GPU execution.
	public const float PositionToleranceInches = .001f;
	public const float NormalTolerance = .0002f;

	public static object AssertSnapshot( string variant, GltfGpuPrimitiveSnapshot snapshot, JsonElement sample, NMatrix placement )
	{
		Require( variant is "glTF" or "glTF-Embedded" or "glTF-Binary" or "glTF-Draco", "Unknown CesiumMan source variant." );
		Require( sample.GetProperty( "variant" ).GetString() == variant, "Sample oracle belongs to a different source variant; geometry equivalence does not imply identical animation duration." );
		Require( snapshot.IndexDataOrigin == "gpu-readback", "Actual bound GPU index readback is required; CPU submission metadata cannot prove the index buffer." );
		Require( snapshot.Node == 2 && snapshot.Mesh == 0 && snapshot.Primitive == 0, "CesiumMan output does not identify the authored mesh node/primitive." );
		Require( snapshot.VertexCount > 0 && snapshot.VertexStride > 0, "Empty GPU snapshot cannot pass." );
		Require( snapshot.Bytes.Length == checked(snapshot.VertexCount * snapshot.VertexStride), "GPU output byte length differs from its declaration." );
		Require( snapshot.SourceVertices.Length == snapshot.VertexCount, "GPU output is missing its source-vertex correspondence." );
		Require( snapshot.Indices.Length == 14016 && snapshot.Indices.All( index => index < snapshot.VertexCount ), "GPU topology differs from the source triangle count or references missing vertices." );
		var position = snapshot.Fields.Single( field => field.Semantic == "POSITION" && field.SemanticIndex == 0 );
		var normal = snapshot.Fields.Single( field => field.Semantic == "NORMAL" && field.SemanticIndex == 0 );
		Require( position.ComponentType == 5126 && position.Components >= 3 && normal.ComponentType == 5126 && normal.Components >= 3,
			"GPU diagnostic currently requires declared float3-compatible output fields." );
		Require( position.Offset >= 0 && position.Offset + 12 <= snapshot.VertexStride && normal.Offset >= 0 && normal.Offset + 12 <= snapshot.VertexStride,
			"Declared GPU fields escape their runtime stride." );
		Require( NMatrix.Invert( placement, out var inversePlacement ), "Test placement must be invertible for its independent normal reference." );
		var normalPlacement = NMatrix.Transpose( inversePlacement );
		var positions = sample.GetProperty( "positions_gltf" ); var normals = sample.GetProperty( "normals_gltf" );
		Require( positions.GetArrayLength() > 0 && normals.GetArrayLength() == positions.GetArrayLength(), "Empty/mismatched independent geometry reference." );
		var maxPositionError = 0f; var maxNormalError = 0f; var minNormalLength = float.PositiveInfinity; var maxNormalLength = 0f;
		for ( var vertex = 0; vertex < snapshot.VertexCount; vertex++ )
		{
			var source = checked((int)snapshot.SourceVertices[vertex]);
			Require( source < positions.GetArrayLength(), "GPU source-vertex mapping escapes the independently decoded source accessor." );
			var sourcePosition = positions[source]; var sourceNormal = normals[source];
			// Explicit independent basis: source meters (x,y,z) -> engine inches (z,x,y).
			var expectedPosition = NV3.Transform( new NV3( sourcePosition[2].GetSingle(), sourcePosition[0].GetSingle(), sourcePosition[1].GetSingle() ) / .0254f, placement );
			var expectedNormal = NV3.Normalize( NV3.TransformNormal( new NV3( sourceNormal[2].GetSingle(), sourceNormal[0].GetSingle(), sourceNormal[1].GetSingle() ), normalPlacement ) );
			var actualPosition = Vector( snapshot, vertex, position.Offset ); var actualNormal = Vector( snapshot, vertex, normal.Offset );
			Require( Finite( actualPosition ) && Finite( actualNormal ), $"Nonfinite GPU vertex {vertex}." );
			var positionError = NV3.Distance( expectedPosition, actualPosition ); var normalError = NV3.Distance( expectedNormal, actualNormal );
			maxPositionError = MathF.Max( maxPositionError, positionError ); maxNormalError = MathF.Max( maxNormalError, normalError );
			minNormalLength = MathF.Min( minNormalLength, actualNormal.Length() ); maxNormalLength = MathF.Max( maxNormalLength, actualNormal.Length() );
			Require( positionError <= PositionToleranceInches, $"GPU position mismatch vertex={vertex},source={source},error_inches={positionError},sample={sample.GetProperty( "label" ).GetString()}." );
			Require( normalError <= NormalTolerance, $"GPU normal mismatch vertex={vertex},source={source},error={normalError},sample={sample.GetProperty( "label" ).GetString()}." );
		}
		Require( minNormalLength >= 1-NormalTolerance && maxNormalLength <= 1+NormalTolerance, "GPU output normals are not normalized." );
		return new { variant, label = sample.GetProperty( "label" ).GetString(), snapshot.Node, snapshot.Mesh, snapshot.Primitive,
			snapshot.VertexCount, snapshot.VertexStride, source_vertices = positions.GetArrayLength(), max_position_error_inches = maxPositionError,
			max_normal_vector_error = maxNormalError, min_normal_length = minNormalLength, max_normal_length = maxNormalLength,
			position_gate_inches = PositionToleranceInches, normal_gate = NormalTolerance,
			index_data_origin = snapshot.IndexDataOrigin, source_vertices_origin = "CPU submission provenance",
			scope = "Completed GPU vertex readback vs independent source positions and normals; raster/shader/timing/Model.Load tests are separate." };
	}

	public static void AssertDistinctPoses( GltfGpuPrimitiveSnapshot first, GltfGpuPrimitiveSnapshot second )
	{
		Require( first.VertexCount == second.VertexCount && first.SourceVertices.SequenceEqual( second.SourceVertices ), "Instance topology differs unexpectedly." );
		var firstPosition = first.Fields.Single( field => field.Semantic == "POSITION" && field.SemanticIndex == 0 );
		var secondPosition = second.Fields.Single( field => field.Semantic == "POSITION" && field.SemanticIndex == 0 );
		Require( Enumerable.Range( 0, first.VertexCount ).Any( vertex => NV3.Distance( Vector( first, vertex, firstPosition.Offset ), Vector( second, vertex, secondPosition.Offset ) ) > .01f ),
			"Independent animation times unexpectedly produced identical GPU geometry." );
	}

	public static object AssertTopology( GltfGpuPrimitiveSnapshot snapshot, JsonElement oracle )
	{
		Require( snapshot.IndexDataOrigin == "gpu-readback", "Topology requires actual bound GPU index readback." );
		var expected = oracle.GetProperty( "indices" );
		Require( expected.GetArrayLength() == snapshot.Indices.Length && snapshot.Indices.Length % 3 == 0,
			"GPU index count differs from the independently decoded triangle source." );
		var windingPreserved = 0; var windingReversed = 0;
		for ( var triangle = 0; triangle < snapshot.Indices.Length; triangle += 3 )
		{
			var source = new uint[3]; var actual = new uint[3];
			for ( var corner = 0; corner < 3; corner++ )
			{
				source[corner] = expected[triangle + corner].GetUInt32();
				var outputVertex = snapshot.Indices[triangle + corner];
				Require( outputVertex < snapshot.SourceVertices.Length, "GPU triangle index escapes source-vertex mapping." );
				actual[corner] = snapshot.SourceVertices[outputVertex];
			}
			bool Cyclic( bool reverse ) => Enumerable.Range( 0, 3 ).Any( start => Enumerable.Range( 0, 3 ).All( corner => source[corner] == actual[(start + (reverse ? 3-corner : corner)) % 3] ) );
			var preserved = Cyclic( false ); var reversed = Cyclic( true );
			Require( preserved || reversed, $"GPU topology changed source triangle {triangle / 3}." );
			if ( preserved ) windingPreserved++; else windingReversed++;
		}
		Require( windingPreserved == 0 || windingReversed == 0, "GPU output inconsistently reverses source winding across the primitive." );
		return new { triangles = snapshot.Indices.Length / 3, index_data_origin = snapshot.IndexDataOrigin,
			source_vertices_origin = "CPU submission provenance", source_winding_preserved = windingPreserved,
			source_winding_reversed = windingReversed, scope = "Source triangle connectivity and consistent winding mapping only; front-face visibility requires independent raster controls." };
	}

	private static NV3 Vector( GltfGpuPrimitiveSnapshot row, int vertex, int offset )
	{
		var bytes = row.Bytes.AsSpan( checked(vertex * row.VertexStride + offset), 12 );
		return new NV3( BinaryPrimitives.ReadSingleLittleEndian( bytes ), BinaryPrimitives.ReadSingleLittleEndian( bytes[4..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[8..] ) );
	}
	private static bool Finite( NV3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );
	private static void Require( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
	}
}
