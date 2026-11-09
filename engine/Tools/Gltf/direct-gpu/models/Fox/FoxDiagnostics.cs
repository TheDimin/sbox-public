using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.Tests;

using NM = System.Numerics.Matrix4x4;
using NV3 = System.Numerics.Vector3;

/// <summary>Measurements only. No producer changes or tolerance adjustment.</summary>
public static class FoxDiagnostics
{
	public sealed record CameraFit( global::Vector3 Position, global::Vector3 Target, float OrthographicHeight, float Near, float Far,
		global::Vector3 SecondInstanceOffset, global::Vector3 DirectionalLightForward,
		float[] MinimumInches, float[] MaximumInches, float[] ExtentInches, int PoseCount, string Scope );
	public static CameraFit FitSmokeCamera( FoxReference reference )
	{
		var samples = new List<(int? Clip, float Time)> { (null, 0), (2, reference.Duration( 2 ) * .25f) };
		for ( var clip = 0; clip < reference.ClipCount; clip++ ) { samples.Add( (clip, 0) ); samples.Add( (clip, reference.Duration( clip ) * .5f) ); }
		var positions = new List<NV3>();
		foreach ( var sample in samples ) positions.AddRange( reference.ToEngine( reference.Evaluate( sample.Clip, sample.Time ), NM.Identity ).Positions );
		var aMinimum = positions.Aggregate( NV3.Min ); var aMaximum = positions.Aggregate( NV3.Max );
		var offset = new NV3( 0, Math.Max( .01f, (aMaximum.Y - aMinimum.Y) * 1.25f ), 0 );
		positions.AddRange( reference.ToEngine( reference.Evaluate( 2, reference.Duration( 2 ) * .5f ), NM.CreateTranslation( offset ) ).Positions );
		var minimum = positions.Aggregate( NV3.Min ); var maximum = positions.Aggregate( NV3.Max );
		var center = (minimum + maximum) * .5f; var extent = maximum - minimum; var radius = Math.Max( .01f, extent.Length() * .5f );
		var direction = NV3.Normalize( new NV3( 1, 1, .65f ) ); var camera = center + direction * (radius * 3);
		return new CameraFit( V( camera ), V( center ), radius * 2.4f, Math.Max( .01f, radius * .01f ), radius * 8, V( offset ), V( -direction ),
			A( minimum ), A( maximum ), A( extent ), samples.Count + 1,
			"One fixed camera fitted to independent source positions across every smoke pose plus instance B. Source units/geometry are unchanged. Native lighting must cover this measured scale separately." );
	}

	public static object[] AuditArithmetic( GltfDocument document, FoxReference reference )
	{
		var result = new List<object>();
		for ( var clip = 0; clip < reference.ClipCount; clip++ )
		{
			var keys = reference.KeyTimes( clip ); var times = keys.Concat( keys.Zip( keys.Skip( 1 ), (a, b) => (a + b) * .5f ) ).Append( keys[0] - .01f ).Append( keys[^1] + .01f ).Distinct().Order().ToArray();
			object firstMatrix = null, worstMatrix = null, firstVertex = null, worstVertex = null;
			var maximumMatrix = 0d; var maximumVertex = 0d; var maximumComponentUlps = 0L; var count = 0;
			foreach ( var time in times )
			{
				var expected = reference.Evaluate( clip, time ); var actual = document.EvaluatePose( clip, time );
				for ( var node = 0; node < reference.NodeCount; node++ )
				{
					var a = Values( actual.GetWorldTransform( node ) ); var e = Values( expected.Worlds[node] );
					for ( var component = 0; component < 16; component++ )
					{
						var error = Math.Abs( (double)a[component] - e[component] ); var ulps = Ulps( a[component], e[component] );
						var row = new { clip, time, exact_union_key = Array.BinarySearch( keys, time ) >= 0, node, component,
							component_name = "M" + (component / 4 + 1) + (component % 4 + 1), actual = a[component], expected = e[component], absolute_error = error,
							actual_bits = BitConverter.SingleToUInt32Bits( a[component] ), expected_bits = BitConverter.SingleToUInt32Bits( e[component] ), float_ulp_distance = ulps,
							local_float_spacing = Math.Abs( (double)MathF.BitIncrement( e[component] ) - e[component] ),
							relative_to_component = error / Math.Max( 1e-300, Math.Abs( (double)e[component] ) ),
							matrix_maximum_magnitude = e.Max( x => Math.Abs( x ) ), matrix_values_actual = a, matrix_values_expected = e };
						if ( firstMatrix is null && error > 4e-6 ) firstMatrix = row;
						if ( error > maximumMatrix ) { maximumMatrix = error; worstMatrix = row; }
						maximumComponentUlps = Math.Max( maximumComponentUlps, ulps ); count++;
					}
				}
				foreach ( var vertex in new[] { 0, 1, 2, 37, 122, 437, 701, 998, 1220, 1500, 1725, 1726, 1727 } )
				{
					var a = document.EvaluateVertexPosition( reference.MeshNode, 0, vertex, actual ); var e = expected.Positions[vertex];
					var error = Math.Sqrt( Math.Pow( (double)a.X - e.X, 2 ) + Math.Pow( (double)a.Y - e.Y, 2 ) + Math.Pow( (double)a.Z - e.Z, 2 ) );
					var row = new { clip, time, exact_union_key = Array.BinarySearch( keys, time ) >= 0, vertex, actual = A( a ), expected = A( e ),
						absolute_distance_source_units = error, absolute_distance_inches = error / .0254, component_ulps = new[] { Ulps( a.X, e.X ), Ulps( a.Y, e.Y ), Ulps( a.Z, e.Z ) } };
					if ( firstVertex is null && error > 2e-5 ) firstVertex = row;
					if ( error > maximumVertex ) { maximumVertex = error; worstVertex = row; }
				}
			}
			result.Add( new { clip, name = reference.ClipName( clip ), sampled_times = times.Length, matrix_components_compared = count,
				maximum_matrix_error = maximumMatrix, maximum_vertex_error = maximumVertex, maximum_component_ulp_distance = maximumComponentUlps,
				first_matrix_over_original_gate = firstMatrix, worst_matrix_absolute_error = worstMatrix,
				first_vertex_over_original_gate = firstVertex, worst_vertex_distance = worstVertex,
				scope = "Diagnostics using the original frozen independent reference. Fixed original gates retained; this report does not approve rounding bounds or establish a producer defect." } );
		}
		return result.ToArray();
	}

	private static long Ulps( float a, float b )
	{
		long Ordered( float value ) { var bits = BitConverter.SingleToInt32Bits( value ); return bits < 0 ? int.MinValue - (long)bits : bits; }
		if ( a == b ) return 0; return Math.Abs( Ordered( a ) - Ordered( b ) );
	}
	private static float[] Values( NM m ) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
	private static global::Vector3 V( NV3 value ) => new( value.X, value.Y, value.Z );
	private static float[] A( NV3 value ) => new[] { value.X, value.Y, value.Z };
}
