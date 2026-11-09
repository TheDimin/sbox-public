using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Sandbox.Gltf.DirectGpu.Tests;

/// <summary>Independent oracle regressions: choose authored keys before spherical interpolation.</summary>
public static class FoxReferenceSelectionTests
{
	public sealed record Result( string Name, bool Passed, string Error );
	public static Result[] RunAll()
	{
		var result = new List<Result>();
		Run( "quaternion_exact_keys_preserve_authored_float_bits_and_sign", () =>
		{
			foreach ( var time in new[] { 0f, 1f, 2f } ) EqualBits( Values.AsSpan( (int)time * 4, 4 ).ToArray(), Sample( time ), "exact " + time );
		} );
		Run( "quaternion_clamps_preserve_first_and_last_authored_values", () =>
		{
			EqualBits( Values[..4], Sample( -.5f ), "first held" ); EqualBits( Values[8..], Sample( 2.5f ), "last held" );
		} );
		Run( "strict_interior_antipodal_quaternions_choose_shortest_nonzero_arc", () =>
		{
			var value = Invoke( new[] { 0f, 1f }, new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, -1f }, .5f );
			Require( value[0] == 0 && value[1] == 0 && value[2] == 0 && MathF.Abs( value[3] - 1 ) <= 1.1920929e-7f, "Interior sign correction produced the wrong orientation or a zero quaternion." );
		} );
		return result.ToArray();
		void Run( string name, Action test ) { try { test(); result.Add( new Result( name, true, null ) ); } catch ( Exception error ) { result.Add( new Result( name, false, error.ToString() ) ); } }
	}
	// Near-unit serialized float values deliberately differ from the result of
	// normalization. A held key must retain these original values and signs.
	private static readonly float[] Values = { -.7071067f, 0, 0, .7071067f, 0, -.7071067f, 0, -.7071067f, 0, 0, .7071067f, -.7071067f };
	private static float[] Sample( float time ) => Invoke( new[] { 0f, 1f, 2f }, Values, time );
	private static float[] Invoke( float[] times, float[] values, float time ) => (float[])typeof( FoxReference ).GetMethod( "Sample", BindingFlags.Static | BindingFlags.NonPublic ).Invoke( null, new object[] { times, values, 4, time, true } );
	private static void EqualBits( float[] expected, float[] actual, string label )
	{
		Require( actual.Length == expected.Length && expected.Zip( actual ).All( x => BitConverter.SingleToUInt32Bits( x.First ) == BitConverter.SingleToUInt32Bits( x.Second ) ), "Oracle normalized or changed authored quaternion bits at " + label );
	}
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
