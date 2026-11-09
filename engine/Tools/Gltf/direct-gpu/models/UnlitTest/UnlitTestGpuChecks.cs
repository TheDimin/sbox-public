using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector3 = System.Numerics.Vector3;

/// <summary>Consumes actual completed GPU output. It never calls the producer's CPU evaluator.</summary>
internal static class UnlitTestGpuChecks
{
	public sealed record Measurement( int Nodes, int Vertices, int Indices, bool Finite, bool TopologyMatches,
		float MaximumPositionErrorInches, float MaximumNormalError, float MaximumGeneratedColorError, float MaximumGeneratedUvError );

	public static Measurement Measure( IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, Matrix4x4 placement )
	{
		Require( snapshots is not null && snapshots.Count == 2, "UnlitTest needs exactly two node/primitive outputs." );
		Require( snapshots.Select( row => row.Node ).Order().SequenceEqual( new[] { 0, 1 } ), "Output node identity was duplicated/lost." );
		Require( Matrix4x4.Invert( placement, out var inverse ), "Test placement must be invertible." );
		var normalMatrix = Matrix4x4.Transpose( inverse );
		var maxPosition = 0f; var maxNormal = 0f; var maxColor = 0f; var maxUv = 0f;
		var finite = true; var topology = true; var vertices = 0; var indices = 0;
		foreach ( var row in snapshots )
		{
			Require( row.Mesh == row.Node && row.Primitive == 0 && row.VertexCount == 96, "Pinned node/mesh/primitive identity or vertex count changed." );
			Require( row.VertexStride > 0 && row.Bytes is not null && row.Bytes.Length == checked(row.VertexStride * row.VertexCount), "GPU output byte extent is invalid." );
			Require( row.SourceVertices is not null && row.SourceVertices.Length == row.VertexCount && row.SourceVertices.All( vertex => vertex < 96 ) && row.SourceVertices.Distinct().Count() == 96, "GPU source-vertex mapping is not a permutation of the authored vertices." );
			Require( row.Indices is not null && row.Indices.Length == 132 && row.Indices.All( index => index < row.VertexCount ), "GPU topology is missing or out of range." );
			Require( row.Fields is not null, "Runtime field declarations are missing." );
			var ordered = row.Fields.OrderBy( field => field.Offset ).ToArray();
			Require( ordered.All( field => field.ComponentType == 5126 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Components is >= 1 and <= 4 && field.Offset + field.Components * 4 <= row.VertexStride ), "Invalid output field representation/range." );
			for ( var field = 1; field < ordered.Length; field++ ) Require( ordered[field - 1].Offset + ordered[field - 1].Components * 4 <= ordered[field].Offset, "GPU output fields overlap." );
			var position = Field( row, "POSITION", 3 ); var normal = Field( row, "NORMAL", 3 );
			var color = Field( row, "COLOR", 4 ); var uv = Field( row, "TEXCOORD", 2 );
			for ( var vertex = 0; vertex < row.VertexCount; vertex++ )
			{
				var source = checked((int)row.SourceVertices[vertex]);
				var expectedPosition = Vector3.Transform( UnlitTestReference.EnginePosition( row.Node, source ), placement );
				var expectedNormal = Vector3.Normalize( Vector3.TransformNormal( UnlitTestReference.EngineNormal( source ), normalMatrix ) );
				var actualPosition = Read3( row, position, vertex ); var actualNormal = Read3( row, normal, vertex );
				finite &= Finite( actualPosition ) && Finite( actualNormal );
				maxPosition = MathF.Max( maxPosition, MaximumComponent( Vector3.Abs( expectedPosition - actualPosition ) ) );
				maxNormal = MathF.Max( maxNormal, Vector3.Distance( expectedNormal, actualNormal ) );
				for ( var component = 0; component < 4; component++ )
				{
					var value = Read( row, color, vertex, component ); finite &= float.IsFinite( value );
					maxColor = MathF.Max( maxColor, MathF.Abs( value - 1 ) );
				}
				for ( var component = 0; component < 2; component++ )
				{
					var value = Read( row, uv, vertex, component ); finite &= float.IsFinite( value );
					maxUv = MathF.Max( maxUv, MathF.Abs( value ) );
				}
			}
			var expectedIndices = (uint[])UnlitTestReference.Indices.Clone();
			if ( placement.GetDeterminant() < 0 )
				for ( var triangle = 0; triangle < expectedIndices.Length; triangle += 3 )
					(expectedIndices[triangle], expectedIndices[triangle + 1]) = (expectedIndices[triangle + 1], expectedIndices[triangle]);
			topology &= OrientedTriangles( expectedIndices ).SequenceEqual( OrientedTriangles( row.Indices.Select( index => row.SourceVertices[index] ).ToArray() ) );
			vertices += row.VertexCount; indices += row.Indices.Length;
		}
		return new( snapshots.Count, vertices, indices, finite, topology, maxPosition, maxNormal, maxColor, maxUv );
	}

	public static void RequireWithin( Measurement measurement, float positionToleranceInches, float normalTolerance )
	{
		Require( float.IsFinite( positionToleranceInches ) && positionToleranceInches >= 0 && float.IsFinite( normalTolerance ) && normalTolerance >= 0, "Explicit tolerances must be finite/nonnegative." );
		Require( measurement.Finite && measurement.TopologyMatches && measurement.Nodes == 2 && measurement.Vertices == 192 && measurement.Indices == 264, "GPU output is nonfinite or changed the pinned scene/topology." );
		Require( measurement.MaximumPositionErrorInches <= positionToleranceInches, $"Position error {measurement.MaximumPositionErrorInches:R} exceeds {positionToleranceInches:R} inches." );
		Require( measurement.MaximumNormalError <= normalTolerance, $"Normal error {measurement.MaximumNormalError:R} exceeds {normalTolerance:R}." );
		Require( measurement.MaximumGeneratedColorError == 0 && measurement.MaximumGeneratedUvError == 0, "Missing authored colors/UVs must generate white/zero. Material factors are not vertex colors." );
	}

	/// <summary>Independent reference only: literal triangle drawing, with no native Model/Mesh.</summary>
	public static global::Sandbox.Vertex[] ReferenceTriangles( int node, Matrix4x4 placement )
	{
		Require( node is 0 or 1 && Matrix4x4.Invert( placement, out _ ), "Reference node/placement is invalid." );
		Matrix4x4.Invert( placement, out var inverse ); var normalMatrix = Matrix4x4.Transpose( inverse );
		var mirrored = placement.GetDeterminant() < 0; var result = new global::Sandbox.Vertex[132];
		for ( var corner = 0; corner < result.Length; corner++ )
		{
			var sourceCorner = mirrored && corner % 3 != 2 ? corner + (corner % 3 == 0 ? 1 : -1) : corner;
			var source = checked((int)UnlitTestReference.Indices[sourceCorner]);
			var p = Vector3.Transform( UnlitTestReference.EnginePosition( node, source ), placement );
			var n = Vector3.Normalize( Vector3.TransformNormal( UnlitTestReference.EngineNormal( source ), normalMatrix ) );
			var tangent = Vector3.Normalize( Vector3.Cross( MathF.Abs( n.Z ) < .9f ? Vector3.UnitZ : Vector3.UnitY, n ) );
			result[corner] = new global::Sandbox.Vertex( new global::Vector3( p.X, p.Y, p.Z ) )
			{
				Normal = new global::Vector3( n.X, n.Y, n.Z ), Tangent = new global::Vector4( tangent.X, tangent.Y, tangent.Z, mirrored ? -1 : 1 ), Color = global::Color32.White
			};
		}
		return result;
	}

	private static GltfGpuField Field( GltfGpuPrimitiveSnapshot row, string semantic, int components )
	{
		var field = row.Fields.Single( field => field.Semantic == semantic && field.SemanticIndex == 0 );
		Require( field.Components == components, "Unexpected output field dimensions: " + semantic ); return field;
	}
	private static float Read( GltfGpuPrimitiveSnapshot row, GltfGpuField field, int vertex, int component ) => BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + component * 4, 4 ) );
	private static Vector3 Read3( GltfGpuPrimitiveSnapshot row, GltfGpuField field, int vertex ) => new( Read( row, field, vertex, 0 ), Read( row, field, vertex, 1 ), Read( row, field, vertex, 2 ) );
	private static bool Finite( Vector3 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y ) && float.IsFinite( v.Z );
	private static float MaximumComponent( Vector3 v ) => MathF.Max( v.X, MathF.Max( v.Y, v.Z ) );
	private static void Require( bool condition, string message ) => UnlitTestCoreTests.Require( condition, message );
	private static string[] OrientedTriangles( uint[] values ) => Enumerable.Range( 0, values.Length / 3 ).Select( triangle =>
	{
		var a = values[triangle * 3]; var b = values[triangle * 3 + 1]; var c = values[triangle * 3 + 2];
		if ( b < a && b < c ) (a, b, c) = (b, c, a);
		else if ( c < a && c < b ) (a, b, c) = (c, a, b);
		return $"{a}/{b}/{c}";
	} ).OrderBy( value => value, StringComparer.Ordinal ).ToArray();
}

/// <summary>Checker controls use synthetic literal bytes; they are not GPU-execution evidence.</summary>
public static class UnlitTestGpuVerifierTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 10;
	public static Result[] RunAll( string checkoutRoot )
	{
		_ = checkoutRoot;
		var rows = new List<Result>();
		Run( "independent_literal_rows_accept_nonstandard_field_order", null );
		Run( "reject_wrong_position_view", row => Write3( row.Bytes, 36, UnlitTestReference.EngineNormal( 0 ) ) );
		Run( "reject_nan_position", row => Write( row.Bytes, 36, float.NaN ) );
		Run( "reject_normal_axis_swap", row => Write3( row.Bytes, 24, Vector3.UnitY ) );
		Run( "reject_winding_reversal", row => (row.Indices[0], row.Indices[1]) = (row.Indices[1], row.Indices[0]) );
		Run( "reject_wrong_source_vertex_mapping", row => (row.SourceVertices[0], row.SourceVertices[1]) = (row.SourceVertices[1], row.SourceVertices[0]) );
		Run( "reject_material_factor_baked_into_generated_vertex_color", row => Write( row.Bytes, 4, UnlitTestReference.BaseColors[0].Y ) );
		Run( "reject_nonzero_generated_uv", row => Write( row.Bytes, 16, .5f ) );
		Run( "reject_missing_node", null, missingNode: true );
		Run( "reject_duplicate_node_identity", null, duplicateNode: true );
		UnlitTestCoreTests.Require( rows.Count == ExpectedCases, "Frozen verifier inventory changed." );
		return rows.ToArray();

		void Run( string name, Action<GltfGpuPrimitiveSnapshot> mutate, bool missingNode = false, bool duplicateNode = false )
		{
			try
			{
				var samples = new[] { LiteralRow( 0 ), LiteralRow( duplicateNode ? 0 : 1 ) }; mutate?.Invoke( samples[0] );
				var expectFailure = mutate is not null || missingNode || duplicateNode;
				var rejected = false;
				try { UnlitTestGpuChecks.RequireWithin( UnlitTestGpuChecks.Measure( missingNode ? samples.Take( 1 ).ToArray() : samples, Matrix4x4.Identity ), 0, .000001f ); }
				catch ( InvalidOperationException ) { rejected = true; }
				UnlitTestCoreTests.Require( rejected == expectFailure, "GPU verifier sensitivity control had the wrong result." );
				rows.Add( new( name, "synthetic_checker_control", true, null ) );
			}
			catch ( Exception error ) { rows.Add( new( name, "synthetic_checker_control", false, error.ToString() ) ); }
		}
	}

	private static GltfGpuPrimitiveSnapshot LiteralRow( int node )
	{
		var bytes = new byte[96 * 64];
		for ( var vertex = 0; vertex < 96; vertex++ )
		{
			for ( var component = 0; component < 4; component++ ) Write( bytes, vertex * 64 + component * 4, 1 );
			Write3( bytes, vertex * 64 + 24, UnlitTestReference.EngineNormal( vertex ) );
			Write3( bytes, vertex * 64 + 36, UnlitTestReference.EnginePosition( node, vertex ) );
		}
		return new() { Node = node, Mesh = node, Primitive = 0, VertexCount = 96, VertexStride = 64, Bytes = bytes,
			Fields = new[] { new GltfGpuField( "COLOR", 0, 0, 4 ), new( "TEXCOORD", 0, 16, 2 ), new( "NORMAL", 0, 24, 3 ), new( "POSITION", 0, 36, 3 ), new( "TANGENT", 0, 48, 4 ) },
			SourceVertices = Enumerable.Range( 0, 96 ).Select( value => (uint)value ).ToArray(), Indices = (uint[])UnlitTestReference.Indices.Clone() };
	}
	private static void Write( byte[] bytes, int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset, 4 ), value );
	private static void Write3( byte[] bytes, int offset, Vector3 value ) { Write( bytes, offset, value.X ); Write( bytes, offset + 4, value.Y ); Write( bytes, offset + 8, value.Z ); }
}
