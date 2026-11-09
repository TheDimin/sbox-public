using System;
using System.Linq;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;

/// <summary>
/// Independent, literal seam and UV oracle. A future adapter supplies one
/// FLOAT4 tangent per triangle corner in source-object coordinates.
/// This is a CPU contract, not evidence of GPU upload or rendered pixels.
/// </summary>
internal static class GeneratedTangentOracleTests
{
	internal sealed record Input( NVector3[] Positions, NVector3[] Normals, NVector2[] Uvs, uint[] Indices );
	public static int Run( Func<Input, NVector4[]> generate )
	{
		ArgumentNullException.ThrowIfNull( generate );
		var input = MirroredSeam();
		var positions = (NVector3[])input.Positions.Clone(); var normals = (NVector3[])input.Normals.Clone();
		var uvs = (NVector2[])input.Uvs.Clone(); var indices = (uint[])input.Indices.Clone();
		var actual = generate( input );
		CheckMirroredSeam( actual );
		Require( input.Positions.SequenceEqual( positions ) && input.Normals.SequenceEqual( normals ) && input.Uvs.SequenceEqual( uvs ) && input.Indices.SequenceEqual( indices ), "Generation mutated source data." );

		// The same geometry with standard rather than mirrored UVs has +X/+1
		// throughout; this catches a fixed or ignored normal-map UV selection.
		var standard = MirroredSeam(); standard.Uvs[3] = new( -1, 0 );
		CheckConstant( generate( standard ), new( 1, 0, 0, -1 ), "Selected UV set" );

		// Expanded source vertices must give the same corner frames as indexed
		// input, including distinct results at formerly shared seam vertices.
		var expanded = Expand( MirroredSeam() );
		CheckMirroredSeam( generate( expanded ) );

		// Degenerate UVs are legal input for the generator; the exact fallback is
		// library-defined, but it must not yield NaN or a malformed output.
		var degenerate = MirroredSeam(); Array.Fill( degenerate.Uvs, NVector2.Zero );
		var fallback = generate( degenerate );
		Require( fallback is { Length: 6 } && fallback.All( Finite ), "Degenerate UVs produced invalid corner output." );

		// Negative controls establish that this oracle detects the common bug of
		// overwriting shared source vertices as well as reversed handedness.
		var overwritten = new[] { actual[3], actual[1], actual[4], actual[3], actual[4], actual[5] };
		Require( Rejects( () => CheckMirroredSeam( overwritten ) ), "Oracle missed an indexed seam overwrite." );
		var reversedSign = actual.Select( x => new NVector4( x.X, x.Y, x.Z, -x.W ) ).ToArray();
		Require( Rejects( () => CheckMirroredSeam( reversedSign ) ), "Oracle missed a reversed tangent sign." );
		return 7;
	}

	private static Input MirroredSeam() => new(
		[new( 0, 0, 0 ), new( 1, 0, 0 ), new( 0, 1, 0 ), new( -1, 0, 0 )],
		[NVector3.UnitZ, NVector3.UnitZ, NVector3.UnitZ, NVector3.UnitZ],
		[new( 0, 0 ), new( 1, 0 ), new( 0, 1 ), new( 1, 0 )], [0, 1, 2, 0, 2, 3] );
	private static Input Expand( Input input ) => new( input.Indices.Select( i => input.Positions[(int)i] ).ToArray(),
		input.Indices.Select( i => input.Normals[(int)i] ).ToArray(), input.Indices.Select( i => input.Uvs[(int)i] ).ToArray(), [0, 1, 2, 3, 4, 5] );
	private static void CheckMirroredSeam( NVector4[] actual )
	{
		Require( actual is { Length: 6 }, "Expected six corner frames." );
		for ( var i = 0; i < 6; i++ ) Near( actual[i], i < 3 ? new( 1, 0, 0, -1 ) : new( -1, 0, 0, 1 ), "Mirrored seam corner " + i );
	}
	private static void CheckConstant( NVector4[] actual, NVector4 expected, string label )
	{
		Require( actual is { Length: 6 }, label + " output count" );
		foreach ( var value in actual ) Near( value, expected, label );
	}
	private static void Near( NVector4 value, NVector4 expected, string label ) => Require( Finite( value ) && NVector4.Distance( value, expected ) < .00001f, label + ": " + value + " expected " + expected );
	private static bool Finite( NVector4 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z ) && float.IsFinite( value.W );
	private static bool Rejects( Action action ) { try { action(); return false; } catch ( InvalidOperationException ) { return true; } }
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
