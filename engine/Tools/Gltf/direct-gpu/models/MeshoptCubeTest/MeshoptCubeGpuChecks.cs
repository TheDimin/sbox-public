using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;

using static MeshoptCubeReference;

/// <summary>Checks all output vertices and actual bound GPU indices against the independent fallback oracle.</summary>
internal static class MeshoptCubeGpuChecks
{
	public const double PositionToleranceInches = .0002;
	public const double NormalTolerance = .000002;
	public const double ColorUvTolerance = .0000002;
	public static object AssertReadback( MeshoptCubeReference reference, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, float? time,
		double translateX = 0, double translateY = 0, double translateZ = 0 )
	{
		Require( snapshots.Count == PrimitiveCount && snapshots.Select( x => x.Node ).Distinct().Count() == NodeCount, "GPU snapshot must contain all 35 distinct authored instances." );
		var checkedVertices = 0; var checkedIndices = 0;
		foreach ( var snapshot in snapshots )
		{
			Require( snapshot.Node is >= 0 and < NodeCount && snapshot.Mesh == snapshot.Node && snapshot.Primitive == 0, "Unexpected primitive identity." );
			var count = reference.Accessor( reference.AccessorFor( snapshot.Mesh, "POSITION" ) ).GetProperty( "count" ).GetInt32();
			Require( snapshot.VertexCount == count && snapshot.VertexStride > 0 && snapshot.Bytes.Length == count * snapshot.VertexStride, "GPU output allocation/declaration length changed." );
			// This is CPU correspondence metadata, not a GPU readback of the map.
			Require( snapshot.SourceVertices.SequenceEqual( Enumerable.Range( 0, count ).Select( x => (uint)x ) ), "Unexpected authored-normal expansion/reordering." );
			Require( snapshot.IndexDataOrigin == "gpu-readback", "CPU index submission metadata cannot establish actual bound GPU topology." );
			Require( SameOrientedTriangles( snapshot.Indices, reference.Indices( snapshot.Mesh ) ), $"Actual bound GPU oriented triangles differ from fallback at node {snapshot.Node}: actual [{string.Join( ",", snapshot.Indices )}], expected [{string.Join( ",", reference.Indices( snapshot.Mesh ) )}]." );
			checkedIndices += snapshot.Indices.Length;
			var position = Field( "POSITION", 3 ); var normal = Field( "NORMAL", 3 ); var color = Field( "COLOR", 4 ); var uv = Field( "TEXCOORD", 2 ); var tangent = Field( "TANGENT", 4 );
			for ( var vertex = 0; vertex < count; vertex++ )
			{
				var expectedPosition = reference.Engine( snapshot.Node, vertex, time ); expectedPosition[0] += translateX; expectedPosition[1] += translateY; expectedPosition[2] += translateZ;
				var expectedNormal = reference.Engine( snapshot.Node, vertex, time, true );
				Check( position, vertex, expectedPosition, PositionToleranceInches ); Check( normal, vertex, expectedNormal, NormalTolerance );
				var attributes = reference.Primitive( snapshot.Mesh ).GetProperty( "attributes" );
				var expectedColor = attributes.TryGetProperty( "COLOR_0", out var ca ) ? reference.Value( ca.GetInt32(), vertex ) : new double[] { 1, 1, 1, 1 };
				var expectedUv = attributes.TryGetProperty( "TEXCOORD_0", out var ua ) ? reference.Value( ua.GetInt32(), vertex ) : new double[] { 0, 0 };
				Check( color, vertex, expectedColor, ColorUvTolerance ); Check( uv, vertex, expectedUv, ColorUvTolerance );
				var tv = Read( tangent, vertex ); var nv = Read( normal, vertex );
				Close( 1, Math.Sqrt( tv.Take( 3 ).Sum( x => x * x ) ), 3e-6, $"Generated tangent length at {snapshot.Node}/{vertex}" ); Close( 0, tv.Take( 3 ).Zip( nv, (a, b) => a * b ).Sum(), 3e-6, $"Generated tangent orthogonality at {snapshot.Node}/{vertex}" );
				Close( 1, tv[3], 0, "Generated tangent handedness" );
				checkedVertices++;
			}
			GltfGpuField Field( string semantic, int components )
			{
				var f = snapshot.Fields.Single( x => x.Semantic == semantic && x.SemanticIndex == 0 ); Require( f.ComponentType == 5126 && f.Components == components && f.Offset >= 0 && f.Offset + components * 4 <= snapshot.VertexStride, "Invalid runtime output field: " + semantic ); return f;
			}
			double[] Read( GltfGpuField field, int vertex ) => Enumerable.Range( 0, field.Components ).Select( c => (double)BinaryPrimitives.ReadSingleLittleEndian( snapshot.Bytes.AsSpan( vertex * snapshot.VertexStride + field.Offset + c * 4 ) ) ).ToArray();
			void Check( GltfGpuField field, int vertex, double[] expected, double tolerance )
			{
				var actual = Read( field, vertex ); for ( var c = 0; c < expected.Length; c++ ) Close( expected[c], actual[c], tolerance, $"GPU {snapshot.Node}/{vertex}/{field.Semantic}/{c}" );
			}
		}
		Require( checkedVertices == TotalVertices && checkedIndices == TotalIndices, "GPU full-scene output totals changed." );
		return new { primitives = snapshots.Count, checkedVertices, checkedIndices, sourceTime = time, PositionToleranceInches, NormalTolerance, ColorUvTolerance, materialImageAcceptance = "separate pending gate" };
	}

	// The retained actual GPU diagnostic measures 72 TRIANGLES codec cyclic
	// rotations and zero topology/winding changes. Preserve each triangle ordinal
	// and all source-vertex/attribute correspondence; never sort indices or faces.
	private static bool SameOrientedTriangles( uint[] actual, uint[] expected )
	{
		if ( actual.Length != expected.Length || actual.Length % 3 != 0 ) return false;
		for ( var i = 0; i < actual.Length; i += 3 )
		{
			var a = actual[i]; var b = actual[i + 1]; var c = actual[i + 2];
			var x = expected[i]; var y = expected[i + 1]; var z = expected[i + 2];
			if ( !((a == x && b == y && c == z) || (a == y && b == z && c == x) || (a == z && b == x && c == y)) ) return false;
		}
		return true;
	}

	/// <summary>Synthetic fault controls exercise checker sensitivity; they are never actual GPU evidence.</summary>
	public static MeshoptCubeCoreTests.Result[] RunVerifierControls( string checkoutRoot )
	{
		var reference = new MeshoptCubeReference( checkoutRoot ); var rows = new List<MeshoptCubeCoreTests.Result>();
		var fields = new[] { new GltfGpuField( "NORMAL", 0, 0, 3 ), new GltfGpuField( "TANGENT", 0, 12, 4 ), new GltfGpuField( "POSITION", 0, 32, 3 ), new GltfGpuField( "COLOR", 0, 48, 4 ), new GltfGpuField( "TEXCOORD", 0, 68, 2 ) };
		Run( "synthetic_accepts_reordered_padded_runtime_layout", () => AssertReadback( reference, Make(), .375f ) );
		Run( "rejects_wrong_position_field_offset", () => Reject( () => AssertReadback( reference, Make( (node, data, layout, indices) => { if ( node == 0 ) layout[2] = layout[2] with { Offset = 28 }; } ), .375f ) ) );
		Run( "rejects_wrong_axis_or_unit_conversion", () => Reject( () => AssertReadback( reference, Make( (node, data, layout, indices) => { if ( node == 10 ) BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 32 ), 0 ); } ), .375f ) ) );
		Run( "rejects_rotation_phase_frozen_at_rest", () => Reject( () => AssertReadback( reference, Make(), 0 ) ) );
		Run( "rejects_vertex_color_missing_normalization", () => Reject( () => AssertReadback( reference, Make( (node, data, layout, indices) => { if ( node == 10 ) BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 52 ), 128 ); } ), .375f ) ) );
		Run( "rejects_label_v_flip", () => Reject( () => AssertReadback( reference, Make( (node, data, layout, indices) => { if ( node == 0 ) BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 72 ), 0 ); } ), .375f ) ) );
		Run( "rejects_actual_index_winding_change", () => Reject( () => AssertReadback( reference, Make( (node, data, layout, indices) => { if ( node == 0 ) (indices[1], indices[2]) = (indices[2], indices[1]); } ), .375f ) ) );
		Run( "rejects_cpu_submission_topology_origin", () => Reject( () => AssertReadback( reference, Make( origin: "cpu-submission" ), .375f ) ) );
		Run( "rejects_double_instance_placement", () => Reject( () => AssertReadback( reference, Make(), .375f, 3, -5, 7 ) ) );
		return rows.ToArray();

		GltfGpuPrimitiveSnapshot[] Make( Action<int, byte[], GltfGpuField[], uint[]> mutate = null, string origin = "gpu-readback" )
		{
			return Enumerable.Range( 0, NodeCount ).Select( node =>
			{
				var count = reference.Accessor( reference.AccessorFor( node, "POSITION" ) ).GetProperty( "count" ).GetInt32(); var bytes = new byte[count * 80]; var layout = fields.ToArray(); var indices = reference.Indices( node );
				for ( var vertex = 0; vertex < count; vertex++ )
				{
					var n = reference.Engine( node, vertex, .375f, true ); var p = reference.Engine( node, vertex, .375f );
					var axis = Math.Abs( n[0] ) < .7 ? new double[] { 1, 0, 0 } : new double[] { 0, 1, 0 };
					var t = new[] { n[1] * axis[2] - n[2] * axis[1], n[2] * axis[0] - n[0] * axis[2], n[0] * axis[1] - n[1] * axis[0] }; var length = Math.Sqrt( t.Sum( x => x * x ) ); for ( var c = 0; c < 3; c++ ) t[c] /= length;
					var attributes = reference.Primitive( node ).GetProperty( "attributes" );
					var color = attributes.TryGetProperty( "COLOR_0", out var ca ) ? reference.Value( ca.GetInt32(), vertex ) : new double[] { 1, 1, 1, 1 };
					var uv = attributes.TryGetProperty( "TEXCOORD_0", out var ua ) ? reference.Value( ua.GetInt32(), vertex ) : new double[] { 0, 0 };
					foreach ( var (offset, values) in new[] { (0, n), (12, t.Append( 1d ).ToArray()), (32, p), (48, color), (68, uv) } )
						for ( var c = 0; c < values.Length; c++ ) BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( vertex * 80 + offset + c * 4 ), (float)values[c] );
				}
				mutate?.Invoke( node, bytes, layout, indices );
				return new GltfGpuPrimitiveSnapshot { Node = node, Mesh = node, Primitive = 0, VertexCount = count, VertexStride = 80, Fields = layout, Bytes = bytes, SourceVertices = Enumerable.Range( 0, count ).Select( x => (uint)x ).ToArray(), Indices = indices, IndexDataOrigin = origin };
			} ).ToArray();
		}
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "synthetic verifier control only", true, null ) ); } catch ( Exception e ) { rows.Add( new( name, "synthetic verifier control only", false, e.ToString() ) ); } }
		void Reject( Action body ) { try { body(); } catch ( InvalidOperationException ) { return; } throw new Exception( "Faulty snapshot was accepted." ); }
	}
}
