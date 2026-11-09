using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Tests;

/// <summary>
/// Locate intermediate rounding differences without changing the frozen oracle,
/// producer or acceptance thresholds. Reflection reads this model's independent
/// source decoder; producer accessors never calculate expected channel values.
/// </summary>
public static class FoxChannelArithmeticAudit
{
	public static object[] Run( FoxReference reference )
	{
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
		var type = typeof( FoxReference );
		var source = ((JsonDocument)type.GetField( "owner", flags ).GetValue( reference )).RootElement;
		var decode = type.GetMethod( "Accessor", flags ); var sample = type.GetMethod( "Sample", flags );
		float[] Read( int index ) => (float[])decode.Invoke( reference, [index] );
		var rows = new List<object>();
		foreach ( var request in new[] { (Clip: 0, Time: .8541666f), (Clip: 1, Time: .020833334f), (Clip: 2, Time: .22916666f), (Clip: 2, Time: .0625f) } )
		{
			var animation = source.GetProperty( "animations" )[request.Clip]; var samplers = animation.GetProperty( "samplers" );
			var channelIndex = 0;
			foreach ( var channel in animation.GetProperty( "channels" ).EnumerateArray() )
			{
				var target = channel.GetProperty( "target" ); var path = target.GetProperty( "path" ).GetString();
				var rotation = path == "rotation"; var components = rotation ? 4 : 3;
				var sampler = samplers[channel.GetProperty( "sampler" ).GetInt32()];
				var times = Read( sampler.GetProperty( "input" ).GetInt32() ); var values = Read( sampler.GetProperty( "output" ).GetInt32() );
				var expected = (float[])sample.Invoke( null, [times, values, components, request.Time, rotation] );
				var actual = GltfAnimationSampler.Sample( times, values, components, "LINEAR", request.Time, rotation );
				var errors = actual.Zip( expected, (a, e) => Math.Abs( (double)a - e ) ).ToArray();
				double? radians = null;
				if ( rotation )
				{
					var lengthA = Math.Sqrt( actual.Sum( x => (double)x * x ) ); var lengthE = Math.Sqrt( expected.Sum( x => (double)x * x ) );
					var distance = Math.Sqrt( actual.Zip( expected, (a, e) => Math.Pow( a / lengthA - e / lengthE, 2 ) ).Sum() );
					var antipodalDistance = Math.Sqrt( actual.Zip( expected, (a, e) => Math.Pow( a / lengthA + e / lengthE, 2 ) ).Sum() );
					// Quaternion chord length yields the physical rotation angle and
					// remains informative when dot-product acos rounds to zero.
					radians = 4 * Math.Asin( Math.Clamp( Math.Min( distance, antipodalDistance ) * .5, 0, 1 ) );
				}
				rows.Add( new { request.Clip, request.Time, channel = channelIndex++, node = target.GetProperty( "node" ).GetInt32(), path,
					exact_channel_key = Array.BinarySearch( times, request.Time ) >= 0, actual, expected,
					actual_bits = actual.Select( BitConverter.SingleToUInt32Bits ).ToArray(), expected_bits = expected.Select( BitConverter.SingleToUInt32Bits ).ToArray(),
					maximum_component_error = errors.Max(), rotation_angle_radians = radians,
					scope = "Intermediate measurements only; original Fox world/vertex gates remain unchanged." } );
			}
		}
		return rows.ToArray();
	}
}
