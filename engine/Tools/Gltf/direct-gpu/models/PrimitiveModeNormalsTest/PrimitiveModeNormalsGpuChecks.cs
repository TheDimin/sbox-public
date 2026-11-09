using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.PrimitiveModeNormalsValidation;

using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector3 = System.Numerics.Vector3;
using static PrimitiveModeNormalsReference;

/// <summary>Completed GPU vertex/index bytes, checked against pinned raw geometry and literal hierarchy.</summary>
public static class PrimitiveModeNormalsGpuChecks
{
	public sealed record Measurement( int Mesh, int Vertices, int Indices, int NormalDirectionsChecked,
		int NormalDirectionsUnspecified, float MaximumPositionErrorInches, float MaximumNormalComponentError, float MaximumColorError, float MaximumUvError );
	public static Measurement[] Measure( PrimitiveModeNormalsReference reference, IReadOnlyList<GltfGpuPrimitiveSnapshot> rows, Matrix4x4 placement )
	{
		Require( rows.Count == 25 && rows.Select( x => x.Mesh ).Order().SequenceEqual( Enumerable.Range( 0, 25 ) ), "All 25 applicable primitive instances must be present exactly once." );
		return rows.OrderBy( x => x.Mesh ).Select( row => MeasureRow( reference, row, placement ) ).ToArray();
	}
	public static Measurement MeasureRow( PrimitiveModeNormalsReference reference, GltfGpuPrimitiveSnapshot row, Matrix4x4 placement )
	{
		var mesh = row.Mesh; Require( mesh is >= 0 and < 25 && row.Node == Node( mesh ) && row.Primitive == 0, "Node/mesh/primitive identity differs." );
		var expanded = reference.ExpandedIndices( mesh ); var expectedMap = Flat( mesh ) ? expanded : Enumerable.Range( 0, Count( mesh ) ).Select( x => (uint)x ).ToArray();
		Require( row.IndexDataOrigin == "gpu-readback", "Bound index-buffer bytes must be actual GPU readback." );
		Require( row.VertexCount == expectedMap.Length && row.SourceVertices.SequenceEqual( expectedMap ), "Output source mapping/count differs; valid primitives cannot be dropped." );
		Require( row.Indices.Length == expanded.Length && row.Indices.All( x => x < row.VertexCount ), "GPU index count/range differs." );
		var expectedIndices = (uint[])expanded.Clone();
		if ( DrawMode( mesh ) == 4 && placement.GetDeterminant() < 0 )
			for ( var i = 0; i < expectedIndices.Length; i += 3 ) (expectedIndices[i + 1], expectedIndices[i + 2]) = (expectedIndices[i + 2], expectedIndices[i + 1]);
		Require( row.Indices.Select( x => row.SourceVertices[x] ).SequenceEqual( expectedIndices ), "GPU point order, line-strip adjacency or triangle winding differs." );
		Require( row.VertexStride > 0 && row.Bytes.Length == checked(row.VertexCount * row.VertexStride), "GPU byte extent invalid." );
		var fields = row.Fields.OrderBy( x => x.Offset ).ToArray();
		foreach ( var f in fields ) Require( f.ComponentType == 5126 && f.Components is >= 1 and <= 4 && f.Offset >= 0 && f.Offset % 4 == 0 && f.Offset + f.Components * 4 <= row.VertexStride, "Invalid runtime field range/representation." );
		for ( var i = 1; i < fields.Length; i++ ) Require( fields[i - 1].Offset + fields[i - 1].Components * 4 <= fields[i].Offset, "Overlapping runtime output fields." );
		var position = Field( "POSITION", 3 ); var normal = Field( "NORMAL", 3 ); var color = Field( "COLOR", 4 ); var uv = Field( "TEXCOORD", 2 );
		var maxPosition = 0f; var maxNormal = 0f; var maxColor = 0f; var maxUv = 0f; var checkedNormals = 0;
		for ( var vertex = 0; vertex < row.VertexCount; vertex++ )
		{
			foreach ( var field in fields ) for ( var c = 0; c < field.Components; c++ ) _ = Read( field, vertex, c );
			var source = (int)row.SourceVertices[vertex]; var p = Read3( position, vertex ); var n = Read3( normal, vertex );
			maxPosition = MathF.Max( maxPosition, ComponentError( p, Vector3.Transform( reference.EnginePosition( mesh, source ), placement ) ) );
			if ( HasNormal( mesh ) || Flat( mesh ) )
			{
				var expectedNormal = HasNormal( mesh ) ? reference.AuthoredEngineNormal( mesh, source ) : reference.FlatEngineNormal( mesh, expanded, vertex );
				maxNormal = MathF.Max( maxNormal, ComponentError( n, TransformNormal( expectedNormal, placement ) ) ); checkedNormals++;
			}
			// Missing NORMAL on points/lines has no unique surface-normal direction.
			// The renderer may store a finite placeholder; its direction is never asserted here.
			for ( var c = 0; c < 4; c++ ) maxColor = MathF.Max( maxColor, MathF.Abs( Read( color, vertex, c ) - reference.Color( mesh, source, c ) ) );
			for ( var c = 0; c < 2; c++ ) maxUv = MathF.Max( maxUv, MathF.Abs( Read( uv, vertex, c ) - reference.Uv( mesh, source, c ) ) );
		}
		return new( mesh, row.VertexCount, row.Indices.Length, checkedNormals, row.VertexCount - checkedNormals, maxPosition, maxNormal, maxColor, maxUv );
		GltfGpuField Field( string semantic, int components ) { var f = fields.Single( x => x.Semantic == semantic && x.SemanticIndex == 0 ); Require( f.Components == components, "Unexpected field dimension: " + semantic ); return f; }
		float Read( GltfGpuField field, int vertex, int component ) { var value = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + component * 4, 4 ) ); Require( float.IsFinite( value ), "Nonfinite GPU output." ); return value; }
		Vector3 Read3( GltfGpuField field, int vertex ) => new( Read( field, vertex, 0 ), Read( field, vertex, 1 ), Read( field, vertex, 2 ) );
	}
	public static void RequireWithin( Measurement row, float positionToleranceInches = .002f, float normalTolerance = .00005f )
	{
		Require( float.IsFinite( positionToleranceInches ) && positionToleranceInches >= 0 && float.IsFinite( normalTolerance ) && normalTolerance >= 0, "Invalid explicit numeric thresholds." );
		Require( row.MaximumPositionErrorInches <= positionToleranceInches, $"Mesh {row.Mesh}: position component error {row.MaximumPositionErrorInches:R} exceeds {positionToleranceInches:R} inches." );
		Require( row.MaximumNormalComponentError <= normalTolerance, $"Mesh {row.Mesh}: defined normal component error {row.MaximumNormalComponentError:R} exceeds {normalTolerance:R}." );
		Require( row.MaximumColorError <= .000001f && row.MaximumUvError == 0, "Normalized byte color or raw/default UV differs." );
	}
	/// <summary>Separate future additive backend gate: CPU command-submission evidence, not a GPU rasterizer query.</summary>
	public static void RequireSubmittedModes( IReadOnlyList<GltfGpuPrimitiveSnapshot> rows, long revision )
	{
		Require( rows.Count == 25, "Submitted-mode gate requires every applicable primitive." );
		foreach ( var row in rows )
		{
			object Value( string property ) => row.GetType().GetProperty( property )?.GetValue( row ) ?? throw new InvalidOperationException( "Pending additive backend diagnostic: " + property );
			Require( Convert.ToInt32( Value( "SourceMode" ) ) == SourceMode( row.Mesh ), "Authored source topology metadata changed." );
			Require( Convert.ToInt64( Value( "SubmittedRevision" ) ) == revision && Convert.ToInt32( Value( "DrawCalls" ) ) > 0, "This primitive did not submit the current revision." );
			var expected = DrawMode( row.Mesh ) == 0 ? "Points" : DrawMode( row.Mesh ) == 1 ? "Lines" : "Triangles";
			Require( Value( "SubmittedPrimitiveType" ).ToString() == expected, "Actual DrawIndexed command parameter differs from intended native primitive type." );
		}
	}
	private static float ComponentError( Vector3 a, Vector3 b ) => MathF.Max( MathF.Abs( a.X - b.X ), MathF.Max( MathF.Abs( a.Y - b.Y ), MathF.Abs( a.Z - b.Z ) ) );
}

/// <summary>Synthetic checker sensitivity controls; these do not constitute GPU execution evidence.</summary>
public static class PrimitiveModeNormalsGpuVerifierTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 13;
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new PrimitiveModeNormalsReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var mesh in new[] { 0, 2, 14, 24 } ) Run( "nonstandard_output_field_order_mesh_" + mesh, mesh, null, false );
		Run( "wrong_position_rejected", 14, r => Write( r.Bytes, 36, 1000 ), true );
		Run( "wrong_authored_normal_rejected", 14, r => Write( r.Bytes, 24, 1000 ), true );
		Run( "wrong_generated_flat_normal_rejected", 2, r => Write( r.Bytes, 24, 1000 ), true );
		Run( "reversed_triangle_winding_rejected", 14, r => (r.Indices[0], r.Indices[1]) = (r.Indices[1], r.Indices[0]), true );
		Run( "wrong_generated_color_rejected", 14, r => Write( r.Bytes, 0, .5f ), true );
		Run( "wrong_uv_view_offset_rejected", 24, r => Write( r.Bytes, 16, 2 ), true );
		Run( "wrong_source_mapping_rejected", 14, r => (r.SourceVertices[0], r.SourceVertices[1]) = (r.SourceVertices[1], r.SourceVertices[0]), true );
		Run( "wrong_line_strip_adjacency_rejected", 1, r => r.Indices[2] = 0, true );
		Run( "undefined_point_normal_direction_accepted", 0, r => { Write( r.Bytes, 24, 8 ); Write( r.Bytes, 28, -3 ); Write( r.Bytes, 32, 7 ); }, false );
		Require( results.Count == ExpectedCases, "Frozen verifier inventory changed." ); return results.ToArray();
		void Run( string name, int mesh, Action<GltfGpuPrimitiveSnapshot> mutate, bool expectedRejection )
		{
			try
			{
				var row = LiteralRow( reference, mesh ); mutate?.Invoke( row ); var rejected = false;
				try { PrimitiveModeNormalsGpuChecks.RequireWithin( PrimitiveModeNormalsGpuChecks.MeasureRow( reference, row, Matrix4x4.Identity ) ); } catch ( InvalidOperationException ) { rejected = true; }
				Require( rejected == expectedRejection, "Checker control had wrong acceptance." ); results.Add( new( name, "synthetic_checker_control", true, null ) );
			}
			catch ( Exception error ) { results.Add( new( name, "synthetic_checker_control", false, error.ToString() ) ); }
		}
	}
	private static GltfGpuPrimitiveSnapshot LiteralRow( PrimitiveModeNormalsReference reference, int mesh )
	{
		var expanded = reference.ExpandedIndices( mesh ); var map = Flat( mesh ) ? expanded : Enumerable.Range( 0, Count( mesh ) ).Select( x => (uint)x ).ToArray(); var bytes = new byte[map.Length * 64];
		for ( var i = 0; i < map.Length; i++ )
		{
			var source = (int)map[i]; var p = reference.EnginePosition( mesh, source ); var n = HasNormal( mesh ) ? reference.AuthoredEngineNormal( mesh, source ) : Flat( mesh ) ? reference.FlatEngineNormal( mesh, expanded, i ) : Vector3.UnitX;
			for ( var c = 0; c < 4; c++ ) Write( bytes, i * 64 + c * 4, reference.Color( mesh, source, c ) );
			for ( var c = 0; c < 2; c++ ) Write( bytes, i * 64 + 16 + c * 4, reference.Uv( mesh, source, c ) );
			Write3( bytes, i * 64 + 24, n ); Write3( bytes, i * 64 + 36, p );
		}
		return new() { Node = Node( mesh ), Mesh = mesh, Primitive = 0, VertexCount = map.Length, VertexStride = 64, Bytes = bytes,
			Fields = new[] { new GltfGpuField( "COLOR", 0, 0, 4 ), new( "TEXCOORD", 0, 16, 2 ), new( "NORMAL", 0, 24, 3 ), new( "POSITION", 0, 36, 3 ), new( "TANGENT", 0, 48, 4 ) },
			SourceVertices = (uint[])map.Clone(), Indices = Flat( mesh ) ? Enumerable.Range( 0, map.Length ).Select( x => (uint)x ).ToArray() : expanded, IndexDataOrigin = "gpu-readback" };
	}
	private static void Write( byte[] bytes, int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset, 4 ), value );
	private static void Write3( byte[] bytes, int offset, Vector3 v ) { Write( bytes, offset, v.X ); Write( bytes, offset + 4, v.Y ); Write( bytes, offset + 8, v.Z ); }
}
