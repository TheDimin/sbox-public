using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

/// <summary>
/// Independent double affine frame oracle for authored POSITION/NORMAL/TANGENT.
/// It uses neither producer evaluators nor System.Numerics matrix operations.
/// Singular transforms have positions but no uniquely specified normal frame.
/// </summary>
public static class SimpleInstancingAffineReference
{
	public const int ExpectedCases = 12;
	public sealed record Frame( double[] P, double[] N, double[] T, double W, bool NormalDefined, int DeterminantSign, string UndefinedReason );
	private const double InchesPerMeter = 1 / .0254;

	/// <summary>
	/// Row-vector order is instanceLocal * nodeWorld * sourceBasisAndUnits * placement.
	/// N and T are null, and W is NaN, when the frame is explicitly undefined.
	/// </summary>
	public static Frame Transform( double[] p, double[] n, double[] t, double handedness, Matrix4x4 instanceLocal, Matrix4x4 nodeWorld, Matrix4x4 placement )
	{
		ValidateVector( p, nameof( p ) ); ValidateVector( n, nameof( n ) ); ValidateVector( t, nameof( t ) );
		if ( handedness is not (1 or -1) ) throw new ArgumentException( "Authored tangent handedness must be +1 or -1.", nameof( handedness ) );
		var basis = new double[,] { { 0, InchesPerMeter, 0, 0 }, { 0, 0, InchesPerMeter, 0 }, { InchesPerMeter, 0, 0, 0 }, { 0, 0, 0, 1 } };
		var world = Multiply( Multiply( Multiply( ReadAffine( instanceLocal ), ReadAffine( nodeWorld ) ), basis ), ReadAffine( placement ) );
		var position = new double[3];
		for ( var c = 0; c < 3; c++ ) position[c] = p[0] * world[0, c] + p[1] * world[1, c] + p[2] * world[2, c] + world[3, c];
		if ( position.Any( x => !double.IsFinite( x ) ) ) throw new InvalidOperationException( "Expected position exceeds finite double arithmetic." );

		var linear = new double[3, 3]; var scaled = new double[3, 3]; var rowScales = new double[3];
		for ( var r = 0; r < 3; r++ )
		{
			for ( var c = 0; c < 3; c++ ) { linear[r, c] = world[r, c]; rowScales[r] = Math.Max( rowScales[r], Math.Abs( linear[r, c] ) ); }
			if ( rowScales[r] == 0 ) return Undefined( position, 0, "Singular affine linear transform: a row has zero scale." );
			for ( var c = 0; c < 3; c++ ) scaled[r, c] = linear[r, c] / rowScales[r];
		}
		// Row scales are strictly positive, so scaling cannot change orientation.
		// Pivot signs avoid multiplying three tiny determinants or using a fixed cutoff.
		var sign = Orientation( scaled );
		if ( sign == 0 ) return Undefined( position, 0, "Singular affine linear transform: no unique inverse-transpose normal frame." );

		var sourceNormal = Unit( n );
		if ( sourceNormal is null ) return Undefined( position, sign, "Authored normal has no direction." );
		var weightedNormal = new double[3];
		for ( var r = 0; r < 3; r++ ) weightedNormal[r] = sourceNormal[r] / rowScales[r];
		weightedNormal = Unit( weightedNormal );
		if ( weightedNormal is null ) return Undefined( position, sign, "Scaled normal has no representable direction." );
		var cofactor = Cofactors( scaled );
		var normal = Forward( weightedNormal, cofactor );
		for ( var c = 0; c < 3; c++ ) normal[c] *= sign;
		normal = Unit( normal );
		if ( normal is null ) return Undefined( position, sign, "Cofactor normal has no representable direction." );

		var sourceTangent = Unit( t );
		if ( sourceTangent is null ) return Undefined( position, sign, "Authored tangent has no direction." );
		var tangent = Unit( Forward( sourceTangent, linear ) );
		if ( tangent is null ) return Undefined( position, sign, "Forward tangent has no direction." );
		var projection = Dot( tangent, normal );
		for ( var c = 0; c < 3; c++ ) tangent[c] -= projection * normal[c];
		tangent = Unit( tangent );
		if ( tangent is null ) return Undefined( position, sign, "Tangent is parallel to the transformed normal." );
		return new( position, normal, tangent, handedness * sign, true, sign, null );
	}

	/// <summary>Oracle-only controls. These results are not producer or GPU evidence.</summary>
	public static SimpleInstancingCoreTests.Result[] RunOracleControls()
	{
		var rows = new List<SimpleInstancingCoreTests.Result>(); var identity = Matrix4x4.Identity;
		var p = new double[] { 1, 2, 3 }; var x = new double[] { 1, 0, 0 }; var y = new double[] { 0, 1, 0 }; var z = new double[] { 0, 0, 1 };
		Run( "identity_performs_source_basis_and_meter_conversion_once", () =>
		{
			var frame = Transform( p, z, x, 1, identity, identity, identity );
			Vector( new[] { 3 * InchesPerMeter, InchesPerMeter, 2 * InchesPerMeter }, frame.P, 1e-11 ); Vector( x, frame.N ); Vector( y, frame.T ); Defined( frame, 1, 1 );
		} );
		var shear = new Matrix4x4( 1, 2, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 );
		Run( "shear_uses_closed_form_inverse_transpose_and_forward_tangent", () =>
		{
			var frame = Transform( p, x, y, 1, identity, identity, shear ); var root = Math.Sqrt( 5 );
			Vector( new[] { 3 * InchesPerMeter, 7 * InchesPerMeter, 2 * InchesPerMeter }, frame.P, 1e-11 ); Vector( new[] { -2 / root, 1 / root, 0 }, frame.N ); Vector( z, frame.T ); Defined( frame, 1, 1 );
		} );
		Run( "reflection_flips_frame_orientation_and_handedness", () =>
		{
			var reflection = Diagonal( -1, 1, 1 ); var frame = Transform( p, z, x, 1, identity, identity, reflection );
			Vector( new[] { -3 * InchesPerMeter, InchesPerMeter, 2 * InchesPerMeter }, frame.P, 1e-11 ); Vector( new double[] { -1, 0, 0 }, frame.N ); Vector( y, frame.T ); Defined( frame, -1, -1 );
		} );
		Run( "authored_negative_handedness_multiplies_reflection_sign", () =>
		{
			Defined( Transform( p, z, x, -1, identity, identity, identity ), 1, -1 ); Defined( Transform( p, z, x, -1, identity, identity, Diagonal( -1, 1, 1 ) ), -1, 1 );
		} );
		Run( "tiny_uniform_1e_minus30_preserves_a_defined_frame", () =>
		{
			var frame = Transform( p, z, x, 1, identity, identity, Diagonal( 1e-30f, 1e-30f, 1e-30f ) );
			Vector( x, frame.N ); Vector( y, frame.T ); Defined( frame, 1, 1 ); Require( frame.P.All( value => value != 0 && double.IsFinite( value ) ), "Tiny positions were clamped or underflowed." );
		} );
		Run( "tiny_anisotropic_inverse_transpose_remains_defined", () =>
		{
			var b = (double)2e-30f; var c = (double)3e-30f; var length = Math.Sqrt( b * b + c * c );
			var frame = Transform( p, new double[] { 1, 1, 0 }, new double[] { 1, -1, 0 }, 1, identity, identity, Diagonal( 1e-30f, 2e-30f, 3e-30f ) );
			Vector( new[] { 0, c / length, b / length }, frame.N ); Vector( new[] { 0, b / length, -c / length }, frame.T ); Defined( frame, 1, 1 );
		} );
		Run( "tiny_reflected_anisotropic_transform_keeps_negative_orientation", () =>
		{
			var frame = Transform( p, z, x, 1, identity, identity, Diagonal( -1e-30f, 2e-30f, 3e-30f ) );
			Vector( new double[] { -1, 0, 0 }, frame.N ); Vector( y, frame.T ); Defined( frame, -1, -1 );
		} );
		Run( "zero_linear_transform_has_position_and_explicitly_undefined_frame", () =>
		{
			var zero = new Matrix4x4( 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7, 11, 13, 1 ); var frame = Transform( p, z, x, 1, identity, identity, zero );
			Vector( new double[] { 7, 11, 13 }, frame.P ); UndefinedFrame( frame );
		} );
		Run( "rank_two_transform_does_not_invent_a_unique_normal_frame", () => UndefinedFrame( Transform( p, z, x, 1, identity, identity, Diagonal( 1, 0, 1 ) ) ) );
		Run( "noncommuting_instance_node_basis_placement_order_is_literal", () =>
		{
			var local = new Matrix4x4( 2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 4, 0, 5, 7, 11, 1 );
			var node = new Matrix4x4( 1, 2, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 13, 17, 19, 1 );
			var placement = new Matrix4x4( 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 23, 29, 31, 1 );
			var frame = Transform( p, z, x, 1, local, node, placement ); Vector( new[] { 42 * InchesPerMeter + 23, 20 * InchesPerMeter + 29, 44 * InchesPerMeter + 31 }, frame.P, 1e-10 ); Defined( frame, 1, 1 );
		} );
		Run( "shear_control_distinguishes_wrong_forward_normal", () =>
		{
			var frame = Transform( p, x, y, 1, identity, identity, shear ); Require( Difference( frame.N, y ) > .5, "Fixture cannot reject forward-linear normals under shear." );
		} );
		Run( "basis_control_distinguishes_a_second_axis_permutation", () =>
		{
			var frame = Transform( p, z, x, 1, identity, identity, identity ); var wrong = new[] { frame.P[2], frame.P[0], frame.P[1] }; Require( Difference( frame.P, wrong ) > InchesPerMeter, "Fixture cannot reject double basis conversion." );
		} );
		Require( rows.Count == ExpectedCases, "Affine oracle inventory changed." ); return rows.ToArray();
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "independent_double_affine_oracle_only", true, null ) ); } catch ( Exception error ) { rows.Add( new( name, "independent_double_affine_oracle_only", false, error.ToString() ) ); } }
	}

	private static Frame Undefined( double[] position, int sign, string reason ) => new( position, null, null, double.NaN, false, sign, reason );
	private static double[,] ReadAffine( Matrix4x4 matrix )
	{
		var values = new double[,] { { matrix.M11, matrix.M12, matrix.M13, matrix.M14 }, { matrix.M21, matrix.M22, matrix.M23, matrix.M24 }, { matrix.M31, matrix.M32, matrix.M33, matrix.M34 }, { matrix.M41, matrix.M42, matrix.M43, matrix.M44 } };
		foreach ( var value in values ) if ( !double.IsFinite( value ) ) throw new ArgumentException( "Oracle input matrix must be finite." );
		if ( matrix.M14 != 0 || matrix.M24 != 0 || matrix.M34 != 0 || matrix.M44 != 1 ) throw new ArgumentException( "Oracle input matrix must be affine." );
		return values;
	}
	private static double[,] Multiply( double[,] a, double[,] b )
	{
		var result = new double[4, 4]; for ( var r = 0; r < 4; r++ ) for ( var c = 0; c < 4; c++ ) for ( var i = 0; i < 4; i++ ) result[r, c] += a[r, i] * b[i, c]; return result;
	}
	private static double[,] Cofactors( double[,] matrix )
	{
		var result = new double[3, 3];
		for ( var r = 0; r < 3; r++ ) for ( var c = 0; c < 3; c++ )
		{
			var a = (r + 1) % 3; var b = (r + 2) % 3; var x = (c + 1) % 3; var y = (c + 2) % 3;
			// Cyclic minor ordering incorporates the cofactor's alternating sign.
			result[r, c] = matrix[a, x] * matrix[b, y] - matrix[a, y] * matrix[b, x];
		}
		return result;
	}
	private static int Orientation( double[,] matrix )
	{
		var work = (double[,])matrix.Clone(); var sign = 1;
		for ( var c = 0; c < 3; c++ )
		{
			var pivot = c; for ( var r = c + 1; r < 3; r++ ) if ( Math.Abs( work[r, c] ) > Math.Abs( work[pivot, c] ) ) pivot = r;
			if ( work[pivot, c] == 0 ) return 0;
			if ( pivot != c ) { for ( var i = c; i < 3; i++ ) (work[c, i], work[pivot, i]) = (work[pivot, i], work[c, i]); sign = -sign; }
			if ( work[c, c] < 0 ) sign = -sign;
			for ( var r = c + 1; r < 3; r++ ) { var factor = work[r, c] / work[c, c]; for ( var i = c + 1; i < 3; i++ ) work[r, i] -= factor * work[c, i]; }
		}
		return sign;
	}
	private static double[] Forward( double[] value, double[,] matrix )
	{
		var result = new double[3]; for ( var c = 0; c < 3; c++ ) for ( var r = 0; r < 3; r++ ) result[c] += value[r] * matrix[r, c]; return result;
	}
	private static double[] Unit( double[] value )
	{
		var maximum = value.Max( x => Math.Abs( x ) ); if ( maximum == 0 || !double.IsFinite( maximum ) ) return null;
		var result = value.Select( x => x / maximum ).ToArray(); var length = Math.Sqrt( Dot( result, result ) ); return result.Select( x => x / length ).ToArray();
	}
	private static double Dot( double[] a, double[] b ) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
	private static Matrix4x4 Diagonal( float x, float y, float z ) => new( x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1 );
	private static void ValidateVector( double[] value, string name ) { if ( value is null || value.Length != 3 || value.Any( x => !double.IsFinite( x ) ) ) throw new ArgumentException( "Expected three finite components.", name ); }
	private static void Defined( Frame frame, int sign, double handedness )
	{
		Require( frame.NormalDefined && frame.UndefinedReason is null && frame.DeterminantSign == sign && frame.W == handedness, "Expected a defined frame and exact handedness." );
		Require( frame.N is not null && frame.T is not null && Math.Abs( Dot( frame.N, frame.N ) - 1 ) < 1e-13 && Math.Abs( Dot( frame.T, frame.T ) - 1 ) < 1e-13 && Math.Abs( Dot( frame.N, frame.T ) ) < 1e-13, "Expected an orthonormal frame." );
	}
	private static void UndefinedFrame( Frame frame ) => Require( !frame.NormalDefined && frame.DeterminantSign == 0 && frame.N is null && frame.T is null && double.IsNaN( frame.W ) && !string.IsNullOrWhiteSpace( frame.UndefinedReason ) && frame.P.All( double.IsFinite ), "Singular normal frame must be explicitly undefined while positions stay finite." );
	private static double Difference( double[] a, double[] b ) => a.Zip( b, (x, y) => Math.Abs( x - y ) ).Max();
	private static void Vector( double[] expected, double[] actual, double tolerance = 1e-14 ) => Require( actual is not null && actual.All( double.IsFinite ) && Difference( expected, actual ) <= tolerance, "Independent affine vector differs from literal expectation." );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
