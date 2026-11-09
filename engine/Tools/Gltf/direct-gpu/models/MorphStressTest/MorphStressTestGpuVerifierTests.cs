using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using Vector3 = System.Numerics.Vector3;
using static MorphStressTestReference;

/// <summary>Synthetic sensitivity controls only. They never count as actual GPU execution.</summary>
public static class MorphStressTestGpuVerifierTests
{
	public const int ExpectedCases = 10;
	public static MorphStressTestCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var r = new MorphStressTestReference( checkoutRoot ); var rows = new List<MorphStressTestCoreTests.Result>(); var weights = r.SignedWeights;
		Run( "nonstandard_offsets_padding_and_both_primitives_pass", () => Check( Snapshot( r, weights ), true ) );
		Run( "wrong_position_fails", () => { var x = Snapshot( r, weights ); Write( x[1], 0, 16, 999 ); Check( x, false ); } );
		Run( "wrong_normal_fails", () => { var x = Snapshot( r, weights ); Write( x[1], 0, 32, 999 ); Check( x, false ); } );
		Run( "second_uv_set_mutation_fails", () => { var x = Snapshot( r, weights ); Write( x[1], 0, 0, 999 ); Check( x, false ); } );
		Run( "reversed_winding_fails", () => { var x = Snapshot( r, weights ); (x[1].Indices[1], x[1].Indices[2]) = (x[1].Indices[2], x[1].Indices[1]); Check( x, false ); } );
		Run( "cpu_index_origin_rejected", () => { var x = Snapshot( r, weights, "cpu-submission" ); ExpectFailure( () => Check( x, true ) ); } );
		Run( "missing_static_base_primitive_rejected", () => ExpectFailure( () => Check( Snapshot( r, weights ).Skip( 1 ).ToArray(), true ) ) );
		Run( "dropped_eighth_target_fails", () => Check( Snapshot( r, weights.Take( 7 ).Append( 0 ).ToArray() ), false ) );
		Run( "nonfinite_normal_fails", () => { var x = Snapshot( r, weights ); Write( x[1], 0, 32, float.NaN ); Check( x, false ); } );
		Run( "zero_generated_tangent_fails", () => { var x = Snapshot( r, weights ); for ( var c = 0; c < 3; c++ ) Write( x[1], 0, 48 + c * 4, 0 ); Check( x, false ); } );
		Require( rows.Count == ExpectedCases, "Frozen verifier control inventory changed." ); return rows.ToArray();
		void Check( IReadOnlyList<GltfGpuPrimitiveSnapshot> x, bool pass )
		{
			var measurement = MorphStressTestGpuChecks.Measure( x, r, weights, Matrix4x4.Identity );
			if ( pass ) MorphStressTestGpuChecks.RequireWithin( measurement, .0005, 2e-6, 2e-6 );
			else ExpectFailure( () => MorphStressTestGpuChecks.RequireWithin( measurement, .0005, 2e-6, 2e-6 ) );
		}
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "synthetic_checker_only", true, null ) ); } catch ( Exception error ) { rows.Add( new( name, "synthetic_checker_only", false, error.ToString() ) ); } }
	}
	private static void ExpectFailure( Action body ) { try { body(); } catch ( InvalidOperationException ) { return; } throw new InvalidOperationException( "Negative control was accepted." ); }
	private static void Write( GltfGpuPrimitiveSnapshot row, int vertex, int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + offset, 4 ), value );
	private static GltfGpuPrimitiveSnapshot[] Snapshot( MorphStressTestReference r, double[] weights, string origin = "gpu-readback" )
	{
		return Enumerable.Range( 0, 2 ).Select( primitive =>
		{
			var count = r.VertexCount( primitive ); var bytes = new byte[count * 100];
			var row = new GltfGpuPrimitiveSnapshot { Node = 0, Mesh = 0, Primitive = primitive, VertexCount = count, VertexStride = 100, Bytes = bytes,
				Fields = new[] { new GltfGpuField( "TEXCOORD", 1, 0, 2 ), new GltfGpuField( "POSITION", 0, 16, 3 ), new GltfGpuField( "NORMAL", 0, 32, 3 ), new GltfGpuField( "TANGENT", 0, 48, 4 ), new GltfGpuField( "TEXCOORD", 0, 64, 2 ), new GltfGpuField( "COLOR", 0, 76, 4 ) },
				SourceVertices = Enumerable.Range( 0, count ).Select( x => (uint)x ).ToArray(), Indices = r.Indices( primitive ), IndexDataOrigin = origin };
			for ( var v = 0; v < count; v++ )
			{
				var p = r.Engine( primitive, v, weights, Matrix4x4.Identity ); var n = r.Engine( primitive, v, weights, Matrix4x4.Identity, true ) ?? new double[] { 1, 0, 0 };
				for ( var c = 0; c < 3; c++ ) { Write( row, v, 16 + c * 4, (float)p[c] ); Write( row, v, 32 + c * 4, (float)n[c] ); }
				var normal = new Vector3( (float)n[0], (float)n[1], (float)n[2] ); var tangent = Vector3.Normalize( Vector3.Cross( normal, Math.Abs( normal.X ) < .7 ? Vector3.UnitX : Vector3.UnitY ) );
				Write( row, v, 48, tangent.X ); Write( row, v, 52, tangent.Y ); Write( row, v, 56, tangent.Z ); Write( row, v, 60, 1 );
				for ( var set = 0; set < 2; set++ )
				{
					var uv = r.AccessorValues( r.Primitive( primitive ).GetProperty( "attributes" ).GetProperty( "TEXCOORD_" + set ).GetInt32() )[v];
					for ( var c = 0; c < 2; c++ ) Write( row, v, (set == 0 ? 64 : 0) + c * 4, (float)uv[c] );
				}
				BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( v * 100 + 8, 4 ), 0x7fc00000 ); // Untouched padding must be irrelevant.
			}
			return row;
		} ).ToArray();
	}
}
